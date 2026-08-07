package ch.pianonic.hopper;

import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.assertEquals;

class SideTest {

    private static final String URL = "https://hopper.example.com/api/manifest";

    // A client asks for nothing, so the request is byte-identical to the one every jar in the
    // field already makes. Changing this would re-point every shipped client at a new URL.
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

    // The report posts to a path resolved against the manifest URL, and resolve() drops a query
    // string. Appending the side to the stored URL would have silently broken reporting, which is
    // why manifestUrlFor builds the fetch URL separately.
    @Test
    void theReportUrlIsUnaffectedByTheSide() {
        assertEquals("https://hopper.example.com/api/clients/report",
                Syncer.reportUrl(URL).toString());
    }
}
