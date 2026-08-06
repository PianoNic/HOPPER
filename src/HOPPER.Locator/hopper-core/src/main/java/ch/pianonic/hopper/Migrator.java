package ch.pianonic.hopper;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.DirectoryStream;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.HashSet;
import java.util.IdentityHashMap;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;

/**
 * Gets the player's own copy of a mod out of the way <em>before</em> HOPPER
 * downloads the server's copy of the same mod.
 *
 * <h2>The problem</h2>
 * HOPPER is normally installed into an instance that already has mods. When the
 * player already has a mod the server also distributes, the loader finds the
 * same mod id twice and refuses to start - {@code UniqueModListBuilder}'s
 * "Found duplicate mods:". Filenames and hashes cannot detect that:
 *
 * <pre>
 *   mods/jei-1.20.1-15.2.0.27.jar        the player, an older build
 *   hoppermods/jei-1.20.1-15.3.0.4.jar   HOPPER, the required build
 * </pre>
 *
 * Different name, different hash, same mod. Only the mod id identifies it, which
 * is what {@link ModIds} reads and what the manifest now publishes.
 *
 * <h2>The invariant</h2>
 * <strong>Whatever happens, the same mod is never loaded twice.</strong> Every
 * branch below is written to that, including the ones that fail:
 *
 * <ol>
 *   <li>hash matches an entry - move it into {@code hoppermods/} under the
 *       manifest's filename. The download loop then finds it, hashes it, matches
 *       and skips the download, so a migration costs no bandwidth.</li>
 *   <li>hash does not match - move it into {@code hoppermods/replaced/} so
 *       nothing is ever destroyed, then download the required build normally.</li>
 *   <li>the move fails - Windows keeps every jar in {@code mods/} open, because
 *       {@code ModDirTransformerDiscoverer} reads all of them through SecureJar
 *       before a locator runs - so that mod is <em>not downloaded at all</em>
 *       this launch. It loads from {@code mods/} instead, which is still exactly
 *       one copy, and the migration is retried next launch.</li>
 * </ol>
 *
 * <p>{@code mods/} is otherwise not managed, not swept and never deleted from,
 * and a jar the manifest does not list is never touched at all.
 */
final class Migrator {

    /** The parking directory, inside {@code hoppermods/}. Never scanned, never swept. */
    static final String REPLACED = "replaced";

    /**
     * Parked files keep their original name and gain this.
     *
     * <p>Not decoration. {@code replaced/} is only out of the loader's sight on
     * the loaders whose folder scan is flat, and Fabric and Quilt scanning is
     * not flat - Quilt's plugin manager walks subfolders, and version-named
     * subdirectories of {@code mods/} are a supported feature there. A name that
     * does not end in {@code .jar} is inert on <em>every</em> loader, which is
     * the same trick, for the same reason, as the Fabric adapter's
     * {@code ModsFolderMirror.STALE}.
     */
    static final String PARKED_SUFFIX = ".replaced";

    private static final String README = "README.txt";

    private final Path modsDir;
    private final Path hopperDir;
    private final HopperLog log;

    private final Set<String> blocked = new LinkedHashSet<String>();
    private int moved;
    private int parked;
    private int deferred;

    /**
     * @param modsDir the game's own {@code mods/} folder, or <strong>null</strong>
     *                when this adapter does not actually hand {@code hoppermods/}
     *                to its loader. Null disables the migration entirely: moving
     *                a jar out of {@code mods/} into a directory nothing reads
     *                would unload a working mod. See {@link Hopper#run}.
     */
    Migrator(Path modsDir, Path hopperDir, HopperLog log) {
        this.modsDir = modsDir;
        this.hopperDir = hopperDir;
        this.log = log;
    }

    /** What the migration decided. Read by {@link Syncer#sync()}. */
    static final class Result {

        /**
         * Manifest filenames that must <strong>not</strong> be downloaded this
         * launch, because the player's copy could not be moved and is loading
         * from {@code mods/} instead. Deliberately also kept out of
         * {@code wanted}, so the stale sweep removes any copy an earlier launch
         * downloaded and no adapter hands one to the loader.
         */
        final Set<String> blocked;

