

### Taming
Feed a wild zombie bait until it has eaten enough meals (4 for a walker, +2 feral, +4 radiated, +6 charged/infernal) and it becomes your thrall: it follows you, fights other zombies and never hurts players. Wild zombies near a thrall sometimes go for it instead of you.

Chat commands: `/thrall list | call <n> | stow [n|all] | release <n> | kill <n|all>`. Holding tamed zombies grants hidden perk buffs (Horde Bond I-III). Settings in `AzraelBait.cfg`. Server only. Untested in game.

Animals tame the same way (3 meals by default) and follow you. **Daycare:** stand where the pen is and run `/thrall tether <n|all> [radius]`; tethered creatures stay inside the circle, keep standing while you are offline, and do not use your active slots. `/thrall untether` frees them.

**Limits:** up to 20 owned, 4 active at once by default. Admins change any setting live with `/thrall config <name> <value>` (saved with the world); `/thrall config` lists them.

**Levels:** wild zombies spawn with a level (game day / 5, plus 5 per tier, plus 0-2, max 300) that makes them hit harder (+3% per level) and take less damage (3% per level, divided), and they gain a level by killing a player or thrall. Thralls gain a level every 8 kills and add it to your perk power. Turn off with `ZombieLevels=false`.

**Health:** `/thrall hp` (and `/thrall list`) show a health bar per thrall in chat, and you get a whisper when one drops below 50%, 25% and 10% (`HealthAlerts=false` to turn off). There is no on-screen bar, since that needs a client mod.

### Thrall care and admin
- `/thrall name <n> <name>` nickname. `/thrall stay` / `follow <n|all>` hold position or follow.
- `/thrall mode <n|all> aggressive|defensive|passive`: defensive only fights what comes within 8 m of the thrall or you; passive never starts a fight.
- `/thrall heal <n>` spends 1 bait to restore 40% health. Out of combat thralls also regain 1% every 5 s.
- A fallen thrall returns after 300 s (`ReviveSeconds`, 0 = permadeath), or at once with `/thrall revive <n>` for 3 Plain, 2 Rich or 1 Feast bait (Plain is used first, then Rich, then Feast; `ReviveCostPlain/Rich/Feast`).
- Higher-level targets need more meals (+1 per 17 levels, never more than 27 meals in total: MaxFeeds).
- Tethers are refused inside someone else's land claim.
- Admins: `/thrall admin who | list <player> | release <player> <n> | limit <player> <owned> <active>` (per-player limits).
- Wild zombie levels are now stored in a custom variable on the zombie (no marker buffs).

**Pack animals:** every animal thrall that is active and following you (not tethered, not downed) adds 4 bag slots, up to 48 (`PackSlotsPerAnimal`, `PackSlotsMax`). Tethered daycare pets do not count, so the bonus only helps while they travel with you.

**POIs and infestations:** a zombie that came from a POI sleeper volume or an infested POI counts as dead for that volume the moment it is tamed, so clear quests and respawn timers are not held up by your thralls.

**Toasts:** events (meals, taming, level-ups, low health, perk and pack changes, falls) show as the game's own on-screen toast, so players need nothing installed. Command replies stay in chat. `UseToasts=false` puts everything back in chat.

The commands work from chat (/thrall ...) and from the console (F1, `thrall ...`). Every command is logged as `[Bait] command from #id: ...`.

**Look-at HP:** aim at one of your thralls, or any wild zombie or animal, and a toast shows its name, level and HP (up to 25 m, `LookHp`, `LookHpWild`, `LookHpRange`, `LookHpAngle`). Server side only, no client mod. It is a toast under the crosshair area, not a floating bar.

## Baby animals

Baby animals from other mods (such as CTK Animal Babies: fawns, cubs, pups, piglets, kittens) can be tamed like any other animal. /thrall list shows zombies and animals as separate lists. Bear cubs get the bait behaviour added in code, since that mod replaces their AI list.

## Which creatures can be tamed

Every zombie and every animal, including specials, dire wolves and bosses. To protect some, set `Exclude=className1,className2` in `AzraelBait.cfg` (or `/thrall config Exclude ...`).

Admins manage the list in game: `/thrall exclude list | add <className> | remove <className> | clear` (class names like `animalDireWolf`, `zombieScreamer`). It is saved per world and overrides `AzraelBait.cfg`.

## Stronger bait

- **Rich Bait** counts as 2 meals toward taming. Craft: 5 Zombie and Animal Bait + 1 raw meat + 1 honey. It eats for about 30 s.
- **Feast Bait** counts as 4 meals. Craft: 5 Rich Bait + 3 raw meat + 1 acid, at a campfire (the hardest to make). It eats for about 45 s.

Set the meal counts with `RichBaitMeals` and `FeastBaitMeals`. `giveself azraelBaitRich 10 true` and `giveself azraelBaitFeast 10 true` for testing.

## Protection and vehicles

