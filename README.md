# Wolfpack BepInEx Mods

BepInEx mods for [Wolfpack](https://store.steampowered.com/app/1168840/Wolfpack/) (pre-beta, IL2CPP). Requires [BepInEx 6 IL2CPP](https://github.com/BepInEx/BepInEx).

---

## LogbookExport

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
- **Host only:** if you join someone else's session, the mod stays inert and does not write a file (clients lack visibility into most events).

**Install on:** host (writes the file). Loading it on clients is harmless — it just does nothing.

---

## LogbookSeconds

Three enhancements to the in‑game C‑menu logbook:

1. **Seconds upgrade** — rewrites timestamps from `HH:MM` to `HH:MM:SS` for finer time resolution.
2. **Torpedo launch metadata** — appends the torpedo's speed and detonator type to the launch entry the game just wrote. Example:
   ```
   14:07:12 LAUNCHED TORPEDO TUBE 1. (44kn magnetic)
   ```
3. **Torpedo hit tube** — appends the firing tube to each hit line so the captain can see which tube scored:
   ```
   14:07:40 TORPEDO HIT HEAVY TANKER, TYPE 32. (tube 1)
   ```

`logBook` is per‑peer (not synced), so each peer maintains its own copy. The mod runs locally on every peer that sees the firing/exploding torpedo — each captain gets the enrichment on their own boat regardless of who is hosting. Idempotency guards stop double‑appends if the field ever does sync. Two players running the mod simultaneously is fine.

**Install on:** any client.

---

## NetworkFix

Sets `W_NetworkManager.numIterations = 3` (up from the default 1) to reduce rubber-banding and desync in multiplayer.

> **May be redundant in current builds.** Recent game patches included AI ship maneuverability / movement fixes that appear to address some of what this mod was originally working around. It hasn't been A/B tested against the latest version, so it's left available — using it shouldn't hurt either way, but you may not need it.

**Install on:** host (required if used); clients (optional).

---

## LargerConvoy

Scales convoy size by patching `ConvoySpawner.randomEncounter` and multiplying its outputs (merchants, armed merchants, carriers, sloops, corvettes, destroyers, merchant tonnage goal).

> **Note:** The mission's success/scoring tonnage goal is **not** scaled — only the spawned convoy is. So with ×2 you'll see roughly twice as many targets, but the threshold to "succeed" the mission stays the same as the unmodded value, effectively making missions easier (more targets to chew through for the same objective).

> **Note:** Convoy and escort AI is not designed for these inflated counts. Expect odd behaviour — formation glitches, escorts overlapping or piling up, weird pathing, unusual detection/fleeing responses. The game's AI is balanced around the unmodded convoy size, and the mod doesn't touch any of that logic.

**Install on:** host only.

### Variants

| DLL | Multiplier | Notes |
| --- | --- | --- |
| `LargerConvoy1_5x.dll` | ×1.5 | Gentle bump |
| `LargerConvoy2x.dll` | ×2 | Default |

> **Important:** Load only **one** LargerConvoy DLL at a time. All variants patch the same method (`ConvoySpawner.randomEncounter`), so loading two would **stack** the multipliers — e.g. `1_5x` + `2x` would give ×3, not ×2. When switching variants, delete the old DLL from `BepInEx/plugins/` before dropping in the new one.

---

## Game Time API

Exposes in-game state via a local HTTP API on port 1941 for use by webpages, OBS overlays, or other tools.

**Endpoint:** `http://127.0.0.1:1941/time` returns JSON, e.g.:

```json
{"time":"08:59:03","date":"01.09.1939","vessel":"U-552","missionActive":true}
```

A sample webpage that shows a countdown from current in-game time to a chosen impact time is provided at [`web/toi.html`](web/toi.html) — open it locally in any browser while the game is running.

**Install on:** any client.

---

## MissionMap

Records every entity's position over the course of a mission and ships with a full 3D replay viewer.

**Recorder** writes `BepInEx/MissionMap_<timestamp>.json` containing:
- U-boats sampled every 1 s (drops to 0.25 s during dives so under-keel tracks transitions): position, depth, speed, heading, HP, battery, under-keel clearance.
- Convoy ships every 15 s: position, heading, physical speed, ship-type name, HP, alerted/burning flags.
- Torpedoes and depth charges per tick whenever they're in flight.
- Events: torpedo launch (with TDC range, set speed in kn, gyro, depth, type, magnetic flag, owner), torpedo hit, ship sunk (name + tonnage), depth-charge fire/impact, gun fire/land, collisions, bottom hits, u-boat lost.
- Top-level: ISO-8601 timestamp with timezone offset, in-game date, game-time anchor + measured game-time rate.

A 3D replay viewer (Three.js, single HTML file with multi-TOI tracking, bathymetry sweep, per-boat hit stats, overspeed glow, and an under-keel warning system) is available in the dev repo.

**Notes:**
- Multiplayer clients have limited visibility — recordings are most complete when run on the host.
- Recording only finalises when the mission ends cleanly (debrief).

**Install on:** host (full data); clients (sparse / often empty).

---

## RadioAPI

Exposes the in-game radio's send/receive stream as a local HTTP API on port 1942 for external tools and overlays.

**Endpoints:**
- `GET  /` — current radio state (channel, RX buffer).
- `POST /send-char`, `/send-text`, `/send-morse-bits` — transmit through the player's radio, network-replicated like typing on the in-game radio. Pacing follows the game's per-letter Morse duration so repeated letters key correctly. Scandinavian/German letters substitute to ASCII Morse digraphs (`Å → AA`, `Ø → OE`, `Æ → AE`, etc.).
- `POST /inject-char`, `/inject-text` — local-only test harness: drives the receive side without putting anything on the network. Useful for solo training / bot-sim.

A companion HTML frontend (codebook, Bot Sim panel, Enigma helper, Web Audio fallback synth) is available in the dev repo.

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
├── LargerConvoy1_5x.dll
├── LargerConvoy2x.dll
├── GameTime_API.dll
├── MissionMap.dll
├── RadioAPI.dll
└── web/
    └── toi.html
```

Source is not currently public.

## Requirements

- Wolfpack pre-beta — Steam `testing` or `beta-beta` branch (Unity 2020.3, IL2CPP)
- BepInEx-Unity.IL2CPP-win-x64-6.0.0-pre.2 (the version these mods were built and tested against)