        final int moved;
        final int parked;
        final int deferred;

        Result(Set<String> blocked, int moved, int parked, int deferred) {
            this.blocked = Collections.unmodifiableSet(blocked);
            this.moved = moved;
            this.parked = parked;
            this.deferred = deferred;
        }
    }

    /**
     * Never throws. A migration that cannot run is a launch that still has to
     * happen, and doing nothing is always safe: the player keeps the mods they
     * had and HOPPER downloads what it always downloaded.
     */
    Result run(List<Syncer.Entry> manifest) {
        try {
            migrate(manifest);
        } catch (Throwable t) {
            // Same rule as Hopper.run, and Throwable for the same reason: this runs inside a
            // loader's pre-discovery hook. Whatever was already moved stays moved and is already
            // reflected in the counters and in blocked, so the sync continues from a consistent
            // picture rather than from a half-applied one.
            log.error("[HOPPER] the mods folder check failed; nothing further was moved and the"
                    + " sync will continue as if it had not run", t);
        }

        if (moved > 0 || parked > 0 || deferred > 0) {
            log.info("[HOPPER] migrated " + moved + " mod(s) out of " + modsDir + ", parked "
                    + parked + ", deferred " + deferred);
        }

        return new Result(blocked, moved, parked, deferred);
    }

    private void migrate(List<Syncer.Entry> manifest) throws IOException {
        // No mods folder at all is the normal state of a fresh instance, not an error.
        if (modsDir == null || !Files.isDirectory(modsDir)) return;

        Map<String, Syncer.Entry> index = indexManifest(manifest);
        if (index.isEmpty()) return;

        // Per manifest ENTRY rather than per jar, and that ordering is load-bearing: when two jars
        // in mods/ declare one id, the hash-matching one has to win regardless of which sorts first.
        Map<Syncer.Entry, List<Path>> candidates = matchJars(index);

        for (int i = 0; i < manifest.size(); i++) {
            Syncer.Entry e = manifest.get(i);
            List<Path> mine = candidates.get(e);
            if (mine != null && !mine.isEmpty()) migrateEntry(e, mine);
        }
    }

    /**
     * mod id to the manifest entry that owns it.
     *
     * <p>An id claimed by two different entries is dropped from the index
     * entirely. The server is then distributing two jars that declare one mod
     * id, the loader will refuse to start on that no matter what HOPPER does,
     * and there is no way to know which of the two the player's jar is a copy
     * of - so nothing is migrated on that id and the server is told to fix it.
     */
    private Map<String, Syncer.Entry> indexManifest(List<Syncer.Entry> manifest) {
        Map<String, Syncer.Entry> index = new LinkedHashMap<String, Syncer.Entry>();
        Set<String> conflicted = new HashSet<String>();

        for (int i = 0; i < manifest.size(); i++) {
            Syncer.Entry e = manifest.get(i);

            // A filename the download loop is going to reject is a filename this must not move a
            // player's jar onto. Skipping keeps the rejection where it already is, in the loop.
            if (name(e) == null) continue;

            for (int j = 0; j < e.modIds.size(); j++) {
                String id = e.modIds.get(j);
                if (!ModIds.valid(id) || conflicted.contains(id)) continue;

                Syncer.Entry claimed = index.get(id);
                if (claimed == null) {
                    index.put(id, e);
                } else if (claimed != e) {
                    index.remove(id);
                    conflicted.add(id);
                    log.warn("[HOPPER] the server lists both " + claimed.file + " and " + e.file
                            + ", and both declare the mod id " + id + ". Neither was matched"
                            + " against " + modsDir + " - the server has to fix that.", null);
                }
            }
        }

        return index;
    }

