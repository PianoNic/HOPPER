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

/**
 * Where the Forge 1.12.x adapter actually works. FML runs this once, from
 * {@code FMLPluginWrapper.injectIntoClassLoader}, during LaunchWrapper's tweaker
 * phase - long before {@code Loader.identifyMods} lists the mods directory.
 *
 * <p><b>What this can promise, and it was read out of the bytecode rather than
 * assumed.</b> {@code Loader.identifyMods} builds its candidate list like this
 * (offsets from {@code forge-1.12.2-14.23.5.2864-universal.jar}):
 *
 * <pre>
 *   213: invokestatic  LibraryManager.flattenLists:(Ljava/io/File;)Ljava/util/List;
 *   220: invokestatic  LibraryManager.gatherLegacyCanidates:(Ljava/io/File;)Ljava/util/List;   -> local 4
 *   299: aload 4                                                                              -> iterate
 *   382: new           class ModCandidate
 *   396: invokevirtual ModDiscoverer.addCandidate:(ModCandidate)
 * </pre>
 *
 * and {@code gatherLegacyCanidates(File mcDir)} opens with
 *
 * <pre>
 *     8: getstatic     Launch.blackboard:Ljava/util/Map;
 *    11: ldc           "forgeLaunchArgs"
 *    24: ldc           "--mods"
 *    52: String.split(",")
 *    89: new File(mcDir, entry) -> File.exists()
 *   155: list.add(file)          "  Adding {} ({}) to the mod list"
 * </pre>
 *
 * so writing a comma separated list of game-directory-relative paths into that
 * map makes FML load those jars <em>in this launch</em>. The map is safe to
 * mutate: {@code FMLTweaker.acceptOptions} stores a defensive copy of the launch
 * arguments there (offset 358, {@code Maps.newHashMap(args)}), and
 * {@code FMLTweaker.getLaunchArguments} rebuilds Minecraft's argv from its own
 * field rather than from the blackboard - so nothing we put here ever reaches
 * the game as a command line argument. Only three classes in the whole Forge jar
 * mention {@code forgeLaunchArgs}: {@code FMLTweaker} writes it,
 * {@code LibraryManager} reads {@code --mods}, {@code ModList} reads
 * {@code --modListFile}.
 *
 * <p>This is the 1.12.2 analogue of the {@code MODDIRECTORYFACTORY} the modern
 * adapters use: an arbitrary directory, no launcher argument, no restart, and
 * {@code mods/} stays a directory HOPPER never writes into.
 *
 * <p><b>What it cannot promise.</b> Two things, and both are detected and said
 * out loud rather than papered over, because a log line that implies a mod
 * loaded when it did not is worse than no log line at all:
 *
 * <ul>
 *   <li>A downloaded jar that is itself a <em>coremod or tweaker</em> loads as an
 *       ordinary mod but its coremod half never runs. {@code discoverCoreMods}
 *       fixes its candidate list from {@code mods/} and the command line at
 *       offset 44 and iterates it at 121, all before any {@code IFMLCallHook};
 *       and the blackboard is rebuilt from argv every launch, so this is
 *       permanent for as long as the file lives in {@code hoppermods/}. Not a
 *       restart problem, and the warning below does not pretend it is one.</li>
 *   <li>A launch that did not go through {@code FMLTweaker} has no
 *       {@code forgeLaunchArgs} to write into. Then nothing is loaded, and the
 *       log says exactly that.</li>
 * </ul>
 */
public final class HopperCallHook implements IFMLCallHook {

    /** The blackboard key {@code FMLTweaker.acceptOptions} writes. */
    private static final String LAUNCH_ARGS = "forgeLaunchArgs";

    /** The launch argument {@code LibraryManager.gatherLegacyCanidates} reads. */
    private static final String MODS_ARG = "--mods";

    private static final String USERNAME_ARG = "--username";

    /**
     * {@code new File(mcDir, entry)} resolves these, so they are relative to the
     * game directory and use a forward slash - {@code java.io.File} accepts that
     * on Windows too, whereas a backslash is an ordinary filename character on
     * Linux.
     */
    private static final String PREFIX = Hopper.DIR + "/";

    private File mcLocation;

    /**
     * Keys in this map: {@code runtimeDeobfuscationEnabled}, {@code mcLocation},
     * {@code classLoader}, {@code coremodLocation} and
     * {@code deobfuscationFileName}. We want the game directory and nothing else.
     */
    @Override
    public void injectData(Map<String, Object> data) {
        Object loc = data.get("mcLocation");
        if (loc instanceof File) {
            this.mcLocation = (File) loc;
        }
    }

