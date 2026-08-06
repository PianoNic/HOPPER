package ch.pianonic.hopper;

import org.junit.jupiter.api.Test;

import java.util.List;
import java.util.Map;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

class JsonTest {
    private static String repeat(String unit, int times) {
        StringBuilder sb = new StringBuilder(unit.length() * times);
        for (int i = 0; i < times; i++) {
            sb.append(unit);
        }
        return sb.toString();
    }

    @Test
    void refusesADeeplyNestedArrayInsteadOfOverflowingTheStack() {
        String deep = repeat("[", 20000);

        IllegalArgumentException e =
                assertThrows(IllegalArgumentException.class, () -> Json.parse(deep));
        assertTrue(e.getMessage().contains("nested more than"),
                "expected a depth refusal, got: " + e.getMessage());
    }

    @Test
    void refusesADeeplyNestedObjectToo() {
        String deep = repeat("{\"a\":", 20000);

        IllegalArgumentException e =
                assertThrows(IllegalArgumentException.class, () -> Json.parse(deep));
        assertTrue(e.getMessage().contains("nested more than"),
                "expected a depth refusal, got: " + e.getMessage());
    }

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

    @Test
    void parsesAnOrdinaryManifest() {
        Object root = Json.parse("{\"mods\":[{\"file\":\"jei.jar\",\"url\":\"https://h/x\","
                + "\"sha256\":\"abc\",\"size\":12}]}");

        List<?> mods = Json.asArray(Json.get(root, "mods"));
        assertEquals(1, mods.size());
        assertEquals("jei.jar", Json.string(mods.get(0), "file"));
        assertEquals(12L, Json.number(mods.get(0), "size"));
    }

    @Test
    void parsesRightUpToTheLimit() {
        String atTheLimit = repeat("[", 64) + repeat("]", 64);

        assertTrue(Json.parse(atTheLimit) instanceof List);
    }

    @Test
    void refusesOneLevelPastTheLimit() {
        String oneTooDeep = repeat("[", 65) + repeat("]", 65);

        assertThrows(IllegalArgumentException.class, () -> Json.parse(oneTooDeep));
    }

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

    @Test
    void stillRefusesOrdinaryMalformedInput() {
        assertThrows(IllegalArgumentException.class, () -> Json.parse("{\"a\":1} trailing"));
        assertThrows(IllegalArgumentException.class, () -> Json.parse("{\"a\":}"));
        assertThrows(IllegalArgumentException.class, () -> Json.parse("[1,"));
        assertThrows(IllegalArgumentException.class, () -> Json.parse(null));
    }

    @Test
    void ordinaryNestingIsUntouched() {
        Map<?, ?> map = Json.asObject(Json.parse("{\"a\":{\"b\":[1,2]}}"));
        assertEquals(1, map.size());
        assertEquals(2, Json.asArray(Json.get(map.get("a"), "b")).size());
    }
}
