# Gnostic Game Link: developer guide

Gnostic Game Link is a small mod for 7 Days to Die 3.4. A program on the same PC can use it to open or leave a save, rebuild chunks in a save, get the player's position, teleport the player, add a temporary waypoint to a POI, take a picture, and collect playtest pins. Gnostic - 7D2D Map Editor uses it, and any other tool can use it too.

This guide is for developers. To install and use the mod with the editor, see the [user manual](USER-MANUAL.md#use-gnostic-game-link).

## Overview

- The mod listens on **127.0.0.1, TCP port 7790**. It accepts connections from the same PC only.
- A client connects, sends **one request line**, reads **one reply line**, and closes the connection. Each connection serves exactly one request.
- Requests are short ASCII text. Replies are one line of JSON, encoded as UTF-8 and ended with `\n`.
- The mod serves **one connection at a time**. The game starts each request on Unity's main thread. A picture takes several frames and completes even if the client disconnects.
- The mod works in single-player and when the game on this PC is the host. `STATUS`, `PINS` and `PINSACK` work in any game state.
- The game does not load the mod when Easy Anti-Cheat (EAC) is on (`SkipWithAntiCheat`).

The current protocol version is **2**, supported by Gnostic Game Link 1.1.0.

## Session token

Each time the game starts, the mod generates a new token from 16 cryptographically random bytes. It stores the token as exactly 32 lowercase hexadecimal ASCII characters in `%APPDATA%\7DaysToDie\GnosticGameLink\session-token`. The file has no line ending. It inherits the AppData folder's permissions. The file stays after exit and the mod replaces it at the next start. Read it before each request to pick up the current token.

The mod verifies the token before it checks the protocol version, command, or arguments. This blocks programs that can connect to the local port but cannot read the user's files. A program that runs as the same user can read the token and use Game Link.

## Coordinates

All positions use the game's world coordinates, the same numbers as the game's `teleport` console command:

- `x` grows to the east, `z` grows to the north.
- `y` is the height in blocks.
- `0 0` is the centre of the world. On a world of size `S`, both axes run from `-S/2` to `S/2`.
- Requests take whole numbers (32-bit integers, with an optional `-` or `+` sign). Replies give decimal numbers.

To convert from a top-down map image of the world (pixel `px` from the left, `py` from the top, one pixel per metre):

```
x = px - S/2
z = S/2 - py
```

## Requests

A request is one line of printable ASCII (0x20 to 0x7E), with single spaces between the parts, ended by `\n` (`\r\n` is also accepted). The whole line, with its line end, is at most **160 bytes**. The mod must receive the line end within **2 seconds** of the connection.

Every request starts with `GNOSTICLINK`, the protocol version, and the session token:

```
GNOSTICLINK 2 <token> STATUS
GNOSTICLINK 2 <token> TELEPORT <x> <z> <facing>
GNOSTICLINK 2 <token> WAYPOINT <x> <z> <prefab>
GNOSTICLINK 2 <token> PICTURE <camX> <camY> <camZ> <lookX> <lookY> <lookZ>
GNOSTICLINK 2 <token> LEAVE
GNOSTICLINK 2 <token> OPEN <id>
GNOSTICLINK 2 <token> REBUILD <id>
GNOSTICLINK 2 <token> PINS
GNOSTICLINK 2 <token> PINS <world-key>
GNOSTICLINK 2 <token> PINS <world-key> <after-id>
GNOSTICLINK 2 <token> PINSACK <id>
```

### STATUS

Asks what the game is doing. When the game answers, the reply has `"ok": true` in every state, and `state` tells you whether the other requests can work. (A `STATUS` request can still fail with `not_ready` or a request error, as below.)

```
GNOSTICLINK 2 <token> STATUS
```

Reply when the player is in a world:

```json
{"ok":true,"protocol":2,"mod":"1.1.0","state":"ready","world":"My World","save":"My World Playtest","hosting":true,"otherPlayers":false,"player":{"x":-412.5,"z":1880.2,"yaw":135.0}}
```

| Field | Type | Meaning |
|---|---|---|
| `protocol` | number | The protocol version of the mod. |
| `mod` | string | The mod version, for example `1.1.0`. |
| `state` | string | One of the states below. |
| `world` | string | The world name in the game's settings (the world folder name). At the menu it can be empty or the last world selected. |
| `save` | string | The name of the open save. Empty at the menu. |
| `hosting` | bool | True when this game is single-player or the host. |
| `otherPlayers` | bool | In the ready state, true if remote players are connected or there is more than one local player. |
| `player` | object | Only when `state` is `ready`. `x` and `z` give the player's position. `yaw` gives the direction the player faces, in degrees clockwise from north (0 = north, 90 = east), from 0 to less than 360. |
| `op` | object | The latest menu operation, with `id`, `kind` (`leave`, `open` or `rebuild`), `state` and `reason`. `chunks` gives the rebuild's chunk count once the reset succeeds. Absent or null when there is no operation. |
| `blocked` | object | The target on the Continue page when Start is disabled, with `world`, `save` and `reason`. This also reports a blocked save from the game's launch automation. Absent or null when there is no blocked target. |

| `state` | Meaning |
|---|---|
| `menu` | The game has no world open. |
| `loading` | A world is loading, or the player has not spawned yet. |
| `ready` | The player is in the world. `TELEPORT`, `WAYPOINT` and `PICTURE` can work. |
| `dead` | The player is dead. |
| `remote` | This game joined another game or a dedicated server. |

### LEAVE

Saves and leaves the current hosted game through its normal exit. The game must be ready and safe to leave, with no other players connected and no world save in progress. The reply gives an operation ID, for example `{"ok":true,"id":"0123456789abcdef"}`. The operation's `kind` is `leave`. Poll STATUS until the same ID reaches `at_menu` before writing world files. The intermediate state is `leaving`.

### OPEN

Creates or continues a save from the game menu. The client writes a UTF-8 file named `open-<id>.txt` beside `session-token`, then sends `OPEN <id>`. Use 16 lowercase hexadecimal characters for the ID. The file is at most 4096 bytes and has four lines in this order.

1. Expiry as Unix seconds in UTC, two minutes after the client writes the request.
2. `create` to allow first-time creation or `continue` to require an existing save. The game continues an exact existing match in either case.
3. The world folder name.
4. The save name.

The mod consumes the file once and deletes it before taking action. It uses the game's menu to find the world, select the exact save or set up a new game, and press Start. The reply echoes the operation ID. Poll STATUS for that ID with `kind` set to `open`. Its state is `opening` until the target save is ready, then `done`. Check the returned world and save before sending TELEPORT.

Either operation can report `failed`, with a `reason` such as `players`, `busy`, `world_not_found`, `save_not_found`, or `timeout`. OPEN can also report `blocked` with a reason such as `older_version`, `newer_version`, `world_not_found`, or `start_disabled`.

### REBUILD

Continues the exact save from the preceding Game Link LEAVE in the same game session. Capture the target save and player position from STATUS while the save is ready. Wait for LEAVE to reach `at_menu` and write the new world files before sending REBUILD.

Write a UTF-8 file named `rebuild-<id>.txt` beside `session-token`, then send `REBUILD <id>`. Use 16 lowercase hexadecimal characters for the ID. The file is at most 4096 bytes and has four lines in this order.

1. Expiry as Unix seconds in UTC, two minutes after the client writes the request.
2. The world folder name.
3. The save name.
4. The centre and radius as `x z radius`, with one space between values. Use invariant decimal game coordinates and an integer radius from 16 to 512 metres.

The mod consumes the file once and echoes the operation ID on acceptance. It selects the existing save on the Continue page, reloads it, forces a reset of the selected chunks and returns the player to the captured horizontal position. The area follows chunk boundaries and includes whole POIs from the previous and new layouts. Confirm the loss of buildings, loot and land claims in that area before sending LEAVE.

Poll STATUS for the returned ID with `kind` set to `rebuild`. Its state is `rebuilding` until the player is ready again, then `done`. On success, `chunks` is the size of the selected chunk set. The operation uses the same save selection failure and blocked reasons as OPEN. It can also fail with `players` if other players join before the reset or `reset_failed` if the game could not complete the reset. Inspect the game log after `reset_failed`, as the reset may have started.

### TELEPORT

Teleports the local player to `x`, `z`. The game finds the ground height.

```
GNOSTICLINK 2 <token> TELEPORT -412 1880 se
```

`facing` is the direction to look in after the teleport: `n`, `ne`, `e`, `se`, `s`, `sw`, `w` or `nw` (lowercase), or `-` to keep the current direction.

Reply: `{"ok":true}`

### WAYPOINT

Adds the game's temporary POI waypoint to the POI named `prefab` whose bounding box contains `x`, `z`. Send the position of any point inside the POI. Its centre is safest.

```
GNOSTICLINK 2 <token> WAYPOINT -412 1880 farm_16
```

`prefab` is the prefab name, 1 to 64 characters from `A`–`Z`, `a`–`z`, `0`–`9`, `_` and `-`. Matching ignores case.

Reply: `{"ok":true,"message":"Temporary waypoint added. The game does not save it."}`

The waypoint shows on the game's map and compass. The game does not save it.

### PICTURE

Places the camera at `camX camY camZ` and points it at `lookX lookY lookZ`. Both points must be inside the horizontal world bounds and must differ. The camera can go above the buildable blocks. Heights may extend beyond the vertical world bounds by up to the larger horizontal world span.

```
GNOSTICLINK 2 <token> PICTURE -412 115 1820 -412 65 1880
```

The game hides the HUD and holds the camera in place for at least four seconds. During this time, it requests full detail for the chunks under the target POI or a 32 m square around an open spot. When those chunks have drawn and stayed ready for a second, it captures the window as a PNG at the end of a frame. If the chunks are still drawing after 20 seconds, it returns `picture_render_timeout`.

The capture uses a 50-degree vertical field of view and widens it for camera aspect ratios below 1.6. The game restores the camera and HUD before replying. Teleport near the camera first so the game loads the chunks you want to see.

The reply gives the file name. The mod writes the PNG to `%APPDATA%\7DaysToDie\GnosticGameLink\Pictures`. The name has the form `gnostic-yyyyMMdd-HHmmss-fff.png` and uses UTC. The editor accepts only this name format. It deletes the game's copy after it copies the picture to the project folder. If the editor cannot delete the game's copy, the picture stays in both folders.

```json
{"ok":true,"file":"gnostic-20261123-123456-789.png"}
```

### PINS and PINSACK

Enter `pin <note>` in the local game console while playing in single-player or as host. You can also enter `pin` by itself. The mod records the player position, heading, world name, save name, UTC time, and optional note. It removes line breaks and other control characters and keeps up to 200 UTF-16 code units of the note.

`PINS` returns the oldest queued pins. Pass a world key to read only that world's queue. The key is the lowercase hexadecimal SHA-256 digest of the world name converted to invariant uppercase and encoded as UTF-8. Use the exported folder name as the world name. The reply contains up to eight pins and fits within the 4096-byte reply limit, including the newline. An empty queue returns `{"ok":true,"pins":[]}`.

```json
{"ok":true,"pins":[{"id":"ae0123456789abcdef0123456789abcd","x":-4.5,"y":65,"z":18.25,"yaw":135,"world":"Test World","save":"Test Save","time":"2026-11-23T12:34:56+00:00","note":"Check the \"bridge\""}]}
```

`PINS` leaves the queue intact. Pass the last received ID as `after-id` to collect later pins from the same world before you save. If that ID is gone from the queue, the reply starts with the oldest remaining pin for that world. Use IDs to skip pins you already collected.

After you save the pins, send `PINSACK <id>` with the last saved pin's ID. The ID is a nonzero GUID represented by 32 lowercase hexadecimal characters. The mod removes that pin and all earlier pins in the same world. To acknowledge an unfiltered batch, send one acknowledgment per saved world. A repeated acknowledgment for a removed ID still returns `{"ok":true}`.

The mod stores the queue in `%APPDATA%\7DaysToDie\GnosticGameLink\pins.tsv`. Each tab-separated line stores the ID, X, Y, Z, heading, ISO 8601 timestamp, and Base64-encoded UTF-8 world name, save name and note. The file survives a game restart. The mod skips damaged lines.

## Errors

A failed request gets a reply with `"ok": false`, an `error` code and a `message` for people:

```json
{"ok":false,"error":"off_map","message":"The destination is outside the world."}
```

Use `error` in your code. The `message` text can change.

| `error` | Requests | Meaning |
|---|---|---|
| `invalid_token` | all | The session token is missing or incorrect. Read the file again, restart the game, or check that the editor and mod versions match. |
| `bad_request` | all | The line is not a valid request, is too long, or did not arrive within 2 seconds. |
| `unsupported_protocol` | all | The version number is not one the mod supports. The message names the version to use. |
| `not_ready` | all | The game did not take the request in time, is shutting down, or is in the wrong state for that request. OPEN and REBUILD need the menu. |
| `dead` | TELEPORT, WAYPOINT, PICTURE, LEAVE | The player is dead. |
| `remote` | TELEPORT, WAYPOINT, PICTURE, LEAVE | This game joined another game or a dedicated server. |
| `busy` | actions | Another operation is in progress, or the game cannot safely leave the current save yet. |
| `players` | LEAVE | Other players are connected. |
| `expired` | OPEN, REBUILD | The request file expired. |
| `save_not_found` | REBUILD | The requested save does not match the one left by the preceding LEAVE. |
| `off_map` | TELEPORT, WAYPOINT, PICTURE | A position is outside the world bounds for the request. |
| `poi_not_found` | WAYPOINT | No POI with that name contains the position. |
| `already_set` | WAYPOINT | That POI already has a temporary waypoint. |
| `failed` | all | The game could not process the request. |
| `picture_failed` | PICTURE | The game could not capture or write the PNG. |
| `picture_render_timeout` | PICTURE | The target chunks did not finish drawing within 20 seconds. |

If nothing listens on the port, the connection fails. The game is not running, or the mod is not installed, or EAC stopped the game from loading it. The editor treats that as "Game Link is not available".

## Timing and good practice

- Connect with a short timeout (the editor uses 1 second), then allow about **5 seconds** for the reply, or **30 seconds** for `PICTURE`. A request waits for the game's main thread. While the game loads, that can take a moment.
- Send one request per connection. Open a new connection for the next request.
- The mod serves one connection at a time and queues at most one more. Keep connections short, and poll `STATUS` no more often than about once a second. The editor polls every 3 seconds for its status icon, and once a second while it shows the player arrow.
- Check `state` from `STATUS` before you send `TELEPORT`, `WAYPOINT` or `PICTURE`, and compare `world` with the world your tool has open.
- Treat a missing reply, a reply over 4 KB or a reply that is not valid JSON as an error.

## Versioning

- For requests with the current token, the mod replies `unsupported_protocol` if it does not support the requested version.
- `STATUS` replies give `protocol` (the version of the mod's protocol) and `mod` (the mod's own version).
- A change that would break existing clients gets a new protocol number. New fields can appear in replies without a new number, so ignore fields you do not know.

## Example clients

### Python

```python
import json
import os
from pathlib import Path
import re
import socket

token_path = Path(os.environ["APPDATA"]) / "7DaysToDie" / "GnosticGameLink" / "session-token"

def game_link(request: str) -> dict:
    """Sends one Game Link request and returns the reply as a dict."""
    token = token_path.read_bytes().decode("ascii")
    if not re.fullmatch(r"[0-9a-f]{32}", token):
        raise ValueError("Invalid Game Link token file. Restart the game.")
    with socket.create_connection(("127.0.0.1", 7790), timeout=1.0) as s:
        s.settimeout(5.0)
        s.sendall(f"GNOSTICLINK 2 {token} {request}\n".encode("ascii"))
        data = b""
        while not data.endswith(b"\n") and len(data) < 4096:
            chunk = s.recv(4096)
            if not chunk:
                break
            data += chunk
    return json.loads(data)

status = game_link("STATUS")
if not status["ok"]:
    print(status["message"])
elif status["state"] == "ready":
    p = status["player"]
    print(f"{status['world']}: x={p['x']:.0f} z={p['z']:.0f}, facing {p['yaw']:.0f} degrees")
    print(game_link("TELEPORT -412 1880 se"))
else:
    print("Game state:", status["state"])
```

### PowerShell

[`mod/GnosticGameLink/test-link.ps1`](../mod/GnosticGameLink/test-link.ps1) sends each request from the command line and prints the reply:

```powershell
./test-link.ps1 Status
./test-link.ps1 Teleport -X -412 -Z 1880 -Facing se
./test-link.ps1 Waypoint -X -412 -Z 1880 -PrefabName farm_16
./test-link.ps1 Picture -X -412 -Y 115 -Z 1820 -LookX -412 -LookY 65 -LookZ 1880
```

With the game running, `./test-link.ps1 -CheckLoopback` checks that port 7790 has exactly one listening address, `127.0.0.1`. Run `./test-link.ps1 Status -NoToken` to check that the mod replies with `invalid_token`.

### C#

The editor's own client is [`GameLinkClient`](../src/MapEditor.Core/GameLink/GameLinkClient.cs) in `MapEditor.Core`.

## Build the mod

The source is in [`mod/GnosticGameLink`](../mod/GnosticGameLink). It targets .NET Framework 4.8 and references the game's own assemblies. You need the .NET Framework 4.8 Developer Pack and the game installed. It shares [`GameLinkProtocol.cs`](../src/MapEditor.Core/GameLink/GameLinkProtocol.cs) with the editor, so both read requests the same way.

```powershell
mod/GnosticGameLink/build.ps1 -GamePath "C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die"
```

The script builds the mod and writes `mod/GnosticGameLink/dist/GnosticGameLink-<version>.zip`. Add `-Install` to copy the mod into `%AppData%\7DaysToDie\Mods`. The version comes from `ModInfo.xml`. The [README](../README.md#build-the-game-link-mod) tells how to keep the game folder in a local file instead of `-GamePath`.

## Licence

The mod is part of the editor's repository and is under the same licence, [GPL-3.0](../LICENSE).