    /**
     * {@code FMLPluginWrapper} wraps anything thrown out of here in a
     * {@code RuntimeException} and the launch dies with it, so nothing escapes.
     * HOPPER failing is a reason to play with the mods already on disk, never a
     * reason not to play.
     */
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
            // Every route into this class carries mcLocation, so this is a fallback that should
            // never fire - and "." is what FMLTweaker itself defaults the game directory to.
            gameDirFile = new File(".");
        }
        Path gameDir = gameDirFile.toPath();

        Map<String, Object> args = launchArgs();
        Hopper.Result result = Hopper.run(gameDir, username(args), log, null);

        // wanted is null when the sync did not complete - offline, disabled, bad manifest. Falling
        // back to whatever is already in hoppermods/ is the whole point of that null: a server that is
        // down must not empty the player's mod list.
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

    // ---- the same-launch injection ----

    /**
     * @return the blackboard's copy of the launch arguments, or null when this
     *         launch did not go through {@code FMLTweaker}
     */
    @SuppressWarnings("unchecked")
    private static Map<String, Object> launchArgs() {
        if (Launch.blackboard == null) {
            return null;
        }
        Object raw = Launch.blackboard.get(LAUNCH_ARGS);
        return raw instanceof Map ? (Map<String, Object>) raw : null;
    }

    /**
     * Merges our filenames into {@code --mods} rather than overwriting it:
     * {@code --mods} is also a user-facing launcher argument, and a player who set
     * it deliberately should not lose it because HOPPER is installed.
     */
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

    /** {@code --username} is in the same map, which saves guessing at it. */
    private static String username(Map<String, Object> args) {
        if (args == null) {
            return null;
        }
        Object name = args.get(USERNAME_ARG);
        return name instanceof String ? (String) name : null;
    }

    // ---- what is on disk, and what of it FML can still use ----

    /**
     * The jars to hand over, in manifest order when the sync completed and in
     * directory order when it did not.
     */
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

    /**
     * A jar HOPPER downloads loads as an ordinary mod this launch, but its
     * coremod or tweaker half never runs - and a restart does not fix that,
     * which is why this warning does not offer one.
     *
     * <p>{@code CoreModManager.discoverCoreMods} builds its candidate list at
     * offsets 34-44, from {@code LibraryManager.flattenLists} plus
     * {@code gatherLegacyCanidates} - that is {@code mods/}, {@code mods/1.12.2}
     * and the {@code --mods} that came off the actual command line - and then
     * iterates that fixed list from offset 121. HOPPER runs later, and the
     * blackboard is rebuilt from argv on every launch, so {@code hoppermods/} is
     * never scanned for coremods in this launch or in any later one. Telling a
     * player to restart would send them to do it twice for nothing.
     */
    private static void warnAboutCoremods(Path dir, List<String> jars, HopperLog log) {
        for (String name : jars) {
            Attributes main = mainAttributes(dir.resolve(name), log);
            if (main == null) {
                continue;
            }
            String plugin = main.getValue("FMLCorePlugin");
            String tweak = main.getValue("TweakClass");
            if (plugin == null && tweak == null) {
                continue;
            }
            log.warn("[HOPPER] " + name + " declares "
                    + (plugin != null ? "FMLCorePlugin " + plugin : "TweakClass " + tweak)
                    + " - FML only scans mods/ and the command line for coremods, and it had"
                    + " finished before HOPPER ran. The file is downloaded and it loads as an"
                    + " ordinary mod, but its coremod half will NOT run from " + PREFIX
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
            // A jar we cannot open is a jar FML will complain about too, and more usefully.
            log.warn("[HOPPER] could not read the manifest of " + jar.getFileName(), e);
            return null;
        }
    }

    // ---- logging ----

    /**
     * log4j is on the classpath at coremod time - {@code FMLPluginWrapper} itself
     * logs through it - but a {@code NoClassDefFoundError} thrown out of this
     * class would take the launch down with it, and this adapter runs on the
     * oldest and least predictable JVMs HOPPER supports. So it is tried, and
     * {@code System.out} catches it if it is not there.
     */
    private static HopperLog newLog() {
        HopperLog base;
        try {
            base = new Log4jLog();
        } catch (Throwable t) {
            base = HopperLog.STDOUT;
        }
        return new TlsHintingLog(base);
    }

    /** Kept in its own class so that loading it, not this one, is what needs log4j. */
    private static final class Log4jLog implements HopperLog {

        private final org.apache.logging.log4j.Logger log =
                org.apache.logging.log4j.LogManager.getLogger("HOPPER");

        /** Explicit and package-private so javac needs no synthetic accessor class for it. */
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

    /**
     * 1.12.2 is the one target that routinely runs on a Mojang-shipped 8u from
     * before ISRG Root X1 was in the truststore and before TLS 1.2 was on by
     * default, so a HOPPER server behind Let's Encrypt fails the handshake and
     * "sync failed" alone would cost somebody an afternoon. Named here rather
     * than in the core, because this is the only adapter where it is likely.
     */
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
