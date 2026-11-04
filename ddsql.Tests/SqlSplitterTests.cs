using DdSql;
using Xunit;

namespace DdSql.Tests
{
    public class SqlSplitterTests
    {
        [Fact]
        public void SplitsOnSemicolons()
        {
            List<string> statements = SqlSplitter.Split("SELECT 1; SELECT 2;", out string rest);
            Assert.Equal(new[] { "SELECT 1", "SELECT 2" }, statements);
            Assert.Equal("", rest);
        }

        [Fact]
        public void KeepsSemicolonsInsideQuotes()
        {
            List<string> statements = SqlSplitter.SplitAll("SELECT 'a;b', \"c;d\", [e;f], `g;h`; SELECT 2");
            Assert.Equal(2, statements.Count);
            Assert.Equal("SELECT 'a;b', \"c;d\", [e;f], `g;h`", statements[0]);
        }

        [Fact]
        public void HandlesEscapedQuotes()
        {
            List<string> statements = SqlSplitter.SplitAll("SELECT 'it''s; fine'; SELECT 2");
            Assert.Equal(new[] { "SELECT 'it''s; fine'", "SELECT 2" }, statements);
        }

        [Fact]
        public void IgnoresSemicolonsInComments()
        {
            List<string> statements = SqlSplitter.SplitAll("SELECT 1 -- not; here\n; /* nor; here */ SELECT 2");
            Assert.Equal(2, statements.Count);
            Assert.StartsWith("SELECT 1", statements[0]);
            Assert.EndsWith("SELECT 2", statements[1]);
        }

        [Fact]
        public void DropsStatementsThatAreOnlyComments()
        {
            Assert.Empty(SqlSplitter.SplitAll("-- nothing\n; /* still nothing */ ;  ;"));
        }

        [Fact]
        public void UnfinishedStatementIsTheRemainder()
        {
            List<string> statements = SqlSplitter.Split("SELECT 1; SELECT\n  2", out string rest);
            Assert.Single(statements);
            Assert.Equal("SELECT\n  2", rest);
        }

        [Fact]
        public void UnterminatedQuoteKeepsWaitingForMore()
        {
            List<string> statements = SqlSplitter.Split("SELECT 'abc; def", out string rest);
            Assert.Empty(statements);
            Assert.Equal("SELECT 'abc; def", rest);
        }

        [Fact]
        public void SplitAllCountsAnUnfinishedLastStatement()
        {
            Assert.Equal(new[] { "SELECT 1", "SELECT 2" }, SqlSplitter.SplitAll("SELECT 1; SELECT 2"));
        }
    }
}
