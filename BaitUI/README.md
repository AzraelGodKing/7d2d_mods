# Zombie and Animal Bait - Client UI

Optional **client side** add-on for [Zombie and Animal Bait](../Bait/README.md). Players install it; the server does not need it.
It needs the Bait mod (1.3.0 or newer) on the server. Players without it lose nothing: the server only sends data to players whose client says hello.

## What you get

- **Roster window (Home key)**, in tabs. Drag the title bar to move it; the spot is saved.
  - **Thralls:** one row per thrall with Call/Stow, Stay/Follow, mode, Tether, Heal/Revive, Track and Page. Click a row for its details. Filter, sort, name search and 8/12/16 rows per page along the top.
  - **Groups:** Stow all/active/tethered, Follow all, Stay all, Bring all here, last deaths, teams, blood moon mode, PvP and "All to kennel".
  - **Display:** every HUD option, colours, tag size, window size, re-center, and which columns the rows show (level, HP, status, kills, mode, auto boxes).
  - **Keys:** the roster key, the quick-actions wheel key, the follow/stay/here keys, the go-to key and the thrall keys (Alt+1-9 calls or stows thralls 1 to 9; Ctrl, Shift or off).
  - **Tools:** thrall page, bestiary, session summary, leaderboard, your stats and the server's, and "Craft bait" (opens crafting on the bait recipes and shows how many you can make).
  - **Command bar** under every tab: type any `/thrall` command without the `/thrall` (`call 2`, `mode all defensive`) and press Enter. A hint shows the commands and thralls that match what you typed.
- **Thrall page:** one thrall's stats (level, kills to the next level, deaths, age, perks, evolution) and every action for it: rename, auto-revive, auto-heal, second chance, kennel, track, perks, guard post, patrol, scout, evolve, blood moon, scream, retire, release, move up and down.
- **New thrall setup:** when you tame something, a small window to name it and pick its mode and auto boxes (turn it off there or on the Display tab). A banner and a sound mark the tame.
- **Bestiary:** every zombie and animal, the meals it needs on this server, any cap, and which ones you have.
- **Session summary:** kills, tames, level ups, deaths and bait used since you joined. Each session is added to `AzraelBaitUI-sessions.txt` when you leave.
- **Name tags:** level, name and an HP bar over your active thralls (within 45 m), with a gold glow on level up, a red flash at low HP and a grey countdown where a downed one fell. Roaming Alphas get their own tag.
- **HUD extras:** damage numbers over what your thralls hit, faint lines from each thrall to its target, a downed panel with Revive buttons (click them while any window is open), colour-blind HP bars (blue to orange), a kill feed that groups repeats (all, own kills or special kills only).
- **Track:** a gold compass marker for one thrall at any distance. **Go-to key** (Insert): aim at the ground and the thrall picked in the roster (or every follower) walks there.
- **Party list** and **HP strip:** your active thralls down the left side, or small bars in the bottom-right corner. Drag the party list while a window is open; `baitui reset` puts it back.
- **Fed tags** and **taming helper:** "Fed 2/4" over a creature you are taming, and the meals needed for what you aim at while holding bait.
- **Admin window** (admins only), in tabs: every setting with - / + (and casual/default/hardcore presets), feed rates and caps per animal, the cannot-be-tamed list with search and "Add aimed", and a player browser with heal, revive, bring and release per thrall. The server still checks admin rights.
- **Languages:** the labels follow the game language for German, Spanish, French and Portuguese; everything else is English.

Parts need a matching Bait version on the server: buttons for something the server does not have yet stay blank or say which version they need.

## Traps and turrets

The server sends this mod the entity ids of every thrall, so your turrets, mini turrets and motion sensors stop aiming at them on your screen too (the server already ignores them).

## Options

