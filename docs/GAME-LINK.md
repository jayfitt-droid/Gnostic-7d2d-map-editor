# Gnostic Game Link: developer guide

Gnostic Game Link is a small mod for 7 Days to Die 3.4. A program on the same PC can use it to get the player's position, teleport the player, and add a temporary waypoint to a POI. Gnostic - 7D2D Map Editor uses it, and any other tool can use it too.

This guide is for developers. To install and use the mod with the editor, see the [user manual](USER-MANUAL.md#use-gnostic-game-link).

## Overview

- The mod listens on **127.0.0.1, TCP port 7790**. It accepts connections from the same PC only.
- A client connects, sends **one request line**, reads **one reply line**, and closes the connection. Each connection serves exactly one request.
- Requests are short ASCII text. Replies are one line of JSON, encoded as UTF-8 and ended with `\n`.
- The mod serves **one connection at a time**. The game handles each request on Unity's main thread, at most one per frame.
- The mod works in single-player and when the game on this PC is the host. When the player joins another game or a dedicated server, requests other than `STATUS` fail with `remote`.
- The game does not load the mod when Easy Anti-Cheat (EAC) is on (`SkipWithAntiCheat`).

The current protocol version is **1**.

## Coordinates

All positions use the game's world coordinates, the same numbers as the game's `teleport` console command:

- `x` grows to the east, `z` grows to the north.
- `0 0` is the centre of the world. On a world of size `S`, both axes run from `-S/2` to `S/2`.
- Requests take whole numbers (32-bit integers, with an optional `-` or `+` sign). Replies give decimal numbers.

To convert from a top-down map image of the world (pixel `px` from the left, `py` from the top, one pixel per metre):

```
x = px - S/2
z = S/2 - py
```

## Requests

A request is one line of printable ASCII (0x20 to 0x7E), with single spaces between the parts, ended by `\n` (`\r\n` is also accepted). The whole line, with its line end, is at most **128 bytes**. The mod must receive the line end within **2 seconds** of the connection, or it replies with `bad_request`.

Every request starts with `GNOSTICLINK`, then the protocol version:

```
GNOSTICLINK 1 STATUS
GNOSTICLINK 1 TELEPORT <x> <z> <facing>
GNOSTICLINK 1 WAYPOINT <x> <z> <prefab>
```

### STATUS

Asks what the game is doing. When the game answers, the reply has `"ok": true` in every state, and `state` tells you whether the other requests can work. (A `STATUS` request can still fail with `not_ready` or a request error, as below.)

```
GNOSTICLINK 1 STATUS
```

Reply when the player is in a world:

```json
{"ok":true,"protocol":1,"mod":"1.0.0","state":"ready","world":"My World","hosting":true,"player":{"x":-412.5,"z":1880.2,"yaw":135.0}}
```

| Field | Type | Meaning |
|---|---|---|
| `protocol` | number | The protocol version of the mod. |
| `mod` | string | The mod version, for example `1.0.0`. |
| `state` | string | One of the states below. |
| `world` | string | The world name in the game's settings (the world folder name). At the menu it can be empty or the last world selected. |
| `hosting` | bool | True when this game is single-player or the host. |
| `player` | object | Only when `state` is `ready`. `x` and `z` give the player's position. `yaw` gives the direction the player faces, in degrees clockwise from north (0 = north, 90 = east), from 0 to less than 360. |

| `state` | Meaning |
|---|---|
| `menu` | The game has no world open. |
| `loading` | A world is loading, or the player has not spawned yet. |
| `ready` | The player is in the world. `TELEPORT` and `WAYPOINT` can work. |
| `dead` | The player is dead. |
| `remote` | This game joined another game or a dedicated server. |

### TELEPORT

Teleports the local player to `x`, `z`. The game finds the ground height.

```
GNOSTICLINK 1 TELEPORT -412 1880 se
```

`facing` is the direction to look in after the teleport: `n`, `ne`, `e`, `se`, `s`, `sw`, `w` or `nw` (lowercase), or `-` to keep the current direction.

Reply: `{"ok":true}`

### WAYPOINT

Adds the game's temporary POI waypoint to the POI named `prefab` whose bounding box contains `x`, `z`. Send the position of any point inside the POI. Its centre is safest.

```
GNOSTICLINK 1 WAYPOINT -412 1880 farm_16
```

`prefab` is the prefab name, 1 to 64 characters from `A`–`Z`, `a`–`z`, `0`–`9`, `_` and `-`. Matching ignores case.

Reply: `{"ok":true,"message":"Temporary waypoint added. The game does not save it."}`

The waypoint shows on the game's map and compass. The game does not save it.

## Errors

A failed request gets a reply with `"ok": false`, an `error` code and a `message` for people:

```json
{"ok":false,"error":"off_map","message":"The destination is outside the world."}
```

Use `error` in your code. The `message` text can change.

| `error` | Requests | Meaning |
|---|---|---|
| `bad_request` | all | The line is not a valid request, is too long, or did not arrive within 2 seconds. |
| `unsupported_protocol` | all | The version number is not one the mod supports. The message names the version to use. |
| `not_ready` | all | The game did not take the request within 2 seconds, or it is shutting down. For TELEPORT and WAYPOINT, also: the player is not in a world. Try again later. |
| `dead` | TELEPORT, WAYPOINT | The player is dead. |
| `remote` | TELEPORT, WAYPOINT | This game joined another game or a dedicated server. |
| `busy` | TELEPORT | The player is still teleporting from an earlier request. |
| `off_map` | TELEPORT, WAYPOINT | The position is outside the world. |
| `poi_not_found` | WAYPOINT | No POI with that name contains the position. |
| `already_set` | WAYPOINT | That POI already has a temporary waypoint. |
| `failed` | TELEPORT, WAYPOINT | The game could not do it. The game's log has a line that starts with `[Gnostic Game Link]`. |

If nothing listens on the port, the connection fails. The game is not running, or the mod is not installed, or EAC stopped the game from loading it. The editor treats that as "Game Link is not available".

## Timing and good practice

- Connect with a short timeout (the editor uses 1 second), then allow up to about **5 seconds** for the reply. A request waits for the game's main thread. While the game loads, that can take a moment.
- Send one request per connection. Open a new connection for the next request.
- The mod serves one connection at a time and queues at most one more. Keep connections short, and poll `STATUS` no more often than about once a second. The editor polls every 3 seconds for its status icon, and once a second while it shows the player arrow.
- Check `state` from `STATUS` before you send `TELEPORT` or `WAYPOINT`, and compare `world` with the world your tool has open.
- Treat a missing reply, a reply over 4 KB or a reply that is not valid JSON as an error.

## Versioning

- Every request names the protocol version. A mod that does not support that version replies `unsupported_protocol`.
- `STATUS` replies give `protocol` (the version of the mod's protocol) and `mod` (the mod's own version).
- A change that would break existing clients gets a new protocol number. New fields can appear in replies without a new number, so ignore fields you do not know.

## Example clients

### Python

```python
import json
import socket

def game_link(request: str) -> dict:
    """Sends one Game Link request and returns the reply as a dict."""
    with socket.create_connection(("127.0.0.1", 7790), timeout=1.0) as s:
        s.settimeout(5.0)
        s.sendall(f"GNOSTICLINK 1 {request}\n".encode("ascii"))
        data = b""
        while not data.endswith(b"\n") and len(data) < 4096:
            chunk = s.recv(4096)
            if not chunk:
                break
            data += chunk
    return json.loads(data)

status = game_link("STATUS")
if status["state"] == "ready":
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
```

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
