

### Taming (2.0.0, animals, tether and levels in 2.1.0)
Feed a wild zombie bait until it has eaten enough meals (4 for a walker, +2 feral, +4 radiated, +6 charged/infernal) and it becomes your thrall: it follows you, fights other zombies and never hurts players. Wild zombies near a thrall sometimes go for it instead of you.

Chat commands: `/thrall list | call <n> | stow [n|all] | release <n> | kill <n|all>`.
Holding tamed zombies grants hidden perk buffs (Horde Bond I-III). Settings are in `AzraelBait.cfg`. Server only, no client install. Untested in game.

### Taming (2.0.0, animals, tether and levels in 2.1.0)
Feed a wild zombie bait until it has eaten enough meals (4 for a walker, +2 feral, +4 radiated, +6 charged/infernal) and it becomes your thrall: it follows you, fights other zombies and never hurts players. Wild zombies near a thrall sometimes go for it instead of you.

Chat commands: `/thrall list | call <n> | stow [n|all] | release <n> | kill <n|all>`. Holding tamed zombies grants hidden perk buffs (Horde Bond I-III). Settings in `AzraelBait.cfg`. Server only. Untested in game.

Animals tame the same way (3 meals by default) and follow you. **Daycare:** stand where the pen is and run `/thrall tether <n|all> [radius]`; tethered creatures stay inside the circle, keep standing while you are offline, and do not use your active slots. `/thrall untether` frees them.

**Limits:** up to 20 owned, 4 out at once by default. Admins change any setting live with `/thrall config <name> <value>` (saved with the world); `/thrall config` lists them.

**Levels:** wild zombies spawn with a level (game day / 14, plus tier, +-1, max 5) that makes them hit harder and take less damage, and they gain a level by killing a player or thrall. Thralls gain a level every 8 kills and add it to your perk power. Turn off with `ZombieLevels=false`.
