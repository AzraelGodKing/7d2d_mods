# Zombie and Animal Bait - Client UI

Optional **client side** add-on for [Zombie and Animal Bait](../Bait/README.md). Players install it; the server does not need it.
It needs the Bait mod (1.3.0 or newer) on the server. Players without it lose nothing: the server only sends data to players whose client says hello.

## What you get

- **Roster window (backslash key):** every thrall as a row with buttons: Call/Stow, Stay/Follow, mode (Aggr, Def, Pass cycles), Tether/Untether (tethers where you stand), Heal/Revive, Rename (a small box to type the name, empty clears it), Release (asks twice). Stow all, Stow active (only the ones following you), Stow tethered, Follow all and Stay all along the bottom. Replies from the server show in a status line instead of chat.
- **Name tags:** level, name and an HP bar over your active thralls (within 45 m).
- **Party list:** your active thralls and their HP down the left side of the screen. Open any window with a cursor (the roster, your inventory) and drag the list with the left mouse button to move it; the spot is saved. `baitui reset` puts it back.
- **Fed tags:** "Fed 2/4" and a bar over a creature you are taming, so you do not need the toast.
- **Admin window:** the Admin button on the roster (only shown to server admins). Every setting with - / + buttons (Flip for on/off ones), and the "cannot be tamed" list with Remove buttons, "Exclude what I aim at" and "Clear excludes". The server still checks admin rights.

## Traps and turrets

The server sends this mod the entity ids of every thrall, so your turrets, mini turrets and motion sensors stop aiming at them on your screen too (the server already ignores them).

## Options

Console (F1): `baitui` shows them. `baitui nameplates on|off`, `baitui party on|off`, `baitui feed on|off`, `baitui scale <number>` (size of tags and list, default 1.6), `baitui key <key>` (default Backslash, `none` to turn it off).
Saved in `%APPDATA%\7DaysToDie\AzraelBaitUI.txt`.

## How it works

The buttons send the normal `/thrall` chat commands (they are hidden from other players by the Bait mod). The Bait mod answers new clients with hidden whispers that carry the roster, settings and fed progress; this mod removes them from the chat window before they show. The data is pushed when it changes, nothing is polled.

## Notes

- The window layout is plain game UI built from XML; if a button looks off on your screen size, tell Azrael.
- Install: copy the `AzraelBaitUI` folder into `Mods`. Single player and host games work too (the Bait mod must be installed there as well).
**1.2.0:** shows the new invulnerable mode: the roster mode button cycles Aggr / Def / Pass / Inv, the name tag reads [invulnerable] with a light-blue health bar, and the party list marks [Inv].

**1.2.1:** A name tag or HP bar is only drawn over an entity of the same class the server reported for that thrall. Before, a saved id that had been reused after a respawn or relog could put a thrall's tag over some other creature.
