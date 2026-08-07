package ch.pianonic.hopper;

import net.fabricmc.api.EnvType;
import org.quiltmc.loader.api.LoaderValue;
import org.quiltmc.loader.api.plugin.QuiltLoaderPlugin;
import org.quiltmc.loader.api.minecraft.MinecraftQuiltLoader;
import org.quiltmc.loader.api.plugin.QuiltPluginContext;

import java.nio.file.Path;
import java.nio.file.Paths;
import java.util.Map;

public final class HopperQuiltPlugin implements QuiltLoaderPlugin {
    public HopperQuiltPlugin() {
    }

    private static final HopperLog LOG = HopperLog.STDOUT;

    @Override
    public void load(QuiltPluginContext context, Map<String, LoaderValue> previousData) {
        LOG.info("[HOPPER] this jar requires -Dloader.experimental.allow_loading_plugins=true."
                + " Without it Quilt refuses to parse it at all - use hopper-fabric-1.0.0.jar instead.");

        Path gameDir = gameDir(context);

        Hopper.Result result = Hopper.run(gameDir, LaunchArgs.username(), LOG, null, true, side());

        boolean isNew = context.addFolderToScan(result.dir);
        if (!isNew) {
            LOG.warn("[HOPPER] " + result.dir + " had already been added as a mod folder"
                    + " by something else", null);
        }

        LOG.info("[HOPPER] Quilt loader plugin active - " + result.count
                + " mod(s) handed to the loader in this launch, no restart needed.");
    }

    @Override
    public void unload(Map<String, LoaderValue> data) {
    }

    private static Side side() {
        return MinecraftQuiltLoader.getEnvironmentType() == EnvType.SERVER ? Side.SERVER : Side.CLIENT;
    }

    private static Path gameDir(QuiltPluginContext context) {
        try {
            Path p = context.manager().getGameDirectory();
            if (p != null) return p;
        } catch (RuntimeException e) {
            LOG.warn("[HOPPER] the plugin manager has no game directory yet;"
                    + " falling back to the working directory", e);
        }
        return Paths.get(".");
    }
}
