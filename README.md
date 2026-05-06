# Wolfpack BepInEx Mods

Three mods for [Wolfpack](https://store.steampowered.com/app/1168840/Wolfpack/) (pre-beta, IL2CPP). Requires [BepInEx 6 IL2CPP](https://github.com/BepInEx/BepInEx).

---

## GT-LogbookExport

Writes a patrol log file to `BepInEx/` after each mission.

**Contents:**
- Torpedo launches, hits, premature detonations
- Ship sinkings with type and tonnage
- First detection (visual or hydrophone)
- U-boat loss
- Summary: torpedoes fired/hit/missed, total tonnage, detected yes/no

**Notes:**
- One file per boat per session, named `PatrolLog_U-96_2026-01-01_20-00-00.txt`
- Timestamps use in-game time (HH:MM:SS)
- Each player who installs the mod gets their own log; some events (ship sinkings, detection) may only fire on the host

---

## GT-NetworkFix

Sets `W_NetworkManager.numIterations = 3` to reduce rubber-banding and desync in multiplayer.

**Install on:** host **and** all clients.

---

## GT-LargerConvoy

Doubles convoy size by scaling `ConvoySpawner` fields ×2 on mission start.

Scales: merchants, armed merchants, carriers, sloops, corvettes, destroyers, merchant tonnage goal.

**Install on:** host only.

---

## Installation

1. Install [BepInEx 6 IL2CPP](https://github.com/BepInEx/BepInEx/releases) into the Wolfpack game folder
2. Drop the `.dll` files into `BepInEx/plugins/`
3. Launch the game

## Requirements

- Wolfpack pre-beta (Unity 2020.3, IL2CPP)
- BepInEx 6.0.0-be.697 or later
