# POI Registry (private)

Server-side only. Works out which player owns which POI from land claims, writes `poi-owners.json` next to
`settings.txt`, and can push the same JSON to a web page (`poi.azraelsmods.com`). Players install nothing.

This mod is **not released**: it has no `mod.json`, is not in `scripts/matrix/mod-matrix.json` or the release
workflow, and is not listed in the main README table. `build.ps1` builds it (`.\build.ps1 -Only PoiRegistry -Install`).

## How ownership is decided

- Claims come from `PersistentPlayerList.m_lpBlockMap`. A claim counts when the owner's protection is still valid
  (`World.IsLandProtectionValidForPlayer`, so offline expiry works like vanilla) and it is the primary claim block
  (`TEFeatureLandClaim.IsPrimary`). Its area is the block position +/- `(LandClaimSize - 1) / 2` on X and Z.
- POIs come from `DynamicPrefabDecorator.GetPOIPrefabs`, each with a bounding box.
- A claim counts for a POI when it overlaps the POI by at least `MinOverlapBlocks` (x * z).
- Owner = the player with a claim block inside the POI, else the biggest overlap. Allies of the owner are
  `coOwners`. Any non-allied player who also overlaps makes the POI `contested`.
- Nothing is patched. The mod polls every `IntervalSeconds` and only posts when the result changed, plus a
  heartbeat every `HeartbeatMinutes` so the page can tell the server is alive.

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
random and kept in `poi-state.txt`), never a Steam or platform id. With `ShowNames=off` the names are `null`;
with `ShowCoordinates=off` the positions and sizes are `null`.

## Not verified in game

Built and compiled against V3.4, never run on a server. First things to check: `poi` right after loading a
world, a claim in a POI, a claim next to one, two players (allied and not), and an expired claim.
