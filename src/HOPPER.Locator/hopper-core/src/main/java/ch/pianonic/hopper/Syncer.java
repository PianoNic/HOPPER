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

/**
 * Brings {@code hoppermods/} in line with the remote manifest: download what is
 * missing or wrong, delete what is no longer listed.
 *
 * <p>On every loader except Fabric this runs before a single mod jar has been
 * opened, so files here can be replaced and deleted freely - including on
 * Windows.
 *
 * <p>Imports nothing from any mod loader, and nothing outside the JDK at all.
 * That is what makes this one class shared by six adapters rather than copied
 * into each of them, and it is why the hash check below and the path-traversal
 * check in {@link #sanitize} exist exactly once.
 */
final class Syncer {

    /**
     * Our identity file, kept inside the managed directory so it is wiped
     * together with it. Not a jar and never in the manifest, so the cleanup in
     * {@link #sync()} has to be told to spare it.
     */
    private static final String CLIENT_ID = "client-id";

    // Timeouts. The connect timeout maps 1:1 onto the old HttpClient builder. The read timeout
    // does NOT map onto HttpRequest.timeout: that was a deadline for the whole exchange, this is
    // a per-read stall detector. Deliberate, and an improvement - the old 5-minute whole-request
    // deadline would abort a 300 MB modpack on a slow line even while it was making progress,
    // whereas a 60-second stall detector cannot, and still kills a hung server just as fast.
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

    /** What the sync left on disk. Filled by {@link #sync()}, sent by {@link #report(String)}. */
    private final List<Mod> installed = new ArrayList<Mod>();

    private int added;
    private int removed;
    private int migrated;
    private int deferred;

    /**
     * @param modsDir the game's own mods folder, which HOPPER reads and moves out
     *                of but never manages, or null to skip that entirely. Passed
     *                in rather than derived, because the game directory lives in
     *                {@link Hopper#run} and this class only ever knew about
     *                {@code hoppermods/}.
     */
    Syncer(String manifestUrl, String token, Path dir, Path modsDir, Consumer<String> progress,
            HopperLog log) {
        this.manifestUrl = manifestUrl;
        this.dir = dir;
        this.modsDir = modsDir;
        this.progress = progress;
        this.log = log;
        // The manifest URL is the ONE thing a human configured, so it is the only thing that can
        // say which host is HOPPER's. Every other URL in play - a mod's download URL, a redirect
        // target - arrives inside the manifest and is therefore server-supplied data, no more
        // trusted than the filenames sanitize() rejects. Http compares against this and nothing
        // else before it attaches the bearer token.
        this.http = new Http(token, URI.create(manifestUrl), log);
    }

    /** @return the filenames the manifest asked for */
    Set<String> sync() throws Exception {
        List<Entry> mods = fetchManifest();
        Set<String> wanted = new LinkedHashSet<String>();
        installed.clear();
        added = 0;
        removed = 0;

        // MIGRATION. After the manifest, because it needs each entry's modIds and sha256, and
        // before the download loop, because a completed move makes Files.exists(target) below true
        // with the right hash - so the existing check absorbs it and the migration costs no
        // bandwidth. Never throws; a migration that could not run leaves an empty result.
        Migrator.Result migration = new Migrator(modsDir, dir, log).run(mods);
        migrated = migration.moved;
        deferred = migration.deferred;

        for (Entry e : mods) {
            String name = sanitize(e.file);

            // Deferred: the player's own copy could not be moved out of mods/ and is loading from
            // there this launch, so downloading ours would be the second copy - the exact crash
            // this whole path exists to prevent. Deliberately NOT added to wanted either, so the
            // sweep below deletes any copy an earlier launch downloaded and no adapter hands one
            // to the loader. Retried on the next launch.
            if (migration.blocked.contains(name)) continue;

            wanted.add(name);

            Path target = dir.resolve(name);
            String have = Files.exists(target) ? sha256(target) : null;
            if (have == null || !have.equalsIgnoreCase(e.sha256)) {
                have = download(e, target);
                added++;
            }

            // The hash we computed ourselves, not the one the manifest claimed, so
            // the report describes this disk rather than repeating the server's own
            // list back at it.
            installed.add(new Mod(name, have));
        }

        // hoppermods/ belongs to us, so anything unlisted is stale by definition -
        // including half-finished .part files from an interrupted run.
        //
        // Listed first, deleted after: deleting out of an open DirectoryStream is not defined
        // to be safe, and Files.list()'s stream is Java 8 but its terminal .toList() is 16.
        List<Path> stale = new ArrayList<Path>();
        DirectoryStream<Path> listing = Files.newDirectoryStream(dir);
        try {
            for (Path p : listing) {
                // A directory, so hoppermods/replaced/ is already spared - this listing does not
                // recurse either. The name check below spares it a second time on purpose, so that
                // a future change to a recursive listing cannot silently arm a delete over the one
                // directory whose whole point is that nothing in it is ever destroyed.
                if (!Files.isRegularFile(p)) continue;
                String name = p.getFileName().toString();
                if (wanted.contains(name) || CLIENT_ID.equals(name)
                        || Migrator.REPLACED.equals(name)) {
                    continue;
                }
                stale.add(p);
            }
        } finally {
            listing.close();
        }
        for (Path p : stale) {
            if (Files.deleteIfExists(p)) {
                removed++;
                log.info("[HOPPER] removed " + p.getFileName());
            }
        }

        return wanted;
    }

