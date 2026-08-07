package ch.pianonic.hopper;

import java.io.BufferedReader;
import java.io.IOException;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.nio.charset.Charset;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.util.Collections;
import java.util.LinkedHashSet;
import java.util.Set;

/**
 * Remembers which files in hoppermods/ came out of the player's own mods folder.
 *
 * <p>This has to survive the JVM, and that is the whole point of it. A migration and the day the
 * admin drops that mod from the server are almost never the same launch: the file is moved in once,
 * and weeks later it stops being listed. Without a record on disk the stale sweep cannot tell a jar
 * the player installed from one HOPPER downloaded, and it deletes the player's only copy.
 *
 * <p>Plain text, one filename per line, so a human looking at hoppermods/ can read it and a corrupt
 * line costs one entry rather than the whole file. Java 8, no dependencies.
 */
final class MigratedIndex {

    /** Lives in hoppermods/ next to client-id, and like it must be spared by the sweep. */
    static final String FILE = "migrated.txt";

    private static final Charset UTF8 = Charset.forName("UTF-8");

    private final Path file;
    private final HopperLog log;
    private final Set<String> names = new LinkedHashSet<String>();

    MigratedIndex(Path hopperDir, HopperLog log) {
        this.file = hopperDir.resolve(FILE);
        this.log = log;
        read();
    }

    private void read() {
        if (!Files.isRegularFile(file)) return;
        BufferedReader r = null;
        try {
            r = new BufferedReader(new InputStreamReader(Files.newInputStream(file), UTF8));
            String line;
            while ((line = r.readLine()) != null) {
                String name = line.trim();
                // The header this file is written with, skipped on the way back in. Without this
                // the comments are read as filenames and re-written under a fresh header, so the
                // file grows by two lines on every launch.
                if (name.startsWith("#")) continue;
                // A filename, never a path. Anything else is a corrupt line or an attempt to point
                // the sweep somewhere it has no business writing.
                if (name.isEmpty() || name.indexOf('/') >= 0 || name.indexOf('\\') >= 0
                        || name.contains("..")) {
                    continue;
                }
                names.add(name);
            }
        } catch (IOException e) {
            // A lost index means migrated jars are treated as ordinary downloads again. That is bad
            // but recoverable; failing the launch over it would not be.
            log.warn("[HOPPER] could not read " + file + "; jars migrated earlier may be deleted"
                    + " instead of moved to " + Migrator.REPLACED + " when they leave the manifest", e);
        } finally {
            close(r);
        }
    }

    boolean contains(String name) {
        return names.contains(name);
    }

    void add(String name) {
        names.add(name);
    }

    /** Called once a migrated jar has been parked, so the index does not grow without bound. */
    void remove(String name) {
        names.remove(name);
    }

    Set<String> all() {
        return Collections.unmodifiableSet(names);
    }

    /**
     * Written through a temporary file: a half-written index read on the next launch would be worse
     * than no index at all, because it would silently protect only some of the player's jars.
     */
    void save() {
        try {
            Files.createDirectories(file.getParent());

            if (names.isEmpty()) {
                Files.deleteIfExists(file);
                return;
            }

            Path tmp = file.resolveSibling(FILE + ".part");
            OutputStream out = Files.newOutputStream(tmp);
            try {
                out.write(("# Jars in this folder that came from the player's mods folder.\n"
                        + "# HOPPER moves these to " + Migrator.REPLACED
                        + "/ instead of deleting them.\n").getBytes(UTF8));
                for (String n : names) {
                    out.write((n + "\n").getBytes(UTF8));
                }
            } finally {
                out.close();
            }
            Files.move(tmp, file, StandardCopyOption.REPLACE_EXISTING);
        } catch (IOException e) {
            log.warn("[HOPPER] could not write " + file, e);
        }
    }

    private static void close(BufferedReader r) {
        if (r == null) return;
        try {
            r.close();
        } catch (IOException ignored) {
            // nothing useful to do while closing a reader
        }
    }
}
