package ch.pianonic.hopper;

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

/**
 * Downloads the required mod set before FML walks {@code mods/}, then hands the
 * downloaded jars to FML as ordinary mod candidates in the same launch.
 *
 * <p>Registered through
 * {@code META-INF/services/net.neoforged.neoforgespi.locating.IModFileCandidateLocator}.
 * NeoForge picks that file up at both ends of the supported range, by two
 * completely different mechanisms that happen to want the same file:
 * <ul>
 * <li>21.1.x - {@code ModDirTransformerDiscoverer} walks {@code mods/}, reads
 * each jar's module descriptor, and lifts any jar whose {@code provides} names
 * one of {@code TransformerDiscovererConstants.SERVICES} onto the ModLauncher
 * SERVICE layer, which is built before {@code ModDiscoverer} runs.</li>
 * <li>26.x - ModLauncher is gone; {@code EarlyServiceDiscovery} does a literal
 * {@code getEntry("META-INF/services/" + serviceClass)} on every jar in
 * {@code mods/} and appends the hits to a plain {@code URLClassLoader}.</li>
 * </ul>
 * Either way: no restart, no launcher argument, and the locator jar is never
 * offered back to the discovery pipeline as a mod.
 *
 * <p>Nothing is ever written into {@code mods/}. Downloads live in
 * {@code hoppermods/}, a directory HOPPER owns outright, so there are no open file
 * handles to fight and a player's own mods are never touched.
 *
 * <h2>The cross-version contract</h2>
 *
 * One adapter covers NeoForge 21.1.x through 26.2.x. That only holds because
 * this class touches an extremely small slice of the SPI, and every member of
 * that slice is byte-identical between {@code fancymodloader:loader} 4.0.24 and
 * 11.0.16:
 * <ul>
 * <li>{@link IModFileCandidateLocator#forFolder(java.io.File, String)}</li>
 * <li>{@link IModFileCandidateLocator#findCandidates(ILaunchContext, IDiscoveryPipeline)}</li>
 * <li>{@link net.neoforged.neoforgespi.locating.IOrderedProvider#getPriority()}</li>
 * <li>{@code FMLPaths.GAMEDIR.get()}</li>
 * </ul>
 *
 * <p>What it must never touch, because all of it broke between those two
 * versions: {@code IModFile}, {@code JarContents},
 * {@code IDiscoveryPipeline.addJarContent}, {@code readModFile}, and
 * {@code ILaunchContext.environment()} / {@code modLists()} / {@code mods()} /
 * {@code mavenRoots()}.
 *
 * <p>It also never constructs a {@code net.neoforged.fml.ModLoadingIssue}. That
 * type does resolve in both versions, but an issue of severity ERROR aborts the
 * launch, and HOPPER's rule is that a failed sync never does - the player gets
 * the previous download and a loud log line instead of a crash screen.
 *
 * <p>Everything that is not NeoForge-specific - the download, the hash check,
 * the stale sweep, the config merge - lives in {@link Hopper} in the core,
 * shared with five other adapters. What is left here is the NeoForge shape of
 * it.
 */
public final class HopperNeoLocator implements IModFileCandidateLocator {

    private static final Logger LOG4J = LogManager.getLogger("HOPPER");

    /**
     * The core refuses to name a logger - a Quilt loader plugin cannot see log4j -
     * so every adapter supplies its own. This is NeoForge's, and log4j-api really
     * is there: {@code fancymodloader:loader} declares a compile-scope dependency
     * on it at both ends of the range.
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

    /**
     * {@code ServiceLoaderUtil.loadServices} sorts providers by
     * {@code getPriority()} <em>reversed</em> - "a higher value means the provider
     * will be called earlier" - so this has to sit above
     * {@code HIGHEST_SYSTEM_PRIORITY} for {@code hoppermods/} to be populated before
     * the built-in {@code ModsFolderLocator} walks {@code mods/}.
     *
     * <p>The margin is 100 rather than 1 so a future built-in locator that wants
     * to be first among NeoForge's own does not silently overtake us.
     */
    @Override
    public int getPriority() {
        return HIGHEST_SYSTEM_PRIORITY + 100;
    }

    @Override
    public void findCandidates(ILaunchContext context, IDiscoveryPipeline pipeline) {
        // Never throws. Offline, server down, bad manifest - none of that stops the game from
        // starting; result.wanted is simply null and we hand over the previous download instead.
        //
        // No progress sink: NeoForge's early window is driven by ImmediateWindowProvider, which is
        // a different service entirely and not something a locator gets a handle on.
        Hopper.Result result = Hopper.run(gameDir(), username(), LOG, null);

        warnAboutSurvivors(result);

        // forFolder(File, String) is NeoForge's own "treat this directory the way you treat mods/"
        // helper, and using it is the point: the .jar filter, the sorting, the non-regular-file
        // reporting and the addPath call all stay NeoForge's business rather than being
        // reimplemented here against types that changed between 4.0.24 and 11.0.16.
        //
        // Building it HERE, after the sync, is what makes it safe. Registering forFolder(...)
        // directly as the service would have it scan hoppermods/ on NeoForge's schedule rather than
        // ours, which on a first launch is an empty directory.
        IModFileCandidateLocator.forFolder(result.dir.toFile(), Hopper.DIR)
                .findCandidates(context, pipeline);
    }

    /**
     * Belt and braces, and honest about being only that.
     *
     * <p>{@code Hopper.run} already deleted everything the manifest did not ask
     * for, but a delete can lose to antivirus or a read-only file. The Forge
     * adapters can filter such a survivor back out because they see the scan
     * result; here the delegate goes straight to the pipeline, so the only thing
     * left to do is name the file. A silent extra mod is far worse than a noisy
     * one.
     */
    private static void warnAboutSurvivors(Hopper.Result result) {
        if (result.wanted == null) return; // sync did not complete: everything on disk is wanted

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

    /**
     * The game directory, taken from FML rather than from {@link ILaunchContext}.
     *
     * <p>{@code ILaunchContext.environment()} would be the obvious route on
     * 21.1.x and it is exactly the method that was deleted in 11.0.16.
     * {@code gameDirectory()} is the 26.x replacement and does not exist on
     * 21.1.x. {@code FMLPaths.GAMEDIR} is the one handle that is spelled the same
     * in both, and it is populated by {@code loadAbsolutePaths} long before any
     * locator runs.
     */
    private static Path gameDir() {
        try {
            Path p = FMLPaths.GAMEDIR.get();
            if (p != null) return p;
        } catch (RuntimeException e) {
            LOG.warn("[HOPPER] FMLPaths.GAMEDIR is not set yet; falling back to the working directory", e);
        }
        return Paths.get(".");
    }

    /**
     * Who is playing, for the dashboard's client list. Minecraft is launched with
     * {@code --username <name>}; NeoForge gives a locator no argument map at all,
     * so the command line the JVM itself was given is the only source. {@code null}
     * when it is not there - a dedicated server has no player, and that is a fine
     * thing to report.
     */
    private static String username() {
        String[] launch = System.getProperty("sun.java.command", "").split(" ");
        for (int i = 0; i + 1 < launch.length; i++) {
            if ("--username".equals(launch[i]) && !launch[i + 1].trim().isEmpty()) {
                return launch[i + 1];
            }
        }
        return null;
    }

    /** Shows up in NeoForge's discovery log next to {@code {mods folder locator at ...}}. */
    @Override
    public String toString() {
        return "{HOPPER locator}";
    }
}
