# Wolfpack BepInEx Mods

BepInEx mods for [Wolfpack](https://store.steampowered.com/app/1168840/Wolfpack/) (pre-beta, IL2CPP). Requires [BepInEx 6 IL2CPP](https://github.com/BepInEx/BepInEx).

---

## GT-LogbookExport

Writes a patrol log file to `BepInEx/` after each mission, summarising what happened.

**Patrol log contents:**
- Torpedo launches (with tube number), hits (with tube number), premature detonations
- Ship sinkings with type and tonnage
- First detection (visual, hydrophone, or inferred from convoy fleeing)
- U-boat loss
- Summary: torpedoes fired/hit/missed, total tonnage, detected yes/no

**Notes:**
- One patrol log file per boat per session, named e.g. `PatrolLog_U-96_2026-01-01_20-00-00.txt`
- Timestamps use in-game time (HH:MM:SS)
- **Host only:** if you join someone else's session, the mod stays inert and does not write a file (clients lack visibility into most events). Look for `[LogbookExport] Skipping patrol log (not host)` in `BepInEx/LogOutput.log` if you're unsure.

**Install on:** host (writes the file). Loading it on clients is harmless — it just does nothing.

---

## GT-LogbookSeconds

Upgrades the in-game C-menu logbook to show seconds (`HH:MM` → `HH:MM:SS`).

Pure client-side: works whether you host or join, regardless of whether the host has the mod. Two players running it simultaneously is fine — the rewrite is idempotent.

**Install on:** any client.

---

## GT-NetworkFix

Sets `W_NetworkManager.numIterations = 3` (up from the default 1) to reduce rubber-banding and desync in multiplayer.

> **May be redundant in current builds.** Recent game patches included AI ship maneuverability / movement fixes that appear to address some of what this mod was originally working around. It hasn't been A/B tested against the latest version, so it's left available — using it shouldn't hurt either way, but you may not need it.

**Install on:** host (required if used); clients (optional).

---

## GT-LargerConvoy

Scales convoy size by patching `ConvoySpawner.randomEncounter` and multiplying its outputs (merchants, armed merchants, carriers, sloops, corvettes, destroyers, merchant tonnage goal).

**Install on:** host only.

### Variants

| DLL | Multiplier | Notes |
| --- | --- | --- |
| `LargerConvoy1_5x.dll` | ×1.5 | Gentle bump |
| `LargerConvoy2x.dll` | ×2 | Default |

> **Important:** Load only **one** LargerConvoy DLL at a time. All variants patch the same method (`ConvoySpawner.randomEncounter`), so loading two would **stack** the multipliers — e.g. `1_5x` + `2x` would give ×3, not ×2. When switching variants, delete the old DLL from `BepInEx/plugins/` before dropping in the new one.

---

## GT-GameTime

Exposes in-game state via a local HTTP API on port 1941 for use by webpages, OBS overlays, or other tools.

**Endpoint:** `http://127.0.0.1:1941/time` returns JSON, e.g.:

```json
{"time":"08:59:03","date":"01.09.1939","vessel":"U-552","missionActive":true}
```

A sample webpage that shows a countdown from current in-game time to a chosen impact time is provided at [`web/toi.html`](web/toi.html) — open it locally in any browser while the game is running.

**Install on:** any client.

---

## Installation

1. Install [BepInEx 6 IL2CPP](https://github.com/BepInEx/BepInEx/releases) into the Wolfpack game folder
2. Drop the desired `.dll` files into `BepInEx/plugins/`
3. Launch the game

## Repository Structure

```
.
├── LogbookExport.dll
├── LogbookSeconds.dll
├── NetworkFix.dll
├── LargerConvoy2x.dll
├── LargerConvoy1_5x.dll
├── GameTime.dll
├── src/
│   ├── LogbookExport/
│   ├── LogbookSeconds/
│   ├── WolfpackNetworkFix/
│   ├── LargerConvoy2x/
│   ├── LargerConvoy1_5x/
│   └── GameTime/
└── web/
    └── toi.html
```

## Requirements

- Wolfpack pre-beta — Steam `testing` or `beta-beta` branch (Unity 2020.3, IL2CPP)
- BepInEx-Unity.IL2CPP-win-x64-6.0.0-pre.2 (the version these mods were built and tested against)
- .NET 8 SDK (only if building from source)
