package ch.pianonic.hopper;

import org.junit.jupiter.api.Test;

import java.net.URI;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * The decision that carries the bearer token.
 *
 * <p>{@code Http.get} attaches {@code Authorization} when, and only when, the URL
 * it is about to fetch is the same origin as the configured HOPPER server. That
 * comparison used to be made against the request's own URL, which made it
 * trivially true on the first hop of every request - so a manifest that named a
 * third-party host directly in a mod's {@code url} got the server token handed to
 * it. The redirect path was safe and the direct path was not, which is exactly the
 * kind of asymmetry a unit test is for.
 *
 * <p>Compiled at {@code --release 8}, like the core it tests.
 */
class HttpTest {

    private static final URI SERVER = URI.create("https://hopper.example.com/api/manifest");

    /** The server's own endpoints, so the token has to go. */
    @Test
    void theHopperServerIsItsOwnOrigin() {
        assertTrue(Http.sameOrigin(SERVER, URI.create("https://hopper.example.com/api/manifest")));
        assertTrue(Http.sameOrigin(SERVER, URI.create("https://hopper.example.com/api/clients/report")));
        assertTrue(Http.sameOrigin(SERVER, URI.create("https://hopper.example.com/files/jei.jar")));
    }

    /**
     * The finding, in one assertion. A mod entry whose {@code url} points at a CDN
     * is server-supplied data, no more trusted than the filenames
     * {@link Syncer#sanitize} rejects, and it must not be able to name itself the
     * origin.
     */
    @Test
    void aHostTheManifestNamesIsNotTheOrigin() {
        assertFalse(Http.sameOrigin(SERVER, URI.create("https://cdn.example.com/direct.jar")));
        assertFalse(Http.sameOrigin(SERVER, URI.create("https://evil.example.com/api/manifest")));
        // A subdomain is a different host. Cookies are laxer than this on purpose; a bearer
        // token should not be.
        assertFalse(Http.sameOrigin(SERVER, URI.create("https://files.hopper.example.com/x.jar")));
        // And a prefix match is not a host match, which is how "hopper.example.com.evil.test"
        // would otherwise pass.
        assertFalse(Http.sameOrigin(SERVER, URI.create("https://hopper.example.com.evil.test/x.jar")));
    }

    /** Downgrading the scheme or changing the port is a different origin. */
    @Test
    void schemeAndPortArePartOfTheOrigin() {
        assertFalse(Http.sameOrigin(SERVER, URI.create("http://hopper.example.com/x.jar")));
        assertFalse(Http.sameOrigin(SERVER, URI.create("https://hopper.example.com:8443/x.jar")));
    }

    /**
     * The default port is filled in, so a server configured without one and jars
     * served with one - or the reverse - keeps authenticating. Without this the
     * fix would fail closed in a way that looks like a broken token.
     */
    @Test
    void theDefaultPortIsTheSameAsTheExplicitOne() {
        assertTrue(Http.sameOrigin(SERVER, URI.create("https://hopper.example.com:443/files/x.jar")));
        assertTrue(Http.sameOrigin(URI.create("http://localhost:80/api/manifest"),
                URI.create("http://localhost/api/clients/report")));
        assertTrue(Http.sameOrigin(URI.create("http://localhost:5080/api/manifest"),
                URI.create("http://localhost:5080/files/x.jar")));
    }

    /** Case is not part of a host name, but it is not a way past the check either. */
    @Test
    void hostAndSchemeCompareCaseInsensitively() {
        assertTrue(Http.sameOrigin(SERVER, URI.create("HTTPS://HOPPER.EXAMPLE.COM/files/x.jar")));
    }

    /**
     * A URI with no host - {@code file:}, {@code mailto:}, a bare path - matches
     * nothing, including another hostless URI. Two nulls comparing equal would
     * have made every one of them the origin.
     */
    @Test
    void aHostlessUriIsNeverTheOrigin() {
        assertFalse(Http.sameOrigin(SERVER, URI.create("file:///etc/passwd")));
        assertFalse(Http.sameOrigin(URI.create("file:///a"), URI.create("file:///b")));
        assertFalse(Http.sameOrigin(SERVER, null));
        assertFalse(Http.sameOrigin(null, SERVER));
    }
}
