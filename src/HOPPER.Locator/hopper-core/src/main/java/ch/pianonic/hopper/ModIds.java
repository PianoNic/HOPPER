package ch.pianonic.hopper;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.regex.Pattern;
import java.util.zip.ZipEntry;
import java.util.zip.ZipFile;

/**
 * Reads the mod ids a jar declares for itself, in every metadata format HOPPER's
 * adapters cover.
 *
 * <p>This exists because filename and hash matching cannot solve the problem
 * HOPPER actually has. A player carrying {@code jei-1.20.1-15.2.0.27.jar} and a
 * server distributing {@code jei-1.20.1-15.3.0.4.jar} have two filenames and two
 * hashes for one mod, and the loader refuses to start when it finds the same id
 * twice. Only the mod id identifies it.
 *
 * <p><strong>Exact twin of the server's reader</strong> at
 * {@code src/HOPPER.Application/ModMetadata/ModIdReader.cs} and
 * {@code ModsTomlParser.cs}. A mod id only does anything when both sides derive
 * the identical set from identical bytes: the client moves a jar out of the
 * player's {@code mods/} folder exactly when the id it read here matches an id
 * the server published. Change one, change the other, with the same fixtures.
 *
 * <p>Every method is total. A file that is not a zip, a truncated zip, a
 * metadata file that does not parse, a jar that simply declares nothing - all of
 * them come out as no ids. That bias is deliberate and it is the right way round:
 * a missed id is a migration that does not happen, which is the crash the player
 * already had, while a <em>wrong</em> id is HOPPER moving a jar it was told never
 * to touch.
 *
 * <p>Package-private, and it has to be: {@link Json} and its helpers are, and
 * this reuses them rather than growing a second JSON parser.
 */
final class ModIds {

    /**
     * A real {@code mods.toml} is about a kilobyte. Anything past this is not
     * metadata, and a jar in the player's mods folder is untrusted input - the
     * same posture as {@link Json}'s depth cap, for the same reason.
     */
    private static final int MAX_METADATA_BYTES = 1024 * 1024;

    private static final String NEOFORGE_TOML = "META-INF/neoforge.mods.toml";
    private static final String FORGE_TOML = "META-INF/mods.toml";
    private static final String FABRIC_JSON = "fabric.mod.json";
    private static final String QUILT_JSON = "quilt.mod.json";
    private static final String MCMOD_INFO = "mcmod.info";

    /**
     * Forge's own rule, taken verbatim from the regex and the message
     * {@code "Invalid modId found in file {} - {} does not match the standard: {}"}
     * inside {@code net/neoforged/fml/loading/moddiscovery/ModInfo.class}.
     *
     * <p>Applied to every id from every format, including the Fabric and Quilt
     * ones whose loaders document a slightly wider rule - 3 to 64 characters,
     * a leading digit permitted. One rule everywhere costs a sub-one-percent
     * Fabric id and buys the guarantee that the .NET side, which applies this
     * same single regex, can never disagree with this one. A dropped id is a
     * migration that does not happen; a disagreement is a jar moved for no
     * reason.
     */
    private static final Pattern VALID = Pattern.compile("^[a-z][a-z0-9_.-]{1,63}$");

    private ModIds() {
    }

    static boolean valid(String id) {
        return id != null && VALID.matcher(id).matches();
    }

    /**
     * The mod ids {@code jar} declares for itself, in the order they were found,
     * without duplicates.
     *
     * <p>Never throws, and that is the contract this whole class is written
     * around: it runs inside a loader's pre-discovery hook over files a player
     * put there by hand, so anything that escaped would be a crash report
     * instead of a game.
     *
     * @param log used for the one warning an unreadable jar produces; a jar that
     *            is simply a library and declares nothing is silent, because that
     *            is the ordinary case rather than a problem
     */
    static List<String> read(Path jar, HopperLog log) {
        try {
            ZipFile zip = new ZipFile(jar.toFile());
            try {
                return readEntries(zip);
            } finally {
                zip.close();
            }
        } catch (Throwable t) {
            // Throwable rather than IOException: a corrupt zip has been seen to come back as
            // anything from ZipException to IllegalArgumentException out of the inflater, and the
            // answer to every one of them is the same - this jar contributes no ids and the player
            // still gets a game.
            log.warn("[HOPPER] could not read mod metadata from " + jar
                    + "; it will be left where it is", t);
            return new ArrayList<String>();
        }
    }

