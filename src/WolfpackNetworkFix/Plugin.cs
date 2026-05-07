using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace WolfpackNetworkFix;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    internal static new BepInEx.Logging.ManualLogSource Log;

    public const int NUM_ITERATIONS = 3;

    public override void Load()
    {
        Log = base.Log;
        var harmony = new Harmony("WolfpackNetworkFix");
        harmony.PatchAll();
        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_NAME} is loaded! (numIterations={NUM_ITERATIONS})");
    }
}

[HarmonyPatch(typeof(W_NetworkManager), "Start")]
class NetworkFixStart
{
    static void Postfix(W_NetworkManager __instance)
    {
        __instance.numIterations = Plugin.NUM_ITERATIONS;
        Plugin.Log.LogInfo($"numIterations set to {Plugin.NUM_ITERATIONS}");
    }
}

[HarmonyPatch(typeof(W_NetworkManager), "FixedUpdate")]
class NetworkFixUpdate
{
    static void Prefix(W_NetworkManager __instance)
    {
        __instance.numIterations = Plugin.NUM_ITERATIONS;
    }
}
