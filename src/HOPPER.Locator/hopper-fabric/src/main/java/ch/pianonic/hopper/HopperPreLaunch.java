package ch.pianonic.hopper;

import net.fabricmc.loader.api.FabricLoader;
import net.fabricmc.loader.api.entrypoint.PreLaunchEntrypoint;
import org.apache.logging.log4j.LogManager;
import org.apache.logging.log4j.Logger;

import java.nio.file.Path;

/**
 * The Fabric adapter. <strong>Degraded on purpose, and it says so out loud every
 * single launch.</strong>
 *
 * <h2>Why this one cannot keep the product promise</h2>
 *
 * HOPPER's whole point is that it downloads the required mod set and hands it to
 * the loader in the <em>same</em> launch. That works wherever the loader exposes
 * a pre-discovery hook. Fabric does not have one, and this is not an oversight
 * that a clever ordering trick gets around - it is the shape of {@code Knot.init}:
 *
 * <pre>
 *   loader.load();      // ALL discovery and resolution
 *   loader.freeze();    // "Frozen - cannot load additional mods!"
 *   FabricMixinBootstrap.init(...);
 *   classLoader.initializeTransformers();
 *   loader.invokeEntrypoints("preLaunch", ...);   &lt;-- we are here
 * </pre>
 *
 * By the time this method runs, discovery is finished, resolution is finished,
 * the loader is frozen, Mixin is bootstrapped and the transformers are
 * initialized. Fabric's entire public API is 23 files and none of them is a
 * locator, a discoverer or a candidate finder; {@code ModCandidateFinder} is
 * {@code impl} and the three finders are hard-wired in
 * {@code FabricLoaderImpl.setup()} rather than service-loaded.
 *
 * <p>The one remaining lever is {@code fabric.addMods}, and it is read
 * <em>inside</em> {@code discoverMods}, so setting it from here is far too late.
 * Setting it early enough means putting it on the JVM command line, which is a
 * launcher setting - and a locator that only works if the player first edits
 * their launcher is not a locator, it is a README.
 *
 * <p>So: sync now, mirror into {@code mods/} so the next launch actually differs,
 * and tell the player plainly that a restart is required. Every log line below is
 * written to be impossible to misread as "your mods are live".
 *
 * <h2>Why the mirror is off until asked for</h2>
 *
 * Writing into {@code mods/} is a deviation from the invariant every other
 * adapter keeps - downloads live in {@code hoppermods/} and a player's own files are
 * never touched - and its failure mode is a jar disappearing out of a folder the
 * player owns. {@code ModsFolderMirror}'s ownership record makes that survivable
 * rather than reckless, but survivable is not the same as sanctioned. So the
 * mirror runs only when {@code fabricMirrorMods=true} is in the player's
 * {@code config/hopper.properties}, and when it is not, this class syncs
 * {@code hoppermods/}, says plainly that nothing will load, and says exactly which
 * line to add. The flag is deliberately not readable from the jar's embedded
 * server-written properties - see {@link Config#mirrorMods()}.
 *
 * <p>Declared through {@code fabric.mod.json}'s {@code entrypoints.preLaunch},
 * which is the literal key {@code Knot} invokes.
 */
public final class HopperPreLaunch implements PreLaunchEntrypoint {

    private static final Logger LOG4J = LogManager.getLogger("HOPPER");

    /**
     * The core refuses to name a logger, so every adapter supplies its own. Fabric
     * gets log4j: Minecraft has shipped it in every version Fabric supports, and
     * {@code Knot} unlocks the game classpath one line before it invokes
     * {@code preLaunch}.
     */
    private static final HopperLog LOG = new HopperLog() {

        @Override
        public void info(String message) {
            LOG4J.info(message);
        }

        @Override
        public void warn(String message, Throwable t) {
            if (t == null) LOG4J.warn(message); else LOG4J.warn(message, t);
        }

        @Override
        public void error(String message, Throwable t) {
            if (t == null) LOG4J.error(message); else LOG4J.error(message, t);
        }
    };

