package ch.pianonic.hopper;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.zip.ZipEntry;
import java.util.zip.ZipOutputStream;

import static org.junit.jupiter.api.Assertions.assertArrayEquals;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * The migration, driven against real jars on a real filesystem in a real
 * directory layout. Nothing is mocked and no loader is involved, which is the
 * only honest way to test this: the failures that matter are filesystem
 * failures.
 *
 * <p>Every test here is ultimately checking one invariant - <strong>the same mod
 * is never loadable twice</strong> - including in the arms where the move fails.
 * {@link #loadableCopies} spells that out as an assertion rather than leaving it
 * as a description.
 */
class MigratorTest {

    private static final String JEI_OLD = "jei-1.20.1-15.2.0.27.jar";
    private static final String JEI_NEW = "jei-1.20.1-15.3.0.4.jar";

    private Path mods;
    private Path hopper;

    /** A real jar declaring {@code ids}, plus a payload that makes its hash unique. */
    private static Path jar(Path dir, String name, String payload, String... ids) throws Exception {
        Files.createDirectories(dir);

        StringBuilder toml = new StringBuilder("modLoader=\"javafml\"\nloaderVersion=\"[47,)\"\n");
        for (String id : ids) {
            toml.append("[[mods]]\nmodId = \"").append(id).append("\"\nversion = \"1.0\"\n");
        }

        Path f = dir.resolve(name);
        OutputStream raw = Files.newOutputStream(f);
        try {
            ZipOutputStream zip = new ZipOutputStream(raw);
            if (ids.length > 0) {
                zip.putNextEntry(new ZipEntry("META-INF/mods.toml"));
                zip.write(toml.toString().getBytes(StandardCharsets.UTF_8));
                zip.closeEntry();
            }
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

    /** A 64-character hex hash that nothing on disk will ever match. String.repeat is Java 11. */
    private static String sha(String digit) {
        StringBuilder sb = new StringBuilder(64);
        for (int i = 0; i < 64; i++) sb.append(digit);
        return sb.toString();
    }

    private static Syncer.Entry entry(String file, String sha256, String... ids) {
        Syncer.Entry e = new Syncer.Entry();
        e.file = file;
        e.url = "https://hopper.example.com/api/blobs/" + sha256;
        e.sha256 = sha256;
        e.size = 1;
        e.modIds = new ArrayList<String>(Arrays.asList(ids));
        return e;
    }

    private Migrator.Result run(Path modsDir, Syncer.Entry... manifest) {
        return new Migrator(modsDir, hopper, HopperLog.STDOUT).run(Arrays.asList(manifest));
    }

    private void layout(Path game) throws Exception {
        mods = game.resolve("mods");
        hopper = game.resolve(Hopper.DIR);
        Files.createDirectories(mods);
        Files.createDirectories(hopper);
    }

    /**
     * How many copies of a mod a loader would end up with: what is left in
     * {@code mods/} plus what is in {@code hoppermods/}, counting the download
     * the sync is about to make for every entry that was not blocked.
     */
    private int loadableCopies(Migrator.Result result, Syncer.Entry e, String... modsJars)
            throws Exception {
        int copies = 0;
        for (String name : modsJars) {
            if (Files.exists(mods.resolve(name))) copies++;
        }
        // The download loop writes hopper/<file> unless the entry was blocked, so the file is there
        // afterwards either way. What decides it is whether the entry was blocked.
        if (!result.blocked.contains(e.file)) copies++;
        return copies;
    }

    // ---------------------------------------------------------------- case (a), hash matches

    @Test
    void movesTheMatchingBuildIntoHopperModsUnderTheManifestFilename(@TempDir Path game)
            throws Exception {
        layout(game);
        // Same bytes, different filename: the player downloaded the required build themselves.
        Path mine = jar(mods, "jei-whatever-i-called-it.jar", "required build", "jei");
        Syncer.Entry e = entry(JEI_NEW, Syncer.sha256(mine), "jei");

        Migrator.Result result = run(mods, e);

        assertFalse(Files.exists(mine), "the player's copy must have left mods/");
        assertTrue(Files.exists(hopper.resolve(JEI_NEW)), "it must be in hoppermods/ under the"
                + " manifest's filename, so the download loop hashes it and skips the download");
        assertTrue(result.blocked.isEmpty());
        assertEquals(1, result.moved);
        assertEquals(0, result.parked);
        assertEquals(1, loadableCopies(result, e, "jei-whatever-i-called-it.jar"));
    }

    @Test
    void aMovedJarKeepsItsBytesSoTheDownloadLoopMatchesTheManifestHash(@TempDir Path game)
            throws Exception {
        layout(game);
        Path mine = jar(mods, "jei-mine.jar", "required build", "jei");
        String sha = Syncer.sha256(mine);

        run(mods, entry(JEI_NEW, sha, "jei"));

        // This is the whole "no bandwidth" claim: Syncer.sync() finds the file, hashes it, and the
        // existing check at Files.exists(target) absorbs the migration with no special case.
        assertEquals(sha, Syncer.sha256(hopper.resolve(JEI_NEW)));
    }

    // ---------------------------------------------------------------- case (b), hash differs

    @Test
    void differentFilenameDifferentHashSameModIdIsParkedAndTheRequiredBuildIsDownloaded(
            @TempDir Path game) throws Exception {
        layout(game);
        // Literally the case this whole feature exists for.
        Path mine = jar(mods, JEI_OLD, "an older build", "jei");
        Syncer.Entry e = entry(JEI_NEW, sha("0"), "jei");

        Migrator.Result result = run(mods, e);

        assertFalse(Files.exists(mine), "the older build must have left mods/");
        assertTrue(Files.exists(hopper.resolve(Migrator.REPLACED)
                .resolve(JEI_OLD + Migrator.PARKED_SUFFIX)), "and must still exist, parked");
        assertEquals(1, result.parked);
        assertEquals(0, result.moved);
        // Not blocked, so the required build is downloaded normally - and is then the only copy.
        assertTrue(result.blocked.isEmpty());
        assertEquals(1, loadableCopies(result, e, JEI_OLD));
    }

    @Test
    void aParkedFileIsNotAJarSoNoLoaderCanSeeIt(@TempDir Path game) throws Exception {
        layout(game);
        jar(mods, JEI_OLD, "an older build", "jei");

        run(mods, entry(JEI_NEW, sha("0"), "jei"));

        Path parked = hopper.resolve(Migrator.REPLACED).resolve(JEI_OLD + Migrator.PARKED_SUFFIX);
        assertTrue(Files.exists(parked));
        // The original name stays readable, and the suffix is what makes it inert on Fabric and
        // Quilt too, whose folder scans are not flat.
        assertTrue(parked.getFileName().toString().startsWith(JEI_OLD));
        assertFalse(parked.getFileName().toString().endsWith(".jar"));
    }

    @Test
    void parkingTwiceDoesNotOverwriteTheFirstParkedCopy(@TempDir Path game) throws Exception {
        layout(game);
        Syncer.Entry e = entry(JEI_NEW, sha("0"), "jei");

        jar(mods, JEI_OLD, "the first old build", "jei");
        run(mods, e);

        // Next launch. The player put another build back by hand under the same name.
        jar(mods, JEI_OLD, "a second, different old build", "jei");
        run(mods, e);

        Path replaced = hopper.resolve(Migrator.REPLACED);
        assertTrue(Files.exists(replaced.resolve(JEI_OLD + Migrator.PARKED_SUFFIX)));
        assertTrue(Files.exists(replaced.resolve(
                "jei-1.20.1-15.2.0.27-1.jar" + Migrator.PARKED_SUFFIX)),
                "nothing in replaced/ is ever deleted, so the second park must not overwrite");
    }

    @Test
    void aReadmeIsWrittenTheFirstTimeSomethingIsParked(@TempDir Path game) throws Exception {
        layout(game);
        jar(mods, JEI_OLD, "an older build", "jei");

        run(mods, entry(JEI_NEW, sha("0"), "jei"));

        Path readme = hopper.resolve(Migrator.REPLACED).resolve("README.txt");
        assertTrue(Files.exists(readme));
        String text = new String(Files.readAllBytes(readme), StandardCharsets.UTF_8);
        assertTrue(text.contains(Migrator.PARKED_SUFFIX));
        assertFalse(text.contains("—"), "no em dashes anywhere, including in what we ship");
    }

    // ---------------------------------------------------------------- case (c), the move fails

    @Test
    void deferredWhenTheWinnerCannotBeMoved(@TempDir Path game) throws Exception {
        layout(game);
        Path mine = jar(mods, "jei-mine.jar", "required build", "jei");
        Syncer.Entry e = entry(JEI_NEW, Syncer.sha256(mine), "jei");

        // A real, forced move failure on a real filesystem, standing in for the Windows case where
        // ModDirTransformerDiscoverer is holding the jar open: the destination is a non-empty
        // directory, so Files.move with REPLACE_EXISTING throws DirectoryNotEmptyException.
        Files.createDirectories(hopper.resolve(JEI_NEW).resolve("occupied"));

        Migrator.Result result = run(mods, e);

        assertTrue(Files.exists(mine), "the player's jar must still be in mods/, loading from there");
        assertEquals(1, result.deferred);
        assertEquals(0, result.moved);
        assertTrue(result.blocked.contains(JEI_NEW), "so sync() skips the download entirely");
        assertEquals(1, loadableCopies(result, e, "jei-mine.jar"));
    }

    @Test
    void deferredWhenALoserCannotBeParkedAndTheWinnerIsLeftAlone(@TempDir Path game)
            throws Exception {
        layout(game);
        Path loser = jar(mods, "aaa-jei-old.jar", "an older build", "jei");
        Path winner = jar(mods, "zzz-jei.jar", "required build", "jei");
        Syncer.Entry e = entry(JEI_NEW, Syncer.sha256(winner), "jei");

        // replaced/ cannot be created because a regular file is already sitting on the name.
        Files.write(hopper.resolve(Migrator.REPLACED), "not a directory".getBytes(StandardCharsets.UTF_8));

        Migrator.Result result = run(mods, e);

        assertTrue(Files.exists(loser));
        assertTrue(Files.exists(winner), "losers are parked FIRST precisely so that a failure there"
                + " leaves nothing in hoppermods/ to undo");
        assertFalse(Files.exists(hopper.resolve(JEI_NEW)));
        assertEquals(0, result.moved);
        assertEquals(1, result.deferred);
        assertTrue(result.blocked.contains(JEI_NEW));
    }

    // ---------------------------------------------------------------- two jars, one id

    @Test
    void twoJarsInModsFolderDeclaringOneIdLeaveExactlyOneLoadableCopy(@TempDir Path game)
            throws Exception {
        layout(game);
        // The hash-matching jar sorts LAST. Iterating per manifest entry rather than per jar is
        // what lets it win anyway.
        jar(mods, "aaa-jei-old.jar", "an older build", "jei");
        Path winner = jar(mods, "zzz-jei.jar", "required build", "jei");
        Syncer.Entry e = entry(JEI_NEW, Syncer.sha256(winner), "jei");

        Migrator.Result result = run(mods, e);

        assertFalse(Files.exists(mods.resolve("aaa-jei-old.jar")));
        assertFalse(Files.exists(mods.resolve("zzz-jei.jar")));
        assertTrue(Files.exists(hopper.resolve(JEI_NEW)));
        assertTrue(Files.exists(hopper.resolve(Migrator.REPLACED)
                .resolve("aaa-jei-old.jar" + Migrator.PARKED_SUFFIX)));
        assertEquals(1, result.moved);
        assertEquals(1, result.parked);
        assertEquals(1, loadableCopies(result, e, "aaa-jei-old.jar", "zzz-jei.jar"));
    }

    @Test
    void serverListingOneModIdTwiceMigratesNothing(@TempDir Path game) throws Exception {
        layout(game);
        Path mine = jar(mods, JEI_OLD, "an older build", "jei");

        // The server is distributing two jars that declare one id. The loader will refuse to start
        // on that whatever HOPPER does, and there is no way to know which one the player's jar
        // duplicates, so nothing is migrated on that id.
        Migrator.Result result = run(mods,
                entry("jei-a.jar", sha("1"), "jei"),
                entry("jei-b.jar", sha("2"), "jei"));

        assertTrue(Files.exists(mine));
        assertEquals(0, result.moved);
        assertEquals(0, result.parked);
        assertEquals(0, result.deferred);
        assertTrue(result.blocked.isEmpty());
    }

    // ---------------------------------------------------------------- never touched

    @Test
    void aJarTheManifestDoesNotListIsNeverTouched(@TempDir Path game) throws Exception {
        layout(game);
        Path unrelated = jar(mods, "create-1.20.1-6.0.8.jar", "create", "create");
        Path noIds = jar(mods, "some-library.jar", "a library with no metadata at all");

        Migrator.Result result = run(mods, entry(JEI_NEW, sha("0"), "jei"));

        assertTrue(Files.exists(unrelated));
        assertTrue(Files.exists(noIds));
        assertEquals(0, result.moved + result.parked + result.deferred);
    }

    @Test
    void aJarDeclaringIdsFromTwoDifferentManifestEntriesIsNeverTouched(@TempDir Path game)
            throws Exception {
        layout(game);
        // An all-in-one jar declaring both ids. HOPPER does not guess which of the two manifest
        // files it is a copy of.
        Path both = jar(mods, "combined.jar", "both mods in one", "jei", "create");

        Migrator.Result result = run(mods,
                entry(JEI_NEW, sha("1"), "jei"),
                entry("create-1.20.1.jar", sha("2"), "create"));

        assertTrue(Files.exists(both));
        assertEquals(0, result.moved + result.parked + result.deferred);
    }

    @Test
    void anUnreadableJarIsSkippedRatherThanFailingTheLaunch(@TempDir Path game) throws Exception {
        layout(game);
        // Not a zip at all. Reading it must produce a log line and no ids, and the migration must
        // carry on with the jars it CAN read.
        Path broken = mods.resolve("corrupt.jar");
        Files.write(broken, "PK not really a zip".getBytes(StandardCharsets.UTF_8));
        Path mine = jar(mods, "jei-mine.jar", "required build", "jei");
        Syncer.Entry e = entry(JEI_NEW, Syncer.sha256(mine), "jei");

        Migrator.Result result = run(mods, e);

        assertTrue(Files.exists(broken), "an unreadable jar is left exactly where it is");
        assertEquals(1, result.moved);
        assertTrue(result.blocked.isEmpty());
    }

    @Test
    void aJarWithNoModIdsIsNeverTouched(@TempDir Path game) throws Exception {
        layout(game);
        // A coremod or a plain library. Extremely common and entirely legitimate.
        Path library = jar(mods, "Registrate-MC1.20-1.3.3.jar", "no metadata");

        Migrator.Result result = run(mods, entry(JEI_NEW, sha("0"), "jei"));

        assertTrue(Files.exists(library));
        assertEquals(0, result.moved + result.parked + result.deferred);
    }

    @Test
    void subdirectoriesOfTheModsFolderAreNotScanned(@TempDir Path game) throws Exception {
        layout(game);
        // A version-named subfolder of mods/ is a Fabric and Quilt feature and HOPPER does not
        // manage it either.
        Path nested = jar(mods.resolve("1.20.1"), JEI_OLD, "an older build", "jei");

        Migrator.Result result = run(mods, entry(JEI_NEW, sha("0"), "jei"));

        assertTrue(Files.exists(nested));
        assertEquals(0, result.moved + result.parked + result.deferred);
    }

    @Test
    void aNonJarFileInTheModsFolderIsNeverTouched(@TempDir Path game) throws Exception {
        layout(game);
        Path disabled = mods.resolve(JEI_OLD + ".disabled");
        Files.write(disabled, "an older build".getBytes(StandardCharsets.UTF_8));

        run(mods, entry(JEI_NEW, sha("0"), "jei"));

        assertTrue(Files.exists(disabled));
    }

    // ---------------------------------------------------------------- switched off

    @Test
    void aMissingModsFolderIsNotAnError(@TempDir Path game) throws Exception {
        layout(game);
        Files.delete(mods);

        Migrator.Result result = run(mods, entry(JEI_NEW, sha("0"), "jei"));

        assertTrue(result.blocked.isEmpty());
        assertEquals(0, result.moved + result.parked + result.deferred);
    }

    @Test
    void migrationIsSkippedEntirelyWhenTheHopperDirIsNotLoaded(@TempDir Path game)
            throws Exception {
        layout(game);
        // The Fabric consent gate: with the mirror off, nothing ever loads out of hoppermods/, so
        // moving a jar out of mods/ would not de-duplicate a mod, it would unload one.
        Path mine = jar(mods, JEI_OLD, "an older build", "jei");
        byte[] before = Files.readAllBytes(mine);

        Migrator.Result result = run(null, entry(JEI_NEW, sha("0"), "jei"));

        assertTrue(Files.exists(mine));
        assertArrayEquals(before, Files.readAllBytes(mine));
        assertFalse(Files.exists(hopper.resolve(Migrator.REPLACED)));
        assertEquals(0, result.moved + result.parked + result.deferred);
    }

    @Test
    void anEmptyManifestTouchesNothing(@TempDir Path game) throws Exception {
        layout(game);
        Path mine = jar(mods, JEI_OLD, "an older build", "jei");

        // An old server that does not publish modIds at all parses to entries with empty id lists,
        // which is exactly this: a silent no-op.
        Migrator.Result result = run(mods, entry(JEI_NEW, sha("0")));

        assertTrue(Files.exists(mine));
        assertEquals(0, result.moved + result.parked + result.deferred);
    }

    // ---------------------------------------------------------------- the sweep

    @Test
    void theStaleSweepNeverTouchesTheReplacedDirectory(@TempDir Path game) throws Exception {
        layout(game);
        jar(mods, JEI_OLD, "an older build", "jei");
        run(mods, entry(JEI_NEW, sha("0"), "jei"));

        Path parked = hopper.resolve(Migrator.REPLACED).resolve(JEI_OLD + Migrator.PARKED_SUFFIX);
        assertTrue(Files.exists(parked));

        // What Syncer.sync()'s sweep does: list hoppermods/ non-recursively and delete every
        // regular file that is not wanted. replaced/ is a directory, and is named in the spare
        // list as well, so neither it nor anything under it is ever a candidate.
        List<Path> deletable = new ArrayList<Path>();
        java.nio.file.DirectoryStream<Path> listing = Files.newDirectoryStream(hopper);
        try {
            for (Path p : listing) {
                if (!Files.isRegularFile(p)) continue;
                if (Migrator.REPLACED.equals(p.getFileName().toString())) continue;
                deletable.add(p);
            }
        } finally {
            listing.close();
        }

        assertTrue(deletable.isEmpty());
        assertTrue(Files.exists(parked));
    }

}
