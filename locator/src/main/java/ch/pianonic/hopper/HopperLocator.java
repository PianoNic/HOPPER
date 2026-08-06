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
import java.io.InputStream;
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
 * the SERVICE layer - which is constructed before {@code ModDiscoverer} runs.
 * That is the whole trick: no restart, and it works under every launcher
 * because every launcher loads {@code mods/}.
 *
 * <p>Nothing is ever written into {@code mods/}. Downloads live in
 * {@code hopper/}, a directory this class owns outright, so there are no open
 * file handles to fight and a player's own mods are never touched.
 *
 * <p>Configuration comes from two places and the jar wins - see {@link Config}.
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
                // The server id is logged and never sent: the API resolves the tenant from the
                // bearer token, so a client that could name its own server would be a way around
                // that. It is here so a player with three HOPPER servers can tell from the log
                // which jar they actually installed.
                LOG.info("[HOPPER] syncing from {} (server {})", cfg.manifestUrl(),
                        cfg.serverId() == null ? "unset" : cfg.serverId());
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

    // ---- configuration ----

    /**
     * Where a client is pointed, and at which server.
     *
     * <h2>Precedence</h2>
     * Two sources, merged <strong>per key</strong>, jar first:
     *
     * <ol>
     *   <li>{@code /hopper-server.properties} - written into this very jar by
     *       HOPPER when it was downloaded from {@code GET /api/servers/{id}/jar}.
     *       Carries {@code serverId}, {@code manifestUrl} and {@code token}.</li>
     *   <li>{@code config/hopper.properties} in the game directory - consulted
     *       only for keys the embedded file does not set.</li>
     * </ol>
     *
     * Per key, not whole-file, and that is the point: {@code enabled} is
     * deliberately never written into the jar, so it stays a player's local kill
     * switch even on a jar that configures everything else itself. A value that
     * is present but blank counts as unset and falls through, so an
     * unconfigured template jar behaves like a jar with no embedded file at all.
     *
     * <p>A downloaded jar therefore works with zero configuration, while a
     * hand-built jar keeps the original file-only behaviour untouched.
     *
     * <p>{@code token} is null when unset, which means "send no Authorization
     * header". {@code serverId} is null on a hand-built jar; it is logged rather
     * than sent, because the server derives the tenant from the token itself and
     * the report body is a fixed contract with a shipped client.
     */
    record Config(String serverId, String manifestUrl, String token, boolean enabled) {

        private static final String DEFAULT_URL = "https://hopper.example.com/api/manifest";

        /**
         * Archive root, not a package. Resources under a package are encapsulated
         * in a named module and this jar becomes one in the SERVICE layer, so the
         * root is the only location that stays readable there.
         */
        static final String EMBEDDED = "/hopper-server.properties";

        static Config load(Path gameDir) throws IOException {
            Properties embedded = embedded();
            Path f = gameDir.resolve("config/hopper.properties");

            if (!Files.exists(f)) {
                Files.createDirectories(f.getParent());
                Files.writeString(f, template(!embedded.isEmpty()));
            }

            Properties onDisk = new Properties();
            try (var in = Files.newInputStream(f)) {
                onDisk.load(in);
            }

            return merge(embedded, onDisk);
        }

        /**
         * Empty when this jar was built by hand rather than downloaded.
         *
         * <p>Looked up through {@code Config.class} rather than the enclosing
         * class on purpose: it is the same jar and the same module either way,
         * but this record then carries no reference to a Forge type, so the
         * precedence rule can be exercised in a plain JVM with no Forge on the
         * classpath at all.
         */
        private static Properties embedded() throws IOException {
            Properties p = new Properties();
            try (InputStream in = Config.class.getResourceAsStream(EMBEDDED)) {
                if (in != null) {
                    p.load(in);
                }
            }
            return p;
        }

        /** The precedence rule itself, kept free of IO so it can be tested directly. */
        static Config merge(Properties embedded, Properties onDisk) {
            String url = pick(embedded, onDisk, "manifestUrl");
            String token = pick(embedded, onDisk, "token");
            String enabled = pick(embedded, onDisk, "enabled");

            return new Config(
                    pick(embedded, onDisk, "serverId"),
                    url == null ? DEFAULT_URL : url,
                    token,
                    enabled == null || Boolean.parseBoolean(enabled));
        }

        /** @return the jar's value, else the file's, else null - blank counts as absent */
        private static String pick(Properties embedded, Properties onDisk, String key) {
            String fromJar = trimToNull(embedded.getProperty(key));
            return fromJar != null ? fromJar : trimToNull(onDisk.getProperty(key));
        }

        private static String trimToNull(String value) {
            if (value == null) return null;
            String trimmed = value.trim();
            return trimmed.isEmpty() ? null : trimmed;
        }

        /**
         * Written once, on first launch. A downloaded jar gets the short version:
         * repeating manifestUrl and token there would invite someone to edit a
         * copy that the jar then overrides, and a rotated token would leave a
         * stale one on disk looking authoritative.
         */
        private static String template(boolean selfConfigured) {
            if (selfConfigured) {
                return """
                        # HOPPER client configuration
                        #
                        # This jar was downloaded from HOPPER and already carries its server id,
                        # manifest URL and token inside itself. Nothing else has to be set here.
                        #
                        # Set enabled=false to stop syncing and launch with whatever is already
                        # in hopper/. manifestUrl and token may be set here too, but the jar's
                        # own values win - download a fresh jar instead of editing them.
                        enabled=true
                        """;
            }
            return """
                    # HOPPER client configuration
                    enabled=true
                    manifestUrl=%s
                    # Per-server token from the server. Leave empty for a server without one.
                    token=
                    """.formatted(DEFAULT_URL);
        }
    }
}
