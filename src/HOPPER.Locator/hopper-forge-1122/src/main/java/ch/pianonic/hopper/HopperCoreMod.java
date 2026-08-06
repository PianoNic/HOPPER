package ch.pianonic.hopper;

import java.io.File;
import java.util.Map;

import net.minecraftforge.fml.relauncher.IFMLLoadingPlugin;

/**
 * The Forge 1.12.x entry point. Legacy FML predates ModLauncher entirely, so
 * there is no locator here and no services file - LaunchWrapper finds this class
 * by reading the {@code FMLCorePlugin} attribute out of the jar's main manifest,
 * in {@code CoreModManager.discoverCoreMods}.
 *
 * <p>This class does nothing except name the class that does. All of the work
 * happens in {@link HopperCallHook}, because {@code getSetupClass()} is the slot
 * FML provides for exactly that: {@code FMLPluginWrapper.injectIntoClassLoader}
 * calls {@link #injectData} first, then instantiates the setup class as an
 * {@code IFMLCallHook} and runs it. Verified in the bytecode of
 * {@code forge-1.12.2-14.23.5.2864-universal.jar}:
 *
 * <pre>
 *   309: invokeinterface IFMLLoadingPlugin.injectData:(Ljava/util/Map;)V
 *   318: invokeinterface IFMLLoadingPlugin.getSetupClass:()Ljava/lang/String;
 *   334: invokestatic    Class.forName
 *   340: checkcast       class IFMLCallHook
 *   433: invokeinterface IFMLCallHook.injectData:(Ljava/util/Map;)V
 *   440: invokeinterface IFMLCallHook.call:()Ljava/lang/Object;
 * </pre>
 *
 * <p>There is deliberately no {@code @MCVersion} annotation, and that is not an
 * oversight. {@code CoreModManager.loadCoreMod} compares the annotation's value
 * against {@code FMLInjectionData.mccversion} and, on a mismatch, logs
 * "It will be ignored." and returns null - the coremod is dropped:
 *
 * <pre>
 *   163: invokevirtual   String.equals
 *   166: ifne            193
 *   172: ldc             "The coremod {} is requesting minecraft version {} and
 *                         minecraft is {}. It will be ignored."
 *   191: aconst_null
 *   192: areturn
 * </pre>
 *
 * Annotating {@code "1.12.2"} would therefore make this jar dead on 1.12 and
 * 1.12.1. Leaving it off costs one warning line per launch - "does not have a
 * MCVersion annotation, it may cause issues" - and that warning's premise does
 * not apply here: HOPPER touches no Minecraft class, no obfuscated name and no
 * version-specific API. One jar for all of 1.12.x is worth one log line.
 */
@IFMLLoadingPlugin.Name("HOPPER")
@IFMLLoadingPlugin.TransformerExclusions({ "ch.pianonic.hopper." })
public final class HopperCoreMod implements IFMLLoadingPlugin {

    /**
     * The game directory, handed to us under the key {@code "mcLocation"}.
     * {@link HopperCallHook} receives the same key in its own map and prefers
     * that; this is the fallback for the case where it somehow does not.
     */
    private static volatile File mcLocation;

    static File mcLocation() {
        return mcLocation;
    }

    /**
     * Nothing to transform. HOPPER moves files, it does not rewrite bytecode, and
     * the {@code @TransformerExclusions} above exists so that nobody else rewrites
     * ours either.
     *
     * <p>An empty array rather than null: {@code FMLPluginWrapper} calls this twice
     * in a row and iterates the result.
     */
    @Override
    public String[] getASMTransformerClass() {
        return new String[0];
    }

    /** Not an {@code @Mod}, so there is no container to build. */
    @Override
    public String getModContainerClass() {
        return null;
    }

    @Override
    public String getSetupClass() {
        return "ch.pianonic.hopper.HopperCallHook";
    }

    /**
     * Keys FML puts in this map: {@code mcLocation} (File), {@code coremodList}
     * (List), {@code runtimeDeobfuscationEnabled} (Boolean) and
     * {@code coremodLocation} (File). Only the first is of any use to us.
     */
    @Override
    public void injectData(Map<String, Object> data) {
        Object loc = data.get("mcLocation");
        if (loc instanceof File) {
            mcLocation = (File) loc;
        }
    }

    /** No access transformer - HOPPER never widens a Minecraft member. */
    @Override
    public String getAccessTransformerClass() {
        return null;
    }
}