    /**
     * True when this sync wrote or deleted something. Fabric cannot function
     * without it.
     *
     * <p>A migration counts: it moved a jar out of {@code mods/} and into
     * {@code hoppermods/}, which is a change to both directories even though no
     * byte was downloaded.
     */
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

    /**
     * Tells the server what this client ended up with, for the dashboard's client
     * list. Best effort on purpose: an inventory line is never worth a failed
     * launch, so every failure dies here instead of reaching the caller.
     *
     * <p>There is no {@code InterruptedException} arm any more. {@code HttpClient.send}
     * declared one; {@link java.net.HttpURLConnection} blocks in plain socket IO and
     * never throws it, so the generic catch below is the whole story.
     */
    void report(String username) {
        try {
            String body = reportBody(clientId(), username, new ArrayList<Mod>(installed));
            http.post(reportUrl(manifestUrl).toString(), body.getBytes(StandardCharsets.UTF_8),
                    CONNECT_MS, REPORT_READ_MS);
        } catch (Exception e) {
            log.warn("[HOPPER] could not report to the server", e);
        }
    }

    /**
     * The exact bytes of the report body, split out so a test can pin them.
     *
     * <p>No {@code serverId} here, deliberately. HOPPER is per-server now, but the
     * server it belongs to is decided by the bearer token this request carries and
     * is never taken from the body - a client that could name its own server would
     * be able to file a report against someone else's. The shape is unchanged from
     * the single-server client, so an already-installed jar keeps working.
     *
     * <p>Written by hand rather than by a serializer, and not only because Gson is
     * gone: an absent username has to go out as an explicit {@code "username": null}
     * because the server's report DTO requires the property to be present. That used
     * to be bought by {@code GsonBuilder.serializeNulls()}, one call away from being
     * tidied away by someone who did not know why it was there. Here it is a line of
     * code with a test pinned to it.
     */
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

    /**
     * Derived from the manifest URL so there is exactly one URL to configure and
     * the two can never drift apart. Both endpoints sit under the same prefix and
     * {@code resolve} replaces the last path segment:
     * {@code https://host/api/manifest} to {@code https://host/api/clients/report}.
     */
    static URI reportUrl(String manifestUrl) {
        return URI.create(manifestUrl).resolve("clients/report");
    }

    /**
     * Generated once and kept, so the dashboard shows one machine rather than a
     * new row on every launch.
     */
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

    private List<Entry> fetchManifest() throws IOException {
        return parseManifest(http.get(manifestUrl, CONNECT_MS, MANIFEST_READ_MS, "manifest",
                new Http.Sink<String>() {
                    @Override
                    public String read(InputStream in) throws IOException {
                        return Http.utf8(in);
                    }
                }));
    }

    /**
     * The wire format, split out from the HTTP call so a test can pin it without
     * a socket - the same reason {@link #reportBody} exists separately.
     *
     * <p>{@code modIds} is an additive field: the server omits it when a jar
     * declares none, and a server too old to send it at all leaves every entry
     * with an empty list, which makes the migration a silent no-op. The four
     * original fields are read exactly as they always were.
     */
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

            // Never null, so nothing downstream has to check. A non-string element is dropped
            // rather than refused: one odd element in a list HOPPER only uses to match on must not
            // cost the player a launch.
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

    /** @return the verified hash of the file now on disk */
    private String download(Entry e, Path target) throws Exception {
        log.info("[HOPPER] downloading " + e.file + " (" + (e.size / 1024) + " KiB)");
        progress.accept("HOPPER: " + e.file);

        final Path tmp = target.resolveSibling(target.getFileName() + ".part");
        // An anonymous class, not a lambda: identical at release 8, but it keeps invokedynamic
        // out of the bytecode, which matters on the 1.12.2 path where these very class files are
        // handed to a LaunchWrapper URLClassLoader under a possibly ancient 8u.
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

    /**
     * Filenames come from the server, so they are untrusted. Without this,
     * {@code "../../autostart/evil.jar"} would escape the managed directory.
     */
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

    // ---- wire format ----

    /**
     * One entry of the manifest. Plain fields rather than a record, and rather
     * than accessors: it is a parse target, not a value type.
     */
    static final class Entry {
        String file;
        String url;
        String sha256;
        long size;

        /**
         * Every mod id this jar declares, as the server read them out of it.
         * Never null and often empty - a library or a coremod legitimately
         * declares none. This is the only thing that can tell that the player's
         * {@code jei-1.20.1-15.2.0.27.jar} and this entry's
         * {@code jei-1.20.1-15.3.0.4.jar} are one mod.
         */
        List<String> modIds = new ArrayList<String>();
    }

    /** One line of the report body. Same constructor arity and field order as the old record. */
    static final class Mod {
        final String file;
        final String sha256;

        Mod(String file, String sha256) {
            this.file = file;
            this.sha256 = sha256;
        }
    }
}
