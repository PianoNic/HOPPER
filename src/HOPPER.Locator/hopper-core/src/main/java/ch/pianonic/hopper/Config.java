package ch.pianonic.hopper;

import java.io.IOException;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Properties;

public final class Config {
    private static final String DEFAULT_URL = "https://hopper.example.com/api/manifest";

    static final String EMBEDDED = "/hopper-server.properties";

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

    static Config merge(Properties embedded, Properties onDisk) {
        String url = pick(embedded, onDisk, "manifestUrl");
        String token = pick(embedded, onDisk, "token");
        String enabled = pick(embedded, onDisk, "enabled");

        String mirror = trimToNull(onDisk.getProperty(MIRROR_MODS));

        return new Config(
                pick(embedded, onDisk, "serverId"),
                url == null ? DEFAULT_URL : url,
                token,
                enabled == null || Boolean.parseBoolean(enabled),

                mirror != null && Boolean.parseBoolean(mirror));
    }

    private static String pick(Properties embedded, Properties onDisk, String key) {
        String fromJar = trimToNull(embedded.getProperty(key));
        return fromJar != null ? fromJar : trimToNull(onDisk.getProperty(key));
    }

    private static String trimToNull(String value) {
        if (value == null) return null;
        String trimmed = value.trim();
        return trimmed.isEmpty() ? null : trimmed;
    }

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
            + "# Ignored on Forge and NeoForge: those load out of hoppermods/ directly.\n"
            + MIRROR_MODS + "=false\n";
}
