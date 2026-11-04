using System.Text.Json;
using DdSql;
using Xunit;

namespace DdSql.Tests
{
    /// <summary>Runs ddsql end to end against a fake bridge: arguments in, exit code and output out.</summary>
    public class ProgramTests
    {
        private sealed record Outcome(int Exit, string Out, string Err);

        private static Outcome RunCli(FakeBridge? bridge, string stdin, bool redirected, params string[] args)
        {
            var all = new List<string>();
            if (bridge != null) all.AddRange(new[] { "--connection", bridge.BridgeFile });
            all.AddRange(args);

            var stdout = new StringWriter();
            var stderr = new StringWriter();
            int exit = Program.Run(all.ToArray(), new StringReader(stdin), stdout, stderr, redirected);
            return new Outcome(exit, stdout.ToString().Replace("\r\n", "\n"), stderr.ToString().Replace("\r\n", "\n"));
        }

        private static string Cmd(JsonElement request) => request.GetProperty("cmd").GetString()!;

        [Fact]
        public void QueryPrintsJsonWhenOutputIsPiped()
        {
            using var bridge = new FakeBridge(r => FakeBridge.QueryReply("[\"n\"]", "[[42]]"));
            Outcome o = RunCli(bridge, "", redirected: true, "-q", "SELECT 42 AS n");
            Assert.Equal(0, o.Exit);
            Assert.Contains("\"rows\":[[42]]", o.Out);
        }

        [Fact]
        public void QueryPrintsATableInATerminal()
        {
            using var bridge = new FakeBridge(r => FakeBridge.QueryReply("[\"n\"]", "[[42]]"));
            Outcome o = RunCli(bridge, "", redirected: false, "-q", "SELECT 42 AS n");
            Assert.Equal(0, o.Exit);
            Assert.StartsWith(" n", o.Out);
            Assert.Contains("(1 row", o.Out);
        }

        [Fact]
        public void ARefusedQueryExitsWithOne()
        {
            using var bridge = new FakeBridge(r => FakeBridge.Error("read_only", "This statement would change data."));
            Outcome o = RunCli(bridge, "", true, "-q", "DELETE FROM vehicles");
            Assert.Equal(ExitCodes.Failed, o.Exit);
            Assert.Contains("would change data", o.Err);
        }

        [Fact]
        public void CreateTableWithoutSaveGetsAHintAndNeverSavesImplicitly()
        {
            using var bridge = new FakeBridge(r => FakeBridge.Error("read_only", "Only read-only queries are allowed here."));
            Outcome o = RunCli(bridge, "", true, "-q", "CREATE TABLE x AS SELECT 1");
            Assert.Equal(ExitCodes.Failed, o.Exit);
            Assert.Contains("--save NAME", o.Err);
            Assert.DoesNotContain(bridge.Requests, line => line.Contains("\"cmd\":\"save\""));
        }

        [Fact]
        public void SaveSendsTheSelectAsASaveCommand()
        {
            using var bridge = new FakeBridge(r => Cmd(r) == "save" ? FakeBridge.Ok("\"table\":\"red\",\"message\":\"Saved as table red.\"") : FakeBridge.Error("bad_request", "?"));
            Outcome o = RunCli(bridge, "", false, "-q", "SELECT * FROM vehicles;", "--save", "red");
            Assert.Equal(0, o.Exit);
            Assert.Contains("Saved as table red.", o.Out);
            string request = Assert.Single(bridge.Requests);
            Assert.Contains("\"name\":\"red\"", request);
            Assert.Contains("\"query\":\"SELECT * FROM vehicles\"", request);
        }

        [Fact]
        public void SaveRefusedByTheGameIsAnError()
        {
            using var bridge = new FakeBridge(r => FakeBridge.Error("game_refused", "Table name already used."));
            Outcome o = RunCli(bridge, "", true, "-q", "SELECT 1", "--save", "dup");
            Assert.Equal(ExitCodes.Failed, o.Exit);
            Assert.Contains("already used", o.Err);
        }

        [Fact]
        public void DropIsAnExplicitCommandPerTable()
        {
            using var bridge = new FakeBridge(r => Cmd(r) == "drop" ? FakeBridge.Ok("\"table\":\"red\",\"message\":\"Deleted table red.\"") : FakeBridge.Error("bad_request", "?"));
            Outcome o = RunCli(bridge, "", false, "drop", "red", "blue");
            Assert.Equal(0, o.Exit);
            Assert.Contains("Deleted table red.", o.Out);
            Assert.Equal(2, bridge.Requests.Count);
            Assert.Contains("\"name\":\"blue\"", bridge.Requests[1]);
        }

        [Fact]
        public void DropRefusedByTheGameIsAnErrorAndStopsTheRest()
        {
            using var bridge = new FakeBridge(r => FakeBridge.Error("game_refused", "The game does not let you delete crops; it is part of the case."));
            Outcome o = RunCli(bridge, "", true, "drop", "crops", "red");
            Assert.Equal(ExitCodes.Failed, o.Exit);
            Assert.Contains("part of the case", o.Err);
            Assert.Single(bridge.Requests);
        }

