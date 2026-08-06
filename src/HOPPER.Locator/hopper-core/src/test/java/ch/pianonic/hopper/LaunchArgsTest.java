package ch.pianonic.hopper;

import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNull;

class LaunchArgsTest {

    @Test
    void readsTheNameThatFollowsTheFlag() {
        String[] args = {"--version", "1.20.1", "--username", "Steve", "--uuid", "abc"};
        assertEquals("Steve", LaunchArgs.username(args));
    }

    @Test
    void theFlagAtTheEndWithNoValueIsNotAName() {
        assertNull(LaunchArgs.username(new String[]{"--gameDir", ".", "--username"}));
    }

    @Test
    void aBlankValueIsNotAName() {
        assertNull(LaunchArgs.username(new String[]{"--username", "   "}));
    }

    @Test
    void absentFlagYieldsNothing() {
        assertNull(LaunchArgs.username(new String[]{"--version", "1.20.1"}));
    }

    @Test
    void nullAndEmptyAreTolerated() {
        assertNull(LaunchArgs.username((String[]) null));
        assertNull(LaunchArgs.username(new String[0]));
    }

    @Test
    void aNullEntryBesideTheFlagIsNotAName() {
        assertNull(LaunchArgs.username(new String[]{"--username", null}));
    }

    @Test
    void theFirstOccurrenceWins() {
        assertEquals("First", LaunchArgs.username(new String[]{"--username", "First", "--username", "Second"}));
    }

    // Prism runs org.prismlauncher.EntryPoint and hands the game arguments over stdin, so the
    // property carries no name at all. This is the shape that made every Prism client nameless.
    @Test
    void thePrismCommandLineCarriesNoName() {
        assertNull(LaunchArgs.username(new String[]{"org.prismlauncher.EntryPoint"}));
    }

    // Off a real launch: no ModLauncher on the test classpath, so the reflective lookup has to
    // return null rather than throw.
    @Test
    void modLauncherLookupIsNullWithoutModLauncher() {
        assertNull(LaunchArgs.modLauncherArgs());
    }

    @Test
    void resolvingWithNoSourceAvailableIsNull() {
        assertNull(LaunchArgs.username());
    }
}
