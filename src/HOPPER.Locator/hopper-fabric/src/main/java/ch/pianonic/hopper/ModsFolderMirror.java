package ch.pianonic.hopper;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.DirectoryStream;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.util.ArrayList;
import java.util.Collections;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;

/**
 * Reconciles {@code mods/} against {@code hoppermods/}, and is the reason the Fabric
 * adapter is worth shipping at all.
 *
 * <p>Every other adapter hands {@code hoppermods/} straight to its loader as a
 * source of mod candidates. Fabric has nothing to hand it to: discovery is
 * finished and the loader is frozen before {@code preLaunch} runs, and the one
 * lever that would work - {@code fabric.addMods} - is read inside
 * {@code discoverMods}, so it has to be set on the JVM command line, which is a
 * launcher setting HOPPER refuses to need. Syncing into {@code hoppermods/} alone
 * would therefore change nothing at all, not even after a restart, because
 * nothing on Fabric ever scans that directory.
 *
 * <p>So this one adapter, and only this one, also writes into {@code mods/} -
 * carefully.
 *
 * <h2>What it owns</h2>
 *
 * Exactly the filenames recorded in {@code hoppermods/mods-mirror.txt}, a file this
 * class writes itself. A jar in {@code mods/} that is not in that list is never
 * copied over, never deleted, and never renamed - it is the player's, or another
 * mod manager's. The list is the ownership record, and it is kept inside
 * {@code hoppermods/} so that wiping the managed directory also forgets the claim.
 *
 * <p>The core still syncs {@code hoppermods/} with no knowledge of {@code mods/} at
 * all. The mirror is a Fabric-shaped afterthought bolted on top, not a change to
 * the shared invariant.
 */
final class ModsFolderMirror {

    /** Our ownership record, kept beside the downloads it describes. */
    private static final String LIST = "mods-mirror.txt";

    /**
     * Fabric's {@code DirectoryModCandidateFinder} only accepts {@code .jar}, so a
     * file parked under this suffix is inert - visible to the player, ignored by
     * the loader, and deletable on the next launch.
     */
    private static final String STALE = ".hopper-stale";

    /** Marks the header of {@link #LIST}, which is for the player and not an entry. */
    private static final char COMMENT = '#';

    private final Path modsDir;
    private final Path hopperDir;
    private final HopperLog log;

    /**
     * What we will still claim when this is over.
     *
     * <p>Built up as the reconcile runs rather than assumed to be the wanted set,
     * and that distinction is the whole ownership invariant. A jar we refused to
     * overwrite because it was the player's must NOT end up in here - writing the
     * wanted set out verbatim would claim it, and the next launch would then
     * happily overwrite the very file this one protected.
     */
    private final Set<String> nowOwned = new LinkedHashSet<String>();

    private int copied;
    private int deleted;

    /** Jars we refused to overwrite because a file of that name in {@code mods/} is not ours. */
    private int skipped;

    /**
     * Jars we meant to put into {@code mods/}, or take out of it, and could not -
     * a locked file, a read-only directory, antivirus.
     *
     * <p>Separate from {@link #skipped} because the cause is different - a lost
     * race rather than a refusal - and counted at all because the summary line
     * used to be derived from {@link #copied} and {@link #deleted} alone, which
     * both stay at zero on every one of these paths. That is how a launch where
     * nothing could be mirrored ended up reporting "already up to date".
     */
    private int failed;

    ModsFolderMirror(Path modsDir, Path hopperDir, HopperLog log) {
        this.modsDir = modsDir;
        this.hopperDir = hopperDir;
        this.log = log;
    }

    /** Jars copied into {@code mods/} this launch. */
    int copied() {
        return copied;
    }

    /** Jars removed from {@code mods/} this launch. */
    int deleted() {
        return deleted;
    }

    /**
     * True when {@code mods/} is not what it was when the loader read it. This is
     * the signal - not whether {@code hoppermods/} changed - because {@code mods/} is
     * the only directory Fabric ever looked at.
     */
    boolean changed() {
        return copied > 0 || deleted > 0;
    }

    /**
     * Jars {@code mods/} was supposed to end up with, or end up without, and did
     * not. Zero is the only value that entitles anyone to say "up to date".
     *
     * <p>{@link #changed()} answers "does the player need to restart"; this
     * answers the different question "did this reconcile actually do what it set
     * out to do". They are independent - a launch can change nothing AND fail, and
     * that combination is precisely the one that used to be reported as success.
     */
    int unresolved() {
        return skipped + failed;
    }