Console (F1): `baitui` shows them. `baitui nameplates on|off`, `baitui party on|off`, `baitui feed on|off`, `baitui scale <number>` (size of tags and list, default 1.6), `baitui key <key>` (default Home, `none` to turn it off), `baitui version` (this mod's version and the Bait version the server reported).
Saved in `%APPDATA%\7DaysToDie\AzraelBaitUI.txt`.

## How it works

The buttons send the normal `/thrall` chat commands (they are hidden from other players by the Bait mod). The Bait mod answers new clients with hidden whispers that carry the roster, settings and fed progress; this mod removes them from the chat window before they show. The data is pushed when it changes, nothing is polled.

## Notes

- The window layout is plain game UI built from XML; if a button looks off on your screen size, tell Azrael.
- Install: copy the `AzraelBaitUI` folder into `Mods`. Single player and host games work too (the Bait mod must be installed there as well).
**1.2.0:** shows the new invulnerable mode: the roster mode button cycles Aggr / Def / Pass / Inv, the name tag reads [invulnerable] with a light-blue health bar, and the party list marks [Inv].

**1.2.1:** A name tag or HP bar is only drawn over an entity of the same class the server reported for that thrall. Before, a saved id that had been reused after a respawn or relog could put a thrall's tag over some other creature.

**1.3.0:** The roster window has a new bottom row: buttons to switch name tags, the party list and fed tags on or off, and a size - / + pair (steps of 0.2, from 0.5 to 4). They save with the other settings, the same as the `baitui` console command.

**1.3.1:** Name tags and HP bars sat in the wrong place on screen once you had travelled a few hundred blocks from where the world loaded: the game recentres its scene as you move, and the tag position did not allow for that. They now line up with the thrall.

**1.4.0:** Name tags and HP bars now also show over thralls that belong to members of your party (switch: `Party members' bars` in the roster window; needs Bait 1.7.3 on the server, and the owner's thralls within 45 blocks). New roster-window buttons pick the colour of the name text and of the HP bar (Auto keeps the green-to-red health colour). Thralls no longer show an enemy marker on the compass or map.

**1.5.0:** Name and bar colours can be set per thrall. In the roster window, click the `Colors apply to` button to pick a thrall (or all thralls for the default), then the Name color and Bar color buttons; `Default` follows the all-thralls setting. Saved on your own computer by roster number and class, so releasing or reordering thralls can shift which colour goes with which.

**1.5.1:** The roster window now opens with the Home key, since the backslash key is missing or elsewhere on some keyboard layouts. Settings files that still held the old default are moved to Home; `baitui key <key>` picks any other key (Backslash included).

**1.6.0:** Players pick their own roster hotkey: open the roster window (Home by default), click the `Hotkey` button in the bottom row and press the key you want (Esc cancels). It is saved with the other settings. `baitui key <key>` and `baitui key none` still work from the console.

**1.6.1:** On a dedicated or listen-server client, `/thrall hello` and roster buttons now go through `ChatMessageClient`. The old path called `ChatMessageServer` on the player's game, which the dedicated host refuses, so the roster stayed empty. Home also opens the window while another cursor HUD is up. `baitui open` toggles the roster from F1.

**1.6.2:** Dedicated hosts send their own XUi to every client, which never includes this client-only roster. The client now puts the roster windows back on after those files arrive, so Home and `baitui open` work on a dedicated server without installing Bait UI on the host.

**1.6.4:** Fix: the UI never got an answer from a dedicated server. 1.6.3 sent the hello and the button commands with a call that only prints in your own chat window, so they never left your PC. They go to the server again. New `baitui version` console command shows this mod's version and the server's Bait version, and the version is in the log line on load.

**1.6.3:** Version check. The roster title shows the server's Bait version (Bait 1.7.6 or newer sends it) and warns when it is older than 1.3.0, the oldest this UI can talk to. The `Party members' bars` button says so when the server is older than 1.7.3. The UI asks the server for its roster 3 times (15 s apart) instead of 8, hides the `/thrall` command lines from your chat, and says when the server never answered.

**1.6.5:** On/off settings in the admin window show as a tick box (`[X]` on, `[  ]` off; the Flip button toggles it). The new server setting `KatsSpecial` (Bait 1.7.9) shows as **Kats Special (27 thralls each)**: ticked, every player may hold 27 thralls.

**1.6.6:** Each roster row has an `Auto` tick box (`[X] Auto` on, `[  ] Auto` off) that turns auto-revive on or off for that thrall (`/thrall autorevive <n>`): when it falls and you carry the base Zombie and Animal Bait, the server takes the bait and brings it straight back. The roster window is a little wider to fit it. Needs Bait 1.8.0 or newer on the server; with an older server the box stays blank.

**1.7.0:** Needs Bait 1.9.0 on the server for the parts marked (1.9.0); with an older server those buttons stay blank.
- Roster rows: `[X] Heal` auto-heal box (1.9.0) and `^`/`v` to move a thrall up or down the roster (1.9.0). Click a row's name to open a details line (level, kills to the next level, HP, where it is, mode, auto toggles); click it again to close.
- Title shows the bait the server sees in your inventory (1.9.0).
- `Show: All/Zombies/Animals` filter, `Sort: Number/Level/HP` (HP puts downed and hurt ones first) and `8/12/16 per page` (three roster windows; `tools/gen-roster.py` writes them).
- Bottom rows: `Bring all here` and `Deaths` (listed in chat) (1.9.0), `Re-center`, team picker with `Call team`, `Save active` and `New team` (1.9.0).
- Drag the window by its title bar; the spot is remembered (`Re-center` puts it back).
- Group hotkeys: F6 follow all, F7 stay all, F8 bring all here (1.9.0). Click the `Keys:` button and press three keys in turn to change them (Del = none), or `baitui followkey|staykey|herekey <key>`.
- Quick-actions wheel: hold the middle mouse button (`Wheel:` button or `baitui wheelkey <key>` to change), move the mouse, scroll or press 1-5 to pick Follow all, Stay all, Bring all here, Stow active or Call team, then let go. The camera still turns while you aim.
- Name tags: a thrall under 25% HP flashes red with a short sound (`Low HP flash`, `Low HP sound`); a downed thrall keeps a grey `back in m:ss` tag where it fell (`Downed tags`); a level-up turns the tag gold with `Lv N!` for 4 s (`Level glow`).
- Kill feed: your thralls' kills fade in at the right of the screen for 6 s (1.9.0) (`Kill feed`).
- `tools/gen-windows.ps1` is gone: it no longer matched the windows file. The roster windows come from `tools/gen-roster.py`; the rename and admin windows are edited by hand.


**1.8.0:** Needs Bait 1.10.0 on the server for the parts marked (1.10.0); with an older server those buttons stay blank.
- **Thrall page** (1.10.0): select a row and click `Thrall page` (or it opens the first one). It shows level, kills to the next level, deaths, when it was tamed, where it is, HP, mode, the auto toggles, its perks and slots, and what it evolves into. Its buttons: the four perks and `Reset perks`, `Guard here`, `Add patrol point`, `Start/Stop patrol`, `Clear points`, `Scout`, `Evolve`, and your `Blood moon` mode. `Prev`/`Next` step through your thralls.
- **Search box** at the bottom left of the roster: type part of a name, the creature name or a number. It works with Show and Sort.
- **Tooltips** on every roster button. Heal, revive, auto-revive, auto-heal and Bring all here show what they cost on this server.
- **Map markers:** your thralls show on the map and compass (green), and their daycare, guard post or patrol spot too (blue, 1.10.0). `Map: on/off`.
- **HP strip:** small HP bars for your active thralls in the bottom-right corner, in place of the party list. `HP strip: on/off`.
- **Compact party list:** name and bar on one line, so 10 or more thralls fit. `Compact: on/off`.
- **Coloured rows:** a thrall you gave its own name colour gets a row tinted that colour.
- **Themes:** `Theme: Dark/Light/High contrast` recolours the roster and thrall page, and the backing behind the party list and HP strip.
- **Taming helper** (1.10.0): while you hold bait, the creature under your crosshair shows its meals needed (or how far you are), or why it can't be tamed: already a thrall, excluded on this server, your roster is full, or someone else is feeding it. `Tame help: on/off`.
- Roster rows show guards and patrols. The party list marks them `[G]` and `[P]`.
- The roster windows are 46 px taller for the new row. The 16-row one is 1022 tall.
