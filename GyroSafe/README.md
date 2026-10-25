# Gyro Safe

Gyrocopters no longer damage blocks or terrain when they collide with them.

Block damage from a vehicle is calculated on the machine running its physics (the driver's game, or the host) and then sent to the server. So:

- **Players who fly** (and the host / single player): install it. This stops the damage at the source.
- **Server**: install it too. It drops block changes sent by a player who is riding a gyro, as a safety net (the game does not label vehicle damage with the vehicle, so the rider is checked instead). If a driver does not have the mod, they may see a block damaged on their screen that the server and other players still see intact, until the chunk refreshes.

If a patch fails after a game update, the log shows `[GyroSafe] BROKEN: ...`. The gyro may also take less collision damage than before, since the hit is skipped.

Build: `.\build.ps1 -Only GyroSafe -Install`
