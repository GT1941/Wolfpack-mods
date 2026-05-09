using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace LogbookSeconds;

// Two features in one mod:
//   1. Seconds upgrade: every frame, rewrite each visible uboat's in-game logbook
//      so HH:MM timestamps become HH:MM:SS. Pure client-side, idempotent.
//   2. Torpedo metadata: when a torpedo launches, append a sibling line to its
//      owning uboat's logbook with speed and detonator type. Host-gated to avoid
//      flicker on clients (host's append propagates via the logBook SyncVar).
//
// Same DLL works whether you host or join. No file swap needed.
[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    internal static new ManualLogSource Log;

    // FIFO queue of pending tube numbers per uboat. logLaunchedTorpedo Prefix
    // enqueues; Torpedo2.launch Postfix dequeues. Per-uboat keying handles
    // concurrent launches across boats in MP. Queue (not single int) handles
    // back-to-back launches (e.g. salvo) before postfixes resolve.
    internal static Dictionary<long, Queue<int>> PendingTubesByUboat = new();

    public override void Load()
    {
        Log = base.Log;
        new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll();
        AddComponent<LogbookWatcher>();
        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
    }

    internal static string GameTime()
    {
        try
        {
            float t = W_ServerTime.instance.azureTime.get() % 24f;
            int h = (int)t;
            float mf = (t - h) * 60f;
            int m = (int)mf;
            int s = (int)((mf - m) * 60f);
            return h.ToString("D2") + ":" + m.ToString("D2") + ":" + s.ToString("D2");
        }
        catch { return "??:??:??"; }
    }

    // True if this client is the multiplayer host (or singleplayer). Defaults to true
    // on error so a transient init hiccup doesn't suppress a real host's enrichments.
    internal static bool IsHost()
    {
        try { return W_NetworkManager.IsServer; }
        catch { return true; }
    }

    internal static string TubeName(int t)
    {
        switch (t)
        {
            case 1: return "I";
            case 2: return "II";
            case 3: return "III";
            case 4: return "IV";
            case 5: return "V";
            case 0: return "Salvo";
            default: return "?";
        }
    }
}

class LogbookWatcher : MonoBehaviour
{
    void Update()
    {
        try
        {
            // W_Uboat.allSubs is populated on host AND clients; W_GameManager.uboats can
            // be empty for non-host players. Fall back to mySub as a last resort.
            var subs = W_Uboat.allSubs;
            if (subs == null || subs.Count == 0)
            {
                var mine = W_Uboat.mySub;
                if (mine != null) ProcessUboat(mine);
                return;
            }
            for (int i = 0; i < subs.Count; i++) ProcessUboat(subs[i]);
        }
        catch { }
    }

    static void ProcessUboat(W_Uboat uboat)
    {
        if (uboat == null) return;
        string log = uboat.logBook ?? "";
        if (log.Length < 6) return;
        string[] lines = log.Split('\n');
        bool changed = false;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            // HH:MM format: pos 2=':', pos 5=' '. Already HH:MM:SS has ':' at 5.
            if (line.Length > 5 && line[2] == ':' && line[5] == ' ')
            {
                lines[i] = Plugin.GameTime() + line.Substring(5);
                changed = true;
            }
        }
        if (changed)
        {
            uboat.logBook = string.Join("\n", lines);
            try { uboat.updateLogbook(); } catch { }
        }
    }
}

// Capture tube number when game logs a torpedo launch, so we can correlate it
// with the Torpedo2.launch Postfix that fires immediately after.
[HarmonyPatch(typeof(W_Uboat), "logLaunchedTorpedo")]
class TubeCapturePatch
{
    [HarmonyPrefix]
    static void Pre(W_Uboat __instance, int whatTube)
    {
        try
        {
            long key = __instance.Pointer.ToInt64();
            if (!Plugin.PendingTubesByUboat.TryGetValue(key, out var q))
            {
                q = new Queue<int>();
                Plugin.PendingTubesByUboat[key] = q;
            }
            q.Enqueue(whatTube);
        }
        catch { }
    }
}

// Append a "metadata" line to the firing uboat's logBook with speed + detonator.
// Host-only: clients receive the enriched logBook via SyncVar. Avoids flicker.
[HarmonyPatch(typeof(Torpedo2), "launch")]
class LaunchEnrichPatch
{
    [HarmonyPostfix]
    static void Post(Torpedo2 __instance, TorpedoData __2)
    {
        try
        {
            if (!Plugin.IsHost()) return;

            var ownerUboat = FindOwnerUboat(__instance);
            if (ownerUboat == null) return;

            int tube = -1;
            try
            {
                long key = ownerUboat.Pointer.ToInt64();
                if (Plugin.PendingTubesByUboat.TryGetValue(key, out var q) && q.Count > 0)
                {
                    tube = q.Dequeue();
                }
            }
            catch { }

            float speed = 0f;
            try { speed = __2.Speed; } catch { }
            bool magnetic = false;
            try { magnetic = __instance.magneticTrigger != null && __instance.magneticTrigger.enabled; }
            catch { }

            string speedStr = speed > 0 ? speed.ToString("F0") + "kn" : "?kn";
            string trigger  = magnetic ? "magnetic" : "impact";
            string tubeStr  = tube > 0 ? "Tube " + Plugin.TubeName(tube) : "Tube ?";
            string newLine  = Plugin.GameTime() + "    " + tubeStr + " - " + speedStr + " " + trigger;

            string existing = ownerUboat.logBook ?? "";
            ownerUboat.logBook = existing.Length > 0 ? existing + "\n" + newLine : newLine;
            try { ownerUboat.updateLogbook(); } catch { }
        }
        catch (System.Exception ex)
        {
            try { Plugin.Log.LogWarning("[LogbookSeconds] launch postfix error: " + ex.Message); } catch { }
        }
    }

    // Resolve which W_Uboat owns this torpedo by matching gameObject pointers.
    static W_Uboat FindOwnerUboat(Torpedo2 t)
    {
        try
        {
            var owner = t.owner;
            if (owner == null) return null;
            long ownerPtr = owner.Pointer.ToInt64();
            var subs = W_Uboat.allSubs;
            if (subs != null)
            {
                for (int i = 0; i < subs.Count; i++)
                {
                    var sub = subs[i];
                    if (sub == null || sub.gameObject == null) continue;
                    if (sub.gameObject.Pointer.ToInt64() == ownerPtr) return sub;
                }
            }
        }
        catch { }
        return null;
    }
}
