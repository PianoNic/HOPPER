package ch.pianonic.hopper;

public enum Side {
    CLIENT("client"),
    SERVER("server");

    private final String wire;

    Side(String wire) {
        this.wire = wire;
    }

    public String wire() {
        return wire;
    }
}