    @Override
    public void onPreLaunch() {
        // First, before anything can be mistaken for success. Stated every launch, not only when
        // something changed, so the limitation is never a surprise at the moment it bites.
        LOG.warn("[HOPPER] Fabric exposes no pre-discovery hook. This entrypoint runs after mod"
                + " discovery has finished and the loader is frozen, so nothing synced now can load"
                + " until you restart.", null);

        FabricLoader fabric = FabricLoader.getInstance();
        Path gameDir = fabric.getGameDir();

        // Read BEFORE the sync, because it is also the answer to a second question the core has to
        // ask: may HOPPER move a duplicate out of mods/?
        //
        // On every other loader that move is unambiguously right - the player's jar comes out of
        // mods/ and hoppermods/ is handed to the loader, so the mod still loads and it loads once.
        // On Fabric, hoppermods/ is loaded only by way of the mirror. With the mirror off, moving a
        // jar out of mods/ would not de-duplicate anything, it would silently unload a mod the
        // player installed themselves. So the same consent gates both.
        boolean mirrorMods = mirrorMods(gameDir);

        // Never throws. result.wanted is null when the sync did not complete, and the mirror then
        // reconciles against whatever is already in hoppermods/ rather than emptying mods/.
        Hopper.Result result = Hopper.run(gameDir, username(fabric), LOG, null, mirrorMods);

        if (!mirrorMods) {
            declineToTouchModsFolder(result);
            return;
        }

        // With the mirror on, a migrated jar leaves mods/ here and comes straight back in
        // reconcile() below - under the manifest's filename and recorded as HOPPER's. The two
        // directions look contradictory at a glance and are not: one copy either way, now owned.
        ModsFolderMirror mirror = new ModsFolderMirror(gameDir.resolve("mods"), result.dir, LOG);
        try {
            mirror.reconcile(result.wanted);
        } catch (Throwable t) {
            // Same rule as the core, and Throwable for the same reason: this runs inside the
            // loader's entrypoint invocation, so anything that escapes is a crash report instead
            // of a game. Nothing HOPPER does is worth a failed launch.
            LOG.error("[HOPPER] could not update the mods folder; the game will start with the"
                    + " mods it already has", t);
            return;
        }

        report(result, mirror);
    }

    // ---- the opt-in ----

    /**
     * Has the player agreed to HOPPER writing into {@code mods/}?
     *
     * <p>This adapter is the only one that writes anywhere except {@code hoppermods/},
     * and it has to: nothing on Fabric ever scans {@code hoppermods/}, so without the
     * mirror a sync here changes nothing at all, not even after a restart. But the
     * failure mode of the mirror is a file vanishing out of a player's mods folder,
     * and that is not a call HOPPER gets to make on their behalf. So it is off
     * until {@code fabricMirrorMods=true} is in {@code config/hopper.properties} -
     * the player's file, never the server-written one embedded in the jar.
     *
     * @return false on any failure to read the configuration, because the whole
     *         point of the flag is that HOPPER does not touch {@code mods/} unless
     *         it can point at a human who said it could
     */
    private static boolean mirrorMods(Path gameDir) {
        try {
            // Re-read rather than plumbed out of Hopper.Result: this is a Fabric-only question
            // and the core's result stays free of it. Hopper.run has already created the file if
            // it was missing, so this is a read of a file that now certainly exists.
            return Config.load(gameDir).mirrorMods();
        } catch (Throwable t) {
            LOG.warn("[HOPPER] could not read config/hopper.properties; leaving the mods folder"
                    + " alone", t);
            return false;
        }
    }

    /**
     * What the player is told when the mirror is off. It has to be actionable, and
     * it must not pretend a download that cannot load is a mod that will.
     */
    private static void declineToTouchModsFolder(Hopper.Result result) {
        if (result.wanted == null) {
            LOG.warn("[HOPPER] the sync did not complete, so HOPPER cannot say what the server"
                    + " currently wants. See the error above.", null);
        } else {
            LOG.info("[HOPPER] " + result.count + " mod(s) are downloaded in " + result.dir + ".");
        }
        // Phrased around the directory rather than around "them", so it reads correctly in both
        // arms above - including the one where there is no list of mods to refer back to.
        LOG.warn("[HOPPER] nothing in " + result.dir + " will load. Fabric only reads mods/, and"
                + " HOPPER is not allowed to write there: " + Config.MIRROR_MODS + " is not set to"
                + " true in config/hopper.properties.", null);
        LOG.warn("[HOPPER] set " + Config.MIRROR_MODS + "=true there to let HOPPER copy its"
                + " downloads into mods/ and delete the ones it put there itself. It records what"
                + " it owns in " + Hopper.DIR + "/mods-mirror.txt and never touches a file that is"
                + " not on that list.", null);
    }

