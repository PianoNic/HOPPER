package ch.pianonic.hopper;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.nio.charset.StandardCharsets;
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
 * <p>{@code Config} is now a top-level class in the core rather than a record
 * nested in the Forge locator - it is a plain class because records are Java 16
 * and the core compiles at 8, and it is top-level because it is shared by six
 * adapters. Its accessor names are unchanged, so every assertion below is.
 *
 * <p>That the resource is genuinely readable out of a real jar is a different
 * question, answered by building one.
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

        Files.write(f, "enabled=true\nmanifestUrl=https://home.example.com/api/manifest\ntoken=abc\n"
                .getBytes(StandardCharsets.UTF_8));
        Config second = Config.load(gameDir);

        assertEquals("https://home.example.com/api/manifest", second.manifestUrl());
        assertEquals("abc", second.token());
    }

    /**
     * The one key that does not merge, and the reason it does not.
     *
     * <p>{@code fabricMirrorMods} is the player's consent to HOPPER writing into
     * their {@code mods/} directory - copying jars in, deleting jars out. The
     * embedded properties file is written by the HOPPER server, so honouring it
     * there would let a server grant itself write access to a directory it does
     * not own. Every other key takes the jar's value first; this one ignores it
     * entirely.
     */
    @Test
    void aServerCannotGrantItselfPermissionToWriteIntoTheModsFolder() {
        assertFalse(Config.merge(props("fabricMirrorMods", "true"), new Properties()).mirrorMods());
        assertFalse(Config.merge(props("fabricMirrorMods", "true"),
                props("fabricMirrorMods", "false")).mirrorMods());
        assertTrue(Config.merge(props("fabricMirrorMods", "false"),
                props("fabricMirrorMods", "true")).mirrorMods());
    }

    /** Absent, blank or misspelt all mean no. The failure has to be the safe one. */
    @Test
    void theModsFolderIsOffUnlessThePlayerSaysOtherwise() {
        assertFalse(Config.merge(new Properties(), new Properties()).mirrorMods());
        assertFalse(Config.merge(new Properties(), props("fabricMirrorMods", "   ")).mirrorMods());
        assertFalse(Config.merge(new Properties(), props("fabricMirrorMods", "yes")).mirrorMods());
        assertFalse(Config.merge(new Properties(), props("fabricMirrorMods", "1")).mirrorMods());
        assertTrue(Config.merge(new Properties(), props("fabricMirrorMods", "true")).mirrorMods());
        assertTrue(Config.merge(new Properties(), props("fabricMirrorMods", " TRUE ")).mirrorMods());
    }

    /**
     * The template written on first launch has to name the key, spell out what
     * turning it on allows, and ship it off. A player who never opens the file
     * ends up with a HOPPER that has not touched their mods folder.
     */
    @Test
    void theGeneratedConfigDocumentsTheModsFolderOptIn(@TempDir Path gameDir) throws Exception {
        Config.load(gameDir);
        String written = new String(
                Files.readAllBytes(gameDir.resolve("config/hopper.properties")),
                StandardCharsets.UTF_8);

        assertTrue(written.contains("fabricMirrorMods=false"), written);
        assertTrue(written.contains("mods-mirror.txt"), written);
        assertFalse(Config.load(gameDir).mirrorMods());
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
