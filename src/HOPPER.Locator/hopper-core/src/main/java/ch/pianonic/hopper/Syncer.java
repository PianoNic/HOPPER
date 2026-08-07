package ch.pianonic.hopper;

import java.io.IOException;
import java.io.InputStream;
import java.net.URI;
import java.nio.charset.StandardCharsets;
import java.nio.file.DirectoryStream;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.security.MessageDigest;
import java.util.ArrayList;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;
import java.util.UUID;
import java.util.function.Consumer;

final class Syncer {
    private static final String CLIENT_ID = "client-id";

    /** The list of jars HOPPER downloaded itself, and may therefore delete again. */
    static final String DOWNLOADED = "downloaded";

    /** ModsFolderMirror's own ledger. It lives in hoppermods/ and the sweep must not eat it. */
    static final String MIRROR_LIST = "mods-mirror.txt";

    private static final String PART_SUFFIX = ".part";

    private static final String DOWNLOADED_HEADER =
            "Written by HOPPER. Every file named here is one HOPPER downloaded into this\n"
            + "folder and is therefore one HOPPER may delete once the server stops listing\n"
            + "it. Anything not named here is yours - it is moved to " + Migrator.REPLACED + "/\n"
            + "instead of being deleted. Delete this file to make HOPPER forget the claim: it\n"
            + "will then park everything rather than delete it.";

    private static final int CONNECT_MS = 10_000;
    private static final int MANIFEST_READ_MS = 20_000;
    private static final int DOWNLOAD_READ_MS = 60_000;
    private static final int REPORT_READ_MS = 10_000;

    private final String manifestUrl;
    private final Path dir;
    private final Path modsDir;
    private final Consumer<String> progress;
    private final HopperLog log;
    private final Http http;

    private final List<Mod> installed = new ArrayList<Mod>();

    private int added;
    private int removed;
    private int migrated;
    private int deferred;

    private final Side side;

    Syncer(String manifestUrl, String token, Path dir, Path modsDir, Consumer<String> progress,
            HopperLog log) {
        this(manifestUrl, token, dir, modsDir, progress, log, Side.CLIENT);
    }

    Syncer(String manifestUrl, String token, Path dir, Path modsDir, Consumer<String> progress,
            HopperLog log, Side side) {
        this.side = side == null ? Side.CLIENT : side;
        this.manifestUrl = manifestUrl;
        this.dir = dir;
        this.modsDir = modsDir;
        this.progress = progress;
        this.log = log;

        this.http = new Http(token, URI.create(manifestUrl), log);
    }

    Set<String> sync() throws Exception {
        List<Entry> mods = fetchManifest();
        Set<String> wanted = new LinkedHashSet<String>();
        installed.clear();
        added = 0;
        removed = 0;

        Migrator migrator = new Migrator(modsDir, dir, log);
        Migrator.Result migration = migrator.run(mods);

        migrated = migration.moved;
        deferred = migration.deferred;

        Ledger ledger = new Ledger(dir.resolve(DOWNLOADED), DOWNLOADED_HEADER, log);
        Set<String> owned = ledger.read();

        // A jar that just came out of the player's mods folder is theirs from now on, whatever an
        // older ledger claimed about a download of the same name.
        owned.removeAll(migration.migrated);

        for (Entry e : mods) {
            String name = sanitize(e.file);

            if (migration.blocked.contains(name)) continue;

            wanted.add(name);

            Path target = dir.resolve(name);
            String have = Files.exists(target) ? sha256(target) : null;
            if (have == null || !have.equalsIgnoreCase(e.sha256)) {
                have = download(e, target);
                owned.add(name);
                added++;
            }

            installed.add(new Mod(name, have));
        }

        List<Path> stale = new ArrayList<Path>();
        DirectoryStream<Path> listing = Files.newDirectoryStream(dir);
        try {
            for (Path p : listing) {
                if (!Files.isRegularFile(p)) continue;
                String name = p.getFileName().toString();
                if (wanted.contains(name) || CLIENT_ID.equals(name) || DOWNLOADED.equals(name)
                        || MIRROR_LIST.equals(name) || Migrator.REPLACED.equals(name)) {
                    continue;
                }
                stale.add(p);
            }
        } finally {
            listing.close();
        }
        for (Path p : stale) {
            String name = p.getFileName().toString();

            // Ours, so deleting it destroys nothing: the server still has it and the next sync
            // fetches it again. A leftover .part is a half-finished download of ours as well.
            if (owned.contains(name) || name.endsWith(PART_SUFFIX)) {
                if (Files.deleteIfExists(p)) {
                    removed++;
                    log.info("[HOPPER] removed " + name);
                }
                continue;
            }

            // Not ours. Either the player dropped it in, or it came out of their mods folder. Once
            // it leaves the manifest no other copy exists, so it is parked, never unlinked.
            // Deleting a file a person put there is the one thing HOPPER must never do.
            try {
                Path parked = migrator.park(p);
                removed++;
                log.info("[HOPPER] " + name + " is no longer required and HOPPER did not download"
                        + " it, so it was moved to " + parked + " rather than deleted");
            } catch (IOException ex) {
                log.warn("[HOPPER] could not move " + name + " to "
                        + dir.resolve(Migrator.REPLACED) + "; leaving it in place rather than"
                        + " deleting a file that is not ours", ex);
            }
        }

        owned.retainAll(wanted);
        ledger.write(owned);

        return wanted;
    }

    boolean changed() {
        return added > 0 || removed > 0 || migrated > 0;
    }

    int added() {
        return added;
    }

    int removed() {
        return removed;
    }

    int migrated() {
        return migrated;
    }

    int deferred() {
        return deferred;
    }

