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

To get a newer version, click **Help > Check for Updates**.

### Set the game folder

The editor finds the game through Steam. If it cannot find the game, click **Settings > Game Folder**. Select the folder that contains the `Data` folder.

## 3. The window

The window has five areas.

| Area | What it holds |
|---|---|
| **Ribbon** (top) | The tools in groups: Navigate, Terrain, Paint and Build. It also holds the **Palette** (biome colours and road surfaces), **Layers** (what the map shows), **Validate** and **Export**. |
| **Tool options** (below the ribbon) | The settings for the tool that you use, and a hint in one line. Click **∨** at the right of the ribbon to hide them. |
| **Left rail** | The **size** and **strength** sliders for the brush. |
| **Side panel** (right) | Five tabs: **Prefabs**, **Placed**, **Issues**, **World** and **Generate**. Drag the left edge of the panel to make it wider or narrower. |
| **Status bar** (bottom) | The cursor position, height, biome and prefab under the cursor. It also shows the brush size, the world size, messages and the zoom. |

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

### Go to a place in the game

You can copy a command that moves you to a place on the map when you play the world.

1. Right-click the place on the map. Do not move the mouse while you click.
2. Click **Copy teleport command**, then select the direction that you want to look in. The menu shows each command.
3. In the game, press **F1** to open the console. Paste the command and press Enter.

The game puts you on the ground at that place. The numbers in the command are the same as the numbers in the status bar.

## 4. Worlds and projects

### Make a new world

1. Click **New World** (Ctrl+N).
2. Type a name, and set the size, the ground height and the seed.
3. Under **Land shape**, select one of these:
   - **Land to the edges** puts land on all of the map. Then set the **Edges**.
   - **One island** makes one island with ocean on all sides.
   - **Several islands** makes a group of islands with ocean between them.
   - **Peninsula** makes land that touches one edge. Select the edge in **Joined to**.

   For an island or a peninsula, set **Land** (the share of the map that is land) and **Rough coast**. For several islands, also set **Islands**.
4. Under **Biomes**, select one of these:
   - **One biome** paints one biome on the whole map.
   - **All biomes (generated layout)** makes a layout of all the biomes, as the game does. Select where the biomes start: forest or wasteland in the middle, a ring, two rings, or a line. Then set the **Share of the map** for each biome.
     For **Several islands**, you can also select **One biome per island**. Each island then gets one biome. The forest island is the island with a size near the forest share, near the middle. The islands farther out get the harder biomes in this order: burnt forest, desert, snow, wasteland. The editor tries to give each biome its **Share of the map** of the land. The island sizes are random, so the match is not exact. If there are as many islands as biomes, or more, each biome gets at least one island. A biome with a share of 0 is not used.
5. Click **Create**. The editor opens the Generate panel at stage 1.

The same seed always gives the same biome layout. A biome in the middle stays away from the map edges, so it can get a little less than its share.

On an island world, the share of each biome is a share of the land. The coast seed comes from the world seed.

### Open a world

- Click **Open** (Ctrl+O). Select a project (`.7dmap`), or the `dtm.raw` or `map_info.xml` of a world.
- Or click **File > Open World Folder** and select the world folder.

Sometimes the editor cannot read an entry in a world file, for example a POI or a spawn point. Then it leaves out that entry and shows a warning. The rest of the world opens. If `biomes.png` or `radiation.png` does not fit the world size, the editor keeps the file as it is and shows a warning in the **World** tab. The map does not show or paint that layer, and export writes the file back with no change.

### Save your work

Click **Save** (Ctrl+S). The editor saves a project file (`.7dmap`). A project keeps all of the world and all of the Generate settings.

If you opened a world folder, Ctrl+S saves the world back to that folder.

The editor also saves a copy every 5 minutes. If the editor does not close normally, it offers to recover your work when you start it again.

Projects and recovery copies keep the original biome and radiation images, even if the images are larger than the world.

### Undo

Press **Ctrl+Z** to undo and **Ctrl+Y** to redo. Each stroke, each placed item and each stage run is one undo step.

The editor remembers where you saved. If you undo or redo back to that point, the world has no unsaved changes, and closing does not ask you to save. Undo after a **Re-roll** also puts back the old seed.

If an edit fails, the editor puts the world back as it was and shows a message. A failed road or town keeps its points or plan, so you can try again.

## 5. The tools

Click a tool on the ribbon, or press its key.

### Navigate

