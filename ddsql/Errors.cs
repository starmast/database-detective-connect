namespace DdSql
{
    internal static class ExitCodes
    {
        public const int Ok = 0;
        /// <summary>The game refused or failed the query.</summary>
        public const int Failed = 1;
        public const int Usage = 2;
        /// <summary>No bridge.json, a stale one, or nothing listening.</summary>
        public const int Unreachable = 3;
        public const int Auth = 4;
    }

    /// <summary>An expected failure: printed as a one-line message, never as a stack trace.</summary>
    internal sealed class DdSqlException : Exception
    {
        public int ExitCode { get; }

        /// <summary>The bridge's error code (read_only, timeout, ...), when the failure came from the game.</summary>
        public string? Code { get; }

        public DdSqlException(int exitCode, string message, string? code = null) : base(message)
        {
            ExitCode = exitCode;
            Code = code;
        }
    }
}
