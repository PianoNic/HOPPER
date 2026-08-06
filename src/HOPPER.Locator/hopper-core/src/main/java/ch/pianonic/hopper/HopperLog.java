package ch.pianonic.hopper;

/**
 * The core's only way of saying anything.
 *
 * <p>Not log4j, and not by omission. Forge and Fabric both have log4j on the
 * classpath by the time HOPPER runs, but a Quilt loader plugin runs inside
 * {@code QuiltPluginClassLoader}, which owns only the packages
 * {@code quilt.mod.json} lists and whose parent carries Quilt's <em>shaded</em>
 * logging rather than {@code org.apache.logging.log4j}. A core that named log4j
 * directly would be a {@code NoClassDefFoundError} on exactly one of the six
 * adapters, and it would only show up at runtime.
 *
 * <p>Messages are complete strings - no {@code {}} placeholders - so any backend
 * works without the core knowing which one it got.
 */
public interface HopperLog {

    void info(String message);

    /** @param t may be null */
    void warn(String message, Throwable t);

    /** @param t may be null */
    void error(String message, Throwable t);

    /**
     * The fallback, used by the tests and by the Quilt plugin. {@code System.out}
     * reaches the console in every launcher and every environment, which is more
     * than can be said for any logger this early.
     */
    HopperLog STDOUT = new HopperLog() {

        @Override
        public void info(String message) {
            System.out.println(message);
        }

        @Override
        public void warn(String message, Throwable t) {
            System.out.println(message);
            if (t != null) t.printStackTrace(System.out);
        }

        @Override
        public void error(String message, Throwable t) {
            System.err.println(message);
            if (t != null) t.printStackTrace(System.err);
        }
    };
}
