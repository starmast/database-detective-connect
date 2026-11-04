using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using BepInEx.Logging;

namespace DDConnect
{
    /// <summary>
    /// Keeps a copy of the current level's SQLite database at a fixed path.
    /// Refreshes are driven by <see cref="MarkDirty"/> (game commits and level changes)
    /// and debounced with a one-shot timer, so nothing runs while the game is idle.
    /// When table sending is on, the copy is writable and new tables created in it by an
    /// external editor are handed to <see cref="EditorTables"/> before a refresh can overwrite them.
    /// </summary>
    internal static class MirrorService
    {
        private const int BusyTimeoutMs = 2000;
        private const int BusyRetryMs = 1000;
        private const int EditorPollMs = 1000;

        /// <summary>Held while the mirror is rewritten, scanned or read, so those never overlap.</summary>
        public static readonly object Lock = new object();

        private static ManualLogSource log;
        private static string dataDir;
        private static string mirrorPath;
        private static int debounceMs;
        private static bool sendTables;
        private static Func<int> getLevel;
        private static Timer timer;
        private static Timer watchTimer;

        private static int dirty;
        private static int refreshing;
        private static string pendingReason = "startup";

        // Tables this session wrote into (or already dealt with in) the mirror; null until the first refresh.
        private static volatile HashSet<string> knownTables;
        // File state right after our last write, and at the last editor scan; a different state means an editor wrote.
        private static volatile string writtenSignature;
        private static volatile string scannedSignature;

        public static string MirrorPath => mirrorPath;

        public static string LevelPath(int level) => Path.Combine(dataDir, "Level " + level);

        public static void Init(ManualLogSource logger, string persistentDataPath, string mirrorFile, int debounce, bool sendEditorTables, Func<int> currentLevel)
        {
            log = logger;
            dataDir = persistentDataPath;
            mirrorPath = mirrorFile;
            debounceMs = Math.Max(0, debounce);
            sendTables = sendEditorTables;
            getLevel = currentLevel;
            timer = new Timer(_ => OnTimer(), null, Timeout.Infinite, Timeout.Infinite);
            if (sendTables) watchTimer = new Timer(_ => OnWatch(), null, EditorPollMs, EditorPollMs);
        }

        /// <summary>Thread-safe and cheap: may be called from the game's background query thread.</summary>
        public static void MarkDirty(string reason)
        {
            if (timer == null) return;
            pendingReason = reason;
            Interlocked.Exchange(ref dirty, 1);
            timer.Change(debounceMs, Timeout.Infinite);
        }

        /// <summary>Records tables the main thread has dealt with, so they are not picked up again before the next refresh.</summary>
        public static void MarkHandled(IEnumerable<string> names)
        {
            lock (Lock)
            {
                if (knownTables == null) return;
                foreach (string name in names) knownTables.Add(name);
            }
        }

        /// <summary>A stat() every second; refreshes only when something other than this mod wrote to the file.</summary>
        private static void OnWatch()
        {
            if (knownTables == null) return;
            string signature = Signature();
            if (signature != writtenSignature && signature != scannedSignature) MarkDirty("editor change");
        }

        private static void OnTimer()
        {
            if (Interlocked.CompareExchange(ref refreshing, 1, 0) != 0)
            {
                // A refresh is still running; look again once it has had time to finish.
                timer.Change(debounceMs, Timeout.Infinite);
                return;
            }
            try
            {
                if (Interlocked.Exchange(ref dirty, 0) == 0) return;
                string reason = pendingReason;
                int level = getLevel();
                try
                {
                    Refresh(level, reason);
                }
                catch (SqliteNativeException ex) when (ex.IsBusy)
                {
                    log.LogDebug($"Mirror busy, retrying: {ex.Message}");
                    Interlocked.Exchange(ref dirty, 1);
                    timer.Change(BusyRetryMs, Timeout.Infinite);
                }
                catch (Exception ex)
                {
                    log.LogError($"Mirror refresh of Level {level} failed: {ex}");
                }
            }
            finally
            {
                Interlocked.Exchange(ref refreshing, 0);
            }
        }

