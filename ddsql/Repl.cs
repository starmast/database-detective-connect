using System.Text;
using System.Text.RegularExpressions;

namespace DdSql
{
    /// <summary>The interactive prompt: type SQL ending in a semicolon, or a dot-command.</summary>
    internal sealed class Repl
    {
        private static readonly Regex Identifier = new Regex("^[A-Za-z_][A-Za-z0-9_]*$");

        private const string Help =
            "Type SQL ending in ; to run it. Queries are read-only.\n" +
            "  .tables           list the tables in the case\n" +
            "  .schema [name]    show how tables are defined\n" +
            "  .state            level, desktop icons and saved searches\n" +
            "  .save NAME        save the last SELECT as a table in the game\n" +
            "  .drop NAME        delete a table (same as the game's Delete table)\n" +
            "  .close            close the windows that fill the taskbar\n" +
            "  .format FORMAT    table, json, ndjson or csv\n" +
            "  .help             this text\n" +
            "  .quit             leave (or press Ctrl-D)";

        private readonly Runner runner;
        private readonly TextReader input;
        private readonly TextWriter stdout;
        private readonly TextWriter stderr;
        private readonly string version;

        public Repl(Runner runner, string version, TextReader input, TextWriter stdout, TextWriter stderr)
        {
            this.runner = runner;
            this.version = version;
            this.input = input;
            this.stdout = stdout;
            this.stderr = stderr;
        }

        /// <summary>Returns the process exit code: 0 on a normal exit, 3 if the game went away.</summary>
        public int Run()
        {
            stdout.WriteLine($"ddsql {version}, connected to Database Detective ({Where()}). Type .help for commands, .quit to leave.");
            var buffer = new StringBuilder();
            while (true)
            {
                stdout.Write(buffer.Length == 0 ? $"ddsql [{Where()}]> " : "      ...> ");
                string? line = input.ReadLine();
                if (line == null)
                {
                    stdout.WriteLine();
                    return ExitCodes.Ok;
                }

                if (buffer.Length == 0)
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0) continue;
                    if (trimmed.StartsWith('.') || trimmed is "exit" or "quit")
                    {
                        int? exit = RunDotCommand(trimmed);
                        if (exit != null) return exit.Value;
                        continue;
                    }
                }

                buffer.AppendLine(line);
                List<string> statements = SqlSplitter.Split(buffer.ToString(), out string rest);
                buffer.Clear();
                if (rest.Length > 0) buffer.AppendLine(rest);

                foreach (string statement in statements)
                {
                    int? exit = Guard(() => runner.Execute(statement));
                    if (exit != null) return exit.Value;
                }
            }
        }

        private string Where() => runner.Level > 0 ? $"Level {runner.Level}" : "no case open";

        /// <summary>Returns null to keep going, or the exit code to stop with.</summary>
        private int? RunDotCommand(string command)
        {
            string[] parts = command.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string argument = parts.Length > 1 ? parts[1] : "";
            switch (parts[0])
            {
                case ".quit":
                case ".exit":
                case "exit":
                case "quit":
                    return ExitCodes.Ok;
                case ".help":
                    stdout.WriteLine(Help);
                    return null;
                case ".tables":
                    return Guard(runner.Tables);
                case ".schema":
                    return Guard(() => runner.Schema(argument.Length > 0 ? argument : null));
                case ".state":
                    return Guard(() => runner.PrintState(runner.FetchState()));
                case ".save":
                    return Guard(() => SaveLast(argument));
                case ".drop":
                    return Guard(() => DropTable(argument));
                case ".close":
                    return Guard(runner.CloseWindows);
                case ".format":
                    if (Output.TryParseFormat(argument, out OutputFormat format)) runner.Format = format;
                    else stderr.WriteLine("Error: use .format table, json, ndjson or csv.");
                    return null;
                default:
                    stderr.WriteLine($"Error: unknown command {parts[0]}. Type .help for a list.");
                    return null;
            }
        }

        private void SaveLast(string name)
        {
            if (!Identifier.IsMatch(name)) throw new DdSqlException(ExitCodes.Usage, "Give the table a name: .save red_cars");
            if (runner.LastSelect == null) throw new DdSqlException(ExitCodes.Failed, "Run a SELECT first, then .save NAME saves it.");
            runner.Save(name, runner.LastSelect);
        }

        private void DropTable(string name)
        {
            if (!Identifier.IsMatch(name)) throw new DdSqlException(ExitCodes.Usage, "Give the name of a table: .drop red_cars");
            runner.Drop(name);
        }

        /// <summary>Runs an action; a refused query is reported and the prompt carries on, but losing the game ends the session.</summary>
        private int? Guard(Action action)
        {
            try
            {
                action();
                return null;
            }
            catch (DdSqlException ex)
            {
                stderr.WriteLine("Error: " + ex.Message);
                return ex.ExitCode == ExitCodes.Unreachable || ex.ExitCode == ExitCodes.Auth ? ex.ExitCode : null;
            }
        }
    }
}
