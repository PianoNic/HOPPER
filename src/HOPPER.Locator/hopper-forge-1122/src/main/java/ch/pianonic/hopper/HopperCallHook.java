package ch.pianonic.hopper;

import java.io.File;
import java.io.IOException;
import java.nio.file.DirectoryStream;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.jar.Attributes;
import java.util.jar.JarFile;
import java.util.jar.Manifest;

import javax.net.ssl.SSLHandshakeException;

import net.minecraft.launchwrapper.Launch;
import net.minecraftforge.fml.relauncher.IFMLCallHook;

public final class HopperCallHook implements IFMLCallHook {
    private static final String LAUNCH_ARGS = "forgeLaunchArgs";

    private static final String MODS_ARG = "--mods";

    private static final String USERNAME_ARG = "--username";

    private static final String PREFIX = Hopper.DIR + "/";

    private File mcLocation;

    @Override
    public void injectData(Map<String, Object> data) {
        Object loc = data.get("mcLocation");
        if (loc instanceof File) {
            this.mcLocation = (File) loc;
        }
    }

    @Override
    public Void call() {
        HopperLog log = newLog();
        try {
            sync(log);
        } catch (Throwable t) {
            log.error("[HOPPER] the coremod failed; launching without it", t);
        }
        return null;
    }

    private void sync(HopperLog log) {
        File gameDirFile = this.mcLocation != null ? this.mcLocation : HopperCoreMod.mcLocation();
        if (gameDirFile == null) {
            gameDirFile = new File(".");
        }
        Path gameDir = gameDirFile.toPath();

        Map<String, Object> args = launchArgs();
        Hopper.Result result = Hopper.run(gameDir, username(args), log, null, true, side());

        List<String> jars = jarsOnDisk(result, log);
        if (jars.isEmpty()) {
            log.info("[HOPPER] nothing in " + PREFIX + " to hand to FML");
            return;
        }

        warnAboutCoremods(result.dir, jars, log);

        if (args == null) {
            log.warn("[HOPPER] the LaunchWrapper blackboard has no " + LAUNCH_ARGS
                    + " - this launch did not go through FMLTweaker, so there is nothing to add the"
                    + " mods to. " + jars.size() + " mod(s) synced but NOT loaded this launch."
                    + " Start the game through the Forge profile for HOPPER to hand them over.", null);
            return;
        }

        inject(args, jars);
        log.info("[HOPPER] " + jars.size() + " mod(s) handed to FML for THIS launch via " + MODS_ARG
                + " (" + PREFIX + "); no restart needed");
    }

    @SuppressWarnings("unchecked")
    private static Map<String, Object> launchArgs() {
        if (Launch.blackboard == null) {
            return null;
        }
        Object raw = Launch.blackboard.get(LAUNCH_ARGS);
        return raw instanceof Map ? (Map<String, Object>) raw : null;
    }

    private static void inject(Map<String, Object> args, List<String> jars) {
        Set<String> entries = new LinkedHashSet<String>();
        Object existing = args.get(MODS_ARG);
        if (existing instanceof String) {
            for (String s : ((String) existing).split(",")) {
                String trimmed = s.trim();
                if (!trimmed.isEmpty()) {
                    entries.add(trimmed);
                }
            }
        }

        for (String name : jars) {
            entries.add(PREFIX + name);
        }

        StringBuilder sb = new StringBuilder();
        for (String e : entries) {
            if (sb.length() > 0) {
                sb.append(',');
            }
            sb.append(e);
        }
        args.put(MODS_ARG, sb.toString());
    }

    private static Side side() {
        return net.minecraftforge.fml.relauncher.FMLLaunchHandler.side().isServer()
                ? Side.SERVER
                : Side.CLIENT;
    }

    private static String username(Map<String, Object> args) {
        if (args == null) {
            return null;
        }
        Object name = args.get(USERNAME_ARG);
        return name instanceof String ? (String) name : null;
    }

