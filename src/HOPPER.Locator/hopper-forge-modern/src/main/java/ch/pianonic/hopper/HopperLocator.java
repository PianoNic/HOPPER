package ch.pianonic.hopper;

import cpw.mods.modlauncher.Launcher;
import cpw.mods.modlauncher.api.IEnvironment;
import cpw.mods.modlauncher.api.TypesafeMap;
import net.minecraftforge.api.distmarker.Dist;
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

public final class HopperLocator implements IModLocator {
    private static final Logger LOG4J = LogManager.getLogger("HOPPER");

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

    private Set<String> wanted;

    @Override
    public String name() {
        return "hopper";
    }

    @Override
    public void initArguments(final Map<String, ?> arguments) {
        Path gameDir = env(IEnvironment.Keys.GAMEDIR).orElseGet(() -> Path.of("."));

        Hopper.Result result = Hopper.run(gameDir, username(arguments), LOG, HopperLocator::progress, true, side());
        wanted = result.wanted;

        delegate = env(Environment.Keys.MODDIRECTORYFACTORY)
                .orElseThrow(() -> new IllegalStateException("MODDIRECTORYFACTORY missing from the launch environment"))
                .build(result.dir, Hopper.DIR);
    }

    @Override
    public List<ModFileOrException> scanMods() {
        if (delegate == null) return List.of();

        List<ModFileOrException> found = delegate.scanMods();
        if (wanted == null) return found;

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

    private static String username(final Map<String, ?> arguments) {
        if (arguments != null && arguments.get("username") instanceof String s && !s.isBlank()) {
            return s;
        }
        return LaunchArgs.username();
    }

    private static Side side() {
        return env(Environment.Keys.DIST)
                .map(new java.util.function.Function<Dist, Side>() {
                    @Override
                    public Side apply(Dist dist) {
                        return dist == Dist.DEDICATED_SERVER ? Side.SERVER : Side.CLIENT;
                    }
                })
                .orElse(Side.CLIENT);
    }

    private static <T> Optional<T> env(Supplier<TypesafeMap.Key<T>> key) {
        return Optional.ofNullable(Launcher.INSTANCE)
                .map(Launcher::environment)
                .flatMap(e -> e.getProperty(key.get()));
    }

    private static void progress(String message) {
        env(Environment.Keys.PROGRESSMESSAGE).ifPresent(c -> c.accept(message));
    }
}
