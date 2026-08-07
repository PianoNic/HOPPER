package ch.pianonic.hopper;

import java.io.IOException;
import java.nio.file.DirectoryStream;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.util.LinkedHashSet;
import java.util.Locale;
import java.util.Set;

final class ModsFolderMirror {
    private static final String HEADER =
            "Written by HOPPER. Every file named here is one HOPPER put into mods/ and is\n"
            + "therefore one HOPPER may replace or delete. Anything not named here is yours\n"
            + "and is never touched. Delete this file to make HOPPER forget the claim - it\n"
            + "will then leave every one of them alone.";

    private static final String STALE = ".hopper-stale";

    private final Path modsDir;
    private final Path hopperDir;
    private final HopperLog log;
    private final Ledger ledger;

    private final Set<String> nowOwned = new LinkedHashSet<String>();

    private int copied;
    private int deleted;

    private int skipped;

    private int failed;

    ModsFolderMirror(Path modsDir, Path hopperDir, HopperLog log) {
        this.modsDir = modsDir;
        this.hopperDir = hopperDir;
        this.log = log;
        this.ledger = new Ledger(hopperDir.resolve(Syncer.MIRROR_LIST), HEADER, log);
    }

    int copied() {
        return copied;
    }

    int deleted() {
        return deleted;
    }

    boolean changed() {
        return copied > 0 || deleted > 0;
    }

    int unresolved() {
        return skipped + failed;
    }

    int owned() {
        return nowOwned.size();
    }

    int reconcile(Set<String> wanted) throws IOException {
        Set<String> target = wanted == null ? jarsIn(hopperDir) : new LinkedHashSet<String>(wanted);
        Set<String> owned = ledger.read();

        Files.createDirectories(modsDir);

        for (String name : target) {
            copyIn(name, owned);
        }

        for (String name : owned) {
            if (target.contains(name)) continue;
            removeFrom(name);
        }

        ledger.write(nowOwned);

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

    private void copyIn(String name, Set<String> owned) {
        Path from = hopperDir.resolve(name);
        Path to = modsDir.resolve(name);

        if (!Files.isRegularFile(from)) {
            failed++;
            log.warn("[HOPPER] " + name + " is not in " + hopperDir + ", so it could not be put"
                    + " into " + modsDir, null);
            return;
        }

        try {
            if (Files.exists(to)) {
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
                nowOwned.add(name);
                failed++;
                log.error("[HOPPER] could not remove or rename " + victim
                        + " - it will keep loading until you delete it by hand", renameFailed);
                return;
            }
        }

        sweepParked(name);
    }

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
