using System;
using System.IO;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;
using Wolfpack.Game.Statistics;

namespace LogbookExport;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    internal static new ManualLogSource Log;
    internal static List<string> Entries = new();
    internal static bool MissionActive = false;
    internal static string CurrentLogPath = "";
    internal static string VesselName = "U-???";
    internal static int TorpedoesFired = 0;
    internal static int TorpedoesHit = 0;
    internal static int TorpedoesFailed = 0;
    internal static float TonnageSunk = 0f;
    internal static bool WasDetected = false;
    internal static string DetectionType = "";
    internal static float LastConvoySpeed = -1f;
    internal static bool IsConvoyFleeing = false;

    static readonly string[] VesselNames = { "U-96", "U-552", "U-564", "U-307" };

    public override void Load()
    {
        Plugin.Log = base.Log;
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
            return $"{h:D2}:{m:D2}:{s:D2}";
        }
        catch { return "??:??:??"; }
    }

    internal static float GetConvoyNormalSpeed()
    {
        try
        {
            var convoy = UnityEngine.Object.FindObjectOfType<AI_Convoy>();
            if (convoy != null) return convoy.convoyNormalSpeed;
        }
        catch { }
        return -1f;
    }

    internal static string GetIngameDate()
    {
        try { return W_GameManager.instance?.lobbyData?.CurrentDate.ToString("dd.MM.yyyy") ?? "Unknown"; }
        catch { return "Unknown"; }
    }

    internal static void StartMission(string source)
    {
        if (MissionActive) return;
        Entries.Clear();
        VesselName = "U-???";
        TorpedoesFired = TorpedoesHit = TorpedoesFailed = 0;
        TonnageSunk = 0f;
        WasDetected = false; DetectionType = "";
        LastConvoySpeed = -1f;
        IsConvoyFleeing = false;
        CurrentLogPath = Path.Combine(Paths.BepInExRootPath,
            "PatrolLog_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".txt");
        WriteHeader();
        MissionActive = true;
        Log.LogInfo("[LogbookExport] Mission started (" + source + ")");
    }

    internal static void WriteHeader()
    {
        float spd = GetConvoyNormalSpeed();
        string spdLine = spd > 0 ? $"Convoy speed: {spd:F2} kn\r\n" : "";
        string h = $"=== PATROL LOG - {VesselName} ===\r\n" +
                   $"Date:     {GetIngameDate()}\r\n" +
                   spdLine +
                   $"Logged:   {DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n\r\n";
        File.WriteAllText(CurrentLogPath, h, System.Text.Encoding.UTF8);
        if (spd > 0) LastConvoySpeed = spd;
    }

    internal static void OnUboatSpawned(W_Uboat uboat)
    {
        if (uboat == null) return;
        try
        {
            string name = "";
            try { name = uboat.GetUboatName() ?? ""; } catch { }
            if (string.IsNullOrEmpty(name))
            {
                int n = uboat.crewNumber.get();
                name = (n >= 0 && n < VesselNames.Length) ? VesselNames[n] : ("U-Crew" + n);
            }
            if (name == VesselName) return;
            // Boat switched — close old log, open new one
            if (MissionActive && Entries.Count > 0)
            {
                Log.LogInfo("[LogbookExport] Boat switch: " + VesselName + " → " + name);
                WriteSummary();
            }
            VesselName = name;
            Entries.Clear();
            TorpedoesFired = TorpedoesHit = TorpedoesFailed = 0;
            TonnageSunk = 0f;
            WasDetected = false; DetectionType = "";
            IsConvoyFleeing = false;
            CurrentLogPath = Path.Combine(Paths.BepInExRootPath,
                "PatrolLog_" + VesselName + "_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".txt");
            MissionActive = true;
            WriteHeader();
            Log.LogInfo("[LogbookExport] Log: " + CurrentLogPath);
        }
        catch (Exception ex) { Log.LogWarning("[LogbookExport] OnUboatSpawned: " + ex.Message); }
    }

    internal static void Add(string line)
    {
        if (!MissionActive) return;
        string entry = $"{GameTime()} {line}";
        Entries.Add(entry);
        Log.LogInfo("[LogbookExport] " + entry);
        try { File.AppendAllText(CurrentLogPath, entry + "\r\n", System.Text.Encoding.UTF8); }
        catch (Exception ex) { Log.LogError("[LogbookExport] Write failed: " + ex.Message); }
    }

    internal static void WriteSummary()
    {
        if (!MissionActive || Entries.Count == 0) return;
        int misses = TorpedoesFired - TorpedoesHit - TorpedoesFailed;
        string summary =
            "\r\n--- SUMMARY ---\r\n" +
            $"Torpedoes fired:  {TorpedoesFired}\r\n" +
            $"Hits:             {TorpedoesHit}\r\n" +
            $"Misses:           {misses}\r\n" +
            $"Premature/failed: {TorpedoesFailed}\r\n" +
            $"Tonnage sunk:     {TonnageSunk:F0} t\r\n" +
            $"Detected:         {(WasDetected ? "YES (" + DetectionType + ")" : "No")}\r\n";
        try { File.AppendAllText(CurrentLogPath, summary, System.Text.Encoding.UTF8); }
        catch { }
        Log.LogInfo("[LogbookExport] Summary written.");
    }

    internal static void Export()
    {
        WriteSummary();
        MissionActive = false;
        Entries.Clear();
    }
}

// Watches all active uboats every frame and enhances HH:MM → HH:MM:SS
class LogbookWatcher : MonoBehaviour
{
    int _speedCheckTimer = 0;
    void Update()
    {
        // Monitor convoy speed changes (every ~30 frames to avoid spam)
        _speedCheckTimer++;
        if (_speedCheckTimer >= 30)
        {
            _speedCheckTimer = 0;
            if (Plugin.MissionActive)
            {
                float spd = Plugin.GetConvoyNormalSpeed();
                if (spd > 0 && Plugin.LastConvoySpeed > 0 && Math.Abs(spd - Plugin.LastConvoySpeed) > 0.1f)
                {
                    Plugin.Add($"CONVOY SPEED CHANGED: {Plugin.LastConvoySpeed:F2} → {spd:F2} kn");
                    Plugin.LastConvoySpeed = spd;
                }
                else if (spd > 0 && Plugin.LastConvoySpeed < 0)
                {
                    Plugin.LastConvoySpeed = spd;
                }
            }
        }
        try
        {
            var gm = W_GameManager.instance;
            if (gm?.uboats == null) return;
            foreach (var uboat in gm.uboats)
            {
                if (uboat == null) continue;
                string log = uboat.logBook ?? "";
                if (log.Length < 6) continue;
                string[] lines = log.Split('\n');
                bool changed = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    // Match HH:MM format: pos 2=':', pos 5=' ' (not already HH:MM:SS)
                    if (line.Length > 5 && line[2] == ':' && line[5] == ' ')
                    {
                        lines[i] = Plugin.GameTime() + line.Substring(5);
                        changed = true;
                    }
                }
                if (changed)
                {
                    uboat.logBook = string.Join("\n", lines);
                    uboat.updateLogbook();
                }
            }
        }
        catch { }
    }
}

