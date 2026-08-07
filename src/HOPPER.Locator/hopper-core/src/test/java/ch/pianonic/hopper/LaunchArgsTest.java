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

    @Test
    void thePrismCommandLineCarriesNoName() {
        assertNull(LaunchArgs.username(new String[]{"org.prismlauncher.EntryPoint"}));
    }

    @Test
    void modLauncherLookupIsNullWithoutModLauncher() {
        assertNull(LaunchArgs.modLauncherArgs());
    }

    @Test
    void resolvingWithNoSourceAvailableIsNull() {
        assertNull(LaunchArgs.username());
    }
}
