# 7 Days to Die mods

Small mods for 7 Days to Die, built and tested on **V3.2 (b9/b10)**.

| Mod | Type | Install on | Version |
|---|---|---|---|
| [Remove Entities](#remove-entities) | Code | Server only | 1.1.1 |
| [Remove Any Entity](#remove-any-entity) | Code (Harmony) | Server only | 2.0.0 |
| [Quest Disconnect Fix](#quest-disconnect-fix) | Code (Harmony) | Server only | 1.0.0 |
| [Honk Door Fix](#honk-door-fix) | Code (Harmony) | Server only | 1.0.0 |
| [Speedometer](#speedometer) | Code + UI | Server **and** every player | 1.1.0 |
| [Craft From Chests](#craft-from-chests) | Code (Harmony) | Players only | 1.1.1 |
| [Upgrade Bench](#upgrade-bench) | Code + XML | Server **and** every player | 1.0.1 |
| [Keep Backpacks](#keep-backpacks) | Code | Server only | 1.2.1 |
| [Blood Moon Sound](#blood-moon-sound) | Code (Harmony) | Players (own sound); server optional (sound for everyone, Discord posts) | 1.2.0 |

Download the zips from the Releases page, unzip, and put the folder inside the game's `Mods` folder
(or the server's `Mods` folder). Code that a player installs needs EasyAntiCheat turned off.
Server-only mods do not.

---

## Remove Entities

Uploaded as the existing Remove Zombie Dogs mod. The folder is still `RemoveZombieDogs` and the mod id is still `AzraelRemoveZombieDogs`, so it updates that install. In the game's mod list the name is **Remove Entities**.

Server only. Players install nothing and can leave EasyAntiCheat on.

The first time it runs, zombie dogs, coyotes, dire wolves and screamers stop spawning. After that, an admin chooses the list. The choice is saved in `removed.txt` on the server and applies on the next spawn, with no restart.

| Command | What it does |
|---|---|
| `rement` | Show what is currently removed |
| `rement list` | Every creature class. `rement list dog` narrows it. `rement list all` is every entity class |
| `rement remove <name>` | Stop that class from spawning. `zombieScreamer*` stops every name that starts with that |
| `rement add <name>` | Let it spawn again |

A group that no longer has anything left spawns one of 10 basic zombies (Arlene, Boe, Joe, Marlene, Moe, Darlene, Yo, Steve, Businessman, Janitor). A group that still has a `none` entry keeps that roll, so rare animal groups stay rare. `playerMale`, `playerFemale` and `item` cannot be removed.

Creatures already alive are not deleted. Ones saved in the world stay that creature. The "Kill 50 coyotes" challenge cannot be finished while coyotes are removed.

**Changelog**
- Next: commands to remove or restore any creature without a restart. In-game name is Remove Entities.
- 1.1.1: fix `IsEnemyGroup` NRE spam from `EnemyAnimalsSnowNight` (only `none` left after removal).
- 1.1.0: also remove coyotes, dire wolves, screamers; random zombie replacements.
- 1.0.0: remove zombie dogs (rewrite of CraftManiac's old CSV-based mod for the new `<e n=... p=...>` format).

## Remove Any Entity

(Formerly **Remove Zombie Dogs**.) Pick which zombies and animals stop spawning. `entities.txt` in the
mod folder lists every vanilla entity that spawns naturally, all commented out; remove the `#` in front
of a name to take it out of the game, then restart.

```
#animalZombieDog          zombie dog         <- still spawns
animalCoyote              coyote             <- removed
```

- A name also removes its tiered versions: `zombieScreamer` removes the Feral, Radiated, Charged and
  Infernal screamers too. List a full name (`zombieScreamerRadiated`) to remove just one version.
  Names from other mods work as well.
- Applies to every spawn group (biomes, POI sleepers, wandering and blood moon hordes, screamer/heat
  scouts) as the game loads `entitygroups.xml`, and to Twitch spawn actions. Entities already in the
  world stay, and admins can still `spawnentity` anything.
- Remaining chances are rescaled, so the rest of a group spawns in the removed entities' place.
  A group left with nothing real to spawn gets a random pick from `Replacement=` (10 basic zombies by
  default); its original "nothing spawns" chance is kept, so rare spawns stay rare.
- `none` entries are never left first in a group, which avoids the `EntityGroups.IsEnemyGroup` NRE
  from the old v1.1.0 snow-biome bug.

**Server only**: spawning is decided by the server, so players install nothing. In single player it
goes in your own Mods folder.

| Server console | What it does |
|---|---|
| `rae` | What's removed, how many groups changed, typos in `entities.txt` |
| `rae group <name>` | What a spawn group can spawn now, with chances (partial names are suggested) |

Upgrading from Remove Zombie Dogs: delete the old `RemoveZombieDogs` folder, install this one, and
uncomment `animalZombieDog`, `animalCoyote`, `animalDireWolf` and `zombieScreamer` for the same result.

Known side effect: challenges that need a removed creature (e.g. "Kill 50 coyotes") can't be completed.

This is a separate mod from Remove Entities. Remove Entities changes the list in game with `rement`
and keeps the old mod id. Remove Any Entity reads `entities.txt` and uses its own mod id.

**Changelog**
- 2.0.0: renamed to Remove Any Entity. Now a code mod driven by `entities.txt` (any entity, all tiers,
  other mods' entities), with `rae` / `rae group` console commands and typo warnings.
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

## Honk Door Fix

Nexus: https://www.nexusmods.com/7daystodie/mods/12926

Fixes a vanilla bug that can put a dedicated server into an endless error loop.

Vanilla bug: honking a vehicle horn fires the `honk_trader_doors` game event, whose `BlockDoorState`
action scans blocks around the vehicle (`-5,0,-5` to `5,3,5`). `ActionBaseBlockAction.OnPerformAction`
skips positions below y 0 but never checks the top of the world (255), so a honk at Y 253+ reads past
the chunk and throws `IndexOutOfRangeException` in `Chunk.GetBlockNoDamage`. The event never finishes,
so it throws again every tick.

The mod (Harmony) trims the scan to the world height before it runs (nothing above 255 exists anyway),
and as a safety net ends any block-scan event that still reads out of range. Honking at traders works
exactly as in vanilla. It covers every block-scan game event, not just the honk.

Tested on V3.2 (local dedicated server): unpatched, honking in a gyrocopter at Y 267 produced 1,906
exceptions; patched, the same honks at Y 255-263 produced none, and honking at Trader Jen still
opened the gate. (An XML `InPOI tags="trader"` guard was considered and rejected: no vanilla trader
POI has a `trader` tag, so it would stop honking from opening any trader door.)

**Server only**; players install nothing.

**Changelog**
- 1.0.0: first version.

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

## Craft From Chests

While a crafting screen is open, the recipe list, "have" counts, and the craft button also count
items in player-placed chests inside the land claim you are standing in. Covered screens: backpack,
workbench, chemistry station, cement mixer, and campfire. The forge is unchanged (it has its own
material slots).

A recipe that also needs a quality item still takes the stackable parts from those chests. A gyro
needs a small engine (that stays in your backpack) and wheels (those come from the chests). Tools,
weapons, and armor are never taken.

Upgrading a block with a repair tool uses the same chests for its material (wood, cobblestone,
concrete, steel, and so on). With the Upgrade Bench mod installed, that bench's material cost comes
from the same chests too. The two mods you put in the bench still have to be in the bench.

If your backpack is short, the missing materials move from those chests into your backpack first,
then the normal craft or upgrade runs. On a multiplayer server the mod waits for the server to
confirm each chest change. If someone else has that chest open, the server refuses the change, nothing
is crafted or upgraded, and anything already taken stays in your backpack.

`settings.txt` (each player's own copy; restart after editing): `Enabled=on`, `AllowAllies=on`
(chests in an ally's claim count too), `BackpackCrafting=on`.

**Only players need it; the server doesn't.** Players launch without EAC.

**Changelog**
- 1.1.1: 7 Days 3.3. Chest contents are read from ItemGrid, and item type is a property, so opening a workstation no longer throws and chest materials count again.
- 1.1.0: gyro crafts take stackable parts such as wheels from chests even when the recipe also needs a quality item. Block upgrades, and the Upgrade Bench material cost, use those chests too.
- 1.0.0: first version.

## Upgrade Bench

Adds an Upgrade Bench, crafted at a workbench. Put two of the same weapon, armor, tool, or vehicle
mod in it, at the same quality, plus the materials listed in `upgrades.txt`. Both mods and the
materials are used, and one mod comes back one quality level higher, up to Q6.

The block and recipe have to be on the server (clients receive them automatically). The upgrade
itself runs on each player's PC, and `upgrades.txt` is read from that player's copy of the mod, so
every player installs it too. Restart after editing `upgrades.txt`. Typos in item names show up in
the game log.

**Changelog**
- 1.0.1: 7 Days 3.3. The upgraded mod keeps its mod slots. ItemValue no longer has a public Modifications property.
- 1.0.0: first version.

## Blood Moon Sound

Plays a sound clip of your choice when the blood moon horde starts (or as a warning beforehand).
It can also play a clip when you enter the world, a morning bell at dawn, and a night bell at dusk.
Dawn and dusk follow this world's day length, so a short day still rings at sunrise and sunset.
The mod ships **without** sounds: drop `bloodmoon.ogg`, `.wav` or `.mp3` into the mod folder, and the
same for `spawn`, `morning`, and `night`. A missing bell file means that bell stays quiet.

| Command (F1 console) | What it does |
|---|---|
| `bmsound` | Show status (sound loaded, when it plays, next blood moon day) |
| `bmsound test` | Play the blood moon clip now |
| `bmsound test spawn` | Play the clip that runs when you enter the world |
| `bmsound test morning` | Play the morning bell |
| `bmsound test night` | Play the night bell |
| `bmsound reload` | Re-read `settings.txt` and the sound files |

`settings.txt`: `PlayAt=horde|warning|both`, `WarningHour=21`, `Volume=1.0`,
`Spawn=on|off`, `Morning=on|off`, `Night=on|off`.
The blood moon clip won't play if you join in the middle of a blood moon, only when one starts while
you're in game. The spawn clip plays once each time you enter a world. Dying and respawning in the
same session does not play it again.

**Only players need it for the sound; the server doesn't.** Players launch without EAC.

Any `bloodmoon.*`, `spawn.*`, `morning.*`, or `night.*` file in `BloodMoonSound/mod/` is gitignored.
`build.ps1` leaves them out of the public zip and makes a second `-with-sound` zip (private, for your
own group) which `-Install` uses.

### Horde sound for everyone (server only, on by default)

With the mod on a **dedicated server**, when the horde starts the server tells every player's game to
play one built-in sound (`alarm1_oneshot` by default), so **everyone hears something, even players
without the mod**.

- Players **with** the mod **and** their own `bloodmoon.*` clip hear only their own clip: around horde
  start (±15 game minutes) the mod skips the server's sound and plays their clip instead. Outside that
  window, or with no clip loaded, or with `PlayAt=warning`, the server's sound plays normally.
- How it tells them apart: the server sends the name as `AzraelBloodMoon/alarm1_oneshot`. The vanilla
  game drops the "folder" part of sound names, so players without the mod just hear `alarm1_oneshot`;
  the mod spots the `AzraelBloodMoon/` prefix. Uses the game's own `NetPackageAudioPlayInHead`.
- Server settings (in the server's `settings.txt`): `HordeSound=on|off`,
  `HordeSoundName=<any name from Data\Config\sounds.xml>`.
- `bmdiscord sound` plays it for everyone online right now (a test; it's outside the horde window,
  so modded players hear the game sound too).

### Discord posts (server only, optional)

Put the same mod on a **dedicated server** and it posts blood moon updates to a Discord channel
through a webhook. Players don't need the mod for this, and the sound part does nothing on the server.

The whole schedule is set in `discord.txt`:

| Setting | Default | What it does |
|---|---|---|
| `Reminder = <days before> <hour> \| <message>` | 1 day before at 12:00, and 18:00 on the day | Any number of reminders before the blood moon, one per line (`Reminder=none` for none) |
| `Countdown` / `CountdownHour` / `CountdownFromDays` | off / 8 / 7 | A post every in-game day, "N days until the Blood Moon" |
| `Start` | on | When the horde starts |
| `UpdateEveryHours` | 0 (off) | Extra posts every N in-game hours while the horde is on |
| `End` | on | At dawn afterwards |
| `MsgCountdown`, `MsgStart`, `MsgUpdate`, `MsgEnd` | see file | Message text |

Placeholders: `{day}` blood moon day, `{days}` days left,
`{players}` who's online, `{count}` how many, `\n` new line.

Reminders and the countdown follow the game's own "next blood moon day", so they work with any
blood moon frequency (a 30-day server gets its reminders before day 30, 60, ...) and with a random range.

Setup: copy `discord.example.txt` to `discord.txt` **on the server**, paste the webhook URL
(Discord: channel settings > Integrations > Webhooks), then `bmdiscord reload` and `bmdiscord test`.
`bmdiscord` lists the schedule and any mistakes in the file (bad lines are listed as PROBLEM).

| Server console | What it does |
|---|---|
| `bmdiscord` | Status: on/off, full schedule, next blood moon day, result of the last post |
| `bmdiscord test` | Post a test message |
| `bmdiscord test reminder <n>` | Post reminder number n now |
| `bmdiscord test countdown\|start\|update\|end` | Post that message now |
| `bmdiscord sound` | Play the horde sound for everyone online now |
| `bmdiscord reload` | Re-read `discord.txt` and `settings.txt` |

- Nothing is posted when the server starts in the middle of a blood moon.
- Start/update/dawn are skipped when nobody is online (`SkipEmptyServer=off` to always post).
- Every successful post is logged as `[BloodMoonDiscord] Posted: ...`.
- Nobody gets pinged unless `Mentions=true`, even if a message contains `@everyone`.
- Posting is asynchronous, so a slow or unreachable Discord never lags the server.
- `discord.txt` holds a secret (anyone with the URL can post there): it's gitignored, and `build.ps1`
  leaves it out of **every** zip, including the private `-with-sound` one.

**Changelog**
- 1.2.0: optional clips for entering the world (`spawn`), dawn (`morning`), and dusk (`night`).
  Dawn and dusk follow the world's day length. `bmsound test` can play each clip.
- 1.1.0: server-side Discord posts with a fully custom schedule (any number of reminders, daily
  countdown, horde start, updates during the horde, dawn) and the `bmdiscord` console command.
  Server plays a vanilla horde sound (`alarm1_oneshot`) for every player; players with their own clip
  hear only theirs. Fixed start/dawn both posting when a time skip lands after midnight.
- 1.0.1: `bmsound` now works on multiplayer servers that don't have the mod.
- 1.0.0: first version.

## Keep Backpacks

Server only. Players install nothing and can leave EasyAntiCheat on. The server sends the bag times when they connect.

Death backpacks **never disappear** by default; other bags last **24 hours** (vanilla: everything 1 hour). Change any of them in `settings.txt`.

| Bag | settings.txt | Vanilla | Default |
|---|---|---|---|
| Player death backpack | `DeathBackpack` | 1 hour | permanent |
| Items you drop on the ground | `DroppedItems` | 1 hour | 24 hours |
| Vehicle storage bag (vehicle picked up/destroyed) | `VehicleBag` | 1 hour | 24 hours |
| Zombie loot bags | `ZombieLoot` | 1 hour | 24 hours |

A plain number is seconds. `24h`, `90m`, `3600s`, and `permanent` also work. `0` makes that bag vanish immediately. The game stores seconds ×20 in a 32-bit number, so anything above 107374182 seconds overflows and the bag vanishes. `permanent` is 100000000 seconds, about 3 years of that area being loaded.

The timer only counts while a player is near enough to keep the bag's area loaded. Opened bags that are emptied still disappear, as in vanilla. Zombie loot stays finite on purpose: every zombie can drop one, so permanent bags would pile up and slow the server.

| Command | Who | What it does |
|---|---|---|
| `kbags` | admin | Show the current times |
| `kbags reload` | admin | Re-read `settings.txt` and apply it. No server restart. |
| `kbags password <word>` | admin | Set the password `kbclear` requires. Saved in `password.txt` on the server. |
| `kbclear <password>` | anyone with the password | Remove dropped-item bags and loose items on the ground |

`kbclear` does not remove death backpacks, zombie loot bags, or vehicle storage bags. It only removes things in areas that are loaded right then. Bags in unloaded areas stay until someone goes there.

People already connected keep the old times until they rejoin. Anyone who connects after `kbags reload` gets the new times. There is no command that reloads a mod DLL. Replacing this mod's code still needs a server restart. `kbags reload` only refreshes the bag times.

**Changelog**
- 1.2.1: works on game version 3.3 (3.3 removed the old backpack timer, which made the mod error at start-up). Times live in `settings.txt`; `kbags reload` applies them without a restart; `kbclear` removes dropped items in loaded areas.
- 1.2.0: dropped items and vehicle storage bags last 24 hours (were permanent).
- 1.1.0: zombie loot bags last 24 hours (was vanilla 1 hour).
- 1.0.0: death backpacks, dropped items and vehicle storage bags never despawn.

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

When bumping a version, update both `ModInfo.xml` and the `.csproj` `<Version>`, and the `version` field in that mod's `mod.json`.

Each mod folder has a `mod.json` for the public mod website. It is not copied into the game.

## Releasing

Actions → **Release & Publish** builds one mod (or all of them) and can open a GitHub Release
tagged `Name-vVERSION` and upload that zip to Nexus. The version is the `Version` in `ModInfo.xml`.
A version that is already on GitHub or Nexus is skipped.

The build job runs on a self-hosted Windows runner labeled `7d2d` (plus the default
`self-hosted`, `Windows`, and `X64` labels). It needs a 7 Days to Die install, the same way the
Sun Haven runners need the game mounted: GitHub-hosted runners do not have `Assembly-CSharp.dll`.
The default path is in `Directory.Build.props`. A runner on another machine sets the repository
variable `SEVEND2D_DIR`.

Nexus uploads use the secret `NEXUSMODS_API_KEY` and the file id from the mod's Files tab
(Advanced), stored as `nexus_file_id` in `scripts/matrix/mod-matrix.json`. Remove Any Entity
has no Nexus page yet, so it only gets a GitHub Release.

The same run publishes the zip to the mods website
(`downloads.azraelsmods.com/7-days-to-die/<slug>/<version>/<slug>-<version>.zip`) and records
that version in `downloads.json`. That needs `CLOUDFLARE_API_TOKEN` and `CLOUDFLARE_ACCOUNT_ID`
on this repo. Without them the release still continues and the website file stays unchanged.
