package ch.pianonic.hopper;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Collections;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Set;

final class Ledger {
    private static final char COMMENT = '#';

    private final Path file;
    private final String header;
    private final HopperLog log;

    Ledger(Path file, String header, HopperLog log) {
        this.file = file;
        this.header = header;
        this.log = log;
    }

    Set<String> read() {
        Set<String> out = new LinkedHashSet<String>();
        if (!Files.isRegularFile(file)) return out;

        try {
            String text = new String(Files.readAllBytes(file), StandardCharsets.UTF_8);
            for (String line : text.split("\n")) {
                String name = line.trim();
                if (name.isEmpty() || name.charAt(0) == COMMENT) continue;

                try {
                    out.add(Syncer.sanitize(name));
                } catch (RuntimeException rejected) {
                    log.warn("[HOPPER] ignoring an illegal entry in " + file + ": " + name, null);
                }
            }
        } catch (IOException e) {
            log.warn("[HOPPER] could not read " + file + "; HOPPER will claim nothing this launch",
                    e);
            return new LinkedHashSet<String>();
        }
        return out;
    }

    void write(Set<String> names) {
        StringBuilder sb = new StringBuilder(names.size() * 32 + 256);
        for (String line : header.split("\n")) {
            sb.append(COMMENT).append(' ').append(line).append('\n');
        }

        List<String> sorted = new ArrayList<String>(names);
        Collections.sort(sorted);
        for (int i = 0; i < sorted.size(); i++) {
            sb.append(sorted.get(i)).append('\n');
        }

        try {
            Files.write(file, sb.toString().getBytes(StandardCharsets.UTF_8));
        } catch (IOException e) {
            log.warn("[HOPPER] could not write " + file + "; the next launch will not know which"
                    + " files are HOPPER's", e);
        }
    }
}