    // ---- the summary line ----

    /**
     * The one line the player is actually going to read, so it has to be true in
     * every arm.
     *
     * <p>The signal for "restart" is what happened to {@code mods/}, not what
     * happened to {@code hoppermods/}: Fabric only ever looked at {@code mods/}, so
     * that is the only directory whose change can require one.
     *
     * <p>But "did anything change" is not the same question as "did it work", and
     * this method used to answer only the first. {@code changed()} is
     * {@code copied > 0 || deleted > 0}, and a jar HOPPER refused to overwrite,
     * or could not overwrite, moves neither counter - so a launch that failed to
     * mirror anything at all fell into the else arm and announced that the mods
     * were already up to date, two lines after warning that they were not. The
     * same arm quoted {@code result.count}, which is 0 whenever the sync did not
     * complete, so a dead server produced "0 mod(s) already up to date" - a claim
     * about a manifest HOPPER never managed to read.
     *
     * <p>Hence four arms, in order of what the player most needs to know, and the
     * cheerful one is reachable only when the manifest was read AND every file it
     * named is where it belongs.
     */
    private static void report(Hopper.Result result, ModsFolderMirror mirror) {
        if (mirror.changed()) {
            // WARN rather than INFO on purpose: this survives a filtered log view, and a player
            // who scrolls past it and then wonders why their new mod is missing is a support
            // ticket that should never have existed.
            LOG.warn("[HOPPER] synced " + (mirror.copied() + mirror.deleted()) + " change(s): "
                    + mirror.copied() + " added, " + mirror.deleted() + " removed."
                    + (mirror.unresolved() == 0 ? "" : " " + mirror.unresolved()
                            + " other change(s) could NOT be made - see the warnings above.")
                    + " RESTART MINECRAFT to load them.", null);
            LOG.warn("[HOPPER] the mods you are about to play with are the ones from BEFORE this"
                    + " sync. Nothing downloaded just now is active in this session.", null);
            return;
        }

        if (mirror.unresolved() > 0) {
            LOG.warn("[HOPPER] the mods folder could NOT be brought in line: " + mirror.unresolved()
                    + " mod(s) could not be put in place or removed - see the warnings above."
                    + " Nothing changed this launch, so restarting on its own will not help.", null);
            return;
        }

        if (result.wanted == null) {
            // The sync did not reach the manifest, so there is no set to be up to date WITH.
            // Say what is actually known - what is in mods/ right now - and nothing more.
            LOG.warn("[HOPPER] the sync did not complete, so HOPPER cannot tell you whether your"
                    + " mods are up to date. The mods folder was left as it was, with "
                    + mirror.owned() + " mod(s) from the last successful sync. See the error"
                    + " above.", null);
            return;
        }

        LOG.info("[HOPPER] " + result.count + " mod(s) already up to date - no restart needed.");
    }

    /**
     * Who is playing, for the dashboard's client list. Fabric hands the whole
     * launch command line over, which beats parsing {@code sun.java.command} the
     * way the Forge adapters have to.
     *
     * @return null on a dedicated server, which has no player and is a fine thing
     *         to report
     */
    private static String username(FabricLoader fabric) {
        String[] args;
        try {
            // false: the sanitized form strips credentials, and on some game providers it strips
            // more than that. We want --username, which is not a credential.
            args = fabric.getLaunchArguments(false);
        } catch (RuntimeException e) {
            LOG.warn("[HOPPER] could not read the launch arguments; reporting no username", e);
            return null;
        }
        if (args == null) return null;

        for (int i = 0; i + 1 < args.length; i++) {
            if ("--username".equals(args[i]) && args[i + 1] != null && !args[i + 1].trim().isEmpty()) {
                return args[i + 1];
            }
        }
        return null;
    }
}
