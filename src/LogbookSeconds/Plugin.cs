using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace LogbookSeconds;

// Pure client-side: every frame, scan each visible uboat's in-game logbook,
// upgrade lines from HH:MM to HH:MM:SS using the local W_ServerTime.
// Works whether or not the host has the mod, and is idempotent so two players
// running it simultaneously don't conflict (already-formatted lines are skipped).
[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    internal static new ManualLogSource Log;

    public override void Load()
    {
        Log = base.Log;
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
