package ch.pianonic.hopper;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Just enough JSON, so the core can drop its Gson dependency and stay embeddable
 * in every adapter jar - see hopper-core/build.gradle for why that dependency had
 * to go.
 *
 * <p>Reading produces only JDK types: {@link LinkedHashMap}, {@link ArrayList},
 * {@link String}, {@link Long}, {@link Double}, {@link Boolean} and null. Writing
 * is a single string-escaping helper, because the one document HOPPER writes -
 * the client report - is assembled by hand in {@link Syncer#reportBody} so a test
 * can pin its exact bytes.
 */
final class Json {

    /**
     * How deep {@code {} } and {@code []} may nest before the document is refused.
     *
     * <p>This is not a tidiness limit, it is the thing that keeps a malformed
     * manifest from killing the launch. {@link #value()}, {@link #object()} and
     * {@link #array()} are mutually recursive, so nesting depth is stack depth,
     * and 4000 opening braces in an 8 KB response was measured to throw
     * {@link StackOverflowError} - an {@link Error}, which sails straight through
     * every {@code catch (Exception)} on the way out. Gson, which this class
     * replaced, enforced its own nesting limit for exactly this reason; dropping
     * Gson without replacing the limit dropped the protection with it.
     *
     * <p>64 rather than Gson's 255: HOPPER's manifest is
     * {@code {"mods":[{...}]}}, which is three levels, so 64 is already twenty
     * times more room than the format can use, and it holds even on a small
     * thread stack rather than only on the default one.
     *
     * <p>Exceeding it is an ordinary {@link IllegalArgumentException} - the same
     * "this manifest is malformed" path as a missing brace, which
     * {@link Syncer#fetchManifest} already turns into a failed sync and
     * {@link Hopper#run} already turns into a launch with the cached mods.
     */
    private static final int MAX_DEPTH = 64;

    private final String src;
    private int pos;

    /** Open containers at the current parse position. See {@link #MAX_DEPTH}. */
    private int depth;

    private Json(String src) {
        this.src = src;
    }

    /**
     * @return a Map, List, String, Long, Double, Boolean or null
     * @throws IllegalArgumentException on anything malformed, including trailing
     *         content - a manifest that half-parses is a manifest we refuse - and
     *         including nesting deeper than {@link #MAX_DEPTH}
     */
    static Object parse(String text) {
        if (text == null) {
            throw new IllegalArgumentException("no JSON to parse");
        }
        Json p = new Json(text);
        p.skipWhitespace();
        Object root = p.value();
        p.skipWhitespace();
        if (p.pos != p.src.length()) {
            throw new IllegalArgumentException("trailing content at offset " + p.pos);
        }
        return root;
    }

    // ---- reading ----

    private Object value() {
        char c = peek();
        // A switch STATEMENT. Switch expressions are Java 14 and this file compiles at 8.
        switch (c) {
            case '{':
                return object();
            case '[':
                return array();
            case '"':
                return string();
            case 't':
                literal("true");
                return Boolean.TRUE;
            case 'f':
                literal("false");
                return Boolean.FALSE;
            case 'n':
                literal("null");
                return null;
            default:
                return number();
        }
    }

    private Map<String, Object> object() {
        enter();
        try {
            return objectBody();
        } finally {
            depth--;
        }
    }

    private Map<String, Object> objectBody() {
        Map<String, Object> map = new LinkedHashMap<String, Object>();
        pos++; // {
        skipWhitespace();
        if (peek() == '}') {
            pos++;
            return map;
        }
        for (;;) {
            skipWhitespace();
            if (peek() != '"') {
                throw error("expected a quoted key");
            }
            String key = string();
            skipWhitespace();
            if (peek() != ':') {
                throw error("expected ':' after key " + key);
            }
            pos++;
            skipWhitespace();
            map.put(key, value());
            skipWhitespace();
            char c = peek();
            pos++;
            if (c == ',') continue;
            if (c == '}') return map;
            throw error("expected ',' or '}'");
        }
    }

    private List<Object> array() {
        enter();
        try {
            return arrayBody();
        } finally {
            depth--;
        }
    }

    private List<Object> arrayBody() {
        List<Object> list = new ArrayList<Object>();
        pos++; // [
        skipWhitespace();
        if (peek() == ']') {
            pos++;
            return list;
        }
        for (;;) {
            skipWhitespace();
            list.add(value());
            skipWhitespace();
            char c = peek();
            pos++;
            if (c == ',') continue;
            if (c == ']') return list;
            throw error("expected ',' or ']'");
        }
    }

    /**
     * Counts one container in, and refuses the document before the recursion that
     * would follow can reach the stack limit. Paired with a {@code finally} that
     * counts back out, so a sibling of a deeply nested value is judged on its own
     * depth rather than on the running total.
     */
    private void enter() {
        if (depth + 1 > MAX_DEPTH) {
            throw error("nested more than " + MAX_DEPTH + " levels deep");
        }
        depth++;
    }

    private String string() {
        pos++; // opening quote
        StringBuilder sb = new StringBuilder();
        for (;;) {
            if (pos >= src.length()) {
                throw error("unterminated string");
            }
            char c = src.charAt(pos++);
            if (c == '"') {
                return sb.toString();
            }
            if (c != '\\') {
                sb.append(c);
                continue;
            }
            if (pos >= src.length()) {
                throw error("unterminated escape");
            }
            char e = src.charAt(pos++);
            switch (e) {
                case '"':  sb.append('"');  break;
                case '\\': sb.append('\\'); break;
                case '/':  sb.append('/');  break;
                case 'b':  sb.append('\b'); break;
                case 'f':  sb.append('\f'); break;
                case 'n':  sb.append('\n'); break;
                case 'r':  sb.append('\r'); break;
                case 't':  sb.append('\t'); break;
                case 'u':
                    if (pos + 4 > src.length()) {
                        throw error("truncated \\u escape");
                    }
                    try {
                        sb.append((char) Integer.parseInt(src.substring(pos, pos + 4), 16));
                    } catch (NumberFormatException ex) {
                        throw error("malformed \\u escape");
                    }
                    pos += 4;
                    break;
                default:
                    throw error("illegal escape \\" + e);
            }
        }
    }

    private Object number() {
        int start = pos;
        if (pos < src.length() && src.charAt(pos) == '-') {
            pos++;
        }
        boolean floating = false;
        while (pos < src.length()) {
            char c = src.charAt(pos);
            if (c >= '0' && c <= '9') {
                pos++;
            } else if (c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') {
                floating = true;
                pos++;
            } else {
                break;
            }
        }
        String text = src.substring(start, pos);
        if (text.isEmpty() || "-".equals(text)) {
            throw error("expected a value");
        }
        // Integer-looking numbers stay Long. Entry.size is a byte count, and a double starts
        // silently rounding it above 2^53 - which is the wrong place to discover a rounding bug.
        if (!floating) {
            try {
                return Long.valueOf(text);
            } catch (NumberFormatException ignored) {
                // Longer than a long. Fall through and keep it as an approximate double rather
                // than failing the whole manifest over one oversized field.
            }
        }
        try {
            return Double.valueOf(text);
        } catch (NumberFormatException ex) {
            throw error("not a number: " + text);
        }
    }

    private void literal(String word) {
        if (!src.startsWith(word, pos)) {
            throw error("expected " + word);
        }
        pos += word.length();
    }

    private char peek() {
        if (pos >= src.length()) {
            throw error("unexpected end of input");
        }
        return src.charAt(pos);
    }

    private void skipWhitespace() {
        while (pos < src.length()) {
            char c = src.charAt(pos);
            if (c == ' ' || c == '\t' || c == '\n' || c == '\r') {
                pos++;
            } else {
                break;
            }
        }
    }

    private IllegalArgumentException error(String message) {
        return new IllegalArgumentException("malformed JSON at offset " + pos + ": " + message);
    }

    // ---- typed lookups, so callers never cast ----

    static Map<?, ?> asObject(Object value) {
        return value instanceof Map ? (Map<?, ?>) value : null;
    }

    static List<?> asArray(Object value) {
        return value instanceof List ? (List<?>) value : null;
    }

    static Object get(Object object, String key) {
        Map<?, ?> map = asObject(object);
        return map == null ? null : map.get(key);
    }

    static String string(Object object, String key) {
        Object v = get(object, key);
        return v instanceof String ? (String) v : null;
    }

    static long number(Object object, String key) {
        Object v = get(object, key);
        return v instanceof Number ? ((Number) v).longValue() : 0L;
    }

    // ---- writing ----

    /**
     * Appends {@code value} as a JSON string, or the bare token {@code null}.
     *
     * <p>Escapes exactly what JSON requires and nothing more. Gson additionally
     * HTML-escapes {@code < > & = '} by default; not doing so is a deliberate
     * divergence and it is safe here - the only strings HOPPER writes are a UUID,
     * a username, and filenames that have already been through
     * {@link Syncer#sanitize}, and the receiver parses JSON rather than HTML.
     */
    static void write(StringBuilder out, String value) {
        if (value == null) {
            out.append("null");
            return;
        }
        out.append('"');
        for (int i = 0; i < value.length(); i++) {
            char c = value.charAt(i);
            switch (c) {
                case '"':  out.append("\\\""); break;
                case '\\': out.append("\\\\"); break;
                case '\b': out.append("\\b");  break;
                case '\f': out.append("\\f");  break;
                case '\n': out.append("\\n");  break;
                case '\r': out.append("\\r");  break;
                case '\t': out.append("\\t");  break;
                default:
                    if (c < 0x20) {
                        out.append(String.format("\\u%04x", Integer.valueOf(c)));
                    } else {
                        out.append(c);
                    }
                    break;
            }
        }
        out.append('"');
    }
}
