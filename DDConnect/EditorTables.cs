using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx.Logging;

namespace DDConnect
{
    /// <summary>
    /// Sends tables an external editor created in the mirror into the game. MirrorService finds them
    /// (on its timer thread) and queues them here; <see cref="Tick"/> adopts them on the main thread:
    /// the same checks the game applies to a saved search, a copy of the rows into the level database,
    /// then the same panel and icon the game builds when it loads a level.
    /// </summary>
    internal static class EditorTables
    {
        // The game's own limits: CreatePanels (rows) and QueryButton (icons on the desktop).
        private const int MaxRows = 50000;
        private const int MaxIcons = 60;
        private const int MaxResults = 50;
        private const int BusyTimeoutMs = 1000;

        // What the in-game name box accepts, and it keeps the name safe to put inside a quoted identifier.
        private static readonly Regex NamePattern = new Regex("^[A-Za-z_][A-Za-z0-9_]{0,63}$");

        private static readonly ConcurrentQueue<Batch> queue = new ConcurrentQueue<Batch>();
        private static readonly List<Result> results = new List<Result>();

        private static ManualLogSource log;

        private sealed class Batch
        {
            public int Level;
            public List<string> Names;
        }

        public sealed class Result
        {
            public string TableName;
            public string Status;
            public string Message;
            public string At;
        }

        public static void Init(ManualLogSource logger) => log = logger;

        /// <summary>Tables in the mirror that this session did not put there. Runs on any thread, with MirrorService.Lock held.</summary>
        public static List<string> Scan(string mirrorPath, ICollection<string> known, int level, string sourcePath)
        {
            IntPtr db = IntPtr.Zero;
            try
            {
                db = NativeSqlite.Open(mirrorPath, NativeSqlite.SQLITE_OPEN_READONLY, 2000);
                // Tables made for a different case than the one now loaded would land in the wrong database.
                long mirrorLevel = NativeSqlite.QueryInt64(db, "SELECT level FROM _mirror_info LIMIT 1;");
                if (mirrorLevel != level) return new List<string>();
                // A workshop case is always level -1, so also check it is the same case's database.
                string mirroredSource = NativeSqlite.QueryStrings(db, "SELECT source_path FROM _mirror_info LIMIT 1;").FirstOrDefault();
                if (!string.Equals(mirroredSource, sourcePath, StringComparison.OrdinalIgnoreCase)) return new List<string>();
                return NativeSqlite.QueryStrings(db,
                        "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite!_%' ESCAPE '!' ORDER BY rowid;")
                    .Where(name => !known.Contains(name))
                    .ToList();
            }
            finally
            {
                NativeSqlite.Close(db);
            }
        }

        public static void Enqueue(int level, List<string> names) => queue.Enqueue(new Batch { Level = level, Names = names });

        public static List<Result> Recent()
        {
            lock (results) return new List<Result>(results);
        }

        /// <summary>Call every frame from the main thread; does nothing unless a batch is waiting.</summary>
        public static void Tick()
        {
            if (!queue.TryDequeue(out Batch batch)) return;
            try
            {
                Process(batch);
            }
            catch (Exception ex)
            {
                log.LogError($"Could not process editor tables: {ex}");
            }
            finally
            {
                // Whatever happened, let the mirror refresh again; rejected tables are then removed from it.
                MirrorService.MarkHandled(batch.Names);
                MirrorService.MarkDirty("editor tables");
            }
        }

        private static void Process(Batch batch)
        {
            UnityEngine.Object button = GameUi.FindQueryButton();
            int added = 0;
            string firstError = null;
            foreach (string name in batch.Names)
            {
                string error;
                try
                {
                    error = Adopt(button, batch.Level, name);
                }
                catch (Exception ex)
                {
                    log.LogError($"Editor table {name} failed: {ex}");
                    error = ex is SqliteNativeException native && native.IsBusy
                        ? "The game's database was busy. Run the statement again."
                        : ex.Message;
                }

                if (error == null) added++;
                else firstError = firstError ?? error;
                Record(name, error);
            }

            // A new icon is feedback enough when everything arrived; only speak up when something was refused.
            int failed = batch.Names.Count - added;
            if (button == null || failed == 0) return;
            string summary = added == 0 && failed == 1
                ? "Could not add the table from your SQL editor: " + firstError
                : $"Received {added} of {batch.Names.Count} tables from your SQL editor. In your editor, run SELECT * FROM _connect_results; to see why the rest were refused.";
            GameUi.Notify(button, summary);
        }

