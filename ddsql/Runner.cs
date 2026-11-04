using System.Text;
using System.Text.Json;

namespace DdSql
{
    /// <summary>Runs statements against the game and prints what comes back. Shared by the one-shot modes and the prompt.</summary>
    internal sealed class Runner
    {
        private readonly BridgeClient client;
        private readonly Options options;
        private readonly TextWriter stdout;

        public Runner(BridgeClient client, Options options, OutputFormat format, TextWriter stdout)
        {
            this.client = client;
            this.options = options;
            Format = format;
            this.stdout = stdout;
        }

        public OutputFormat Format { get; set; }

        /// <summary>Runs a statement for the caller to render itself (used by the prompt's .schema).</summary>
        public QueryResult Fetch(string statement)
        {
            QueryResult result = client.Query(statement, options.MaxRows);
            Level = result.Level;
            return result;
        }

        public JsonElement FetchState() => client.State();

        /// <summary>The level named in the most recent reply (0 until one arrives).</summary>
        public int Level { get; set; }

        /// <summary>The last read statement that succeeded, so the prompt's .save can save it as a table.</summary>
        public string? LastSelect { get; private set; }

        /// <summary>Runs one statement as a read-only query and prints the result. Throws <see cref="DdSqlException"/> if refused.</summary>
        public void Execute(string statement)
        {
            QueryResult result;
            try
            {
                result = client.Query(statement, options.MaxRows);
            }
            catch (DdSqlException ex)
            {
                throw Hints.Refine(ex, statement);
            }
            Level = result.Level;
            if (IsRead(statement)) LastSelect = statement;
            Output.Write(result, Format, options.Header, stdout);
        }

        /// <summary>The one way to create a table: run a SELECT as an in-game search and save it under <paramref name="name"/>.</summary>
        public void Save(string name, string statement)
        {
            string message = client.Save(name, statement);
            if (Format is OutputFormat.Json or OutputFormat.Ndjson)
            {
                using var stream = new MemoryStream();
                using (var j = new Utf8JsonWriter(stream))
                {
                    j.WriteStartObject();
                    j.WriteBoolean("ok", true);
                    j.WriteString("table", name);
                    j.WriteString("message", message);
                    j.WriteEndObject();
                }
                stdout.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
            }
            else
            {
                stdout.WriteLine(Output.Clean(message));
            }
        }

        /// <summary>The one way to delete a table: the game's own delete, only for tables the game lets a player delete.</summary>
        public void Drop(string name)
        {
            string message = client.Drop(name);
            if (Format is OutputFormat.Json or OutputFormat.Ndjson) stdout.WriteLine(Json(w => { w.WriteString("table", name); w.WriteString("message", message); }));
            else stdout.WriteLine(Output.Clean(message));
        }

        public void CloseWindows()
        {
            int closed = client.CloseWindows();
            if (Format is OutputFormat.Json or OutputFormat.Ndjson) stdout.WriteLine(Json(w => w.WriteNumber("closed", closed)));
            else stdout.WriteLine(closed == 1 ? "Closed 1 window." : $"Closed {closed} windows.");
        }

        private static string Json(Action<Utf8JsonWriter> members)
        {
            using var stream = new MemoryStream();
            using (var j = new Utf8JsonWriter(stream))
            {
                j.WriteStartObject();
                j.WriteBoolean("ok", true);
                members(j);
                j.WriteEndObject();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        public void PrintState(JsonElement state)
        {
            if (Format is OutputFormat.Json or OutputFormat.Ndjson)
            {
                stdout.WriteLine(state.GetRawText());
                return;
            }

            int level = state.GetProperty("level").GetInt32();
            Level = level;
            if (!state.GetProperty("inLevel").GetBoolean())
            {
                stdout.WriteLine(level == 0 ? "No case is open (main menu)." : $"Level {level} is not ready yet.");
                return;
            }

            stdout.WriteLine($"Level {level}, {state.GetProperty("iconCount").GetInt32()} of {state.GetProperty("iconLimit").GetInt32()} desktop icons used" +
                             (state.GetProperty("busy").GetBoolean() ? " (a save is running)" : ""));
            foreach (JsonElement table in state.GetProperty("tables").EnumerateArray())
            {
                string name = Output.Clean(table.GetProperty("name").GetString() ?? "");
                stdout.WriteLine("  " + name + (table.GetProperty("saved").GetBoolean() ? "  (saved search)" : ""));
            }
        }

        internal static bool IsRead(string statement)
        {
            string word = Hints.FirstWord(statement);
            return word is "SELECT" or "WITH" or "VALUES";
        }
    }

    internal static class Hints
    {
        /// <summary>Adds the way forward to a refusal when someone tries to create a table with plain SQL.</summary>
        public static DdSqlException Refine(DdSqlException ex, string statement)
        {
            if (ex.Code != "read_only") return ex;
            string? way = FirstWord(statement) switch
            {
                "CREATE" => "Use --save NAME with a SELECT to create a table in the game.",
                "DROP" => "Use ddsql drop NAME to delete a table in the game.",
                _ => null,
            };
            return way == null ? ex : new DdSqlException(ex.ExitCode, ex.Message + " " + way, ex.Code);
        }

        /// <summary>The first keyword of the statement, upper-cased, skipping leading whitespace and comments.</summary>
        public static string FirstWord(string statement)
        {
            int i = 0;
            while (i < statement.Length)
            {
                if (char.IsWhiteSpace(statement[i])) { i++; continue; }
                if (statement.AsSpan(i).StartsWith("--"))
                {
                    int end = statement.IndexOf('\n', i);
                    if (end < 0) return "";
                    i = end;
                    continue;
                }
                if (statement.AsSpan(i).StartsWith("/*"))
                {
                    int end = statement.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (end < 0) return "";
                    i = end + 2;
                    continue;
                }
                break;
            }

            int start = i;
            while (i < statement.Length && char.IsLetter(statement[i])) i++;
            return statement.Substring(start, i - start).ToUpperInvariant();
        }
    }
}
