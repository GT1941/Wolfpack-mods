# Wolfpack Mods

Gameplay mods and useful extras for [Wolfpack](https://store.steampowered.com/app/490920/Wolfpack/).
Choose the mods you want; you do not need to install the whole collection.
Requires [BepInEx 6 IL2CPP](https://github.com/BepInEx/BepInEx).

## Which players need the mod?

- **Host only:** install on the player hosting the mission. Other players can join without the mod.
- **Each player:** install on every player who wants that feature, including the host. These mods change that player's own interface or listening experience.
- **Host or client:** follow the individual mod's notes below; some features are shared when the host has the mod.

Downloads are arranged in [`Host mods/`](<Host mods/>), [`Client mods/`](<Client mods/>)
and [`Host and or Client mods/`](<Host and or Client mods/>).

## Mod overview

| Mod | What it adds | Install on |
| --- | --- | --- |
| [MissionMap](#missionmap) | A mission recording and interactive 3D replay | Host only |
| [Going Under — Convoy Ship Sinking](#going-under) | Damaged ships list, settle and sink in varied ways | Host only |
| [Shaken, Not Sunk](#shaken-not-sunk) | Machinery failures and onboard emergencies after depth-charge attacks | Host only |
| [AlliedUboat](#allieduboat) | A player-controlled allied U-boat alongside the convoy | Host only |
| [QuietLogbook](#quietlogbook) | Hide the logbook during play, then reveal it with F6 | Host only |
| [LargerConvoyScaled](#largerconvoyscaled) | Larger convoys, adjustable escort numbers and sinking goals | Host only |
| [TorpedoLoadout](#torpedoloadout) | 14 steam and 14 electric torpedoes per boat | Host only |
| [ClientBugFixes](#clientbugfixes) | Fixes for local sounds, TDC input and door appearance | Each player |
| [LogbookSeconds](#logbookseconds) | Seconds and extra torpedo details in the logbook | Host recommended for shared entries |
| [NetworkFix](#networkfix) | Multiplayer smoothing | Host; client installation optional |
| [SettingsKeeper](#settingskeeper) | Helps preserve settings between launches and branch changes | Each player |
| [TimeHUD](#timehud) | An on-screen clock and optional chat timestamps | Each player; host for shared timestamps |
| [Game Time API](#game-time-api) | Supplies mission time to compatible overlays and tools | Player using the tool |
| [RadioAPI](#radioapi) | Connects compatible radio tools to the game's radio | Player using the tool |

## Host gameplay mods

### MissionMap

Records a mission so the crew can review it afterward in a browser. The 3D viewer
shows U-boats, merchants, escorts, torpedoes, depth charges, routes, hits and
sinkings. Follow a boat, measure an attack or compare what happened across the convoy.

![MissionMap replay viewer](<Host mods/Mission Map/2.0/missionmap-2.0-preview.png>)

**How to use:**

1. Install [MissionMap.dll](<Host mods/Mission Map/2.0/MissionMap.dll>) on the host.
2. Finish the mission through the normal end/debrief screen so the recording is saved.
3. Find `MissionMap_<timestamp>.json` in the game's `BepInEx` folder.
4. Open the bundled [2.0.2 replay viewer](<Host mods/Mission Map/2.0/missionmap2.0.2.html>) in a browser and drop the recording onto it.

The recorder can also post completed recordings to Discord. This is **off by
default**; enable it and enter your channel's webhook in `BepInEx/config/MissionMap.cfg`.
The older recorder and viewer remain in [pre-2.0](<Host mods/Mission Map/pre-2.0/>).
Use one recorder version at a time.

<a id="going-under"></a>

### Going Under — Convoy Ship Sinking

Makes damaged convoy merchants and escorts list and settle before sinking.
Ships can go down bow-first or stern-first, roll onto their side, or occasionally
turn upside down. The locations and combined damage of hits influence the result.
**U-boats are not affected.**

The bow and stern only settle slightly while a ship is still alive; larger
movements begin after the game marks it sunk. Wrecks remain until their centre
reaches **100 metres underwater or 10 minutes have passed**, whichever comes first.
Time acceleration advances that removal timer. Damage, fires and sinking credit
still follow the game's normal rules.

**Use:** automatic; no hotkey. Install on the host only.

**Download:** [GoingUnder.dll](<Host mods/GoingUnder.dll>). This replaces
`ProgressiveSinking.dll`; remove the old DLL when upgrading. Existing settings
are kept in `gt1941.wolfpack.progressivesinking.cfg`.

<a id="shaken-not-sunk"></a>

### Shaken, Not Sunk

Turns depth-charge attacks into onboard emergencies. Depending on the damage,
the crew may have to deal with ventilation flooding, flooded engine cylinders,
electrical or machinery faults, stuck controls and temporary instrument failures.
The boat can also suffer a temporary sideways roll.

Severe attacks causing **20% or more damage** force at least one engine-room
ventilation valve open when ventilation failures are enabled. The crew must close
it to stop the water entering. The mod does not protect the boat from sinking or
repair ordinary hull damage.

**Use:** failures occur automatically after attacks. Failure types and chances
are configurable. Clients can operate the affected machinery without installing
the mod.

**Download:** [ShakenNotSunk.dll](<Host mods/ShakenNotSunk.dll>).

### AlliedUboat

Lets a player crew act as an allied submarine near the convoy. Choose the boat
by name; convoy ships and escorts treat it as friendly while its allied status
is active. It can fight the Axis U-boats, and attacking convoy ships can revoke
its protection.

**Use:** enable the mod and set the allied boat's name in
`BepInEx/config/AlliedUboat.cfg` before starting the mission. It is disabled by
default.

**Download:** [AlliedUboat.dll](<Host mods/AlliedUboat.dll>). Remove the older
`GhostUboat.dll` if upgrading. Do not combine it with Playable Corvette.

### QuietLogbook

Keeps the logbook empty during the mission so the crew must judge hits and
sinkings from what they observe. The host can press **F6** to reveal the
accumulated entries before leaving the mission. MissionMap continues recording
the events normally.

**Note:** clients still see the tonnage counter. F6 can reveal the book at any
time, so save it for the end if that is your group's rule.

**Download:** [QuietLogbook.dll](<Host mods/QuietLogbook.dll>).

### LargerConvoyScaled

Adds larger convoys and sets sinking goals and escort numbers for each lobby
convoy-size setting. The host can adjust convoy tonnage, the mission goal,
sloops, corvettes and destroyers in `BepInEx/config/LargerConvoyScaled.cfg`.

**Use:** choose the convoy size in the lobby, then start a mission. Install only
one `LargerConvoy` variant at a time. Very large formations can produce unusual
ship or escort behavior.

**Download:** [LargerConvoyScaled.dll](<Host mods/LargerConvoyScaled.dll>).

### TorpedoLoadout

Gives each boat **14 steam (T1) and 14 electric (T2) torpedoes**. The loadout is
also reapplied when the game resets or reloads the boat's torpedo selection.

**Use:** automatic; no hotkey.

**Download:** [TorpedoLoadout.dll](<Host mods/TorpedoLoadout.dll>).

## Local fixes and utilities

### ClientBugFixes

Fixes several issues on the player's own game:

- TDC dials changing just from hovering after leaving the reload screen or returning to the station.
- Doors looking open while still blocking passage.
- Creep-mode switch clicks and ladder sounds being heard too far away.
- Some missing sounds and the dive alarm being inaudible in the engine room.

An optional setting also reduces the straight bright strip beneath burning ships.
It is **off by default** and reduces fire glow on the water when enabled.

**Use:** automatic. Individual fixes can be enabled or disabled in
`BepInEx/config/ClientBugFixes.cfg`. Each player who wants the fixes must install
the mod, including the host.
Remove older `AudioPatcher.dll` and `TorpedoMenuFix.dll` copies that duplicate these fixes.

**Download:** [ClientBugFixes.dll](<Host and or Client mods/ClientBugFixes.dll>).

### LogbookSeconds

Adds seconds to logbook timestamps and extra details to torpedo entries:
launch speed and detonator, firing tube, impact angle and clearer friendly-fire reports.

For example: `14:07:12 LAUNCHED TORPEDO TUBE 1. (44kn magnetic)`.

**Use:** automatic. Install on the host to share the enhanced logbook entries
with the crew. Clients do not need the mod for this.

**Download:** [LogbookSeconds.dll](<Host and or Client mods/LogbookSeconds.dll>).

### NetworkFix

Helps reduce delayed multiplayer updates and rubber-banding. Install on the
host; client installation is optional.

**Download:** [NetworkFix.dll](<Host and or Client mods/NetworkFix.dll>).

### SettingsKeeper

Helps retain selected settings between launches and Steam branch changes,
including the recognition manual's background preference.

**Use:** automatic. Install on each player who wants to keep their own settings.

**Download:** [SettingsKeeper.dll](<Client mods/SettingsKeeper.dll>).

### TimeHUD

Adds a clock showing the current in-game time and optional timestamps to chat.
The clock is local to each player; host installation makes shared chat timestamps
available to the other players.

**Use:** automatic. Display options are configurable in `BepInEx/config/TimeHUD.cfg`.

**Download:** [TimeHUD.dll](<Client mods/TimeHUD.dll>).

### Game Time API

Supplies the current mission time, date and boat information to compatible
webpages, stream overlays and other tools. It does not add an in-game display by itself.

**Use:** install on the same computer as the tool using it.

**Download:** [GameTime_API.dll](<Client mods/GameTime_API.dll>).

### RadioAPI

Allows compatible external radio tools to read received radio traffic and send
Morse messages through the game's radio. Actual transmissions are heard by the
other players just like normal radio use.

**Use:** install on the same computer as the radio tool. It does not add an
in-game radio interface by itself.

**Download:** [RadioAPI.dll](<Client mods/RadioAPI.dll>).

## Installation

1. Close Wolfpack.
2. Install a compatible **BepInEx 6 IL2CPP for Windows x64** build in the Wolfpack game folder. Builds are available from the [official BepInEx build server](https://builds.bepinex.dev/projects/bepinex_be).
3. Launch the game once, then close it to create the BepInEx folders.
4. Copy your chosen mod DLLs into `Wolfpack/BepInEx/plugins/`. The repository's folder names describe who needs the mods; the DLLs all go into this same game folder.
5. Start the game. Most mods create their settings in `BepInEx/config/` on first launch. Close the game before editing those files.

To update a mod, replace its old DLL rather than keeping multiple versions.
If a mod has been renamed, remove the previous DLL too. To remove a mod, close
the game and take its DLL out of `BepInEx/plugins/`.

## Archived releases

The [`archive/`](archive/) folder contains older `LargerConvoy1_5x.dll`,
`LargerConvoy2x.dll` and `LogbookExport.dll` releases. They are kept for reference
and may not work with current game versions. MissionMap replaces the older
LogbookExport recording tool. The previous MissionMap recorder/viewer is kept
separately under [pre-2.0](<Host mods/Mission Map/pre-2.0/>).

## Licence

The plugin DLLs, replay viewer and documentation are **All Rights Reserved**.
See [LICENSE.txt](LICENSE.txt) for the full terms. Redistribution or modification
requires the copyright holder's written permission.

The linked dependencies retain their own licences, reproduced in `LICENSE.txt`:

- **BepInEx:** GNU Lesser General Public License, version 2.1.
- **HarmonyX:** MIT.

## Support development

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/L8E121S5BX)
