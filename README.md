# 7 Days to Die mods

Small mods for 7 Days to Die. Every mod builds against **V3.4** (b6), and each one's Harmony patches and XML were checked against it.

| Mod | Type | Install on | Version |
|---|---|---|---|
| [Remove Any Entity](#remove-any-entity) | Code (Harmony) | Server only | 2.2.0 |
| [Quest Disconnect Fix](#quest-disconnect-fix) | Code (Harmony) | Server only | 1.1.0 |
| [Honk Door Fix](#honk-door-fix) | Code (Harmony) | Server only | 1.0.0 |
| [Craftable Canvas Signs](#craftable-canvas-signs) | XML only | Server only | 1.0.0 |
| [Speedometer](#speedometer) | Code + UI | Players only | 1.1.1 |
| [Craft From Chests](#craft-from-chests) | Code (Harmony) | Players only | 1.2.0 |
| [Upgrade Bench](#upgrade-bench) | Code + XML | Server **and** every player | 1.0.2 |
| [No Ding Sound](#no-ding-sound) | XML only | Server only | 1.0.0 |
| [Keep Backpacks](#keep-backpacks) | Code (Harmony) | Server only | 1.3.0 |
| [Blood Moon Sound](#blood-moon-sound) | Code (Harmony) | Players (own sound, on-screen countdown); server optional (sounds for everyone, Discord and game chat posts, records, `/bm`) | 1.5.0 |
| [Zombie and Animal Bait](#zombie-and-animal-bait) | Code (Harmony) + XML | Server only | 1.12.1 |
| [Gyro Safe](#gyro-safe) | Code (Harmony) | Server and players who fly | 1.0.0 |
| [Drone Loot Filter](#drone-loot-filter) | Code (Harmony), needs Drone Automation | Server only | 1.2.0 |
| [Bait UI](#bait-ui) | Code + UI | Players only (optional, needs Bait on the server) | 1.9.0 |
| [Spawn Tuner](#spawn-tuner) | Code (Harmony) | Server only | 1.1.0 |
| [Slot Lock Fix](#slot-lock-fix) | Code (Harmony) | Players only | 1.0.0 |
| [K9 Death Sound](#k9-death-sound) | XML (+ optional own clip) | Server; players optional | 1.1.0 |

Ideas not built yet are kept in [docs/ideas.md](docs/ideas.md).

**Mod manager:** the Windows mod manager is now [Survivor's Toolkit](https://github.com/AzraelGodKing/Survivor-s-Toolkit), in its own repository.

Download the zips from the Releases page, unzip, and put the folder inside the game's `Mods` folder
(or the server's `Mods` folder). Code that a player installs needs EasyAntiCheat turned off.
Server-only mods do not.

---

## Remove Any Entity

Formerly **Remove Zombie Dogs** (later **Remove Entities**), and the same Nexus page. Pick which zombies and animals stop spawning. `entities.txt` in the
mod folder lists every vanilla entity that spawns naturally, all commented out; remove the `#` in front
of a name to take it out of the game, then type `rae reload` in the server console (or restart).

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
| `rae reload` | Re-read `entities.txt` and apply it now, without a restart |
| `rae remove <name>` | Stop `<name>` (and its tiers) spawning: saves it to `entities.txt` and applies it now |
| `rae remove <name> in <biomes>` | Stop `<name>` only in the open-world spawns of those biomes, e.g. `rae remove animalCoyote in desert, wasteland` |
| `rae replace <name> <other>` | Spawn `<other>` wherever `<name>` would have, at the same chance, e.g. `rae replace animalZombieDog animalWolf` |
| `rae keep <name>` | Let `<name>` spawn again (undoes remove, remove-in and replace): comments its line out and applies it now |
| `rae cull [<name>]` | Despawn creatures already out that the rules no longer allow, in loaded areas (or just `<name>` and its tiers) |
| `rae cull count [<name>]` | Only count what `rae cull` would despawn |

The same rules can be written in `entities.txt`:

```
animalCoyote in desert, wasteland     removed from those biomes only
animalZombieDog -> animalWolf         every zombie dog spawn becomes a wolf
```

- **Per biome** only changes open-world (biome) spawns. Sleepers in buildings, wandering and blood moon
  hordes aren't tied to a biome, so they keep it. Biomes: `pine_forest` (or `forest`), `burnt_forest`
  (or `burnt`), `desert`, `snow`, `wasteland`. When a biome spawn rolls a creature removed there, it
  rolls again; if nothing else comes up, that spot stays empty.
- **Replace** keeps a group zombies-first or animals-first as it was, so swapping a zombie for an animal
  doesn't change which spawner uses the group. The target must exist and not be removed itself.
- **Cull** never touches players, vehicles or Bait thralls.

Live changes affect new spawns only: creatures already in the world stay, and a horde that has already
picked its zombies keeps them. `rae reload` rebuilds the groups from what `entitygroups.xml` defined, so
another mod that changes spawn groups after they load (rather than through XML) has those changes undone
until the next restart.

Upgrading from Remove Zombie Dogs / Remove Entities: this version has its own mod id, so delete the
old `RemoveZombieDogs` folder from `Mods` first (running both would remove creatures twice). The old
`removed.txt` / `rement` list doesn't carry over: uncomment the same names in `entities.txt`
(`animalZombieDog`, `animalCoyote`, `animalDireWolf` and `zombieScreamer` match the old defaults).

Known side effect: challenges that need a removed creature (e.g. "Kill 50 coyotes") can't be completed.

**Changelog**
- 2.2.0: `name -> other` lines and `rae replace` swap one creature for another at the same chance. `name in <biomes>` lines and `rae remove <name> in <biomes>` stop a creature in some biomes only. `rae cull` despawns creatures already out that the rules no longer allow (`rae cull count` just counts); players, vehicles and Bait thralls are skipped.
- 2.1.0: change spawns without a restart. `rae reload` applies an edited `entities.txt` straight away, and `rae remove <name>` / `rae keep <name>` edit the file from the console and apply it.
- 2.0.1: replaces Remove Zombie Dogs / Remove Entities on its Nexus page (the `rement` version is retired). A Twitch spawn whose creatures are all removed, with no usable `Replacement=`, is left unchanged (with one warning), the same as an emptied spawn group. It used to end up empty, which made the action rebuild its list every tick and never finish.
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

It also fixes **parties that stop working** after a robotic drone unloads.

Vanilla 3.3 bug: a drone listens to its owner's party. When it unloads, it clears its member list but
only stops listening if it can still find its owner. When it can't, the next join or leave calls the
dead drone, which throws a `NullReferenceException` in `EntityDrone.onPartyMemberAdded` or
`onPartyMemberRemoved`. That stops `Party.AddPlayer` or `Party.RemovePlayer` halfway, before the party
is sent to the players, so nobody can join or leave that party until a restart.

The mod skips a drone whose list is gone, stops it listening to that party, and logs a
`[QuestDisconnectFix] Skipped an unloaded drone` warning. A live drone works as before.

Server-side only; players install nothing.

**Changelog**
- 1.1.0: fix parties breaking after a robotic drone unloads (3.3). Website is the GitHub repo, matching the other mods. DisplayName is `Quest Disconnect Fix (No Ghost Players and No Broken Parties)` so Nexus will accept the zip (it rejects a comma in the file name).
- 1.0.0: first version.

## Craftable Canvas Signs

**Server only (XML); players install nothing.** The game has picture signs you can draw on with the
in-game sign editor, but only places them in POIs: they are flagged developer-only and have no
recipe. This makes them craftable. There are two families: **canvas** signs (framed) and **decal**
signs (just the picture, no frame; the Doggo's billboard is made of these).

| Sign | Recipe (one craft, any size) |
|---|---|
| Canvas / decal sign, wood | 10 wood, 10 cloth, 5 paint |
| Canvas / decal sign, corrugated iron | 10 forged iron, 10 cloth, 5 paint |

- You pick the size when you place it (up to 13 blocks wide), so a big billboard costs the same as a small sign.
- Tougher than vanilla: 10x the HP (wood 225 -> 2250, iron 300 -> 3000). Only the picture signs change, not other wood or metal blocks. Adjust `MaxDamage` in `Config/blocks.xml`.
- Change the costs in `Config/recipes.xml`.

---
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
- 1.1.1: `speedo` now works on multiplayer servers that don't have the mod (runs on the player's PC). DisplayName is `Speedometer (MPH and KMH)` so Nexus will accept the zip (it rejects a slash in the file name). Website is the GitHub repo, matching the other mods.
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

When `UpgradeFromChests` is on, upgrading a block with a repair tool uses the same chests for its
material (wood, cobblestone, concrete, steel, and so on). With the Upgrade Bench mod installed, that
bench's material cost comes from the same chests too. The two mods you put in the bench still have
to be in the bench. That setting is **off by default**.

If your backpack is short, the missing materials move from those chests into your backpack first,
then the normal craft or upgrade runs. On a multiplayer server the mod waits for the server to
confirm each chest change. If someone else has that chest open, the server refuses the change, nothing
is crafted or upgraded, and anything already taken stays in your backpack. If the server is slow to
answer (more than 10 seconds), those materials aren't handed out yet; they arrive in your backpack
once the server confirms the take.

`settings.txt` (each player's own copy; restart after editing): `Enabled=on`, `AllowAllies=on`
(chests in an ally's claim count too), `BackpackCrafting=on`, `UpgradeFromChests=off` (turn on to let block upgrades and the Upgrade Bench material cost use chests too).

**Only players need it; the server doesn't.** Players launch without EAC.

**Changelog**
- 1.2.0: using chests for block upgrades and the Upgrade Bench is now a setting, `UpgradeFromChests`, and it is **off by default**. Set it to `on` in `settings.txt` to get the 1.1.x behavior back. ModInfo, site descriptions, and the README how-it-works paragraph match: crafting uses claim chests; upgrades only when that setting is on.
- 1.1.2: fixed items duplicating on a laggy server. The mod used to stop waiting for the server after 3 seconds and hand out the materials anyway; if the server then refused, the chest got them back too. It now waits up to 10 seconds, and materials from a chest the server still hasn't answered arrive only once the server accepts.
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
- 1.0.2: forged-iron mods (Rad Remover, Barbed Wire, Metal Chain, Grave Digger, Bunker Buster, Iron Breaker, Wood Splitter) cost 4 iron for Q1→Q2, not 10, matching the other steps (4, 6, 8, 10, 12).
- 1.0.1: 7 Days 3.3. The upgraded mod keeps its mod slots. ItemValue no longer has a public Modifications property.
- 1.0.0: first version.

## Blood Moon Sound

Plays a sound clip of your choice when the blood moon horde starts (or as a warning beforehand).
It can also play a clip when you enter the world, a morning bell at dawn, a night bell at dusk, and a
"survived" clip at the dawn after a blood moon (instead of the morning bell that day).
Dawn and dusk follow this world's day length, so a short day still rings at sunrise and sunset.
The mod ships **without** sounds: drop `bloodmoon.ogg`, `.wav` or `.mp3` into the mod folder, and the
same for `spawn`, `morning`, `night` and `survived`. A missing bell file means that bell stays quiet.

Several clips of one kind play at random, never the same one twice in a row: name them
`bloodmoon.ogg`, `bloodmoon2.ogg`, `bloodmoon-scary.wav` and so on (any name that starts with
`bloodmoon`, `spawn`, `morning`, `night` or `survived`). `BloodMoonVolume`, `SpawnVolume`, `MorningVolume`,
`NightVolume` and `SurvivedVolume` in `settings.txt` set each kind's volume on top of `Volume`.

| Command (F1 console) | What it does |
|---|---|
| `bmsound` | Show status (sound loaded, when it plays, next blood moon day and how long until the horde) |
| `bmsound test` | Play the blood moon clip now |
| `bmsound test spawn` | Play the clip that runs when you enter the world |
| `bmsound test morning` | Play the morning bell |
| `bmsound test night` | Play the night bell |
| `bmsound test survived` | Play the survived clip |
| `bmsound test banner` | Show the warning banner (and the red pulse, if it's on) |
| `bmsound ui` | Open the settings window (same as the `Insert` key) |
| `bmsound reload` | Re-read `settings.txt` and the sound files |

`settings.txt`: `PlayAt=horde|warning|both`, `WarningHour=21`, `Volume=1.0`,
`Spawn=on|off`, `Morning=on|off`, `Night=on|off`, `Survived=on|off`.
While a morning or night bell is on and loaded, the game's own dawn or dusk sound stays quiet so
only the bell plays. A `WarningHour` at or after dusk plays one hour before dusk.
The blood moon clip won't play if you join in the middle of a blood moon, only when one starts while
you're in game. The spawn clip plays once each time you enter a world. Dying and respawning in the
same session does not play it again.

### On screen (players)

- **Countdown** (`Hud=on`): a "Blood Moon in 1d 6h" line near the top of the screen, from `HudFromDays=7`
  days out (`0` = always). Orange on blood moon day, red in the last in-game hour, and "dawn in ..." while
  the horde is on. Hidden while a menu is open or the HUD is off. `HudX`, `HudY` (share of the screen) and
  `HudSize` move and size it.
- **Banner** (`Banner=on`): big red text across the screen at `WarningHour` on blood moon day
  ("BLOOD MOON IN 1 HOUR") and when the horde starts.
- **Red pulse** (`Pulse=off` by default, `PulseStrength=0.6`): a red glow around the screen edge that
  pulses once when the horde starts.
- **Settings window**: press `Insert` (`SettingsKey=` in `settings.txt`, `none` for no key) or type
  `bmsound ui`. Volumes, which clips are on, `PlayAt`, `WarningHour`, the on-screen options and a Test
  button for each clip. **Save** writes them to `settings.txt` and keeps its comments.

All of it is drawn with Unity's own immediate-mode GUI, so there are no XUi files to clash with UI mods.

**Only players need it for the sound; the server doesn't.** Players launch without EAC.

Any `bloodmoon*`, `spawn*`, `morning*`, `night*` or `survived*` .ogg/.wav/.mp3 in `BloodMoonSound/mod/` is gitignored.
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
- The same happens at `WarningHour` on blood moon day (`WarningSound=on`, `horde_spawn_warning`) and at
  dawn after a blood moon (`DawnSound=on`, `quest_master_complete`). Players with the mod and their own
  clip for that moment (`PlayAt=warning|both`, or a `survived` / `morning` clip) hear only theirs.
  `bmdiscord sound warning|dawn` tests them.

### Discord posts (server only, optional)

Put the same mod on a **dedicated server** and it posts blood moon updates to a Discord channel
through a webhook. Players don't need the mod for this, and the sound part does nothing on the server.

The whole schedule is set in `discord.txt`:

| Setting | Default | What it does |
|---|---|---|
| `Reminder = <days before> <hour> \| <message>` | 1 day before at 12:00, and 18:00 on the day | Any number of reminders before the blood moon, one per line (`Reminder=none` for none) |
| `Countdown` / `CountdownHour` / `CountdownFromDays` | off / 8 / 7 | A post every in-game day, "Blood Moon in 3 days 14 hours" |
| `Start` | on | When the horde starts |
| `UpdateEveryHours` | 0 (off) | Extra posts every N in-game hours while the horde is on |
| `End` | on | At dawn afterwards |
| `Recap` | on | Adds a horde recap to the dawn post: zombies killed, kills per player, who died (`{recap}` places it) |
| `ServerStart` / `ServerStop` | off / off | When the server is up, and as it shuts down |
| `PlayerJoin` / `PlayerLeave` | off / off | When a player joins or leaves (`{player}`) |
| `Embeds` | on | Coloured Discord embeds: orange reminders, red horde, gold dawn. `off` = plain text |
| `InGameChat` / `InGameChatFor` | on / reminder, countdown, start, end | Show those messages in game chat too (no emoji or bold there). Works without a webhook |
| `LiveCountdown` / `LiveEveryHours` | on / 1 | One Discord message per blood moon for the reminders and countdown, edited instead of re-posted; the time left refreshes every N in-game hours. At horde start it changes to `MsgLiveDone` |
| `DeathAlerts` | on | A post when a player dies during the horde (`{player}`, `{clock}`, `{kills}`, `{standing}`) |
| `LastStanding` | on | Once per horde, when everyone online but one has died (needs 2+ online) |
| `Awards` | on | MVP, First Blood, First to Fall, Untouchable and special zombies killed, in the recap |
| `History` | on | "New server record" in the recap when a horde beats the most kills ever on this world |
| `Streaks` | on | Blood moons in a row each player survived without dying, and streaks that ended |
| `Leaderboard` / `LeaderboardDay` / `LeaderboardHour` | on / sunday / 18 | A weekly leaderboard (server PC's clock): most horde kills, most survived, longest streak |
| `ScheduleAlerts` | on | When the blood moon day moves before it comes, or time is skipped past it (`MsgMoved`, `MsgSkipped`, `{oldday}`) |
| `ChatCommand` | on | Players type `/bm` in game chat: blood moon day and time left, privately. `/bm me` their record, `/bm top` the leaderboard |
| `MsgCountdown`, `MsgStart`, `MsgUpdate`, `MsgEnd` | see file | Message text |

Placeholders: `{day}` blood moon day, `{timeleft}` time left in game time ("1 day 6 hours", "45 minutes"),
`{realtime}` the same in real time ("1h 15m", from the server's day length), `{hours}` the same in whole in-game hours,
`{days}` calendar days left, `{starts}` / `{dawn}` the horde's start and end time ("22:00", "04:00"),
`{players}` who's online, `{count}` how many, `{player}` who joined or left, `{recap}` the horde recap, `\n` new line,
`{when}` a Discord timestamp ("in 1 hour", counting down by itself in each reader's Discord), `{whenat}` the real clock
time in each reader's own time zone, `{clock}` the in-game time now. `{when}`/`{whenat}` don't work in the headline line,
and game chat shows them as "in 1h 15m".
Before the horde, `{timeleft}` counts down to it; in the start and update messages it counts down to dawn.

Every default message is a short headline and a detail line, for example:

> **⚠️ Blood Moon tomorrow · Day 14**
> Horde in **1 day 10 hours** at 22:00 (about 1h 25m real time). Get your base ready!

The first line of a message is the embed title in Discord and the bright red line in game chat; the
rest shows underneath (softer colour in game chat). Embeds also carry a timestamp in each reader's own
time zone. A `discord.txt` that still has the 1.3.0 default messages word for word gets the new wording
on load, so you don't have to edit it; messages you changed yourself are left alone.

Reminders and the countdown follow the game's own "next blood moon day", so they work with any
blood moon frequency (a 30-day server gets its reminders before day 30, 60, ...) and with a random range.

Setup: copy `discord.example.txt` to `discord.txt` **on the server**, paste the webhook URL
(Discord: channel settings > Integrations > Webhooks), then `bmdiscord reload` and `bmdiscord test`.
`bmdiscord` lists the schedule and any mistakes in the file (bad lines are listed as PROBLEM).

| Server console | What it does |
|---|---|
| `bmdiscord` | Status: on/off, full schedule, next blood moon day and time left, result of the last post |
| `bmdiscord test` | Post a test message |
| `bmdiscord test reminder <n>` | Post reminder number n now, with the time left as it will be when it really goes out |
| `bmdiscord test countdown\|start\|update\|end` | Post that message now, timed the same way (`end` includes the recap so far) |
| `bmdiscord test serverstart\|serverstop\|join\|leave` | Post that message now |
| `bmdiscord test death\|laststanding\|leaderboard\|moved\|skipped` | Post that message now |
| `bmdiscord recap` | The horde recap so far, or from the last horde |
| `bmdiscord history` | The last 10 blood moons on this world (kills, deaths) and the all-time best |
| `bmdiscord leaderboard` | Show the leaderboard in the console |
| `bmdiscord sound [warning\|dawn]` | Play the horde (or warning / dawn) sound for everyone online now |
| `bmdiscord reload` | Re-read `discord.txt` and `settings.txt` |

- Nothing is posted when the server starts in the middle of a blood moon.
- Start/update/dawn are skipped when nobody is online (`SkipEmptyServer=off` to always post).
- Every successful post is logged as `[BloodMoonDiscord] Posted: ...`.
- Nobody gets pinged unless `Mentions=true`, even if a message contains `@everyone`. A message that pings goes out as plain text, since Discord doesn't ping from an embed.
- The shutdown post is sent straight away (it waits up to 5 seconds), since the server stops right after.
- Without a `discord.txt` nothing is posted, in Discord or game chat.
- Posting is asynchronous, so a slow or unreachable Discord never lags the server.
- Records, streaks and the live message's id are saved with the world, in
  `Saves\<world>\<game>\AzraelBloodMoon\` (`history.txt`, `players.txt`, `state.txt`), so they carry on
  after restarts and each world keeps its own.
- `/bm` works without a webhook, and even without a `discord.txt`.
- `discord.txt` holds a secret (anyone with the URL can post there): it's gitignored, and `build.ps1`
  leaves it out of **every** zip, including the private `-with-sound` one.

**Changelog**
- 1.5.0: players get an on-screen blood moon countdown, a warning banner, an optional red pulse at horde start, a `survived` clip at the dawn after a blood moon, and an in-game settings window (`Insert` or `bmsound ui`). Servers get a live countdown message that edits itself instead of re-posting, Discord timestamps in each reader's time zone (`{when}`, `{whenat}`), live death alerts, a "last one standing" post, recap awards (MVP, First Blood, First to Fall, Untouchable, special zombies), saved horde history with server records, survival streaks, a weekly leaderboard, `/bm` in game chat, built-in warning and dawn sounds for everyone, and an alert when the blood moon day moves or is skipped.
- 1.4.0: cleaner blood moon messages that say how long is left in hours as well as days: "Blood Moon in 1 day 6 hours", with the horde's start time and the real-time wait. New placeholders `{timeleft}`, `{realtime}`, `{hours}`, `{starts}` and `{dawn}`. Start and update messages count down to dawn. Discord embeds get a title line and a timestamp; game chat shows a bright headline with the details under it. Old 1.3.0 default wording in `discord.txt` is upgraded on load. `bmdiscord`, `bmsound` and `bmdiscord test` show the time left too.
- 1.3.0: players: several clips per kind (`bloodmoon2.ogg`, `bloodmoon-scary.wav`...) play one at random, and each kind has its own volume (`BloodMoonVolume`, `SpawnVolume`, `MorningVolume`, `NightVolume`). Server: horde recap in the dawn post (`Recap`), server start/stop and player join/leave posts, coloured embeds (`Embeds`), and the same messages in game chat (`InGameChat`, `InGameChatFor`). `bmdiscord recap`, and `bmdiscord test` covers the new messages.
- 1.2.2: the morning and night bells replace the game's own dawn and dusk sounds instead of playing on top of them (only while that bell is on and its file is loaded). If `WarningHour` is at or after this world's dusk, the warning plays one hour before dusk instead of never, and the log and `bmsound` say so. `settings.txt` now lists `Spawn`, `Morning` and `Night`. `PUT_YOUR_SOUND_HERE.txt` names those clips too.
- 1.2.1: morning and night no longer play together when a world finishes loading. The enter-world clip waits until the clock has settled. Dawn and dusk also play once per event day, and a second init keeps a single player or Discord object.
- 1.2.0: optional clips for entering the world (`spawn`), dawn (`morning`), and dusk (`night`).
  Dawn and dusk follow the world's day length. `bmsound test` can play each clip.
- 1.1.0: server-side Discord posts with a fully custom schedule (any number of reminders, daily
  countdown, horde start, updates during the horde, dawn) and the `bmdiscord` console command.
  Server plays a vanilla horde sound (`alarm1_oneshot`) for every player; players with their own clip
  hear only theirs. Fixed start/dawn both posting when a time skip lands after midnight.
- 1.0.1: `bmsound` now works on multiplayer servers that don't have the mod.
- 1.0.0: first version.

## Bait UI

Optional client add-on for Zombie and Animal Bait: roster window (Home key), name tags with HP bars, party list, fed tags and an admin window. Players only; needs Bait 1.3.0+ on the server. See [BaitUI/README.md](BaitUI/README.md).

**1.9.0:** cleaner, tabbed roster (Thralls, Groups, Display, Keys, Tools) with a command bar, choosable columns and a window size setting; every per-thrall action on the thrall page; a bestiary, a session summary, a new thrall setup popup and a tame banner; damage numbers, target lines, a downed panel with Revive buttons, colour-blind HP bars; track a thrall on the compass, Alt+1-9 to call or stow, a go-to key, a bait crafting shortcut; grouped kill feed with a filter; German, Spanish, French and Portuguese labels; admin tabs for feed rates, the exclude list and every player's thralls. Parts need Bait 1.12.0. **1.8.0:** thrall page (stats, perks, guard, patrol, scout, evolve, blood moon mode), name search, tooltips on every button, map markers, HP strip, compact party list, coloured rows, Dark/Light/High contrast themes and a taming helper. Parts need Bait 1.10.0. **1.7.0:** filter, sort and 8/12/16 rows per page, row details, Heal box and move arrows, team buttons, Bring all here, draggable window, group hotkeys (F6/F7/F8), a quick-actions wheel (middle mouse), low HP flash and sound, downed countdown tags, level-up glow and a kill feed. Parts need Bait 1.9.0. **1.6.6:** Auto (auto-revive) tick box per row.

## Gyro Safe

Gyrocopters no longer damage blocks or terrain they hit. Install on the players who fly and on the server. See [GyroSafe/README.md](GyroSafe/README.md).

## Drone Loot Filter

Companion for tehAon's Drone Automation. Each player chooses items their Auto-Loot drone leaves alone (bags, containers and loose items) with `/dai`. See [DroneLootFilter/README.md](DroneLootFilter/README.md).

## Zombie and Animal Bait

**Server only; players install nothing.** A craftable throwable that pulls zombies and animals to a spot.

- **Item:** Zombie and Animal Bait. Recipe: 2 rotting flesh + 1 plant fiber makes 2 baits.
- **Use:** throw it like a rock. Once it lands it calls every zombie and animal within 40 blocks for 45 seconds. Whatever reaches it stops and eats until it is used up (400 eat ticks, about 20 seconds of one animal eating).
- **How it works:** vanilla already has throwable decoys (rocks, snowballs) that zombies chase through the ApproachDistraction AI task. Animals don't have that task, so this adds it to each animal's task list (after attacking, before Look/Wander). Skipped: bosses, vultures, insect swarms.
- **Protected while baited (1.1.0):** a creature that is going for (or eating) the bait is only hurt by the player who threw it. Turrets (auto and mini) stop picking it as a target, and damage from traps, fire, other monsters and other defences is cancelled. Damage the thrower deals still counts, and so do hunger, falling and despawn, so nothing gets stuck alive. Once the bait is gone the creature is a normal target again.
- **Limits:** turret bullets are credited to the turret's owner, so a stray bullet from the thrower's own turret can still hit a baited creature in its line of fire (the turret just won't aim at it). Hits from other players are not blocked (their client sends that damage to the server).
- Tune the radius, time and eat ticks in `Config/items.xml`.
- Taming, thralls and every `/thrall` command: see [Bait/README.md](Bait/README.md).
- **1.12.1:** fix: spike traps, barbed wire, blade traps and electric fences no longer kill thralls (`TrapsSpareThralls`, on by default).
- **1.12.0:** thralls guard your backpack when you die, rush to you at low HP, walk quietly while you crouch and make no heat; `/t` short commands and aliases; `/thrall scream`, `pvp`, `duel`, `secondchance`, `retire`, `kennel`, `goto`, `stats`; catch-up XP, owner XP share and quest kill credit; a named Alpha after every horde night (Feast bait only); new Thrall Whistle item, Bait Feeder and Thrall Kennel blocks; per-creature caps, taming resistance, `config preset casual|default|hardcore`, an admin audit log and a name filter. See [Bait/README.md](Bait/README.md#new-in-1120).
- **1.11.0:** stronger animal thralls: they keep up with the day-based level wild zombies start at, and deal 50% more and take a third less damage (`AnimalDayLevels`, `AnimalDamagePct`, `AnimalToughPct`).
- **1.10.0:** perks at levels 10/25/50, guard posts, patrol routes, blood moon mode, `/thrall top` leaderboard, `/thrall scout`, `/thrall evolve`, admin heal/revive/tp, and Discord posts for rare tames, level milestones and evolutions.
- **1.9.0:** thralls by name, `/thrall here`, `where`, `move`, `deaths`, teams, auto-heal, a low bait warning, uneaten bait comes back to the thrower, new Lure Bait (zombies only), bait stacks to 100. **1.8.0:** per-thrall auto-revive with plain bait (`/thrall autorevive <n>`).

Note: this changes animal AI for the whole server, but only things marked for animals pull them. Vanilla rocks and snowballs are still zombie-only, so only the bait draws animals.

---
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
| `kbclear <password> [r=<blocks>] [count]` | admin, with the password | Remove dropped-item bags and loose items on the ground. `r=50` only within 50 blocks of you (from the server console: of the only player online). `count` only counts. |
| `/mybag` (chat) | every player | Your death backpacks: distance, direction, height and when you died, newest first. Works for bags in unloaded areas too. |

**Owner-only window.** For the first `OwnerOnly` (default `15m`, real time) after a death, the backpack
opens only for the player who died, and for their party (`OwnerOnlyParty=on`) and allies
(`OwnerOnlyAllies=on`). Anyone else is told whose bag it is and how long is left. After that it opens for
everyone, as in vanilla. `OwnerOnly=0` turns it off. `BagCommand` changes `/mybag`.

`kbclear` does not remove death backpacks, zombie loot bags, or vehicle storage bags. It only removes things in areas that are loaded right then. Bags in unloaded areas stay until someone goes there.

People already connected keep the old times until they rejoin. Anyone who connects after `kbags reload` gets the new times. There is no command that reloads a mod DLL. Replacing this mod's code still needs a server restart. `kbags reload` only refreshes the bag times.

**Changelog**
- 1.3.0: `/mybag` tells players where their death backpacks are. Death backpacks open only for their owner (and party/allies) for the first 15 minutes (`OwnerOnly`, `OwnerOnlyParty`, `OwnerOnlyAllies`). `kbclear` takes `r=<blocks>` to clear only near you, and `count` to only count. Now uses Harmony (the game's own `0_TFP_Harmony`), still server only.
- 1.2.2: `kbclear` is admin-only (permission 0) and still asks for the password. It used to be open to every player with only the password guarding it. A server that set its own permission for `kbclear` in `serveradmin.xml` keeps that setting.
- 1.2.1: works on game version 3.3 (3.3 removed the old backpack timer, which made the mod error at start-up). Times live in `settings.txt`; `kbags reload` applies them without a restart; `kbclear` removes dropped items in loaded areas.
- 1.2.0: dropped items and vehicle storage bags last 24 hours (were permanent).
- 1.1.0: zombie loot bags last 24 hours (was vanilla 1 hour).
- 1.0.0: death backpacks, dropped items and vehicle storage bags never despawn.

---

## Spawn Tuner

Change spawn rates while the game runs, with no restart. Type the commands in the server console
(F1 in single player). Each change is saved to `spawns.txt` in the mod folder, so it's still there
after a restart.

```
spawns                      list every setting: the value now, and the world's own value
spawns NightDensity 4       up to 4 zombies per spawn spot at night
spawns NightRespawn fast    cleared areas refill faster at night
spawns BloodMoonCount 16    16 zombies per player at the next blood moon
spawns MaxZombies 100       allow 100 zombies alive at once
spawns NightDensity world   back to the world's own setting
spawns refresh              apply density and respawn changes to the areas already loaded
spawns HordeFrequency 200   wandering hordes twice as often
spawns ramp NightDensity +1 every 7 max 8   one more per spot each week, up to 8
spawns save hard            save every change as a preset
spawns load chill           switch to a preset
```

| Setting | Values | What it is |
|---|---|---|
| `DayDensity`, `NightDensity` | `none`, `default`, 1-100 | Zombies each spawn spot in an area can hold. The game's own sandbox range is 1-5. |
| `DayRespawn`, `NightRespawn` | `default`, `veryslow`, `slow`, `normal`, `fast`, `veryfast` | How fast a cleared area fills up again. |
| `AnimalDayDensity`, `AnimalNightDensity`, `AnimalDayRespawn`, `AnimalNightRespawn` | as above | The same for animals. |
| `BloodMoonCount` | 0-1000 | Zombies per player at a blood moon (the game offers 4-64). |
| `MaxZombies`, `MaxAnimals` | 0-1000 | Most zombies / animals alive in the whole world at once. |
| `HordeFrequency` | 0-1000% | How often wandering hordes come. 100% is vanilla (one every 12-24 game hours), 200% twice as often, 0% none. |
| `HordeSize` | 0-1000% | How many zombies each wandering horde has, as a percentage of vanilla. |

These are the same values as the Sandbox Options you pick when making a world (`MaxZombies` and
`MaxAnimals` are the server's `MaxSpawnedZombies` / `MaxSpawnedAnimals`). Spawn Tuner sets them on
top of the world's options, and a setting you never change keeps the world's value.

- Each 80x80 area keeps its own spawn timer, which can run for a few game days. A new density or
  respawn speed is used as each timer runs out. `spawns refresh` restarts the timers in the areas
  around players now, so they fill up with the new settings within seconds. Cleared areas fill up again too.
- `MaxZombies` and `MaxAnimals` apply straight away. `BloodMoonCount` applies from the next blood
  moon: one that has already started keeps its count.
- High numbers cost server performance. `MaxZombies` is the cap that protects the server: density
  can't push past it.
- Edited `spawns.txt` by hand? Type `spawns reload`.
- `spawns` also shows how many zombies and animals are alive against the caps, and when the next
  wandering horde is due. `HordeFrequency` and `HordeSize` apply from the next wandering horde.
- **Ramps** move a setting as game days pass: `spawns ramp NightDensity +1 every 7 max 8` adds 1 on
  day 8, 2 on day 15 and so on, never past 8, starting from the setting's value (or the world's). Use a
  minus to go down. Saved as `Ramp.NightDensity = ...` in `spawns.txt`; `spawns ramp <setting> off` stops it.
  Density and respawn need a number to start from (not `none`/`default`).
- **Presets** are whole sets of settings in the `presets` folder. `spawns save <name>` saves the current
  changes, `spawns load <name>` switches to one (everything not in it goes back to the world's value),
  `spawns presets` lists them. `chill` and `hard` come with the mod.

**Server only**: spawning is decided by the server, so players install nothing.

**Changelog**
- 1.1.0: wandering hordes (`HordeFrequency`, `HordeSize`), presets (`spawns save/load/presets`, ships `chill` and `hard`), ramps by game day (`spawns ramp`), and `spawns` shows zombies/animals alive and the next wandering horde.
- 1.0.0: first version.

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
`download.file` is `<slug>-<version>.zip`, the name CI uploads.

## Releasing

Actions → **Release & Publish** builds one mod (or all of them) and can open a GitHub Release
tagged `Name-vVERSION` and upload that zip to Nexus. The version is the `Version` in `ModInfo.xml`.
A version that is already on GitHub or Nexus is skipped.

The build job runs on a self-hosted Linux runner labeled `[self-hosted, linux, 7d2d]`.
It needs a 7 Days to Die install, the same way the Sun Haven runners need the game
mounted: GitHub-hosted runners do not have `Assembly-CSharp.dll`.
The default path is in `Directory.Build.props`. The Docker runner sets
`RUNNER_7D2D_GAMEDIR`; otherwise the repository variable `SEVEND2D_DIR` is used.

Nexus uploads use the secret `NEXUSMODS_API_KEY` and the file id from the mod's Files tab
(Advanced), stored as `nexus_file_id` in `scripts/matrix/mod-matrix.json`. Remove Any Entity
uploads to the old Remove Zombie Dogs page (12841) and file.

The same run publishes the zip to the mods website
(`downloads.azraelsmods.com/7-days-to-die/<slug>/<version>/<slug>-<version>.zip`) and records
that version in `downloads.json`. That needs `CLOUDFLARE_API_TOKEN` and `CLOUDFLARE_ACCOUNT_ID`
on this repo. Without them the release still continues and the website file stays unchanged.
Releases that run at the same time each rewrite `downloads.json`, so the script reads it back
after a pause and writes its entry again until it stays (AZR-453). If the file cannot be read,
it is left alone rather than replaced.

## No Ding Sound

Replaces the ding you hear when your tool hits a block that can't be broken (bedrock, the map edge) with a bicycle horn. It changes the game's keystone_impact_overlay sound. The server sends sound settings to players, so only the server needs the mod.

To use a different sound, edit the ClipName in `Config/sounds.xml` (any clip path from the game's `Data/Config/sounds.xml`).

---
## Slot Lock Fix

Makes the padlock button in the backpack, storage bag and loot windows work again on V3.4.

Vanilla bug (V3.4 b6): in lock mode, clicking a slot only flips the padlock on screen. Each window has a
method that copies those padlocks into the bag or container's real slot locks (`UpdateLockedSlots`), but
nothing calls it: `XUiC_ContainerStandardControls.UpdateLockedSlotStates` is never assigned and
`LockModeToggled` only redraws the window. So Sort and the Move buttons move locked items, and the locks
are gone the next time the window opens.

The mod (Harmony) connects `UpdateLockedSlots` again. Locks are saved before Sort and every Move button,
and whenever lock mode is turned off (the padlock button, Esc, or closing the window). If a later game
version fixes this itself, the mod leaves the game's own hook alone.

**Players only** (it is UI code), so every player installs it and needs EasyAntiCheat off. The server
needs nothing.

**Changelog**
- 1.0.0: first version.

## K9 Death Sound

Wolves, dire wolves, coyotes and zombie dogs honk a bicycle horn when they die, and make a random silly
noise from the game when they're hurt. Install it on the server and everyone hears the horn. Players who
also install it and drop `k9death.ogg` / `.wav` / `.mp3` in its folder hear their own clip instead
(EAC off). `settings.txt` (each player's own copy): `Volume`, `MaxDistance`.

This folder is the built mod as shipped; its source isn't in this repository.

**Changelog**
- 1.1.0: the version in this folder (earlier history wasn't recorded).
