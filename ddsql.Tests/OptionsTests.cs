using DdSql;
using Xunit;

namespace DdSql.Tests
{
    public class OptionsTests
    {
        [Fact]
        public void NoArgumentsOpensThePrompt() => Assert.Equal(Mode.Repl, Options.Parse(Array.Empty<string>()).Mode);

        [Fact]
        public void QueryAndFormat()
        {
            Options o = Options.Parse(new[] { "-q", "SELECT 1", "--format", "csv", "--max-rows", "5", "--no-header" });
            Assert.Equal(Mode.Query, o.Mode);
            Assert.Equal("SELECT 1", o.Sql);
            Assert.Equal(OutputFormat.Csv, o.Format);
            Assert.Equal(5, o.MaxRows);
            Assert.False(o.Header);
        }

        [Fact]
        public void LongOptionsAcceptEquals()
        {
            Options o = Options.Parse(new[] { "--query=SELECT 1", "--format=json" });
            Assert.Equal("SELECT 1", o.Sql);
            Assert.Equal(OutputFormat.Json, o.Format);
        }

        [Fact]
        public void DashReadsStandardInput() => Assert.Equal(Mode.Stdin, Options.Parse(new[] { "-" }).Mode);

        [Fact]
        public void ConnectionTestAndState()
        {
            Assert.Equal(Mode.ConnectionTest, Options.Parse(new[] { "connection", "test" }).Mode);
            Assert.Equal(Mode.State, Options.Parse(new[] { "state" }).Mode);
        }

        [Fact]
        public void SaveNeedsAQuery()
        {
            DdSqlException ex = Assert.Throws<DdSqlException>(() => Options.Parse(new[] { "--save", "x" }));
            Assert.Equal(ExitCodes.Usage, ex.ExitCode);
            Assert.Equal("x", Options.Parse(new[] { "-q", "SELECT 1", "--save", "x" }).SaveName);
        }

        [Theory]
        [InlineData("-q")]
        [InlineData("--max-rows", "0")]
        [InlineData("--max-rows", "50001")]
        [InlineData("--format", "xml")]
        [InlineData("--nope")]
        [InlineData("connection")]
        public void BadArgumentsAreUsageErrors(params string[] args)
        {
            Assert.Equal(ExitCodes.Usage, Assert.Throws<DdSqlException>(() => Options.Parse(args)).ExitCode);
        }

        [Fact]
        public void OnlyOneModeAtATime()
        {
            Assert.Throws<DdSqlException>(() => Options.Parse(new[] { "-q", "SELECT 1", "-f", "x.sql" }));
            Assert.Throws<DdSqlException>(() => Options.Parse(new[] { "state", "-" }));
        }
    }
}