[HarmonyPatch]
class LogbookPatcher
{
    [HarmonyPatch(typeof(ConvoySpawner), "spawnShips")]
    [HarmonyPostfix] static void Reset1() => Plugin.StartMission("ConvoySpawner");
    [HarmonyPatch(typeof(Convoy),        "spawnShips")]
    [HarmonyPostfix] static void Reset2() => Plugin.StartMission("Convoy");
    [HarmonyPatch(typeof(W_LevelLoader), "spawnShips")]
    [HarmonyPostfix] static void Reset3() => Plugin.StartMission("W_LevelLoader");

    [HarmonyPatch(typeof(W_GameManager), "spawnUboat")]
    [HarmonyPostfix]
    static void OnSpawnUboat(W_GameManager __instance)
    {
        try
        {
            int myCrew = __instance.getMyCrew();
            var uboats = __instance.uboats;
            if (uboats == null || myCrew < 0 || myCrew >= uboats.Length) return;
            if (uboats[myCrew] != null) Plugin.OnUboatSpawned(uboats[myCrew]);
        }
        catch { }
    }

    // ── Helpers ───────────────────────────────────────────────

    static W_Uboat GetMyUboat()
    {
        try
        {
            var gm = W_GameManager.instance;
            if (gm == null) return null;
            int myCrew = gm.getMyCrew();
            var uboats = gm.uboats;
            if (uboats == null || myCrew < 0 || myCrew >= uboats.Length) return null;
            return uboats[myCrew];
        }
        catch { return null; }
    }

    static bool IsMyTorpedo(Torpedo2 t)
    {
        try
        {
            var myUboat = GetMyUboat();
            if (myUboat == null) return true;
            Plugin.OnUboatSpawned(myUboat);
            var owner = t.owner;
            if (owner == null) return false;
            return owner.Pointer == myUboat.gameObject.Pointer;
        }
        catch { return true; }
    }

    static bool IsOurBoat(GameObject go)
    {
        if (go == null) return false;
        try
        {
            var myUboat = GetMyUboat();
            if (myUboat == null) return true;
            return go.Pointer == myUboat.gameObject.Pointer;
        }
        catch { return true; }
    }