        /// <summary>Returns null on success, otherwise the reason the table was refused.</summary>
        private static string Adopt(UnityEngine.Object button, int level, string name)
        {
            if (button == null || level == 0) return "Open a case in the game first, then run the statement again.";
            if (!NamePattern.IsMatch(name))
            {
                return "Table names must start with a letter or underscore and use only letters, digits and underscores (64 characters at most).";
            }
            if (GameUi.TableNames().Any(existing => existing.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                return "The game already has a table with this name.";
            }
            if (GameUi.IconCount(button) >= MaxIcons)
            {
                return $"There are too many icons on your desktop (the limit is {MaxIcons}). Delete some tables first.";
            }
            if (!GameUi.IsTableNameValid(button, name)) return "The game does not allow this table name (reserved word or name).";

            long rows = CountRows(name);
            if (rows == 0) return "The table is empty, and the game does not keep empty tables.";
            if (rows > MaxRows) return $"The table has {rows} rows; the game allows at most {MaxRows}.";

            CopyIntoLevel(level, name);
            try
            {
                GameUi.ShowTable(button, name);
            }
            catch (Exception ex)
            {
                GameUi.DropTable(name);
                return ex.Message;
            }
            log.LogInfo($"Added table {name} ({rows} rows) from the editor.");
            return null;
        }

        private static long CountRows(string name)
        {
            lock (MirrorService.Lock)
            {
                IntPtr db = IntPtr.Zero;
                try
                {
                    db = NativeSqlite.Open(MirrorService.MirrorPath, NativeSqlite.SQLITE_OPEN_READONLY, BusyTimeoutMs);
                    return NativeSqlite.QueryInt64(db, $"SELECT COUNT(*) FROM \"{name}\";");
                }
                finally
                {
                    NativeSqlite.Close(db);
                }
            }
        }

        /// <summary>Copies the table from the mirror into the level's database, the same way the game saves a search result.</summary>
        private static void CopyIntoLevel(int level, string name)
        {
            lock (MirrorService.Lock)
            {
                IntPtr db = IntPtr.Zero;
                try
                {
                    db = NativeSqlite.Open(MirrorService.LevelPath(level), NativeSqlite.SQLITE_OPEN_READWRITE, BusyTimeoutMs);
                    NativeSqlite.Exec(db, "ATTACH DATABASE '" + MirrorService.MirrorPath.Replace("'", "''") + "' AS editor;");
                    try
                    {
                        NativeSqlite.Exec(db, "BEGIN;");
                        try
                        {
                            NativeSqlite.Exec(db, $"CREATE TABLE main.\"{name}\" AS SELECT * FROM editor.\"{name}\";");
                            NativeSqlite.Exec(db, "COMMIT;");
                        }
                        catch
                        {
                            try { NativeSqlite.Exec(db, "ROLLBACK;"); } catch (SqliteNativeException) { }
                            throw;
                        }
                    }
                    finally
                    {
                        try { NativeSqlite.Exec(db, "DETACH DATABASE editor;"); } catch (SqliteNativeException) { }
                    }
                }
                finally
                {
                    NativeSqlite.Close(db);
                }
            }
        }

        private static void Record(string name, string error)
        {
            if (error != null) log.LogInfo($"Editor table {name} refused: {error}");
            lock (results)
            {
                results.Add(new Result
                {
                    TableName = name,
                    Status = error == null ? "ok" : "error",
                    Message = error ?? "Added to the game.",
                    At = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                });
                if (results.Count > MaxResults) results.RemoveRange(0, results.Count - MaxResults);
            }
        }
    }
}