    /**
     * Files in {@code mods/} that HOPPER claims after this reconcile. An honest
     * count to quote when the sync itself failed, because it describes the disk
     * rather than a manifest that was never read.
     */
    int owned() {
        return nowOwned.size();
    }

    /**
     * @param wanted the filenames the manifest asked for, or {@code null} when the
     *               sync did not complete. Null does not mean "remove everything":
     *               it means take whatever is already in {@code hoppermods/}, because a
     *               failed sync is not worth an emptied mods folder.
     * @return the number of files in {@code mods/} that this call added or removed
     */
    int reconcile(Set<String> wanted) throws IOException {
        Set<String> target = wanted == null ? jarsIn(hopperDir) : new LinkedHashSet<String>(wanted);
        Set<String> owned = readList();

        Files.createDirectories(modsDir);

        for (String name : target) {
            copyIn(name, owned);
        }

        for (String name : owned) {
            if (target.contains(name)) continue;
            removeFrom(name);
        }

        // Written last, and it records what we ACTUALLY own rather than what we set out to own -
        // see the field javadoc. Written even when nothing moved, so an interrupted launch leaves
        // a record describing what is really in mods/ rather than what we hoped to put there.
        writeList(nowOwned);

        if (skipped > 0) {
            log.warn("[HOPPER] " + skipped + " mod(s) could not be mirrored into " + modsDir
                    + " because a file of the same name is already there and is not ours."
                    + " Rename or remove it, then restart.", null);
        }
        if (failed > 0) {
            log.warn("[HOPPER] " + failed + " mod(s) could not be written to or removed from "
                    + modsDir + ". HOPPER will try again on the next launch.", null);
        }
        return copied + deleted;
    }

    // ---- one file at a time ----

    private void copyIn(String name, Set<String> owned) {
        Path from = hopperDir.resolve(name);
        Path to = modsDir.resolve(name);

        if (!Files.isRegularFile(from)) {
            // The manifest named it but there is nothing in hoppermods/ to copy - the core already
            // said why. Counted rather than ignored: mods/ is now missing a jar that was asked
            // for, and a summary line that did not know about it would call that up to date.
            failed++;
            log.warn("[HOPPER] " + name + " is not in " + hopperDir + ", so it could not be put"
                    + " into " + modsDir, null);
            return;
        }

        try {
            if (Files.exists(to)) {
                // The invariant: we only ever overwrite a file we put there ourselves. A jar with
                // the same name that we do not own belongs to the player and is left alone - and
                // it is deliberately NOT added to nowOwned, so we do not quietly acquire it and
                // overwrite it on the next launch instead.
                if (!owned.contains(name)) {
                    skipped++;
                    log.warn("[HOPPER] not touching " + to + " - same name, but HOPPER did not put it there", null);
                    return;
                }
                if (sameFile(from, to)) {
                    nowOwned.add(name);
                    return;
                }
            }

            Files.copy(from, to, StandardCopyOption.REPLACE_EXISTING, StandardCopyOption.COPY_ATTRIBUTES);
            nowOwned.add(name);
            copied++;
            log.info("[HOPPER] copied " + name + " into " + modsDir);
        } catch (IOException e) {
            // On Windows the loader is holding a ZipFile handle on every jar it discovered, so
            // replacing one in place can simply fail. Nothing to do but say so - the file that is
            // already there keeps working, and the next launch will try again.
            //
            // The claim survives only if there was already a file of ours there to claim: half of
            // a failed copy is still ours to clean up, but a copy that never started is not.
            if (owned.contains(name) && Files.exists(to)) nowOwned.add(name);
            failed++;
            log.warn("[HOPPER] could not copy " + name + " into " + modsDir
                    + " (in use?); it will be retried on the next launch", e);
        }
    }

    private void removeFrom(String name) {
        Path victim = modsDir.resolve(name);
        try {
            if (Files.deleteIfExists(victim)) {
                deleted++;
                log.info("[HOPPER] removed " + name + " from " + modsDir);
                return;
            }
        } catch (IOException deleteFailed) {
            Path parked = modsDir.resolve(name + STALE);
            try {
                Files.move(victim, parked, StandardCopyOption.REPLACE_EXISTING);
                deleted++;
                log.warn("[HOPPER] could not remove " + victim + " (in use); renamed to "
                        + parked.getFileName() + " and will retry next launch", null);
                return;
            } catch (IOException renameFailed) {
                // Still ours, so keep the claim: dropping it here would mean never trying again,
                // and the player would be left with a mod the server has already withdrawn.
                nowOwned.add(name);
                failed++;
                log.error("[HOPPER] could not remove or rename " + victim
                        + " - it will keep loading until you delete it by hand", renameFailed);
                return;
            }
        }
        // Nothing was there to delete, so an earlier launch may have parked it instead.
        sweepParked(name);
    }

