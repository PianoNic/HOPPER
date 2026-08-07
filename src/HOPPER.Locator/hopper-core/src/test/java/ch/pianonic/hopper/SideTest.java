package ch.pianonic.hopper;

import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.assertEquals;

class SideTest {
    private static final String URL = "https://hopper.example.com/api/manifest";

    @Test
    void aClientAsksForTheSameUrlItAlwaysHas() {
        assertEquals(URL, Syncer.manifestUrlFor(URL, Side.CLIENT));
    }

    @Test
    void aNullSideIsTreatedAsClient() {
        assertEquals(URL, Syncer.manifestUrlFor(URL, null));
    }

    @Test
    void aServerAsksForTheServerSet() {
        assertEquals(URL + "?side=server", Syncer.manifestUrlFor(URL, Side.SERVER));
    }

    @Test
    void anExistingQueryStringIsExtendedRatherThanReplaced() {
        assertEquals("https://h/api/manifest?x=1&side=server",
                Syncer.manifestUrlFor("https://h/api/manifest?x=1", Side.SERVER));
    }

    @Test
    void theWireValuesAreTheOnesTheApiAccepts() {
        assertEquals("client", Side.CLIENT.wire());
        assertEquals("server", Side.SERVER.wire());
    }

    @Test
    void theReportUrlIsUnaffectedByTheSide() {
        assertEquals("https://hopper.example.com/api/clients/report",
                Syncer.reportUrl(URL).toString());
    }
}