    private static List<String> jarsOnDisk(Hopper.Result result, HopperLog log) {
        List<String> jars = new ArrayList<String>();
        if (result.wanted != null) {
            for (String name : result.wanted) {
                if (Files.isRegularFile(result.dir.resolve(name))) {
                    jars.add(name);
                }
            }
            return jars;
        }

        try {
            DirectoryStream<Path> listing = Files.newDirectoryStream(result.dir);
            try {
                for (Path p : listing) {
                    String name = p.getFileName().toString();
                    if (Files.isRegularFile(p) && name.toLowerCase(Locale.ROOT).endsWith(".jar")) {
                        jars.add(name);
                    }
                }
            } finally {
                listing.close();
            }
        } catch (IOException e) {
            log.warn("[HOPPER] could not list " + result.dir, e);
        }
        return jars;
    }

    private static void warnAboutCoremods(Path dir, List<String> jars, HopperLog log) {
        for (String name : jars) {
            Attributes main = mainAttributes(dir.resolve(name), log);
            if (main == null) {
                continue;
            }
            String plugin = main.getValue("FMLCorePlugin");
            String tweak = main.getValue("TweakClass");
            // An access transformer is applied during the same mods/ scan, so it is lost the same
            // way - and unlike a coremod it crashes at runtime with the mod taking the blame.
            String at = main.getValue("FMLAT");
            if (plugin == null && tweak == null && at == null) {
                continue;
            }
            String declares = plugin != null ? "FMLCorePlugin " + plugin
                    : tweak != null ? "TweakClass " + tweak
                    : "FMLAT " + at;
            log.warn("[HOPPER] " + name + " declares " + declares
                    + " - FML only scans mods/ and the command line for those, and it had"
                    + " finished before HOPPER ran. The file is downloaded and it loads as an"
                    + " ordinary mod, but that half will NOT run from " + PREFIX
                    + " - not this launch and not after a restart. Install it in mods/ by hand,"
                    + " or ask the server owner not to ship it through HOPPER.", null);
        }
    }

    private static Attributes mainAttributes(Path jar, HopperLog log) {
        try {
            JarFile jf = new JarFile(jar.toFile());
            try {
                Manifest mf = jf.getManifest();
                return mf == null ? null : mf.getMainAttributes();
            } finally {
                jf.close();
            }
        } catch (IOException e) {
            log.warn("[HOPPER] could not read the manifest of " + jar.getFileName(), e);
            return null;
        }
    }

    private static HopperLog newLog() {
        HopperLog base;
        try {
            base = new Log4jLog();
        } catch (Throwable t) {
            base = HopperLog.STDOUT;
        }
        return new TlsHintingLog(base);
    }

    private static final class Log4jLog implements HopperLog {
        private final org.apache.logging.log4j.Logger log =
                org.apache.logging.log4j.LogManager.getLogger("HOPPER");

        Log4jLog() {
        }

        @Override
        public void info(String message) {
            log.info(message);
        }

        @Override
        public void warn(String message, Throwable t) {
            if (t == null) {
                log.warn(message);
            } else {
                log.warn(message, t);
            }
        }

        @Override
        public void error(String message, Throwable t) {
            if (t == null) {
                log.error(message);
            } else {
                log.error(message, t);
            }
        }
    }

    private static final class TlsHintingLog implements HopperLog {
        private final HopperLog delegate;

        TlsHintingLog(HopperLog delegate) {
            this.delegate = delegate;
        }

        @Override
        public void info(String message) {
            delegate.info(message);
        }

        @Override
        public void warn(String message, Throwable t) {
            delegate.warn(message, t);
            hint(t);
        }

        @Override
        public void error(String message, Throwable t) {
            delegate.error(message, t);
            hint(t);
        }

        private void hint(Throwable t) {
            for (Throwable c = t; c != null; c = c.getCause()) {
                if (c instanceof SSLHandshakeException) {
                    delegate.error("[HOPPER] TLS handshake failed - this Java 8 is too old for the"
                            + " server's certificate. Use a current Java 8 build, or serve HOPPER"
                            + " over plain HTTP on a LAN.", null);
                    return;
                }
                if (c.getCause() == c) {
                    return;
                }
            }
        }
    }
}