        [Fact]
        public void DropNeedsATableName()
        {
            using var bridge = new FakeBridge(r => FakeBridge.Ok());
            Outcome o = RunCli(bridge, "", true, "drop");
            Assert.Equal(ExitCodes.Usage, o.Exit);
            Assert.Empty(bridge.Requests);
        }

        [Fact]
        public void DropTableSqlIsStillRefusedAndPointsToDrop()
        {
            using var bridge = new FakeBridge(r => FakeBridge.Error("read_only", "Only read-only queries are allowed here."));
            Outcome o = RunCli(bridge, "", true, "-q", "DROP TABLE red");
            Assert.Equal(ExitCodes.Failed, o.Exit);
            Assert.Contains("ddsql drop NAME", o.Err);
            Assert.DoesNotContain(bridge.Requests, line => line.Contains("\"cmd\":\"drop\""));
        }

        [Fact]
        public void TablesListsThemWithAReadOnlyQuery()
        {
            using var bridge = new FakeBridge(r => FakeBridge.QueryReply("[\"name\"]", "[[\"crops\"]]"));
            Outcome o = RunCli(bridge, "", true, "tables");
            Assert.Equal(0, o.Exit);
            Assert.Contains("\"crops\"", o.Out);
            Assert.Contains("sqlite_master", bridge.Requests[0]);
        }

        [Fact]
        public void SchemaPrintsTheDefinitionAsTextInATerminalAndColumnsWhenPiped()
        {
            using var bridge = new FakeBridge(r => r.GetProperty("sql").GetString()!.StartsWith("SELECT name, sql")
                ? FakeBridge.QueryReply("[\"name\",\"sql\"]", "[[\"crops\",\"CREATE TABLE crops (id INT)\"]]")
                : FakeBridge.QueryReply("[\"sql\"]", "[[\"CREATE TABLE crops (id INT)\"]]"));
            Outcome terminal = RunCli(bridge, "", false, "schema", "crops");
            Assert.Contains("CREATE TABLE crops (id INT)\n;", terminal.Out);
            Outcome piped = RunCli(bridge, "", true, "schema", "crops");
            Assert.Contains("\"columns\":[\"name\",\"sql\"]", piped.Out.Replace(" ", ""));
        }

        [Fact]
        public void SchemaRejectsAnythingButATableName()
        {
            using var bridge = new FakeBridge(r => FakeBridge.Ok());
            Outcome o = RunCli(bridge, "", true, "schema", "x'; DROP TABLE y");
            Assert.Equal(ExitCodes.Usage, o.Exit);
            Assert.Empty(bridge.Requests);
        }

        [Fact]
        public void WaitGivesUpWithExitThreeWhenTheGameNeverAppears()
        {
            string missing = Path.Combine(Path.GetTempPath(), "ddsql-missing-" + Guid.NewGuid().ToString("N"), "bridge.json");
            var timer = System.Diagnostics.Stopwatch.StartNew();
            Outcome o = RunCli(null, "", true, "--connection", missing, "--wait", "1", "connection", "test");
            Assert.Equal(ExitCodes.Unreachable, o.Exit);
            Assert.True(timer.ElapsedMilliseconds >= 900, "should have kept trying for about a second");
        }

        [Fact]
        public void WaitConnectsOnceTheGameAppears()
        {
            using var bridge = new FakeBridge(r => FakeBridge.Ok("\"protocol\":1,\"version\":\"0.1.1\",\"level\":3,\"inLevel\":true"));
            string late = Path.Combine(Path.GetTempPath(), "ddsql-late-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                Task.Run(async () =>
                {
                    await Task.Delay(700);
                    File.Copy(bridge.BridgeFile, late);
                });
                Outcome o = RunCli(null, "", false, "--connection", late, "--wait", "10", "connection", "test");
                Assert.Equal(0, o.Exit);
                Assert.Contains("Connected to DD Connect", o.Out);
            }
            finally
            {
                File.Delete(late);
            }
        }

        [Fact]
        public void WaitNeedsASensibleNumber()
        {
            Assert.Equal(ExitCodes.Usage, RunCli(null, "", true, "--wait", "0", "state").Exit);
        }

        [Fact]
        public void CloseWindowsReportsHowManyWereClosed()
        {
            using var bridge = new FakeBridge(r => Cmd(r) == "close_windows" ? FakeBridge.Ok("\"closed\":12") : FakeBridge.Error("bad_request", "?"));
            Assert.Contains("Closed 12 windows.", RunCli(bridge, "", false, "close-windows").Out);
            Assert.Contains("\"closed\":12", RunCli(bridge, "", true, "close-windows").Out);
        }

        [Fact]
        public void SaveNeedsExactlyOneStatement()
        {
            using var bridge = new FakeBridge(r => FakeBridge.Ok());
            Outcome o = RunCli(bridge, "", true, "-q", "SELECT 1; SELECT 2", "--save", "x");
            Assert.Equal(ExitCodes.Usage, o.Exit);
            Assert.Empty(bridge.Requests);
        }

