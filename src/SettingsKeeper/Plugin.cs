using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace SettingsKeeper;

// Persists Wolfpack's per-user game settings across Steam branch
// switches (current ↔ beta ↔ legacy). Steam can place each branch's
// build in a way that makes the Unity engine point PlayerPrefs and/or
// the persistent-data path at a slightly different location, which is
// what causes the "all my options reset" behaviour the user reported
// (background colour slider in the Recognition Manual, plus everything
// else in Game / Audio / Video / Water / Mouse options panels).
//
// Strategy: on every launch, snapshot the live PlayerPrefs registry
// key and the contents of Application.persistentDataPath to a stable
// location under BepInEx\config\SettingsKeeper\. On the NEXT launch,
// if either source is missing values that the snapshot has, restore
// them — but never delete or overwrite anything the user has currently
// set. Worst case is one extra setting comes back from a stale snapshot;
// best case is the entire options screen survives a branch switch.
//
// Layout under BepInEx\config\SettingsKeeper\:
//   registry-backup.json     — snapshot of HKCU\Software\<co>\<product>
//   persistent-backup\       — verbatim copy of Application.persistentDataPath
//   last-snapshot.txt        — UTC timestamp + summary, for diagnostics
//
// All operations are READ + ADD only. We never call PlayerPrefs.DeleteAll
// and never delete files in persistentDataPath. The mod is meant to be
// safe even if installed with no prior backup (it just snapshots on the
// first run and starts restoring from the second run onward).
//
// Registry access goes through direct P/Invoke to advapi32.dll. We
// could have referenced Microsoft.Win32.Registry, but that package is
// not bundled with the BepInEx IL2CPP runtime — using the Win32 API
// avoids the missing-assembly problem and keeps the mod a single DLL.

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    internal static new ManualLogSource Log;

    // BepInEx\config\SettingsKeeper\... — survives branch switches because
    // BepInEx itself lives next to the game install but the user typically
    // copies their BepInEx folder across versions.
    static string _backupDir;
    static string _registryBackupPath;
    static string _persistentBackupDir;
    static string _lastSnapshotPath;

    // Session-only settings that Wolfpack itself never persists.
    // Right now this is just RecognitionManual.BackgroundColorMultiplier
    // (the slider in the in-game manual's top bar) but the format is a
    // JSON object so we can add more without a schema migration.
    static string _sessionOnlyBackupPath;

    // Loaded once at plugin load — value to apply when
    // RecognitionManual.instance becomes non-null (which is later than
    // plugin load, since the manual is a UI element that only exists
    // once the player is in a mission). _appliedBgColor flips true the
    // first time we successfully apply so we don't keep overwriting
    // the user's mid-session adjustments.
    internal static float? _pendingBgColorMul;
    internal static bool   _appliedBgColor;

    // Escape hatch. v0.3.0 restored the value via SnapshotWatcher.Update
    // as soon as RecognitionManual.instance went non-null, but the user
    // reported the slider stopped responding to drags after that.
    // Hypothesis: calling SetBackgroundColor before the UI tree (the
    // slider + its onValueChanged binding) is fully wired up leaves
    // the slider event handler pointed at a stale set of dimmable
    // images. We now apply later, via a Harmony postfix on
    // RecognitionManual.OnEnable. If that ALSO breaks something,
    // the user can flip RestoreBackgroundColor=false to disable.
    internal static ConfigEntry<bool> RestoreBackgroundColor;

    public override void Load()
    {
        Log = base.Log;

        _backupDir              = Path.Combine(Paths.ConfigPath, MyPluginInfo.PLUGIN_NAME);
        _registryBackupPath     = Path.Combine(_backupDir, "registry-backup.json");
        _persistentBackupDir    = Path.Combine(_backupDir, "persistent-backup");
        _lastSnapshotPath       = Path.Combine(_backupDir, "last-snapshot.txt");
        _sessionOnlyBackupPath  = Path.Combine(_backupDir, "session-only-settings.json");

        try { Directory.CreateDirectory(_backupDir); }
        catch (Exception ex) { Log.LogError("[SettingsKeeper] cannot create backup dir: " + ex.Message); return; }

        RestoreBackgroundColor = Config.Bind("SessionOnly", "RestoreBackgroundColor", true,
            "Restore the in-manual 'Background color' slider value across launches. " +
            "Wolfpack itself doesn't persist this setting. If enabling this breaks " +
            "the slider's responsiveness in-game, set to false.");

        new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll();

        // RESTORE first, then SNAPSHOT. If the user just launched a
        // freshly-switched branch and the settings look empty, we
        // restore from the snapshot left by the previous branch
        // BEFORE overwriting the snapshot with the empty live state.
        try { RestoreFromBackup(); }
        catch (Exception ex) { Log.LogWarning("[SettingsKeeper] restore pass error: " + ex.Message); }

        // Load the session-only settings cache. The actual apply
        // happens later, once RecognitionManual.instance exists —
        // see SnapshotWatcher.TryApplySessionOnly.
        _pendingBgColorMul = LoadSessionOnlyBgColor();
        if (_pendingBgColorMul.HasValue)
            Log.LogInfo("[SettingsKeeper] session-only restore queued: " +
                        "BackgroundColorMultiplier=" + _pendingBgColorMul.Value.ToString("0.000", CultureInfo.InvariantCulture));

        try { SnapshotToBackup(reason: "load"); }
        catch (Exception ex) { Log.LogWarning("[SettingsKeeper] snapshot pass error: " + ex.Message); }

        // Periodic snapshot watcher — catches settings the user tweaked
        // mid-session before quitting via Alt-F4 / Steam force-quit /
        // branch-switch while the game is still running.
        AddComponent<SnapshotWatcher>();

        Log.LogInfo("[SettingsKeeper] " + MyPluginInfo.PLUGIN_VERSION + " loaded — backup at " + _backupDir);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Registry path resolution
    // ──────────────────────────────────────────────────────────────────

    // Unity 2017+ standalone PlayerPrefs on Windows live at:
    //   HKCU\Software\<companyName>\<productName>
    // where companyName / productName come from Player Settings at
    // build time. Application.companyName / productName return them
    // at runtime.
    static string GetRegistryKeyPath()
    {
        string co = null, prod = null;
        try { co   = Application.companyName; } catch { }
        try { prod = Application.productName; } catch { }
        if (string.IsNullOrEmpty(co) || string.IsNullOrEmpty(prod)) return null;
        return "Software\\" + co + "\\" + prod;
    }

    // ──────────────────────────────────────────────────────────────────
    //  Snapshot  (live state → backup)
    // ──────────────────────────────────────────────────────────────────

    internal static void SnapshotToBackup(string reason)
    {
        // Flush in-memory state to disk BEFORE we read it.
        //
        // Why this matters: Wolfpack's user settings (volumes, map BG
        // colour, line colours, recognition-manual brightness, etc.)
        // live on a single in-memory object W_Options.instance. The
        // settings *file* on disk (settings.json under persistentDataPath)
        // is only rewritten when something calls W_Options.Save() —
        // typically a "back out of options menu" or a clean shutdown.
        // If we snapshot before that happens, we capture whatever was
        // last persisted, which can be days stale.
        //
        // Calling Save() ourselves is idempotent and quick (~one JSON
        // serialize + file write). PlayerPrefs.Save() flushes the
        // Unity-built-in prefs (resolution / display mode) from the
        // process-local cache to HKCU.
        ForceFlushToDisk();

        int regCount = SnapshotRegistry();
        int fileCount = SnapshotPersistentData();
        try
        {
            File.WriteAllText(_lastSnapshotPath,
                "Snapshot @ " + DateTime.UtcNow.ToString("u") + " (reason: " + reason + ")" +
                "\nRegistry values: " + regCount +
                "\nPersistent files: " + fileCount + "\n");
        }
        catch { }
    }

    // Force every code path that holds settings in memory to write its
    // current state to disk / registry. Best-effort — each call is
    // wrapped because the call sites can throw if the type isn't
    // resolved yet (W_Options is null in the main menu before the game
    // loads its profile).
    //
    // v0.2.1 diagnostic: log W_Options' opinion of mapBackgroundColor +
    // mapForegroundColor before Save(). If the in-memory value matches
    // what's on disk after Save() but doesn't match what the user
    // expects, the in-game slider isn't writing to W_Options at all
    // (preview state held elsewhere). If the in-memory value matches
    // the user's expectation but disk doesn't, Save() isn't actually
    // persisting these fields.
    internal static void ForceFlushToDisk()
    {
        try
        {
            // Try lowercase `instance` first (older convention), fall
            // back to uppercase `Instance` (current). Both should
            // resolve to the same object in practice but we don't
            // know that for certain.
            W_Options opts = null;
            try { opts = W_Options.instance; } catch { }
            if (opts == null)
            {
                try { opts = W_Options.Instance; } catch { }
                if (opts != null) Log.LogInfo("[SettingsKeeper] W_Options.instance was null; using W_Options.Instance");
            }

            if (opts == null)
            {
                Log.LogInfo("[SettingsKeeper] W_Options is null — skipping Save (not yet initialised)");
            }
            else
            {
                // Log the current in-memory values so we can compare
                // against settings.json after the snapshot completes.
                float bgR = 0, bgG = 0, bgB = 0, fgR = 0, fgG = 0, fgB = 0;
                try { bgR = opts.mapBackgroundColorR; bgG = opts.mapBackgroundColorG; bgB = opts.mapBackgroundColorB; } catch { }
                try { fgR = opts.mapForegroundColorR; fgG = opts.mapForegroundColorG; fgB = opts.mapForegroundColorB; } catch { }
                Log.LogInfo("[SettingsKeeper] W_Options in-memory: " +
                            "mapBG=(" + bgR.ToString("0.000") + "," + bgG.ToString("0.000") + "," + bgB.ToString("0.000") + ") " +
                            "mapFG=(" + fgR.ToString("0.000") + "," + fgG.ToString("0.000") + "," + fgB.ToString("0.000") + ")");
                opts.Save();
            }
        }
        catch (Exception ex)
        {
            Log.LogWarning("[SettingsKeeper] W_Options.Save() failed: " + ex.Message);
        }
        try { PlayerPrefs.Save(); }
        catch (Exception ex) { Log.LogWarning("[SettingsKeeper] PlayerPrefs.Save() failed: " + ex.Message); }

        // Session-only settings — values Wolfpack itself doesn't
        // persist. Snapshot them to our own JSON file so we can
        // restore on the next launch. Right now only the in-manual
        // "Background color" slider belongs here.
        try
        {
            float bgMul = RecognitionManual.BackgroundColorMultiplier;
            SaveSessionOnlyBgColor(bgMul);
            Log.LogInfo("[SettingsKeeper] session-only saved: BackgroundColorMultiplier=" +
                        bgMul.ToString("0.000", CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            Log.LogWarning("[SettingsKeeper] BackgroundColorMultiplier read failed: " + ex.Message);
        }
    }

    // ──────────────────────────────────────────────────────────────────
    //  Session-only settings persistence
    // ──────────────────────────────────────────────────────────────────
    //
    // Tiny hand-rolled JSON because the rest of this file already
    // hand-rolls JSON for the registry backup, and pulling in a JSON
    // library would balloon the assembly footprint. Keep the format
    // tolerant of extra fields so we can add more session-only
    // values later without breaking older readers.

    static void SaveSessionOnlyBgColor(float value)
    {
        try
        {
            var content = "{\n  \"backgroundColorMultiplier\": " +
                          value.ToString("0.######", CultureInfo.InvariantCulture) +
                          "\n}\n";
            File.WriteAllText(_sessionOnlyBackupPath, content);
        }
        catch (Exception ex)
        {
            Log.LogWarning("[SettingsKeeper] session-only write failed: " + ex.Message);
        }
    }

    static float? LoadSessionOnlyBgColor()
    {
        try
        {
            if (!File.Exists(_sessionOnlyBackupPath)) return null;
            var txt = File.ReadAllText(_sessionOnlyBackupPath);
            int key = txt.IndexOf("\"backgroundColorMultiplier\"", StringComparison.Ordinal);
            if (key < 0) return null;
            int colon = txt.IndexOf(':', key);
            if (colon < 0) return null;

            // Scan forward past whitespace, then collect the numeric
            // chars. Bounded by 64 chars to avoid runaway on a
            // malformed file.
            int i = colon + 1;
            while (i < txt.Length && (txt[i] == ' ' || txt[i] == '\t' || txt[i] == '\r' || txt[i] == '\n')) i++;
            int start = i;
            while (i < txt.Length && i - start < 64
                   && ((txt[i] >= '0' && txt[i] <= '9') || txt[i] == '.' || txt[i] == '-' || txt[i] == '+' || txt[i] == 'e' || txt[i] == 'E')) i++;
            if (i == start) return null;
            var numStr = txt.Substring(start, i - start);
            if (float.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return v;
        }
        catch (Exception ex)
        {
            Log.LogWarning("[SettingsKeeper] session-only read failed: " + ex.Message);
        }
        return null;
    }

    static int SnapshotRegistry()
    {
        var subPath = GetRegistryKeyPath();
        if (subPath == null)
        {
            Log.LogInfo("[SettingsKeeper] no company/product yet — registry snapshot skipped");
            return 0;
        }
        var values = Win32Reg.EnumerateValues(Win32Reg.HKEY_CURRENT_USER, subPath, out var err);
        if (values == null)
        {
            if (err == Win32Reg.ERROR_FILE_NOT_FOUND)
                Log.LogInfo("[SettingsKeeper] HKCU\\" + subPath + " missing — registry snapshot skipped");
            else
                Log.LogWarning("[SettingsKeeper] registry enum failed (err=" + err + "): " + subPath);
            return 0;
        }
        var sb = new StringBuilder();
        sb.Append("{\n  \"key\": \"HKCU\\\\").Append(subPath.Replace("\\", "\\\\")).Append("\",\n");
        sb.Append("  \"capturedAtUtc\": \"").Append(DateTime.UtcNow.ToString("u")).Append("\",\n");
        sb.Append("  \"values\": {\n");
        for (int i = 0; i < values.Count; i++)
        {
            var v = values[i];
            if (i > 0) sb.Append(",\n");
            sb.Append("    ").Append(JsonString(v.Name)).Append(": { \"kind\": ").Append(v.KindCode).Append(", \"value\": ");
            AppendValueJson(sb, v);
            sb.Append(" }");
        }
        sb.Append("\n  }\n}\n");
        try
        {
            File.WriteAllText(_registryBackupPath, sb.ToString());
            Log.LogInfo("[SettingsKeeper] snapshot: registry HKCU\\" + subPath + "  " + values.Count + " values");
            return values.Count;
        }
        catch (Exception ex)
        {
            Log.LogWarning("[SettingsKeeper] registry snapshot write failed: " + ex.Message);
            return 0;
        }
    }

    static int SnapshotPersistentData()
    {
        string src = null;
        try { src = Application.persistentDataPath; } catch { }
        if (string.IsNullOrEmpty(src) || !Directory.Exists(src))
        {
            Log.LogInfo("[SettingsKeeper] persistentDataPath unavailable — file snapshot skipped");
            return 0;
        }
        try
        {
            Directory.CreateDirectory(_persistentBackupDir);
            int copied = CopyDirectoryDelta(src, _persistentBackupDir, snapshotIsSource: true);
            Log.LogInfo("[SettingsKeeper] snapshot: " + src + " → " + _persistentBackupDir + "  " + copied + " files");
            return copied;
        }
        catch (Exception ex)
        {
            Log.LogWarning("[SettingsKeeper] file snapshot failed: " + ex.Message);
            return 0;
        }
    }

    // ──────────────────────────────────────────────────────────────────
    //  Restore  (backup → live state, only if live is missing entries)
    // ──────────────────────────────────────────────────────────────────

    static void RestoreFromBackup()
    {
        RestoreRegistry();
        RestorePersistentData();
    }

    static void RestoreRegistry()
    {
        if (!File.Exists(_registryBackupPath))
        {
            Log.LogInfo("[SettingsKeeper] no registry backup to restore from");
            return;
        }
        var subPath = GetRegistryKeyPath();
        if (subPath == null) { Log.LogInfo("[SettingsKeeper] no company/product yet — registry restore skipped"); return; }

        string txt;
        try { txt = File.ReadAllText(_registryBackupPath); }
        catch (Exception ex) { Log.LogWarning("[SettingsKeeper] cannot read registry backup: " + ex.Message); return; }

        var entries = ParseRegistryBackup(txt);
        if (entries == null || entries.Count == 0)
        {
            Log.LogInfo("[SettingsKeeper] registry backup empty / unparseable — nothing to restore");
            return;
        }

        // Collect live value names so the restore is add-only.
        var live = new HashSet<string>(StringComparer.Ordinal);
        var liveValues = Win32Reg.EnumerateValues(Win32Reg.HKEY_CURRENT_USER, subPath, out _);
        if (liveValues != null)
            foreach (var v in liveValues) live.Add(v.Name);

        int restored = 0, skipped = 0;
        foreach (var e in entries)
        {
            if (live.Contains(e.Name)) { skipped++; continue; }
            if (Win32Reg.SetValue(Win32Reg.HKEY_CURRENT_USER, subPath, e.Name, e.KindCode, e.RawBytes))
                restored++;
            else
                Log.LogWarning("[SettingsKeeper] failed to restore " + e.Name);
        }
        Log.LogInfo("[SettingsKeeper] restore: registry HKCU\\" + subPath + "  +" + restored + " values (skipped " + skipped + " already-present)");
    }

    static void RestorePersistentData()
    {
        if (!Directory.Exists(_persistentBackupDir))
        {
            Log.LogInfo("[SettingsKeeper] no persistent backup to restore from");
            return;
        }
        string dst = null;
        try { dst = Application.persistentDataPath; } catch { }
        if (string.IsNullOrEmpty(dst))
        {
            Log.LogInfo("[SettingsKeeper] persistentDataPath unavailable — file restore skipped");
            return;
        }
        try { Directory.CreateDirectory(dst); }
        catch (Exception ex) { Log.LogWarning("[SettingsKeeper] cannot create persistentDataPath: " + ex.Message); return; }

        int copied = CopyDirectoryDelta(_persistentBackupDir, dst, snapshotIsSource: false);
        Log.LogInfo("[SettingsKeeper] restore: " + _persistentBackupDir + " → " + dst + "  +" + copied + " files (existing untouched)");
    }

    // ──────────────────────────────────────────────────────────────────
    //  Filesystem helper
    // ──────────────────────────────────────────────────────────────────

    // Copies every file under srcRoot into dstRoot recursively.
    //
    // snapshotIsSource=true:  snapshot pass — always overwrite the
    //   backup with the latest live state.
    // snapshotIsSource=false: restore pass — copy ONLY when the
    //   destination file doesn't already exist. Never overwrite the
    //   live state with a stale backup.
    static int CopyDirectoryDelta(string srcRoot, string dstRoot, bool snapshotIsSource)
    {
        int copied = 0;
        try
        {
            var srcInfo = new DirectoryInfo(srcRoot);
            if (!srcInfo.Exists) return 0;
            foreach (var f in srcInfo.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                var rel = f.FullName.Substring(srcRoot.Length).TrimStart('\\', '/');
                var dst = Path.Combine(dstRoot, rel);
                try { Directory.CreateDirectory(Path.GetDirectoryName(dst)); } catch { }
                if (snapshotIsSource)
                {
                    try { File.Copy(f.FullName, dst, overwrite: true); copied++; }
                    catch (Exception ex) { Log.LogWarning("[SettingsKeeper] copy " + rel + " failed: " + ex.Message); }
                }
                else
                {
                    if (!File.Exists(dst))
                    {
                        try { File.Copy(f.FullName, dst, overwrite: false); copied++; }
                        catch (Exception ex) { Log.LogWarning("[SettingsKeeper] restore " + rel + " failed: " + ex.Message); }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.LogWarning("[SettingsKeeper] dir walk failed: " + ex.Message);
        }
        return copied;
    }

    // ──────────────────────────────────────────────────────────────────
    //  JSON encode / parse
    // ──────────────────────────────────────────────────────────────────

    static string JsonString(string s)
    {
        if (s == null) return "null";
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            switch (c)
            {
                case '"':  sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n");  break;
                case '\r': sb.Append("\\r");  break;
                case '\t': sb.Append("\\t");  break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    static void AppendValueJson(StringBuilder sb, Win32Reg.RegValue v)
    {
        switch (v.KindCode)
        {
            case Win32Reg.REG_DWORD:
                sb.Append(BitConverter.ToInt32(v.RawBytes, 0));
                break;
            case Win32Reg.REG_QWORD:
                sb.Append(BitConverter.ToInt64(v.RawBytes, 0));
                break;
            case Win32Reg.REG_SZ:
            case Win32Reg.REG_EXPAND_SZ:
                sb.Append(JsonString(v.AsString()));
                break;
            case Win32Reg.REG_BINARY:
                sb.Append(JsonString(Convert.ToBase64String(v.RawBytes)));
                break;
            case Win32Reg.REG_MULTI_SZ:
                {
                    var parts = v.AsMultiString();
                    sb.Append('[');
                    for (int i = 0; i < parts.Length; i++) { if (i > 0) sb.Append(','); sb.Append(JsonString(parts[i])); }
                    sb.Append(']');
                    break;
                }
            default:
                // Unknown kind: store as base64.
                sb.Append(JsonString(Convert.ToBase64String(v.RawBytes ?? new byte[0])));
                break;
        }
    }

    // Hand-rolled parser for the registry-backup JSON we wrote ourselves.
    // Walks the "values" object collecting { name, kind, value } triples.
    static List<Win32Reg.RegValue> ParseRegistryBackup(string txt)
    {
        var list = new List<Win32Reg.RegValue>();
        int vi = txt.IndexOf("\"values\"", StringComparison.Ordinal);
        if (vi < 0) return list;
        int braceStart = txt.IndexOf('{', vi);
        if (braceStart < 0) return list;
        int depth = 1, i = braceStart + 1;
        while (i < txt.Length && depth > 0)
        {
            char c = txt[i];
            if (c == '{') depth++;
            else if (c == '}') { depth--; if (depth == 0) break; }
            else if (c == '"')
            {
                int nameEnd;
                string name = ReadJsonString(txt, i, out nameEnd);
                if (name == null) { i++; continue; }
                i = nameEnd;
                while (i < txt.Length && (txt[i] == ':' || txt[i] == ' ' || txt[i] == '\t' || txt[i] == '\r' || txt[i] == '\n')) i++;
                if (i >= txt.Length || txt[i] != '{') continue;
                int innerDepth = 1; i++;
                int kindCode = -1; byte[] raw = null; string strValue = null; string[] multiStr = null;
                while (i < txt.Length && innerDepth > 0)
                {
                    char ic = txt[i];
                    if (ic == '{') innerDepth++;
                    else if (ic == '}') { innerDepth--; if (innerDepth == 0) { i++; break; } }
                    else if (ic == '"')
                    {
                        int fnEnd;
                        string field = ReadJsonString(txt, i, out fnEnd);
                        i = fnEnd;
                        while (i < txt.Length && (txt[i] == ':' || txt[i] == ' ' || txt[i] == '\t' || txt[i] == '\r' || txt[i] == '\n')) i++;
                        if (field == "kind")
                        {
                            int vEnd;
                            var k = ReadJsonValue(txt, i, out vEnd);
                            i = vEnd;
                            if (k is long lk) kindCode = (int)lk;
                            else if (k is int ik) kindCode = ik;
                        }
                        else if (field == "value")
                        {
                            int vEnd;
                            var val = ReadJsonValue(txt, i, out vEnd);
                            i = vEnd;
                            if (val is string s) strValue = s;
                            else if (val is long ll) raw = BitConverter.GetBytes(ll);
                            else if (val is double dd) raw = BitConverter.GetBytes((long)dd);
                            else if (val is List<object> arr)
                            {
                                multiStr = new string[arr.Count];
                                for (int k = 0; k < arr.Count; k++) multiStr[k] = arr[k] as string ?? "";
                            }
                        }
                        else { i++; }
                    }
                    else i++;
                }
                if (kindCode > 0)
                {
                    var entry = new Win32Reg.RegValue { Name = name, KindCode = kindCode };
                    if (kindCode == Win32Reg.REG_DWORD)
                    {
                        if (raw != null && raw.Length >= 4) entry.RawBytes = raw;
                        else if (strValue != null && int.TryParse(strValue, out var iv)) entry.RawBytes = BitConverter.GetBytes(iv);
                    }
                    else if (kindCode == Win32Reg.REG_QWORD)
                    {
                        if (raw != null && raw.Length >= 8) entry.RawBytes = raw;
                    }
                    else if (kindCode == Win32Reg.REG_SZ || kindCode == Win32Reg.REG_EXPAND_SZ)
                    {
                        entry.RawBytes = Win32Reg.EncodeUtf16Z(strValue ?? "");
                    }
                    else if (kindCode == Win32Reg.REG_BINARY)
                    {
                        try { entry.RawBytes = Convert.FromBase64String(strValue ?? ""); }
                        catch { entry.RawBytes = new byte[0]; }
                    }
                    else if (kindCode == Win32Reg.REG_MULTI_SZ)
                    {
                        entry.RawBytes = Win32Reg.EncodeMultiUtf16Z(multiStr ?? new string[0]);
                    }
                    else
                    {
                        try { entry.RawBytes = Convert.FromBase64String(strValue ?? ""); }
                        catch { entry.RawBytes = new byte[0]; }
                    }
                    if (entry.RawBytes != null) list.Add(entry);
                }
                continue;
            }
            i++;
        }
        return list;
    }

    static string ReadJsonString(string txt, int i, out int next)
    {
        next = i;
        if (i >= txt.Length || txt[i] != '"') return null;
        var sb = new StringBuilder();
        i++;
        while (i < txt.Length)
        {
            char c = txt[i];
            if (c == '\\' && i + 1 < txt.Length)
            {
                char n = txt[i + 1];
                if (n == '"') sb.Append('"');
                else if (n == '\\') sb.Append('\\');
                else if (n == '/') sb.Append('/');
                else if (n == 'n') sb.Append('\n');
                else if (n == 'r') sb.Append('\r');
                else if (n == 't') sb.Append('\t');
                else if (n == 'u' && i + 5 < txt.Length)
                {
                    int code = Convert.ToInt32(txt.Substring(i + 2, 4), 16);
                    sb.Append((char)code);
                    i += 4;
                }
                else sb.Append(n);
                i += 2;
            }
            else if (c == '"') { next = i + 1; return sb.ToString(); }
            else { sb.Append(c); i++; }
        }
        return null;
    }

    static object ReadJsonValue(string txt, int i, out int next)
    {
        while (i < txt.Length && (txt[i] == ' ' || txt[i] == '\t' || txt[i] == '\r' || txt[i] == '\n')) i++;
        if (i >= txt.Length) { next = i; return null; }
        char c = txt[i];
        if (c == '"') return ReadJsonString(txt, i, out next);
        if (c == '[')
        {
            // Array of strings (for REG_MULTI_SZ)
            var list = new List<object>();
            i++;
            while (i < txt.Length)
            {
                while (i < txt.Length && (txt[i] == ',' || txt[i] == ' ' || txt[i] == '\t' || txt[i] == '\r' || txt[i] == '\n')) i++;
                if (i < txt.Length && txt[i] == ']') { next = i + 1; return list; }
                int vEnd;
                var val = ReadJsonValue(txt, i, out vEnd);
                if (vEnd == i) { i++; continue; }
                list.Add(val);
                i = vEnd;
            }
            next = i;
            return list;
        }
        if (c == '-' || (c >= '0' && c <= '9'))
        {
            int start = i;
            while (i < txt.Length && ((txt[i] >= '0' && txt[i] <= '9') || txt[i] == '-' || txt[i] == '+' || txt[i] == '.' || txt[i] == 'e' || txt[i] == 'E')) i++;
            next = i;
            string numStr = txt.Substring(start, i - start);
            if (long.TryParse(numStr, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var lv)) return lv;
            if (double.TryParse(numStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dv)) return dv;
            return numStr;
        }
        if (c == 't' && i + 4 <= txt.Length && txt.Substring(i, 4) == "true")  { next = i + 4; return true; }
        if (c == 'f' && i + 5 <= txt.Length && txt.Substring(i, 5) == "false") { next = i + 5; return false; }
        if (c == 'n' && i + 4 <= txt.Length && txt.Substring(i, 4) == "null")  { next = i + 4; return null; }
        next = i + 1;
        return null;
    }
}

// Background snapshotter — three triggers:
//
//   - Periodic (every SnapshotIntervalSeconds) catches mid-session
//     setting changes for the case where the user crashes or
//     force-quits before any other path fires.
//
//   - OnApplicationFocus(false) catches Alt-Tab away. The user
//     might change settings, alt-tab out, never come back, and
//     close the game from the taskbar — we want a snapshot for
//     that.
//
//   - OnApplicationQuit fires when the user uses the in-game Quit
//     menu (and on a clean Alt-F4 / window-close). This is the
//     most important trigger for "I changed BG colour, quit, came
//     back next day" — combined with the explicit W_Options.Save()
//     call in SnapshotToBackup, the latest in-memory state lands
//     on disk first, then in the backup.
//
// AddComponent<T>() from BasePlugin handles the IL2CPP registration.
public class SnapshotWatcher : MonoBehaviour
{
    const float SnapshotIntervalSeconds = 30f;
    float _accum;

    void Update()
    {
        _accum += Time.unscaledDeltaTime;
        if (_accum < SnapshotIntervalSeconds) return;
        _accum = 0f;
        try { Plugin.SnapshotToBackup(reason: "watcher"); }
        catch { /* swallow — periodic snapshot is best-effort */ }
    }

    void OnApplicationFocus(bool focused)
    {
        if (focused) return;
        try { Plugin.SnapshotToBackup(reason: "focus-loss"); }
        catch { }
    }

    void OnApplicationQuit()
    {
        try { Plugin.SnapshotToBackup(reason: "quit"); }
        catch { }
    }
}

// Apply the persisted in-manual "Background color" slider value AFTER
// RecognitionManual finishes initialising. Doing this earlier — at
// plugin Load() or as soon as RecognitionManual.instance went
// non-null — disconnected the slider's response in v0.3.0, plausibly
// because SetBackgroundColor was iterating over a dimmableImages
// array that hadn't been populated yet.
//
// OnEnable runs every time the manual becomes visible. We apply only
// once per session (gated by _appliedBgColor) so that mid-session
// adjustments the user makes aren't overwritten if they close and
// re-open the manual.
[HarmonyPatch(typeof(RecognitionManual), "OnEnable")]
class RecognitionManualOnEnablePatch
{
    [HarmonyPostfix]
    static void Post(RecognitionManual __instance)
    {
        try
        {
            if (Plugin._appliedBgColor) return;
            if (!Plugin._pendingBgColorMul.HasValue) return;
            if (Plugin.RestoreBackgroundColor != null && !Plugin.RestoreBackgroundColor.Value) return;
            if (__instance == null) return;

            float v = Plugin._pendingBgColorMul.Value;
            __instance.SetBackgroundColor(v);
            Plugin._appliedBgColor = true;
            Plugin.Log.LogInfo("[SettingsKeeper] applied BackgroundColorMultiplier = " +
                               v.ToString("0.000", CultureInfo.InvariantCulture) +
                               " (via OnEnable postfix)");
        }
        catch (Exception ex)
        {
            try { Plugin.Log.LogWarning("[SettingsKeeper] OnEnable apply failed: " + ex.Message); } catch { }
        }
    }
}

// Direct Win32 registry access. We could have used Microsoft.Win32 from
// .NET, but that assembly isn't bundled with BepInEx IL2CPP, so we go
// straight to advapi32.dll which is always present on Windows. Just
// enough surface for our enumerate-all + write-individual workflow.
internal static class Win32Reg
{
    internal const int HKEY_CURRENT_USER = unchecked((int)0x80000001);

    internal const int KEY_READ        = 0x20019;
    internal const int KEY_WRITE       = 0x20006;
    internal const int KEY_QUERY_VALUE = 0x00001;

    internal const int REG_NONE      = 0;
    internal const int REG_SZ        = 1;
    internal const int REG_EXPAND_SZ = 2;
    internal const int REG_BINARY    = 3;
    internal const int REG_DWORD     = 4;
    internal const int REG_MULTI_SZ  = 7;
    internal const int REG_QWORD     = 11;

    internal const int ERROR_SUCCESS         = 0;
    internal const int ERROR_FILE_NOT_FOUND  = 2;
    internal const int ERROR_NO_MORE_ITEMS   = 259;

    [DllImport("advapi32.dll", EntryPoint = "RegOpenKeyExW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int RegOpenKeyExW(IntPtr hKey, string subKey, int ulOptions, int samDesired, out IntPtr hkResult);

    [DllImport("advapi32.dll", EntryPoint = "RegCreateKeyExW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int RegCreateKeyExW(IntPtr hKey, string lpSubKey, int reserved, string lpClass,
        int dwOptions, int samDesired, IntPtr lpSecurityAttributes, out IntPtr phkResult, out int lpdwDisposition);

    [DllImport("advapi32.dll", EntryPoint = "RegCloseKey", ExactSpelling = true)]
    static extern int RegCloseKey(IntPtr hKey);

    [DllImport("advapi32.dll", EntryPoint = "RegEnumValueW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int RegEnumValueW(IntPtr hKey, int dwIndex, StringBuilder lpValueName, ref int lpcchValueName,
        IntPtr lpReserved, out int lpType, byte[] lpData, ref int lpcbData);

    [DllImport("advapi32.dll", EntryPoint = "RegSetValueExW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int RegSetValueExW(IntPtr hKey, string lpValueName, int reserved, int dwType, byte[] lpData, int cbData);

    internal class RegValue
    {
        public string Name;
        public int KindCode;
        public byte[] RawBytes;

        public string AsString()
        {
            if (RawBytes == null) return "";
            // Trim trailing UTF-16 NUL.
            int len = RawBytes.Length / 2;
            if (len > 0 && BitConverter.ToChar(RawBytes, (len - 1) * 2) == '\0') len--;
            return Encoding.Unicode.GetString(RawBytes, 0, len * 2);
        }

        public string[] AsMultiString()
        {
            if (RawBytes == null || RawBytes.Length < 2) return new string[0];
            var s = Encoding.Unicode.GetString(RawBytes);
            // REG_MULTI_SZ ends in a double NUL. Strip trailing NULs then split.
            s = s.TrimEnd('\0');
            if (s.Length == 0) return new string[0];
            return s.Split('\0');
        }
    }

    internal static List<RegValue> EnumerateValues(int hRootInt, string subKey, out int errOut)
    {
        errOut = 0;
        IntPtr hRoot = new IntPtr(hRootInt);
        IntPtr h;
        int err = RegOpenKeyExW(hRoot, subKey, 0, KEY_READ, out h);
        if (err != ERROR_SUCCESS) { errOut = err; return null; }
        var list = new List<RegValue>();
        try
        {
            for (int idx = 0; ; idx++)
            {
                var nameBuf = new StringBuilder(16384);
                int nameLen = nameBuf.Capacity;
                int kind;
                int dataLen = 0;
                // First call: get required buffer size for data.
                int e = RegEnumValueW(h, idx, nameBuf, ref nameLen, IntPtr.Zero, out kind, null, ref dataLen);
                if (e == ERROR_NO_MORE_ITEMS) break;
                if (e != ERROR_SUCCESS && e != 234 /* ERROR_MORE_DATA */)
                {
                    errOut = e;
                    break;
                }
                // Second call: actually read.
                nameBuf.Length = 0;
                nameLen = nameBuf.Capacity;
                var data = new byte[Math.Max(dataLen, 1)];
                int dataLen2 = data.Length;
                e = RegEnumValueW(h, idx, nameBuf, ref nameLen, IntPtr.Zero, out kind, data, ref dataLen2);
                if (e != ERROR_SUCCESS) { errOut = e; break; }
                if (dataLen2 < data.Length)
                {
                    var trimmed = new byte[dataLen2];
                    Array.Copy(data, trimmed, dataLen2);
                    data = trimmed;
                }
                list.Add(new RegValue { Name = nameBuf.ToString(), KindCode = kind, RawBytes = data });
            }
        }
        finally { RegCloseKey(h); }
        return list;
    }

    internal static bool SetValue(int hRootInt, string subKey, string valueName, int kindCode, byte[] data)
    {
        IntPtr hRoot = new IntPtr(hRootInt);
        IntPtr h;
        int disp;
        int e = RegCreateKeyExW(hRoot, subKey, 0, null, 0, KEY_WRITE, IntPtr.Zero, out h, out disp);
        if (e != ERROR_SUCCESS) return false;
        try
        {
            e = RegSetValueExW(h, valueName, 0, kindCode, data ?? new byte[0], data?.Length ?? 0);
            return e == ERROR_SUCCESS;
        }
        finally { RegCloseKey(h); }
    }

    internal static byte[] EncodeUtf16Z(string s)
    {
        if (s == null) s = "";
        var bytes = Encoding.Unicode.GetBytes(s);
        var withNul = new byte[bytes.Length + 2];
        Array.Copy(bytes, withNul, bytes.Length);
        return withNul;
    }

    internal static byte[] EncodeMultiUtf16Z(string[] parts)
    {
        if (parts == null || parts.Length == 0) return new byte[] { 0, 0, 0, 0 };
        var sb = new StringBuilder();
        foreach (var p in parts) { sb.Append(p); sb.Append('\0'); }
        sb.Append('\0');
        return Encoding.Unicode.GetBytes(sb.ToString());
    }
}
