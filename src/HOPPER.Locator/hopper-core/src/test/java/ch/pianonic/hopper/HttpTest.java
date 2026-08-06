package ch.pianonic.hopper;

import org.junit.jupiter.api.Test;

import java.net.URI;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

class HttpTest {
    private static final URI SERVER = URI.create("https://hopper.example.com/api/manifest");

    @Test
    void theHopperServerIsItsOwnOrigin() {
        assertTrue(Http.sameOrigin(SERVER, URI.create("https://hopper.example.com/api/manifest")));
        assertTrue(Http.sameOrigin(SERVER, URI.create("https://hopper.example.com/api/clients/report")));
        assertTrue(Http.sameOrigin(SERVER, URI.create("https://hopper.example.com/files/jei.jar")));
    }

    @Test
    void aHostTheManifestNamesIsNotTheOrigin() {
        assertFalse(Http.sameOrigin(SERVER, URI.create("https://cdn.example.com/direct.jar")));
        assertFalse(Http.sameOrigin(SERVER, URI.create("https://evil.example.com/api/manifest")));

        assertFalse(Http.sameOrigin(SERVER, URI.create("https://files.hopper.example.com/x.jar")));

        assertFalse(Http.sameOrigin(SERVER, URI.create("https://hopper.example.com.evil.test/x.jar")));
    }

    @Test
    void schemeAndPortArePartOfTheOrigin() {
        assertFalse(Http.sameOrigin(SERVER, URI.create("http://hopper.example.com/x.jar")));
        assertFalse(Http.sameOrigin(SERVER, URI.create("https://hopper.example.com:8443/x.jar")));
    }

    @Test
    void theDefaultPortIsTheSameAsTheExplicitOne() {
        assertTrue(Http.sameOrigin(SERVER, URI.create("https://hopper.example.com:443/files/x.jar")));
        assertTrue(Http.sameOrigin(URI.create("http://localhost:80/api/manifest"),
                URI.create("http://localhost/api/clients/report")));
        assertTrue(Http.sameOrigin(URI.create("http://localhost:5080/api/manifest"),
                URI.create("http://localhost:5080/files/x.jar")));
    }

    @Test
    void hostAndSchemeCompareCaseInsensitively() {
        assertTrue(Http.sameOrigin(SERVER, URI.create("HTTPS://HOPPER.EXAMPLE.COM/files/x.jar")));
    }

    @Test
    void aHostlessUriIsNeverTheOrigin() {
        assertFalse(Http.sameOrigin(SERVER, URI.create("file:///etc/passwd")));
        assertFalse(Http.sameOrigin(URI.create("file:///a"), URI.create("file:///b")));
        assertFalse(Http.sameOrigin(SERVER, null));
        assertFalse(Http.sameOrigin(null, SERVER));
    }
}
