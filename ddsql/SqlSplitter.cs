using System.Text;

namespace DdSql
{
    /// <summary>Splits SQL text into statements on semicolons, ignoring ones inside quotes, brackets and comments.</summary>
    internal static class SqlSplitter
    {
        /// <summary>
        /// Returns the statements that are finished (ended by a semicolon). <paramref name="remainder"/> is whatever
        /// follows the last semicolon, or an empty string when that is only whitespace and comments. The prompt uses
        /// it to keep reading lines until a statement is complete.
        /// </summary>
        public static List<string> Split(string text, out string remainder)
        {
            var statements = new List<string>();
            var current = new StringBuilder();
            bool hasContent = false;
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                char next = i + 1 < text.Length ? text[i + 1] : '\0';

                if (c == '\'' || c == '"' || c == '`')
                {
                    i = CopyQuoted(text, i, c, current);
                    hasContent = true;
                }
                else if (c == '[')
                {
                    int end = text.IndexOf(']', i);
                    int stop = end < 0 ? text.Length : end + 1;
                    current.Append(text, i, stop - i);
                    i = stop;
                    hasContent = true;
                }
                else if (c == '-' && next == '-')
                {
                    int end = text.IndexOf('\n', i);
                    int stop = end < 0 ? text.Length : end;
                    current.Append(text, i, stop - i);
                    i = stop;
                }
                else if (c == '/' && next == '*')
                {
                    int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    int stop = end < 0 ? text.Length : end + 2;
                    current.Append(text, i, stop - i);
                    i = stop;
                }
                else if (c == ';')
                {
                    if (hasContent) statements.Add(current.ToString().Trim());
                    current.Clear();
                    hasContent = false;
                    i++;
                }
                else
                {
                    current.Append(c);
                    if (!char.IsWhiteSpace(c)) hasContent = true;
                    i++;
                }
            }

            remainder = hasContent ? current.ToString().Trim() : "";
            return statements;
        }

        /// <summary>All statements in the text, counting an unfinished last one (no closing semicolon) as a statement.</summary>
        public static List<string> SplitAll(string text)
        {
            List<string> statements = Split(text, out string remainder);
            if (remainder.Length > 0) statements.Add(remainder);
            return statements;
        }

        /// <summary>Copies a quoted run starting at <paramref name="start"/>; a doubled quote character is an escaped one.</summary>
        private static int CopyQuoted(string text, int start, char quote, StringBuilder into)
        {
            into.Append(quote);
            int i = start + 1;
            while (i < text.Length)
            {
                char c = text[i];
                into.Append(c);
                i++;
                if (c != quote) continue;
                if (i < text.Length && text[i] == quote)
                {
                    into.Append(quote);
                    i++;
                    continue;
                }
                break;
            }
            return i;
        }
    }
}
