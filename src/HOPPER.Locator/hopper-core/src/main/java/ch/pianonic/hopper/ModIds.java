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

final class ModIds {
    private static final int MAX_METADATA_BYTES = 1024 * 1024;

    private static final String NEOFORGE_TOML = "META-INF/neoforge.mods.toml";
    private static final String FORGE_TOML = "META-INF/mods.toml";
    private static final String FABRIC_JSON = "fabric.mod.json";
    private static final String QUILT_JSON = "quilt.mod.json";
    private static final String MCMOD_INFO = "mcmod.info";

    private static final Pattern VALID = Pattern.compile("^[a-z][a-z0-9_.-]{1,63}$");

    private ModIds() {
    }

    static boolean valid(String id) {
        return id != null && VALID.matcher(id).matches();
    }

    static List<String> read(Path jar, HopperLog log) {
        try {
            ZipFile zip = new ZipFile(jar.toFile());
            try {
                return readEntries(zip);
            } finally {
                zip.close();
            }
        } catch (Throwable t) {
            log.warn("[HOPPER] could not read mod metadata from " + jar
                    + "; it will be left where it is", t);
            return new ArrayList<String>();
        }
    }

    private static List<String> readEntries(ZipFile zip) throws IOException {
        List<String> ids = new ArrayList<String>();

        String toml = text(zip, NEOFORGE_TOML);
        List<String> tomlIds = toml == null
                ? Collections.<String>emptyList()
                : fromModsToml(toml);

        if (tomlIds.isEmpty()) {
            String legacy = text(zip, FORGE_TOML);
            if (legacy != null) tomlIds = fromModsToml(legacy);
        }

        addAll(ids, tomlIds);

        String fabric = text(zip, FABRIC_JSON);
        if (fabric != null) addAll(ids, fromFabricJson(fabric));

        String quilt = text(zip, QUILT_JSON);
        if (quilt != null) addAll(ids, fromQuiltJson(quilt));

        String mcmod = text(zip, MCMOD_INFO);
        if (mcmod != null) addAll(ids, fromMcmodInfo(mcmod));

        return ids;
    }

    static List<String> fromModsToml(String text) {
        List<String> ids = new ArrayList<String>();
        if (text == null || text.isEmpty()) return ids;

        String currentTable = "";

        boolean multiline = false;
        String multilineDelimiter = "";

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

    private static String tableName(String line) {
        if (line.startsWith("[[")) {
            int end = line.indexOf("]]", 2);
            return end < 0 ? "" : unquote(line.substring(2, end).trim());
        }
        int close = line.indexOf(']', 1);
        return close < 0 ? "" : unquote(line.substring(1, close).trim());
    }

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

    private static String unquote(String value) {
        if (value.length() >= 2) {
            char first = value.charAt(0);
            if ((first == '"' || first == '\'') && value.charAt(value.length() - 1) == first) {
                return value.substring(1, value.length() - 1);
            }
        }
        return value;
    }

    static List<String> fromFabricJson(String text) {
        List<String> ids = new ArrayList<String>();
        add(ids, Json.string(parseOrNull(text), "id"));
        return ids;
    }

    static List<String> fromQuiltJson(String text) {
        List<String> ids = new ArrayList<String>();
        add(ids, Json.string(Json.get(parseOrNull(text), "quilt_loader"), "id"));
        return ids;
    }

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

    private static Object parseOrNull(String text) {
        try {
            return Json.parse(text);
        } catch (RuntimeException e) {
            return null;
        }
    }

    private static String text(ZipFile zip, String name) throws IOException {
        ZipEntry entry = zip.getEntry(name);
        if (entry == null) return null;

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

        int offset = bytes.length >= 3
                && (bytes[0] & 0xFF) == 0xEF
                && (bytes[1] & 0xFF) == 0xBB
                && (bytes[2] & 0xFF) == 0xBF ? 3 : 0;

        return new String(bytes, offset, bytes.length - offset, StandardCharsets.UTF_8);
    }

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
