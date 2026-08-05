package ch.pianonic.hopper;

import cpw.mods.modlauncher.Launcher;
import cpw.mods.modlauncher.api.IEnvironment;
import cpw.mods.modlauncher.api.TypesafeMap;
import net.minecraftforge.forgespi.Environment;
import net.minecraftforge.forgespi.locating.IModFile;
import net.minecraftforge.forgespi.locating.IModLocator;
import org.apache.logging.log4j.LogManager;
import org.apache.logging.log4j.Logger;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.Properties;
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
 * the SERVICE layer — which is constructed before {@code ModDiscoverer} runs.
 * That is the whole trick: no restart, and it works under every launcher
 * because every launcher loads {@code mods/}.
 *
 * <p>Nothing is ever written into {@code mods/}. Downloads live in
 * {@code hopper/}, a directory this class owns outright, so there are no open
 * file handles to fight and a player's own mods are never touched.
 */
public final class HopperLocator implements IModLocator {

    private static final Logger LOG = LogManager.getLogger("HOPPER");

    /** Our managed directory. Everything in here is disposable and server-owned. */
    private static final String DIR = "hopper";

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
        Path dir = gameDir.resolve(DIR);

        try {
            Files.createDirectories(dir);
            Config cfg = Config.load(gameDir);

            if (cfg.enabled()) {
                LOG.info("[HOPPER] syncing from {}", cfg.manifestUrl());
                Syncer syncer = new Syncer(cfg.manifestUrl(), cfg.token(), dir, HopperLocator::progress);
                wanted = syncer.sync();
                LOG.info("[HOPPER] {} mod(s) ready", wanted.size());

                // Never throws: the inventory the dashboard shows is not worth a
                // failed launch, so report() handles its own failures.
                syncer.report(username(arguments));
            } else {
                LOG.info("[HOPPER] disabled in config; loading what is already downloaded");
            }
        } catch (Exception e) {
            // Offline, server down, bad manifest - none of that should stop the
            // game from starting. Fall back to the last set we successfully
            // downloaded and say so loudly.
            LOG.error("[HOPPER] sync failed; launching with the previously downloaded mods", e);
            progress("HOPPER: sync failed, using cached mods");
        }

        // Built after syncing so the delegate never sees a partially written file.
        delegate = env(Environment.Keys.MODDIRECTORYFACTORY)
                .orElseThrow(() -> new IllegalStateException("MODDIRECTORYFACTORY missing from the launch environment"))
                .build(dir, DIR);
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
     * given. {@code null} when neither has it — a dedicated server has no player
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

    // ---- config/hopper.properties ----

    /** {@code token} is null when unset, which means "send no Authorization header". */
    record Config(String manifestUrl, String token, boolean enabled) {

        private static final String DEFAULT_URL = "https://hopper.example.com/api/manifest";

        static Config load(Path gameDir) throws IOException {
            Path f = gameDir.resolve("config/hopper.properties");

            if (!Files.exists(f)) {
                Files.createDirectories(f.getParent());
                Files.writeString(f, """
                        # HOPPER client configuration
                        enabled=true
                        manifestUrl=%s
                        # Shared token from the server. Leave empty for a server without one.
                        token=
                        """.formatted(DEFAULT_URL));
                return new Config(DEFAULT_URL, null, true);
            }

            Properties p = new Properties();
            try (var in = Files.newInputStream(f)) {
                p.load(in);
            }
            String token = p.getProperty("token", "").trim();
            return new Config(
                    p.getProperty("manifestUrl", DEFAULT_URL),
                    token.isEmpty() ? null : token,
                    Boolean.parseBoolean(p.getProperty("enabled", "true")));
        }
    }
}
