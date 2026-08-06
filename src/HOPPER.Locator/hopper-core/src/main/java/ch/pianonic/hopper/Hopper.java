package ch.pianonic.hopper;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Collections;
import java.util.Set;
import java.util.function.Consumer;

/**
 * The one entry point every adapter calls. Create {@code hoppermods/}, read the
 * merged configuration, sync, report - and never throw.
 *
 * <p>An adapter is then about forty lines: work out the game directory the way
 * its loader exposes it, call {@link #run}, and hand {@code result.dir} to
 * whatever that loader accepts as a source of mod candidates.
 *
 * <p>Nothing here names a mod loader. That is enforced by the build rather than
 * by discipline - this module has no loader on its compile classpath at all.
 */
public final class Hopper {

    /** Our managed directory. Everything in here is disposable and server-owned. */
    public static final String DIR = "hoppermods";

    private Hopper() {
    }

    /** What a sync left behind. Read-only, and safe to read after a failure. */
    public static final class Result {

        /** {@code gameDir/hopper}. Created if it was missing, and always non-null. */
        public final Path dir;

        /**
         * Filenames the manifest asked for, or {@code null} when the sync did not
         * complete - offline, disabled, bad manifest. An adapter that gets null
         * must load whatever is already in {@link #dir} rather than nothing:
         * a failed sync is not worth a failed launch.
         */
        public final Set<String> wanted;

        /** Size of {@link #wanted}, or 0. */
        public final int count;

        /** True when at least one file was written or deleted. Fabric needs exactly this. */
        public final boolean changed;

        public final int added;
        public final int removed;

        /**
         * Mods moved out of the game's own {@code mods/} folder into
         * {@link #dir}, because the player already had the required build. Each
         * one is a download that did not have to happen.
         */
        public final int migrated;

        /**
         * Mods the player already had that could NOT be moved - Windows keeps
         * jars in {@code mods/} open. Those load from {@code mods/} this launch
         * and were deliberately not downloaded, so the same mod is still loaded
         * exactly once. Retried on the next launch.
         */
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

    /**
     * @param gameDir  the Minecraft directory - {@code hoppermods/} and
     *                 {@code config/hopper.properties} are both resolved against it
     * @param username who is playing, for the dashboard's client list; null on a
     *                 dedicated server, which is a fine thing to report
     * @param log      the adapter's logger, since the core refuses to name one
     * @param progress a sink for early-loading-screen lines, or null
     */
    public static Result run(Path gameDir, String username, HopperLog log, Consumer<String> progress) {
        return run(gameDir, username, log, progress, true);
    }

    /**
     * @param hopperDirIsLoaded whether this adapter actually hands
     *        {@code hoppermods/} to its loader.
     *
     *        <p>Six adapters do, and use the four-argument overload above. The
     *        Fabric adapter passes the player's {@code fabricMirrorMods}
     *        consent, because on Fabric nothing ever loads out of
     *        {@code hoppermods/} except by way of the mirror copying it into
     *        {@code mods/}.
     *
     *        <p>False switches the {@code mods/} migration off entirely, and it
     *        has to: moving a player's working jar out of {@code mods/} and into
     *        a directory that nothing reads would not de-duplicate a mod, it
     *        would unload one. The core still names no loader - it is told
     *        whether its directory is loaded, not which loader is asking.
     */
    public static Result run(Path gameDir, String username, HopperLog log,
            Consumer<String> progress, boolean hopperDirIsLoaded) {
        Path dir = gameDir.resolve(DIR);
        Consumer<String> sink = progress == null ? NO_PROGRESS : progress;

        try {
            Files.createDirectories(dir);
            Config cfg = Config.load(gameDir);

            if (!cfg.enabled()) {
                log.info("[HOPPER] disabled in config; loading what is already downloaded");
                return new Result(dir, null, false, 0, 0, 0, 0);
            }

            // The server id is logged and never sent: the API resolves the tenant from the bearer
            // token, so a client that could name its own server would be a way around that. It is
            // here so a player with three HOPPER servers can tell from the log which jar they
            // actually installed.
            log.info("[HOPPER] syncing from " + cfg.manifestUrl()
                    + " (server " + (cfg.serverId() == null ? "unset" : cfg.serverId()) + ")");

            Syncer syncer = new Syncer(cfg.manifestUrl(), cfg.token(), dir,
                    hopperDirIsLoaded ? gameDir.resolve("mods") : null, sink, log);
            Set<String> wanted = syncer.sync();
            log.info("[HOPPER] " + wanted.size() + " mod(s) ready");

            // Never throws: the inventory the dashboard shows is not worth a failed launch, so
            // report() handles its own failures.
            syncer.report(username);

            return new Result(dir, wanted, syncer.changed(), syncer.added(), syncer.removed(),
                    syncer.migrated(), syncer.deferred());
        } catch (Throwable t) {
            // Throwable, not Exception, and that difference is the whole "never throw" contract.
            //
            // This method is called from inside a loader's pre-discovery hook. Anything that
            // escapes it is not a failed sync, it is a failed launch - the player gets a crash
            // report instead of a game. catch (Exception) leaves every Error on the table, and
            // they are reachable from here rather than theoretical: a manifest nested a few
            // thousand levels deep used to come back as StackOverflowError out of Json.parse and
            // sail straight past this arm. Json now refuses that document before it recurses -
            // see Json.MAX_DEPTH - but the catch is widened as well, because the fallback below
            // is the correct answer to ANY failure, not only to the ones we thought of first.
            //
            // Nothing is rethrown, including InterruptedException's interrupt flag: HttpURLConnection
            // blocks in plain socket IO and never throws it, so there is no flag to restore.
            //
            // Offline, server down, bad manifest - none of that should stop the game from
            // starting. Fall back to the last set we successfully downloaded and say so loudly.
            log.error("[HOPPER] sync failed; launching with the previously downloaded mods", t);
            sink.accept("HOPPER: sync failed, using cached mods");
            return new Result(dir, null, false, 0, 0, 0, 0);
        }
    }

    private static final Consumer<String> NO_PROGRESS = new Consumer<String>() {
        @Override
        public void accept(String message) {
            // Not every loader has an early-loading screen to write onto.
        }
    };
}
