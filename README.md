# Wolfpack BepInEx Mods

BepInEx mods for [Wolfpack](https://store.steampowered.com/app/1168840/Wolfpack/) (pre-beta, IL2CPP). Requires [BepInEx 6 IL2CPP](https://github.com/BepInEx/BepInEx).

---

## MissionMap

Records every entity's position over the course of a mission and ships with a full 3D replay viewer.

**Recorder** writes `BepInEx/MissionMap_<timestamp>.json` containing:
- U-boats sampled every 1 s (drops to 0.25 s during dives so under-keel tracks transitions): position, depth, speed, heading, HP, battery, under-keel clearance.
- Convoy ships every 15 s: position, heading, physical speed, ship-type name, HP, alerted/burning flags.
- Torpedoes and depth charges per tick whenever they're in flight (torpedo depth included).
- Events: torpedo launch (with TDC range, set speed in kn, gyro, depth, type, magnetic flag, owner, tube), torpedo hit (with tube + server-synced time), ship sunk (name + tonnage), depth-charge fire/impact, gun fire/land, collisions, bottom hits, u-boat lost.
- Mission settings, crew roster snapshots, and player connect/disconnect events.
- Top-level: ISO-8601 timestamp with timezone offset, in-game date, game-time anchor + measured game-time rate.

A 3D replay viewer is included at [`web/missionmap.html`](web/missionmap.html) — drop a mission JSON onto it. Three.js scene with to-scale hulls, a hierarchical Kriegsmarine naval grid that subdivides as you zoom, seafloor depth markers, per-U-boat chart-drawing toggles, multi-TOI tracking, bathymetry sweep, per-boat hit stats, overspeed glow, and an under-keel warning system.

**Discord auto-post (experimental):** there is an initial attempt at automatically posting the finished mission JSON (plus an optional summary, roster, and settings) to a Discord channel via webhook. It's off by default and configured entirely through the mod's BepInEx config file (`BepInEx/config/MissionMap.cfg`) — set a webhook URL and the post options there to enable it. Treat it as a work in progress.

**Notes:**
- **Host-only:** `StartRecording` early-returns when `W_NetworkManager.IsServer` is false, so client installs log `"Not host — recording skipped"` and stay inert. Drop the DLL on every peer if you want — only the host writes.
- Recording only finalises when the mission ends cleanly (debrief).
- 1.9.9+ recordings carry real ship draught + total height from `MerchantShipStats` / `WarshipStats`, so the viewer renders underwater hulls to scale (torpedo-depth vs keel lines up visually).
- 1.9.11+ recordings emit `convoy_fleeing` events when the AI flips into evasion; the viewer shows a 💡 lights-on indicator on every merchant while the convoy is fleeing.

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

`logBook` is per‑peer (not synced), so each peer maintains its own copy. The mod runs locally on every peer that sees the firing/exploding torpedo — each captain gets the enrichment on their own boat regardless of who is hosting. Idempotency guards stop double‑appends if the field ever does sync. Two players running the mod simultaneously is fine.

**Install on:** any client.

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

## LargerConvoyScaled (experimental)

Sibling of the LargerConvoy multiplier family with a different design: instead of a single ×N factor, it uses **per-size targets** with a flat 100k-step goal progression and 25k of spawn headroom over every goal. Lobby size selector becomes a difficulty knob rather than a convoy-size knob:

| Convoy size | Scaled spawn | Scaled goal | Scaled escorts (S / C / DD) | Total escorts |
| --- | ---:| ---:| ---:| ---:|
| Small      | 125,000 t | 100,000 t | 0 /  4 / 1 |  5 |
| Normal     | 225,000 t | 200,000 t | 0 /  8 / 3 | 11 |
| Large      | 325,000 t | 300,000 t | 4 / 10 / 3 | 17 |
| Very Large | 425,000 t | 400,000 t | 7 / 11 / 4 | 22 |

The mission tonnage goal is scaled by writing `VictoryConditionsTonnage.tonnageLimit` after `initConditions` (`SyncedFloat`, host-side write replicates to clients). Escort counts are absolute per-size targets so the screen feels comparable even when the rolled encounter has a different default.

**Install on:** host only. **Do not load alongside any other `LargerConvoy*.dll`** — they all patch the same methods and would stack into nonsense.

> Convoy/escort AI is not designed for inflated counts of this magnitude. Expect formation glitches and odd path-finding at the higher sizes — the game's AI doesn't get patched by this mod.

---

## TorpedoLoadout

Sets the per-boat torpedo loadout to **14 steam (T1) and 14 electric (T2)**, applied to all 4 crews.

A Harmony prefix on `Crew.resetTorpedoes(byte numT1, byte numT2)` rewrites the count arguments before the game applies them, catching every reset path (mission start, lobby UI reset button, default-loadout reload).

**Install on:** host only.

---

## ChartSync (experimental)

Attempts to fix the multiplayer chart-drawing bug: in the vanilla game, **drawings made by a client never sync to the host or other clients** — the host draws and everyone sees, but the client draws and only that client sees.

ChartSync postfixes `MapUndo.pushUndoDraw` (which fires exactly once per local draw, never on network-received re-instantiates) and routes the local spawn through `W_NetworkManager.instance.clientInstantiate(type, pos, rot)` so the host receives via the normal client-spawn pipeline and rebroadcasts via `spawnForAll`. The local-only copy is destroyed; the synced version arrives back within one network round-trip.

**Known limitation:** `clientInstantiate` carries only position + rotation. Per-shape syncvars (line endpoints, circle radius, time-node timestamp) don't transfer through this path yet — those land at the host with prefab defaults. **Crosses (points) sync correctly across all peers; lines and circles may sync with default geometry** until a follow-up build also pushes those syncvars.

**Install on:** every peer (host + all clients). Skipped on the host via `W_NetworkManager.IsServer` since the host's own drawings already broadcast correctly. Each broadcast logs `[ChartSync] CLIENT broadcast <type> at (X,Z)` so transmission can be verified from the BepInEx log.

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

## TimeHUD

Two small features in one mod:

1. **In-game clock HUD** — a small label in the top-right of the screen showing the current in-game clock (`HH:MM:SS` by default; seconds optional). Drawn over the game UI so it sits on top of every screen without fighting Wolfpack's canvas hierarchy.
2. **Chat timestamps** — every line shown via `W_InGameChat.showMessage`/`showTextMessage` gets a `[HH:MM]` prefix using the current in-game time. Each viewer prepends their own local stamp; `azureTime` is server-synced, so timestamps agree across peers within ~1 s.

Both features are individually togglable in BepInEx config (`HUD.Enabled`, `Chat.Enabled`), with extra knobs for font size, seconds vs. minutes precision, etc.

**Install on:** any client.

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

1. Install BepInEx 6 IL2CPP into the Wolfpack game folder. Use a recent **bleeding-edge** build (the `win-x64` IL2CPP artifact) from the official build server: <https://builds.bepinex.dev/projects/bepinex_be>
2. Drop the desired `.dll` files into `BepInEx/plugins/`
3. Launch the game

## Repository Structure

```
.
├── ChartSync.dll
├── GameTime_API.dll
├── LargerConvoy1_5x.dll
├── LargerConvoy2x.dll
├── LargerConvoyScaled.dll
├── LogbookExport.dll
├── LogbookSeconds.dll
├── MissionMap.dll
├── RadioAPI.dll
├── TimeHUD.dll
├── TorpedoLoadout.dll
└── web/
    ├── missionmap.html
    └── toi.html
```

Source is not currently public.

## Requirements

- Wolfpack pre-beta — Steam `testing` or `beta-beta` branch (Unity 2020.3, IL2CPP)
- BepInEx-Unity.IL2CPP-win-x64-6.0.0-pre.2 (the version these mods were built and tested against). Newer bleeding-edge builds from <https://builds.bepinex.dev/projects/bepinex_be> generally work too.
