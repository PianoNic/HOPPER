package ch.pianonic.hopper;

import cpw.mods.modlauncher.Launcher;
import cpw.mods.modlauncher.api.IEnvironment;
import cpw.mods.modlauncher.api.TypesafeMap;
import net.minecraftforge.forgespi.Environment;
import net.minecraftforge.forgespi.locating.IModDirectoryLocatorFactory;
import net.minecraftforge.forgespi.locating.IModFile;
import net.minecraftforge.forgespi.locating.IModLocator;
import org.apache.logging.log4j.LogManager;
import org.apache.logging.log4j.Logger;

import java.nio.file.Path;
import java.nio.file.Paths;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.Set;
import java.util.function.Consumer;
import java.util.function.Supplier;
import java.util.jar.Manifest;

/**
 * The Forge 1.14.4 to 1.16.5 adapter. Downloads the required mod set before FML
 * scans anything, then hands the downloaded jars to FML as ordinary mod
 * candidates in the same launch.
 *
 * <p>This is <em>not</em> {@code HopperLocator} from {@code hopper-forge-modern}
 * with a different name on it. The two interfaces genuinely differ: at forgespi
 * 3.2.0 and below {@code IModLocator} has no {@code IModProvider} supertype,
 * {@link #scanMods()} returns {@code List<IModFile>} rather than
 * {@code List<ModFileOrException>}, and {@link #findPath} and
 * {@link #findManifest} are abstract here while they are absent from the modern
 * interface. The <em>shape</em> is the same - sync, then delegate to the
 * directory locator FML itself uses - and that shape is all the two share.
 *
 * <p>Compiled against forgespi 1.5.0, the oldest in the range (1.14.4). 1.15.2
 * ships 3.0.0 and 1.16.5 ships 3.2.0, and 3.2.0 only <em>adds</em> the
 * {@code findManifestAndSigners} default, so code written against 1.5.0
 * satisfies all three. Do not reach for {@code Environment.Keys.MODFILEFACTORY}
 * here - it does not exist until 3.2.0.
 *
 * <p>Registered through {@code META-INF/services/…IModLocator}. Forge's
 * {@code ModDirTransformerDiscoverer} opens every jar in {@code mods/} as a
 * {@code ZipFile} and looks for exactly that entry name; the jars it finds are
 * pushed onto {@code LocatorClassLoader} - a flat {@code URLClassLoader}, no
 * module layer, which is why this module wants no
 * {@code Automatic-Module-Name} - and {@code ModDiscoverer} then service-loads
 * them before it scans for mods. No restart, no launcher argument.
 *
 * <p>Nothing is ever written into {@code mods/}. Downloads live in
 * {@code hoppermods/}, a directory HOPPER owns outright.
 *
 * <p>Everything that is not Forge-specific - the download, the hash check, the
 * stale sweep, the config merge - lives in {@link Hopper} in the core, shared
 * with five other adapters. Java 8 all the way through, because 1.16.5 and
 * older run on Java 8.
 */
public final class HopperLocator1165 implements IModLocator {

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
        Path gameDir = env(IEnvironment.Keys.GAMEDIR).orElseGet(new Supplier<Path>() {
            @Override
            public Path get() {
                return Paths.get(".");
            }
        });

        // Never throws. Offline, server down, bad manifest - none of that stops the game from
        // starting; result.wanted is simply null and we load the previous download instead.
        Hopper.Result result = Hopper.run(gameDir, username(arguments), LOG, PROGRESS);
        wanted = result.wanted;

        // Built after syncing so the delegate never sees a partially written file.
        Optional<IModDirectoryLocatorFactory> factory = env(Environment.Keys.MODDIRECTORYFACTORY);
        if (!factory.isPresent()) {
            throw new IllegalStateException("MODDIRECTORYFACTORY missing from the launch environment");
        }
        delegate = factory.get().build(result.dir, Hopper.DIR);
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
        List<IModFile> kept = new ArrayList<IModFile>(found.size());
        for (IModFile file : found) {
            if (file == null || wanted.contains(file.getFileName())) {
                kept.add(file);
            }
        }
        return kept;
    }

    @Override
    public Path findPath(final IModFile modFile, final String... path) {
        return delegate.findPath(modFile, path);
    }

    @Override
    public void scanFile(final IModFile modFile, final Consumer<Path> pathConsumer) {
        delegate.scanFile(modFile, pathConsumer);
    }

    /**
     * Delegated, and in practice never called on this instance: every
     * {@code IModFile} we return was created by the delegate, so
     * {@code modFile.getLocator()} is the delegate and FML asks it, not us. The
     * same is true of the {@code findManifestAndSigners} default that forgespi
     * 3.2.0 adds - which is why this module can be compiled against 1.5.0, where
     * that method does not exist yet, without dropping anyone's code signers.
     */
    @Override
    public Optional<Manifest> findManifest(final Path path) {
        return delegate.findManifest(path);
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
        if (arguments != null) {
            Object fromArgs = arguments.get("username");
            if (fromArgs instanceof String && !((String) fromArgs).trim().isEmpty()) {
                return (String) fromArgs;
            }
        }
        String[] launch = System.getProperty("sun.java.command", "").split(" ");
        for (int i = 0; i + 1 < launch.length; i++) {
            if ("--username".equals(launch[i]) && !launch[i + 1].trim().isEmpty()) {
                return launch[i + 1];
            }
        }
        return null;
    }

    private static <T> Optional<T> env(Supplier<TypesafeMap.Key<T>> key) {
        Launcher launcher = Launcher.INSTANCE;
        if (launcher == null) {
            return Optional.empty();
        }
        // Assigned through the interface on purpose. Launcher.environment() is declared to return
        // the concrete cpw.mods.modlauncher.Environment in every version this module claims
        // (checked: modlauncher 4.1.0, 5.1.0, 8.0.9, 8.1.3 - identical signature), so the call
        // site is stable, but nothing here depends on that class beyond the one call.
        IEnvironment environment = launcher.environment();
        return environment.getProperty(key.get());
    }

    /** Writes a line onto the Forge early-loading window. The only UI we get this early. */
    private static final Consumer<String> PROGRESS = new Consumer<String>() {
        @Override
        public void accept(String message) {
            Optional<Consumer<String>> sink = env(Environment.Keys.PROGRESSMESSAGE);
            if (sink.isPresent()) {
                sink.get().accept(message);
            }
        }
    };
}
