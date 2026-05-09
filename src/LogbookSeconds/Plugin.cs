using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace LogbookSeconds;

// Two features in one mod:
//   1. Seconds upgrade: every frame, rewrite each visible uboat's in-game logbook
//      so HH:MM timestamps become HH:MM:SS. Pure client-side, idempotent.
//   2. Torpedo metadata: when a torpedo launches, append speed + detonator type
//      to the just-written launch line (in-place, so it stays on one line).
//      Host-gated to avoid SyncVar flicker on clients.
//
// Same DLL works whether you host or join. No file swap needed.
[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    internal static new ManualLogSource Log;

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

// Append " (SPEEDkn TRIGGER)" directly to the launch line the game just wrote.
// Host-only: clients receive the enriched logBook via SyncVar.
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

            float speed = 0f;
            try { speed = __2.Speed; } catch { }
            bool magnetic = false;
            try { magnetic = __instance.magneticTrigger != null && __instance.magneticTrigger.enabled; }
            catch { }

            string speedStr = speed > 0 ? speed.ToString("F0") + "kn" : "?kn";
            string trigger  = magnetic ? "magnetic" : "impact";
            string suffix   = " (" + speedStr + " " + trigger + ")";

            string existing = ownerUboat.logBook ?? "";
            if (existing.Length == 0) return;

            // Strip trailing newlines so we land on the actual content of the last line.
            int trimEnd = existing.Length;
            while (trimEnd > 0 && (existing[trimEnd - 1] == '\n' || existing[trimEnd - 1] == '\r')) trimEnd--;
            if (trimEnd == 0) return;
            string trailing = existing.Substring(trimEnd);
            string content  = existing.Substring(0, trimEnd);
            int lastLineStart = content.LastIndexOf('\n') + 1;
            string lastLine = content.Substring(lastLineStart);

            // Sanity: this should be a torpedo launch line the game just wrote.
            // If we can't confirm it, skip rather than corrupt an arbitrary line.
            string upper = lastLine.ToUpperInvariant();
            if (!upper.Contains("TORPEDO") && !upper.Contains("TUBE")) return;

            // Idempotency: don't append twice.
            if (lastLine.Contains("kn ") && (lastLine.Contains("magnetic") || lastLine.Contains("impact"))) return;
            if (lastLine.Contains("KN ") && (upper.Contains("MAGNETIC") || upper.Contains("IMPACT"))) return;

            ownerUboat.logBook = content + suffix + trailing;
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
