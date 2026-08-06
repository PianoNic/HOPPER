package ch.pianonic.hopper;

import java.lang.reflect.Field;

public final class LaunchArgs {

    private static final String USERNAME = "--username";

    private LaunchArgs() {
    }

    public static String username(String[] args) {
        if (args == null) {
            return null;
        }
        for (int i = 0; i + 1 < args.length; i++) {
            if (USERNAME.equals(args[i]) && args[i + 1] != null && !args[i + 1].trim().isEmpty()) {
                return args[i + 1];
            }
        }
        return null;
    }

    // Reflective and addressed by name, so this module still compiles with no loader on its
    // classpath. It has to be reflective either way: ModLauncher parses the game arguments but
    // exposes them through no public API, and IEnvironment.Keys carries a UUID and no name.
    public static String[] modLauncherArgs() {
        try {
            Class<?> launcher = Class.forName("cpw.mods.modlauncher.Launcher");
            Object instance = launcher.getField("INSTANCE").get(null);
            if (instance == null) {
                return null;
            }
            Field handlerField = launcher.getDeclaredField("argumentHandler");
            handlerField.setAccessible(true);
            Object handler = handlerField.get(instance);
            if (handler == null) {
                return null;
            }
            Field argsField = handler.getClass().getDeclaredField("args");
            argsField.setAccessible(true);
            Object args = argsField.get(handler);
            return args instanceof String[] ? (String[]) args : null;
        } catch (Throwable t) {
            return null;
        }
    }

    // Carries the game arguments only when the launcher put them on the java command line. The
    // vanilla launcher and CurseForge do; Prism runs org.prismlauncher.EntryPoint and passes them
    // over stdin, which is why this cannot be the only source.
    public static String[] commandLineArgs() {
        return System.getProperty("sun.java.command", "").split(" ");
    }

    public static String username() {
        String fromModLauncher = username(modLauncherArgs());
        return fromModLauncher != null ? fromModLauncher : username(commandLineArgs());
    }
}
