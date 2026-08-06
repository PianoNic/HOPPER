package ch.pianonic.hopper;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URI;
import java.net.URL;
import java.net.URLConnection;
import java.nio.charset.StandardCharsets;
import java.util.Locale;

final class Http {
    private static final int MAX_REDIRECTS = 5;

    interface Sink<T> {
        T read(InputStream body) throws IOException;
    }

    private final String token;

    private final URI trustedOrigin;

    private final HopperLog log;

    Http(String token, URI trustedOrigin, HopperLog log) {
        this.token = token;
        this.trustedOrigin = trustedOrigin;
        this.log = log;
    }

    <T> T get(String url, int connectMs, int readMs, String what, Sink<T> sink) throws IOException {
        URI current = URI.create(url);

        for (int hop = 0; ; hop++) {
            HttpURLConnection c = open(current, "GET", connectMs, readMs, sameOrigin(trustedOrigin, current));
            int code = c.getResponseCode();

            if (isRedirect(code)) {
                String location = c.getHeaderField("Location");

                drainQuietly(quietInput(c));
                c.disconnect();

                if (location == null) {
                    throw new IOException(what + " returned HTTP " + code + " with no Location header");
                }
                if (hop >= MAX_REDIRECTS) {
                    throw new IOException(what + " exceeded " + MAX_REDIRECTS + " redirects");
                }

                URI next = current.resolve(location);
                String scheme = next.getScheme() == null ? "" : next.getScheme().toLowerCase(Locale.ROOT);
                if (!"http".equals(scheme) && !"https".equals(scheme)) {
                    throw new SecurityException("refusing redirect to " + scheme + ": " + next);
                }

                if ("https".equalsIgnoreCase(current.getScheme()) && "http".equals(scheme)) {
                    throw new SecurityException("refusing HTTPS to HTTP redirect: " + next);
                }
                current = next;
                continue;
            }

            if (code >= 400) {
                String detail = snippet(c.getErrorStream());
                c.disconnect();
                throw new IllegalStateException(what + " returned HTTP " + code
                        + (detail.isEmpty() ? "" : ": " + detail));
            }
            if (code != 200) {
                drainQuietly(quietInput(c));
                c.disconnect();
                throw new IllegalStateException(what + " returned HTTP " + code);
            }

            InputStream body = c.getInputStream();
            try {
                return sink.read(body);
            } finally {
                closeQuietly(body);
                c.disconnect();
            }
        }
    }

    int post(String url, byte[] body, int connectMs, int readMs) throws IOException {
        URI target = URI.create(url);

        HttpURLConnection c = open(target, "POST", connectMs, readMs, sameOrigin(trustedOrigin, target));
        c.setDoOutput(true);

        c.setFixedLengthStreamingMode(body.length);
        c.setRequestProperty("Content-Type", "application/json; charset=utf-8");

        OutputStream out = c.getOutputStream();
        try {
            out.write(body);
            out.flush();
        } finally {
            closeQuietly(out);
        }

        int code = c.getResponseCode();
        if (code >= 400) {
            String detail = snippet(c.getErrorStream());
            log.warn("[HOPPER] report returned HTTP " + code + (detail.isEmpty() ? "" : ": " + detail), null);
        } else if (code >= 300) {
            log.warn("[HOPPER] report was redirected (HTTP " + code + "); not following it on a POST", null);
        } else {
            drainQuietly(quietInput(c));
        }
        c.disconnect();
        return code;
    }

    private HttpURLConnection open(URI uri, String method, int connectMs, int readMs, boolean auth)
            throws IOException {
        URLConnection raw = new URL(uri.toString()).openConnection();
        if (!(raw instanceof HttpURLConnection)) {
            throw new IOException("not an http(s) URL: " + uri);
        }
        HttpURLConnection c = (HttpURLConnection) raw;

        c.setInstanceFollowRedirects(false);
        c.setConnectTimeout(connectMs);
        c.setReadTimeout(readMs);
        c.setUseCaches(false);
        c.setRequestMethod(method);
        c.setRequestProperty("User-Agent", "HOPPER/1.0");

        c.setRequestProperty("Accept-Encoding", "identity");

        if (auth && token != null && !token.trim().isEmpty()) {
            c.setRequestProperty("Authorization", "Bearer " + token);
        }
        return c;
    }

    private static boolean isRedirect(int code) {
        return code == 301 || code == 302 || code == 303 || code == 307 || code == 308;
    }

    static boolean sameOrigin(URI a, URI b) {
        if (a == null || b == null || a.getHost() == null || b.getHost() == null) {
            return false;
        }
        return equalsIgnoreCaseOrNull(a.getScheme(), b.getScheme())
                && a.getHost().equalsIgnoreCase(b.getHost())
                && port(a) == port(b);
    }

    private static int port(URI uri) {
        if (uri.getPort() != -1) {
            return uri.getPort();
        }
        String scheme = uri.getScheme() == null ? "" : uri.getScheme().toLowerCase(Locale.ROOT);
        if ("https".equals(scheme)) return 443;
        if ("http".equals(scheme)) return 80;
        return -1;
    }

    private static boolean equalsIgnoreCaseOrNull(String a, String b) {
        return a == null ? b == null : a.equalsIgnoreCase(b);
    }

    static byte[] drain(InputStream in) throws IOException {
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        byte[] buf = new byte[8192];
        int n;
        while ((n = in.read(buf)) > 0) {
            out.write(buf, 0, n);
        }
        return out.toByteArray();
    }

    static String utf8(InputStream in) throws IOException {
        return new String(drain(in), StandardCharsets.UTF_8);
    }

    private static String snippet(InputStream err) {
        if (err == null) return "";
        try {
            byte[] b = drain(err);
            String s = new String(b, 0, Math.min(b.length, 512), StandardCharsets.UTF_8);
            return s.replace('\n', ' ').replace('\r', ' ').trim();
        } catch (IOException e) {
            return "";
        } finally {
            closeQuietly(err);
        }
    }

    private static InputStream quietInput(HttpURLConnection c) {
        try {
            return c.getInputStream();
        } catch (IOException e) {
            return c.getErrorStream();
        }
    }

    private static void drainQuietly(InputStream in) {
        if (in == null) return;
        try {
            byte[] buf = new byte[4096];
            while (in.read(buf) > 0) {
            }
        } catch (IOException ignored) {
        } finally {
            closeQuietly(in);
        }
    }

    private static void closeQuietly(java.io.Closeable c) {
        if (c == null) return;
        try {
            c.close();
        } catch (IOException ignored) {
        }
    }
}
