package ch.pianonic.hopper;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.attribute.FileTime;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.zip.ZipEntry;
import java.util.zip.ZipOutputStream;

import static org.junit.jupiter.api.Assertions.assertArrayEquals;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

class MigratorTest {
    private static final String JEI_OLD = "jei-1.20.1-15.2.0.27.jar";
    private static final String JEI_NEW = "jei-1.20.1-15.3.0.4.jar";

    private Path mods;
    private Path hopper;

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

    private int loadableCopies(Migrator.Result result, Syncer.Entry e, String... modsJars)
            throws Exception {
        int copies = 0;
        for (String name : modsJars) {
            if (Files.exists(mods.resolve(name))) copies++;
        }

        if (!result.blocked.contains(e.file)) copies++;
        return copies;
    }

    @Test
    void aMigratedJarIsRecordedSoTheSweepCanParkItInsteadOfDeletingIt(@TempDir Path game)
            throws Exception {
        layout(game);
        Path mine = jar(mods, "jei-whatever-i-called-it.jar", "required build", "jei");
        String sha = Syncer.sha256(mine);

        Migrator.Result result = run(mods, entry(JEI_NEW, sha, "jei"));

        assertEquals(1, result.moved);
        assertTrue(Files.exists(hopper.resolve(JEI_NEW)), "the winner is in hoppermods/");
        assertTrue(result.migrated.contains(JEI_NEW),
                "the sweep can only spare what the migrator reported, so the name must be here");
    }

    @Test
    void aDownloadedJarIsNotReportedAsMigrated(@TempDir Path game) throws Exception {
        layout(game);

        Migrator.Result result = run(mods, entry(JEI_NEW, sha("a"), "jei"));

        assertEquals(0, result.moved);
        assertTrue(result.migrated.isEmpty(), "nothing came out of mods/, so nothing is protected");
    }

    @Test
    void aJarTheFabricMirrorPutInModsIsNotMigratedBackOut(@TempDir Path game) throws Exception {
        layout(game);

        // The state after one successful mirrored sync: HOPPER's own copy sitting in mods/, named
        // in mods-mirror.txt. Adopting it means moving a jar the running loader holds open.
        Path mirrored = jar(mods, JEI_NEW, "the build the server wants", "jei");
        Files.write(hopper.resolve(Syncer.MIRROR_LIST), (JEI_NEW + "\n").getBytes("UTF-8"));

        Migrator.Result result = run(mods, entry(JEI_NEW, Syncer.sha256(mirrored), "jei"));

        assertTrue(Files.exists(mirrored), "the mirror's own copy must stay where the loader found it");
        assertFalse(Files.exists(hopper.resolve(JEI_NEW)));
        assertEquals(0, result.moved);
        assertEquals(0, result.deferred);
    }

    @Test
    void aJarThePlayerPutInModsIsStillMigratedWhenTheMirrorOwnsSomethingElse(@TempDir Path game)
            throws Exception {
        layout(game);

        Path mine = jar(mods, "jei-whatever-i-called-it.jar", "required build", "jei");
        Files.write(hopper.resolve(Syncer.MIRROR_LIST), "something-else.jar\n".getBytes("UTF-8"));

        Migrator.Result result = run(mods, entry(JEI_NEW, Syncer.sha256(mine), "jei"));

        assertFalse(Files.exists(mine));
        assertEquals(1, result.moved);
    }

    @Test
    void movesTheMatchingBuildIntoHopperModsUnderTheManifestFilename(@TempDir Path game)
            throws Exception {
        layout(game);

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

        assertEquals(sha, Syncer.sha256(hopper.resolve(JEI_NEW)));
    }

    @Test
    void differentFilenameDifferentHashSameModIdIsParkedAndTheRequiredBuildIsDownloaded(
            @TempDir Path game) throws Exception {
        layout(game);

        Path mine = jar(mods, JEI_OLD, "an older build", "jei");
        Syncer.Entry e = entry(JEI_NEW, sha("0"), "jei");

        Migrator.Result result = run(mods, e);

        assertFalse(Files.exists(mine), "the older build must have left mods/");
        assertTrue(Files.exists(hopper.resolve(Migrator.PARKED)
                .resolve(JEI_OLD + Migrator.PARKED_SUFFIX)), "and must still exist, parked");
        assertEquals(1, result.parked);
        assertEquals(0, result.moved);

        assertTrue(result.blocked.isEmpty());
        assertEquals(1, loadableCopies(result, e, JEI_OLD));
    }

