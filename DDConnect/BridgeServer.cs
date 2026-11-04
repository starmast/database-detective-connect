using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BepInEx.Logging;

namespace DDConnect
{
    /// <summary>
    /// Localhost-only TCP server that lets tools such as ddsql talk to the running game. The wire format is one
    /// JSON object per line in each direction (see docs/bridge-protocol.md). Every request must carry the token
    /// from Connect/bridge.json, which only the user's own processes can read.
    /// </summary>
    internal static class BridgeServer
    {
        public const int ProtocolVersion = 1;

        private const int MaxConnections = 4;
        private const int MaxLineBytes = 64 * 1024;
        private const int IdleTimeoutMs = 120000;
        private const int MainThreadTimeoutMs = 20000;
        private const int IconLimit = 60;

        private static readonly string[] HttpMethods = { "GET ", "POST ", "PUT ", "HEAD ", "OPTIONS ", "DELETE ", "PATCH ", "CONNECT ", "TRACE " };

        private static ManualLogSource log;
        private static TcpListener listener;
        private static string bridgeFile;
        private static string token;
        private static volatile bool stopping;
        private static int connections;

        public static void Start(ManualLogSource logger, string bridgeFilePath)
        {
            log = logger;
            bridgeFile = bridgeFilePath;
            token = NewToken();

            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            WriteBridgeFile(port);
            AppDomain.CurrentDomain.ProcessExit += (_, __) => Stop();

            var thread = new Thread(AcceptLoop) { IsBackground = true, Name = "DDConnect bridge" };
            thread.Start();
            log.LogInfo($"Bridge listening on 127.0.0.1:{port} (details in {bridgeFile})");
        }

        public static void Stop()
        {
            if (stopping) return;
            stopping = true;
            try { listener?.Stop(); } catch (Exception) { }
            try { if (bridgeFile != null) File.Delete(bridgeFile); } catch (Exception) { }
        }

        private static string NewToken()
        {
            var bytes = new byte[32];
            using (var rng = new RNGCryptoServiceProvider()) rng.GetBytes(bytes);
            return string.Concat(bytes.Select(b => b.ToString("x2")));
        }