        private static void Refresh(int level, string reason)
        {
            string source = LevelPath(level);
            if (!File.Exists(source))
            {
                log.LogInfo($"No database for Level {level} yet; mirror unchanged.");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(mirrorPath));
            var sw = Stopwatch.StartNew();
            lock (Lock)
            {
                if (sendTables && HandOverEditorTables(level)) return;

                SetReadOnly(false);
                IntPtr src = IntPtr.Zero, dst = IntPtr.Zero;
                try
                {
                    src = NativeSqlite.Open(source, NativeSqlite.SQLITE_OPEN_READONLY, BusyTimeoutMs);
                    dst = NativeSqlite.Open(mirrorPath, NativeSqlite.SQLITE_OPEN_READWRITE | NativeSqlite.SQLITE_OPEN_CREATE, BusyTimeoutMs);
                    NativeSqlite.Backup(dst, src);
                    NativeSqlite.Exec(dst,
                        "PRAGMA journal_mode=DELETE;" +
                        "DROP TABLE IF EXISTS _mirror_info;" +
                        "CREATE TABLE _mirror_info (level INTEGER, source_path TEXT, refreshed_at TEXT);" +
                        $"INSERT INTO _mirror_info VALUES ({level}, {Quote(source)}, '{DateTime.Now:yyyy-MM-dd HH:mm:ss}');" +
                        ResultsSql());
                    knownTables = new HashSet<string>(
                        NativeSqlite.QueryStrings(dst, "SELECT name FROM sqlite_master WHERE type='table';"),
                        StringComparer.OrdinalIgnoreCase);
                }
                finally
                {
                    NativeSqlite.Close(dst);
                    NativeSqlite.Close(src);
                    if (!sendTables) SetReadOnly(true);
                }
                writtenSignature = Signature();
            }
            log.LogInfo($"Mirrored Level {level} ({reason}) in {sw.ElapsedMilliseconds} ms");
        }

        /// <summary>
        /// If an editor created tables in the mirror since we last wrote it, queues them for the game and
        /// returns true so this refresh is skipped (it would erase them). The refresh runs after they are handled.
        /// </summary>
        private static bool HandOverEditorTables(int level)
        {
            if (knownTables == null || !File.Exists(mirrorPath)) return false;
            string signature = Signature();
            if (signature == writtenSignature || signature == scannedSignature) return false;

            List<string> found = EditorTables.Scan(mirrorPath, knownTables, level);
            scannedSignature = signature;
            if (found.Count == 0) return false;

            log.LogInfo($"Found {found.Count} new table(s) in the mirror: {string.Join(", ", found)}");
            EditorTables.Enqueue(level, found);
            return true;
        }

        /// <summary>Recreates the _connect_results table so an editor can see what happened to its tables.</summary>
        private static string ResultsSql()
        {
            var sql = new StringBuilder(
                "DROP TABLE IF EXISTS _connect_results;" +
                "CREATE TABLE _connect_results (table_name TEXT, status TEXT, message TEXT, at TEXT);");
            foreach (EditorTables.Result r in EditorTables.Recent())
            {
                sql.Append($"INSERT INTO _connect_results VALUES ({Quote(r.TableName)}, {Quote(r.Status)}, {Quote(r.Message)}, {Quote(r.At)});");
            }
            return sql.ToString();
        }

        private static string Quote(string value) => "'" + (value ?? "").Replace("'", "''") + "'";

        /// <summary>Modification time and size of the database and its journal files; changes whenever anything writes to it.</summary>
        private static string Signature()
        {
            var sb = new StringBuilder();
            foreach (string path in new[] { mirrorPath, mirrorPath + "-wal", mirrorPath + "-journal" })
            {
                var info = new FileInfo(path);
                sb.Append(info.Exists ? info.LastWriteTimeUtc.Ticks + ":" + info.Length : "-").Append('|');
            }
            return sb.ToString();
        }

        private static void SetReadOnly(bool readOnly)
        {
            if (!File.Exists(mirrorPath)) return;
            var attrs = File.GetAttributes(mirrorPath);
            var updated = readOnly ? attrs | FileAttributes.ReadOnly : attrs & ~FileAttributes.ReadOnly;
            if (updated != attrs) File.SetAttributes(mirrorPath, updated);
        }
    }
}
