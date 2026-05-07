// Reconstructed from GT-LargerConvoy.dll (uploaded version)
// Confirmed via MemberRef analysis: scales numMerchants, numArmedMerchants,
// numCarriers, numSloops, numCorvettes, numDestroyers, merchantTonnes

using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace LargerConvoy;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    internal static new BepInEx.Logging.ManualLogSource Log;
    public const float CONVOY_SIZE_MULTIPLIER = 2f;

    public override void Load()
    {
        Log = base.Log;
        var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        harmony.PatchAll();
        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_NAME} is loaded! (multiplier={CONVOY_SIZE_MULTIPLIER}x)");
    }
}

[HarmonyPatch(typeof(ConvoySpawner), "spawnShips")]
class WolfpackConvoyEnlargener
{
    static void Postfix(ConvoySpawner __instance)
    {
        float m = Plugin.CONVOY_SIZE_MULTIPLIER;
        __instance.numMerchants      = Scale(__instance.numMerchants,      m);
        __instance.numArmedMerchants = Scale(__instance.numArmedMerchants, m);
        __instance.numCarriers       = Scale(__instance.numCarriers,       m);
        __instance.numSloops         = Scale(__instance.numSloops,         m);
        __instance.numCorvettes      = Scale(__instance.numCorvettes,      m);
        __instance.numDestroyers     = Scale(__instance.numDestroyers,     m);
        __instance.merchantTonnes    = Scale(__instance.merchantTonnes,    m);
    }

    static int Scale(int value, float multiplier)
    {
        if (value <= 0) return value;
        return System.Math.Max(1, (int)(value * multiplier));
    }
}
