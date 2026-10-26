# 7 Days Mod Manager

A Windows mod manager for **7 Days to Die**, built on the player side of
[ModWright](https://github.com/AzraelGodKing/ModWright) (The Drop Pod): the same .NET 8 WinForms stack, the same
look and every one of its themes, cut down to what 7 Days to Die needs.

It finds the game through your Steam libraries (or a folder you pick) and reads every mod from both places the game
loads them: the game's own `Mods` folder and `%AppData%\7DaysToDie\Mods`. Nothing is changed until you press
**Save to game**, and nothing is changed while the game is running.

## Using it

- **Mods:** tick a mod to enable it. **Save to game** (Ctrl+S) moves unticked mods to a `Mods-disabled` folder next
  to the `Mods` folder they came from, and ticked ones back. The game's own `0_TFP_Harmony` is always on. The
  **Load order** column shows the order the game loads them in (by folder name, A to Z). Ctrl+Z and Ctrl+Y undo and
  redo ticks; **Undo changes** goes back to what is on disk.
- **Install:** drop a mod's `.zip` or folder anywhere on the window, or press **Install...** (Ctrl+O). Every folder
  inside with a `ModInfo.xml` is a mod, however deep the zip put it. A mod that is already installed (same ModInfo
  `Name`) is replaced where it is, after the old copy is zipped into the mod backups. `.rar` and `.7z` need unpacking
  first. **Uninstall** sends a mod to the Recycle Bin.
- **Modlists:** **Save as...** keeps the ticked mods as a named list and **Switch to it** enables exactly those.
  **More** imports and exports lists, copies a share code (`7DMM1:...`) to paste in a chat, and adds a list from one.
- **Problems:** mods with no or a broken `ModInfo.xml`, mods unpacked one folder too deep (**Fix** moves the folder
  up), two enabled mods with the same `Name` (the game loads only the first), old-format ModInfo files, code mods
  with EasyAntiCheat forced on, and a missing `0_TFP_Harmony` (**Fix** asks Steam to verify the game files).
  It also reads each enabled mod's `Config` XML: two mods that `set`, `remove` or `setattribute` the same xpath in the
  same file (whatever their quotes and spacing; the one that loads last wins), two mods that add an item, block or
  buff with the same name, and XML files that aren't valid. A mod whose description says it needs another ("Needs the
  Bait mod", "Requires SCore") is flagged when that mod is disabled or missing; one needed only on the server is left
  alone when it isn't installed here. Mods whose name or description says they are for an older game ("A20", "for
  V1.0") are flagged against the game version read from the log, which is shown beside the game folder.
- **Play:** starts the game. With EAC on it goes through Steam and the game's launcher; with EAC off it starts
  `7DaysToDie.exe` directly. On **Auto** (the default), EAC is off whenever an enabled mod has code (a DLL). Launch
  options, and a backup of the newest save before each start, are under **Options > Play**.
- **Test** and **Read the log:** Test starts the game and follows its log; Read the log reads the last one. Errors
  and warnings are grouped by message, and each names the mod most likely at fault: a mod named in it (XML patch
  errors name the mod), a file in its folder, or its code in the stack trace (namespaces read from the mod's DLLs,
  never run).
- **Server, client or both:** the **Side** column says where each mod has to be installed for a multiplayer game.
  It comes from the mod's own description when it says so ("Server only: players install nothing", "Only players
  need it", "Install on the server AND every player's game"). Otherwise it is a guess from the mod's files, marked
  with `?`: XML alone works from the server, which sends it to players; HUD windows (XUi), textures, sounds and asset
  bundles have to be on each player's PC; code counts as both. Right-click a mod > **Side** to set it yourself.
- **Settings:** **Settings...** beside the list (or right-click > **Edit settings...**) opens a mod's settings files
  (`settings.txt`, `*.cfg` and the like, not readmes or examples). A file of `Key=value` lines is shown as a form,
  each setting with the author's comment above it and on/off switches as drop-downs; **Text** edits any file as
  text, such as a list like `entities.txt`. Saving rewrites only the lines you changed, and keeps your copy: when the
  mod is updated or reinstalled, your values go back into the new version's file (new settings it adds keep their
  defaults).
- **Details beside the list:** the picked mod's ModInfo, where it is, whether it has code, its size and your note.
  Right-click a mod for **Note...**, **Colour**, its folder and website, and **Uninstall**.
- **Tools:** **Find the broken mod for me** (enables half the suspects each round until one is left, then puts your
  mods back), the Mods and backup folders, and **Verify the game files in Steam**.
- **Saves...:** every save with its world, when it was last played and its size, with **Back up** to a zip.
- **Options:** theme (Dark, Light, Kats, Koneko, Serina, Wolfie, Vickra, Kitomi, Szendoo, Triphlx, Tohma, Skywolf,
  Hunter, or Match Windows; Windows High Contrast always wins), text size, the game and data folders, where new mods
  are installed, EasyAntiCheat and launch options.

- **Updates:** at start-up the app asks GitHub for a newer release and offers **Update now**, **Release notes** or
  **Later**, with a box to stop reminding you about that version. **Update now** downloads the new exe, checks it
  against the release's `SHA256SUMS.txt`, and restarts into it; the old exe is kept as `<name>.update-old` until the
  next start, and put back if Windows (or antivirus) won't run the new one. Only GitHub's public list of releases is
  read. Turn the check off, or **Check now**, under **Options > About**. Only the single-file exe updates itself; a
  copy built with `dotnet build` offers the release page instead.

Settings, modlists and backups are in `%LocalAppData%\SevenDaysModManager`.

## Command line

```
SevenDaysModManager list [--json]         the installed mods (* = enabled)
SevenDaysModManager problems [--json]     problems with the enabled mods (exit code 1 on errors)
SevenDaysModManager lists                 the saved modlists (* = last used)
SevenDaysModManager switch <modlist>      enable exactly the mods of a modlist
SevenDaysModManager play [<modlist>]      start the game, after switching to the modlist
SevenDaysModManager install <zip|folder>  install a mod
```

A desktop shortcut to `SevenDaysModManager.exe play "Horde night"` starts that setup in one click.

## Build

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). Unlike the mods in this repo, it needs no
game files.

```powershell
cd ModManager
dotnet build ModManager.sln -c Release
dotnet test ModManager.sln
```

The app is `src\SevenDaysModManager\bin\Release\net8.0-windows\SevenDaysModManager.exe`. The **Mod Manager**
GitHub workflow builds and tests every change under `ModManager/` and uploads a self-contained single-file exe.

`src/SevenDaysModManager.Core` holds everything that needs no WinForms (finding the game, reading mods, installing,
modlists, problems, the log, saves, the command line) and is what the tests cover. `src/SevenDaysModManager` is the
window. `Theme.cs` is ModWright's, unchanged apart from its namespace.

## Releasing

Bump `<Version>` in `ModManager/Directory.Build.props` on `main`, then run the **Release Mod Manager** workflow
(Actions tab), optionally with change notes. It tests, publishes `SevenDaysModManager-<version>-win-x64.exe` and
`SHA256SUMS.txt`, and creates the release `ModManager-v<version>`, which is what the app's update check looks for. The
mods' releases (`<ModName>-v<version>`) are ignored by it, and the app's releases are never marked "latest".

## Not carried over from ModWright

The Launch Pad (Workshop uploads; 7 Days to Die has no Workshop), SteamCMD, Workshop and Thunderstore downloads,
load-order sorting (the game has no load-order file), and RimWorld's instances, XML and texture tools.