        private static void WriteBridgeFile(int port)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(bridgeFile));
            // Create it empty and restrict it before the token goes in.
            File.WriteAllText(bridgeFile, "");
            RestrictToOwner(bridgeFile);
            string json = new JsonWriter().BeginObject()
                .Prop("version", ProtocolVersion)
                .Prop("port", port)
                .Prop("token", token)
                .Prop("pid", Process.GetCurrentProcess().Id)
                .EndObject().ToString();
            File.WriteAllText(bridgeFile, json);
        }

        [DllImport("libc", EntryPoint = "chmod", SetLastError = true)]
        private static extern int chmod(string path, uint mode);

        /// <summary>On Linux and macOS, owner read/write only (0600). On Windows the user's profile folder is already private.</summary>
        private static void RestrictToOwner(string path)
        {
            if (Environment.OSVersion.Platform != PlatformID.Unix && Environment.OSVersion.Platform != PlatformID.MacOSX) return;
            try
            {
                chmod(path, Convert.ToUInt32("600", 8));
            }
            catch (Exception ex)
            {
                log.LogWarning($"Could not restrict {path} to your user: {ex.Message}");
            }
        }

        private static void AcceptLoop()
        {
            while (!stopping)
            {
                TcpClient client;
                try
                {
                    client = listener.AcceptTcpClient();
                }
                catch (Exception)
                {
                    if (stopping) return;
                    Thread.Sleep(100);
                    continue;
                }

                if (Interlocked.Increment(ref connections) > MaxConnections)
                {
                    Interlocked.Decrement(ref connections);
                    client.Close();
                    continue;
                }
                new Thread(() => Serve(client)) { IsBackground = true, Name = "DDConnect bridge client" }.Start();
            }
        }

        private static void Serve(TcpClient client)
        {
            try
            {
                client.ReceiveTimeout = IdleTimeoutMs;
                client.SendTimeout = 10000;
                client.NoDelay = true;
                using (NetworkStream stream = client.GetStream())
                {
                    var reader = new LineReader(stream, MaxLineBytes);
                    bool first = true;
                    while (true)
                    {
                        string line = reader.ReadLine();
                        if (line == null) break;
                        // A browser or HTTP client got here by mistake or on purpose: say nothing and hang up.
                        if (first && HttpMethods.Any(m => line.StartsWith(m, StringComparison.Ordinal))) break;
                        first = false;

                        string reply = Handle(line, out bool close);
                        byte[] bytes = Encoding.UTF8.GetBytes(reply + "\n");
                        stream.Write(bytes, 0, bytes.Length);
                        if (close) break;
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogDebug($"Bridge connection ended: {ex.Message}");
            }
            finally
            {
                client.Close();
                Interlocked.Decrement(ref connections);
            }
        }

        private static string Handle(string line, out bool close)
        {
            close = false;
            Dictionary<string, string> request = FlatJson.Parse(line);
            if (request == null)
            {
                close = true;
                return BridgeResult.Fail("bad_request", "Requests must be one JSON object per line.").ToJson(0);
            }

            long id = request.TryGetValue("id", out string rawId) && long.TryParse(rawId, out long parsed) ? parsed : 0;
            if (!request.TryGetValue("token", out string given) || !ConstantTimeEquals(given, token))
            {
                // Slow down guessing, and drop the connection.
                Thread.Sleep(250);
                close = true;
                return BridgeResult.Fail("auth", "Missing or wrong token. Read it from bridge.json.").ToJson(id);
            }

            try
            {
                return Dispatch(request).ToJson(id);
            }
            catch (Exception ex)
            {
                log.LogError($"Bridge command failed: {ex}");
                return BridgeResult.Fail("internal", ex.Message).ToJson(id);
            }
        }

        private static BridgeResult Dispatch(Dictionary<string, string> request)
        {
            request.TryGetValue("cmd", out string cmd);
            switch (cmd)
            {
                case "ping":
                    return BridgeResult.Success(w => w
                        .Prop("protocol", ProtocolVersion)
                        .Prop("version", Plugin.Version)
                        .Prop("level", MainThread.Level)
                        .Prop("inLevel", MainThread.InLevel));

                case "state":
                    if (!GameUi.Ready) return BridgeResult.Fail("unavailable", "The game's table UI could not be hooked.");
                    return MainThread.Run(State, MainThreadTimeoutMs);

                case "query":
                    request.TryGetValue("sql", out string sql);
                    int maxRows = request.TryGetValue("maxRows", out string rawMax) && int.TryParse(rawMax, out int m) ? m : 0;
                    return LiveQuery.Run(sql, maxRows);

                case "save":
                    if (!QueryRunner.Ready) return BridgeResult.Fail("unavailable", "The game's search bar could not be hooked.");
                    request.TryGetValue("name", out string name);
                    request.TryGetValue("query", out string query);
                    return MainThread.Run(p => QueryRunner.Save(p, name, query), MainThreadTimeoutMs);

                case "drop":
                    if (!GameUi.HousekeepingReady) return BridgeResult.Fail("unavailable", "The game's delete code could not be hooked: " + GameUi.HousekeepingError);
                    request.TryGetValue("name", out string dropName);
                    return MainThread.Run(p => Housekeeping.Drop(p, dropName), MainThreadTimeoutMs);

                case "close_windows":
                    if (!GameUi.HousekeepingReady) return BridgeResult.Fail("unavailable", "The game's window code could not be hooked: " + GameUi.HousekeepingError);
                    return MainThread.Run(Housekeeping.CloseWindows, MainThreadTimeoutMs);

                default:
                    return BridgeResult.Fail("bad_request", "Unknown command: " + (cmd ?? "(none)"));
            }
        }

        private static IEnumerator State(MainThread.Pending pending)
        {
            int level = MainThread.Level;
            UnityEngine.Object button = MainThread.InLevel ? GameUi.FindQueryButton() : null;
            if (button == null)
            {
                pending.Result = BridgeResult.Success(w => w.Prop("level", level).Prop("inLevel", false));
                yield break;
            }

            List<string> tables = GameUi.TableNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            Dictionary<string, string> saved = GameUi.SavedQueries();
            int icons = GameUi.IconCount(button);
            bool busy = QueryRunner.Ready && QueryRunner.Busy;
            pending.Result = BridgeResult.Success(w =>
            {
                w.Prop("level", level).Prop("inLevel", true)
                    .Prop("iconCount", icons).Prop("iconLimit", IconLimit).Prop("busy", busy)
                    .BeginArray("tables");
                foreach (string table in tables)
                {
                    w.BeginObject().Prop("name", table).Prop("saved", saved.ContainsKey(table));
                    if (saved.TryGetValue(table, out string text)) w.Prop("query", text);
                    w.EndObject();
                }
                w.EndArray();
            });
        }

        /// <summary>Compares without stopping at the first difference, so timing does not leak how much of a guess was right.</summary>
        private static bool ConstantTimeEquals(string a, string b)
        {
            byte[] x = Encoding.UTF8.GetBytes(a ?? "");
            byte[] y = Encoding.UTF8.GetBytes(b ?? "");
            int diff = x.Length ^ y.Length;
            for (int i = 0; i < Math.Min(x.Length, y.Length); i++) diff |= x[i] ^ y[i];
            return diff == 0;
        }

        /// <summary>Reads newline-terminated UTF-8 lines and refuses to buffer more than a fixed number of bytes.</summary>
        private sealed class LineReader
        {
            private readonly Stream stream;
            private readonly int maxBytes;
            private readonly byte[] buffer = new byte[4096];
            private int start;
            private int end;

            public LineReader(Stream stream, int maxBytes)
            {
                this.stream = stream;
                this.maxBytes = maxBytes;
            }

            /// <summary>Returns the next line, or null when the peer closed the connection.</summary>
            public string ReadLine()
            {
                var line = new MemoryStream();
                while (true)
                {
                    if (start == end)
                    {
                        start = 0;
                        end = stream.Read(buffer, 0, buffer.Length);
                        if (end == 0) return null;
                    }

                    int newline = Array.IndexOf(buffer, (byte)'\n', start, end - start);
                    int take = (newline < 0 ? end : newline) - start;
                    if (line.Length + take > maxBytes) throw new IOException("Request line too long.");
                    line.Write(buffer, start, take);
                    start += take;

                    if (newline >= 0)
                    {
                        start++;
                        return Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length).TrimEnd('\r');
                    }
                }
            }
        }
    }
}
