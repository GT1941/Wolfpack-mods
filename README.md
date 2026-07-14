**Wolfpack BepInEx Mods**

BepInEx mods for [Wolfpack](https://store.steampowered.com/app/490920/Wolfpack/). Requires [BepInEx 6 IL2CPP](https://github.com/BepInEx/BepInEx).

---

## MissionMap

Records every entity's position over the course of a mission and ships with a full 3D replay viewer.

**MissionMap 2.0 is coming soon.** The public release currently contains the archived pre-2.0 recorder and viewer under `Mission Map/pre-2.0/`; the 2.0 recorder and viewer are still being finalized.

**Recorder** writes `BepInEx/MissionMap_<timestamp>.json` containing:
- U-boats sampled every 1 s (drops to 0.25 s during dives so under-keel tracks transitions): position, depth, speed, heading, HP, battery, under-keel clearance.
- Convoy ships every 15 s: position, heading, physical speed, ship-type name, HP, two-level alert flag (hard = target acquired, soft = investigating without target), target bearing on hard-alerted escorts, burning flag.
- Torpedoes and depth charges per tick whenever they're in flight (torpedo depth included).
- Seafloor depth markers tagged with a coarse / fine level so the viewer can replicate the in-game zoom-based LOD swap.
- Events: torpedo launch (with TDC range, set speed in kn, gyro, depth, type, magnetic flag, owner, tube), torpedo hit (with tube + server-synced time), ship sunk (name + tonnage), depth-charge fire/impact, gun fire/land, collisions, bottom hits, u-boat lost.
- Crew chart drawings (lines, circles, time-nodes, text annotations) with per-boat ownership.
- Mission settings, crew roster snapshots, and player connect/disconnect events.
- Top-level: ISO-8601 timestamp with timezone offset, in-game date, game-time anchor + measured game-time rate.

A 3D replay viewer is included at [`Mission Map/pre-2.0/missionmap.html`](<Mission Map/pre-2.0/missionmap.html>) — drop a mission JSON onto it to review the patrol. It shows the boats, convoy, torpedoes, routes, depths, chart drawings, hits, and other important events on an interactive map. You can follow individual U-boats, measure distances and times, inspect attacks, and see when escorts are searching or engaging.

MissionMap 2.0 preview:

![MissionMap 2.0 preview](<Mission Map/2.0/missionmap-2.0-preview.png>)

**Discord auto-post:** MissionMap can automatically post the finished mission JSON, plus optional summary, roster, and settings files, to a Discord channel via webhook. It's off by default and configured through the mod's BepInEx config file (`BepInEx/config/MissionMap.cfg`) — set a webhook URL and the post options there to enable it.

**Notes:**
- **Host-only.** Only the host writes the recording. Dropping the DLL on every peer is harmless — non-host installs stay inert.
- Recording finalises when the mission ends cleanly (debrief).
- Ship hulls in the viewer are drawn to their real draught and height so torpedo running depths visually line up with the keel they would strike.
- A "lights on" indicator appears over every merchant while the convoy is fleeing, matching what the AI is actually doing in the moment.

**Install on:** host only (clients no-op silently).

---

## LogbookSeconds

Enhancements to the in‑game C‑menu logbook:

1. **Seconds upgrade** — rewrites timestamps from `HH:MM` to `HH:MM:SS` for finer time resolution.
2. **Torpedo launch metadata** — appends the torpedo's speed and detonator type to the launch entry the game just wrote. Example:
   ```
   14:07:12 LAUNCHED TORPEDO TUBE 1. (44kn magnetic)
   ```
3. **Torpedo hit tube + impact angle** — appends the firing tube and impact angle to each hit line so the captain can see which tube scored and how square the hit was:
   ```
   14:07:40 TORPEDO HIT HEAVY TANKER, TYPE 32. (tube 1, 78° impact)
   ```
4. **Friendly-fire reporting** — when a torpedo strikes another U-boat the game logs only a vague "premature" line; this rewrites it into a proper friendly-fire hit/sink report (firer, victim, tube, impact angle), and notes the loss on both boats if the hit is fatal.

Each player sees their own logbook locally — the mod doesn't change anything on the wire and is safe to run alongside other players with or without the same mod installed.

**Install on:** any client.

---

## TorpedoLoadout

Sets the per-boat torpedo loadout to **14 steam (T1) and 14 electric (T2)**, applied to all 4 crews. The override catches every reload path — mission start, the lobby's reset button, default-loadout reload — so the count stays at 14/14 regardless of how the game tries to reset it.

**Install on:** host only.

---

## Other Mods

Additional utilities in the [`Other Mods/`](Other%20Mods/) folder:

- **Game Time API** — exposes in-game time and mission state at `http://127.0.0.1:1941/time` for webpages and overlays. Install on any client.
- **TimeHUD** — adds an in-game clock and optional synchronized chat timestamps. Install on any client; host installation makes chat timestamps visible to everyone.
- **RadioAPI** — exposes radio send/receive endpoints at `http://127.0.0.1:1942` for external tools and overlays. Install on any client.
- **EscortDCStock** — restores stock-style escort depth-charge inventory and reload behavior. Host only.
- **SettingsKeeper** — persists selected Wolfpack settings between launches. Install on any client.

---

## Archived mods

Past mods kept under `archive/` for historical reference. Not actively
maintained — pull a DLL out and drop it into `BepInEx/plugins/` if you
want to try it, but expect drift against current game versions.

- **`archive/LogbookExport.dll`** — wrote a patrol-log summary file to
  `BepInEx/` after each mission (torpedo launches / hits, sinkings,
  first detection, summary totals). Superseded in practice by the
  much richer MissionMap JSON + viewer.
- **`archive/NetworkFix.dll`** — bumped the multiplayer network update
  rate to reduce rubber-banding and desync. Recent game patches appear
  to have addressed most of what it was working around.
- **`archive/LargerConvoy1_5x.dll`** / **`archive/LargerConvoy2x.dll`** —
  increase convoy spawn counts by ×1.5 or ×2. Load only one variant at a
  time; the convoy AI may behave unpredictably at inflated sizes.
- **`LargerConvoyScaled.dll`** — uses configurable per-lobby-size spawn,
  tonnage-goal, and escort targets. Host only; do not load alongside another
  `LargerConvoy*.dll`.

---

## Installation

1. Install BepInEx 6 IL2CPP into the Wolfpack game folder. Use a recent **bleeding-edge** build (the `win-x64` IL2CPP artifact) from the official build server: <https://builds.bepinex.dev/projects/bepinex_be>
2. Drop the desired `.dll` files into `BepInEx/plugins/`
3. Launch the game

## Repository Structure

```
.
├── LargerConvoyScaled.dll
├── LogbookSeconds.dll
├── TorpedoLoadout.dll
├── Other Mods/
│   ├── EscortDCStock.dll
│   ├── GameTime_API.dll
│   ├── RadioAPI.dll
│   ├── SettingsKeeper.dll
│   └── TimeHUD.dll
├── archive/
│   ├── LargerConvoy1_5x.dll
│   ├── LargerConvoy2x.dll
│   ├── LogbookExport.dll
│   └── NetworkFix.dll
└── Mission Map/
    ├── 2.0/                 # coming soon
    │   └── missionmap-2.0-preview.png
    └── pre-2.0/
        ├── MissionMap.dll
        ├── missionmap.html
        └── insignia/
```

Source is not currently public.

## Requirements

- Wolfpack pre-beta — Steam `testing` or `beta-beta` branch (Unity 2020.3, IL2CPP)
- BepInEx-Unity.IL2CPP-win-x64-6.0.0-pre.2 (the version these mods were built and tested against). Newer bleeding-edge builds from <https://builds.bepinex.dev/projects/bepinex_be> generally work too.

## Licence

The plugin DLLs, web viewer, and documentation in this repository are
**All Rights Reserved** — see [`LICENSE.txt`](LICENSE.txt) for the full
text. They are not open source; redistribution or modification requires
the copyright holder's written permission.

The linked dependencies retain their original licences, also reproduced
in `LICENSE.txt`:

- **BepInEx** — GNU Lesser General Public License, version 2.1
- **HarmonyX** (the Harmony2 fork bundled with BepInEx 6) — MIT
