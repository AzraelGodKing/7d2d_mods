# 7 Days to Die mods

Small mods for 7 Days to Die, built and tested on **V3.2 (b9/b10)**.

| Mod | Type | Install on | Version |
|---|---|---|---|
| [Remove Zombie Dogs](#remove-zombie-dogs) | XML only | Server (clients get it automatically) | 1.1.1 |
| [Quest Disconnect Fix](#quest-disconnect-fix) | Code (Harmony) | Server only | 1.0.0 |
| [Speedometer](#speedometer) | Code + UI | Server **and** every player | 1.1.0 |
| [Keep Backpacks](#keep-backpacks) | XML only | Server (clients get it automatically) | 1.2.0 |
| [Blood Moon Sound](#blood-moon-sound) | Code (Harmony) | Players (sound); server optional (Discord posts) | 1.1.0 |

Download the zips from the Releases page, unzip, and put the folder inside the game's `Mods` folder
(or the server's `Mods` folder). Mods with code need EasyAntiCheat turned off.

---

## Remove Zombie Dogs

Removes zombie dogs, coyotes, dire wolves and screamers from every spawn group: biome spawns,
POI sleepers, wandering hordes, blood moon hordes, screamer/heat scouts and Twitch spawn actions.

- Groups left empty get a random pick of 10 basic zombies (Arlene, Boe, Joe, Marlene, Moe,
  Darlene, Yo, Steve, Businessman, Janitor).
- Groups whose first entry would become `none` get the same zombies inserted first (low weight), which
  avoids a `NullReferenceException` in `EntityGroups.IsEnemyGroup` (the v1.1.0 snow-biome bug).
- Twitch actions that spawned these animals by class name spawn a random basic zombie instead.

Known side effect: the "Kill 50 coyotes" challenge can't be completed.

**Changelog**
- 1.1.1: fix `IsEnemyGroup` NRE spam from `EnemyAnimalsSnowNight` (only `none` left after removal).
- 1.1.0: also remove coyotes, dire wolves, screamers; random zombie replacements.
- 1.0.0: remove zombie dogs (rewrite of CraftManiac's old CSV-based mod for the new `<e n=... p=...>` format).

## Quest Disconnect Fix

Fixes **ghost players** on dedicated servers.

Vanilla bug: on logout, `QuestEventManager.HandlePlayerDisconnect` calls `QuestUnlockPOI` for each
in-progress quest. If a quest's saved POI position doesn't match any POI, it dereferences null and
throws, `ConnectionManager.DisconnectClient` aborts halfway, and the player is never removed. They stay
in the world, the server keeps pinging them (`Ping requested for unknown client`), rejoining fails with
`Duplicate player ID`, and new players can't get in until a restart.

The mod:
- skips the unlock when there's no POI at that position (logs a `[QuestDisconnectFix] Skipped POI unlock` warning), and
- as a safety net, catches any other exception in the quest cleanup so the logout always finishes.

Server-side only; players install nothing.

## Speedometer

Shows your speed on the HUD, bottom right above the vehicle gas/health bars.

| Key / command (F1 console) | What it does |
|---|---|
| **F5** | Show / hide |
| **F2** | Switch MPH / KM/H |
| click it (inventory open) | Switch MPH / KM/H |
| `speedo` | Show current settings |
| `speedo off` / `on` / `always` | Hidden / in vehicles only (default) / on foot too |
| `speedo mph` / `kmh` / `unit` | Pick or switch the unit |
| `speedo key hide <key>` / `speedo key unit <key>` | Rebind a hotkey (`none` turns it off) |
| `speedo key reset` | Back to F5 / F2 |

F2 and F5 have no default binding in V3.2, and the game won't let players bind F-keys in Options,
so they can't clash with game controls. Hotkeys are ignored while typing, in menus, or paused.

Speed is measured from how far you (or your vehicle) actually moved, smoothed every 0.1 s, so it
works for drivers, passengers and on foot, and ignores teleports. Settings are saved per player in
`%APPDATA%\7DaysToDie\AzraelSpeedometer.txt`.

**Only players need it; the server doesn't.** Players launch without EAC.

**Changelog**
- 1.1.1: `speedo` now works on multiplayer servers that don't have the mod (runs on the player's PC).
- 1.1.0: F5/F2 hotkeys (rebindable), on-screen confirmation, moved above the vehicle gauges.
- 1.0.0: first version.

## Blood Moon Sound

Plays a sound clip of your choice when the blood moon horde starts (or as a warning beforehand).
The mod ships **without** a sound: drop `bloodmoon.ogg`, `.wav` or `.mp3` into the mod folder.

| Command (F1 console) | What it does |
|---|---|
| `bmsound` | Show status (sound loaded, when it plays, next blood moon day) |
| `bmsound test` | Play the sound now |
| `bmsound reload` | Re-read `settings.txt` and the sound file |

`settings.txt`: `PlayAt=horde|warning|both`, `WarningHour=21`, `Volume=1.0`.
It won't play if you join in the middle of a blood moon, only when one starts while you're in game.

**Only players need it for the sound; the server doesn't.** Players launch without EAC.

Any `bloodmoon.*` file in `BloodMoonSound/mod/` is gitignored. `build.ps1` leaves it out of the
public zip and makes a second `-with-sound` zip (private, for your own group) which `-Install` uses.

### Discord posts (server only, optional)

Put the same mod on a **dedicated server** and it posts blood moon updates to a Discord channel
through a webhook. Players don't need the mod for this, and the sound part does nothing on the server.

| Message | Default | Default text |
|---|---|---|
| Day before | 12:00 the day before | Blood Moon **tomorrow night** (day N). Get your base ready! |
| Warning | 18:00 on blood moon day | Blood Moon **tonight** (day N). The horde comes at dusk. |
| Start | when the horde starts | The Blood Moon has risen! Horde incoming. Online: *names* |
| End | at dawn afterwards | Dawn! Blood Moon day N survived. Online: *names* |

Setup: copy `discord.example.txt` to `discord.txt` **on the server**, paste the webhook URL
(Discord: channel settings > Integrations > Webhooks), then `bmdiscord reload` and `bmdiscord test`.
Every message can be turned off, retimed or reworded in `discord.txt` (`{day}`, `{players}`, `{count}`).

| Server console | What it does |
|---|---|
| `bmdiscord` | Status: on/off, messages, next blood moon day, result of the last post |
| `bmdiscord test` | Post a test message |
| `bmdiscord test daybefore\|warning\|start\|end` | Post that message now |
| `bmdiscord reload` | Re-read `discord.txt` |

- Nothing is posted when the server starts in the middle of a blood moon.
- Start/dawn are skipped when nobody is online (`SkipEmptyServer=false` to always post).
- Nobody gets pinged unless `Mentions=true`, even if a message contains `@everyone`.
- Posting is asynchronous, so a slow or unreachable Discord never lags the server.
- `discord.txt` holds a secret (anyone with the URL can post there): it's gitignored, and `build.ps1`
  leaves it out of **every** zip, including the private `-with-sound` one.

**Changelog**
- 1.1.0: server-side Discord posts (day before, warning, horde start, dawn) with `bmdiscord` console command.
- 1.0.1: `bmsound` now works on multiplayer servers that don't have the mod.
- 1.0.0: first version.

## Keep Backpacks

Death backpacks **never disappear**; other bags last **24 hours** (vanilla: everything 1 hour).

| Bag | Vanilla | With this mod |
|---|---|---|
| Player death backpack | 1 hour | permanent |
| Items you drop on the ground | 1 hour | 24 hours |
| Vehicle storage bag (vehicle picked up/destroyed) | 1 hour | 24 hours |
| Zombie loot bags | 1 hour | 24 hours |

- "Permanent" is `TimeStayAfterDeath = 100000000` seconds. The game stores this ×20 in a 32-bit int, so
  bigger values overflow and the bag vanishes instantly (and `0` also means instantly). 100M seconds is
  ~3 years of the area being loaded, which is effectively forever.
- The vanilla timer only counts while a player is near enough to keep the bag's area loaded.
- Opened bags that are emptied still disappear, as in vanilla.
- Zombie loot bags get 24 hours instead of permanent on purpose: every zombie can drop one, so
  permanent bags would pile up by the thousands and slow the server.

**Changelog**
- 1.2.0: dropped items and vehicle storage bags last 24 hours (were permanent).
- 1.1.0: zombie loot bags last 24 hours (was vanilla 1 hour).
- 1.0.0: death backpacks, dropped items and vehicle storage bags never despawn.

No `serverconfig.xml` setting controls this; the timers live in `entityclasses.xml`.

---

## Rules for new mods

- **If a mod doesn't need to be on the server, it must work without being on the server.**
  Client-only mods (HUD, sounds, etc.) must never require the server to install them.
- **Console commands in client-only mods:** on a multiplayer server the game sends *every* F1
  command to the server, which answers "Unknown command" if it doesn't have the mod. Client-only
  mods therefore Harmony-prefix `GUIWindowConsole.EnterCommand` and run their own command names
  locally with `SdtdConsole.ExecuteSync(cmd, null)` when `ConnectionManager.IsClient` is true.
  See `BmsConsolePatch` (BloodMoonSound) or `SpeedoConsolePatch` (Speedometer); copy that pattern
  and change only the command names. Skip the patch on dedicated servers.
- Keep copyrighted assets (sounds, images) out of git and out of public zips (see Blood Moon Sound).

## Building

Requirements: .NET SDK (any recent version; builds `net48`) and a 7 Days to Die install.
The game's DLLs are **not** in this repo; the projects reference them from your install.

```powershell
.\build.ps1                     # build everything into .\dist (folders + release zips)
.\build.ps1 -Install            # also copy into the game's Mods folder
.\build.ps1 -Only Speedometer   # one mod
.\build.ps1 -GameDir "D:\Games\7 Days To Die"
```

The default game path is in `Directory.Build.props`. To change it on one PC without editing that
file, create `Directory.Build.local.props` (gitignored):

```xml
<Project><PropertyGroup><GameDir>D:\Games\7 Days To Die</GameDir></PropertyGroup></Project>
```

### Layout

```
<Mod>/mod/   files that ship as-is (ModInfo.xml, Config/...)
<Mod>/src/   C# source + .csproj (code mods only); the built .dll is added to the shipped folder
```

When bumping a version, update both `ModInfo.xml` and the `.csproj` `<Version>`.
