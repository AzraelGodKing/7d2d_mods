# 7 Days to Die mods

Small mods for 7 Days to Die, built and tested on **V3.2 (b9/b10)**.

| Mod | Type | Install on | Version |
|---|---|---|---|
| [Remove Zombie Dogs](#remove-zombie-dogs) | XML only | Server (clients get it automatically) | 1.1.1 |
| [Quest Disconnect Fix](#quest-disconnect-fix) | Code (Harmony) | Server only | 1.0.0 |
| [Speedometer](#speedometer) | Code + UI | Server **and** every player | 1.1.0 |

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

Install on the server **and** every player's game (the HUD runs client-side). Players launch without EAC.

**Changelog**
- 1.1.0: F5/F2 hotkeys (rebindable), on-screen confirmation, moved above the vehicle gauges.
- 1.0.0: first version.

---

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