    /**
     * Reads every jar in {@code mods/} once and groups them under the manifest
     * entry they are a copy of.
     *
     * <p>A jar that reaches no entry is never touched, which covers both a mod
     * the manifest does not list and a library or coremod that declares no ids
     * at all - the latter reaches nothing by construction and needs no special
     * case. A jar that reaches more than one entry is ambiguous and is also left
     * alone: HOPPER does not guess which of two manifest files it duplicates.
     */
    private Map<Syncer.Entry, List<Path>> matchJars(Map<String, Syncer.Entry> index)
            throws IOException {

        List<Path> jars = new ArrayList<Path>();
        DirectoryStream<Path> listing = Files.newDirectoryStream(modsDir);
        try {
            for (Path p : listing) {
                // Non-recursive on purpose, and regular files only: a version-named subfolder of
                // mods/ is a Fabric and Quilt feature, and HOPPER does not manage those either.
                if (!Files.isRegularFile(p)) continue;
                if (!p.getFileName().toString().toLowerCase(Locale.ROOT).endsWith(".jar")) continue;
                jars.add(p);
            }
        } finally {
            listing.close();
        }

        // Sorted so a run over the same directory always makes the same decisions in the same
        // order, whatever the filesystem happens to hand back.
        Collections.sort(jars, new Comparator<Path>() {
            @Override
            public int compare(Path a, Path b) {
                return a.getFileName().toString().compareTo(b.getFileName().toString());
            }
        });

        // Identity, because Syncer.Entry has no equals() and two entries with the same content are
        // still two entries.
        Map<Syncer.Entry, List<Path>> candidates = new IdentityHashMap<Syncer.Entry, List<Path>>();

        for (int i = 0; i < jars.size(); i++) {
            Path jar = jars.get(i);
            List<String> ids = ModIds.read(jar, log);

            List<Syncer.Entry> hit = new ArrayList<Syncer.Entry>();
            for (int j = 0; j < ids.size(); j++) {
                Syncer.Entry e = index.get(ids.get(j));
                if (e != null && !containsIdentity(hit, e)) hit.add(e);
            }

            if (hit.isEmpty()) continue;

            if (hit.size() > 1) {
                log.warn("[HOPPER] " + jar.getFileName() + " declares mod ids the server spreads"
                        + " across more than one file; leaving it where it is.", null);
                continue;
            }

            Syncer.Entry e = hit.get(0);
            List<Path> mine = candidates.get(e);
            if (mine == null) {
                mine = new ArrayList<Path>();
                candidates.put(e, mine);
            }
            mine.add(jar);
        }

        return candidates;
    }

    /**
     * One manifest entry and every jar in {@code mods/} that is a copy of it.
     *
     * <p>Losers are parked <em>first</em>, and that ordering is why no undo is
     * ever needed: if parking fails, the winner has not been moved yet, so
     * nothing was added to {@code hoppermods/} that would have to be taken back
     * out.
     */
    private void migrateEntry(Syncer.Entry e, List<Path> mine) {
        String target = name(e);
        if (target == null) return;

        Path winner = null;
        List<Path> losers = new ArrayList<Path>();

        for (int i = 0; i < mine.size(); i++) {
            Path jar = mine.get(i);

            String hash;
            try {
                hash = Syncer.sha256(jar);
            } catch (Exception ex) {
                // We could not read a jar we are about to decide about. Downloading now could put a
                // second copy of this mod on the classpath, so defer instead: the player's file
                // loads from mods/ and the invariant holds.
                defer(e, jar.getFileName().toString(), ex);
                return;
            }

            if (winner == null && hash.equalsIgnoreCase(e.sha256)) {
                winner = jar;
            } else {
                losers.add(jar);
            }
        }

        for (int i = 0; i < losers.size(); i++) {
            Path loser = losers.get(i);
            try {
                Path parkedAt = park(loser);
                parked++;
                log.info("[HOPPER] " + loser.getFileName() + " in " + modsDir + " is a different"
                        + " build of " + describe(e) + " than this server distributes; moved it to "
                        + parkedAt + " and downloading the required build. Nothing was deleted.");
            } catch (IOException ex) {
                if (mine.size() > 1) {
                    log.error("[HOPPER] " + modsDir + " contains more than one copy of "
                            + describe(e) + " and " + loser.getFileName() + " could not be moved."
                            + " Delete one of them by hand or the game will refuse to start.", ex);
                }
                defer(e, loser.getFileName().toString(), ex);
                return;
            }
        }

        if (winner == null) return;

        try {
            Files.move(winner, hopperDir.resolve(target), StandardCopyOption.REPLACE_EXISTING);
            moved++;
            log.info("[HOPPER] " + winner.getFileName() + " in " + modsDir + " is already the"
                    + " required build of " + describe(e) + "; moved it into " + hopperDir + " as "
                    + target + " - no download needed");
        } catch (IOException ex) {
            defer(e, winner.getFileName().toString(), ex);
        }
    }

