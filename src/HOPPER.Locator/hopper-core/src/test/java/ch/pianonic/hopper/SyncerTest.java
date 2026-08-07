package ch.pianonic.hopper;

import com.sun.net.httpserver.HttpExchange;
import com.sun.net.httpserver.HttpHandler;
import com.sun.net.httpserver.HttpServer;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.io.IOException;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.net.URI;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Arrays;
import java.util.Collections;
import java.util.List;
import java.util.Set;
import java.util.function.Consumer;
import java.util.zip.ZipEntry;
import java.util.zip.ZipOutputStream;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

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
        Files.write(f, "hopper".getBytes(StandardCharsets.UTF_8));

        assertEquals("392a5bcbd71a7db2cfb9796c633326f7fba6730bdb0c801d3b0fd30886821000",
                Syncer.sha256(f));
    }

    @Test
    void derivesTheReportEndpointFromTheManifestUrl() {
        assertEquals(URI.create("https://hopper.example.com/api/clients/report"),
                Syncer.reportUrl("https://hopper.example.com/api/manifest"));
        assertEquals(URI.create("http://localhost:5080/api/clients/report"),
                Syncer.reportUrl("http://localhost:5080/api/manifest"));

        assertEquals(URI.create("https://home.example.com/hoppermods/api/clients/report"),
                Syncer.reportUrl("https://home.example.com/hoppermods/api/manifest"));
    }

    @Test
    void reportBodyCarriesNoServerId() {
        assertEquals(
                "{\"clientId\":\"c-1\",\"username\":\"steve\","
                        + "\"mods\":[{\"file\":\"jei.jar\",\"sha256\":\"abc\"}]}",
                Syncer.reportBody("c-1", "steve",
                        Collections.singletonList(new Syncer.Mod("jei.jar", "abc"))));
    }

    @Test
    void reportSendsAnAbsentUsernameAsAnExplicitNull() {
        assertEquals("{\"clientId\":\"c-1\",\"username\":null,\"mods\":[]}",
                Syncer.reportBody("c-1", null, Collections.<Syncer.Mod>emptyList()));
    }

    @Test
    void parsesModIdsFromTheManifest() {
        List<Syncer.Entry> mods = Syncer.parseManifest(
                "{\"mods\":[{\"file\":\"jei.jar\",\"url\":\"https://h/x\",\"sha256\":\"abc\","
                        + "\"size\":12,\"modIds\":[\"jei\",\"jeitweaker\"]}]}");

        assertEquals(1, mods.size());
        assertEquals("jei.jar", mods.get(0).file);
        assertEquals(Arrays.asList("jei", "jeitweaker"), mods.get(0).modIds);
    }

    @Test
    void aManifestWithoutModIdsParsesToEmptyIdLists() {
        List<Syncer.Entry> mods = Syncer.parseManifest(
                "{\"mods\":[{\"file\":\"jei.jar\",\"url\":\"https://h/x\",\"sha256\":\"abc\","
                        + "\"size\":12}]}");

        assertEquals("jei.jar", mods.get(0).file);
        assertEquals("abc", mods.get(0).sha256);
        assertEquals(12L, mods.get(0).size);
        assertNotNull(mods.get(0).modIds);
        assertTrue(mods.get(0).modIds.isEmpty());
    }

    @Test
    void nonStringElementsInModIdsAreIgnoredRatherThanFailingTheSync() {
        List<Syncer.Entry> mods = Syncer.parseManifest(
                "{\"mods\":[{\"file\":\"jei.jar\",\"sha256\":\"abc\","
                        + "\"modIds\":[\"jei\",42,null,{\"id\":\"x\"}]}]}");

        assertEquals(Arrays.asList("jei"), mods.get(0).modIds);
    }

    @Test
    void aModIdsFieldThatIsNotAnArrayIsIgnored() {
        List<Syncer.Entry> mods = Syncer.parseManifest(
                "{\"mods\":[{\"file\":\"jei.jar\",\"sha256\":\"abc\",\"modIds\":\"jei\"}]}");

        assertTrue(mods.get(0).modIds.isEmpty());
    }

    @Test
    void aMigratedModIsNotDownloadedAtAll(@TempDir Path game) throws Exception {
        Path mods = Files.createDirectories(game.resolve("mods"));
        Path dir = Files.createDirectories(game.resolve(Hopper.DIR));

        Path mine = jar(mods, "jei-under-my-own-name.jar", "the required build", "jei");
        String sha = Syncer.sha256(mine);

        Stub stub = new Stub();
        try {
            stub.manifest("{\"mods\":[{\"file\":\"jei-1.20.1-15.3.0.4.jar\",\"url\":\""
                    + stub.blobUrl() + "\",\"sha256\":\"" + sha + "\",\"size\":1,"
                    + "\"modIds\":[\"jei\"]}]}");

            Set<String> wanted = new Syncer(stub.manifestUrl(), null, dir, mods, NO_PROGRESS,
                    HopperLog.STDOUT).sync();

            assertEquals(0, stub.downloads, "a migration must cost no bandwidth");
            assertTrue(wanted.contains("jei-1.20.1-15.3.0.4.jar"));
            assertTrue(Files.exists(dir.resolve("jei-1.20.1-15.3.0.4.jar")));
            assertFalse(Files.exists(mine), "and the player's copy must not still be in mods/");
        } finally {
            stub.stop();
        }
    }

    /**
     * Issue #17, the half the first fix missed. A migration and the day that mod leaves the manifest
     * are almost never the same launch, so knowing what was migrated only within one run is not
     * enough: on the next run nothing is migrated, the set is empty, and the sweep deletes the
     * player's own jar. Two separate Syncer instances here on purpose - that is the whole point.
     */
    @Test
    void aJarMigratedInAnEarlierRunIsParkedNotDeletedWhenItLeavesTheManifest(@TempDir Path game)
            throws Exception {
        Path mods = Files.createDirectories(game.resolve("mods"));
        Path dir = Files.createDirectories(game.resolve(Hopper.DIR));

        Path mine = jar(mods, "appleskin-mine.jar", "the required build", "appleskin");
        String sha = Syncer.sha256(mine);

        Stub stub = new Stub();
        try {
            stub.manifest("{\"mods\":[{\"file\":\"appleskin-2.5.1.jar\",\"url\":\""
                    + stub.blobUrl() + "\",\"sha256\":\"" + sha + "\",\"size\":1,"
                    + "\"modIds\":[\"appleskin\"]}]}");

            new Syncer(stub.manifestUrl(), null, dir, mods, NO_PROGRESS, HopperLog.STDOUT).sync();
            assertTrue(Files.exists(dir.resolve("appleskin-2.5.1.jar")), "run 1 migrates it in");

            // A later launch. The admin has dropped the mod, so the manifest is empty and this
            // Syncer knows nothing about the earlier migration except what is on disk.
            stub.manifest("{\"mods\":[]}");
            new Syncer(stub.manifestUrl(), null, dir, mods, NO_PROGRESS, HopperLog.STDOUT).sync();

            assertFalse(Files.exists(dir.resolve("appleskin-2.5.1.jar")),
                    "it is no longer required, so it leaves hoppermods/");
            assertTrue(Files.isDirectory(dir.resolve(Migrator.REPLACED)),
                    "but it must be parked rather than unlinked: it is the player's only copy");
            assertEquals(1, countJars(dir.resolve(Migrator.REPLACED)),
                    "exactly the one file the player owned");
        } finally {
            stub.stop();
        }
    }

    /** A jar HOPPER downloaded itself is not the player's, so it stays deletable. */
    @Test
    void aDownloadedJarIsStillDeletedWhenItLeavesTheManifest(@TempDir Path game) throws Exception {
        Path mods = Files.createDirectories(game.resolve("mods"));
        Path dir = Files.createDirectories(game.resolve(Hopper.DIR));

        Stub stub = new Stub();
        try {
            stub.blob = "downloaded bytes".getBytes(StandardCharsets.UTF_8);
            stub.manifest("{\"mods\":[{\"file\":\"something-1.0.jar\",\"url\":\"" + stub.blobUrl()
                    + "\",\"sha256\":\"" + sha256(stub.blob) + "\",\"size\":1}]}");
            new Syncer(stub.manifestUrl(), null, dir, mods, NO_PROGRESS, HopperLog.STDOUT).sync();
            assertTrue(Files.exists(dir.resolve("something-1.0.jar")));

            stub.manifest("{\"mods\":[]}");
            new Syncer(stub.manifestUrl(), null, dir, mods, NO_PROGRESS, HopperLog.STDOUT).sync();

            assertFalse(Files.exists(dir.resolve("something-1.0.jar")));
            assertEquals(0, countJars(dir.resolve(Migrator.REPLACED)),
                    "nothing to preserve: HOPPER put it there and HOPPER can take it away");
        } finally {
            stub.stop();
        }
    }

    private static int countJars(Path dir) throws IOException {
        if (!Files.isDirectory(dir)) return 0;
        int n = 0;
        java.nio.file.DirectoryStream<Path> ds = Files.newDirectoryStream(dir);
        try {
            for (Path p : ds) if (Files.isRegularFile(p) && !p.getFileName().toString().endsWith(".txt")) n++;
        } finally {
            ds.close();
        }
        return n;
    }

    @Test
    void aDeferredModIsNotDownloadedNotWantedAndItsOldCopyIsSwept(@TempDir Path game)
            throws Exception {
        Path mods = Files.createDirectories(game.resolve("mods"));
        Path dir = Files.createDirectories(game.resolve(Hopper.DIR));

        Path mine = jar(mods, "jei-1.20.1-15.2.0.27.jar", "an older build", "jei");

        Files.write(dir.resolve(Migrator.REPLACED), "not a directory".getBytes(StandardCharsets.UTF_8));

        Path stale = dir.resolve("jei-1.20.1-15.3.0.4.jar");
        Files.write(stale, "the required build".getBytes(StandardCharsets.UTF_8));

        Stub stub = new Stub();
        try {
            stub.manifest("{\"mods\":[{\"file\":\"jei-1.20.1-15.3.0.4.jar\",\"url\":\""
                    + stub.blobUrl() + "\",\"sha256\":\"" + Syncer.sha256(stale) + "\",\"size\":1,"
                    + "\"modIds\":[\"jei\"]}]}");

            Set<String> wanted = new Syncer(stub.manifestUrl(), null, dir, mods, NO_PROGRESS,
                    HopperLog.STDOUT).sync();

            assertTrue(wanted.isEmpty(), "a deferred entry must not be in wanted");
            assertEquals(0, stub.downloads);
            assertTrue(Files.exists(mine), "the player's jar loads from mods/ this launch");
            assertFalse(Files.exists(stale), "so the copy in hoppermods/ has to go, or the mod"
                    + " would be loaded twice");
        } finally {
            stub.stop();
        }
    }

    @Test
    void anOrdinaryModIsStillDownloadedTheWayItAlwaysWas(@TempDir Path game) throws Exception {
        Path mods = Files.createDirectories(game.resolve("mods"));
        Path dir = Files.createDirectories(game.resolve(Hopper.DIR));

        byte[] body = "a mod the player does not have".getBytes(StandardCharsets.UTF_8);

        Stub stub = new Stub();
        try {
            stub.blob = body;
            stub.manifest("{\"mods\":[{\"file\":\"create.jar\",\"url\":\"" + stub.blobUrl()
                    + "\",\"sha256\":\"" + sha256(body) + "\",\"size\":" + body.length
                    + ",\"modIds\":[\"create\"]}]}");

            Set<String> wanted = new Syncer(stub.manifestUrl(), null, dir, mods, NO_PROGRESS,
                    HopperLog.STDOUT).sync();

            assertEquals(1, stub.downloads);
            assertTrue(wanted.contains("create.jar"));
            assertTrue(Files.exists(dir.resolve("create.jar")));
        } finally {
            stub.stop();
        }
    }

    private static final Consumer<String> NO_PROGRESS = new Consumer<String>() {
        @Override
        public void accept(String message) {
        }
    };

    private static String sha256(byte[] body) throws Exception {
        Path tmp = Files.createTempFile("hopper", ".bin");
        try {
            Files.write(tmp, body);
            return Syncer.sha256(tmp);
        } finally {
            Files.deleteIfExists(tmp);
        }
    }

    private static Path jar(Path dir, String name, String payload, String... ids) throws Exception {
        StringBuilder toml = new StringBuilder();
        for (String id : ids) {
            toml.append("[[mods]]\nmodId = \"").append(id).append("\"\n");
        }

        Path f = dir.resolve(name);
        OutputStream raw = Files.newOutputStream(f);
        try {
            ZipOutputStream zip = new ZipOutputStream(raw);
            zip.putNextEntry(new ZipEntry("META-INF/mods.toml"));
            zip.write(toml.toString().getBytes(StandardCharsets.UTF_8));
            zip.closeEntry();
            zip.putNextEntry(new ZipEntry("payload.txt"));
            zip.write(payload.getBytes(StandardCharsets.UTF_8));
            zip.closeEntry();
            zip.finish();
            zip.close();
        } finally {
            raw.close();
        }
        return f;
    }

    private static final class Stub {
        private final HttpServer server;
        private byte[] manifest = new byte[0];
        byte[] blob = new byte[0];
        int downloads;

        Stub() throws IOException {
            server = HttpServer.create(new InetSocketAddress("127.0.0.1", 0), 0);
            server.createContext("/api/manifest", new HttpHandler() {
                @Override
                public void handle(HttpExchange exchange) throws IOException {
                    respond(exchange, manifest);
                }
            });
            server.createContext("/api/blobs/", new HttpHandler() {
                @Override
                public void handle(HttpExchange exchange) throws IOException {
                    downloads++;
                    respond(exchange, blob);
                }
            });
            server.start();
        }

        private static void respond(HttpExchange exchange, byte[] body) throws IOException {
            exchange.sendResponseHeaders(200, body.length);
            OutputStream out = exchange.getResponseBody();
            try {
                out.write(body);
            } finally {
                out.close();
            }
        }

        void manifest(String json) {
            manifest = json.getBytes(StandardCharsets.UTF_8);
        }

        String manifestUrl() {
            return "http://127.0.0.1:" + server.getAddress().getPort() + "/api/manifest";
        }

        String blobUrl() {
            return "http://127.0.0.1:" + server.getAddress().getPort() + "/api/blobs/x";
        }

        void stop() {
            server.stop(0);
        }
    }
}
