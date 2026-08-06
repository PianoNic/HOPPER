package ch.pianonic.hopper;

import java.io.IOException;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Properties;

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
 * One key is outside that merge entirely: {@code fabricMirrorMods} is read from
 * the player's file and never from the jar, because it is the player's consent to
 * HOPPER writing into their {@code mods/} directory and a server must not be able
 * to grant itself that. See {@link #mirrorMods()}.
 *
 * <p>Per key, not whole-file, and that is the point: {@code enabled} is
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
 *
 * <p>A plain final class rather than a record: records are Java 16 and this
 * compiles at 8. The accessor names are the record's, so nothing that reads a
 * Config had to change.
 */
public final class Config {

    private static final String DEFAULT_URL = "https://hopper.example.com/api/manifest";

    /**
     * Archive root, not a package. Resources under a package are encapsulated
     * in a named module and this jar becomes one in Forge's SERVICE layer, so the
     * root is the only location that stays readable there.
     */
    static final String EMBEDDED = "/hopper-server.properties";

    /**
     * The key that lets the Fabric adapter write into {@code mods/}. Read from
     * the player's file ONLY - see {@link #mirrorMods()}.
     */
    static final String MIRROR_MODS = "fabricMirrorMods";

    private final String serverId;
    private final String manifestUrl;
    private final String token;
    private final boolean enabled;
    private final boolean mirrorMods;

    Config(String serverId, String manifestUrl, String token, boolean enabled, boolean mirrorMods) {
        this.serverId = serverId;
        this.manifestUrl = manifestUrl;
        this.token = token;
        this.enabled = enabled;
        this.mirrorMods = mirrorMods;
    }

    public String serverId() {
        return serverId;
    }

    public String manifestUrl() {
        return manifestUrl;
    }

    public String token() {
        return token;
    }

    public boolean enabled() {
        return enabled;
    }

    /**
     * May the Fabric adapter write into the game's {@code mods/} directory?
     * <strong>False unless the player wrote {@code fabricMirrorMods=true} into
     * {@code config/hopper.properties} themselves.</strong>
     *
     * <p>Every other adapter hands {@code hoppermods/} to its loader and never touches
     * {@code mods/}. Fabric cannot: it has no pre-discovery hook, so the only way
     * a downloaded jar ever loads there is for HOPPER to copy it into
     * {@code mods/} - which also means deleting from {@code mods/} when the server
     * withdraws a mod. That is a real deviation from the project-wide invariant
     * that downloads live in {@code hoppermods/}, and its failure mode is a file
     * disappearing out of a player's mods folder. {@code ModsFolderMirror}'s
     * ownership record makes it survivable; it does not make it something HOPPER
     * gets to decide on the player's behalf. So it is off until a human turns it
     * on, and the Fabric adapter says exactly that, every launch, until they do.
     *
     * <p>Read from {@code config/hopper.properties} and <em>never</em> from the
     * jar's embedded {@code /hopper-server.properties}, which is the one asymmetry
     * in this class and the point of the whole key: the embedded file is written
     * by the HOPPER server, so honouring it there would let a server grant itself
     * write access to the player's mods folder. The consent has to come from the
     * side that owns the directory.
     */
    public boolean mirrorMods() {
        return mirrorMods;
    }

    static Config load(Path gameDir) throws IOException {
        Properties embedded = embedded();
        Path f = gameDir.resolve("config/hopper.properties");

        if (!Files.exists(f)) {
            Files.createDirectories(f.getParent());
            Files.write(f, template(!embedded.isEmpty()).getBytes(StandardCharsets.UTF_8));
        }

        Properties onDisk = new Properties();
        InputStream in = Files.newInputStream(f);
        try {
            onDisk.load(in);
        } finally {
            in.close();
        }

        return merge(embedded, onDisk);
    }

    /**
     * Empty when this jar was built by hand rather than downloaded.
     *
     * <p>Looked up through {@code Config.class} on purpose: it is the same jar
     * and the same module as the adapter either way, but this class then carries
     * no reference to any loader type, so the precedence rule can be exercised in
     * a plain JVM with no loader on the classpath at all.
     */
    private static Properties embedded() throws IOException {
        Properties p = new Properties();
        InputStream in = Config.class.getResourceAsStream(EMBEDDED);
        if (in != null) {
            try {
                p.load(in);
            } finally {
                in.close();
            }
        }
        return p;
    }

    /** The precedence rule itself, kept free of IO so it can be tested directly. */
    static Config merge(Properties embedded, Properties onDisk) {
        String url = pick(embedded, onDisk, "manifestUrl");
        String token = pick(embedded, onDisk, "token");
        String enabled = pick(embedded, onDisk, "enabled");

        // NOT pick(): the embedded file is server-written, and this key is the player's consent
        // to HOPPER writing into their mods folder. See mirrorMods().
        String mirror = trimToNull(onDisk.getProperty(MIRROR_MODS));

        return new Config(
                pick(embedded, onDisk, "serverId"),
                url == null ? DEFAULT_URL : url,
                token,
                enabled == null || Boolean.parseBoolean(enabled),
                // Absent means off. Boolean.parseBoolean already reads anything that is not
                // "true" as false, so a typo fails closed rather than silently arming it.
                mirror != null && Boolean.parseBoolean(mirror));
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
     *
     * <p>String concatenation rather than a text block - text blocks are Java 15.
     */
    private static String template(boolean selfConfigured) {
        if (selfConfigured) {
            return "# HOPPER client configuration\n"
                    + "#\n"
                    + "# This jar was downloaded from HOPPER and already carries its server id,\n"
                    + "# manifest URL and token inside itself. Nothing else has to be set here.\n"
                    + "#\n"
                    + "# Set enabled=false to stop syncing and launch with whatever is already\n"
                    + "# in hoppermods/. manifestUrl and token may be set here too, but the jar's\n"
                    + "# own values win - download a fresh jar instead of editing them.\n"
                    + "enabled=true\n"
                    + MIRROR_MODS_HELP;
        }
        return "# HOPPER client configuration\n"
                + "enabled=true\n"
                + "manifestUrl=" + DEFAULT_URL + "\n"
                + "# Per-server token from the server. Leave empty for a server without one.\n"
                + "token=\n"
                + MIRROR_MODS_HELP;
    }

    /**
     * Written into both templates, because the one thing a player must not
     * discover by accident is that something started deleting from their mods
     * folder. Only the Fabric adapter reads the key; on every other loader it is
     * inert, and the comment says so rather than leaving it looking optional.
     */
    private static final String MIRROR_MODS_HELP =
            "\n"
            + "# FABRIC ONLY, and off by default.\n"
            + "#\n"
            + "# Fabric has no pre-discovery hook, so HOPPER cannot hand it the jars it just\n"
            + "# downloaded - Fabric only ever looks in mods/. Setting this to true lets HOPPER\n"
            + "# copy its downloads into mods/ and delete the ones it previously put there, so a\n"
            + "# restart actually picks them up. Without it, HOPPER on Fabric downloads into\n"
            + "# hoppermods/ and nothing loads from there, ever.\n"
            + "#\n"
            + "# HOPPER only touches filenames it recorded in hoppermods/mods-mirror.txt, which is a\n"
            + "# list of files HOPPER itself put in mods/. Anything else in mods/ - yours, or\n"
            + "# another mod manager's - is never replaced and never deleted. Delete that file to\n"
            + "# revoke the claim.\n"
            + "#\n"
            + "# Ignored on Forge, NeoForge and Quilt: those load out of hoppermods/ directly.\n"
            + MIRROR_MODS + "=false\n";
}