    @Test
    void aParkedFileIsNotAJarSoNoLoaderCanSeeIt(@TempDir Path game) throws Exception {
        layout(game);
        jar(mods, JEI_OLD, "an older build", "jei");

        run(mods, entry(JEI_NEW, sha("0"), "jei"));

        Path parked = hopper.resolve(Migrator.PARKED).resolve(JEI_OLD + Migrator.PARKED_SUFFIX);
        assertTrue(Files.exists(parked));

        assertTrue(parked.getFileName().toString().startsWith(JEI_OLD));
        assertFalse(parked.getFileName().toString().endsWith(".jar"));
    }

    @Test
    void parkingTwiceDoesNotOverwriteTheFirstParkedCopy(@TempDir Path game) throws Exception {
        layout(game);
        Syncer.Entry e = entry(JEI_NEW, sha("0"), "jei");

        jar(mods, JEI_OLD, "the first old build", "jei");
        run(mods, e);

        jar(mods, JEI_OLD, "a second, different old build", "jei");
        run(mods, e);

        Path parked = hopper.resolve(Migrator.PARKED);
        assertTrue(Files.exists(parked.resolve(JEI_OLD + Migrator.PARKED_SUFFIX)));
        assertTrue(Files.exists(parked.resolve(
                "jei-1.20.1-15.2.0.27-1.jar" + Migrator.PARKED_SUFFIX)),
                "a second park must not overwrite the first");
    }

    @Test
    void aReadmeIsWrittenTheFirstTimeSomethingIsParked(@TempDir Path game) throws Exception {
        layout(game);
        jar(mods, JEI_OLD, "an older build", "jei");

        run(mods, entry(JEI_NEW, sha("0"), "jei"));

        Path readme = hopper.resolve(Migrator.PARKED).resolve("README.txt");
        assertTrue(Files.exists(readme));
        String text = new String(Files.readAllBytes(readme), StandardCharsets.UTF_8);
        assertTrue(text.contains(Migrator.PARKED_SUFFIX));
        assertFalse(text.contains("—"), "no em dashes anywhere, including in what we ship");
    }

    @Test
    void deferredWhenTheWinnerCannotBeMoved(@TempDir Path game) throws Exception {
        layout(game);
        Path mine = jar(mods, "jei-mine.jar", "required build", "jei");
        Syncer.Entry e = entry(JEI_NEW, Syncer.sha256(mine), "jei");

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

        Files.write(hopper.resolve(Migrator.PARKED), "not a directory".getBytes(StandardCharsets.UTF_8));

        Migrator.Result result = run(mods, e);

        assertTrue(Files.exists(loser));
        assertTrue(Files.exists(winner), "losers are parked FIRST precisely so that a failure there"
                + " leaves nothing in hoppermods/ to undo");
        assertFalse(Files.exists(hopper.resolve(JEI_NEW)));
        assertEquals(0, result.moved);
        assertEquals(1, result.deferred);
        assertTrue(result.blocked.contains(JEI_NEW));
    }

    @Test
    void twoJarsInModsFolderDeclaringOneIdLeaveExactlyOneLoadableCopy(@TempDir Path game)
            throws Exception {
        layout(game);

        jar(mods, "aaa-jei-old.jar", "an older build", "jei");
        Path winner = jar(mods, "zzz-jei.jar", "required build", "jei");
        Syncer.Entry e = entry(JEI_NEW, Syncer.sha256(winner), "jei");

        Migrator.Result result = run(mods, e);

        assertFalse(Files.exists(mods.resolve("aaa-jei-old.jar")));
        assertFalse(Files.exists(mods.resolve("zzz-jei.jar")));
        assertTrue(Files.exists(hopper.resolve(JEI_NEW)));
        assertTrue(Files.exists(hopper.resolve(Migrator.PARKED)
                .resolve("aaa-jei-old.jar" + Migrator.PARKED_SUFFIX)));
        assertEquals(1, result.moved);
        assertEquals(1, result.parked);
        assertEquals(1, loadableCopies(result, e, "aaa-jei-old.jar", "zzz-jei.jar"));
    }

    @Test
    void serverListingOneModIdTwiceMigratesNothing(@TempDir Path game) throws Exception {
        layout(game);
        Path mine = jar(mods, JEI_OLD, "an older build", "jei");

        Migrator.Result result = run(mods,
                entry("jei-a.jar", sha("1"), "jei"),
                entry("jei-b.jar", sha("2"), "jei"));

        assertTrue(Files.exists(mine));
        assertEquals(0, result.moved);
        assertEquals(0, result.parked);
        assertEquals(0, result.deferred);
        assertTrue(result.blocked.isEmpty());
    }

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

        Path library = jar(mods, "Registrate-MC1.20-1.3.3.jar", "no metadata");

        Migrator.Result result = run(mods, entry(JEI_NEW, sha("0"), "jei"));

