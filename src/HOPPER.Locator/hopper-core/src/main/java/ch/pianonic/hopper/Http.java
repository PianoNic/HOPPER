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

/**
 * The Java 8 stand-in for {@code java.net.http.HttpClient}, which is Java 11 and
 * therefore unavailable to a core that has to run on Minecraft 1.16.5.
 *
 * <p>It deliberately reproduces the two behaviours the old builder configured -
 * a connect timeout and {@code Redirect.NORMAL} - because
 * {@link HttpURLConnection} gives neither for free, and losing either one
 * silently would look like an offline server rather than a bug.
 */
final class Http {

    /** The same ceiling {@code java.net.http} uses. */
    private static final int MAX_REDIRECTS = 5;

    /** Reads a response body. An interface rather than a lambda target, see {@link Syncer}. */
    interface Sink<T> {
        T read(InputStream body) throws IOException;
    }

    private final String token;

    /**
     * The one host allowed to see the bearer token: the origin of the configured
     * {@code manifestUrl}.
     *
     * <p>Fixed at construction, and deliberately NOT re-derived per request. It
     * used to be {@code URI.create(url)} inside {@link #get}, which made the
     * comparison "is this URL the same origin as itself" - always true on the
     * first hop, so the token went to whatever host the manifest happened to name
     * in a mod's {@code url}. That is a HOPPER server token handed to an arbitrary
     * third party on the say-so of the very document the token authenticates.
     * Manifest content is untrusted everywhere else in the core - it is why
     * {@link Syncer#sanitize} exists - and it is untrusted here too.
     */
    private final URI trustedOrigin;

    private final HopperLog log;

    /**
     * @param trustedOrigin any URI on the HOPPER server, normally the configured
     *                      manifest URL. Only its scheme, host and port are used.
     */
    Http(String token, URI trustedOrigin, HopperLog log) {
        this.token = token;
        this.trustedOrigin = trustedOrigin;
        this.log = log;
    }

    // ---- GET ----

    /**
     * @param connectMs socket connect timeout
     * @param readMs    per-read stall timeout - NOT a deadline for the whole exchange
     * @param what      names the request in any exception, so a failure says which one failed
     */
    <T> T get(String url, int connectMs, int readMs, String what, Sink<T> sink) throws IOException {
        URI current = URI.create(url);

        for (int hop = 0; ; hop++) {
            // Authorization only ever goes to the HOPPER server. A manifest that points a jar
            // download at a CDN - directly, or through a redirect - must not leak the server
            // token to that CDN, so the test is against trustedOrigin and never against the URL
            // being fetched.
            HttpURLConnection c = open(current, "GET", connectMs, readMs, sameOrigin(trustedOrigin, current));
            int code = c.getResponseCode();

            if (isRedirect(code)) {
                String location = c.getHeaderField("Location");
                // A 3xx is not an error status, so the body is on getInputStream(). Drain it so
                // the connection can go back into the keep-alive pool instead of being torn down.
                drainQuietly(quietInput(c));
                c.disconnect();

                if (location == null) {
                    throw new IOException(what + " returned HTTP " + code + " with no Location header");
                }
                if (hop >= MAX_REDIRECTS) {
                    throw new IOException(what + " exceeded " + MAX_REDIRECTS + " redirects");
                }
                // A relative Location is legal and common behind a reverse proxy. resolve()
                // handles both forms.
                URI next = current.resolve(location);
                String scheme = next.getScheme() == null ? "" : next.getScheme().toLowerCase(Locale.ROOT);
                if (!"http".equals(scheme) && !"https".equals(scheme)) {
                    throw new SecurityException("refusing redirect to " + scheme + ": " + next);
                }
                // HttpClient.Redirect.NORMAL, restated: always follow, except HTTPS -> HTTP.
                if ("https".equalsIgnoreCase(current.getScheme()) && "http".equals(scheme)) {
                    throw new SecurityException("refusing HTTPS to HTTP redirect: " + next);
                }
                current = next;
                continue;
            }

            if (code >= 400) {
                // getInputStream() throws at >= 400; the body is on getErrorStream(), which may be
                // null. A bounded slice of it goes into the exception so a 401 can say "bad token"
                // instead of only "HTTP 401".
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
                return sink.read(body); // streamed - a 300 MB jar is never buffered whole
            } finally {
                closeQuietly(body);
                c.disconnect();
            }
        }
    }