    private static List<String> readEntries(ZipFile zip) throws IOException {
        List<String> ids = new ArrayList<String>();

        // Precedence applies only inside the toml pair, and only in this direction. NeoForge 21.1+
        // reads META-INF/neoforge.mods.toml and treats META-INF/mods.toml as the marker of a legacy
        // Forge jar. The fallback when the new file is present but yields nothing is for the
        // malformed case: a broken new file must not cost us the ids the old one still carries.
        String toml = text(zip, NEOFORGE_TOML);
        List<String> tomlIds = toml == null
                ? Collections.<String>emptyList()
                : fromModsToml(toml);

        if (tomlIds.isEmpty()) {
            String legacy = text(zip, FORGE_TOML);
            if (legacy != null) tomlIds = fromModsToml(legacy);
        }

        addAll(ids, tomlIds);

        // Everything else is a union rather than a first match. Terralith ships mods.toml,
        // fabric.mod.json and quilt.mod.json, all three declaring "terralith", and the union is one
        // id. A jar whose formats genuinely disagreed would load under two ids depending on the
        // loader, so migrating on either is the safe answer.
        String fabric = text(zip, FABRIC_JSON);
        if (fabric != null) addAll(ids, fromFabricJson(fabric));

        String quilt = text(zip, QUILT_JSON);
        if (quilt != null) addAll(ids, fromQuiltJson(quilt));

        String mcmod = text(zip, MCMOD_INFO);
        if (mcmod != null) addAll(ids, fromMcmodInfo(mcmod));

        // Nested jars under META-INF/jarjar/ and META-INF/jars/ are DELIBERATELY not read, and
        // this is not an oversight to be fixed.
        //
        // In the 102-jar reference instance, 26 jars bundle nested jars and FOURTEEN different
        // top-level mods bundle a copy of mixinextras. If HOPPER recursed, one distributed jar
        // containing mixinextras would make "mixinextras" a manifest mod id, and this client would
        // then see thirteen unrelated jars in the player's mods/ folder as "the same mod" and start
        // moving them into hoppermods/replaced/. That is data movement against jars HOPPER was told
        // never to touch.
        //
        // It is also unnecessary: jar-in-jar exists precisely so nested copies do not collide - the
        // loader version-selects them. The hard "Found duplicate mods:" failure this whole feature
        // prevents is between top-level mod files. A pure container jar declares nothing here.
        return ids;
    }

    // ---- toml ----

