using DdSql;
using Xunit;

namespace DdSql.Tests
{
    public class OutputTests
    {
        private static QueryResult Sample(bool truncated = false) => new QueryResult(
            new[] { "id", "name" },
            new List<object?[]> { new object?[] { 1L, "Dana" }, new object?[] { 22L, null }, new object?[] { 3L, "a,\"b\"" } },
            truncated, 12, 3);

        private static string Render(QueryResult r, OutputFormat f, bool header = true)
        {
            var w = new StringWriter();
            Output.Write(r, f, header, w);
            return w.ToString().Replace("\r\n", "\n");
        }

        [Fact]
        public void TableAlignsColumnsAndShowsNull()
        {
            string text = Render(Sample(), OutputFormat.Table);
            string[] lines = text.Split('\n');
            Assert.Equal(" id | name", lines[0]);
            Assert.Equal("----+-------", lines[1]);
            Assert.Equal("  1 | Dana", lines[2]);
            Assert.Equal(" 22 | NULL", lines[3]);
            Assert.Contains("(3 rows, 12 ms)", text);
        }

        [Fact]
        public void TableSaysWhenTruncated() => Assert.Contains("--max-rows", Render(Sample(truncated: true), OutputFormat.Table));

        [Fact]
        public void JsonKeepsTypes()
        {
            string text = Render(Sample(), OutputFormat.Json);
            Assert.Contains("\"columns\":[\"id\",\"name\"]", text);
            Assert.Contains("[22,null]", text);
            Assert.Contains("\"rowCount\":3", text);
            Assert.Contains("\"truncated\":false", text);
        }

        [Fact]
        public void NdjsonIsOneObjectPerRow()
        {
            string[] lines = Render(Sample(), OutputFormat.Ndjson).TrimEnd('\n').Split('\n');
            Assert.Equal(3, lines.Length);
            Assert.Equal("{\"id\":1,\"name\":\"Dana\"}", lines[0]);
        }

        [Fact]
        public void NdjsonNumbersRepeatedColumnNames()
        {
            var r = new QueryResult(new[] { "id", "id" }, new List<object?[]> { new object?[] { 1L, 2L } }, false, 1, 1);
            Assert.Equal("{\"id\":1,\"id_2\":2}", Render(r, OutputFormat.Ndjson).Trim());
        }

        [Fact]
        public void CsvQuotesAndCanOmitHeader()
        {
            string text = Render(Sample(), OutputFormat.Csv, header: false);
            Assert.Equal("1,Dana\n22,\n3,\"a,\"\"b\"\"\"\n", text);
            Assert.StartsWith("id,name\n", Render(Sample(), OutputFormat.Csv));
        }

        [Fact]
        public void TerminalEscapesAndOverridesAreStripped()
        {
            var r = new QueryResult(new[] { "note" }, new List<object?[]> { new object?[] { "\u001b[31mred\u001b[0m‮evil\u0007\nline" } }, false, 1, 1);
            foreach (OutputFormat f in new[] { OutputFormat.Table, OutputFormat.Csv })
            {
                string text = Render(r, f);
                Assert.DoesNotContain('\u001b', text);
                Assert.DoesNotContain('‮', text);
                Assert.DoesNotContain('\u0007', text);
                Assert.Contains("[31mred[0mevil line", text);
            }
        }

        [Fact]
        public void LongCellsAreShortenedInTables()
        {
            var r = new QueryResult(new[] { "t" }, new List<object?[]> { new object?[] { new string('x', 300) } }, false, 1, 1);
            Assert.Contains("…", Render(r, OutputFormat.Table));
            Assert.DoesNotContain("…", Render(r, OutputFormat.Json));
        }
    }
}
