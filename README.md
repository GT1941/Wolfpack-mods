# Wolfpack BepInEx Mods

Three BepInEx mods for [Wolfpack](https://store.steampowered.com/app/1168840/Wolfpack/) (pre-beta, IL2CPP). Requires [BepInEx 6 IL2CPP](https://github.com/BepInEx/BepInEx).

---

## GT-LogbookExport

Writes a patrol log file to `BepInEx/` after each mission, and upgrades the in-game C-menu logbook to show seconds.

**Patrol log contents:**
- Torpedo launches, hits, premature detonations
- Ship sinkings with type and tonnage
- First detection (visual, hydrophone, or inferred from convoy fleeing)
- U-boat loss
- Summary: torpedoes fired/hit/missed, total tonnage, detected yes/no

**In-game logbook:**
- Upgrades timestamps from `HH:MM` to `HH:MM:SS` in the C-menu logbook

**Notes:**
- One patrol log file per boat per session, named e.g. `PatrolLog_U-96_2026-01-01_20-00-00.txt`
- Timestamps use in-game time (HH:MM:SS)
- Some patrol log events (ship sinkings, detection) may only fire on the host

---

## GT-NetworkFix

Sets `W_NetworkManager.numIterations = 3` (up from the default 1) to reduce rubber-banding and desync in multiplayer.

**Install on:** host **and** all clients.

---

## GT-LargerConvoy

Doubles convoy size by scaling `ConvoySpawner` fields ×2 on mission start.

Scales: merchants, armed merchants, carriers, sloops, corvettes, destroyers, merchant tonnage goal.

**Install on:** host only.

---

## Installation

1. Install [BepInEx 6 IL2CPP](https://github.com/BepInEx/BepInEx/releases) into the Wolfpack game folder
2. Drop the desired `.dll` files into `BepInEx/plugins/`
3. Launch the game

## Repository Structure

```
.
├── GT-LogbookExport.dll
├── GT-NetworkFix.dll
├── GT-LargerConvoy.dll
└── src/
    ├── LogbookExport/
    ├── WolfpackNetworkFix/
    └── LargerConvoy/
```

## Requirements

- Wolfpack pre-beta (Unity 2020.3, IL2CPP)
- BepInEx 6.0.0-be.697 or later
- .NET 8 SDK (only if building from source)
