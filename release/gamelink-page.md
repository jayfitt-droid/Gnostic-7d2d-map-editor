# Gnostic Game Link

**An optional mod for 7 Days to Die that connects the game to Gnostic - 7D2D Map Editor. Teleport to any spot on the editor's map and take pictures of POIs. Play your world straight after export, and pin notes in the game that show up in the editor.**

## New in 1.1.0

- **Take pictures.** Right-click a POI in the editor and click **Take Picture**. The game takes a picture of the POI from above, with the HUD hidden, and the editor saves it next to your project.
- **Open the playtest save.** Export with **After export > Open the playtest save in the game**. The game opens a save named `<world> Playtest` on the new world, and you arrive at the centre of the editor's view.
- **Rebuild the area around the player.** Export, and the game makes the area around you again from the new world, in the same save.
- **Pins.** In the game, enter `pin` and a note in the console. The pin shows on the editor's map and in its **Pins** list.
- **Session token.** The mod accepts requests only from the editor on your PC.

**Gnostic Game Link 1.1.0 needs the editor v2.4.0 or later, and the editor v2.4.0 needs Game Link 1.1.0.** Update both together.

## What it does

**Teleport from the editor.** Right-click the map and click **Teleport Here**. You arrive in the game at that same spot.

**Check each issue in the game.** In the editor's Issues tab, click **Teleport** next to an issue to go straight to it.

**Take pictures.** Right-click a POI or a place on the map and click **Take Picture**. The game moves you near it and takes a picture from above, with the HUD hidden. The editor saves the PNG in a `<project name> pictures` folder next to the project. To take a group of pictures, use **Take pictures of selection** with the **Multi-select** tool, or **Take pictures...** in the Issues tab.

**Play your world after export.** In **Export World**, select **After export > Open the playtest save in the game**. If the game does not run, the editor starts it. If you have another save open, the editor asks first, and the game saves it and goes to the main menu. Then the game opens `<world> Playtest`, and you arrive at the centre of the editor's view. Your other saves do not change.

**Rebuild the area around the player.** In **Export World**, select **After export > Rebuild the area around the player** and set the radius. The game saves, and the editor writes the world. Then the game loads the same save and makes the area around you again from the new world. This changes your save: read "Back up your save before a rebuild" below.

**Pin notes in the game.** Press **F1** and enter `pin Check the bridge`, or only `pin`. Then open the project for that world. The editor shows each pin on the map and in the **Pins** list in the Issues tab, with **Teleport** and **Delete**. Game Link keeps the pins until you save the project, also when you close the game.

**Mark a POI.** Right-click a POI and click **Add Waypoint**. The game shows a temporary waypoint on its map and compass.

**See yourself on the map.** **Layers > Show player** shows a purple arrow where you are and the way you face. **Follow** keeps you in view while you walk.

**A plug in the status bar** shows the state of the link. Grey: the game does not reply. Yellow: the game replies, but it does not have this world open. Green: you host the game, and it has this world open. Red: the mod is older than the editor needs, or the session check fails. Point to the plug to see why.

The editor checks that the game has the same world open before it teleports you or shows you on the map.

## Requirements

- 7 Days to Die 3.4, with Easy Anti-Cheat (EAC) off
- Single-player, or a game that you host on the same PC
- [Gnostic - 7D2D Map Editor](https://7daystodiemods.com/mods/gnostic-7d2d-map-editor) v2.4.0 or later, on the same PC

Game Link does not work when you join another player's game or a dedicated server.

## Install

1. Close the game.
2. Download **[GnosticGameLink-1.1.0.zip](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/releases/latest/download/GnosticGameLink-1.1.0.zip)** from GitHub.
3. Extract the `GnosticGameLink` folder into `%AppData%\7DaysToDie\Mods`. Create the `Mods` folder if it does not exist. The folder `Mods\GnosticGameLink` must contain `ModInfo.xml` and `GnosticGameLink.dll`. If you have Game Link 1.0.0, replace its folder.
4. Start the game with EAC off. With EAC on, the game does not load the mod.

To uninstall the mod, delete the `GnosticGameLink` folder.

## Quick start

1. Open your world in the editor.
2. Press **Ctrl+E**. Click **Use the game's GeneratedWorlds folder**, then select **After export > Open the playtest save in the game**.
3. Click **Export**. The game starts and opens the playtest save.
4. In the editor, right-click the map and click **Teleport Here**.

The [user manual](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/blob/main/docs/USER-MANUAL.md#use-gnostic-game-link) explains each step.

## Back up your save before a rebuild

**Rebuild the area around the player** changes your save, and you cannot undo it. In the rebuilt area, the game removes player buildings, loot, land claims, bedrolls, vehicles and dropped backpacks. The area can be larger than the radius, because it includes whole POIs at the edge.

- Use the playtest save that the editor makes (`<world> Playtest`), not a save that you play for real.
- Before you rebuild in any other save, close the game and copy the save folder: `%AppData%\7DaysToDie\Saves\<world>\<save>`. To go back, put the copy back.
- Read the warning before you click **OK**. Click **Cancel** if you are not sure.

## Safe by design

- The mod listens only on your own PC (127.0.0.1, port 7790). Other computers cannot connect to it.
- Each time the game starts, the mod makes a new session token in your user folder. It accepts only requests that send this token, so a program that cannot read your files cannot control the game.
- It does not change your world or other players. Only **Rebuild the area around the player** changes your save (see above). **Open the playtest save** uses its own save and does not change your other saves.
- Waypoints are temporary. The game does not save them.
- It needs no other mods and changes no game files.

## For mod and tool authors

Your own tools can use Game Link too. The protocol is one line of text over a local TCP connection, with a JSON reply. Each request sends the session token from `%AppData%\7DaysToDie\GnosticGameLink\session-token`. The [developer guide](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/blob/main/docs/GAME-LINK.md) describes each request and reply, with a short example client. The source is in the editor's [repository](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/tree/main/mod/GnosticGameLink), under the GPL-3.0 licence.

---

## Bugs and feedback

To report a bug, use the [bug report form](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/new?template=bug_report.yml) on GitHub, and say that you use Game Link. No GitHub account? Post in the comments with the editor version (**Help > About**), the game version, and what happened. The game's log (`%AppData%\7DaysToDie\logs`) has lines that start with `[Gnostic Game Link]`.

## Support my work

The editor and the mod are free, and I build them in my spare time. If they save you time, you can buy me a coffee on [Ko-fi](https://ko-fi.com/jayfitt). Thank you!

---

*This is a fan-made mod. It is not made by or affiliated with The Fun Pimps.*