| Tool | Key | What it does |
|---|---|---|
| **Select** | V | Selects a POI, trader, street tile or spawn point. See [Select and move items](#select-and-move-items). |

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

- Click an item to select it. The info card shows its picture, tier and details.
- Drag the item to move it.
- Press **Q** or **E** to turn it.
- Press **PgUp** or **PgDn** to make it one block higher or lower.
- Press **Del** to remove it.
- Drag empty ground to move the map.

To replace a selected prefab, select a different prefab in the **Prefabs** tab. The info card shows it as the replacement. Click **Replace**, or click **Back** (Esc) to cancel.

Click **☆** next to a prefab to make it a favourite. **Favourites** in the category list shows your favourites.

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

Roads make their flat parts level to whole metres. Prefabs also sit at whole metres, so a POI next to a road meets it with no step.

### Bridges

1. Choose the **Bridge** tool. Click where the bridge starts, then click where it ends.
2. The bridge locks to north-south or east-west, whichever is nearer, and to whole 9 m pieces (at least 2). The preview shows the pieces, the length and the deck height. It also shows the ramp road at each end, with its length and grade (the average slope, in %). An orange ramp is steeper than 6%, or does not reach the ground. If a ramp stops at a town or a POI, the preview gives its name.
3. Over water the beams are 5 m above the water, as the Highways stage builds them. Over land the deck is level with the higher end's ground. To use another height, change **Deck height** before you build.
4. Press **Enter** to build it. Press **Esc** to cancel it. Press **Esc** again to go back to **Select**. The bridge, its footings and a ramp road from each end down to the ground are one undo step.

Click a bridge to select it. Drag it to move it along its line. Drag one of its ends to make it longer or shorter. Change **Deck height** to rebuild it at another height. Press **Delete** to take it down: the footings and ramps go too, and the ground comes back as it was.

A ramp that comes to a town or a POI stops at its edge, level with its ground. **Car wrecks** puts a wreck on the deck about every 100 to 150 m. **Rebuild all** builds all your bridges again with the current rules, as one undo step.

The tool lists only its own bridges in the project. The Highways stage keeps its own bridges and does not touch these. The stages keep their roads and POIs off your bridges and their ramps.

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

Click the name of a stage to open its card. One card is open at a time. A **Done** label shows on each stage that you completed.

### Use stages and tools together

The stages and the tools change the same world. You can use them in any mix:

- Run a stage, then change its result with a tool.
- Make something with a tool, and a later stage uses it.
- Run a stage again later. The other stages and your tool work stay.

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
- Each stage also has its own **Seed** and a **Re-roll** button. **Re-roll** runs that stage again with a new seed. It does not change the other stages.

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
- **Game numbers** or **Game settings** puts back the game's values for your world size. The seed does not change.
- **Pinned** keeps an item when you run the stage again. The editor pins each item that you put on the map by hand.
- **Drop** removes one item.

Each stage runs in the background. A progress bar shows. Click **Cancel** to stop the stage.

## 7. The six stages

### Stage 1: Terrain

This stage makes plains, hills and mountains. It divides the map into tiles of 256 m. Each tile gets plains, hills or mountains. Then the editor puts the game's terrain stamps on the tiles.

The stage needs a biome map. If the world has no biome map, paint biomes first.

**Settings:**

- **Plains**, **Hills** and **Mountains** set how much of each type the map gets. The editor compares the three numbers. The game uses 4, 4 and 2.
- **Per biome**: tick a biome to give it its own numbers. A wasteland that you do not tick gets no mountains, as in the game.
- **Land shape**: the shape of the land, as in **New World**. **Coast seed** and its **Re-roll** button change only the coast. The terrain seed does not change. For an island or a peninsula, the editor does not make edges.
- **Edges** (only for **Land to the edges**): select an edge type for the north, east, south and west edges. The types are **Land** (rough raised land), **Water** (ocean) and **None** (no change). The editor makes the edges again only when you generate the whole map.
- **Plan the biomes again for this land**: the editor makes the biome layout from **New World** again, for the land shape. The editor ticks this setting when you change the shape. It replaces the biomes that you painted. Undo puts them back. This setting shows only for a world with a generated biome layout from **New World**. When you tick it, a **Biome seed** row shows. Its **Re-roll** makes a new biome layout on the ground that you have, and the terrain does not change. A **Layout** row also shows, where you can select a different layout, for example **One biome per island** for **Several islands**.
- **Where**:
  - **Whole map** makes all of the terrain again.
  - **Only an area** makes only the tiles in a rectangle. Click **Select area**, then drag on the map. The new tiles blend into the land around them. Prefabs and roads do not move.
- **Tile types painted by hand**: select a type and click **Paint**. Then paint 256 m tiles on the map. Shift+drag removes painted tiles. **Clear** removes all painted tiles. When you generate, your painted tiles keep their type. The editor fills the other tiles.
- **Show the last run's tile types** colours each tile by the type that the last run gave it. This setting is off by default.

On an island or a peninsula, sea tiles get no plains, hills or mountains.

Click **Generate** to make the terrain.

New terrain for the whole map removes the planned towns of stage 2 that are not pinned, because their ground changed. A pinned town stays if its ground can still hold it. Undo puts the towns back.

### Stage 2: Town sites

This stage plans towns and cities, and then builds them. It uses the game's town types and the game's counts for your world size.

**Settings:**

- **The town table** has one row for each town type.
  - **How many**: the number of towns of this type.
  - **Smallest** and **Biggest**: the size of one town, in street tiles. One street tile is 150 m.
  - **About**: the approximate width of a town of that size.
- **One trader per town**:
  - Off: the game's rules. Each biome gets its own trader, 2 to 4 times, in the biggest towns of that biome.
  - On: each town gets one trader.
- **Let towns cross biome borders**:
  - Off: the game's rules. All street tiles of a town are in one biome.
  - On: a town can be on two biomes. Each lot gets POIs and traders for the biome under it.
- **Spacing and biomes**:
  - The distance of the towns from the map edge, in % of the map. The game uses 10%.
  - **Apart**: the minimum number of street tiles between two towns.
  - **Biomes**: the biomes where a town can start. Leave it empty to use the game's list.

**Plan, review, then build:**

1. Click **Plan towns**. The editor selects the town sites and shows each planned town on the map. The world does not change yet.
2. Click **Review on the map** to change the plan:
   - Click a town to select it.
   - Drag a town to move it. The editor plans it again at the new position.
   - Drag a rectangle on empty ground to add a town. **Add as** sets the type of the new town.
   - For the selected town, change its type or its number of street tiles. Click **Re-roll** to plan it again, or **Drop** to remove it.
   - Tick **Pinned** to keep a town when you re-roll the stage. The editor pins each town that you add.
3. Click **Build**. The editor builds all the planned towns: streets, districts, POIs, traders and flat ground. This is one undo step.

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
- **Mountain lakes**: lakes fill at their own ground level, not at the game's fixed 30 m. Lakes can then hold water on high ground and in the mountains.
- **Long rivers**: each river goes downhill, stamp after stamp, until it gets to water or the map edge. A river that stops on dry land ends in a lake.
- **Most stamps in one river**: the maximum length of a long river.

**Place on the map**: select a feature in **Place**, then click empty ground. The editor pins each feature that you place. Click a feature to select it. Then click **Drop** to remove it, or tick **Pinned**. **Drop** removes all of a long river.

A crater or canyon that touches water fills with water, as in the game.

No feature starts in the sea of an island or a peninsula.

### Stage 4: Highways

This stage connects the towns with asphalt highways. It uses the towns that are in the world when it runs. These can come from stage 2, from the Town tool, or from an opened world.

The game's rules connect each town exit to the nearest of its three nearest towns. Big cities get ring roads. Towns that have no exits get gravel roads.

**Settings:**

- **Route wiggle**: how much the seed bends the routes, in %. Set 0 to get the game's exact routes.
- **Longest causeway over water**: the longest crossing over water, in metres. A highway goes around wider water.
- **Join every town**: after the game's rules, the editor connects a group of towns that has no road to the other towns.
- **Bridge islands** (with **Join every town**): the editor connects towns on different islands with bridges. It uses the game's bridge sections, `rwg_bridge_tile_01` and `rwg_bridge_end_01`. Each bridge goes straight north–south or east–west. A highway goes from each town to the bridge.
- **Longest bridge over water**: the maximum length of water that a bridge crosses, in metres.
- **Shortcut bridges**: the editor first connects all the islands. Then it adds a direct bridge between two islands if the route between them crosses three or more bridges.
- **Allow a second road between two towns**: the game can connect two towns with two highways side by side. Off: one highway connects two towns.
- **Car wrecks on bridges**: puts a car wreck on the deck about every 100 to 150 m. On by default.

The bottom of each bridge is 5 m above the water, so boats can go under it. The editor puts a footing under each row of pillars. The footing stays under the water.

If two islands have no straight crossing within the longest bridge, the editor does not connect them. The status line tells you. No other road connects to a highway within 80 m of a bridge.

**Re-roll** gives new routes. **Remove all** removes the roads that this stage made and puts back the ground under them. **Remove all** also removes the bridges and their footings.

### Stage 5: Wilderness POIs and gravel roads

This stage puts POIs outside the towns. It also connects them to the roads with gravel roads.

In each biome, the editor first uses free 150 m cells far from towns. Each cell gets the wilderness POI that fits best. The ground must be dry, flat enough, and in one biome.

**Settings:**

- **Game count**: select the game's **Few**, **Default** or **Many** count. Then set **×%** to multiply that count. 100% is the game's count.
- **Tiers**: move the slider left for easier POIs or right for harder POIs. The middle is the game's mix.
- **Gap from towns and roads**: the minimum distance from towns and roads, in metres. A POI on ground that is higher or lower than a town stays farther away: 4 m for each metre of difference, plus 6 m. This gives a road room to go up or down between them.
- **Gravel roads**: a gravel road goes from the road exit of each POI to the nearest road. Any road counts, also a road that you drew.
- **Longest road**: a POI farther from a road than this gets no gravel road.

**Place on the map**: select a POI in **Place**, then click empty ground. The editor pins each POI that you place. Click a POI that this stage placed to select it. **Drop** removes the POI and its gravel road.

### Stage 6: Spawn points

This stage puts player spawn points on the map, the way the game does:

1. Along the gravel roads from stage 5, next to POIs of tier 0 and 1, near a trader of the same biome. The spawns are at least 60 m apart.
2. Then on the free cells nearest to a trader, until there are enough spawns.

The stage uses all traders in the world. These can come from stage 2, from the Trader tool, or from an opened world. If the world has no traders, no spawns go along the gravel roads.

**Settings:**

- **Biomes**: tick the biomes for spawns. The game uses only the forest. Tick none to allow all biomes and all traders.
- **At least**: the minimum number of spawns. Spawns that are already in the world count. The game uses 12.
- **At most**: the maximum number of spawns. Set 0 for no limit, as in the game.
- **Road spawns within this of a trader**: the maximum distance from a trader, in metres. The game uses 900.
- **Near the opening quest's trader**: the game's first quest sends each new player to one trader. With this setting, all spawns are within 750 m of that trader. On for new projects.

**Edit on the map**: add, move, turn and remove spawns by hand. A spawn that you add or move stays when you run the stage again.

## 8. Example workflows

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
  The road then changes height gradually over 20 m.
- **A spawn point is under water or outside the world**: **Move spawn** moves it to the nearest dry ground. That ground is in the world and not in a prefab.

Other issues need you to decide what to do, so they have no **Fix** button.

### Check the issues in the game

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

The world then shows under **New Game** in the game.

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
| V, R, L, F, S, N, T | Select, Raise, Lower, Flatten, Smooth, Roughen, Stamp |
| B, D, W | Paint biome, Paint radiation, Water fill |
| O, U, I, A, G, H | Road, Bridge, Prefabs, Trader, Town, Spawn points |
| [ and ] | Smaller or larger brush |
| Q and E | Turn the square brush, stamp, prefab or spawn |
| Ctrl+click | Take the height (Flatten) or water level (Water fill) from the map |
| Shift+drag | Remove roads or radiation |
| Alt+click | Put a road point, POI, trader or street tile without snapping |
| Alt+drag | Draw a town off the shared street-tile grid |
| Enter, Backspace | Finish, or remove the last point (roads, towns) |
| Esc | Cancel what the tool is doing (a road, bridge or town, or a prefab to put). Press it again to go back to **Select**. In **Select**, clear the selection. |
| Del, PgUp, PgDn | Remove the selected prefab, or move it one block higher or lower |
| Wheel, + and -, Home | Zoom, see the whole map |
| Right drag | Move the map with any tool |
| Right-click | Open the map menu (**Copy teleport command**) |
| Ctrl+Z, Ctrl+Y | Undo, redo |
| Ctrl+N, Ctrl+O, Ctrl+S, Ctrl+Shift+S, Ctrl+E | New, open, save, save project as, export |
| Ctrl+F | Search the prefabs. If the **Placed** tab is open, search the prefabs in your world |
| F7, F1 | Validate, show shortcuts |

## 11. Words in this manual

| Word | Meaning |
|---|---|
| **Biome** | An area of one type: forest, burnt forest, desert, snow or wasteland. |
| **Deck** | The road surface of a bridge. |
| **Footing** | Ground that the editor raises under the pillars of a bridge. |
| **Land shape** | The outline of the land: land to the edges, one island, several islands or a peninsula. |
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

A screenshot helps. If the editor crashed, attach `crash.log` from `%APPDATA%\7D2DMapEditor`.

*This is a fan-made tool. It is not made by or affiliated with The Fun Pimps.*
