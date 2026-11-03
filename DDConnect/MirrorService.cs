using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using BepInEx.Logging;

namespace DDConnect
{
    /// <summary>
    /// Keeps a read-only copy of the current level's SQLite database at a fixed path.
    /// Refreshes are driven by <see cref="MarkDirty"/> (game commits and level changes)
    /// and debounced with a one-shot timer, so nothing runs while the game is idle.
    /// </summary>
    internal static class MirrorService
    {
        private const int BusyTimeoutMs = 2000;
        private const int BusyRetryMs = 1000;

        private static ManualLogSource log;
        private static string dataDir;
        private static string mirrorPath;
        private static int debounceMs;
        private static Func<int> getLevel;
        private static Timer timer;

        private static int dirty;
        private static int refreshing;
        private static string pendingReason = "startup";

        public static void Init(ManualLogSource logger, string persistentDataPath, string mirrorFile, int debounce, Func<int> currentLevel)
        {
            log = logger;
            dataDir = persistentDataPath;
            mirrorPath = mirrorFile;
            debounceMs = Math.Max(0, debounce);
            getLevel = currentLevel;
            timer = new Timer(_ => OnTimer(), null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>Thread-safe and cheap: may be called from the game's background query thread.</summary>
        public static void MarkDirty(string reason)
        {
            if (timer == null) return;
            pendingReason = reason;
            Interlocked.Exchange(ref dirty, 1);
            timer.Change(debounceMs, Timeout.Infinite);
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
            string source = Path.Combine(dataDir, "Level " + level);
            if (!File.Exists(source))
            {
                log.LogInfo($"No database for Level {level} yet; mirror unchanged.");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(mirrorPath));
            var sw = Stopwatch.StartNew();
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
                    $"INSERT INTO _mirror_info VALUES ({level}, '{source.Replace("'", "''")}', '{DateTime.Now:yyyy-MM-dd HH:mm:ss}');");
            }
            finally
            {
                NativeSqlite.Close(dst);
                NativeSqlite.Close(src);
                SetReadOnly(true);
            }
            log.LogInfo($"Mirrored Level {level} ({reason}) in {sw.ElapsedMilliseconds} ms");
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
