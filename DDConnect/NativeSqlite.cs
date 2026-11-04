using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace DDConnect
{
    /// <summary>
    /// Minimal bindings to the game's bundled sqlite3.dll (already loaded by Mono.Data.Sqlite),
    /// used for the online backup API, which the game's Mono.Data.Sqlite build does not expose.
    /// </summary>
    internal static class NativeSqlite
    {
        private const string Lib = "sqlite3";

        public const int SQLITE_OK = 0;
        public const int SQLITE_BUSY = 5;
        public const int SQLITE_LOCKED = 6;
        public const int SQLITE_ROW = 100;
        public const int SQLITE_DONE = 101;

        public const int SQLITE_OPEN_READONLY = 0x00000001;
        public const int SQLITE_OPEN_READWRITE = 0x00000002;
        public const int SQLITE_OPEN_CREATE = 0x00000004;

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr zVfs);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_close_v2(IntPtr db);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_busy_timeout(IntPtr db, int ms);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_backup_init(IntPtr dest, byte[] destName, IntPtr source, byte[] sourceName);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_backup_step(IntPtr backup, int pages);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_backup_finish(IntPtr backup);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_exec(IntPtr db, byte[] sql, IntPtr callback, IntPtr arg, out IntPtr errmsg);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern void sqlite3_free(IntPtr ptr);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_errmsg(IntPtr db);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int nByte, out IntPtr stmt, IntPtr tail);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_step(IntPtr stmt);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_column_text(IntPtr stmt, int column);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern long sqlite3_column_int64(IntPtr stmt, int column);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_finalize(IntPtr stmt);

        private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s + "\0");

        private static string FromUtf8(IntPtr p)
        {
            if (p == IntPtr.Zero) return null;
            int len = 0;
            while (Marshal.ReadByte(p, len) != 0) len++;
            byte[] buf = new byte[len];
            Marshal.Copy(p, buf, 0, len);
            return Encoding.UTF8.GetString(buf);
        }

        public static IntPtr Open(string path, int flags, int busyTimeoutMs)
        {
            int rc = sqlite3_open_v2(Utf8(path), out IntPtr db, flags, IntPtr.Zero);
            if (rc != SQLITE_OK)
            {
                string msg = db != IntPtr.Zero ? FromUtf8(sqlite3_errmsg(db)) : "out of memory";
                if (db != IntPtr.Zero) sqlite3_close_v2(db);
                throw new SqliteNativeException(rc, $"open '{path}': {msg}");
            }
            sqlite3_busy_timeout(db, busyTimeoutMs);
            return db;
        }

        public static void Close(IntPtr db)
        {
            if (db != IntPtr.Zero) sqlite3_close_v2(db);
        }

        /// <summary>Copies all of <paramref name="source"/>'s main database into <paramref name="dest"/> in one step.</summary>
        public static void Backup(IntPtr dest, IntPtr source)
        {
            byte[] main = Utf8("main");
            IntPtr backup = sqlite3_backup_init(dest, main, source, main);
            if (backup == IntPtr.Zero)
            {
                throw new SqliteNativeException(-1, "backup_init: " + FromUtf8(sqlite3_errmsg(dest)));
            }
            int stepRc = sqlite3_backup_step(backup, -1);
            int finishRc = sqlite3_backup_finish(backup);
            if (stepRc != SQLITE_DONE)
            {
                throw new SqliteNativeException(stepRc, "backup_step: " + FromUtf8(sqlite3_errmsg(dest)));
            }
            if (finishRc != SQLITE_OK)
            {
                throw new SqliteNativeException(finishRc, "backup_finish: " + FromUtf8(sqlite3_errmsg(dest)));
            }
        }

        /// <summary>Compiles one statement; the caller must <see cref="Finalize"/> the result.</summary>
        public static IntPtr Prepare(IntPtr db, string sql)
        {
            int rc = sqlite3_prepare_v2(db, Utf8(sql), -1, out IntPtr stmt, IntPtr.Zero);
            if (rc != SQLITE_OK)
            {
                throw new SqliteNativeException(rc, "prepare: " + FromUtf8(sqlite3_errmsg(db)));
            }
            return stmt;
        }

        /// <summary>Returns true when a row is available, false when the statement is done.</summary>
        public static bool Step(IntPtr db, IntPtr stmt)
        {
            int rc = sqlite3_step(stmt);
            if (rc == SQLITE_ROW) return true;
            if (rc == SQLITE_DONE) return false;
            throw new SqliteNativeException(rc, "step: " + FromUtf8(sqlite3_errmsg(db)));
        }

        /// <summary>Runs a query and returns its first column as text, one entry per row.</summary>
        public static List<string> QueryStrings(IntPtr db, string sql)
        {
            var values = new List<string>();
            IntPtr stmt = Prepare(db, sql);
            try
            {
                while (Step(db, stmt)) values.Add(ColumnText(stmt, 0));
            }
            finally
            {
                Finalize(stmt);
            }
            return values;
        }

        /// <summary>Runs a query and returns the first column of the first row, or 0 when there are no rows.</summary>
        public static long QueryInt64(IntPtr db, string sql)
        {
            IntPtr stmt = Prepare(db, sql);
            try
            {
                return Step(db, stmt) ? ColumnInt64(stmt, 0) : 0;
            }
            finally
            {
                Finalize(stmt);
            }
        }

        public static string ColumnText(IntPtr stmt, int column) => FromUtf8(sqlite3_column_text(stmt, column));

        public static long ColumnInt64(IntPtr stmt, int column) => sqlite3_column_int64(stmt, column);

        public static void Finalize(IntPtr stmt)
        {
            if (stmt != IntPtr.Zero) sqlite3_finalize(stmt);
        }

        public static void Exec(IntPtr db, string sql)
        {
            int rc = sqlite3_exec(db, Utf8(sql), IntPtr.Zero, IntPtr.Zero, out IntPtr errPtr);
            if (rc != SQLITE_OK)
            {
                string msg = FromUtf8(errPtr);
                if (errPtr != IntPtr.Zero) sqlite3_free(errPtr);
                throw new SqliteNativeException(rc, "exec: " + msg);
            }
        }
    }

    internal sealed class SqliteNativeException : Exception
    {
        public int Code { get; }

        public bool IsBusy => Code == NativeSqlite.SQLITE_BUSY || Code == NativeSqlite.SQLITE_LOCKED;

        public SqliteNativeException(int code, string message) : base($"{message} (rc={code})")
        {
            Code = code;
        }
    }
}