    /**
     * A single-purpose, hand-written scan of {@code META-INF/mods.toml} and
     * {@code META-INF/neoforge.mods.toml} answering exactly one question: which
     * mod ids does this jar declare for itself?
     *
     * <p>Hand-written because the core has no dependencies and is not going to
     * grow a TOML library for one key.
     *
     * <p><strong>The whole correctness of this parser is that it accepts
     * {@code modId} only while the current table is exactly {@code mods}.</strong>
     * A real file also carries {@code [[dependencies.<id>]]} tables, each with
     * its own {@code modId} key naming a <em>different</em> mod - Embeddium's
     * file mentions embeddium, rubidium, oculus and textrues_embeddium_options,
     * and only the first two are its own. 98 of the 104 real toml files in the
     * reference instance have such a table. Reading a dependency's id as an
     * identity does not fail safe: it moves an unrelated jar out of the player's
     * mods folder.
     *
     * <p>Iterative, with no recursion anywhere.
     */
    static List<String> fromModsToml(String text) {
        List<String> ids = new ArrayList<String>();
        if (text == null || text.isEmpty()) return ids;

        // "" is the root table. Only the literal "mods" opens the array-of-tables we care about.
        String currentTable = "";

        // A modId value is never a triple-quoted string, so any line carrying a ''' or """ is
        // uninteresting by construction and the body of a multiline string can be skipped
        // wholesale. That is what stops a description containing a line like "# Features" or
        // "[[mods]]" from being read as structure. 67 of the 104 real files have one.
        boolean multiline = false;
        String multilineDelimiter = "";

        // Depth inside the inline form, mods = [ { modId = '...' }, ]. Two of the 104 real files
        // use it - the lowcodefml datapack wrapper writes it that way - and a parser keyed only on
        // [[mods]] silently returns nothing for both.
        int inlineDepth = 0;

        String[] lines = text.split("\n", -1);
        for (int i = 0; i < lines.length; i++) {
            String line = trimTrailingCr(lines[i]);

            if (multiline) {
                if (line.contains(multilineDelimiter)) multiline = false;
                continue;
            }

            String delimiter = tripleQuoteDelimiter(line);
            if (delimiter != null) {
                // An odd number of delimiters on this line means one is still open at the end of it.
                if (occurrences(line, delimiter) % 2 == 1) {
                    multiline = true;
                    multilineDelimiter = delimiter;
                }
                continue;
            }

            line = line.trim();
            if (line.isEmpty() || line.charAt(0) == '#') continue;

            if (inlineDepth > 0) {
                String body = stripComment(line);
                collectInline(body, ids);
                inlineDepth += bracketDelta(body);
                continue;
            }

            if (line.charAt(0) == '[') {
                currentTable = tableName(line);
                continue;
            }

            String stripped = stripComment(line);
            int equals = indexOfEqualsOutsideString(stripped);
            if (equals < 0) continue;

            String key = stripped.substring(0, equals).trim();
            String value = stripped.substring(equals + 1).trim();

            // The inline array only ever appears at the root. A [[dependencies.x]] table carrying a
            // key called "mods" is not a thing, but scoping it costs one comparison.
            if (currentTable.isEmpty() && "mods".equals(key) && value.startsWith("[")) {
                collectInline(value, ids);
                inlineDepth += bracketDelta(value);
                continue;
            }

            if ("mods".equals(currentTable) && "modId".equals(key)) {
                add(ids, unquote(value));
            }
        }

        return ids;
    }

    /**
     * Text between {@code [[} and {@code ]]}, or {@code [} and {@code ]},
     * trimmed and unquoted. Real files write
     * {@code [["dependencies.yet_another_config_lib_v3"]]} with the table path
     * itself quoted, and 23 of them put a {@code #} comment after the closing
     * bracket - taking only what is between the brackets handles both without a
     * special case.
     */
    private static String tableName(String line) {
        if (line.startsWith("[[")) {
            int end = line.indexOf("]]", 2);
            return end < 0 ? "" : unquote(line.substring(2, end).trim());
        }
        int close = line.indexOf(']', 1);
        return close < 0 ? "" : unquote(line.substring(1, close).trim());
    }

    /**
     * Pulls every {@code modId} out of a fragment of the inline
     * {@code mods = [ { ... } ]} form. Splits on the structural characters that
     * are outside a string, so {@code displayName = "Farmer's Delight Compat"}
     * does not end a value at its apostrophe.
     */
    private static void collectInline(String fragment, List<String> ids) {
        int start = 0;
        boolean inString = false;
        char quote = '\0';

        for (int i = 0; i <= fragment.length(); i++) {
            if (i == fragment.length()) {
                inlinePiece(fragment.substring(start), ids);
                break;
            }

            char c = fragment.charAt(i);

            if (inString) {
                // Only a basic string processes escapes. A literal '...' string takes a backslash
                // at face value, which is why the check is on the delimiter.
                if (quote == '"' && c == '\\') {
                    i++;
                    continue;
                }
                if (c == quote) inString = false;
                continue;
            }

            if (c == '"' || c == '\'') {
                inString = true;
                quote = c;
                continue;
            }

            if (c == '{' || c == '}' || c == ',' || c == '[' || c == ']') {
                inlinePiece(fragment.substring(start, i), ids);
                start = i + 1;
            }
        }
    }

    private static void inlinePiece(String piece, List<String> ids) {
        int equals = indexOfEqualsOutsideString(piece);
        if (equals < 0) return;
        if ("modId".equals(piece.substring(0, equals).trim())) {
            add(ids, unquote(piece.substring(equals + 1).trim()));
        }
    }

