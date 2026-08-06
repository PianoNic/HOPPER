package ch.pianonic.hopper;

import ch.pianonic.hopper.HopperLocator.Config;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Properties;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * The precedence rule that makes a downloaded jar work with zero configuration.
 * {@link Config#merge} is the whole of it, kept free of IO precisely so it can be
 * asserted here rather than only in an end-to-end run of a real game.
 *
 * <p>That the resource is genuinely readable out of a real jar is a different
 * question, answered by building one - see the locator README.
 */
class ConfigTest {

    private static Properties props(String... keyThenValue) {
        Properties p = new Properties();
        for (int i = 0; i < keyThenValue.length; i += 2) {
            p.setProperty(keyThenValue[i], keyThenValue[i + 1]);
        }
        return p;
    }

    /** The whole point: a downloaded jar ignores whatever a stale file still says. */
    @Test
    void theJarWinsOverTheFile() {
        Config c = Config.merge(
                props("serverId", "6f1a…", "manifestUrl", "https://mine.example.com/api/manifest",
                        "token", "from-jar"),
                props("manifestUrl", "https://stale.example.com/api/manifest", "token", "from-file"));

        assertEquals("6f1a…", c.serverId());
        assertEquals("https://mine.example.com/api/manifest", c.manifestUrl());
        assertEquals("from-jar", c.token());
    }

    /**
     * Per key, not per file. HOPPER never writes {@code enabled} into a jar, so a
     * player keeps a local kill switch on a jar that configures everything else.
     */
    @Test
    void enabledStillComesFromTheFileOnASelfConfiguredJar() {
        Config c = Config.merge(
                props("manifestUrl", "https://mine.example.com/api/manifest", "token", "t"),
                props("enabled", "false"));

        assertFalse(c.enabled());
        assertEquals("https://mine.example.com/api/manifest", c.manifestUrl());
    }

    /** An unpatched template jar has to behave exactly like a jar with no embedded file. */
    @Test
    void blankEmbeddedValuesFallThroughToTheFile() {
        Config c = Config.merge(
                props("serverId", "  ", "manifestUrl", "", "token", " "),
                props("manifestUrl", "https://file.example.com/api/manifest", "token", "from-file"));

        assertNull(c.serverId());
        assertEquals("https://file.example.com/api/manifest", c.manifestUrl());
        assertEquals("from-file", c.token());
    }

    /** A hand-built jar keeps the original file-only behaviour, untouched. */
    @Test
    void withNothingEmbeddedTheFileIsTheWholeConfiguration() {
        Config c = Config.merge(
                new Properties(),
                props("manifestUrl", "https://file.example.com/api/manifest", "token", "from-file"));

        assertNull(c.serverId());
        assertEquals("https://file.example.com/api/manifest", c.manifestUrl());
        assertEquals("from-file", c.token());
        assertTrue(c.enabled());
    }

    /** An empty token means "open server": Syncer reads null as "send no header at all". */
    @Test
    void anEmptyTokenIsNullRatherThanAnEmptyHeader() {
        assertNull(Config.merge(new Properties(), props("token", "   ")).token());
    }

    /** Syncing is the default, so a file that says nothing must not disable it. */
    @Test
    void syncingIsOnUnlessSomethingTurnsItOff() {
        assertTrue(Config.merge(new Properties(), new Properties()).enabled());
        assertFalse(Config.merge(new Properties(), props("enabled", "false")).enabled());
        assertTrue(Config.merge(props("enabled", "true"), props("enabled", "false")).enabled());
    }

    /**
     * No {@code /hopper-server.properties} on the test classpath, so this is the
     * hand-built-jar path end to end: the file is created on first launch and read
     * back on the next one.
     */
    @Test
    void writesAConfigFileOnFirstLaunchAndReadsItBack(@TempDir Path gameDir) throws Exception {
        Config first = Config.load(gameDir);
        Path f = gameDir.resolve("config/hopper.properties");

        assertTrue(Files.exists(f));
        assertTrue(first.enabled());
        assertNull(first.token());
        assertNull(first.serverId());

        Files.writeString(f, "enabled=true\nmanifestUrl=https://home.example.com/api/manifest\ntoken=abc\n");
        Config second = Config.load(gameDir);

        assertEquals("https://home.example.com/api/manifest", second.manifestUrl());
        assertEquals("abc", second.token());
    }

    /**
     * The resource name is a contract with LocatorJarBuilder on the server, which
     * writes the archive entry {@code hopper-server.properties} at the root. A
     * leading slash here and no package there is what makes those the same file.
     */
    @Test
    void looksForTheEntryTheServerWrites() {
        assertEquals("/hopper-server.properties", Config.EMBEDDED);
    }
}
