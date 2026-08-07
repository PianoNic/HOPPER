package ch.pianonic.hopper;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Collections;
import java.util.Set;

import static org.junit.jupiter.api.Assertions.assertArrayEquals;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

class ModsFolderMirrorTest {
    private static final byte[] BODY = "a jar the server distributes".getBytes(StandardCharsets.UTF_8);

    @Test
    void aCopiedJarArrivesWholeAndLeavesNoLeftover(@TempDir Path game) throws Exception {
        Path mods = Files.createDirectories(game.resolve("mods"));
        Path hopper = Files.createDirectories(game.resolve(Hopper.DIR));
        Files.write(hopper.resolve("jei.jar"), BODY);

        ModsFolderMirror mirror = new ModsFolderMirror(mods, hopper, HopperLog.STDOUT);
        mirror.reconcile(Collections.singleton("jei.jar"));

        assertArrayEquals(BODY, Files.readAllBytes(mods.resolve("jei.jar")));
        assertFalse(Files.exists(mods.resolve("jei.jar.part")), "the staging name must not survive");
        assertEquals(0, mirror.unresolved());
    }

    /**
     * A copy that dies halfway must not leave a truncated zip under a name the loader scans: Fabric
     * would fail the launch before preLaunch could run and repair it, and on a first copy the name
     * is not in the ledger yet, so the repair pass would never even consider it.
     *
     * <p>The failure is forced by parking a non-empty directory on the staging name, which is the
     * one way to make Files.copy throw that behaves the same on every OS.
     */
    @Test
    void aFailedCopyLeavesNothingBehindUnderTheJarName(@TempDir Path game) throws Exception {
        Path mods = Files.createDirectories(game.resolve("mods"));
        Path hopper = Files.createDirectories(game.resolve(Hopper.DIR));
        Files.write(hopper.resolve("jei.jar"), BODY);

        Path blocker = Files.createDirectories(mods.resolve("jei.jar.part"));
        Files.write(blocker.resolve("occupied"), new byte[] { 1 });

        ModsFolderMirror mirror = new ModsFolderMirror(mods, hopper, HopperLog.STDOUT);
        mirror.reconcile(Collections.singleton("jei.jar"));

        assertFalse(Files.exists(mods.resolve("jei.jar")),
                "a half-written jar under the scanned name is worse than no jar at all");
        assertEquals(1, mirror.unresolved(), "and the launch has to be told it is incomplete");
    }

    @Test
    void aLeftoverPartIsSweptWhenTheModLeavesTheList(@TempDir Path game) throws Exception {
        Path mods = Files.createDirectories(game.resolve("mods"));
        Path hopper = Files.createDirectories(game.resolve(Hopper.DIR));
        Files.write(hopper.resolve("jei.jar"), BODY);

        new ModsFolderMirror(mods, hopper, HopperLog.STDOUT).reconcile(Collections.singleton("jei.jar"));

        Path orphan = mods.resolve("jei.jar.part");
        Files.write(orphan, "half a copy".getBytes(StandardCharsets.UTF_8));
        Files.delete(hopper.resolve("jei.jar"));

        new ModsFolderMirror(mods, hopper, HopperLog.STDOUT).reconcile(Collections.<String>emptySet());

        assertFalse(Files.exists(mods.resolve("jei.jar")));
        assertFalse(Files.exists(orphan), "the staging file goes with the jar it was staging");
    }

    @Test
    void aJarHopperDidNotPutThereIsNeverTouched(@TempDir Path game) throws Exception {
        Path mods = Files.createDirectories(game.resolve("mods"));
        Path hopper = Files.createDirectories(game.resolve(Hopper.DIR));

        byte[] theirs = "the player's own build".getBytes(StandardCharsets.UTF_8);
        Files.write(mods.resolve("jei.jar"), theirs);
        Files.write(hopper.resolve("jei.jar"), BODY);

        ModsFolderMirror mirror = new ModsFolderMirror(mods, hopper, HopperLog.STDOUT);
        Set<String> wanted = Collections.singleton("jei.jar");
        mirror.reconcile(wanted);

        assertArrayEquals(theirs, Files.readAllBytes(mods.resolve("jei.jar")));
        assertTrue(mirror.unresolved() > 0, "and the player has to be told why it was skipped");
    }
}
