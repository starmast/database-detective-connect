using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DDConnect
{
    /// <summary>
    /// Just enough JSON for the bridge protocol: a writer for replies (rows hold mixed value types,
    /// which Unity's JsonUtility cannot emit) and a parser for requests, which are flat objects.
    /// Written by hand so it works on any thread and adds no dependency.
    /// </summary>
    internal sealed class JsonWriter
    {
        private readonly StringBuilder sb = new StringBuilder();
        private readonly Stack<bool> needsComma = new Stack<bool>();

        public JsonWriter BeginObject(string name = null) => Open(name, '{');

        public JsonWriter EndObject() => Close('}');

        public JsonWriter BeginArray(string name = null) => Open(name, '[');

        public JsonWriter EndArray() => Close(']');

        public JsonWriter Prop(string name, string value) => Key(name).Text(value);

        public JsonWriter Prop(string name, long value) => Key(name).Raw(value.ToString(CultureInfo.InvariantCulture));

        public JsonWriter Prop(string name, bool value) => Key(name).Raw(value ? "true" : "false");

        public JsonWriter Elem(string value) => Separate().Text(value);

        public JsonWriter Elem(long value) => Separate().Raw(value.ToString(CultureInfo.InvariantCulture));

        public JsonWriter Elem(double value)
        {
            // JSON has no NaN or Infinity.
            if (double.IsNaN(value) || double.IsInfinity(value)) return ElemNull();
            return Separate().Raw(value.ToString("R", CultureInfo.InvariantCulture));
        }

        public JsonWriter ElemNull() => Separate().Raw("null");

        /// <summary>Inserts already-rendered members (<c>"a":1,"b":[]</c>, no braces) into the current object.</summary>
        public JsonWriter Fragment(string members)
        {
            Separate();
            sb.Append(members);
            return this;
        }

        public override string ToString() => sb.ToString();

        private JsonWriter Open(string name, char bracket)
        {
            if (name != null) Key(name); else Separate();
            sb.Append(bracket);
            needsComma.Push(false);
            return this;
        }

        private JsonWriter Close(char bracket)
        {
            needsComma.Pop();
            sb.Append(bracket);
            return this;
        }

        private JsonWriter Key(string name)
        {
            Separate();
            AppendString(name);
            sb.Append(':');
            return this;
        }

        private JsonWriter Separate()
        {
            if (needsComma.Count == 0) return this;
            if (needsComma.Pop()) sb.Append(',');
            needsComma.Push(true);
            return this;
        }

        private JsonWriter Text(string value)
        {
            if (value == null) return Raw("null");
            AppendString(value);
            return this;
        }

        private JsonWriter Raw(string token)
        {
            sb.Append(token);
            return this;
        }

        private void AppendString(string value)
        {
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }

    internal static class FlatJson
    {
        /// <summary>
        /// Parses one flat JSON object. Strings are unescaped; numbers, true, false and null are kept as their
        /// raw text. Returns null for anything else (nested values, trailing text, malformed input).
        /// </summary>
        public static Dictionary<string, string> Parse(string text)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            int i = 0;
            SkipSpace(text, ref i);
            if (i >= text.Length || text[i++] != '{') return null;
            SkipSpace(text, ref i);
            if (i < text.Length && text[i] == '}')
            {
                i++;
            }
            else
            {
                while (true)
                {
                    SkipSpace(text, ref i);
                    if (!ReadString(text, ref i, out string key)) return null;
                    SkipSpace(text, ref i);
                    if (i >= text.Length || text[i++] != ':') return null;
                    SkipSpace(text, ref i);
                    if (i >= text.Length) return null;

                    string value;
                    if (text[i] == '"')
                    {
                        if (!ReadString(text, ref i, out value)) return null;
                    }
                    else if (text[i] == '{' || text[i] == '[')
                    {
                        return null;
                    }
                    else
                    {
                        int start = i;
                        while (i < text.Length && text[i] != ',' && text[i] != '}' && !char.IsWhiteSpace(text[i])) i++;
                        value = text.Substring(start, i - start);
                        if (value.Length == 0) return null;
                    }
                    result[key] = value;

                    SkipSpace(text, ref i);
                    if (i >= text.Length) return null;
                    if (text[i] == ',') { i++; continue; }
                    if (text[i] == '}') { i++; break; }
                    return null;
                }
            }
            SkipSpace(text, ref i);
            return i == text.Length ? result : null;
        }

        private static void SkipSpace(string text, ref int i)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        }

        private static bool ReadString(string text, ref int i, out string value)
        {
            value = null;
            if (i >= text.Length || text[i] != '"') return false;
            i++;
            var sb = new StringBuilder();
            while (i < text.Length)
            {
                char c = text[i++];
                if (c == '"')
                {
                    value = sb.ToString();
                    return true;
                }
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }
                if (i >= text.Length) return false;
                char e = text[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > text.Length || !int.TryParse(text.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code)) return false;
                        sb.Append((char)code);
                        i += 4;
                        break;
                    default:
                        return false;
                }
            }
            return false;
        }
    }
}
