# Gnostic - 7D2D Map Editor: User Manual

This manual is for version 2.0.0. It tells you how to make and change worlds for 7 Days to Die.

## Contents

1. [What the editor does](#1-what-the-editor-does)
2. [Before you start](#2-before-you-start)
3. [The window](#3-the-window)
4. [Worlds and projects](#4-worlds-and-projects)
5. [The tools](#5-the-tools)
6. [The Generate panel](#6-the-generate-panel)
7. [The six stages](#7-the-six-stages)
8. [Example workflows](#8-example-workflows)
   - [Guided tours](#guided-tours)
9. [Validate and export the world](#9-validate-and-export-the-world)
10. [Keyboard shortcuts](#10-keyboard-shortcuts)
11. [Words in this manual](#11-words-in-this-manual)
12. [Report a bug](#12-report-a-bug)

## 1. What the editor does

The editor makes and changes worlds for 7 Days to Die. You can open a world that the game generated, or you can make a new world.

You can make a world in two ways:

- **With the tools.** You shape the ground, paint biomes, draw roads and put each POI, town and spawn point on the map.
- **With the Generate panel.** The editor makes the world in six stages, the way the game's world generator does.

**You can use the tools and the stages together.** For example, generate the terrain, then change one mountain with the Raise tool. Or draw a city with the Town tool, then let the Highways stage connect it to the other towns. Every stage and every tool changes the same world.

The editor writes a world folder that the game loads directly. The game does not need a mod.

The editor reads stamps, prefabs and biome colours from your copy of the game. It does not include game files.

The editor also reads prefabs from your mods and from `%APPDATA%\7DaysToDie\LocalPrefabs`. The game's prefab editor saves your prefabs in that folder. If two prefabs have the same name, the editor uses the prefab from `LocalPrefabs`, as the game does.

## 2. Before you start

You need:

- Windows 10 or 11 (64-bit).
- 7 Days to Die, version 3.x.

### Install the editor

Download the editor from the [releases page](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/releases). Use one of these files:

- `Gnostic-7D2DMapEditor-win-Setup.exe` installs the editor for your Windows user. It does not need administrator rights.
- `Gnostic-7D2DMapEditor-win-Portable.zip` does not install. Unzip it and run `Gnostic - 7D2D Map Editor.exe`.

Windows SmartScreen can show "Windows protected your PC". The editor is not signed. Click **More info**, then click **Run anyway**.

The installed editor looks for a new version at start. If a new version is available, the editor opens a dialog.
Click **Install** to download the new version and restart the editor. The editor asks about unsaved changes, and about changes in Land mode, before the download.
If the download fails, the editor stays open and keeps your work.
Click **Later** to close the dialog. To turn off this check, clear **Settings > Check for updates at start**.
To look for a new version, click **Help > Check for Updates...**. This menu item is unavailable while the editor is busy.

### Set the game folder

The editor finds the game through Steam. If it cannot find the game, click **Settings > Game Folder**. Select the folder that contains the `Data` folder.

## 3. The window

The editor opens where you closed it if that screen is still connected.

The window has five areas.

| Area | What it holds |
|---|---|
| **Ribbon** (top) | The tools in groups: Navigate, Terrain, Paint and Build. It also holds the **Palette** (biome colours and road surfaces), **Layers** (what the map shows), **Validate** and **Export**. |
| **Tool options** (below the ribbon) | The settings for the tool that you use, and a hint in one line. Click **∨** at the right of the ribbon to hide them. |
| **Left rail** | The **size** and **strength** sliders for the brush. |
| **Side panel** (right) | Five tabs: **Prefabs**, **Placed**, **Issues**, **World** and **Generate**. Drag the left edge of the panel to make it wider or narrower. |
| **Status bar** (bottom) | The cursor position, height, biome and prefab under the cursor. It also shows the brush size, the world size, the latest message and the zoom. |

You can also show the **Messages** panel under the map. Refer to [Read the messages](#read-the-messages).

On a short window, the ribbon becomes one row of icons. To keep the ribbon full or compact, click **View > Ribbon**.

The editor shows the game's own names, for example **Pine Forest** and **Old West**, not `pine_forest` and `oldwest`. A POI shows its game name with its file name below it, and search finds either name. Saved files keep the game's file names.

### Choose a theme

The editor has two themes. Both themes use the light or dark setting of Windows.

| Theme | What it shows |
|---|---|
| **Flat** | The ribbon and the side panel that this manual shows. This is the default theme. |
| **Modern** | A slim title bar, one row of named tools and a side bar with icons. |

To change the theme:

1. Click **Settings > Theme**, then click **Flat** or **Modern**.
2. Click **Restart now**. If the world has changes, the editor asks you to save them.

The tools and the panels do the same work in the two themes. The Modern theme has these differences:

| Area | In the Modern theme |
|---|---|
| **Title bar** | It holds the menus. The **Side bar** button shows or hides the side bar. The **Layout** button holds the layout settings. |
| **Toolbar** | Each tool shows its name. **Layers**, **Generate**, **Validate**, **Export** and **Package** are at the right. In a narrow window, the toolbar shows only icons. |
| **Tool options** | This row also holds the **size** and **strength** sliders, and the biome colours and road surfaces. |
| **Side bar** | Click an icon to open a view. Click the icon of the open view to close the side bar. To move the side bar, click **View > Side Bar on the Right**. |
| **Status bar** | It shows the number of errors and warnings. Click the numbers to open the **Issues** view. |

### Move around the map

- Turn the mouse wheel, or press **+** and **-**, to zoom.
- Press **Home** to see the whole map.
- Drag with the right mouse button to move the map. This works with every tool.

### Find a placed prefab

The **Placed** tab lists every POI, street tile and part in your world.

1. Click the **Placed** tab in the side panel.
2. Type a name in the search box. When the **Placed** tab is open, **Ctrl+F** puts the cursor in its search box. You can also filter by type. The map shows a ring around each prefab that matches.
3. Click a row to zoom to its prefab. Each row shows the biome at the centre of the prefab.
4. To select the prefab, double-click its row or press Enter.

### Read the messages

The status bar shows only the latest message. The **Messages** panel shows all the messages of this session. Each line shows the time of the message.

- To show or hide the panel, click **View > Messages**, or click the **Messages** button in the status bar, at the right of the message. You can also press **Ctrl+M**. When you start the editor again, the panel is open or closed as before, and it has the same height.
- Drag the top edge of the panel to make it taller or shorter.
- The newest message is at the bottom. The panel scrolls to each new message. If you scroll up to read an older message, the panel does not move.
- A circle with an X marks an error, for example **Autosave failed**. A triangle marks a warning, for example a setting that the editor could not save.
- Autosave messages show as dim lines. They do not replace the message in the status bar.
- A message that shows progress uses one line, for example **Drawing the map image... 40%**. The editor updates that line.
- Click **Copy** to copy all the messages as text. To copy only some messages, select them (Ctrl+click or Shift+click), then click **Copy** or press **Ctrl+C**.
- Click **Clear** to remove all the messages.

The editor does not save the messages in the project. The panel keeps the last 1,000 messages.

### Go to a place in the game

You can copy a command that moves you to a place on the map when you play the world.

1. Right-click the place on the map. Do not move the mouse while you click.
2. Click **Copy teleport command**, then select the direction that you want to look in. The menu shows each command.
3. In the game, press **F1** to open the console. Paste the command and press Enter.

The game puts you on the ground at that place. The numbers in the command are the same as the numbers in the status bar.

#### Use Gnostic Game Link

Gnostic Game Link is an optional mod for 7 Days to Die 3.4. It connects the editor and the game on the same PC. This editor uses Gnostic Game Link 1.1.0.

1. Close the game. Download `GnosticGameLink-<version>.zip` from the editor's [release page](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/releases/latest).
2. Extract the `GnosticGameLink` folder into `%AppData%\7DaysToDie\Mods`. Create the `Mods` folder if you need it. The folder `Mods\GnosticGameLink` must contain `ModInfo.xml` and `GnosticGameLink.dll`.
3. Start the game with Easy Anti-Cheat (EAC) off. Play in single-player or host the game on this PC.

##### Teleport and add a waypoint

1. Open the same world in the editor and in the game. Export the world first if it has no game world folder.
2. Right-click a place in the editor. Click **Teleport Here** to go to the place in the game.
3. To mark a POI, right-click the POI and click **Add Waypoint**. The game shows a temporary waypoint on the map and compass. The game does not save the waypoint.

The waypoint option shows for POI names of up to 64 characters. The name can use letters A to Z, digits, underscores and hyphens.

The editor compares the world in the game with the world folder you opened or last exported to. If they differ, the editor asks if you want to continue. Check the world names before you continue.

If the editor cannot connect to Game Link, it shows a message. You can still use **Copy teleport command**.

Each time you start the game, Game Link makes a new token. The editor uses this token to connect. If you get a token error, start the game again. If the problem continues, install the mod that matches the editor.

If you installed Game Link, a plug icon appears in the status bar. The icon is grey if the game does not reply. It is green if you host the game and the game has this world open. It stays green if the player dies. It is yellow at the main menu, while the game loads, or if the game has a different world open. It is also yellow if you join another player's game or if you have no export folder. It turns red if the game replies but the mod is older than the editor needs, or if the mod rejects the session. Install Gnostic Game Link 1.1.0, or start the game again. Point to the icon to read the connection status.

After a teleport, the status bar tells you if the editor has changes since export. Export again to use the latest terrain. A saved game keeps the terrain in chunks that it already generated.

##### Take pictures

Save the project first. Right-click a POI or a place on the map and click **Take Picture**. The game moves the player near the target. It takes a close view from above with the HUD hidden.

The camera faces the front of a POI if there is room. The game waits for the area to draw before it takes the picture. The editor shows an error if this takes more than 20 seconds.

The editor saves the PNG in a folder next to the `.7dmap` file. For `My World.7dmap`, the folder is `My World pictures`. The status bar shows where the editor saved the picture.

After the editor saves the picture, it deletes the game's copy. If the editor cannot delete the game's copy, the picture stays in both folders.

- To take a picture of each selected POI, use the **Multi-select** tool. Right-click the map and click **Take pictures of selection**.
- To take a picture of each issue, click **Validate**. In the **Issues** tab, click **Take pictures...**. An issue with no place on the map counts as a failed picture.

Press Esc in the editor to stop a group of pictures. If the game is taking a picture, it completes that picture first. The status bar shows how many pictures the editor saved and how many failed.

#### Show the player on the map

Open **Layers** and select **Show player**. When the game has the same world open, a purple arrow shows where you are and which way you face. The arrow updates about once a second.

Select **Follow** to keep the player in view. Any change you make to the view turns off **Follow**. You can drag the map, zoom, or go to an issue. If Game Link loses its connection, the arrow fades and the status bar tells you. The arrow comes back when the game replies from the matching world. Clear **Show player** to hide the arrow.

##### Add playtest pins

1. In the game, press **F1** to open the console. Enter `pin Check the bridge` to add a pin with a note. Enter `pin` to add a pin with no note.
2. In the editor, open the project for the same world. Open **Issues** and find **Pins**. Each row shows the note, coordinates, and time. The map shows a marker for each pin.
3. Double-click a pin or select it and press **Enter** to find it on the map. Click **Teleport** to go to the pin in the game. Click **Delete** to delete the pin.
4. Save the project to keep your pins. Game Link keeps its copy of each pin until you save the project.

You can add pins before you open the editor. Game Link keeps the pins when you close the game. Undo does not change pins.

## 4. Worlds and projects

### Make a new world

1. Click **New World** (Ctrl+N).
2. Type a name, and set the size, the ground height and the seed.
3. Under **Land shape**, select one of these:
   - **Land to the edges** puts land on all of the map. Then set the **Edges**.
   - **One island** makes one island with ocean on all sides.
   - **Several islands** makes a group of islands with ocean between them.
   - **Peninsula** makes land that touches one edge. Select the edge in **Joined to**.
   - **Draw your own** opens Land mode so you can place land and water yourself.

   For an island or a peninsula, set **Land** (the share of the map that is land) and **Rough coast**. For several islands, also set **Islands**.
4. Under **Biomes**, select one of these:
   - **One biome** paints one biome on the whole map.
   - **All biomes (generated layout)** makes a layout of all the biomes, as the game does. Select where the biomes start: forest or wasteland in the middle, a ring, two rings, or a line. Then set the **Share of the map** for each biome.
     For **Several islands**, you can also select **One biome per island**. Each island then gets one biome. The forest island is the island with a size near the forest share, near the middle. The islands farther out get the harder biomes in this order: burnt forest, desert, snow, wasteland. The editor tries to give each biome its **Share of the map** of the land. The island sizes are random, so the match is not exact. If there are as many islands as biomes, or more, each biome gets at least one island. A biome with a share of 0 is not used.
5. Click **Create**. For **Draw your own**, follow the Land mode steps below. For the preset shapes, the editor opens the Generate panel at stage 1.

The same seed always gives the same biome layout. A biome in the middle stays away from the map edges, so it can get a little less than its share.

On an island world, the share of each biome is a share of the land. The coast seed comes from the world seed.

### Draw land in Land mode

1. Click **Rectangle** in the Land mode toolbar and drag a box on the map. The preview shows the coast as you draw. Draw several boxes to make islands. Land joins where two coasts meet. To make land run off the map, drag the box to the map edge.
2. Hold **Alt** while you drag a box over land to make a water box. Use it to make a bay or an inland sea.
3. Click **Select boxes** next to **Rectangle**, then click a box on the map. You can also select a box from the **Box** list in the **Land** panel. Drag inside the box to move it. Drag a corner or side handle to resize it. The pointer shows which way the handle moves.
4. In the **Land** panel, change **Fill** to set how much of the box to fill. Change **Roughness** to set how ragged the coast is. Set a **Coast seed** or click **Re-roll** to change the coast of that box. Use **Delete box** to remove the box. An island with less than about 360 × 360 m of land can disappear. Make its box larger or increase **Fill**.
5. Click **Build World**. The editor makes the ground, beaches, sea floor, ocean and biomes, then opens the normal tools. The map uses the same blue colours for water as the Land mode preview. Save the world as a `.7dmap` project to keep the boxes.

Select **Grid Layer** in the Land mode toolbar to show a grid with 150 m squares. See the bottom of the **Land** panel for help with dragging boxes.

If you close the editor or install an update, click **Cancel** in either message about unsaved changes to stay in Land mode and keep your drawing.

Use **Ctrl+Z** and **Ctrl+Y** in Land mode to undo and redo changes to the drawing. After **Build World**, press **Ctrl+Z** to put back the previous ground. The editor goes back to Land mode with your boxes, so you can change them and click **Build World** again. Press **Ctrl+Y** to build the world again. If you click **Cancel**, the map shows the boxes as dashed outlines until you build the world.

To open a saved drawing, go to **Generate > Terrain** and click **Edit land**. **Build World** makes the terrain again for the whole map. It removes everything on the map, also the towns and bridges that you drew with the **Town** and **Bridge** tools. Run the later Generate stages again when you need them. **Cancel** discards your changes to the drawing. If you have just started a new world, **Cancel** closes it and returns to the start screen.

### Open a world

- Click **Open** (Ctrl+O). Select a project (`.7dmap`), or the `dtm.raw` or `map_info.xml` of a world.
- Or click **File > Open World Folder** and select the world folder.

Double-click a `.7dmap` file in File Explorer to open it in the installed editor. The portable copy does not register this file type.

When you open a project with missing world files, the editor shows a warning.

Sometimes the editor cannot read an entry in a world file, for example a POI or a spawn point. Then it leaves out that entry and shows a warning. The rest of the world opens. If `biomes.png` or `radiation.png` does not fit the world size, the editor keeps the file as it is and shows a warning in the **World** tab. The map does not show or paint that layer, and export writes the file back with no change.

### Read the world statistics

Open the **World** tab in the side panel. The **Land** and **Water** bar shows the percentage of the full map. **Height range** shows the lowest and highest terrain heights in metres. The biome bars show the percentage of land in each biome. The bars use the same biome colours as the map.

**Road length (estimate)** gives the length of asphalt and gravel in kilometres. The editor divides the painted area by 18 m for asphalt and by 9 m for gravel. Gravel paint can include road shoulders. Changes in road width change the estimate.

The tab counts POIs by difficulty tier and by the biome at the centre of each POI. The game folder supplies the tiers. A known prefab with no tier set counts as tier 0. The list shows **Unknown** for POIs with no prefab data. The tab also counts street tiles, towns, traders and spawn points. Each group of street tiles that touch at their edges counts as one town.

The numbers update after each edit, undo, redo and Generate stage. If you make several edits quickly, the numbers update after a short pause.

### Save your work

Click **Save** (Ctrl+S). The editor saves a project file (`.7dmap`). A project keeps all of the world and all of the Generate settings.

If you opened a world folder, Ctrl+S saves the world back to that folder.

For **Save Project As**, select a folder outside the editor's recovery folder.

A project keeps the world folder you opened or last exported to. When you open the project again, Game Link uses that folder.

If you use **Save Project As** with a new name, the editor makes a new project. The editor suggests the new project name for its first export.

If you close the editor during a save or export, the editor waits for the operation to finish. The editor asks about any unsaved changes before it closes.

The editor saves a recovery copy of unsaved work every 5 minutes. When you save, close, or switch worlds, the editor deletes the old copy. After a crash, the editor offers to recover the most recent copy. Click **No** to delete the recovery copies. When the editor searches for recovery copies, it also deletes temporary autosave files that are more than one hour old.

Projects and recovery copies keep the original biome and radiation images, even if the images are larger than the world.

### Undo

Press **Ctrl+Z** to undo and **Ctrl+Y** to redo. Each stroke, each placed item and each stage run is one undo step.

The editor remembers where you saved. If you undo or redo back to that point, the world has no unsaved changes, and closing does not ask you to save. Undo after a **Re-roll** also puts back the old seed.

If an edit fails, the editor puts the world back as it was and shows a message. A failed road or town keeps its points or plan, so you can try again.

### View the history

Open **History** in the side panel. In the Modern theme, click the clock icon. The newest entry is at the top. Type words in the filter box to find an entry. Click an entry with a map area to go to that area.

History keeps edits, Generate runs, saves, exports, and other project events. In Land mode, **Build World** adds one entry with the number of boxes. Drawing boxes adds no entries.

Click **Undo to here** to undo the edits after that entry. Click **Redo to here** to redo the edits up to and including that entry. You can also right-click the entry to use the same action. The entry for the current state has no button. Each undo or redo adds an entry to the list.

You can use these actions while the edit is in the undo or redo list. When the editor drops an old undo step to save memory, its history entry stays. A `.7dmap` keeps the history. When you open it again, you can read the old entries and go to their map areas. To keep the history of a world you opened from a game folder, use **Save Project As**.

If the history is larger than 64 MB, the editor drops the oldest entries when it saves the project.

## 5. The tools

Click a tool on the ribbon, or press its key.

### Navigate

| Tool | Key | What it does |
|---|---|---|
| **Select** | V | Selects a POI, trader, street tile or spawn point. See [Select and move items](#select-and-move-items). |
| **Multi-select** | M | Selects and edits a group of items on the map. See [Multi-select](#multi-select). |
| **Ruler** | K | Measures distance, height change, and slope. See [Ruler](#ruler). |

### Terrain

These tools use a brush. Set the brush size and strength on the left rail. Press **[** or **]** to make the brush smaller or larger.

| Tool | Key | What it does |
|---|---|---|
| **Raise** | R | Makes the ground higher. |
| **Lower** | L | Makes the ground lower. |
| **Flatten** | F | Makes the ground the same height. Ctrl+click to take the height from the map. |
| **Smooth** | S | Makes the ground smoother. |
| **Roughen** | N | Makes the ground rougher. |
| **Stamp** | T | Puts one of the game's terrain stamps on the ground. Press Q or E to turn the stamp. |

With a square brush, press Q or E to turn the brush.

Lake, river and water stamps always fill with water. For the other stamps, tick **Fill water** to fill them. After each stamp, the status bar tells you how much the ground went down or up, and how much new water it made.

### Paint

| Tool | Key | What it does |
|---|---|---|
| **Paint biome** | B | Paints the biome that you select in the Palette. |
| **Paint radiation** | D | Paints radiation. Shift+drag removes radiation. |
| **Water fill** | W | Fills an area with water. Ctrl+click to take the water level from the map. |

### Build

| Tool | Key | What it does |
|---|---|---|
| **Road** | O | Draws a road. See [Roads](#roads). |
| **Bridge** | U | Builds a bridge with ramps. See [Bridges](#bridges). |
| **Prefabs** | I | Puts a POI, street tile or part on the map. Select it in the **Prefabs** tab. A click on a prefab in that tab also starts this tool. |
| **Trader** | A | Puts a trader on the map. Select a trader in the tool options, or click **Random**. |
| **Town** | G | Makes a whole town in a rectangle that you drag. See [Towns](#towns). |
| **Spawn points** | H | Puts player spawn points on the map. |

### Select and move items

With the **Select** tool:

- Click an item to select it. A click does not move the item. For a POI, the info card shows its picture, tier and details.
- Drag the item to move it.
- Press **Q** or **E** to turn it.
- Press **PgUp** or **PgDn** to make it one block higher or lower.
- Press **Del** to remove it.
- Press **Ctrl+C** to copy a selected prefab or spawn point.
- Click a bridge deck or ramp to select the bridge and switch to the **Bridge** tool. See [Bridges](#bridges).
- Drag empty ground to move the map.

When you select a spawn point with **Select** or **Spawn points**, the status bar shows its position and the direction it faces.

While you drag a prefab or spawn point, press **Esc** or switch tools to finish the move. You can undo the move with **Ctrl+Z**. If another window takes control of the mouse during the drag, the item goes back to its starting position.

To replace a selected prefab, select a different prefab in the **Prefabs** tab. The info card shows it as the replacement. Click **Replace**, or click **Back** (Esc) to cancel.

To paste, press **Ctrl+V** and move the pointer over the map. Press **Q** or **E** to turn the preview. Click to put the copy on the map, or press **Esc** to cancel.

You can paste into a different project or editor window. The editor skips prefabs missing from the prefab library. The status bar shows how many items it put on the map and how many prefabs it skipped.

Click **☆** next to a prefab to make it a favourite. **Favourites** in the category list shows your favourites.

#### Multi-select

Click **Multi-select** next to **Select**, or press **M**.

- Drag a box around items on the map. The box must contain the whole item.
- Hold **Shift** and drag to add items to the selection. Hold **Shift** and click an item to add or remove it. Click empty ground to clear the selection.
- Drag a selected item to move the group. Street tiles stay level, and the POIs on them move with them. Other POIs and spawn points sit on the ground at the new location.
- Press **Q** or **E** to turn the group around its centre. Press the other key to put the items back where they were. Press **PgUp** or **PgDn** to move the selected prefabs one block higher or lower. The status bar shows the result. Press **Del** to remove the group.
- The editor stops a move or turn if any part of a prefab would go outside the map. The editor also stops a turn if a prefab would overlap a POI outside the selection.
- A drop replaces covered prefabs. The editor stops the drop if a selected street tile partly overlaps another street tile, or if the group overlaps a bridge. Bridges made with the Bridge tool or Highways stay in place.
- Each group edit takes one undo step. The map shows the selection.
- Press **Ctrl+C** to copy the group, then press **Ctrl+V** to paste it. The copy keeps the spacing, directions and height offsets of the items. The editor skips pieces of bridges made with the **Bridge** tool or the **Highways** stage.
- Turn and put the group on the map as in **Select**. Hold **Alt** to put it on the map without snapping to street tiles. The same map edge and overlap rules apply as for a group move. A paste takes one undo step and selects the new items.

#### Ruler

Click **Ruler** in the toolbar, or press **K**.

1. Click the map to add each point. Move the pointer to see the next part of the path.
2. Double-click the last point or press **Enter** to finish. The path stays on the map until you click again or press **Esc**.

To measure one straight line, press and hold the left mouse button. Drag to the end point, then release the button.
Press **Backspace** to remove the last point. Press **Esc** to clear the ruler. Press **Esc** again to go back to **Select**.

Each line shows its horizontal length, the height change, and the slope, in that order.
The ruler takes the height at each point from the nearest terrain cell. The slope is the height change divided by the horizontal length, as a percentage.
**Total** shows the sum of the horizontal lengths. The ruler does not change the map.

### Roads

1. Click on the map to add each point of the road.
2. Press **Enter** to finish the road. Press **Backspace** to remove the last point. Press **Esc** to cancel the road. Press **Esc** again to go back to **Select**.

The **Preset** list sets the size of common roads. **Highway** (18 m) and **Country road** (9 m gravel) are the same as the game's roads.

The road snaps as you draw:

- A segment within 5° of north, south, east or west becomes straight.
- Click near the end of a road to continue it. The new road gets the same surface and width.
- Click near the end of a bridge to start or finish a road on it. The road meets the deck at the deck's height.
- Hold **Alt** to draw without snapping.

To remove a road, hold **Shift** and drag over it.

When you grade a road, the ground beside it slopes gently. The slope is wider where the ground is steep.

Roads make their flat parts level to whole metres. Prefabs also sit at whole metres, so a POI next to a road meets it with no step.

### Bridges

1. Choose the **Bridge** tool. Click where the bridge starts, then click where it ends.
2. The bridge locks to north-south or east-west, whichever is nearer, and to whole 9 m pieces (at least 2). The preview shows the pieces, the length and the deck height. It also shows the ramp road at each end, with its length and grade (the average slope, in %). An orange ramp is steeper than 6%, or does not reach the ground. If a ramp stops at a town or a POI, the preview gives its name.
3. Over water the beams are 5 m above the water, as the Highways stage builds them. Over land the deck is level with the higher end's ground. To use another height, change **Deck height** before you build.
4. Press **Enter** to build it. Press **Esc** to cancel it. Press **Esc** again to go back to **Select**. The bridge, its footings and a ramp road from each end down to the ground are one undo step.

Click the deck or a ramp to select the bridge. The editor shows an outline around the deck and ramps. Drag the middle of the deck to move the bridge along its line. Drag a ramp or the end of the deck to make the bridge longer or shorter. Change **Deck height** to rebuild the bridge at a different height. Press **Delete** to take down the bridge, its footings and its ramps. The editor puts back the ground under the bridge and ramps. If you changed the ground after you built the bridge, for example when you drew a road, those changes stay. For bridges from older versions, this applies after the editor rebuilds them.

A ramp that comes to a town or a POI stops at its edge, level with its ground. **Car wrecks** puts a wreck on the deck about every 100 to 150 m. **Rebuild all** builds all your bridges again with the current rules, as one undo step.

The tool lists only its own bridges in the project. The Highways stage keeps its own bridges and does not touch these. The stages keep their roads and POIs off your bridges and their ramps. Stages 2 to 5 build your bridges again when they run. New terrain (stage 1 or **Build World**) removes them, because their shores changed. Draw them again on the new ground.

In the game, the ground beside a bridge has a small groove where it meets the deck, and a small pit around each pillar. The game makes these gaps, for every bridge: changing the heights does not remove them. They are only a visual problem: vehicles and players cross the ends without catching.

On a hill, a flat building cannot meet a sloped road level. The editor shows a warning. To prevent this, flatten the ground first:

1. Select the **Flatten** tool and set **Whole metres** on.
2. With a large brush, drag along the road and the ground where the building goes.
3. Put the building on the map.

### Prefabs, street tiles and traders

- To find a prefab in the **Prefabs** tab, press **Ctrl+F** and type a name, tag or zone.
- A **street tile** snaps to the 150 m grid of a street tile nearby, so the streets align. Hold **Alt** to put a tile anywhere.
- Street tiles cannot overlap. Drop a tile on the middle of another tile to replace it.
- A **POI** or trader snaps to the edges of street tiles near it. Hold **Alt** to put it anywhere, also in a street-tile lot.
- A POI in a street-tile lot faces the street. Press **Q** or **E** to turn it to another side, while you put it on the map or after you select it. It stays in the lot. If it does not fit the lot when it turns, the status bar tells you. When you drag a turned POI to a different lot, it keeps its turn. You can also press **Q** or **E** while you drag a POI. If it does not fit that lot with the turn, it faces the street.
- To replace a POI, drag a new POI over it until the old POI becomes red. Then drop the new POI.
- A prefab that you put next to a road gets the height of the road. The road does not change.

### Towns

The **Town** tool makes a complete town: streets, districts, POIs, traders and flat ground.

1. Drag a rectangle on the map. The rectangle snaps to the world's 150 m street-tile grid. The towns from the Town sites stage use the same grid, so the towns align. To see the grid, click **Layers** and select **World grid**.
2. Select the town type in the tool options.
3. Change the town if necessary:
   - Drag an edge or a corner of the rectangle to change its size.
   - Click a green link road to remove it. Click the dashed line to add it again.
   - Change the seed to get a different plan.
4. Press **Enter** to build the town. Press **Esc** to cancel it. Press **Esc** again to go back to **Select**.

Hold **Alt** when you start to drag to start the grid at the mouse position. Use this to put a town tightly against a road.
The Town tool leaves a gap of at least 150 m from street tiles that stay in the world.

## 6. The Generate panel

The **Generate** tab in the side panel makes the world in stages, the way the game's world generator does.

### The stages

| Stage | What it makes |
|---|---|
| 1 Terrain | Plains, hills and mountains |
| 2 Town sites | Towns and cities, with traders |
| 3 Lakes, rivers, canyons, craters | Water and holes in the ground |
| 4 Highways | Asphalt roads between the towns |
| 5 Wilderness POIs and gravel roads | POIs outside the towns, and gravel roads to them |
| 6 Spawn points | Player spawn points |

Click the name of a stage to open its card. One card is open at a time. A **Done** label shows on each stage that you completed. Click **Help** in a stage to read more about its settings.

Each card shows the settings that you change most, and a row of buttons. The other settings and the stage's **Seed** are under **More settings**. The editor starts with **More settings** closed. Click it to open it.

### Use stages and tools together

The stages and the tools change the same world. You can use them in any mix:

- Run a stage, then change its result with a tool.
- Make something with a tool, and a later stage uses it.
- Run a stage again later. The other stages and your tool work stay. Stage 1 is different: new terrain removes everything on it (see Stage 1).

These are some examples:

- Generate the terrain. Then use **Raise** and **Smooth** to change one area.
- Draw a city with the **Town** tool. The **Highways** stage connects it to the other towns.
- Put a trader with the **Trader** tool. The **Spawn points** stage uses it.
- Draw a road with the **Road** tool. The **Wilderness** stage can connect gravel roads to it.
- Open a world that the game generated. Run only the **Spawn points** stage to make new spawns.

### Skip a stage

You do not have to run all six stages. You can skip a stage if you do not want what it makes, or if you make it with the tools.

Some stages use the result of an earlier stage, or of a tool. This table shows what each stage needs.

| Stage | It needs | You can skip it if |
|---|---|---|
| 1 Terrain | A biome map | You want to keep the current ground, or shape it with the tools. |
| 2 Town sites | Nothing | You want no towns, or you draw them with the Town tool. |
| 3 Lakes, rivers, canyons, craters | Nothing | You want no water features, or you add water with the Water fill tool. |
| 4 Highways | Towns in the world, from stage 2, the Town tool or an opened world | You want no highways, or you draw roads with the Road tool. |
| 5 Wilderness POIs and gravel roads | Nothing. Its gravel roads connect to any road. | You want no wilderness POIs, or you put them with the Prefabs tool. |
| 6 Spawn points | Traders in the world, from stage 2, the Trader tool or an opened world | You put spawn points with the Spawn points tool. |

The editor gives a message if a stage cannot run. For example, the Highways stage shows "There are no towns to join: build towns first."

**Do the stages in sequence, 1 to 6.** A later stage uses the result of the earlier stages. For example, the Highways stage connects the towns that are in the world when it runs.

### Seeds

The seed controls the random choices. The same seeds always make the same world.

- **World seed** is at the top of the panel. Click **New** to get a new world seed. The seed of each stage comes from the world seed.
- Each stage also has its own **Seed**, under **More settings**, and a **Re-roll** button next to **Generate**. **Re-roll** runs that stage again with a new seed. It does not change the other stages.

### Presets

A preset is a saved set of Generate settings.

- To use a preset, select it in the **Preset** list and click **Use**. The seeds, the map edges and the tiles that you painted do not change.
- To save your settings, type a name and click **Save**.
- To remove a preset that you saved, select it and click **Delete**.

The editor has eight built-in presets. Each preset changes the game's numbers for your world size.

| Preset | What it changes |
|---|---|
| Game defaults | Nothing. It uses the game's own settings. |
| Big cities | 60% of the game's town count. Each town can have up to two times the street tiles. |
| Frontier | Half the towns, 150% of the roadside stops, many harder wilderness POIs, gravel roads up to 1.5 km. |
| Border towns | The game's counts. Towns can cross biome borders, and each town gets a trader. |
| Easy start | Easier wilderness POIs. At least 20 spawns in all biomes, near roads, within 1.2 km of a trader. |
| Rugged | Plains 2, hills 4, mountains 5. Lakes on mountains. Two times the canyons. |
| Flatlands | Plains 7, hills 3, no mountains. Half the canyons. |
| Waterways | Long rivers of up to 12 stamps. Two times the lakes and rivers. Causeways up to 100 m. |
| Wasteland heavy | Harder wilderness POIs and two times the craters. Spawns in the forest, as the game does. |

### Common buttons

Most stages have these buttons:

- **Generate** removes the result of the last run of the stage and makes a new result. It is one undo step.
- **Remove all** removes everything that the stage made, pinned items too. It puts back the ground. It is one undo step.
- **Game numbers** or **Game settings** puts back the game's values for your world size. The seed does not change. In stages 5 and 6, **Game settings** is under **More settings**.
- **Pinned** keeps an item when you run the stage again. The editor pins each item that you put on the map by hand.
- **Drop** removes one item.

Each stage runs in the background. A progress bar shows. Click **Cancel** to stop the stage.

Stage 3 has the map tool **Place on the map**, and stage 6 has **Edit on the map**. The controls for one item, for example **Place**, **Drop** and **Pinned**, show only while that map tool is on.

## 7. The six stages

### Stage 1: Terrain

This stage makes plains, hills and mountains. It divides the map into tiles of 256 m. Each tile gets plains, hills or mountains. Then the editor puts the game's terrain stamps on the tiles.

The stage needs a biome map. If the world has no biome map, paint biomes first.

**Settings:**

- **Plains**, **Hills** and **Mountains** set how much of each type the map gets. The editor compares the three numbers. The game uses 4, 4 and 2.
- **Per biome** (under **More settings**): tick a biome to give it its own numbers. A wasteland that you do not tick gets no mountains, as in the game.
- **Land shape**: the shape of the land, as in **New World**. For a preset with a coast, **Coast seed** and **Re-roll** change the coast. The terrain seed stays the same. For **Draw your own**, click **Edit land** to open the drawing in Land mode.
- **Edges** (only for **Land to the edges**): select an edge type for the north, east, south and west edges. The types are **Land** (rough raised land), **Water** (ocean) and **None** (no change). The editor makes the edges again only when you generate the whole map.
- **Plan the biomes again for this land** (under **More settings**): the editor makes the biome layout from **New World** again, for the land shape. The editor ticks this setting when you change the shape. It replaces the biomes that you painted. Undo puts them back. This setting shows only for a world with a generated biome layout from **New World**. When you tick it, a **Biome seed** row shows. Its **Re-roll** makes a new biome layout on the ground that you have, and the terrain does not change. A **Layout** row also shows, where you can select a different layout, for example **One biome per island** for **Several islands**.
- **Where**:
  - **Whole map** makes all of the terrain again.
  - **Only an area** makes only the tiles in a rectangle. Click **Select area**, then drag on the map. The new tiles blend into the land around them.

On an island or a peninsula, sea tiles get no plains, hills or mountains.

Click **Generate** to make the terrain.

New terrain removes everything on the new ground: towns, POIs, bridges, roads and spawn points. This includes the items that you put with the tools, for example a city from the **Town** tool or a bridge from the **Bridge** tool. These items cannot stay, because the ground under them changed. For **Only an area**, the editor removes the items in the area and near it. The editor asks before it starts. Undo puts everything back.

New terrain for the whole map removes the planned towns of stage 2 that are not pinned, because their ground changed. A pinned town stays if its ground can still hold it. Undo puts the towns back.

### Stage 2: Town sites

This stage plans towns and cities, and then builds them. It uses the game's town types and the game's counts for your world size.
Towns have a gap of at least one 150 m cell between them.

**Town presets**

- The editor adds 12 starter presets. For 6K, 8K and 10K maps, there are three presets: **Many towns** (the counts the game uses when you set Towns to Many), **Countryside** (more villages, fewer cities) and **Cities** (more and bigger cities). There are also **12K Large**, **16K Huge** and **Sparse (any size)**, which has few towns. You can replace or delete these presets.
- In the **Town sites** card, enter a name under **Preset** and click **Save**. Use the same name to replace a preset. Uppercase and lowercase letters match.
- Select a preset and click **Use**. The editor restores the town table, trader setting, biome border setting, edge margin, spacing, and biome list. The town seed and the other stages stay the same.
- Save a preset for each map size. The editor uses the saved values at any map size.
- If your game has different town types, the editor skips types it does not know. For types missing from the preset, it keeps the current values or uses the game's values.
- Select a preset and click **Delete** to remove it. The editor keeps town presets in its settings so you can use them in other worlds.

**Settings:**

- **The town table** has one row for each town type, for example **Big city** or **Roadside stop**.
  - **How many**: the number of towns of this type.
  - Point at a town name to read its description. In a narrow panel, the size estimate is under the name. Drag the edge to widen the panel and see the estimate in the **About** column.
  - **Smallest** and **Biggest**: the size of one town, in street tiles. One street tile is 150 m.
  - **About**: the approximate width of a town of that size.
- **One trader per town**:
  - Off: the game's rules. Each biome gets its own trader, 2 to 4 times, in the biggest towns of that biome.
  - On: each town gets one trader.
- **Let towns cross biome borders**:
  - Off: the game's rules. All street tiles of a town are in one biome.
  - On: a town can be on two biomes. Each lot gets POIs and traders for the biome under it.
- **Spacing and biomes** (under **More settings**):
  - **Edge margin**: the distance of the towns from the map edge, in % of the map. The game uses 10%.
  - **Apart**: the minimum number of street tiles between two towns.
  - **Biomes**: the biomes where a town can start. Leave it empty to use the game's list.

**Plan, then build:**

1. Click **Plan towns**. The editor selects the town sites and shows each planned town on the map. The planned towns stay on the map on every tab, until you build or clear them. The world does not change yet. This is one undo step. Undo puts back the previous town sites. Redo puts back the new town sites.
2. Look at the plan on the map. If you do not like it, click **Re-roll** to plan again with a new seed.
3. Click **Build**. The editor builds all the planned towns: streets, districts, POIs, traders and flat ground. This is one undo step.

If you save a project before you build the towns, the same planned sites show when you open it again. You can then click **Build** or **Clear**.

To put a town in a specific position, use the **Town** tool on the ribbon (G).

After **Build**, you can still click **Plan towns** or **Re-roll**. The editor asks first. Then it removes the built towns that are not pinned, with the highways, wilderness POIs and spawns that use them, and plans new towns. Pinned towns stay. Undo puts everything back.

**Clear** removes all planned towns, pinned towns too.

All towns use the world's 150 m street-tile grid. The towns align with each other, and street tiles fit between them. To see the grid on the map, click **Layers** and select **World grid**. The grid shows when you zoom in.

The stage does not make roads between the towns. Stage 4 makes them.

### Stage 3: Lakes, rivers, canyons, craters

This stage presses lakes, rivers, canyons and craters into the ground. It uses the game's counts for your world size.

**Settings:**

- **The feature table** has one row for each feature.
  - **Count**: how many of the feature.
  - **Size min** and **Size max**: the scale of the stamps. A scale of 1 is a stamp of about 700 m.
  - If a feature does not fit, the editor leaves it out.
- **Mountain lakes** (under **More settings**): lakes fill at their own ground level, not at the game's fixed 30 m. Lakes can then hold water on high ground and in the mountains.
- **Long rivers** (under **More settings**): each river goes downhill, stamp after stamp, until it gets to water or the map edge. A river that stops on dry land ends in a lake.
- **River stamps** (under **More settings**): the maximum number of stamps in one long river.

**Place on the map**: this starts the map tool. The **Place** list shows. Select a feature in **Place**, then click empty ground. The editor pins each feature that you place. Click a feature to select it. Then click **Drop** to remove it, or tick **Pinned**. **Drop** removes all of a long river.

A crater or canyon that touches water fills with water, as in the game.

No feature starts in the sea of an island or a peninsula.

### Stage 4: Highways

This stage connects the towns with asphalt highways. It uses the towns that are in the world when it runs. These can come from stage 2, from the Town tool, or from an opened world.

The game's rules connect each town exit to the nearest of its three nearest towns. Big cities get ring roads. Towns that have no exits get gravel roads.

A highway joins an existing road when both roads go the same way. The highway uses that part of the road.

Where a new road meets, crosses or runs on an existing road, the existing road keeps its height. The new road slopes up or down to that height, also on a causeway.

Roads leave room for their shoulders and for the ground to slope back to the nearby terrain. They meet street tiles at the street exits.

**Settings:**

- **Route wiggle** (under **More settings**): how much the seed bends the routes, in %. The default is 15%. Set 0 to turn off these bends.
- **Longest causeway** (under **More settings**): the longest crossing over water, in metres. The default is 120 m. A highway goes around wider water.
- **Join every town**: after the game's rules, the editor connects a group of towns that has no road to the other towns.
- **Bridge islands** (with **Join every town**): the editor connects towns on different islands with bridges. It uses the game's bridge sections, `rwg_bridge_tile_01` and `rwg_bridge_end_01`. Each bridge goes straight north–south or east–west. A highway goes from each town to the bridge.
- **Longest bridge** (under **More settings**): the maximum length of water that a bridge crosses, in metres.
- **Shortcut bridges** (shows under **Bridge islands** when you tick it): the editor first connects all the islands. Then it adds a direct bridge between two islands if the route between them crosses three or more bridges.
- **Car wrecks on bridges** (shows under **Bridge islands** when you tick it): puts a car wreck on the deck about every 100 to 150 m. On by default.
- **Allow a second road** (under **More settings**): two highways can connect the same pair of towns. Off: one highway connects two towns.

The bottom of each bridge is 5 m above the water, so boats can go under it. The editor puts a footing under each row of pillars. The footing stays under the water.

If two islands have no straight crossing within the longest bridge, the editor does not connect them. The status line tells you. No other road connects to a highway within 80 m of a bridge.

**Re-roll** gives new routes. **Remove all** removes the roads that this stage made and puts back the ground under them. **Remove all** also removes the bridges and their footings.

### Stage 5: Wilderness POIs and gravel roads

This stage puts POIs outside the towns. It also connects them to the roads with gravel roads.

In each biome, the editor first uses free 150 m cells far from towns. Each cell gets the wilderness POI that fits best. The ground must be dry, flat enough, and in one biome.

**Settings:**

- **Game count**: select the game's **Few**, **Default** or **Many** count. Then set **×%** to multiply that count. 100% is the game's count.
- **Tiers**: move the slider left for easier POIs or right for harder POIs. The middle is the game's mix.
- **Gap** (under **More settings**): the minimum distance from towns and roads, in metres. A POI on ground that is higher or lower than a town stays farther away: 4 m for each metre of difference, plus 6 m. This gives a road room to go up or down between them.
- **Gravel roads**: a gravel road goes from the road exit of each POI to the nearest road. Any road counts, also a road that you drew.
- **Longest road** (under **More settings**): a POI farther from a road than this gets no gravel road.

To put a POI in a specific position, use the **Prefabs (POIs)** tool on the ribbon (I).

### Stage 6: Spawn points

This stage puts player spawn points on the map, the way the game does:

1. Along the gravel roads from stage 5, next to POIs of tier 0 and 1, near a trader of the same biome. The spawns are at least 60 m apart.
2. Then on the free cells nearest to a trader, until there are enough spawns.

The stage uses all traders in the world. These can come from stage 2, from the Trader tool, or from an opened world. If the world has no traders, no spawns go along the gravel roads.

**Settings:**

- **Biomes**: tick the biomes for spawns. The game uses only the forest. Tick none to allow all biomes and all traders.
- **At least**: the minimum number of spawns. Spawns that are already in the world count. The game uses 12.
- **At most** (under **More settings**): the maximum number of spawns. Set 0 for no limit, as in the game.
- **Trader distance** (under **More settings**): the maximum distance from a trader to a spawn along a gravel road, in metres. The game uses 900.
- **Near the opening quest's trader** (under **More settings**): the game's first quest sends each new player to one trader. With this setting, all spawns are within 750 m of that trader. On for new projects.

**Edit on the map**: add, move, turn and remove spawns by hand. A spawn that you add or move stays when you run the stage again.

## 8. Example workflows

### Guided tours

In **Help > Guided tours**, select **Draw your own islands** or **Stages and tools together**.
Do the action in each tip or click **Next**. Click **Back** to see an earlier step. Click **Close** to end the tour.

The editor can offer the islands tour after you make a new world. Click **Start tour** to start it.
To stop the offer without starting the tour, select **Don't show again**, then click **Close**.

### A complete generated world

1. Click **New World**. Select the size and a biome layout.
2. In the Generate panel, select a preset, or keep **Game defaults**.
3. Run stage 1. Change the terrain with the terrain tools if necessary.
4. Run stage 2: click **Plan towns**, review the towns, then click **Build**.
5. Run stages 3, 4, 5 and 6.
6. Press **F7** to validate. Then press **Ctrl+E** to export.

### A world with your own city

1. Run stage 1 to make the terrain.
2. Draw your city with the **Town** tool and press **Enter**.
3. Run stage 2. Set **How many** for the city type to 0 if you want no other cities.
4. Run stage 4. The highways connect your city to the other towns.
5. Run stages 5 and 6.

### A world with land that you draw

1. Click **New World** (Ctrl+N). Under **Land shape**, select **Draw your own**. Select the biomes, then click **Create**. Land mode opens.
2. Click **Rectangle** and drag boxes on the map to make land. Hold **Alt** while you drag to make water, for example a bay.
3. Click **Select boxes** to move or resize a box. In the **Land** panel, set **Fill** and **Roughness** for each box.
4. Click **Build World**. The editor makes the terrain (stage 1) and the biomes for your land.
5. Run stages 2, 3, 4, 5 and 6.
6. Press **F7** to validate. Then press **Ctrl+E** to export.

To change the land later, go to **Generate > Terrain** and click **Edit land**. **Build World** clears the later stages and removes everything on the map. Run the stages again, and draw your towns and bridges again.

### New spawns for a world that you have

1. Open the world folder.
2. Open stage 6 and set the settings.
3. Click **Generate**.
4. Save the world.

## 9. Validate and export the world

### Validate

Press **F7**, or click **Validate**. The **Issues** tab lists the problems that the editor finds. For example, it lists a POI that does not meet its road level. Double-click an issue to go to it on the map.

Validate also checks:

- **Bridges**: steps at the deck ends, roads to each end, steep approaches, ground under the deck, footings and the room under the beams.
- **The opening quest's trader**: the game's first quest sends each new player to one forest trader. Validate warns if there is no such trader, or if a spawn is 750 m or more from it.

Some issues have a **Fix** button. Click it to fix the issue. Then Validate runs again. Press **Ctrl+Z** to undo a fix.

- **A POI does not meet the road level**: **Level road** sets the road and the ground near the POI to the height of the POI.
  The road then changes height gradually over 20 m. Validate skips this warning for bridges.
- **A spawn point is under water or outside the world**: **Move spawn** moves it to the nearest dry ground. That ground is in the world and not in a prefab.

Other issues need you to decide what to do, so they have no **Fix** button.

### Check the issues in the game

With Gnostic Game Link installed, click **Teleport** on an issue in the **Issues** tab. The editor selects that row and takes you to it in the game. You cannot click the buttons while Game Link has no connection to the game. Issues with no place on the map have no **Teleport** button.

Select **Enable in-game shortcuts**. These shortcuts work only while the game or the editor is in front. Press **Ctrl+Alt+Right** to go to the issue after the selected row. Press **Ctrl+Alt+Left** to go to the issue before it. The shortcuts skip rows with no place on the map and rows at the same stop as the selected issue. Click a different row to start from there. With no row selected, **Ctrl+Alt+Right** takes you to the first issue with a place on the map. The **Issues** tab and **Help > Keyboard Shortcuts** show both shortcuts. Clear the check box when you finish. The editor also turns the shortcuts off when you close the world or open another one.

You can make a list of teleport commands, one for each issue that Validate finds. Then you can go to each issue in the game.

1. Click **Teleport list** in the **Issues** tab. You can click it after Validate finds issues, or after you fix an issue with a **Fix** button.
2. The editor validates the world again. Then select where to save the list (a Markdown file).
3. In the game, press **F1** to open the console. Paste one command from the list and press Enter.

Each command puts you on the ground 15 m from the issue, and you look at it. The editor puts you on the first side that is on the map, out of the water and not in a prefab. Issues that are less than 5 m apart share one command. The list also shows the issues that have no place to go to, for example a missing trader or no spawn points. These issues have no command.

The list also shows the issues that you fixed with a **Fix** button while the world is open. These rows start with **Fixed** and tell what the editor did. For a spawn point that the editor moved, the command goes to the new position of the spawn point. Use these commands to make sure that each fix is good in the game. If you undo a fix, the list shows the issue again and not the fix. When you close the world or open a different world, the editor removes the fixes from the list.

### Export

1. Press **Ctrl+E**, or click **Export**.
2. Select a folder. To play the world, select the game's `GeneratedWorlds` folder.
3. The editor validates the world, then writes the world folder. Double-click an issue in the list to close the dialog and go to the issue on the map.

If the world has no spawn points, the editor adds one on the dry ground nearest the centre. If there is no dry ground, Validate shows an error and the editor stops the export. Make dry ground before you export again.

The world then shows under **New Game** in the game.

If you export to the same folder again, the world keeps its ID. Uppercase and lowercase letters in the folder name match.

Under **After export**, select **Do nothing**, **Open the playtest save in the game**, or **Rebuild the area around the player**. The editor keeps your selection for the next export.

#### Open the world in the game

Select **Open the playtest save in the game** in **Export World**. Set the game folder in **Settings > Game Folder** to use this option. In **Export World**, click **Use the game's GeneratedWorlds folder**, or select another world folder that the game uses. The editor disables the option for other folders.

The first export makes a save named `<world> Playtest`. Later exports open the same save. The game keeps chunks that it already made in this save.

The game limits the save name to 50 characters. It also limits which characters you can use. If the editor disables the option for the save name, change the world name.

- If the game does not run, the editor starts it and opens the playtest save.
- If the game is at the main menu with Gnostic Game Link, the editor writes the world and opens the playtest save.
- If you have a save open with Game Link, click **OK** to save it and go to the main menu. The editor then writes the world and opens the playtest save. Click **Cancel** to stop the export. If other players share the game, the editor stops the export.
- If the game is open without Game Link, the editor writes the world. Open it from the game's menu.
- If the game has an older Game Link, the plug turns red. The editor stops the export and does not write the world.

While the editor waits for the game to save and go to the main menu, press **Shift-Esc** to stop waiting. The editor then does not write the world. The editor also stops by itself if the game closes.

With Game Link, the game moves the player to the centre of the map view at the time you click **Export** in **Export World**. The status bar shows the coordinates. When the status bar shows the shortcut, press **Shift-Esc** to cancel the automatic teleport. The game continues to load. If the editor starts the game without Game Link, it shows the coordinates in the status bar.

If the game cannot open an old playtest save, the editor shows a message. To make a new playtest save, delete the old playtest save on the game's **Continue** page, then export again.

#### Rebuild the area around the player

Start the game with Gnostic Game Link. Open a save that uses the world you export to. Play alone in this save. Move the player to the area to rebuild. Select **Rebuild the area around the player** in **Export World**. The editor shows the save name. The option is unavailable if the map has no changes since the last export to that folder. If you cannot select this option, read the note below it.

Set **Radius** from 16 to 512 m. The default is 128 m. The editor keeps the radius for the next export. The game rebuilds a square around the player, not a circle. The radius is the distance from the player to each side of the square. A 128 m radius gives a square of 256 m by 256 m. The game uses whole 16 m chunks. It includes whole POIs that cross the edge, so the area can extend farther than the radius.

Read the warning before you click **OK**. The warning shows the number of chunks and how far the area extends from the player along the map axes. It tells you if POIs make the area much larger than the square. You lose player buildings, loot, bedrolls and land claims in the chunks that the game rebuilds. This includes the full area of POIs near the edge. If you exported with **Do nothing** while the game had the world open, the game still has the older POIs. The warning counts those POIs too, so it can show a few more chunks than the game rebuilds. Click **Cancel** to stop the export and keep the save as it is.

The game saves, goes to the main menu, and opens the same save after the export. The player goes back to the same map position, at the new ground height. Press **Shift-Esc** in the editor to stop waiting. The game can still finish the rebuild.

Move vehicles and collect dropped backpacks before you rebuild. The game can remove them in the rebuilt area. Trees can keep their old positions because the save stores them separately. A POI that you move or remove can leave old enemy spawn points or triggers.

### Export a picture of the map

Click **File > Export Map Image**. The editor saves the whole map as a PNG, north at the top, with no windows or tools on it. Select the size (1024, 2048, 4096, or 1 pixel for each metre) and the layers, POIs and world grid to show.

If the export fails or you cancel it, the editor keeps the existing PNG file.

### Package maps as a mod

Use **Package** to share your maps with other players. The editor puts one or more exported maps into a mod zip. The zip also holds the custom prefabs that the maps use. A player unzips it into the game's `Mods` folder.

1. Set the game folder first (**Settings > Game Folder**). The editor uses it to find the game's own prefabs.
2. Click **Package**, or click **World > Package Maps**. You do not have to open a world.
3. Select the maps from the list. The list shows the maps in the game's `GeneratedWorlds` folder.
4. Type a **Display name**. The editor makes the **Name** (the mod folder name) from it. You can change the name.
5. Optional: type a description, an author, a website and a version. To include a `Config` folder for the mod, click **Browse**.
6. Read the **Prefabs** list. It shows the custom prefabs that the zip will hold. The game's own prefabs are never in the zip. If the editor cannot find a prefab that a map uses, the list shows it.
7. Click **Save as** and select where to save the zip.

The editor remembers the author and the website.

Custom prefabs from other authors are their work. Share them only if their authors let you.

The game keeps the chunks that it already generated in a saved game. To see all of your changes, start a new game.

## 10. Keyboard shortcuts

Press **F1** in the editor to see this list.

| Key | Action |
|---|---|
| V, M, R, L, F, S, N, T | Select, Multi-select, Raise, Lower, Flatten, Smooth, Roughen, Stamp |
| K | Ruler |
| B, D, W | Paint biome, Paint radiation, Water fill |
| O, U, I, A, G, H | Road, Bridge, Prefabs, Trader, Town, Spawn points |
| [ and ] | Smaller or larger brush |
| Q and E | Turn the square brush, stamp, prefab or spawn |
| Ctrl+click | Take the height (Flatten) or water level (Water fill) from the map |
| Shift+drag | Remove roads or radiation |
| Alt+click | Put a road point, POI, trader or street tile without snapping |
| Alt+drag | Draw a town off the shared street-tile grid |
| Enter, Backspace | Finish, or remove the last point (roads, towns) |
| Enter, Backspace (Ruler) | Finish the path, or remove its last point |
| Esc | Cancel what the tool is doing or clear the ruler. Press it again to go back to **Select**. In **Select**, clear the selection. |
| Del, PgUp, PgDn | Remove the selected prefab, or move it one block higher or lower |
| Wheel, + and -, Home | Zoom, see the whole map |
| Ctrl+M | Show or hide the **Messages** panel |
| Right drag | Move the map with any tool |
| Right-click | Open the map menu (**Copy teleport command**) |
| Ctrl+Z, Ctrl+Y | Undo, redo |
| Ctrl+C, Ctrl+V | Copy or paste items in Select and Multi-select |
| Ctrl+N, Ctrl+O, Ctrl+S, Ctrl+Shift+S, Ctrl+E | New, open, save, save project as, export |
| Ctrl+F | Search the prefabs. If the **Placed** tab is open, search the prefabs in your world |
| Ctrl+Alt+Left | Teleport to the previous issue when **Enable in-game shortcuts** is on in the **Issues** tab |
| Ctrl+Alt+Right | Teleport to the next issue when **Enable in-game shortcuts** is on in the **Issues** tab |
| F7, F1 | Validate, show shortcuts |

## 11. Words in this manual

| Word | Meaning |
|---|---|
| **Biome** | An area of one type: forest, burnt forest, desert, snow or wasteland. |
| **Deck** | The road surface of a bridge. |
| **Footing** | Ground that the editor raises under the pillars of a bridge. |
| **Land shape** | The outline of the land. Choose land to the edges, one island, several islands, a peninsula or your own drawing. |
| **POI** | A point of interest: a building or other place that a player can visit. |
| **Prefab** | An object from the game that you can put on the map: a POI, a street tile or a part. |
| **Project** | A `.7dmap` file. It keeps the world and all of your settings. |
| **Ramp** | The road from the end of a bridge deck down to the ground. |
| **Seed** | A number that controls the random choices. The same seed gives the same result. |
| **Stage** | One step in the Generate panel. |
| **Stamp** | A height map from the game that the editor puts on the ground. |
| **Street tile** | A 150 m square of town streets. A town consists of street tiles. |
| **Tier** | How difficult a POI is, from 0 (easy) to 5 (difficult). |
| **World folder** | The folder of files that the game loads. |

## 12. Report a bug

Open an [issue](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues) on GitHub. Tell us:

- The world size.
- The stage or tool that you used.
- What happened, and what you expected.

A screenshot helps. To add the messages, click **Copy** in the **Messages** panel (Ctrl+M) and paste them into the issue. If the editor crashed, attach `crash.log` from `%APPDATA%\7D2DMapEditor`.

*This is a fan-made tool. It is not made by or affiliated with The Fun Pimps.*