        [Fact]
        public void FileAndStdinRunEveryStatementInOrder()
        {
            using var bridge = new FakeBridge(r => FakeBridge.QueryReply("[\"n\"]", "[[1]]"));
            Outcome o = RunCli(bridge, "SELECT 1; SELECT 2;", true, "-");
            Assert.Equal(0, o.Exit);
            Assert.Equal(2, bridge.Requests.Count);
            Assert.Contains("SELECT 1", bridge.Requests[0]);
            Assert.Contains("SELECT 2", bridge.Requests[1]);
        }

        [Fact]
        public void ExecutionStopsAtTheFirstFailure()
        {
            using var bridge = new FakeBridge(r => FakeBridge.Error("sql_error", "no such table: nope"));
            Outcome o = RunCli(bridge, "SELECT * FROM nope; SELECT 2;", true, "-");
            Assert.Equal(ExitCodes.Failed, o.Exit);
            Assert.Single(bridge.Requests);
        }

        [Fact]
        public void WrongTokenExitsWithFour()
        {
            using var bridge = new FakeBridge(r => FakeBridge.Ok());
            File.WriteAllText(bridge.BridgeFile, File.ReadAllText(bridge.BridgeFile).Replace(FakeBridge.Token, "wrong"));
            Outcome o = RunCli(bridge, "", true, "-q", "SELECT 1");
            Assert.Equal(ExitCodes.Auth, o.Exit);
        }

        [Fact]
        public void MissingBridgeFileExitsWithThreeAndSaysHowToEnableIt()
        {
            string missing = Path.Combine(Path.GetTempPath(), "ddsql-missing-" + Guid.NewGuid().ToString("N"), "bridge.json");
            Outcome o = RunCli(null, "", true, "--connection", missing, "-q", "SELECT 1");
            Assert.Equal(ExitCodes.Unreachable, o.Exit);
            Assert.Contains("[Bridge]", o.Err);
        }

        [Fact]
        public void NothingListeningExitsWithThree()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ddsql-dead-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "bridge.json");
            // Port 1 is never open on a normal machine.
            File.WriteAllText(file, "{\"version\":1,\"port\":1,\"token\":\"t\",\"pid\":0}");
            try
            {
                Outcome o = RunCli(null, "", true, "--connection", file, "-q", "SELECT 1");
                Assert.Equal(ExitCodes.Unreachable, o.Exit);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void ConnectionTestReportsTheGame()
        {
            using var bridge = new FakeBridge(r => FakeBridge.Ok("\"protocol\":1,\"version\":\"0.1.1\",\"level\":3,\"inLevel\":true"));
            Outcome o = RunCli(bridge, "", false, "connection", "test");
            Assert.Equal(0, o.Exit);
            Assert.Contains("DD Connect 0.1.1", o.Out);
            Assert.Contains("Level 3", o.Out);
        }

        [Fact]
        public void MissingSqlFileIsAUsageError()
        {
            Outcome o = RunCli(null, "", true, "-f", Path.Combine(Path.GetTempPath(), "does-not-exist.sql"));
            Assert.Equal(ExitCodes.Usage, o.Exit);
        }

        [Fact]
        public void PromptRunsStatementsDotCommandsAndRecoversFromErrors()
        {
            using var bridge = new FakeBridge(r => Cmd(r) switch
            {
                "ping" => FakeBridge.Ok("\"protocol\":1,\"version\":\"0.1.1\",\"level\":2,\"inLevel\":true"),
                "query" => r.GetProperty("sql").GetString()!.Contains("bad")
                    ? FakeBridge.Error("sql_error", "no such table: bad")
                    : FakeBridge.QueryReply("[\"n\"]", "[[7]]"),
                "save" => FakeBridge.Ok("\"table\":\"seven\",\"message\":\"Saved as table seven.\""),
                _ => FakeBridge.Error("bad_request", "?"),
            });
            const string session = "SELECT 7\n  AS n;\nSELECT * FROM bad;\n.save seven\n.format json\nSELECT 7;\n.quit\n";
            Outcome o = RunCli(bridge, session, false);
            Assert.Equal(0, o.Exit);
            Assert.Contains("ddsql [Level 2]> ", o.Out);
            Assert.Contains("...> ", o.Out);
            Assert.Contains("no such table: bad", o.Err);
            Assert.Contains("Saved as table seven.", o.Out);
            Assert.Contains("\"rows\":[[7]]", o.Out);
        }

        [Fact]
        public void SaveInThePromptNeedsAPriorSelect()
        {
            using var bridge = new FakeBridge(r => Cmd(r) == "ping"
                ? FakeBridge.Ok("\"protocol\":1,\"version\":\"0.1.1\",\"level\":2,\"inLevel\":true")
                : FakeBridge.Ok());
            Outcome o = RunCli(bridge, ".save x\n.quit\n", false);
            Assert.Contains("Run a SELECT first", o.Err);
            Assert.DoesNotContain(bridge.Requests, line => line.Contains("\"cmd\":\"save\""));
        }

        [Fact]
        public void HelpAndVersionNeedNoGame()
        {
            Assert.Equal(0, RunCli(null, "", false, "--help").Exit);
            Assert.StartsWith("ddsql ", RunCli(null, "", false, "--version").Out);
        }
    }
}
