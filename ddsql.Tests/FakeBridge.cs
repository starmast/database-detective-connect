using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace DdSql.Tests
{
    /// <summary>
    /// A stand-in for the mod's bridge: listens on a loopback port, answers each request line with whatever
    /// the handler returns, and writes a bridge.json for the client to find.
    /// </summary>
    internal sealed class FakeBridge : IDisposable
    {
        public const string Token = "test-token";

        private readonly TcpListener listener;
        private readonly Func<JsonElement, string> handler;
        private readonly string directory;
        private readonly Task loop;

        public List<string> Requests { get; } = new List<string>();
        public string BridgeFile { get; }

        public FakeBridge(Func<JsonElement, string> handler)
        {
            this.handler = handler;
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            directory = Path.Combine(Path.GetTempPath(), "ddsql-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            BridgeFile = Path.Combine(directory, "bridge.json");
            File.WriteAllText(BridgeFile, $"{{\"version\":1,\"port\":{port},\"token\":\"{Token}\",\"pid\":0}}");

            loop = Task.Run(Serve);
        }

        public static string Ok(string members = "") => "{\"id\":1,\"ok\":true" + (members.Length > 0 ? "," + members : "") + "}";

        public static string Error(string code, string message) =>
            "{\"id\":1,\"ok\":false,\"error\":{\"code\":\"" + code + "\",\"message\":\"" + message + "\"}}";

        public static string QueryReply(string columnsJson, string rowsJson, bool truncated = false) =>
            Ok($"\"columns\":{columnsJson},\"rows\":{rowsJson},\"rowCount\":0,\"truncated\":{(truncated ? "true" : "false")},\"elapsedMs\":3,\"level\":2");

        private async Task Serve()
        {
            while (true)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync();
                }
                catch (Exception)
                {
                    return;
                }

                using (client)
                using (var reader = new StreamReader(client.GetStream(), new UTF8Encoding(false)))
                using (var writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" })
                {
                    try
                    {
                        string? line;
                        while ((line = await reader.ReadLineAsync()) != null)
                        {
                            lock (Requests) Requests.Add(line);
                            using JsonDocument doc = JsonDocument.Parse(line);
                            string reply = doc.RootElement.GetProperty("token").GetString() == Token
                                ? handler(doc.RootElement)
                                : Error("auth", "Missing or wrong token.");
                            await writer.WriteLineAsync(reply);
                        }
                    }
                    catch (IOException)
                    {
                    }
                }
            }
        }

        public void Dispose()
        {
            listener.Stop();
            try { loop.Wait(1000); } catch (AggregateException) { }
            try { Directory.Delete(directory, true); } catch (IOException) { }
        }
    }
}
