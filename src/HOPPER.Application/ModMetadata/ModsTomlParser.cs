namespace HOPPER.Application.ModMetadata
{
    /// <summary>A single-purpose, hand-written scan of META-INF/mods.toml and
    /// META-INF/neoforge.mods.toml that answers exactly one question: which mod ids does this jar
    /// declare for itself?
    ///
    /// Hand-written because neither HOPPER.Application nor HOPPER.Infrastructure references a TOML
    /// library and pulling one in for a single key is not warranted. It has an exact twin in the Java
    /// client at hopper-core/src/main/java/ch/pianonic/hopper/ModIds.java: the client only migrates a
    /// jar out of the player's mods/ folder when the id it read matches the id the server published,
    /// so the two implementations must derive the identical set from identical bytes. Change one and
    /// you change the other, in the same commit, with the same fixtures.
    ///
    /// The whole correctness of this parser is that it accepts modId ONLY while the current table is
    /// exactly [[mods]]. A real mods.toml also carries [[dependencies.&lt;id&gt;]] tables, each with
    /// its own modId key naming a DIFFERENT mod - Embeddium's file mentions embeddium, rubidium,
    /// oculus and textrues_embeddium_options, and only the first two are its own. Reading a
    /// dependency's id as an identity does not fail safe: it makes the client move an unrelated jar
    /// out of a folder HOPPER was told never to manage.
    ///
    /// It is iterative with no recursion anywhere. A jar is untrusted input.</summary>
    public static class ModsTomlParser
    {
        /// <summary>Reads the ids out of a mods.toml / neoforge.mods.toml body. Never throws; a file
        /// it cannot make sense of yields nothing, which the client reads as "do nothing".</summary>
        public static string[] Parse(string text)
        {
            if (string.IsNullOrEmpty(text))
                return [];

            var ids = new List<string>();

            // "" is the root table. Only the literal "mods" opens the array-of-tables we care about.
            var currentTable = string.Empty;

            // A modId value is never a triple-quoted string, so any line carrying a ''' or """ is
            // uninteresting by construction and the body of a multiline string can be skipped
            // wholesale. That is what stops a description containing a line like "# Features" or
            // "[[mods]]" from being read as structure.
            var multiline = false;
            var multilineDelimiter = string.Empty;

            // Depth inside the inline form, mods = [ { modId = '...' }, ]. Two of the 104 real files
            // in the reference instance use it (the lowcodefml datapack wrapper writes it that way)
            // and a parser keyed only on [[mods]] silently returns nothing for both.
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
                    // Odd number of delimiters on this line means one is still open at the end of it.
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

                // The inline array only ever appears at the root. [[dependencies.x]] carrying a key
                // called "mods" is not a thing, but scoping it costs one comparison.
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

        /// <summary>Text between [[ and ]] (or [ and ]), trimmed and unquoted. Real files write
        /// [["dependencies.yet_another_config_lib_v3"]] with the table path itself quoted, and 23 of
        /// them put a # comment after the closing bracket - taking only what is between the brackets
        /// handles both without a special case.</summary>
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

        /// <summary>Pulls every modId out of a fragment of the inline mods = [ { ... } ] form.
        /// Splits on the structural characters that are outside a string, so
        /// displayName = "Farmer's Delight Compat" does not end a value at its apostrophe.</summary>
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
                    // Only a basic string processes escapes. A literal '...' string takes a
                    // backslash at face value, which is why the check is on the delimiter.
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

        /// <summary>Cuts the line at the first # that is not inside a string. A # inside a value is
        /// legal TOML and does not appear in the 104-file reference corpus, but the grammar allows it
        /// and being quote-aware is four extra lines.</summary>
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

        /// <summary>Strips one matching pair of surrounding quotes. TOML escape sequences are not
        /// processed on purpose: a mod id matches ^[a-z][a-z0-9_.-]{1,63}$ and so can never contain
        /// one, and anything that did would be rejected by the validator anyway.</summary>
        private static string Unquote(string value)
        {
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
                return value[1..^1];

            return value;
        }
    }
}
