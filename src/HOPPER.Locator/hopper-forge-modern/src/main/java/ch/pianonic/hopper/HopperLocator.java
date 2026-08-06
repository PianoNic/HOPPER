package ch.pianonic.hopper;

import cpw.mods.modlauncher.Launcher;
import cpw.mods.modlauncher.api.IEnvironment;
import cpw.mods.modlauncher.api.TypesafeMap;
import net.minecraftforge.forgespi.Environment;
import net.minecraftforge.forgespi.locating.IModFile;
import net.minecraftforge.forgespi.locating.IModLocator;
import org.apache.logging.log4j.LogManager;
import org.apache.logging.log4j.Logger;

import java.nio.file.Path;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.Set;
import java.util.function.Consumer;
import java.util.function.Supplier;

/**
 * Downloads the required mod set before FML scans anything, then hands the
 * downloaded jars to FML as ordinary mod candidates in the same launch.
 *
 * <p>Registered through {@code META-INF/services/…IModLocator}. Forge's
 * {@code ModDirTransformerDiscoverer} walks {@code mods/}, reads each jar's
 * module descriptor, and lifts any jar that <em>provides</em> that service into
 * the SERVICE layer - which is constructed before {@code ModDiscoverer} runs.
 * That is the whole trick: no restart, and it works under every launcher
 * because every launcher loads {@code mods/}.
 *
 * <p>Nothing is ever written into {@code mods/}. Downloads live in
 * {@code hoppermods/}, a directory HOPPER owns outright, so there are no open file
 * handles to fight and a player's own mods are never touched.
 *
 * <p>Everything that is not Forge-specific - the download, the hash check, the
 * stale sweep, the config merge - lives in {@link Hopper} in the core, shared
 * with five other adapters. What is left here is the Forge shape of it.
 */
public final class HopperLocator implements IModLocator {

    private static final Logger LOG4J = LogManager.getLogger("HOPPER");

    /**
     * The core refuses to name a logger - a Quilt loader plugin cannot see log4j -
     * so every adapter supplies its own. This is Forge's.
     */
    private static final HopperLog LOG = new HopperLog() {

        @Override
        public void info(String message) {
            LOG4J.info(message);
        }

        @Override
        public void warn(String message, Throwable t) {
            if (t == null) LOG4J.warn(message); else LOG4J.warn(message, t);
        }

        @Override
        public void error(String message, Throwable t) {
            if (t == null) LOG4J.error(message); else LOG4J.error(message, t);
        }
    };

    private IModLocator delegate;

    /**
     * Filenames the manifest asked for. {@code null} means the sync did not
     * complete, in which case we load whatever was already downloaded rather
     * than blocking the launch.
     */
    private Set<String> wanted;

    @Override
    public String name() {
        return "hopper";
    }

    @Override
    public void initArguments(final Map<String, ?> arguments) {
        Path gameDir = env(IEnvironment.Keys.GAMEDIR).orElseGet(() -> Path.of("."));

        // Never throws. Offline, server down, bad manifest - none of that stops the game from
        // starting; result.wanted is simply null and we load the previous download instead.
        Hopper.Result result = Hopper.run(gameDir, username(arguments), LOG, HopperLocator::progress);
        wanted = result.wanted;

        // Built after syncing so the delegate never sees a partially written file.
        delegate = env(Environment.Keys.MODDIRECTORYFACTORY)
                .orElseThrow(() -> new IllegalStateException("MODDIRECTORYFACTORY missing from the launch environment"))
                .build(result.dir, Hopper.DIR);
    }

    @Override
    public List<ModFileOrException> scanMods() {
        if (delegate == null) return List.of();

        List<ModFileOrException> found = delegate.scanMods();
        if (wanted == null) return found; // sync failed or disabled: take what we have

        // Belt and braces. sync() already deleted anything stale, but a delete can
        // lose to antivirus or a read-only file, and a leftover jar must not load.
        return found.stream()
                .filter(e -> e.file() == null || wanted.contains(e.file().getFileName()))
                .toList();
    }

    @Override
    public void scanFile(final IModFile modFile, final Consumer<Path> pathConsumer) {
        delegate.scanFile(modFile, pathConsumer);
    }

    @Override
    public boolean isValid(final IModFile modFile) {
        return delegate != null && delegate.isValid(modFile);
    }

    // ---- launch environment ----

    /**
     * Who is playing, for the dashboard's client list. Minecraft is launched with
     * {@code --username <name>}; FML's locator arguments are checked first in case
     * a launcher ever passes it there, then the command line the JVM itself was
     * given. {@code null} when neither has it - a dedicated server has no player
     * at all, and that is a fine thing to report.
     */
    private static String username(final Map<String, ?> arguments) {
        if (arguments != null && arguments.get("username") instanceof String s && !s.isBlank()) {
            return s;
        }
        String[] launch = System.getProperty("sun.java.command", "").split(" ");
        for (int i = 0; i + 1 < launch.length; i++) {
            if ("--username".equals(launch[i]) && !launch[i + 1].isBlank()) {
                return launch[i + 1];
            }
        }
        return null;
    }

    private static <T> Optional<T> env(Supplier<TypesafeMap.Key<T>> key) {
        return Optional.ofNullable(Launcher.INSTANCE)
                .map(Launcher::environment)
                .flatMap(e -> e.getProperty(key.get()));
    }

    /** Writes a line onto the Forge early-loading window. The only UI we get this early. */
    private static void progress(String message) {
        env(Environment.Keys.PROGRESSMESSAGE).ifPresent(c -> c.accept(message));
    }
}
