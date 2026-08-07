package ch.pianonic.hopper;

import net.neoforged.api.distmarker.Dist;
import net.neoforged.fml.loading.FMLEnvironment;
import net.neoforged.fml.loading.FMLPaths;
import net.neoforged.neoforgespi.ILaunchContext;
import net.neoforged.neoforgespi.locating.IDiscoveryPipeline;
import net.neoforged.neoforgespi.locating.IModFileCandidateLocator;
import org.apache.logging.log4j.LogManager;
import org.apache.logging.log4j.Logger;

import java.io.IOException;
import java.nio.file.DirectoryStream;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

public final class HopperNeoLocator implements IModFileCandidateLocator {
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
    public int getPriority() {
        return HIGHEST_SYSTEM_PRIORITY + 100;
    }

    @Override
    public void findCandidates(ILaunchContext context, IDiscoveryPipeline pipeline) {
        Hopper.Result result = Hopper.run(gameDir(), LaunchArgs.username(), LOG, null, true, side());

        warnAboutSurvivors(result);

        IModFileCandidateLocator.forFolder(result.dir.toFile(), Hopper.DIR)
                .findCandidates(context, pipeline);
    }

    private static void warnAboutSurvivors(Hopper.Result result) {
        if (result.wanted == null) return;

        List<String> survivors = new ArrayList<>();
        try (DirectoryStream<Path> listing = Files.newDirectoryStream(result.dir)) {
            for (Path p : listing) {
                String name = p.getFileName().toString();
                if (!name.toLowerCase(Locale.ROOT).endsWith(".jar")) continue;
                if (!result.wanted.contains(name)) survivors.add(name);
            }
        } catch (IOException e) {
            LOG.warn("[HOPPER] could not re-check " + result.dir + " after the sync", e);
            return;
        }

        for (String name : survivors) {
            LOG.warn("[HOPPER] " + name + " is no longer in the manifest but could not be deleted"
                    + " from " + result.dir + "; it WILL be loaded this launch."
                    + " Remove it by hand if that is not what you want.", null);
        }
    }

    private static Side side() {
        return FMLEnvironment.dist == Dist.DEDICATED_SERVER ? Side.SERVER : Side.CLIENT;
    }

    private static Path gameDir() {
        try {
            Path p = FMLPaths.GAMEDIR.get();
            if (p != null) return p;
        } catch (RuntimeException e) {
            LOG.warn("[HOPPER] FMLPaths.GAMEDIR is not set yet; falling back to the working directory", e);
        }
        return Paths.get(".");
    }


    @Override
    public String toString() {
        return "{HOPPER locator}";
    }
}
