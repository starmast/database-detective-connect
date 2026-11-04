using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DdSql
{
    internal enum OutputFormat
    {
        Table,
        Json,
        Ndjson,
        Csv,
    }

    /// <summary>Renders query results. Anything that comes from the game is treated as untrusted text.</summary>
    internal static class Output
    {
        private const int MaxCellWidth = 80;

        public static void Write(QueryResult result, OutputFormat format, bool header, TextWriter w)
        {
            switch (format)
            {
                case OutputFormat.Json: WriteJson(result, w); break;
                case OutputFormat.Ndjson: WriteNdjson(result, w); break;
                case OutputFormat.Csv: WriteCsv(result, header, w); break;
                default: WriteTable(result, header, w); break;
            }
        }

        public static bool TryParseFormat(string text, out OutputFormat format)
        {
            switch (text.ToLowerInvariant())
            {
                case "table": format = OutputFormat.Table; return true;
                case "json": format = OutputFormat.Json; return true;
                case "ndjson": format = OutputFormat.Ndjson; return true;
                case "csv": format = OutputFormat.Csv; return true;
                default: format = OutputFormat.Table; return false;
            }
        }

        /// <summary>
        /// Makes text safe to print: line breaks and tabs become spaces, and other control characters (which include
        /// terminal escape sequences) and text-direction overrides are dropped.
        /// </summary>
        public static string Clean(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (c is '\n' or '\r' or '\t') sb.Append(' ');
                else if (char.IsControl(c)) continue;
                else if ((c >= '‪' && c <= '‮') || (c >= '⁦' && c <= '⁩')) continue;
                else sb.Append(c);
            }
            return sb.ToString();
        }

        internal static string Format(object? value) => value switch
        {
            null => "NULL",
            long l => l.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            string s => Clean(s),
            _ => Clean(value.ToString() ?? ""),
        };

        private static void WriteTable(QueryResult r, bool header, TextWriter w)
        {
            int columns = r.Columns.Count;
            var cells = r.Rows.Select(row => row.Select(v => Shorten(Format(v))).ToArray()).ToList();
            var names = r.Columns.Select(c => Shorten(Clean(c))).ToArray();
            var widths = new int[columns];
            for (int c = 0; c < columns; c++)
            {
                widths[c] = header ? names[c].Length : 1;
                foreach (string[] row in cells) widths[c] = Math.Max(widths[c], row[c].Length);
            }

            if (header && columns > 0)
            {
                w.WriteLine(" " + string.Join(" | ", names.Select((n, c) => n.PadRight(widths[c]))).TrimEnd());
                w.WriteLine("-" + string.Join("-+-", widths.Select(width => new string('-', width))) + "-");
            }
            for (int r0 = 0; r0 < cells.Count; r0++)
            {
                var parts = new string[columns];
                for (int c = 0; c < columns; c++)
                {
                    bool numeric = r.Rows[r0][c] is long or double;
                    parts[c] = numeric ? cells[r0][c].PadLeft(widths[c]) : cells[r0][c].PadRight(widths[c]);
                }
                w.WriteLine((" " + string.Join(" | ", parts)).TrimEnd());
            }

            string rows = r.Rows.Count == 1 ? "1 row" : $"{r.Rows.Count} rows";
            w.WriteLine($"({rows}, {r.ElapsedMs} ms)" + (r.Truncated ? $" Stopped at {r.Rows.Count} rows; use --max-rows to see more." : ""));
        }

        private static string Shorten(string text) => text.Length <= MaxCellWidth ? text : text.Substring(0, MaxCellWidth - 1) + "…";

        private static void WriteJson(QueryResult r, TextWriter w)
        {
            using var stream = new MemoryStream();
            using (var j = new Utf8JsonWriter(stream))
            {
                j.WriteStartObject();
                j.WriteStartArray("columns");
                foreach (string c in r.Columns) j.WriteStringValue(c);
                j.WriteEndArray();
                j.WriteStartArray("rows");
                foreach (object?[] row in r.Rows)
                {
                    j.WriteStartArray();
                    foreach (object? v in row) WriteValue(j, v);
                    j.WriteEndArray();
                }
                j.WriteEndArray();
                j.WriteNumber("rowCount", r.Rows.Count);
                j.WriteBoolean("truncated", r.Truncated);
                j.WriteNumber("elapsedMs", r.ElapsedMs);
                j.WriteNumber("level", r.Level);
                j.WriteEndObject();
            }
            w.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
        }

        private static void WriteNdjson(QueryResult r, TextWriter w)
        {
            string[] keys = UniqueNames(r.Columns);
            foreach (object?[] row in r.Rows)
            {
                using var stream = new MemoryStream();
                using (var j = new Utf8JsonWriter(stream))
                {
                    j.WriteStartObject();
                    for (int c = 0; c < keys.Length; c++)
                    {
                        j.WritePropertyName(keys[c]);
                        WriteValue(j, row[c]);
                    }
                    j.WriteEndObject();
                }
                w.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
            }
        }

        private static void WriteCsv(QueryResult r, bool header, TextWriter w)
        {
            if (header) w.WriteLine(string.Join(",", r.Columns.Select(c => CsvField(Clean(c)))));
            foreach (object?[] row in r.Rows)
            {
                w.WriteLine(string.Join(",", row.Select(v => v == null ? "" : CsvField(Format(v)))));
            }
        }

        private static string CsvField(string text) =>
            text.IndexOfAny(new[] { ',', '"' }) >= 0 || text.Length != text.Trim().Length
                ? "\"" + text.Replace("\"", "\"\"") + "\""
                : text;

        private static void WriteValue(Utf8JsonWriter j, object? v)
        {
            switch (v)
            {
                case null: j.WriteNullValue(); break;
                case long l: j.WriteNumberValue(l); break;
                case double d: j.WriteNumberValue(d); break;
                default: j.WriteStringValue(v.ToString()); break;
            }
        }

        /// <summary>Column names can repeat (SELECT a.id, b.id); keep every value by numbering the repeats.</summary>
        internal static string[] UniqueNames(IReadOnlyList<string> names)
        {
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            var result = new string[names.Count];
            for (int i = 0; i < names.Count; i++)
            {
                seen.TryGetValue(names[i], out int count);
                seen[names[i]] = count + 1;
                result[i] = count == 0 ? names[i] : $"{names[i]}_{count + 1}";
            }
            return result;
        }
    }
}
