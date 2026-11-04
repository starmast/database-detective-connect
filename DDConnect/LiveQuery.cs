using System;
using System.IO;
using System.Threading;

namespace DDConnect
{
    /// <summary>
    /// The bridge's read-only query command. Runs on the connection's own thread against a fresh read-only
    /// connection to the live level file, so it never touches Unity and can never change the game's data:
    /// the file is opened read-only, only one statement is allowed, and the statement must be a plain read.
    /// </summary>
    internal static class LiveQuery
    {
        public const int DefaultMaxRows = 1000;
        public const int HardMaxRows = 50000;

        private const int TimeoutMs = 5000;
        private const int BusyTimeoutMs = 1000;

        // Statements that start with anything else are refused even if sqlite would call them read-only
        // (ATTACH, DETACH, BEGIN and friends report as read-only but still change the connection).
        private static readonly string[] AllowedStarts = { "SELECT", "WITH", "VALUES", "EXPLAIN", "PRAGMA" };

        public static BridgeResult Run(string sql, int maxRows)
        {
            int level = MainThread.Level;
            if (level == 0) return BridgeResult.Fail("not_in_level", "Open a case in the game first.");
            string path = MirrorService.LevelPath(level);
            if (!File.Exists(path)) return BridgeResult.Fail("not_in_level", $"The case (level {level}) has no database yet.");
            if (string.IsNullOrWhiteSpace(sql)) return BridgeResult.Fail("bad_request", "The query is empty.");
            maxRows = maxRows <= 0 ? DefaultMaxRows : Math.Min(maxRows, HardMaxRows);

            if (!StartsWithAllowedWord(sql))
            {
                return BridgeResult.Fail("read_only", "Only read-only queries (SELECT, WITH, VALUES, EXPLAIN, PRAGMA) are allowed here.");
            }

            IntPtr db = IntPtr.Zero, stmt = IntPtr.Zero;
            Timer limit = null;
            var gate = new object();
            bool closed = false;
            try
            {
                db = NativeSqlite.Open(path, NativeSqlite.SQLITE_OPEN_READONLY, BusyTimeoutMs);
                IntPtr handle = db;
                limit = new Timer(_ =>
                {
                    lock (gate)
                    {
                        if (!closed) NativeSqlite.Interrupt(handle);
                    }
                }, null, TimeoutMs, Timeout.Infinite);

                stmt = NativeSqlite.PrepareFirst(db, sql, out string rest);
                if (stmt == IntPtr.Zero) return BridgeResult.Fail("bad_request", "The query holds no statement.");
                if (!string.IsNullOrWhiteSpace(rest) && HasAnotherStatement(db, rest))
                {
                    return BridgeResult.Fail("read_only", "Only one statement at a time.");
                }
                if (!NativeSqlite.IsReadOnly(stmt))
                {
                    return BridgeResult.Fail("read_only", "This statement would change data, and queries here are read-only.");
                }

                var started = System.Diagnostics.Stopwatch.StartNew();
                int columns = NativeSqlite.ColumnCount(stmt);
                var w = new JsonWriter().BeginObject();
                int rows = 0;
                bool truncated = false;

                w.BeginArray("columns");
                for (int c = 0; c < columns; c++) w.Elem(NativeSqlite.ColumnName(stmt, c));
                w.EndArray();

                w.BeginArray("rows");
                while (NativeSqlite.Step(db, stmt))
                {
                    if (rows >= maxRows)
                    {
                        truncated = true;
                        break;
                    }
                    w.BeginArray();
                    for (int c = 0; c < columns; c++) WriteCell(w, stmt, c);
                    w.EndArray();
                    rows++;
                }
                w.EndArray().EndObject();

                long elapsed = started.ElapsedMilliseconds;
                // Read everything now, while the connection is open; the reply is assembled after it closes.
                string body = w.ToString();
                string data = body.Substring(1, body.Length - 2);
                return BridgeResult.Success(reply =>
                {
                    reply.Fragment(data);
                    reply.Prop("rowCount", rows).Prop("truncated", truncated).Prop("elapsedMs", elapsed).Prop("level", level);
                });
            }
            catch (SqliteNativeException ex) when (ex.Code == NativeSqlite.SQLITE_INTERRUPT)
            {
                return BridgeResult.Fail("timeout", "The query took longer than 5 seconds. Try limiting it with WHERE or LIMIT.");
            }
            catch (SqliteNativeException ex) when (ex.IsBusy)
            {
                return BridgeResult.Fail("busy", "The game's database is busy. Try again.");
            }
            catch (SqliteNativeException ex)
            {
                return BridgeResult.Fail("sql_error", ex.Message);
            }
            finally
            {
                lock (gate)
                {
                    closed = true;
                }
                limit?.Dispose();
                NativeSqlite.Finalize(stmt);
                NativeSqlite.Close(db);
            }
        }

        private static void WriteCell(JsonWriter w, IntPtr stmt, int column)
        {
            switch (NativeSqlite.ColumnType(stmt, column))
            {
                case 1: w.Elem(NativeSqlite.ColumnInt64(stmt, column)); break;
                case 2: w.Elem(NativeSqlite.ColumnDouble(stmt, column)); break;
                case 3: w.Elem(NativeSqlite.ColumnText(stmt, column)); break;
                case 4: w.Elem($"<blob {NativeSqlite.ColumnBytes(stmt, column)} bytes>"); break;
                default: w.ElemNull(); break;
            }
        }

        /// <summary>Compiling the remainder tells a second statement apart from trailing comments or semicolons.</summary>
        private static bool HasAnotherStatement(IntPtr db, string rest)
        {
            IntPtr extra = IntPtr.Zero;
            try
            {
                extra = NativeSqlite.PrepareFirst(db, rest, out string _);
                return extra != IntPtr.Zero;
            }
            finally
            {
                NativeSqlite.Finalize(extra);
            }
        }

        private static bool StartsWithAllowedWord(string sql)
        {
            int i = 0;
            while (i < sql.Length)
            {
                if (char.IsWhiteSpace(sql[i]) || sql[i] == ';') { i++; continue; }
                if (i + 1 < sql.Length && sql[i] == '-' && sql[i + 1] == '-')
                {
                    while (i < sql.Length && sql[i] != '\n') i++;
                    continue;
                }
                if (i + 1 < sql.Length && sql[i] == '/' && sql[i + 1] == '*')
                {
                    int end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (end < 0) return false;
                    i = end + 2;
                    continue;
                }
                break;
            }

            int start = i;
            while (i < sql.Length && char.IsLetter(sql[i])) i++;
            string word = sql.Substring(start, i - start);
            foreach (string allowed in AllowedStarts)
            {
                if (word.Equals(allowed, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