        assertTrue(Files.exists(library));
        assertEquals(0, result.moved + result.parked + result.deferred);
    }

    @Test
    void subdirectoriesOfTheModsFolderAreNotScanned(@TempDir Path game) throws Exception {
        layout(game);

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

        Path mine = jar(mods, JEI_OLD, "an older build", "jei");
        byte[] before = Files.readAllBytes(mine);

        Migrator.Result result = run(null, entry(JEI_NEW, sha("0"), "jei"));

        assertTrue(Files.exists(mine));
        assertArrayEquals(before, Files.readAllBytes(mine));
        assertFalse(Files.exists(hopper.resolve(Migrator.PARKED)));
        assertEquals(0, result.moved + result.parked + result.deferred);
    }

    @Test
    void anEmptyManifestTouchesNothing(@TempDir Path game) throws Exception {
        layout(game);
        Path mine = jar(mods, JEI_OLD, "an older build", "jei");

        Migrator.Result result = run(mods, entry(JEI_NEW, sha("0")));

        assertTrue(Files.exists(mine));
        assertEquals(0, result.moved + result.parked + result.deferred);
    }

    @Test
    void theStaleSweepNeverTouchesTheReplacedDirectory(@TempDir Path game) throws Exception {
        layout(game);
        jar(mods, JEI_OLD, "an older build", "jei");
        run(mods, entry(JEI_NEW, sha("0"), "jei"));

        Path parked = hopper.resolve(Migrator.PARKED).resolve(JEI_OLD + Migrator.PARKED_SUFFIX);
        assertTrue(Files.exists(parked));

        List<Path> deletable = new ArrayList<Path>();
        java.nio.file.DirectoryStream<Path> listing = Files.newDirectoryStream(hopper);
        try {
            for (Path p : listing) {
                if (!Files.isRegularFile(p)) continue;
                if (Migrator.PARKED.equals(p.getFileName().toString())) continue;
                deletable.add(p);
            }
        } finally {
            listing.close();
        }

        assertTrue(deletable.isEmpty());
        assertTrue(Files.exists(parked));
    }

    @Test
    void aParkedFileOlderThanThreeDaysIsDeleted(@TempDir Path game) throws Exception {
        layout(game);
        jar(mods, JEI_OLD, "an older build", "jei");
        run(mods, entry(JEI_NEW, sha("0"), "jei"));

        Path parked = hopper.resolve(Migrator.PARKED).resolve(JEI_OLD + Migrator.PARKED_SUFFIX);
        assertTrue(Files.exists(parked), "parked to begin with");

        long now = System.currentTimeMillis();
        Files.setLastModifiedTime(parked, FileTime.fromMillis(now - Migrator.KEEP_PARKED_MS - 1000));

        assertEquals(1, new Migrator(mods, hopper, HopperLog.STDOUT).sweepParked(now));
        assertFalse(Files.exists(parked), "and gone once nobody is coming back for it");
    }

    @Test
    void aParkedFileInsideThreeDaysIsKept(@TempDir Path game) throws Exception {
        layout(game);
        jar(mods, JEI_OLD, "an older build", "jei");
        run(mods, entry(JEI_NEW, sha("0"), "jei"));

        Path parked = hopper.resolve(Migrator.PARKED).resolve(JEI_OLD + Migrator.PARKED_SUFFIX);
        long now = System.currentTimeMillis();
        Files.setLastModifiedTime(parked, FileTime.fromMillis(now - Migrator.KEEP_PARKED_MS + 60_000));

        assertEquals(0, new Migrator(mods, hopper, HopperLog.STDOUT).sweepParked(now));
        assertTrue(Files.exists(parked), "a player still has three days to fetch it back");
    }

    @Test
    void theSweepLeavesTheReadmeAndAnythingNotParkedAlone(@TempDir Path game) throws Exception {
        layout(game);
        jar(mods, JEI_OLD, "an older build", "jei");
        run(mods, entry(JEI_NEW, sha("0"), "jei"));

        Path dir = hopper.resolve(Migrator.PARKED);
        Path readme = dir.resolve("README.txt");
        Path stray = dir.resolve("notes.txt");
        Files.write(stray, "mine".getBytes(StandardCharsets.UTF_8));

        long now = System.currentTimeMillis();
        long old = now - Migrator.KEEP_PARKED_MS - 1000;
        Files.setLastModifiedTime(readme, FileTime.fromMillis(old));
        Files.setLastModifiedTime(stray, FileTime.fromMillis(old));

        new Migrator(mods, hopper, HopperLog.STDOUT).sweepParked(now);

        assertTrue(Files.exists(readme), "the README explains the folder, so it outlives its contents");
        assertTrue(Files.exists(stray), "only files HOPPER parked carry the suffix, and only those go");
    }

    @Test
    void sweepingWithNoParkedFolderIsNotAnError(@TempDir Path game) throws Exception {
        layout(game);

        assertEquals(0, new Migrator(mods, hopper, HopperLog.STDOUT).sweepParked(System.currentTimeMillis()));
    }
}
