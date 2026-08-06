package ch.pianonic.hopper;

public interface HopperLog {
    void info(String message);

    void warn(String message, Throwable t);

    void error(String message, Throwable t);

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
