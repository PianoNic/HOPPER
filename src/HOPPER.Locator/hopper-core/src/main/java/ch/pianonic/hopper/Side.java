package ch.pianonic.hopper;

/**
 * Which side this process is. Named here rather than taken from a loader type, because the core
 * has no loader on its classpath - each adapter translates its own loader's answer into this.
 */
public enum Side {
    CLIENT("client"),
    SERVER("server");

    private final String wire;

    Side(String wire) {
        this.wire = wire;
    }

    /** The value the manifest endpoint expects in its side parameter. */
    public String wire() {
        return wire;
    }
}
