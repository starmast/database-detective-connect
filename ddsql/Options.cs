namespace DdSql
{
    internal enum Mode
    {
        Repl,
        Query,
        File,
        Stdin,
        ConnectionTest,
        State,
        Drop,
        CloseWindows,
        Tables,
        Schema,
        Help,
        Version,
    }

    internal sealed class Options
    {
        public const int DefaultMaxRows = 1000;
        public const int HardMaxRows = 50000;

        public Mode Mode { get; private set; } = Mode.Repl;
        public string? Sql { get; private set; }
        public string? Path { get; private set; }

        /// <summary>When set, the single SELECT is run as an in-game search and saved as a table with this name.</summary>
        public string? SaveName { get; private set; }

        /// <summary>The tables named after <c>drop</c>.</summary>
        public List<string> DropNames { get; } = new List<string>();

        /// <summary>The table named after <c>schema</c>, or null for all of them.</summary>
        public string? SchemaName { get; private set; }

        /// <summary>How long to keep trying to reach the game before giving up; zero means one attempt.</summary>
        public TimeSpan Wait { get; private set; }

        /// <summary>Null means "decide from whether output is a terminal".</summary>
        public OutputFormat? Format { get; private set; }

        public int MaxRows { get; private set; } = DefaultMaxRows;
        public bool Header { get; private set; } = true;
        public string? Connection { get; private set; }

        public static Options Parse(string[] args)
        {
            var o = new Options();
            bool modeChosen = false;

            void Choose(Mode mode)
            {
                if (modeChosen) throw Usage("Use only one of -q, -f, - (stdin), 'state', 'tables', 'schema', 'drop', 'close-windows' or 'connection test'.");
                o.Mode = mode;
                modeChosen = true;
            }

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                string? inline = null;
                if (arg.StartsWith("--", StringComparison.Ordinal) && arg.Contains('='))
                {
                    int eq = arg.IndexOf('=');
                    inline = arg.Substring(eq + 1);
                    arg = arg.Substring(0, eq);
                }

                string Value()
                {
                    if (inline != null) return inline;
                    if (i + 1 >= args.Length) throw Usage($"{arg} needs a value.");
                    return args[++i];
                }

                switch (arg)
                {
                    case "-h":
                    case "--help":
                        o.Mode = Mode.Help;
                        return o;
                    case "--version":
                        o.Mode = Mode.Version;
                        return o;
                    case "-q":
                    case "--query":
                        Choose(Mode.Query);
                        o.Sql = Value();
                        break;
                    case "-f":
                    case "--filename":
                        Choose(Mode.File);
                        o.Path = Value();
                        break;
                    case "-":
                        Choose(Mode.Stdin);
                        break;
                    case "--save":
                        o.SaveName = Value();
                        break;
                    case "-o":
                    case "--format":
                        string text = Value();
                        if (!Output.TryParseFormat(text, out OutputFormat format)) throw Usage($"Unknown format '{text}'. Use table, json, ndjson or csv.");
                        o.Format = format;
                        break;
                    case "--max-rows":
                        string rows = Value();
                        if (!int.TryParse(rows, out int n) || n < 1 || n > HardMaxRows) throw Usage($"--max-rows must be a number from 1 to {HardMaxRows}.");
                        o.MaxRows = n;
                        break;
                    case "--no-header":
                        o.Header = false;
                        break;
                    case "-c":
                    case "--connection":
                        o.Connection = Value();
                        break;
                    case "state":
                        Choose(Mode.State);
                        break;
                    case "drop":
                        Choose(Mode.Drop);
                        while (i + 1 < args.Length && !args[i + 1].StartsWith('-')) o.DropNames.Add(args[++i]);
                        break;
                    case "--wait":
                        string seconds = Value();
                        if (!int.TryParse(seconds, out int wait) || wait < 1 || wait > 3600) throw Usage("--wait needs a number of seconds from 1 to 3600.");
                        o.Wait = TimeSpan.FromSeconds(wait);
                        break;
                    case "tables":
                        Choose(Mode.Tables);
                        break;
                    case "schema":
                        Choose(Mode.Schema);
                        if (i + 1 < args.Length && !args[i + 1].StartsWith('-')) o.SchemaName = args[++i];
                        break;
                    case "close-windows":
                        Choose(Mode.CloseWindows);
                        break;
                    case "connection":
                        if (i + 1 >= args.Length || args[i + 1] != "test") throw Usage("Did you mean 'ddsql connection test'?");
                        i++;
                        Choose(Mode.ConnectionTest);
                        break;
                    default:
                        throw Usage($"Unknown argument '{arg}'. Run ddsql --help.");
                }
            }

            if (o.Mode == Mode.Drop && o.DropNames.Count == 0) throw Usage("drop needs the name of a table: ddsql drop red_cars");
            if (o.Mode == Mode.Schema && o.SchemaName != null && !Runner.IsIdentifier(o.SchemaName)) throw Usage("That is not a table name.");
            if (o.SaveName != null && o.Mode is not (Mode.Query or Mode.File or Mode.Stdin))
            {
                throw Usage("--save NAME needs a query to save: use it with -q, -f or -.");
            }
            return o;
        }

        private static DdSqlException Usage(string message) => new DdSqlException(ExitCodes.Usage, message);
    }
}