    /**
     * Cuts the line at the first {@code #} that is not inside a string. A
     * {@code #} inside a value is legal TOML and absent from the 104-file
     * reference corpus, but the grammar allows it and being quote-aware is four
     * extra lines.
     */
    private static String stripComment(String line) {
        boolean inString = false;
        char quote = '\0';

        for (int i = 0; i < line.length(); i++) {
            char c = line.charAt(i);

            if (inString) {
                if (quote == '"' && c == '\\') {
                    i++;
                    continue;
                }
                if (c == quote) inString = false;
                continue;
            }

            if (c == '"' || c == '\'') {
                inString = true;
                quote = c;
                continue;
            }

            if (c == '#') {
                return trimTrailingWhitespace(line.substring(0, i));
            }
        }

        return line;
    }

    private static int indexOfEqualsOutsideString(String line) {
        boolean inString = false;
        char quote = '\0';

        for (int i = 0; i < line.length(); i++) {
            char c = line.charAt(i);

            if (inString) {
                if (quote == '"' && c == '\\') {
                    i++;
                    continue;
                }
                if (c == quote) inString = false;
                continue;
            }

            if (c == '"' || c == '\'') {
                inString = true;
                quote = c;
                continue;
            }

            if (c == '=') return i;
        }

        return -1;
    }

    private static int bracketDelta(String line) {
        int depth = 0;
        boolean inString = false;
        char quote = '\0';

        for (int i = 0; i < line.length(); i++) {
            char c = line.charAt(i);

            if (inString) {
                if (quote == '"' && c == '\\') {
                    i++;
                    continue;
                }
                if (c == quote) inString = false;
                continue;
            }

            if (c == '"' || c == '\'') {
                inString = true;
                quote = c;
                continue;
            }

            if (c == '[') depth++;
            else if (c == ']') depth--;
        }

        return depth;
    }

    /** @return {@code '''}, {@code """} or null - whichever comes first on the line */
    private static String tripleQuoteDelimiter(String line) {
        int single = line.indexOf("'''");
        int twin = line.indexOf("\"\"\"");

        if (single < 0 && twin < 0) return null;
        if (single >= 0 && (twin < 0 || single < twin)) return "'''";
        return "\"\"\"";
    }

    private static int occurrences(String line, String needle) {
        int count = 0;
        int at = line.indexOf(needle);
        while (at >= 0) {
            count++;
            at = line.indexOf(needle, at + needle.length());
        }
        return count;
    }

    /**
     * Strips one matching pair of surrounding quotes. TOML escape sequences are
     * not processed on purpose: a mod id matches
     * {@code ^[a-z][a-z0-9_.-]{1,63}$} and so can never contain one, and
     * anything that did would be rejected by {@link #valid} anyway.
     */
    private static String unquote(String value) {
        if (value.length() >= 2) {
            char first = value.charAt(0);
            if ((first == '"' || first == '\'') && value.charAt(value.length() - 1) == first) {
                return value.substring(1, value.length() - 1);
            }
        }
        return value;
    }

    // ---- json ----

    /**
     * Fabric's id is a top-level string under the key {@code id}. Read by its
     * exact path and never searched for: {@code depends} is an object whose
     * <em>keys</em> are ids - fabricloader, minecraft, fabric-api-base - so a
     * hunt for anything that looks like an id returns the mod's dependencies as
     * if they were the mod.
     */
    static List<String> fromFabricJson(String text) {
        List<String> ids = new ArrayList<String>();
        add(ids, Json.string(parseOrNull(text), "id"));
        return ids;
    }

    /**
     * Quilt nests its id one level down, at {@code quilt_loader.id}. Its
     * {@code depends} is an array of objects each carrying an {@code id} - the
     * same hazard as {@code [[dependencies.*]]} in toml - so this too reads the
     * exact path.
     *
     * <p>{@code quilt_loader.provides} is ignored on purpose. It is an aliasing
     * mechanism ("this mod also satisfies X"), not an identity, and treating it
     * as one would migrate jars that merely declare the same alias.
     */
    static List<String> fromQuiltJson(String text) {
        List<String> ids = new ArrayList<String>();
        add(ids, Json.string(Json.get(parseOrNull(text), "quilt_loader"), "id"));
        return ids;
    }

