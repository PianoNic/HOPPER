package ch.pianonic.hopper;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import org.apache.logging.log4j.LogManager;
import org.apache.logging.log4j.Logger;

import java.io.IOException;
import java.io.InputStream;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.security.MessageDigest;
import java.time.Duration;
import java.util.ArrayList;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;
import java.util.UUID;
import java.util.function.Consumer;

/**
 * Brings {@code hopper/} in line with the remote manifest: download what is
 * missing or wrong, delete what is no longer listed.
 *
 * <p>Runs before FML has opened a single mod jar, so files here can be replaced
 * and deleted freely — including on Windows.
 */
final class Syncer {

    private static final Logger LOG = LogManager.getLogger("HOPPER");

    /**
     * {@code serializeNulls} because an absent username has to go out as an
     * explicit {@code "username": null}: the server's report DTO requires the
     * property to be present, and Gson drops null fields by default.
     */
    private static final Gson GSON = new GsonBuilder().serializeNulls().create();

    /**
     * Our identity file, kept inside the managed directory so it is wiped
     * together with it. Not a jar and never in the manifest, so the cleanup in
     * {@link #sync()} has to be told to spare it.
     */
    private static final String CLIENT_ID = "client-id";

    private final HttpClient http = HttpClient.newBuilder()
            .connectTimeout(Duration.ofSeconds(10))
            .followRedirects(HttpClient.Redirect.NORMAL)
            .build();

    private final String manifestUrl;
    private final String token;
    private final Path dir;
    private final Consumer<String> progress;

    /** What the sync left on disk. Filled by {@link #sync()}, sent by {@link #report(String)}. */
    private final List<Mod> installed = new ArrayList<>();

    Syncer(String manifestUrl, String token, Path dir, Consumer<String> progress) {
        this.manifestUrl = manifestUrl;
        this.token = token;
        this.dir = dir;
        this.progress = progress;
    }

    /** @return the filenames the manifest asked for */
    Set<String> sync() throws Exception {
        Manifest manifest = fetchManifest();
        Set<String> wanted = new LinkedHashSet<>();
        installed.clear();

        for (Entry e : manifest.mods) {
            String name = sanitize(e.file);
            wanted.add(name);

            Path target = dir.resolve(name);
            String have = Files.exists(target) ? sha256(target) : null;
            if (have == null || !have.equalsIgnoreCase(e.sha256)) {
                have = download(e, target);
            }

            // The hash we computed ourselves, not the one the manifest claimed, so
            // the report describes this disk rather than repeating the server's own
            // list back at it.
            installed.add(new Mod(name, have));
        }

        // hopper/ belongs to us, so anything unlisted is stale by definition -
        // including half-finished .part files from an interrupted run.
        try (var listing = Files.list(dir)) {
            for (Path p : listing.filter(Files::isRegularFile).toList()) {
                String name = p.getFileName().toString();
                if (wanted.contains(name) || CLIENT_ID.equals(name)) continue;
                if (Files.deleteIfExists(p)) {
                    LOG.info("[HOPPER] removed {}", name);
                }
            }
        }

        return wanted;
    }

    /**
     * Tells the server what this client ended up with, for the dashboard's client
     * list. Best effort on purpose: an inventory line is never worth a failed
     * launch, so every failure dies here instead of reaching the caller.
     */
    void report(String username) {
        try {
            String body = GSON.toJson(new Report(clientId(), username, List.copyOf(installed)));

            HttpRequest req = request(reportUrl(manifestUrl))
                    .timeout(Duration.ofSeconds(10))
                    .header("Content-Type", "application/json")
                    .POST(HttpRequest.BodyPublishers.ofString(body, StandardCharsets.UTF_8))
                    .build();

            HttpResponse<Void> res = http.send(req, HttpResponse.BodyHandlers.discarding());
            if (res.statusCode() / 100 != 2) {
                LOG.warn("[HOPPER] report returned HTTP {}", res.statusCode());
            }
        } catch (InterruptedException e) {
            // Swallowing this one without re-arming the flag would hide a shutdown
            // from everything that runs after us.
            Thread.currentThread().interrupt();
            LOG.warn("[HOPPER] interrupted while reporting to the server", e);
        } catch (Exception e) {
            LOG.warn("[HOPPER] could not report to the server", e);
        }
    }

    /**
     * Derived from the manifest URL so there is exactly one URL to configure and
     * the two can never drift apart. Both endpoints sit under the same prefix and
     * {@code resolve} replaces the last path segment:
     * {@code https://host/api/manifest} → {@code https://host/api/clients/report}.
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
            String existing = Files.readString(f).trim();
            if (!existing.isEmpty()) return existing;
        }
        String id = UUID.randomUUID().toString();
        Files.writeString(f, id);
        return id;
    }

    /**
     * The manifest, the jars and the report are all the same server behind the
     * same shared token. An unset token means an open server, so send no header
     * at all rather than an empty one, which reads as a malformed credential.
     */
    private HttpRequest.Builder request(URI uri) {
        HttpRequest.Builder b = HttpRequest.newBuilder(uri).header("User-Agent", "HOPPER/1.0");
        if (token != null && !token.isBlank()) {
            b.header("Authorization", "Bearer " + token);
        }
        return b;
    }

    private Manifest fetchManifest() throws Exception {
        HttpRequest req = request(URI.create(manifestUrl))
                .timeout(Duration.ofSeconds(20))
                .GET()
                .build();

        HttpResponse<String> res = http.send(req, HttpResponse.BodyHandlers.ofString(StandardCharsets.UTF_8));
        if (res.statusCode() != 200) {
            throw new IllegalStateException("manifest returned HTTP " + res.statusCode());
        }

        Manifest m = GSON.fromJson(res.body(), Manifest.class);
        if (m == null || m.mods == null) {
            throw new IllegalStateException("manifest is empty or malformed");
        }
        return m;
    }

    /** @return the verified hash of the file now on disk */
    private String download(Entry e, Path target) throws Exception {
        LOG.info("[HOPPER] downloading {} ({} KiB)", e.file, e.size / 1024);
        progress.accept("HOPPER: " + e.file);

        Path tmp = target.resolveSibling(target.getFileName() + ".part");
        HttpRequest req = request(URI.create(e.url))
                .timeout(Duration.ofMinutes(5))
                .GET()
                .build();

        HttpResponse<InputStream> res = http.send(req, HttpResponse.BodyHandlers.ofInputStream());
        if (res.statusCode() != 200) {
            throw new IllegalStateException("download of " + e.file + " returned HTTP " + res.statusCode());
        }
        try (InputStream in = res.body()) {
            Files.copy(in, tmp, StandardCopyOption.REPLACE_EXISTING);
        }

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
        if (name == null || name.isBlank()) {
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
        try (InputStream in = Files.newInputStream(p)) {
            byte[] buf = new byte[16384];
            int n;
            while ((n = in.read(buf)) > 0) md.update(buf, 0, n);
        }
        StringBuilder sb = new StringBuilder(64);
        for (byte b : md.digest()) {
            sb.append(Character.forDigit((b >> 4) & 0xF, 16)).append(Character.forDigit(b & 0xF, 16));
        }
        return sb.toString();
    }

    // ---- wire format ----

    static final class Manifest {
        List<Entry> mods;
    }

    static final class Entry {
        String file;
        String url;
        String sha256;
        long size;
    }

    /** Body of POST /api/clients/report. Component names are the JSON field names. */
    record Report(String clientId, String username, List<Mod> mods) { }

    record Mod(String file, String sha256) { }
}
