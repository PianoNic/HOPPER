package ch.pianonic.hopper;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

final class Json {
    private static final int MAX_DEPTH = 64;

    private final String src;
    private int pos;

    private int depth;

    private Json(String src) {
        this.src = src;
    }

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

    private Object value() {
        char c = peek();

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
        pos++;
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
        pos++;
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

    private void enter() {
        if (depth + 1 > MAX_DEPTH) {
            throw error("nested more than " + MAX_DEPTH + " levels deep");
        }
        depth++;
    }

    private String string() {
        pos++;
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

        if (!floating) {
            try {
                return Long.valueOf(text);
            } catch (NumberFormatException ignored) {
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
