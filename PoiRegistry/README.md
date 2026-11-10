# POI Registry (private)

Server-side only. Works out which player owns which POI from land claims, writes `poi-owners.json` next to
`settings.txt`, and can push the same JSON to a web page (`poi.azraelsmods.com`). Players install nothing.

This mod is **not released**: its `mod.json` says `"private": true` (the website skips it, and the release
workflow never uploads a private mod to R2 or `downloads.json`), it is not in `scripts/matrix/mod-matrix.json`
or the release workflow's list, and it is not in the main README table. `build.ps1` builds it (`.\build.ps1 -Only PoiRegistry -Install`).

## How ownership is decided

- Claims come from `PersistentPlayerList.m_lpBlockMap`. A claim counts when the owner's protection is still valid
  (`World.IsLandProtectionValidForPlayer`, so offline expiry works like vanilla) and it is not a non-primary claim block
  (`TEFeatureLandClaim.IsPrimary`, which is only known while the block's chunk is loaded; an unloaded claim counts). Its area is the block position +/- `(LandClaimSize - 1) / 2` on X and Z.
- POIs come from `DynamicPrefabDecorator.GetPOIPrefabs`, each with a bounding box.
- A claim counts for a POI when it overlaps the POI by at least `MinOverlapBlocks` (x * z).
- Owner = the player with a claim block inside the POI, else the biggest overlap. Allies of the owner are
  `coOwners`. Any non-allied player who also overlaps makes the POI `contested`.
- Nothing is patched. Every `IntervalSeconds` (10) the mod takes a cheap fingerprint of the claim list. It only
  recalculates ownership when that changed (a claim placed, removed or destroyed), a player joined or left, or
  the heartbeat is due (`HeartbeatMinutes`, 10; this also catches claims that expired with time).
- Posting: `UnityWebRequest` in a coroutine (the game never waits for it), 10 second timeout, at most one post
  per 6 seconds, only changed snapshots plus the heartbeat. On HTTP 429 it waits the `Retry-After` time; on other
  errors it backs off from 30 seconds up to 10 minutes and logs one line per kind of problem, not one per try.
- The first post goes out a few seconds after the world has loaded.

## Setup

1. Build and install (`build.ps1 -Only PoiRegistry -Install`), or copy the built folder into the server's `Mods`.
2. Edit `settings.txt`: set `PostUrl`, `Token`, `ServerName`. Or set the environment variables
   `POI_REGISTRY_URL` and `POI_REGISTRY_TOKEN` (they win, and a reinstall of the mod can't wipe them).
3. Start the server, or type `poi reload`. Then `poi` shows a summary and the last push result.

Never commit a real token. `settings.txt` in the repo has it blank.

## Console

| Command | What it does |
|---|---|
| `poi` | Summary and last push status |
| `poi list [player]` | Claimed POIs, optionally one player's |
| `poi here` | The POI you stand in (in game) |
| `poi now` | Recompute and push right away |
| `poi reload` | Re-read `settings.txt` |

## What the page receives

`POST` with `Authorization: Bearer <Token>` and a JSON body (`schema: 1`): `server`, `generatedAt`, `settings`,
`pois[]` (id, name, displayName, tier, x, z, sizeX, sizeZ, trader, status, owner, coOwners, contested, claims,
claimedSince) and `players[]` (id, name, online, lastSeen, claimedPois). Player ids are a salted hash (the salt is
random and kept in `poi-state.txt`), never a Steam or platform id. A player or server name shaped like a
platform id or an IP address is sent as `null`, because the page refuses such a snapshot. With `ShowNames=off` the names are `null`;
with `ShowCoordinates=off` the positions and sizes are `null` and POI ids are hashed (`poi_...`).

## Not verified in game

Built and compiled against V3.4, never run on a server. First things to check: `poi` right after loading a
world, a claim in a POI, a claim next to one, two players (allied and not), and an expired claim.
