# Gnostic Game Link

**An optional mod for 7 Days to Die that connects the game to Gnostic - 7D2D Map Editor. Teleport to any spot on the editor's map, mark a POI with a waypoint, and see where you are while you edit.**

## What it does

**Teleport from the editor.** Right-click the map and click **Teleport Here**. You arrive in the game at that spot, facing the way you choose.

**Check each issue in the game.** In the editor's Issues tab, click **Teleport** next to an issue to go straight to it.

**Mark a POI.** Right-click a POI and click **Add Waypoint**. The game shows a temporary waypoint on its map and compass.

**See yourself on the map.** **Layers > Show player** shows a purple arrow where you are and the way you face. **Follow** keeps you in view while you walk.

**A plug in the status bar** turns green when the game replies, so you always know if the link works.

The editor checks that the game has the same world open before it teleports you or shows you on the map.

## Requirements

- 7 Days to Die 3.4, with Easy Anti-Cheat (EAC) off
- Single-player, or a game that you host on the same PC
- [Gnostic - 7D2D Map Editor](https://7daystodiemods.com/mods/gnostic-7d2d-map-editor) v2.3.0 or later, on the same PC

Game Link does not work when you join another player's game or a dedicated server.

## Install

1. Close the game.
2. Download **[GnosticGameLink-1.0.0.zip](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/releases/latest/download/GnosticGameLink-1.0.0.zip)** from GitHub.
3. Extract the `GnosticGameLink` folder into `%AppData%\7DaysToDie\Mods`. Create the `Mods` folder if it does not exist. The folder `Mods\GnosticGameLink` must contain `ModInfo.xml` and `GnosticGameLink.dll`.
4. Start the game with EAC off. With EAC on, the game does not load the mod.

To uninstall the mod, delete the `GnosticGameLink` folder.

## Quick start

1. Export your world from the editor into `GeneratedWorlds` (Ctrl+E).
2. Start a game on that world, in single-player or as the host.
3. In the editor, right-click the map and click **Teleport Here**.

The [user manual](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/blob/main/docs/USER-MANUAL.md#use-gnostic-game-link) explains each step.

## Safe by design

- The mod listens only on your own PC (127.0.0.1, port 7790). Other computers cannot connect to it.
- It accepts only three short requests: status, teleport and waypoint. It does not change your world, your saves or other players.
- Waypoints are temporary. The game does not save them.
- It needs no other mods and changes no game files.

## For mod and tool authors

Your own tools can use Game Link too. The protocol is one line of text over a local TCP connection, with a JSON reply. The [developer guide](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/blob/main/docs/GAME-LINK.md) describes each request and reply, with a short example client. The source is in the editor's [repository](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/tree/main/mod/GnosticGameLink), under the GPL-3.0 licence.

---

## Bugs and feedback

To report a bug, use the [bug report form](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/new?template=bug_report.yml) on GitHub, and say that you use Game Link. No GitHub account? Post in the comments with the editor version (**Help > About**), the game version, and what happened. The game's log (`%AppData%\7DaysToDie\logs`) has lines that start with `[Gnostic Game Link]`.

## Support my work

The editor and the mod are free, and I build them in my spare time. If they save you time, you can buy me a coffee on [Ko-fi](https://ko-fi.com/jayfitt). Thank you!

---

*This is a fan-made mod. It is not made by or affiliated with The Fun Pimps.*
