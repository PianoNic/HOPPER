package ch.pianonic.hopper;

import java.io.File;
import java.util.Map;

import net.minecraftforge.fml.relauncher.IFMLLoadingPlugin;

@IFMLLoadingPlugin.Name("HOPPER")
@IFMLLoadingPlugin.TransformerExclusions({ "ch.pianonic.hopper." })
public final class HopperCoreMod implements IFMLLoadingPlugin {
    private static volatile File mcLocation;

    static File mcLocation() {
        return mcLocation;
    }

    @Override
    public String[] getASMTransformerClass() {
        return new String[0];
    }

    @Override
    public String getModContainerClass() {
        return null;
    }

    @Override
    public String getSetupClass() {
        return "ch.pianonic.hopper.HopperCallHook";
    }

    @Override
    public void injectData(Map<String, Object> data) {
        Object loc = data.get("mcLocation");
        if (loc instanceof File) {
            mcLocation = (File) loc;
        }
    }

    @Override
    public String getAccessTransformerClass() {
        return null;
    }
}
