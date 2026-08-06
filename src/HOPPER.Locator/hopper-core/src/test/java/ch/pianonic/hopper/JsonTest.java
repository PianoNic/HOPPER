package ch.pianonic.hopper;

import org.junit.jupiter.api.Test;

import java.util.List;
import java.util.Map;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * The parser that reads whatever the server sends, which is the one place in the
 * core where an untrusted document steers control flow.
 *
 * <p>Compiled at {@code --release 8}, like the core it tests.
 */
class JsonTest {

    private static String repeat(String unit, int times) {
        StringBuilder sb = new StringBuilder(unit.length() * times);
        for (int i = 0; i < times; i++) {
            sb.append(unit);
        }
        return sb.toString();
    }

    /**
     * The one that matters. {@code Json.value/object/array} are mutually
     * recursive, so nesting depth is stack depth, and a few thousand bytes of
     * nothing but {@code [} used to come back as {@link StackOverflowError} - an
     * {@link Error}, which no {@code catch (Exception)} in the call chain would
     * have stopped, out of a locator running inside the loader's pre-discovery
     * hook. That is a crash report instead of a game, caused by a file the player
     * does not control.
     *
     * <p>Asserted well past the depth that overflows a default stack, so this
     * fails loudly if the cap is ever removed rather than passing by luck on a
     * roomy JVM.
     */
    @Test
    void refusesADeeplyNestedArrayInsteadOfOverflowingTheStack() {
        String deep = repeat("[", 20000);

        IllegalArgumentException e =
                assertThrows(IllegalArgumentException.class, () -> Json.parse(deep));
        assertTrue(e.getMessage().contains("nested more than"),
                "expected a depth refusal, got: " + e.getMessage());
    }

    /** Objects recurse through a different method than arrays, so both are pinned. */
    @Test
    void refusesADeeplyNestedObjectToo() {
        String deep = repeat("{\"a\":", 20000);

        IllegalArgumentException e =
                assertThrows(IllegalArgumentException.class, () -> Json.parse(deep));
        assertTrue(e.getMessage().contains("nested more than"),
                "expected a depth refusal, got: " + e.getMessage());
    }

    /**
     * The cap counts depth, not total containers, so a manifest with a thousand
     * mods in one array has to keep parsing. A cap implemented as a running total
     * would pass the two tests above and break every real modpack.
     */
    @Test
    void aLongFlatManifestIsNotDeep() {
        StringBuilder sb = new StringBuilder("{\"mods\":[");
        for (int i = 0; i < 1000; i++) {
            if (i > 0) sb.append(',');
            sb.append("{\"file\":\"m").append(i).append(".jar\",\"sha256\":\"ab\",\"size\":1}");
        }
        sb.append("]}");

        List<?> mods = Json.asArray(Json.get(Json.parse(sb.toString()), "mods"));
        assertEquals(1000, mods.size());
        assertEquals("m999.jar", Json.string(mods.get(999), "file"));
    }

    /** The shape HOPPER actually receives is three levels deep and must be unaffected. */
    @Test
    void parsesAnOrdinaryManifest() {
        Object root = Json.parse("{\"mods\":[{\"file\":\"jei.jar\",\"url\":\"https://h/x\","
                + "\"sha256\":\"abc\",\"size\":12}]}");

        List<?> mods = Json.asArray(Json.get(root, "mods"));
        assertEquals(1, mods.size());
        assertEquals("jei.jar", Json.string(mods.get(0), "file"));
        assertEquals(12L, Json.number(mods.get(0), "size"));
    }

    /** Sixty-four levels is the limit, so sixty-four levels has to still work. */
    @Test
    void parsesRightUpToTheLimit() {
        String atTheLimit = repeat("[", 64) + repeat("]", 64);

        assertTrue(Json.parse(atTheLimit) instanceof List);
    }

    /** One past it is refused, which is what makes the limit a limit and not a suggestion. */
    @Test
    void refusesOneLevelPastTheLimit() {
        String oneTooDeep = repeat("[", 65) + repeat("]", 65);

        assertThrows(IllegalArgumentException.class, () -> Json.parse(oneTooDeep));
    }

    /**
     * Depth is counted in and back out again, so a document that is wide but
     * shallow stays legal no matter how many containers it closes on the way.
     */
    @Test
    void siblingsAreJudgedOnTheirOwnDepth() {
        StringBuilder sb = new StringBuilder("[");
        for (int i = 0; i < 500; i++) {
            if (i > 0) sb.append(',');
            sb.append("[[[1]]]");
        }
        sb.append(']');

        assertEquals(500, Json.asArray(Json.parse(sb.toString())).size());
    }

    /** Ordinary malformed input is still refused exactly the way it always was. */
    @Test
    void stillRefusesOrdinaryMalformedInput() {
        assertThrows(IllegalArgumentException.class, () -> Json.parse("{\"a\":1} trailing"));
        assertThrows(IllegalArgumentException.class, () -> Json.parse("{\"a\":}"));
        assertThrows(IllegalArgumentException.class, () -> Json.parse("[1,"));
        assertThrows(IllegalArgumentException.class, () -> Json.parse(null));
    }

    /** Nothing about the depth counter changed what a well-formed document parses to. */
    @Test
    void ordinaryNestingIsUntouched() {
        Map<?, ?> map = Json.asObject(Json.parse("{\"a\":{\"b\":[1,2]}}"));
        assertEquals(1, map.size());
        assertEquals(2, Json.asArray(Json.get(map.get("a"), "b")).size());
    }
}
