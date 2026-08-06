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

class ConfigTest {
    private static Properties props(String... keyThenValue) {
        Properties p = new Properties();
        for (int i = 0; i < keyThenValue.length; i += 2) {
            p.setProperty(keyThenValue[i], keyThenValue[i + 1]);
        }
        return p;
    }

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

    @Test
    void enabledStillComesFromTheFileOnASelfConfiguredJar() {
        Config c = Config.merge(
                props("manifestUrl", "https://mine.example.com/api/manifest", "token", "t"),
                props("enabled", "false"));

        assertFalse(c.enabled());
        assertEquals("https://mine.example.com/api/manifest", c.manifestUrl());
    }

    @Test
    void blankEmbeddedValuesFallThroughToTheFile() {
        Config c = Config.merge(
                props("serverId", "  ", "manifestUrl", "", "token", " "),
                props("manifestUrl", "https://file.example.com/api/manifest", "token", "from-file"));

        assertNull(c.serverId());
        assertEquals("https://file.example.com/api/manifest", c.manifestUrl());
        assertEquals("from-file", c.token());
    }

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

    @Test
    void anEmptyTokenIsNullRatherThanAnEmptyHeader() {
        assertNull(Config.merge(new Properties(), props("token", "   ")).token());
    }

    @Test
    void syncingIsOnUnlessSomethingTurnsItOff() {
        assertTrue(Config.merge(new Properties(), new Properties()).enabled());
        assertFalse(Config.merge(new Properties(), props("enabled", "false")).enabled());
        assertTrue(Config.merge(props("enabled", "true"), props("enabled", "false")).enabled());
    }

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

    @Test
    void aServerCannotGrantItselfPermissionToWriteIntoTheModsFolder() {
        assertFalse(Config.merge(props("fabricMirrorMods", "true"), new Properties()).mirrorMods());
        assertFalse(Config.merge(props("fabricMirrorMods", "true"),
                props("fabricMirrorMods", "false")).mirrorMods());
        assertTrue(Config.merge(props("fabricMirrorMods", "false"),
                props("fabricMirrorMods", "true")).mirrorMods());
    }

    @Test
    void theModsFolderIsOffUnlessThePlayerSaysOtherwise() {
        assertFalse(Config.merge(new Properties(), new Properties()).mirrorMods());
        assertFalse(Config.merge(new Properties(), props("fabricMirrorMods", "   ")).mirrorMods());
        assertFalse(Config.merge(new Properties(), props("fabricMirrorMods", "yes")).mirrorMods());
        assertFalse(Config.merge(new Properties(), props("fabricMirrorMods", "1")).mirrorMods());
        assertTrue(Config.merge(new Properties(), props("fabricMirrorMods", "true")).mirrorMods());
        assertTrue(Config.merge(new Properties(), props("fabricMirrorMods", " TRUE ")).mirrorMods());
    }

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

    @Test
    void looksForTheEntryTheServerWrites() {
        assertEquals("/hopper-server.properties", Config.EMBEDDED);
    }
}
