# Drone Loot Filter

Companion for **Drone Automation** by tehAon (it must be installed). Lets every player choose which items their Auto-Loot drone leaves alone. Applies to loot bags, containers and loose ground items. Server-side only: players install nothing.

Commands (chat, private to you, saved with the world):

- `/dai` - help
- `/dai last` - the last things your drone took, numbered; `/dai skip <n>` ignores that kind from now on
- `/dai add <name>` - ignore an item by internal or in-game name; `*` is a wildcard (`/dai add ammo*`); `/dai add held` ignores what you are holding
- `/dai remove <n|name>` - stop ignoring
- `/dai list` - your ignore list
- `/dai find <text>` - look up item names
- `/dai clear` - empty your list

A container or bag holding only ignored items is left alone completely (the drone doesn't keep re-opening it). Anything that isn't on your list is looted as before. Optional `DroneLootFilter.cfg` in the mod folder: `Prefix=/dai`, `DefaultIgnore=a,b*` (applied to everyone).

Build: `.\build.ps1 -Only DroneLootFilter -Install`