- No player can hurt a thrall, not even its owner (`OwnerCanHurtThralls` and `OthersCanHurtThralls` allow it).
- Drones ignore thralls and treat them as allies.
- Tethers are blocked only inside another player's land claim. Your own claim and an ally's claim are fine.
- While you ride a vehicle your active, non-tethered thralls go inactive, and they are called back when you get off (`AutoStowInVehicle=false` turns it off).

**Spawn caps:** live thralls are not counted against `MaxSpawnedZombies` / `MaxSpawnedAnimals`, so tamed creatures do not shrink how many wild ones the server can spawn (`ThrallsCountTowardSpawnCap=true` counts them again).

**Stow variants (1.4.0):** `/thrall stow all` stows every thrall (tethered ones too), `/thrall stow active` only the ones following you, `/thrall stow tethered` only the tethered ones. `/thrall stow <n>` is one.

**Death reasons (1.4.0):** when a thrall dies you get a toast saying how ("killed by Zombie Lv3 (last hit 40, 2s ago)", "a fall", "a block (spikes)", "fire or heat"...), and the server log has a line `[Bait] thrall died: ...` with the position. If a thrall just vanishes with no death (despawned or its area unloaded) you are told that too.

**No digging (1.4.0):** thralls never break blocks while following you or going home to a tether; only entities, depending on their mode.

**Traps (1.4.0):** motion sensors (which set off dart traps and other powered traps) skip thralls the same way turrets do. Spikes and other damage blocks do not hurt drones (`DronesIgnoreSpikes`). Turrets and sensors also aim on every client, so the optional BaitUI mod stops that on screen too (the server sends it the thrall ids).

**Revive and heal costs (1.4.0):** pay with plain bait, Rich bait or Feast bait (they count as the meals they are worth). If you are short, the reply says how many the server sees in your toolbelt and bag.

**Client UI (1.3.0):** the optional BaitUI client mod gives players a roster window, name tags, a party list, fed tags and an admin window. The server sends it data only after the client says `/thrall hello` (hidden from chat), so players without it see nothing new.

**Grabbing (fixed in 1.4.0 for multiplayer):** picking up a thrall (a chicken, with an empty hand) leaves it in your inventory as the item and marks it inactive on your roster. Place the item down and it is that thrall again, tethered where you put it if it was tethered, otherwise following you. Or use `/thrall call <n>` (that takes the item back). Before, the roster respawned it and you ended up with a copy.

**Tame handoff (1.5.0):** a zombie tamed out of a POI sleeper volume is swapped for a fresh thrall at the same spot, so the red quest dot on the map goes away (it already counted as dead for the POI). Every new or called thrall is untargetable and unhurtable for `NewThrallGraceSeconds` (8) so it can walk back to you. Mode (aggressive/defensive/passive), stay and tether are stored with the thrall and survive stow, call, grab and revive.

**No harvesting (1.5.1):** a fallen thrall's body drops no loot bag and has nothing to butcher or search; it comes back from its record anyway.

**Animal feed rates (1.6.0):** each animal now needs its own number of meals instead of a flat 3: chicken and rabbit 1; snake, pig, stag, doe 2; boar, coyote 3; wolf, small bear, zombie dog, zombie vulture 4; mountain lion, zombie boar 5; bear, radiated vulture 6; dire wolf, zombie bear 8. Change them with `AnimalFeeds=animalBear:6,animalChicken:1` in `AzraelBait.cfg`; `AnimalFeedsToTame` is used for any animal not listed. Level bonuses and `MaxFeeds` still apply.

**Feed rates in game (1.6.1):** admins can read and change them with `/thrall config feeds` (lists them) and `/thrall config feeds.animalBear 6`. Changes are saved with the world in `AzraelBaitSettings.cfg`; only the rates you changed are written there, so later default changes still apply to the rest.

**Invulnerable mode (1.7.0):** `/thrall mode <n|all> invulnerable` - the thrall never attacks (like passive) and cannot be killed: all damage is cancelled and its health is kept full each tick, so bleeding, poison and fire don't hurt it either. Only despawn-type damage still applies. Mode is stored with the thrall like the others.

**1.7.1:** an invulnerable thrall is also untargetable: zombies and animals can't pick it as a target (anything already hunting it lets go, checked every second), on top of all damage being cancelled. Turrets, traps, motion sensors and drones already ignore thralls.

**1.7.2:** Heal and revive now find your bait on a dedicated server, with no client mod needed. The server cannot see a remote player's bag or toolbelt, only the saved copy their game sends, so it now reads that copy for the count and takes the bait with a game event that runs on your own game (new `Config/gameevents.xml`, and each bait item has a tag in `items.xml`). Damage that reaches a thrall through something another player owns (a turret, trap or explosive) is cancelled like a direct hit. A thrall that teleports to you or is called lands a few blocks behind you (opposite the way you move or face), never in front of a vehicle, and does not teleport while you ride. Drones never bark at or attack a thrall.

**Kill credit (1.7.2):** a thrall that is out with you now also gets credit for the hostile creatures you kill within `OwnerKillShareRange` (30 blocks, 0 = off), so clearing a POI mostly yourself still levels your thralls (8 kills per level by default). Before, only kills the thrall landed itself counted.