    void report(String username) {
        try {
            String body = reportBody(clientId(), username, new ArrayList<Mod>(installed));
            http.post(reportUrl(manifestUrl).toString(), body.getBytes(StandardCharsets.UTF_8),
                    CONNECT_MS, REPORT_READ_MS);
        } catch (Exception e) {
            log.warn("[HOPPER] could not report to the server", e);
        }
    }

    static String reportBody(String clientId, String username, List<Mod> mods) {
        StringBuilder sb = new StringBuilder(64 + mods.size() * 96);
        sb.append("{\"clientId\":");
        Json.write(sb, clientId);
        sb.append(",\"username\":");
        Json.write(sb, username);
        sb.append(",\"mods\":[");
        for (int i = 0; i < mods.size(); i++) {
            if (i > 0) sb.append(',');
            Mod m = mods.get(i);
            sb.append("{\"file\":");
            Json.write(sb, m.file);
            sb.append(",\"sha256\":");
            Json.write(sb, m.sha256);
            sb.append('}');
        }
        return sb.append("]}").toString();
    }

    static URI reportUrl(String manifestUrl) {
        return URI.create(manifestUrl).resolve("clients/report");
    }

    private String clientId() throws IOException {
        Path f = dir.resolve(CLIENT_ID);
        if (Files.exists(f)) {
            String existing = new String(Files.readAllBytes(f), StandardCharsets.UTF_8).trim();
            if (!existing.isEmpty()) return existing;
        }
        String id = UUID.randomUUID().toString();
        Files.write(f, id.getBytes(StandardCharsets.UTF_8));
        return id;
    }

    // Only the client set has ever been the default, so a client asks for nothing and keeps the
    // request every shipped jar already makes. The stored manifestUrl is left clean because
    // reportUrl resolves against it and resolve() would drop a query string.
    static String manifestUrlFor(String manifestUrl, Side side) {
        if (side != Side.SERVER) {
            return manifestUrl;
        }
        return manifestUrl + (manifestUrl.indexOf('?') < 0 ? "?" : "&") + "side=" + side.wire();
    }

    private List<Entry> fetchManifest() throws IOException {
        return parseManifest(http.get(manifestUrlFor(manifestUrl, side), CONNECT_MS, MANIFEST_READ_MS, "manifest",
                new Http.Sink<String>() {
                    @Override
                    public String read(InputStream in) throws IOException {
                        return Http.utf8(in);
                    }
                }));
    }

    static List<Entry> parseManifest(String text) {
        Object root;
        try {
            root = Json.parse(text);
        } catch (IllegalArgumentException e) {
            throw new IllegalStateException("manifest is empty or malformed", e);
        }
        List<?> mods = Json.asArray(Json.get(root, "mods"));
        if (mods == null) {
            throw new IllegalStateException("manifest is empty or malformed");
        }

        List<Entry> out = new ArrayList<Entry>(mods.size());
        for (Object o : mods) {
            Entry e = new Entry();
            e.file = Json.string(o, "file");
            e.url = Json.string(o, "url");
            e.sha256 = Json.string(o, "sha256");
            e.size = Json.number(o, "size");

            e.modIds = new ArrayList<String>();
            List<?> ids = Json.asArray(Json.get(o, "modIds"));
            if (ids != null) {
                for (Object id : ids) {
                    if (id instanceof String) e.modIds.add((String) id);
                }
            }

            out.add(e);
        }
        return out;
    }

    private String download(Entry e, Path target) throws Exception {
        log.info("[HOPPER] downloading " + e.file + " (" + (e.size / 1024) + " KiB)");
        progress.accept("HOPPER: " + e.file);

        final Path tmp = target.resolveSibling(target.getFileName() + ".part");

        http.get(e.url, CONNECT_MS, DOWNLOAD_READ_MS, "download of " + e.file,
                new Http.Sink<Void>() {
                    @Override
                    public Void read(InputStream in) throws IOException {
                        Files.copy(in, tmp, StandardCopyOption.REPLACE_EXISTING);
                        return null;
                    }
                });

        String actual = sha256(tmp);
        if (!actual.equalsIgnoreCase(e.sha256)) {
            Files.deleteIfExists(tmp);
            throw new SecurityException(
                    "hash mismatch for " + e.file + " (expected " + e.sha256 + ", got " + actual + ")");
        }
        Files.move(tmp, target, StandardCopyOption.REPLACE_EXISTING);
        return actual;
    }

    static String sanitize(String name) {
        if (name == null || name.trim().isEmpty()) {
            throw new IllegalArgumentException("empty filename in manifest");
        }
        if (name.contains("/") || name.contains("\\") || name.contains("..") || name.startsWith(".")) {
            throw new SecurityException("illegal filename in manifest: " + name);
        }
        if (!name.toLowerCase(Locale.ROOT).endsWith(".jar")) {
            throw new SecurityException("manifest entry is not a jar: " + name);
        }
        return name;
    }

    static String sha256(Path p) throws Exception {
        MessageDigest md = MessageDigest.getInstance("SHA-256");
        InputStream in = Files.newInputStream(p);
        try {
            byte[] buf = new byte[16384];
            int n;
            while ((n = in.read(buf)) > 0) md.update(buf, 0, n);
        } finally {
            in.close();
        }
        StringBuilder sb = new StringBuilder(64);
        for (byte b : md.digest()) {
            sb.append(Character.forDigit((b >> 4) & 0xF, 16)).append(Character.forDigit(b & 0xF, 16));
        }
        return sb.toString();
    }

    static final class Entry {
        String file;
        String url;
        String sha256;
        long size;

        List<String> modIds = new ArrayList<String>();
    }

    static final class Mod {
        final String file;
        final String sha256;

        Mod(String file, String sha256) {
            this.file = file;
            this.sha256 = sha256;
        }
    }
}
