# Ideas backlog

Update ideas that haven't been built yet, grouped by mod. Each has a short code so it can be picked
by name. **Server** = only the server installs it, **Client** = each player installs it (EAC off),
**Both** = both.

Built from the October 2026 list: Spawn Tuner 1.1.0 (ST1-ST4), Remove Any Entity 2.2.0 (RAE1-RAE3),
Keep Backpacks 1.3.0 (KB1-KB3), Blood Moon Sound 1.3.0 (BM1-BM5) and the README refresh (H1).

## Speedometer (client)

- **SP1** Gyro readout: altitude and climb rate while flying a gyro, plus a compass heading.
- **SP2** Fuel range: roughly how far you can go on the fuel in the tank at your current speed.
- **SP3** Trip stats: top speed and distance for the current trip, reset with a command.
- **SP4** Move it: `speedo pos` to place the readout anywhere on screen.

## Craft From Chests (client)

- **CFC1** Quick stash: one button that moves backpack items into claim chests that already hold the same item.
- **CFC2** Range instead of claim: option to use chests within N blocks of you, for building outside a land claim.
- **CFC3** Skip some chests: chests whose name or sign has `nocraft` are never used.
- **CFC4** Repairs: item repairs (repair kits, duct tape) can also take materials from chests.

## Upgrade Bench (both)

- **UB1** Server decides the costs: the server sends `upgrades.txt` to players when they join, so players
  don't need matching copies of the file.
- **UB2** Cost preview: the bench window shows the result and the material cost before you click.

## Drone Loot Filter (server)

- **DLF1** Only-list mode: `/dai only` flips the list so the drone takes only the listed items.
- **DLF2** Groups: `/dai add group:ammo` (and food, meds, resources) using the game's item groups.
- **DLF3** Share with party: `/dai share` lets party members use one list.

## Slot Lock Fix (client)

- **SLF1** Finish the open bugs from the 1.0.0 test: locking hit every slot in a container, and the
  backpack showed no padlocks because the server's 17-column backpack UI (AGF BackpackPlus) has no lock
  button. Needs the client log from the logging build, and the lock button added to that backpack UI (or
  to the AGF BackpackPlus 3.4 temp fix).

## Gyro Safe (both)

- **GS1** Any vehicle: a setting listing which vehicles are safe (gyro by default; optionally the 4x4,
  motorcycle, drone).

## K9 Death Sound and No Ding Sound (server, client optional)

- **K9-1** Any creature: a settings list so any animal or zombie can have a death sound, not just dogs.
  No Ding Sound could fold into the same mod.

## Quest Disconnect Fix (server)

- **QDF1** Clear a ghost by hand: a `ghostkick <name>` command that removes a stuck player without a
  restart, for any ghost the fix doesn't catch.

## Craftable Canvas Signs (server)

- **CS1** More craftable blocks: other developer-only decor blocks (picture frames, posters) made
  craftable the same way.

## Blood Moon Sound (both)

The second Blood Moon Sound list (October 29, 2026) was built as 1.5.0: S01, S02, S05-S13 (server) and
U01-U03, U05, U12 (player). Nothing from it is waiting.

**Rejected** (never suggest these again unless asked):
- **S03** Second webhook (send each message kind to its own channel).
- **S04** A Discord thread per blood moon.
- **U04** Horde music that loops until dawn.
- **U06** Several warning times with their own clips.
- **U07** Ducking the game's music while a clip plays.
- **U08** Death and respawn clips.
- **U09** Volume per file in the file name.
- **U10** A clip folder per server or world.
- **U11** A key that stops the clip that's playing.

## Housekeeping notes (October 2026)

- `AzraelK9DeathSound/` is the built mod only: no `mod/` + `src/` layout, no `mod.json`, and its DLL
  source isn't in the repo (DLLs are gitignored, so a fresh clone can't rebuild it). Not in `build.ps1`.
- `scripts/matrix/mod-matrix.json` (the Release & Publish list) now has every mod `build.ps1` builds.
  Spawn Tuner, Bait, Bait UI, Gyro Safe, Drone Loot Filter, Slot Lock Fix, Craftable Signs and No Ding
  Sound have empty Nexus ids until their Nexus pages exist. K9 Death Sound stays out (see above).
