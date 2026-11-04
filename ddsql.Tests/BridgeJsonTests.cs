using DDConnect;
using Xunit;

namespace DdSql.Tests
{
    /// <summary>Tests the mod's JSON helpers (BridgeJson.cs is compiled into this project).</summary>
    public class BridgeJsonTests
    {
        [Fact]
        public void WriterPlacesCommasAndNesting()
        {
            string json = new JsonWriter().BeginObject()
                .Prop("a", 1).Prop("b", "x").Prop("c", true)
                .BeginArray("d").Elem(1).Elem("two").ElemNull().Elem(2.5).EndArray()
                .BeginObject("e").Prop("f", 3).EndObject()
                .BeginArray("g").BeginArray().Elem(1).Elem(2).EndArray().BeginArray().EndArray().EndArray()
                .EndObject().ToString();
            Assert.Equal("{\"a\":1,\"b\":\"x\",\"c\":true,\"d\":[1,\"two\",null,2.5],\"e\":{\"f\":3},\"g\":[[1,2],[]]}", json);
        }

        [Fact]
        public void WriterEscapesStrings()
        {
            string json = new JsonWriter().BeginObject().Prop("s", "q\"b\\n\n\t\u0001").EndObject().ToString();
            Assert.Equal("{\"s\":\"q\\\"b\\\\n\\n\\t\\u0001\"}", json);
        }

        [Fact]
        public void WriterTurnsNaNIntoNull()
        {
            Assert.Equal("[null]", new JsonWriter().BeginArray().Elem(double.NaN).EndArray().ToString());
        }

        [Fact]
        public void FragmentSplicesIntoAnObject()
        {
            string json = new JsonWriter().BeginObject().Prop("a", 1).Fragment("\"b\":[1],\"c\":2").Prop("d", 4).EndObject().ToString();
            Assert.Equal("{\"a\":1,\"b\":[1],\"c\":2,\"d\":4}", json);
        }

        [Fact]
        public void ParserReadsFlatObjects()
        {
            Dictionary<string, string>? r = FlatJson.Parse("{\"id\": 7, \"token\":\"abc\", \"cmd\":\"query\", \"sql\":\"SELECT \\\"x\\\"\\nFROM t\", \"maxRows\":10, \"n\":null}");
            Assert.NotNull(r);
            Assert.Equal("7", r!["id"]);
            Assert.Equal("SELECT \"x\"\nFROM t", r["sql"]);
            Assert.Equal("10", r["maxRows"]);
            Assert.Equal("null", r["n"]);
        }

        [Fact]
        public void ParserHandlesUnicodeEscapes()
        {
            Assert.Equal("é", FlatJson.Parse("{\"s\":\"\\u00e9\"}")!["s"]);
        }

        [Theory]
        [InlineData("")]
        [InlineData("GET / HTTP/1.1")]
        [InlineData("[1,2]")]
        [InlineData("{\"a\":{\"b\":1}}")]
        [InlineData("{\"a\":[1]}")]
        [InlineData("{\"a\":1} trailing")]
        [InlineData("{\"a\":}")]
        [InlineData("{\"a\":\"unterminated}")]
        [InlineData("{\"a\":1,}")]
        [InlineData("{\"a\":\"\\q\"}")]
        public void ParserRejectsAnythingElse(string line) => Assert.Null(FlatJson.Parse(line));

        [Fact]
        public void EmptyObjectIsFine() => Assert.Empty(FlatJson.Parse("{ }")!);
    }
}