    /**
     * Forge 1.12.2 and older. The key is {@code modid}, <strong>all
     * lowercase</strong> - the opposite convention from mods.toml's camelCase
     * {@code modId}, and the single most likely thing to get wrong. It is pinned
     * by {@code ModMetadata.class}, whose Java field {@code modId} carries
     * {@code @SerializedName("modid")}.
     *
     * <p>The root is a JSON array, or an object whose {@code modList} is one.
     * Forge itself branches on exactly that, and an mcmod.info may legitimately
     * list several mods - the {@code parent} field exists for child mods - so
     * every element contributes.
     */
    static List<String> fromMcmodInfo(String text) {
        List<String> ids = new ArrayList<String>();

        Object root = parseOrNull(text);
        List<?> list = Json.asArray(root);
        if (list == null) list = Json.asArray(Json.get(root, "modList"));
        if (list == null) return ids;

        for (Object element : list) {
            add(ids, Json.string(element, "modid"));
        }
        return ids;
    }

    /**
     * {@link Json#parse} is deliberately strict - no comments, no trailing
     * commas, no trailing content - because it also parses the manifest, where
     * that strictness is correct. It is now also the contract with the .NET
     * side, which parses these same files with {@code JsonDocument}'s default
     * (equally strict) options. Two sides with different leniency would derive
     * different id sets from one jar, which is the exact failure this feature
     * exists to avoid. A hand-edited {@code fabric.mod.json} with a stray comma
     * therefore yields no ids on both sides - a missed migration, never a wrong
     * move.
     *
     * @return null when the text is not JSON this parser accepts
     */
    private static Object parseOrNull(String text) {
        try {
            return Json.parse(text);
        } catch (RuntimeException e) {
            return null;
        }
    }

    // ---- zip ----

    /**
     * Reads one entry by its <strong>exact, case-sensitive</strong> name. All
     * five names are exact and case-sensitive in every loader checked, so
     * enumerating and comparing loosely could only ever invent a match.
     *
     * @return null when the entry is absent or over {@link #MAX_METADATA_BYTES}
     */
    private static String text(ZipFile zip, String name) throws IOException {
        ZipEntry entry = zip.getEntry(name);
        if (entry == null) return null;

        // getSize() is the central directory's claim and may be -1 or a lie, so the read below is
        // capped independently rather than trusted.
        if (entry.getSize() > MAX_METADATA_BYTES) return null;

        ByteArrayOutputStream buffer = new ByteArrayOutputStream();
        InputStream in = zip.getInputStream(entry);
        if (in == null) return null;
        try {
            byte[] chunk = new byte[8192];
            int read;
            while ((read = in.read(chunk)) > 0) {
                if (buffer.size() + read > MAX_METADATA_BYTES) return null;
                buffer.write(chunk, 0, read);
            }
        } finally {
            in.close();
        }

        byte[] bytes = buffer.toByteArray();

        // A BOM in front of a JSON document is a parse error and in front of a TOML key it is a
        // stray character. Real files carry them.
        int offset = bytes.length >= 3
                && (bytes[0] & 0xFF) == 0xEF
                && (bytes[1] & 0xFF) == 0xBB
                && (bytes[2] & 0xFF) == 0xBF ? 3 : 0;

        return new String(bytes, offset, bytes.length - offset, StandardCharsets.UTF_8);
    }

    // ---- small shared helpers ----

    private static void add(List<String> ids, String id) {
        if (valid(id) && !ids.contains(id)) ids.add(id);
    }

    private static void addAll(List<String> ids, List<String> more) {
        for (int i = 0; i < more.size(); i++) {
            String id = more.get(i);
            if (!ids.contains(id)) ids.add(id);
        }
    }

    private static String trimTrailingCr(String line) {
        int end = line.length();
        while (end > 0 && line.charAt(end - 1) == '\r') end--;
        return end == line.length() ? line : line.substring(0, end);
    }

    private static String trimTrailingWhitespace(String value) {
        int end = value.length();
        while (end > 0 && value.charAt(end - 1) <= ' ') end--;
        return end == value.length() ? value : value.substring(0, end);
    }
}
