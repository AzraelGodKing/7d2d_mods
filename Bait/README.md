

### Taming (2.0.0, animals, tether and levels in 2.1.0)
Feed a wild zombie bait until it has eaten enough meals (4 for a walker, +2 feral, +4 radiated, +6 charged/infernal) and it becomes your thrall: it follows you, fights other zombies and never hurts players. Wild zombies near a thrall sometimes go for it instead of you.

Chat commands: `/thrall list | call <n> | stow [n|all] | release <n> | kill <n|all>`.
Holding tamed zombies grants hidden perk buffs (Horde Bond I-III). Settings are in `AzraelBait.cfg`. Server only, no client install. Untested in game.

### Taming (2.0.0, animals, tether and levels in 2.1.0)
Feed a wild zombie bait until it has eaten enough meals (4 for a walker, +2 feral, +4 radiated, +6 charged/infernal) and it becomes your thrall: it follows you, fights other zombies and never hurts players. Wild zombies near a thrall sometimes go for it instead of you.

Chat commands: `/thrall list | call <n> | stow [n|all] | release <n> | kill <n|all>`. Holding tamed zombies grants hidden perk buffs (Horde Bond I-III). Settings in `AzraelBait.cfg`. Server only. Untested in game.

Animals tame the same way (3 meals by default) and follow you. **Daycare:** stand where the pen is and run `/thrall tether <n|all> [radius]`; tethered creatures stay inside the circle, keep standing while you are offline, and do not use your active slots. `/thrall untether` frees them.

**Limits:** up to 20 owned, 4 out at once by default. Admins change any setting live with `/thrall config <name> <value>` (saved with the world); `/thrall config` lists them.

**Levels:** wild zombies spawn with a level (game day / 5, plus 5 per tier, plus 0-2, max 300) that makes them hit harder (+3% per level) and take less damage (3% per level, divided), and they gain a level by killing a player or thrall. Thralls gain a level every 8 kills and add it to your perk power. Turn off with `ZombieLevels=false`.

**Health:** `/thrall hp` (and `/thrall list`) show a health bar per thrall in chat, and you get a whisper when one drops below 50%, 25% and 10% (`HealthAlerts=false` to turn off). There is no on-screen bar, since that needs a client mod.

### Thrall care and admin (2.3.0)
- `/thrall name <n> <name>` nickname. `/thrall stay` / `follow <n|all>` hold position or follow.
- `/thrall mode <n|all> aggressive|defend|passive`: defend only fights what comes within 8 m of the thrall or you; passive never starts a fight.
- `/thrall heal <n>` spends 1 bait to restore 40% health. Out of combat thralls also regain 1% every 5 s.
- A fallen thrall returns after 300 s (`ReviveSeconds`, 0 = permadeath), or at once with `/thrall revive <n>` for 3 bait.
- Higher-level targets need more meals (+1 per 25 levels).
- Tethers are refused inside someone else's land claim.
- Admins: `/thrall admin who | list <player> | release <player> <n> | limit <player> <owned> <active>` (per-player limits).
- Wild zombie levels are now stored in a custom variable on the zombie (no marker buffs).

**Pack animals (2.4.0):** every animal thrall that is out and following you (not tethered, not downed) adds 4 bag slots, up to 48 (`PackSlotsPerAnimal`, `PackSlotsMax`). Tethered daycare pets do not count, so the bonus only helps while they travel with you.

**POIs and infestations (2.4.1):** a zombie that came from a POI sleeper volume or an infested POI counts as dead for that volume the moment it is tamed, so clear quests and respawn timers are not held up by your thralls.
