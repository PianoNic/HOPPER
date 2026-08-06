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
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.Set;
import java.util.function.Consumer;
import java.util.function.Supplier;

/**
 * The Forge adapter for Minecraft 1.17.1, 1.18, 1.18.1 and 1.18.2. Downloads the
 * required mod set before FML scans anything, then hands the downloaded jars to
 * FML as ordinary mod candidates in the same launch.
 *
 * <h2>Why this is a third locator rather than a widened one</h2>
 *
 * {@code IModLocator} has three incompatible shapes across the versions HOPPER
 * supports, and this range sits in the middle of them:
 *
 * <pre>
 *   forgespi 3.2.0   IModLocator                       scanMods() -&gt; List&lt;IModFile&gt;
 *                                                      + findPath + findManifest
 *   forgespi 4.0.9   IModLocator                       scanMods() -&gt; List&lt;IModFile&gt;
 *                                                      findPath and findManifest REMOVED
 *   forgespi 6.0.0   IModLocator extends IModProvider  scanMods() -&gt; List&lt;ModFileOrException&gt;
 * </pre>
 *
 * {@code HopperLocator1165} overrides {@code findPath} and {@code findManifest},
 * which do not exist here; {@code HopperLocator} returns
 * {@code List<ModFileOrException>} and implements a supertype that does not exist
 * yet. Neither compiles against this range. The <em>shape</em> is the same in all
 * three - sync, then delegate to the directory locator FML itself uses - and that
 * shape is all the three share.
 *
 * <h2>The range this actually claims</h2>
 *
 * Compiled against forgespi 4.0.9, which is the oldest any Forge in the range
 * ships (1.17.1-37.0.0). 1.17.1-37.1.1 through 1.18.2-40.1.x ship 4.0.10 with the
 * same five abstract methods. 1.18.2-40.2.0 and later ship 4.0.15-4.x, which turns
 * {@code scanMods()} into a default method and adds a defaulted
 * {@code scanMods(Iterable)} - so this class satisfies every one of them, and
 * overriding the no-arg form is still what the mod-locator pass calls.
 *
 * <p>{@code scanMods(Iterable)} is deliberately <em>not</em> overridden. On
 * 4.0.15-4.x that overload is FML's dependency-locator pass, run after the mod
 * pass over the same locator list; its default returns an empty list, which is the
 * correct answer from a locator that is not a dependency locator. Overriding it
 * would offer HOPPER's jars to FML a second time.
 *
 * <h2>How the jar gets loaded this early</h2>
 *
 * Registered through {@code META-INF/services/…IModLocator}.
 * {@code ModDirTransformerDiscoverer} opens every jar in {@code mods/} as a zip
 * and looks for exactly that entry name; the jars it finds are put on the
 * ModLauncher SERVICE module layer, and {@code ModDiscoverer} then builds its
 * {@code ServiceLoader} from that layer before it scans for mods. That is why this
 * module sets {@code Automatic-Module-Name} while {@code hopper-forge-1165} does
 * not: 1.16.5 used a flat {@code URLClassLoader} with no module layer at all.
 *
 * <p>Nothing is ever written into {@code mods/}. Downloads live in
 * {@code hoppermods/}, a directory HOPPER owns outright.
 *
 * <p>Everything that is not Forge-specific - the download, the hash check, the
 * stale sweep, the config merge - lives in {@link Hopper} in the core, shared with
 * every other adapter.
 */
public final class HopperLocator1182 implements IModLocator {

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
            if (t == null) {
                LOG4J.warn(message);
            } else {
                LOG4J.warn(message, t);
            }
        }

        @Override
        public void error(String message, Throwable t) {
            if (t == null) {
                LOG4J.error(message);
            } else {
                LOG4J.error(message, t);
            }
        }
    };

    /**
     * FML's own directory locator, pointed at {@code hoppermods/}. Built by
     * {@code MODDIRECTORYFACTORY}, so the jars we contribute are constructed by
     * exactly the code that builds the ones in {@code mods/} - same
     * {@code ModFile}, same manifest handling, same code signers.
     */
    private IModLocator delegate;

    /**
     * Filenames the manifest asked for. {@code null} means the sync did not
     * complete, in which case we load whatever was already downloaded rather than
     * blocking the launch.
     */
    private Set<String> wanted;

    @Override
    public String name() {
        return "hopper";
    }

    @Override
    public void initArguments(final Map<String, ?> arguments) {
        Path gameDir = env(IEnvironment.Keys.GAMEDIR).orElseGet(() -> Path.of("."));

        // Never throws - not even an Error. Offline, server down, bad manifest - none of that
        // stops the game from starting; result.wanted is simply null and we load the previous
        // download instead.
        Hopper.Result result = Hopper.run(gameDir, username(arguments), LOG, HopperLocator1182::progress);
        wanted = result.wanted;

        // Built after syncing so the delegate never sees a partially written file.
        delegate = env(Environment.Keys.MODDIRECTORYFACTORY)
                .orElseThrow(() -> new IllegalStateException("MODDIRECTORYFACTORY missing from the launch environment"))
                .build(result.dir, Hopper.DIR);
    }

    @Override
    public List<IModFile> scanMods() {
        if (delegate == null) {
            return Collections.emptyList();
        }

        List<IModFile> found = delegate.scanMods();
        if (wanted == null) {
            return found; // sync failed or disabled: take what we have
        }

        // Belt and braces. sync() already deleted anything stale, but a delete can lose to
        // antivirus or a read-only file, and a leftover jar must not load.
        List<IModFile> kept = new ArrayList<>(found.size());
        for (IModFile file : found) {
            if (file == null || wanted.contains(file.getFileName())) {
                kept.add(file);
            }
        }
        return kept;
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
     * given. {@code null} when neither has it - a dedicated server has no player at
     * all, and that is a fine thing to report.
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
