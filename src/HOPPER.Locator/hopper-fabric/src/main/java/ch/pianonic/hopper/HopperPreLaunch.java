package ch.pianonic.hopper;

import net.fabricmc.api.EnvType;
import net.fabricmc.loader.api.FabricLoader;
import net.fabricmc.loader.api.entrypoint.PreLaunchEntrypoint;
import org.apache.logging.log4j.LogManager;
import org.apache.logging.log4j.Logger;

import java.nio.file.Path;

public final class HopperPreLaunch implements PreLaunchEntrypoint {
    private static final Logger LOG4J = LogManager.getLogger("HOPPER");

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
        LOG.warn("[HOPPER] Fabric exposes no pre-discovery hook. This entrypoint runs after mod"
                + " discovery has finished and the loader is frozen, so nothing synced now can load"
                + " until you restart.", null);

        FabricLoader fabric = FabricLoader.getInstance();
        Path gameDir = fabric.getGameDir();

        boolean mirrorMods = mirrorMods(gameDir);

        Hopper.Result result = Hopper.run(gameDir, username(fabric), LOG, null, mirrorMods, side(fabric));

        if (!mirrorMods) {
            declineToTouchModsFolder(result);
            return;
        }

        ModsFolderMirror mirror = new ModsFolderMirror(gameDir.resolve("mods"), result.dir, LOG);
        try {
            mirror.reconcile(result.wanted);
        } catch (Throwable t) {
            LOG.error("[HOPPER] could not update the mods folder; the game will start with the"
                    + " mods it already has", t);
            return;
        }

        report(result, mirror);
    }

    private static boolean mirrorMods(Path gameDir) {
        try {
            return Config.load(gameDir).mirrorMods();
        } catch (Throwable t) {
            LOG.warn("[HOPPER] could not read config/hopper.properties; leaving the mods folder"
                    + " alone", t);
            return false;
        }
    }

    private static void declineToTouchModsFolder(Hopper.Result result) {
        if (result.wanted == null) {
            LOG.warn("[HOPPER] the sync did not complete, so HOPPER cannot say what the server"
                    + " currently wants. See the error above.", null);
        } else {
            LOG.info("[HOPPER] " + result.count + " mod(s) are downloaded in " + result.dir + ".");
        }

        LOG.warn("[HOPPER] nothing in " + result.dir + " will load. Fabric only reads mods/, and"
                + " HOPPER is not allowed to write there: " + Config.MIRROR_MODS + " is not set to"
                + " true in config/hopper.properties.", null);
        LOG.warn("[HOPPER] set " + Config.MIRROR_MODS + "=true there to let HOPPER copy its"
                + " downloads into mods/ and delete the ones it put there itself. It records what"
                + " it owns in " + Hopper.DIR + "/" + Syncer.MIRROR_LIST + " and never touches a"
                + " file that is not on that list.", null);
    }

    private static void report(Hopper.Result result, ModsFolderMirror mirror) {
        if (mirror.changed()) {
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
            LOG.warn("[HOPPER] the sync did not complete, so HOPPER cannot tell you whether your"
                    + " mods are up to date. The mods folder was left as it was, with "
                    + mirror.owned() + " mod(s) from the last successful sync. See the error"
                    + " above.", null);
            return;
        }

        LOG.info("[HOPPER] " + result.count + " mod(s) already up to date - no restart needed.");
    }

    private static Side side(FabricLoader fabric) {
        return fabric.getEnvironmentType() == EnvType.SERVER ? Side.SERVER : Side.CLIENT;
    }

    private static String username(FabricLoader fabric) {
        String[] args;
        try {
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