    /**
     * Second chance at a file an earlier launch could only rename. By now the
     * loader has no handle on it - it is not a {@code .jar} any more, so it was
     * never discovered.
     */
    private void sweepParked(String name) {
        Path parked = modsDir.resolve(name + STALE);
        try {
            if (Files.deleteIfExists(parked)) {
                log.info("[HOPPER] cleaned up " + parked.getFileName());
            }
        } catch (IOException e) {
            log.warn("[HOPPER] could not clean up " + parked, e);
        }
    }

    // ---- the ownership record ----

    private Set<String> readList() {
        Path f = hopperDir.resolve(LIST);
        Set<String> out = new LinkedHashSet<String>();
        if (!Files.isRegularFile(f)) return out;

        try {
            String text = new String(Files.readAllBytes(f), StandardCharsets.UTF_8);
            for (String line : text.split("\n")) {
                String name = line.trim();
                if (name.isEmpty() || name.charAt(0) == COMMENT) continue;
                // Re-sanitized on the way in as well as on the way out. This file is ours, but it
                // sits in a directory a player can edit, and it names files we are willing to
                // DELETE - so a hand-edited "../../saves/world" must not be honoured.
                try {
                    out.add(Syncer.sanitize(name));
                } catch (RuntimeException rejected) {
                    log.warn("[HOPPER] ignoring an illegal entry in " + f + ": " + name, null);
                }
            }
        } catch (IOException e) {
            // An unreadable record means we cannot prove we own anything, so we claim nothing:
            // no deletes, and no overwrites of files already in mods/.
            log.warn("[HOPPER] could not read " + f + "; no file in " + modsDir
                    + " will be replaced or removed this launch", e);
            return new LinkedHashSet<String>();
        }
        return out;
    }

    private void writeList(Set<String> claimed) {
        Path f = hopperDir.resolve(LIST);
        StringBuilder sb = new StringBuilder(claimed.size() * 32 + 128);
        sb.append(COMMENT).append(" Written by HOPPER. Every file named here is one HOPPER put into mods/\n");
        sb.append(COMMENT).append(" and is therefore one HOPPER may replace or delete. Anything not named\n");
        sb.append(COMMENT).append(" here is yours and is never touched. Delete this file to make HOPPER\n");
        sb.append(COMMENT).append(" forget the claim - it will then leave every one of them alone.\n");
        List<String> sorted = new ArrayList<String>(claimed);
        Collections.sort(sorted);
        for (String name : sorted) {
            sb.append(name).append('\n');
        }
        try {
            Files.write(f, sb.toString().getBytes(StandardCharsets.UTF_8));
        } catch (IOException e) {
            log.warn("[HOPPER] could not write " + f + "; the next launch will not know which"
                    + " files in " + modsDir + " are HOPPER's", e);
        }
    }

    // ---- helpers ----

    /**
     * Cheap "is this already the same jar" test. Size and modification time, not a
     * hash: {@link StandardCopyOption#COPY_ATTRIBUTES} carries the timestamp across
     * on the way in, so a mirrored file matches its source exactly, and re-hashing
     * a modpack's worth of jars on every launch would cost seconds for nothing.
     */
    private static boolean sameFile(Path from, Path to) throws IOException {
        return Files.size(from) == Files.size(to)
                && Files.getLastModifiedTime(from).toMillis() == Files.getLastModifiedTime(to).toMillis();
    }

    private static Set<String> jarsIn(Path dir) throws IOException {
        Set<String> out = new LinkedHashSet<String>();
        if (!Files.isDirectory(dir)) return out;

        DirectoryStream<Path> listing = Files.newDirectoryStream(dir);
        try {
            for (Path p : listing) {
                if (!Files.isRegularFile(p)) continue;
                String name = p.getFileName().toString();
                if (name.toLowerCase(Locale.ROOT).endsWith(".jar")) out.add(name);
            }
        } finally {
            listing.close();
        }
        return out;
    }
}
