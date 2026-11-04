using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace DdSql
{
    internal sealed record BridgeInfo(int Version, int Port, string Token, int Pid);

    internal sealed record PingInfo(int Protocol, string Version, int Level, bool InLevel);

    internal sealed record QueryResult(IReadOnlyList<string> Columns, IReadOnlyList<object?[]> Rows, bool Truncated, long ElapsedMs, int Level);

    /// <summary>Finds the running game's bridge.json, in the same places the README lists for current.db.</summary>
    internal static class BridgeLocator
    {
        public const string EnableHint =
            "Start the game with the bridge turned on: set Enabled = true under [Bridge] in BepInEx/config/dev.ddconnect.cfg, " +
            "or launch the game with the environment variable DDCONNECT_BRIDGE=1.";

        public static string DefaultFile()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string dataDir;
            if (OperatingSystem.IsWindows()) dataDir = Path.Combine(home, "AppData", "LocalLow", "HsuCorp", "copOS");
            else if (OperatingSystem.IsMacOS()) dataDir = Path.Combine(home, "Library", "Application Support", "HsuCorp", "copOS");
            else dataDir = Path.Combine(home, ".config", "unity3d", "HsuCorp", "copOS");
            return Path.Combine(dataDir, "Connect", "bridge.json");
        }

        public static BridgeInfo Load(string? overridePath)
        {
            string path = overridePath ?? Environment.GetEnvironmentVariable("DDSQL_CONNECTION") ?? DefaultFile();
            if (!File.Exists(path))
            {
                throw new DdSqlException(ExitCodes.Unreachable, $"No bridge.json at {path}. {EnableHint}");
            }

            BridgeInfo info = Parse(File.ReadAllText(path), path);
            if (info.Pid > 0 && !IsRunning(info.Pid))
            {
                throw new DdSqlException(ExitCodes.Unreachable,
                    $"{path} is left over from a game that is no longer running (process {info.Pid}). Start the game; {EnableHint}");
            }
            return info;
        }

        public static BridgeInfo Parse(string json, string path)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                return new BridgeInfo(
                    root.GetProperty("version").GetInt32(),
                    root.GetProperty("port").GetInt32(),
                    root.GetProperty("token").GetString() ?? throw new FormatException("empty token"),
                    root.TryGetProperty("pid", out JsonElement pid) ? pid.GetInt32() : 0);
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
            {
                throw new DdSqlException(ExitCodes.Unreachable, $"{path} is not a valid bridge.json ({ex.Message}).");
            }
        }

        private static bool IsRunning(int pid)
        {
            try
            {
                using Process process = Process.GetProcessById(pid);
                return !process.HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (Exception)
            {
                // Cannot tell (permissions); let the connection attempt decide.
                return true;
            }
        }
    }

    /// <summary>One authenticated connection to the game's bridge. Requests and replies are one JSON line each.</summary>
    internal sealed class BridgeClient : IDisposable
    {
        private const int ReplyTimeoutMs = 40000;

        private readonly TcpClient tcp;
        private readonly StreamReader reader;
        private readonly StreamWriter writer;
        private readonly string token;
        private long nextId;

        private BridgeClient(TcpClient tcp, string token)
        {
            this.tcp = tcp;
            this.token = token;
            Stream stream = tcp.GetStream();
            reader = new StreamReader(stream, new UTF8Encoding(false));
            writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        }

        public static BridgeClient Connect(BridgeInfo info)
        {
            var tcp = new TcpClient { ReceiveTimeout = ReplyTimeoutMs, SendTimeout = 10000, NoDelay = true };
            try
            {
                if (!tcp.ConnectAsync(IPAddress.Loopback, info.Port).Wait(3000)) throw new TimeoutException();
            }
            catch (Exception ex) when (ex is AggregateException or SocketException or TimeoutException)
            {
                tcp.Dispose();
                throw new DdSqlException(ExitCodes.Unreachable,
                    $"Could not connect to the game on 127.0.0.1:{info.Port}. Is it still running with the bridge enabled? {BridgeLocator.EnableHint}");
            }
            return new BridgeClient(tcp, info.Token);
        }

        public PingInfo Ping()
        {
            JsonElement r = Call("ping");
            return new PingInfo(r.GetProperty("protocol").GetInt32(), r.GetProperty("version").GetString() ?? "", r.GetProperty("level").GetInt32(), r.GetProperty("inLevel").GetBoolean());
        }

        /// <summary>The raw reply, since its shape is only for display.</summary>
        public JsonElement State() => Call("state");

        public QueryResult Query(string sql, int maxRows)
        {
            JsonElement r = Call("query", ("sql", sql), ("maxRows", maxRows));
            var columns = r.GetProperty("columns").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
            var rows = new List<object?[]>();
            foreach (JsonElement row in r.GetProperty("rows").EnumerateArray())
            {
                rows.Add(row.EnumerateArray().Select(Cell).ToArray());
            }
            return new QueryResult(columns, rows, r.GetProperty("truncated").GetBoolean(), r.GetProperty("elapsedMs").GetInt64(), r.GetProperty("level").GetInt32());
        }

        /// <summary>Runs the query as an in-game search and saves the result as a table. Returns the game's message.</summary>
        public string Save(string name, string query)
        {
            JsonElement r = Call("save", ("name", name), ("query", query));
            return r.TryGetProperty("message", out JsonElement m) ? m.GetString() ?? "" : "";
        }

        /// <summary>Deletes a table the way the game's own "Delete table" menu does. Returns the game's message.</summary>
        public string Drop(string name)
        {
            JsonElement r = Call("drop", ("name", name));
            return r.TryGetProperty("message", out JsonElement m) ? m.GetString() ?? "" : "";
        }

        /// <summary>Closes every window that holds a taskbar button. Returns how many were closed.</summary>
        public int CloseWindows() => Call("close_windows").GetProperty("closed").GetInt32();

        public void Dispose()
        {
            reader.Dispose();
            writer.Dispose();
            tcp.Dispose();
        }

        private static object? Cell(JsonElement e) => e.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.Number => e.TryGetInt64(out long l) ? l : e.GetDouble(),
            JsonValueKind.String => e.GetString(),
            _ => e.GetRawText(),
        };

        private JsonElement Call(string cmd, params (string Name, object Value)[] args)
        {
            long id = ++nextId;
            try
            {
                writer.WriteLine(BuildRequest(id, token, cmd, args));
                string? line = reader.ReadLine();
                if (line == null) throw new DdSqlException(ExitCodes.Unreachable, "The game closed the connection.");
                return ParseReply(line);
            }
            catch (IOException ex)
            {
                throw new DdSqlException(ExitCodes.Unreachable, "Lost the connection to the game: " + ex.Message);
            }
        }

        internal static string BuildRequest(long id, string token, string cmd, params (string Name, object Value)[] args)
        {
            using var stream = new MemoryStream();
            using (var w = new Utf8JsonWriter(stream))
            {
                w.WriteStartObject();
                w.WriteNumber("id", id);
                w.WriteString("token", token);
                w.WriteString("cmd", cmd);
                foreach ((string name, object value) in args)
                {
                    if (value is int i) w.WriteNumber(name, i);
                    else w.WriteString(name, value.ToString());
                }
                w.WriteEndObject();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        internal static JsonElement ParseReply(string line)
        {
            JsonElement root;
            try
            {
                root = JsonDocument.Parse(line).RootElement;
            }
            catch (JsonException)
            {
                throw new DdSqlException(ExitCodes.Failed, "The game sent a reply that is not valid JSON.");
            }

            if (root.GetProperty("ok").GetBoolean()) return root;

            JsonElement error = root.GetProperty("error");
            string code = error.GetProperty("code").GetString() ?? "";
            string message = error.GetProperty("message").GetString() ?? "";
            throw new DdSqlException(code == "auth" ? ExitCodes.Auth : ExitCodes.Failed, message, code);
        }
    }
}
