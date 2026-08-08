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

final class Migrator {
    static final String PARKED = "parked";

    static final String PARKED_SUFFIX = ".parked";

    private static final String README = "README.txt";

    private final Path modsDir;
    private final Path hopperDir;
    private final HopperLog log;

    private final Set<String> blocked = new LinkedHashSet<String>();
    private final Set<String> migrated = new LinkedHashSet<String>();
    private int moved;
    private int parked;
    private int deferred;

    Migrator(Path modsDir, Path hopperDir, HopperLog log) {
        this.modsDir = modsDir;
        this.hopperDir = hopperDir;
        this.log = log;
    }

    static final class Result {
        final Set<String> blocked;

        final Set<String> migrated;

        final int moved;
        final int parked;
        final int deferred;

        Result(Set<String> blocked, Set<String> migrated, int moved, int parked, int deferred) {
            this.blocked = Collections.unmodifiableSet(blocked);
            this.migrated = Collections.unmodifiableSet(migrated);
            this.moved = moved;
            this.parked = parked;
            this.deferred = deferred;
        }
    }

    Result run(List<Syncer.Entry> manifest) {
        try {
            migrate(manifest);
        } catch (Throwable t) {
            log.error("[HOPPER] the mods folder check failed; nothing further was moved and the"
                    + " sync will continue as if it had not run", t);
        }

        if (moved > 0 || parked > 0 || deferred > 0) {
            log.info("[HOPPER] migrated " + moved + " mod(s) out of " + modsDir + ", parked "
                    + parked + ", deferred " + deferred);
        }

        return new Result(blocked, migrated, moved, parked, deferred);
    }

    private void migrate(List<Syncer.Entry> manifest) throws IOException {
        if (modsDir == null || !Files.isDirectory(modsDir)) return;

        Map<String, Syncer.Entry> index = indexManifest(manifest);
        if (index.isEmpty()) return;

        Map<Syncer.Entry, List<Path>> candidates = matchJars(index);

        for (int i = 0; i < manifest.size(); i++) {
            Syncer.Entry e = manifest.get(i);
            List<Path> mine = candidates.get(e);
            if (mine != null && !mine.isEmpty()) migrateEntry(e, mine);
        }
    }

    private Map<String, Syncer.Entry> indexManifest(List<Syncer.Entry> manifest) {
        Map<String, Syncer.Entry> index = new LinkedHashMap<String, Syncer.Entry>();
        Set<String> conflicted = new HashSet<String>();

        for (int i = 0; i < manifest.size(); i++) {
            Syncer.Entry e = manifest.get(i);

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

    private Set<String> mirrorOwned() {
        Path list = hopperDir.resolve(Syncer.MIRROR_LIST);
        if (!Files.isRegularFile(list)) return Collections.emptySet();

        return new Ledger(list, "", log).read();
    }

    private Map<Syncer.Entry, List<Path>> matchJars(Map<String, Syncer.Entry> index)
            throws IOException {
        // Only what the player put there. The mirror's own copies match every manifest entry.
        Set<String> mirrored = mirrorOwned();

        List<Path> jars = new ArrayList<Path>();
        DirectoryStream<Path> listing = Files.newDirectoryStream(modsDir);
        try {
            for (Path p : listing) {
                if (!Files.isRegularFile(p)) continue;
                String name = p.getFileName().toString();
                if (!name.toLowerCase(Locale.ROOT).endsWith(".jar")) continue;
                if (mirrored.contains(name)) continue;
                jars.add(p);
            }
        } finally {
            listing.close();
        }

        Collections.sort(jars, new Comparator<Path>() {
            @Override
            public int compare(Path a, Path b) {
                return a.getFileName().toString().compareTo(b.getFileName().toString());
            }
        });

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
            migrated.add(target);
            moved++;
            log.info("[HOPPER] " + winner.getFileName() + " in " + modsDir + " is already the"
                    + " required build of " + describe(e) + "; moved it into " + hopperDir + " as "
                    + target + " - no download needed");
        } catch (IOException ex) {
            defer(e, winner.getFileName().toString(), ex);
        }
    }

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

    static final long KEEP_PARKED_MS = 3L * 24 * 60 * 60 * 1000;

    // Parked files are the player's own, so HOPPER never deletes one on the spot. After three days
    // nobody is coming back for it, and a folder that only grows is its own kind of mess.
    int sweepParked(long nowMs) {
        Path dir = hopperDir.resolve(PARKED);
        if (!Files.isDirectory(dir)) return 0;

        int swept = 0;

        DirectoryStream<Path> listing;
        try {
            listing = Files.newDirectoryStream(dir);
        } catch (IOException e) {
            return 0;
        }

        try {
            for (Path p : listing) {
                if (!Files.isRegularFile(p)) continue;
                if (!p.getFileName().toString().endsWith(PARKED_SUFFIX)) continue;

                try {
                    if (nowMs - Files.getLastModifiedTime(p).toMillis() < KEEP_PARKED_MS) continue;

                    Files.delete(p);
                    swept++;
                    log.info("[HOPPER] deleted " + p.getFileName() + ", parked for more than three days");
                } catch (IOException e) {
                    log.warn("[HOPPER] could not delete " + p, e);
                }
            }
        } finally {
            try { listing.close(); } catch (IOException ignored) { }
        }

        return swept;
    }

    Path park(Path jar) throws IOException {
        Path dir = hopperDir.resolve(PARKED);
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

    private static String suffixed(String name, String extra) {
        int dot = name.lastIndexOf('.');
        return dot < 0 ? name + extra : name.substring(0, dot) + extra + name.substring(dot);
    }

    private void writeReadme(Path dir) {
        Path f = dir.resolve(README);
        if (Files.exists(f)) return;

        String text = "Files in this folder are mods HOPPER moved out of the way: either a different\n"
                + "build of one the server distributes, or one that was in " + Hopper.DIR + "/ and is\n"
                + "no longer on the server's list. Nothing here was deleted and nothing here is\n"
                + "loaded. To put one back, move it into mods/ and remove the " + PARKED_SUFFIX + "\n"
                + "suffix from the end of its name - but the server's build will then load as\n"
                + "well, and the game will refuse to start with two copies of the same mod.\n"
                + "\n"
                + "HOPPER deletes anything in here that has been parked for more than three days.\n";

        try {
            Files.write(f, text.getBytes(StandardCharsets.UTF_8));
        } catch (IOException e) {
            log.warn("[HOPPER] could not write " + f, e);
        }
    }

    private static String name(Syncer.Entry e) {
        try {
            return Syncer.sanitize(e.file);
        } catch (RuntimeException ex) {
            return null;
        }
    }

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