    // ── Torpedo events ────────────────────────────────────────

    [HarmonyPatch(typeof(Torpedo2), "launch")]
    [HarmonyPostfix]
    static void Post_Launch(Torpedo2 __instance)
    {
        if (!IsMyTorpedo(__instance)) return;
        Plugin.TorpedoesFired++;
        Plugin.Add("LAUNCHED TORPEDO");
    }

    [HarmonyPatch(typeof(Torpedo2), "explode")]
    [HarmonyPostfix]
    static void Post_Explode(Torpedo2 __instance)
    {
        if (!IsMyTorpedo(__instance)) return;
        Plugin.TorpedoesHit++;
        Plugin.Add("TORPEDO HIT");
    }

    [HarmonyPatch(typeof(Torpedo2), "torpedoFail")]
    [HarmonyPostfix]
    static void Post_Fail(Torpedo2 __instance)
    {
        if (!IsMyTorpedo(__instance)) return;
        Plugin.TorpedoesFailed++;
        Plugin.Add("TORPEDO FAILED");
    }

    // ── Ship sunk (all clients log all sinkings) ──────────────

    [HarmonyPatch(typeof(W_Cargoship), "die")]
    [HarmonyPostfix]
    static void Post_CargoshipDie(W_Cargoship __instance)
    {
        try
        {
            string typeName = "";
            float displacement = 0f;
            var stats = __instance.GetComponent<MerchantShipStats>();
            if (stats != null) { typeName = stats.NameOfShipType ?? ""; displacement = stats.Displacement; }
            if (string.IsNullOrEmpty(typeName))
                typeName = __instance.gameObject?.name?.Replace("(Clone)", "").Trim() ?? "unknown";
            Plugin.TonnageSunk += displacement;
            Plugin.Add(displacement > 0 ? $"SUNK {typeName} ({displacement:F0} t)" : $"SUNK {typeName}");
        }
        catch { Plugin.Add("SUNK unknown"); }
    }

    // ── Uboat death ───────────────────────────────────────────

    [HarmonyPatch(typeof(W_Uboat), "die")]
    [HarmonyPostfix]
    static void Post_UboatDie(W_Uboat __instance)
    {
        var myUboat = GetMyUboat();
        if (myUboat == null || myUboat.Pointer != __instance.Pointer) return;
        Plugin.Add("U-BOAT LOST");
        Plugin.WriteSummary();
    }

    // ── Detection (filtered to our boat) ─────────────────────

    [HarmonyPatch(typeof(AI_Convoy), "detectedUboat")]
    [HarmonyPostfix]
    static void OnDetectedVisual(GameObject __0)
    {
        if (!IsOurBoat(__0)) return;
        if (Plugin.WasDetected) return;
        Plugin.WasDetected = true; Plugin.DetectionType = "visual";
        Plugin.Add("DETECTED (visual)");
    }

    [HarmonyPatch(typeof(AI_Convoy), "detectedUboatHydrophone")]
    [HarmonyPostfix]
    static void OnDetectedHydro(GameObject __0)
    {
        if (!IsOurBoat(__0)) return;
        if (Plugin.WasDetected) return;
        Plugin.WasDetected = true; Plugin.DetectionType = "hydrophone";
        Plugin.Add("DETECTED (hydrophone)");
    }

    [HarmonyPatch(typeof(AI_Convoy), "startFleeing")]
    [HarmonyPostfix]
    static void OnStartFleeing()
    {
        if (Plugin.IsConvoyFleeing) return;
        Plugin.IsConvoyFleeing = true;
        if (!Plugin.WasDetected)
        {
            Plugin.WasDetected = true;
            Plugin.DetectionType = "unknown";
        }
        Plugin.Add("CONVOY FLEEING");
    }

    [HarmonyPatch(typeof(AI_Convoy), "stopFleeing")]
    [HarmonyPostfix]
    static void OnStopFleeing()
    {
        if (!Plugin.IsConvoyFleeing) return;
        Plugin.IsConvoyFleeing = false;
        Plugin.Add("CONVOY RESUMED COURSE");
    }

    // ── Export triggers ───────────────────────────────────────

    [HarmonyPatch(typeof(EndGameStats), "reset")]
    [HarmonyPrefix]  static void OnReset()       => Plugin.Export();
    [HarmonyPatch(typeof(EndGameStats), "writeToFile")]
    [HarmonyPostfix] static void OnWrite()        => Plugin.Export();
    [HarmonyPatch(typeof(W_LevelLoader), "unloadLevel")]
    [HarmonyPrefix]  static void OnUnload()
    {
        Plugin.Log.LogInfo("[LogbookExport] unloadLevel");
        Plugin.Export();
    }
}