    // ---- POST ----

    /**
     * @return the HTTP status. Never follows a redirect: a 3xx on the report POST
     *         is a server misconfiguration, and replaying the body at a new URL
     *         would be a second report rather than the same one.
     */
    int post(String url, byte[] body, int connectMs, int readMs) throws IOException {
        URI target = URI.create(url);
        // Same rule as GET rather than an unconditional true. The report URL is derived from
        // manifestUrl by Syncer.reportUrl, so in practice this is always the trusted origin - but
        // "in practice always" is exactly the assumption the GET path was making when it leaked
        // the token, so it is checked here rather than assumed.
        HttpURLConnection c = open(target, "POST", connectMs, readMs, sameOrigin(trustedOrigin, target));
        c.setDoOutput(true);
        // Fixed length rather than chunked: the body is a few hundred bytes, some reverse proxies
        // still mishandle a chunked request, and this also keeps the body out of an output buffer.
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

    // ---- connection setup ----

    private HttpURLConnection open(URI uri, String method, int connectMs, int readMs, boolean auth)
            throws IOException {
        URLConnection raw = new URL(uri.toString()).openConnection();
        if (!(raw instanceof HttpURLConnection)) {
            throw new IOException("not an http(s) URL: " + uri);
        }
        HttpURLConnection c = (HttpURLConnection) raw;
        // We follow redirects ourselves. HttpURLConnection's own follower refuses to cross
        // protocols, so it silently stops at an http -> https redirect - exactly the redirect a
        // HOPPER server behind a reverse proxy is most likely to issue.
        c.setInstanceFollowRedirects(false);
        c.setConnectTimeout(connectMs);
        c.setReadTimeout(readMs);
        c.setUseCaches(false);
        c.setRequestMethod(method);
        c.setRequestProperty("User-Agent", "HOPPER/1.0");
        // The hash is computed over the bytes written to disk, so the bytes on the wire have to be
        // the bytes in the file. Asking for identity stops a proxy handing us a gzip stream that
        // HttpURLConnection would not transparently decode.
        c.setRequestProperty("Accept-Encoding", "identity");
        // An unset token means an open server: send no header at all rather than an empty one,
        // which reads as a malformed credential.
        if (auth && token != null && !token.trim().isEmpty()) {
            c.setRequestProperty("Authorization", "Bearer " + token);
        }
        return c;
    }

    private static boolean isRedirect(int code) {
        return code == 301 || code == 302 || code == 303 || code == 307 || code == 308;
    }

    /**
     * Scheme, host and port, with the scheme's default port filled in.
     *
     * <p>Package-private so a test can pin the decision that carries the token.
     *
     * <p>The default-port step matters in the safe direction as well as the unsafe
     * one: a manifest configured as {@code https://host/api/manifest} (port -1)
     * that serves its jars from {@code https://host:443/...} is the same origin,
     * and without normalisation HOPPER would quietly stop authenticating against
     * its own server. A null host never matches anything, including another null.
     */
    static boolean sameOrigin(URI a, URI b) {
        if (a == null || b == null || a.getHost() == null || b.getHost() == null) {
            return false;
        }
        return equalsIgnoreCaseOrNull(a.getScheme(), b.getScheme())
                && a.getHost().equalsIgnoreCase(b.getHost())
                && port(a) == port(b);
    }

    /** @return the explicit port, or the scheme default, or -1 for a scheme with no default */
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

    // ---- stream plumbing ----

    /** {@code InputStream.readAllBytes} is Java 9, so this is the Java 8 spelling of it. */
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

    /** First 512 bytes of an error body, on one line, for an exception message. */
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
                // discarded on purpose - we only want the connection released
            }
        } catch (IOException ignored) {
            // nothing to salvage from a body we were throwing away anyway
        } finally {
            closeQuietly(in);
        }
    }

    private static void closeQuietly(java.io.Closeable c) {
        if (c == null) return;
        try {
            c.close();
        } catch (IOException ignored) {
            // a close failure on a stream we are done with changes nothing
        }
    }
}
