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

public final class HopperLocator1165 implements IModLocator {
    private static final Logger LOG4J = LogManager.getLogger("HOPPER");

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

    private IModLocator delegate;

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

        Hopper.Result result = Hopper.run(gameDir, username(arguments), LOG, PROGRESS);
        wanted = result.wanted;

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
            return found;
        }

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

    @Override
    public Optional<Manifest> findManifest(final Path path) {
        return delegate.findManifest(path);
    }

    @Override
    public boolean isValid(final IModFile modFile) {
        return delegate != null && delegate.isValid(modFile);
    }

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

        IEnvironment environment = launcher.environment();
        return environment.getProperty(key.get());
    }

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
