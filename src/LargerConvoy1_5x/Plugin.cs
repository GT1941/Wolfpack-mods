using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace LargerConvoy1_5x;

// Sibling of GT-LargerConvoy with a 1.5x multiplier — a gentle bump in convoy size
// without doubling. Install only ONE GT-LargerConvoy*.dll variant; loading multiple
// would stack their multipliers.
[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    internal static new BepInEx.Logging.ManualLogSource Log;

    public const float CONVOY_SIZE_MULTIPLIER = 1.5f;

    public override void Load()
    {
        Log = base.Log;
        var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        harmony.PatchAll();
        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_NAME} is loaded! (multiplier={CONVOY_SIZE_MULTIPLIER}x)");
    }
}

// Postfix on randomEncounter: scenario has populated the count fields, but
// spawnShips hasn't read them yet — perfect window to multiply.
[HarmonyPatch(typeof(ConvoySpawner), "randomEncounter")]
class WolfpackConvoyEnlargener1_5x
{
    static void Postfix(ConvoySpawner __instance)
    {
        float m = Plugin.CONVOY_SIZE_MULTIPLIER;

        int merchBase  = __instance.numMerchants;
        int armedBase  = __instance.numArmedMerchants;
        int carrBase   = __instance.numCarriers;
        int sloopBase  = __instance.numSloops;
        int corvBase   = __instance.numCorvettes;
        int destBase   = __instance.numDestroyers;
        int tonnesBase = __instance.merchantTonnes;

        __instance.numMerchants      = Scale(merchBase,  m);
        __instance.numArmedMerchants = Scale(armedBase,  m);
        __instance.numCarriers       = Scale(carrBase,   m);
        __instance.numSloops         = Scale(sloopBase,  m);
        __instance.numCorvettes      = Scale(corvBase,   m);
        __instance.numDestroyers     = Scale(destBase,   m);
        __instance.merchantTonnes    = Scale(tonnesBase, m);

        Plugin.Log.LogInfo(
            "[LargerConvoy1.5x] Scaled (base -> final): "
            + "merchants " + merchBase + "->" + __instance.numMerchants + ", "
            + "armed " + armedBase + "->" + __instance.numArmedMerchants + ", "
            + "carriers " + carrBase + "->" + __instance.numCarriers + ", "
            + "sloops " + sloopBase + "->" + __instance.numSloops + ", "
            + "corvettes " + corvBase + "->" + __instance.numCorvettes + ", "
            + "destroyers " + destBase + "->" + __instance.numDestroyers + ", "
            + "tonnage " + tonnesBase + "->" + __instance.merchantTonnes);
    }

    static int Scale(int value, float multiplier)
    {
        if (value <= 0) return value;
        return System.Math.Max(1, (int)(value * multiplier));
    }
}
