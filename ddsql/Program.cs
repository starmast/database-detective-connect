using System.Reflection;
using System.Text;

namespace DdSql
{
    internal static class Program
    {
        private const string Usage =
            "ddsql: a SQL client for the Database Detective game that is running right now.\n" +
            "\n" +
            "Usage:\n" +
            "  ddsql                          open the interactive prompt\n" +
            "  ddsql -q \"SELECT ...\"          run one query\n" +
            "  ddsql -f queries.sql           run the statements in a file\n" +
            "  ddsql -                        run SQL read from standard input\n" +
            "  ddsql -q \"SELECT ...\" --save NAME\n" +
            "                                 run a SELECT in the game and save the result as a table\n" +
            "  ddsql drop NAME [NAME...]      delete tables, as the game's own Delete table does\n" +
            "  ddsql close-windows            close the windows that fill the taskbar\n" +
            "  ddsql tables                   list the tables\n" +
            "  ddsql schema [NAME]            how the tables are defined\n" +
            "  ddsql state                    level, desktop icons and tables\n" +
            "  ddsql connection test          check that the game can be reached\n" +
            "\n" +
            "Queries are read-only. A table can only be created with --save NAME and deleted with drop NAME.\n" +
            "\n" +
            "Options:\n" +
            "  -o, --format FORMAT   table, json, ndjson or csv (default: table in a terminal, json when piped)\n" +
            "      --max-rows N      stop after N rows (default 1000, at most 50000)\n" +
            "      --no-header       leave out the column names (table and csv)\n" +
            "  -c, --connection FILE use this bridge.json (or set DDSQL_CONNECTION)\n" +
            "      --wait SECONDS    keep trying to reach the game for this long\n" +
            "  -h, --help            show this text\n" +
            "      --version         show the version\n" +
            "\n" +
            "Exit codes: 0 ok, 1 query refused or failed, 2 wrong usage, 3 game not reachable, 4 wrong token.\n" +
            "The game needs the bridge turned on: [Bridge] Enabled = true in BepInEx/config/dev.ddconnect.cfg.";

        public static string Version { get; } = ReadVersion();

        private static int Main(string[] args) =>
            Run(args, Console.In, Console.Out, Console.Error, Console.IsOutputRedirected);

        internal static int Run(string[] args, TextReader stdin, TextWriter stdout, TextWriter stderr, bool outputRedirected)
        {
            try
            {
                Options options = Options.Parse(args);
                switch (options.Mode)
                {
                    case Mode.Help:
                        stdout.WriteLine(Usage);
                        return ExitCodes.Ok;
                    case Mode.Version:
                        stdout.WriteLine("ddsql " + Version);
                        return ExitCodes.Ok;
                }

                // Read the SQL before connecting, so a missing file is reported even with the game closed.
                List<string> statements = options.Mode switch
                {
                    Mode.Query => SqlSplitter.SplitAll(options.Sql ?? ""),
                    Mode.File => SqlSplitter.SplitAll(ReadFile(options.Path!)),
                    Mode.Stdin => SqlSplitter.SplitAll(stdin.ReadToEnd()),
                    _ => new List<string>(),
                };
                bool runsSql = options.Mode is Mode.Query or Mode.File or Mode.Stdin;
                if (runsSql && statements.Count == 0) throw new DdSqlException(ExitCodes.Usage, "There is no SQL to run.");
                if (options.SaveName != null && statements.Count != 1)
                {
                    throw new DdSqlException(ExitCodes.Usage, "--save NAME saves one SELECT; give exactly one statement.");
                }

                OutputFormat format = options.Format ?? (outputRedirected ? OutputFormat.Json : OutputFormat.Table);
                using BridgeClient client = ConnectWaiting(options, out BridgeInfo info);
                var runner = new Runner(client, options, format, stdout);

                switch (options.Mode)
                {
                    case Mode.ConnectionTest:
                        return TestConnection(client, info, stdout);
                    case Mode.State:
                        runner.PrintState(client.State());
                        return ExitCodes.Ok;
                    case Mode.Tables:
                        runner.Tables();
                        return ExitCodes.Ok;
                    case Mode.Schema:
                        runner.Schema(options.SchemaName);
                        return ExitCodes.Ok;
                    case Mode.Drop:
                        foreach (string name in options.DropNames) runner.Drop(name);
                        return ExitCodes.Ok;
                    case Mode.CloseWindows:
                        runner.CloseWindows();
                        return ExitCodes.Ok;
                    case Mode.Repl:
                        PingInfo ping = client.Ping();
                        runner.Level = ping.Level;
                        return new Repl(runner, Version, stdin, stdout, stderr).Run();
                }

                if (options.SaveName != null)
                {
                    runner.Save(options.SaveName, statements[0]);
                    return ExitCodes.Ok;
                }
                foreach (string statement in statements) runner.Execute(statement);
                return ExitCodes.Ok;
            }
            catch (DdSqlException ex)
            {
                stderr.WriteLine("ddsql: " + ex.Message);
                return ex.ExitCode;
            }
        }

        /// <summary>Connects, retrying while the game is not reachable yet for as long as --wait allows.</summary>
        private static BridgeClient ConnectWaiting(Options options, out BridgeInfo info)
        {
            DateTime deadline = DateTime.UtcNow + options.Wait;
            while (true)
            {
                try
                {
                    info = BridgeLocator.Load(options.Connection);
                    return BridgeClient.Connect(info);
                }
                catch (DdSqlException ex) when (ex.ExitCode == ExitCodes.Unreachable && DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(500);
                }
            }
        }

        private static int TestConnection(BridgeClient client, BridgeInfo info, TextWriter stdout)
        {
            PingInfo ping = client.Ping();
            stdout.WriteLine($"Connected to DD Connect {ping.Version} on 127.0.0.1:{info.Port} (protocol {ping.Protocol}).");
            stdout.WriteLine(ping.InLevel ? $"A case is open: Level {ping.Level}." : "No case is open yet; load one in the game to run queries.");
            return ExitCodes.Ok;
        }

        private static string ReadFile(string path)
        {
            try
            {
                return File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new DdSqlException(ExitCodes.Usage, $"Cannot read {path}: {ex.Message}");
            }
        }

        private static string ReadVersion()
        {
            string? text = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrEmpty(text)) return "0.0.0";
            int plus = text.IndexOf('+');
            return plus < 0 ? text : text.Substring(0, plus);
        }
    }
}
