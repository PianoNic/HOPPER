package ch.pianonic.hopper;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.net.URI;
import java.nio.file.Files;
import java.nio.file.Path;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;

/**
 * Covers the three things that must not silently break: the filename trust
 * boundary, the hash the whole sync decision rests on, and the report URL
 * derived from the one URL anyone configures.
 */
class SyncerTest {

    @Test
    void rejectsFilenamesThatEscapeTheManagedDirectory() {
        assertThrows(SecurityException.class, () -> Syncer.sanitize("../../autostart/evil.jar"));
        assertThrows(SecurityException.class, () -> Syncer.sanitize("sub/dir/mod.jar"));
        assertThrows(SecurityException.class, () -> Syncer.sanitize("sub\\dir\\mod.jar"));
        assertThrows(SecurityException.class, () -> Syncer.sanitize(".hidden.jar"));
        assertThrows(SecurityException.class, () -> Syncer.sanitize("payload.exe"));
        assertThrows(IllegalArgumentException.class, () -> Syncer.sanitize("  "));
    }

    @Test
    void acceptsAnOrdinaryModFilename() {
        assertEquals("jei-1.20.1-15.2.0.27.jar", Syncer.sanitize("jei-1.20.1-15.2.0.27.jar"));
    }

    @Test
    void hashesTheSameWayTheServerDoes(@TempDir Path dir) throws Exception {
        Path f = dir.resolve("a.jar");
        Files.writeString(f, "hopper");
        // python -c "import hashlib;print(hashlib.sha256(b'hopper').hexdigest())"
        assertEquals("392a5bcbd71a7db2cfb9796c633326f7fba6730bdb0c801d3b0fd30886821000",
                Syncer.sha256(f));
    }

    /**
     * Only manifestUrl is configured, so a wrong derivation here would send every
     * client's report to a 404 that nothing in the game would ever surface.
     */
    @Test
    void derivesTheReportEndpointFromTheManifestUrl() {
        assertEquals(URI.create("https://hopper.example.com/api/clients/report"),
                Syncer.reportUrl("https://hopper.example.com/api/manifest"));
        assertEquals(URI.create("http://localhost:5080/api/clients/report"),
                Syncer.reportUrl("http://localhost:5080/api/manifest"));
        // A host serving HOPPER under a prefix keeps the prefix.
        assertEquals(URI.create("https://home.example.com/hopper/api/clients/report"),
                Syncer.reportUrl("https://home.example.com/hopper/api/manifest"));
    }
}