    /**
     * The jar could not be moved, so it stays in {@code mods/} and loads from
     * there. Its manifest entry is not downloaded and not put into
     * {@code wanted}, so any copy an earlier launch downloaded is swept and
     * exactly one copy remains loadable.
     */
    private void defer(Syncer.Entry e, String jarName, Throwable cause) {
        String target = name(e);
        if (target != null) blocked.add(target);
        deferred++;

        log.warn("[HOPPER] could not move " + jarName + " out of " + modsDir + " (in use?), so "
                + e.file + " will NOT be downloaded this launch - your copy of " + describe(e)
                + " loads from " + modsDir + " instead and the mod is not loaded twice. HOPPER will"
                + " try again on the next launch. If this keeps happening, close the game and move "
                + jarName + " out of " + modsDir + " by hand.", cause);
    }

    /**
     * Moves a jar into {@code hoppermods/replaced/} under a name nothing has
     * taken yet.
     *
     * <p>Nothing in there is ever deleted, so a second launch parking a
     * same-named jar must not destroy what the first one parked - which is why
     * there is no REPLACE_EXISTING anywhere in this method.
     */
    private Path park(Path jar) throws IOException {
        Path dir = hopperDir.resolve(REPLACED);
        Files.createDirectories(dir);
        writeReadme(dir);

        String name = jar.getFileName().toString();
        Path target = dir.resolve(name + PARKED_SUFFIX);
        for (int n = 1; Files.exists(target); n++) {
            target = dir.resolve(suffixed(name, "-" + n) + PARKED_SUFFIX);
        }

        Files.move(jar, target);
        return target;
    }

    /** {@code jei-15.2.0.jar} plus {@code -1} is {@code jei-15.2.0-1.jar}. */
    private static String suffixed(String name, String extra) {
        int dot = name.lastIndexOf('.');
        return dot < 0 ? name + extra : name.substring(0, dot) + extra + name.substring(dot);
    }

    /**
     * Written the first time anything is parked and never rewritten, because a
     * folder full of a player's mods with no explanation in it is how a support
     * ticket starts.
     */
    private void writeReadme(Path dir) {
        Path f = dir.resolve(README);
        if (Files.exists(f)) return;

        String text = "Files in this folder are mods HOPPER found in your mods folder that the\n"
                + "server distributes a different build of. Nothing here was deleted and nothing\n"
                + "here is loaded. To put one back, move it into mods/ and remove the "
                + PARKED_SUFFIX + "\n"
                + "suffix from the end of its name - but the server's build will then load as\n"
                + "well, and the game will refuse to start with two copies of the same mod.\n";

        try {
            Files.write(f, text.getBytes(StandardCharsets.UTF_8));
        } catch (IOException e) {
            // A missing explanation is not worth failing a migration over.
            log.warn("[HOPPER] could not write " + f, e);
        }
    }

    /** The manifest's filename for an entry, or null when the manifest's is illegal. */
    private static String name(Syncer.Entry e) {
        try {
            return Syncer.sanitize(e.file);
        } catch (RuntimeException ex) {
            return null;
        }
    }

    /** The mod id if there is exactly one, else the filename. For log lines only. */
    private static String describe(Syncer.Entry e) {
        return e.modIds.size() == 1 ? e.modIds.get(0) : e.file;
    }

    private static boolean containsIdentity(List<Syncer.Entry> list, Syncer.Entry e) {
        for (int i = 0; i < list.size(); i++) {
            if (list.get(i) == e) return true;
        }
        return false;
    }
}
