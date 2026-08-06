package ch.pianonic.hopper;

import org.quiltmc.loader.api.LoaderValue;
import org.quiltmc.loader.api.plugin.QuiltLoaderPlugin;
import org.quiltmc.loader.api.plugin.QuiltPluginContext;

import java.nio.file.Path;
import java.nio.file.Paths;
import java.util.Map;

/**
 * The Quilt adapter, and the only one on a loader that has a real, documented,
 * public plugin API for exactly this.
 *
 * <p>{@code QuiltLoaderPlugin.load} is called before Quilt scans anything, and
 * {@link QuiltPluginContext#addFolderToScan(Path)} adds a directory that is then
 * "treated in the same way as the regular mods folder". That is the product
 * promise, met properly: download now, loaded this launch, no restart, and
 * without HOPPER reimplementing a single line of mod discovery.
 *
 * <h2>The catch, and why a second jar exists</h2>
 *
 * A mod-provided loader plugin is gated behind a system property.
 * {@code V1ModMetadataImpl} throws a {@code ParseException} - a hard failure at
 * metadata parse time, not a degradation - the moment it sees the
 * {@code experimental_quilt_loader_plugin} key while
 * {@code -Dloader.experimental.allow_loading_plugins=true} is unset. The same
 * gate is present in 0.26.0 and in 0.30.1-beta.2, so it is not a passing state of
 * one release.
 *
 * <p>That is the same category of blocker HOPPER already refuses for Fabric: a
 * JVM argument is a launcher setting. So Quilt ships two jars and this is not
 * optional:
 * <ul>
 * <li>{@code hopper-fabric-1.0.0.jar} is the default. Quilt runs Fabric mods
 * through {@code StandardFabricPlugin} and a {@code preLaunch} entrypoint works
 * on Quilt unchanged - degraded, restart required.</li>
 * <li>{@code hopper-quilt-plugin-1.0.0.jar}, this one, is the opt-in upgrade for
 * a player willing to add one JVM argument. Real same-launch loading.</li>
 * </ul>
 * The first log line below names the flag, so if the flag is ever removed the
 * resulting parse failure explains itself to whoever reads the log.
 *
 * <h2>Two things this class must not do</h2>
 *
 * It must not log through log4j. This runs inside {@code QuiltPluginClassLoader},
 * which owns only the packages {@code quilt.mod.json} lists and whose parent
 * carries Quilt's <em>shaded</em> logging rather than
 * {@code org.apache.logging.log4j} - so it logs through {@link HopperLog#STDOUT},
 * which is the reason the core has {@link HopperLog} at all.
 *
 * <p>And it must not call {@code QuiltLoader} directly. The interface javadoc is
 * explicit: "plugins must never call QuiltLoader directly - that's designed
 * solely for mods to use after mod loading is complete." Everything comes from
 * the {@link QuiltPluginContext} instead.
 */
public final class HopperQuiltPlugin implements QuiltLoaderPlugin {

    /**
     * {@code QuiltPluginContextImpl} does {@code loadClassDirectly(...)} then
     * {@code getDeclaredConstructor().newInstance()}, so this has to be public and
     * take no arguments. Written out rather than left implicit precisely because
     * "the compiler gives you one for free" stops being true the moment someone
     * adds a field that wants initializing.
     */
    public HopperQuiltPlugin() {
    }

    /**
     * {@code System.out}, not log4j - see the class javadoc. It reaches the console
     * in every launcher, which is more than can be said for any logger this early
     * inside a plugin classloader.
     */
    private static final HopperLog LOG = HopperLog.STDOUT;

    @Override
    public void load(QuiltPluginContext context, Map<String, LoaderValue> previousData) {
        // First line, every launch. If the flag is ever dropped, Quilt fails at parse time and
        // this line is the last thing HOPPER ever got to say - so it has to be the thing that
        // explains the failure.
        LOG.info("[HOPPER] this jar requires -Dloader.experimental.allow_loading_plugins=true."
                + " Without it Quilt refuses to parse it at all - use hopper-fabric-1.0.0.jar instead.");

        Path gameDir = gameDir(context);

        // Never throws. Offline, server down, bad manifest - none of that stops the game from
        // starting; the directory handed over below simply still holds the previous download.
        //
        // No progress sink: Quilt's loading gui is driven from QuiltPluginContext.reportError and
        // the tree nodes, neither of which is a line-of-text channel.
        Hopper.Result result = Hopper.run(gameDir, username(), LOG, null);

        // The whole adapter, in one call. Quilt walks the folder exactly as it walks mods/ - the
        // quilt.mod.json check, the jar-in-jar handling, the fabric-mod fallback through
        // StandardFabricPlugin - all of it, on files that were downloaded moments ago.
        //
        // addFolderToScan(Path), not addFileToScan: that one has two overloads, PluginGuiTreeNode
        // and QuiltTreeNode, and there is no gui node to hand it at this point.
        boolean isNew = context.addFolderToScan(result.dir);
        if (!isNew) {
            // Something already claimed this directory. Not fatal - the files still get scanned -
            // but worth knowing about, because it means two things are managing hoppermods/.
            LOG.warn("[HOPPER] " + result.dir + " had already been added as a mod folder"
                    + " by something else", null);
        }

        LOG.info("[HOPPER] Quilt loader plugin active - " + result.count
                + " mod(s) handed to the loader in this launch, no restart needed.");
    }

    /**
     * Nothing to hand to a future version of this plugin. The state that matters is
     * on disk in {@code hoppermods/}, where a reload finds it anyway, and a half-written
     * sync is better re-run than resumed from a map.
     */
    @Override
    public void unload(Map<String, LoaderValue> data) {
        // Deliberately empty - see javadoc.
    }

    /**
     * The game directory, from the plugin manager rather than from
     * {@code QuiltLoader.getGameDir()}, which plugins are told never to touch.
     */
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

    /**
     * Who is playing, for the dashboard's client list. There is no launch-argument
     * accessor on {@link QuiltPluginContext}, and {@code QuiltLoader} is off limits
     * to a plugin, so the command line the JVM itself was given is the only source
     * left. {@code null} when it is not there - a dedicated server has no player,
     * and that is a fine thing to report.
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
}
