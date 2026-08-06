namespace HOPPER.Application.ModMetadata
{
    public static class ModsTomlParser
    {
        public static string[] Parse(string text)
        {
            if (string.IsNullOrEmpty(text))
                return [];

            var ids = new List<string>();

            var currentTable = string.Empty;

            var multiline = false;
            var multilineDelimiter = string.Empty;

            var inlineDepth = 0;

            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');

                if (multiline)
                {
                    if (line.Contains(multilineDelimiter, StringComparison.Ordinal))
                        multiline = false;
                    continue;
                }

                var tripleAt = IndexOfTripleQuote(line, out var delimiter);
                if (tripleAt >= 0)
                {
                    if (Occurrences(line, delimiter) % 2 == 1)
                    {
                        multiline = true;
                        multilineDelimiter = delimiter;
                    }

                    continue;
                }

                line = line.Trim();

                if (line.Length == 0 || line[0] == '#')
                    continue;

                if (inlineDepth > 0)
                {
                    var body = StripComment(line);
                    CollectInline(body, ids);
                    inlineDepth += BracketDelta(body);
                    continue;
                }

                if (line[0] == '[')
                {
                    currentTable = TableName(line);
                    continue;
                }

                var stripped = StripComment(line);
                var equals = IndexOfEqualsOutsideString(stripped);
                if (equals < 0)
                    continue;

                var key = stripped[..equals].Trim();
                var value = stripped[(equals + 1)..].Trim();

                if (currentTable.Length == 0
                    && string.Equals(key, "mods", StringComparison.Ordinal)
                    && value.StartsWith('['))
                {
                    CollectInline(value, ids);
                    inlineDepth += BracketDelta(value);
                    continue;
                }

                if (string.Equals(currentTable, "mods", StringComparison.Ordinal)
                    && string.Equals(key, "modId", StringComparison.Ordinal))
                {
                    Add(ids, Unquote(value));
                }
            }

            return [.. ids];
        }

        private static string TableName(string line)
        {
            if (line.StartsWith("[[", StringComparison.Ordinal))
            {
                var end = line.IndexOf("]]", 2, StringComparison.Ordinal);
                return end < 0 ? string.Empty : Unquote(line[2..end].Trim());
            }

            var close = line.IndexOf(']', 1);
            return close < 0 ? string.Empty : Unquote(line[1..close].Trim());
        }

        private static void CollectInline(string fragment, List<string> ids)
        {
            var start = 0;
            var inString = false;
            var quote = '\0';

            for (var i = 0; i <= fragment.Length; i++)
            {
                if (i == fragment.Length)
                {
                    InlinePiece(fragment[start..], ids);
                    break;
                }

                var c = fragment[i];

                if (inString)
                {
                    if (quote == '"' && c == '\\')
                    {
                        i++;
                        continue;
                    }

                    if (c == quote)
                        inString = false;

                    continue;
                }

                if (c is '"' or '\'')
                {
                    inString = true;
                    quote = c;
                    continue;
                }

                if (c is '{' or '}' or ',' or '[' or ']')
                {
                    InlinePiece(fragment[start..i], ids);
                    start = i + 1;
                }
            }
        }

        private static void InlinePiece(string piece, List<string> ids)
        {
            var equals = IndexOfEqualsOutsideString(piece);
            if (equals < 0)
                return;

            if (string.Equals(piece[..equals].Trim(), "modId", StringComparison.Ordinal))
                Add(ids, Unquote(piece[(equals + 1)..].Trim()));
        }

        private static void Add(List<string> ids, string? id)
        {
            if (id is not null && ModIdReader.IsValidModId(id) && !ids.Contains(id, StringComparer.Ordinal))
                ids.Add(id);
        }

        private static string StripComment(string line)
        {
            var inString = false;
            var quote = '\0';

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];

                if (inString)
                {
                    if (quote == '"' && c == '\\')
                    {
                        i++;
                        continue;
                    }

                    if (c == quote)
                        inString = false;

                    continue;
                }

                if (c is '"' or '\'')
                {
                    inString = true;
                    quote = c;
                    continue;
                }

                if (c == '#')
                    return line[..i].TrimEnd();
            }

            return line;
        }

        private static int IndexOfEqualsOutsideString(string line)
        {
            var inString = false;
            var quote = '\0';

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];

                if (inString)
                {
                    if (quote == '"' && c == '\\')
                    {
                        i++;
                        continue;
                    }

                    if (c == quote)
                        inString = false;

                    continue;
                }

                if (c is '"' or '\'')
                {
                    inString = true;
                    quote = c;
                    continue;
                }

                if (c == '=')
                    return i;
            }

            return -1;
        }

        private static int BracketDelta(string line)
        {
            var depth = 0;
            var inString = false;
            var quote = '\0';

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];

                if (inString)
                {
                    if (quote == '"' && c == '\\')
                    {
                        i++;
                        continue;
                    }

                    if (c == quote)
                        inString = false;

                    continue;
                }

                if (c is '"' or '\'')
                {
                    inString = true;
                    quote = c;
                    continue;
                }

                if (c == '[') depth++;
                else if (c == ']') depth--;
            }

            return depth;
        }

        private static int IndexOfTripleQuote(string line, out string delimiter)
        {
            var single = line.IndexOf("'''", StringComparison.Ordinal);
            var @double = line.IndexOf("\"\"\"", StringComparison.Ordinal);

            if (single < 0 && @double < 0)
            {
                delimiter = string.Empty;
                return -1;
            }

            if (single >= 0 && (@double < 0 || single < @double))
            {
                delimiter = "'''";
                return single;
            }

            delimiter = "\"\"\"";
            return @double;
        }

        private static int Occurrences(string line, string needle)
        {
            var count = 0;
            var at = line.IndexOf(needle, StringComparison.Ordinal);

            while (at >= 0)
            {
                count++;
                at = line.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
            }

            return count;
        }

        private static string Unquote(string value)
        {
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
                return value[1..^1];

            return value;
        }
    }
}
