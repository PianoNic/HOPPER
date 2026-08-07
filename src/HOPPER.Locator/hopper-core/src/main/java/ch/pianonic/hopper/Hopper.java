package ch.pianonic.hopper;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Collections;
import java.util.Set;
import java.util.function.Consumer;

public final class Hopper {
    public static final String DIR = "hoppermods";

    private Hopper() {
    }

    public static final class Result {
        public final Path dir;

        public final Set<String> wanted;

        public final int count;

        public final boolean changed;

        public final int added;
        public final int removed;

        public final int migrated;

        public final int deferred;

        Result(Path dir, Set<String> wanted, boolean changed, int added, int removed,
                int migrated, int deferred) {
            this.dir = dir;
            this.wanted = wanted == null ? null : Collections.unmodifiableSet(wanted);
            this.count = wanted == null ? 0 : wanted.size();
            this.changed = changed;
            this.added = added;
            this.removed = removed;
            this.migrated = migrated;
            this.deferred = deferred;
        }
    }

    public static Result run(Path gameDir, String username, HopperLog log, Consumer<String> progress) {
        return run(gameDir, username, log, progress, true);
    }

    public static Result run(Path gameDir, String username, HopperLog log,
            Consumer<String> progress, boolean hopperDirIsLoaded) {
        return run(gameDir, username, log, progress, hopperDirIsLoaded, Side.CLIENT);
    }

    public static Result run(Path gameDir, String username, HopperLog log,
            Consumer<String> progress, boolean hopperDirIsLoaded, Side side) {
        Path dir = gameDir.resolve(DIR);
        Consumer<String> sink = progress == null ? NO_PROGRESS : progress;

        try {
            Files.createDirectories(dir);
            Config cfg = Config.load(gameDir);

            if (!cfg.enabled()) {
                log.info("[HOPPER] disabled in config; loading what is already downloaded");
                return new Result(dir, null, false, 0, 0, 0, 0);
            }

            log.info("[HOPPER] syncing from " + cfg.manifestUrl()
                    + " (server " + (cfg.serverId() == null ? "unset" : cfg.serverId()) + ")");

            Syncer syncer = new Syncer(cfg.manifestUrl(), cfg.token(), dir,
                    hopperDirIsLoaded ? gameDir.resolve("mods") : null, sink, log, side);
            Set<String> wanted = syncer.sync();
            log.info("[HOPPER] " + wanted.size() + " mod(s) ready");

            syncer.report(username);

            return new Result(dir, wanted, syncer.changed(), syncer.added(), syncer.removed(),
                    syncer.migrated(), syncer.deferred());
        } catch (Throwable t) {
            log.error("[HOPPER] sync failed; launching with the previously downloaded mods", t);
            sink.accept("HOPPER: sync failed, using cached mods");
            return new Result(dir, null, false, 0, 0, 0, 0);
        }
    }

    private static final Consumer<String> NO_PROGRESS = new Consumer<String>() {
        @Override
        public void accept(String message) {
        }
    };
}
