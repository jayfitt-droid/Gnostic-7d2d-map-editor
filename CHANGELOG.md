# Changelog

What changed in each release of Gnostic - 7D2D Map Editor, newest first. Downloads are on the [releases page](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/releases). Installed copies update from **Help > Check for Updates**.

## v2.3.0-rc.2 (2026-11-20)

The second release candidate for v2.3.0. It has all the changes in v2.3.0-rc.1 below, and these fixes. Only copies of a test version update to it. To try it, download it from the releases page.

### Fixes

- In the **Multi-select** tool, **PgUp** and **PgDn** did not show what they did. Now the status bar shows how many prefabs moved up or down. If you select only spawn points, a message tells you to select a prefab. ([#149](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/149))

### Known issues

- Bridges leave a gap in the ground along the deck, across each end of the deck and around each bridge post. The gaps are cosmetic: vehicles and boats can use the bridges. ([#5](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/5))
- If you open two editor windows, they share one recovery copy and one settings file. Closing one window can remove the other window's recovery copy or its new town presets. ([#137](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/137), [#138](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/138))
- Windows SmartScreen can show "Windows protected your PC" when you start the editor. The editor is not signed. Click **More info**, then **Run anyway**.

## v2.3.0-rc.1 (2026-11-20)

A release candidate for v2.3.0: a test version before the full release. Only copies of a test version update to it. To try it, download it from the releases page.

### New

- **Modern theme.** **Settings > Theme** has a new theme, **Modern**. The menus and the window title are in a slim title bar. The tools are in one row with their names. A row of icons opens the side bar, which can go on either side of the map. The Issues icon and the status bar show the number of errors and warnings. **Flat**, the layout you know, stays the default. ([#63](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/63))
- **Draw your own land.** In **New World**, select **Draw your own** to draw the land in Land mode. Drag boxes of land on the map, and hold **Alt** to cut out water. Each box has its own coast seed, fill and roughness. **Build World** makes the terrain from your drawing. The project keeps the drawing, so you can click **Edit land** and build again. ([#91](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/91), [#119](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/119))
- **Gnostic Game Link.** An optional mod for 7 Days to Die 3.4 connects the editor to the game on the same PC. Right-click the map and click **Teleport Here**, or right-click a POI and click **Add Waypoint**. In the Issues tab, click **Teleport** to go to an issue in the game. **Show player** shows where you are. Download `GnosticGameLink-1.0.0.zip` from the release page and extract it into `%AppData%\7DaysToDie\Mods`. Play with EAC off, in single-player or as the host. ([#92](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/92))
- **Multi-select tool.** The **Multi-select** tool (**M**) selects the POIs, traders, street tiles and spawn points in a box. Drag the group to move it. **Q** and **E** turn it, **PgUp** and **PgDn** move it up or down, and **Del** removes it. Each change is one undo step. ([#90](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/90))
- **History tab.** The **History** tab shows each edit, Generate stage, save and export, with its date and time. Click an entry to go to its area on the map. Click **Undo to here** or **Redo to here** to go back or forward to an edit. The project keeps the history. ([#57](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/57))
- **Town presets.** The **Town sites** stage has a **Preset** list. Save the town table and the town settings with a name, and use them again in any world. The editor adds 12 starter presets for 6K to 16K maps. ([#116](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/116))
- **World grid in map images.** **File > Export Map Image** has a **World grid** checkbox. The picture then shows the 150 m grid that towns snap to. ([#85](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/85))
- **Project files in File Explorer.** When you install with Setup, `.7dmap` files get their own icon, and a double-click opens the project in the editor. ([#93](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/93))

### Changes

- Water is a slate blue that gets darker as it gets deeper, on the map and in Land mode. ([#119](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/119))
- The **Generate** panel is easier to read. Each stage shows its main settings and its buttons. **More settings** holds the seed and the other settings. **Help** holds the longer text. New worlds use 15% route wiggle and a 120 m longest causeway for highways. ([#114](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/114))
- The editor no longer has **Review on the map** (stage 2) or **Place on the map** (stage 5). Use the **Town** tool and the **Prefabs** tools to put towns and POIs on the map. ([#114](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/114))
- Stage 1 no longer paints tile types or has **Show the last run's tile types**. Use the **Terrain** tools to shape the ground. Old projects still open. ([#115](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/115))

### Fixes

- A project did not open again if the world's original `biomes.png` or `radiation.png` was larger than the world. ([#73](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/73))
- An autosave that was still running when you saved or closed the world could bring back an old recovery copy. ([#74](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/74))
- A failed **Export Map Image** replaced the old PNG with a part of a file. Now the old PNG stays. ([#76](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/76))
- An export to the same folder as before marked the project changed. ([#82](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/82))
- If you saved a project before you built its planned towns, the town sites were gone when you opened it again. ([#127](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/127))
- An export to the same folder, with the folder name in other uppercase and lowercase letters, gave the world a new ID. ([#132](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/132))
- A world with land only near a corner exported with no spawn point. Now export finds dry ground anywhere on the map. If there is no dry ground, the export stops. ([#133](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/133))
- If you closed the editor during a save or an export, it did not close when the work finished. ([#136](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/136))

### Known issues

- Bridges leave a gap in the ground along the deck, across each end of the deck and around each bridge post. The gaps are cosmetic: vehicles and boats can use the bridges. ([#5](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/5))
- If you open two editor windows, they share one recovery copy and one settings file. Closing one window can remove the other window's recovery copy or its new town presets. ([#137](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/137), [#138](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/138))
- Windows SmartScreen can show "Windows protected your PC" when you start the editor. The editor is not signed. Click **More info**, then **Run anyway**.

## v2.2.0 (2026-11-18)

This full release has all the changes since v2.1.0, with all the changes from the three release candidates. Copies of v2.1.0 update to this version from **Help > Check for Updates**.

### New

- **Package maps.** **Package** (or **World > Package Maps**) puts one or more maps from `GeneratedWorlds` into a mod zip, with the custom prefabs that they use. The game's own prefabs are never in the zip. The files from a stopped export (`.mapeditor-staging` and `.mapeditor-backup`) are also not in the zip. Players unzip it into their `Mods` folder. ([#77](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/77))
- **Teleport commands.** Right-click the map and select **Copy teleport command** to get a command for the game's console (F1) that takes you to that place. A submenu sets the direction that you look in. ([#39](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/39))
- **Teleport list.** **Teleport list...** in the Issues tab saves a Markdown file with one teleport command for each issue. Each command puts you on the ground 15 m from the issue, out of the water and not in a prefab. You look at the issue. ([#41](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/41))
- **Fix buttons.** Some issues in the Issues tab have a **Fix** button. **Level road** sets the road beside a POI to the height of the POI. **Move spawn** moves a spawn point out of the water, or into the world, to the nearest dry ground. Each fix is one undo step, and Validate then runs again. ([#40](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/40))
- **Fixed issues in the teleport list.** The teleport list also shows the issues that you fixed with a **Fix** button while the world is open. Use it to make sure that each fix is good in the game. For a moved spawn point, the command goes to its new position. ([#59](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/59))
- **One biome per island.** For **Several islands**, New World and stage 1 (Terrain) have a new biome layout, **One biome per island**. The forest island is near the middle, and the islands farther out get the harder biomes. ([#46](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/46))
- **World grid layer.** **Layers > World grid** shows the 150 m street-tile grid that towns snap to. It shows when you zoom in. ([#47](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/47))
- **Ctrl+F** puts the cursor in the search box of the Prefabs tab, or of the Placed tab when it is open. ([#44](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/44))

### Changes

- **Esc** cancels what the tool is doing and keeps the tool. Press **Esc** again to go back to **Select**. ([#35](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/35))
- Click a prefab in the Prefabs tab to start the Prefabs tool with that prefab. ([#36](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/36))
- Press **Q** or **E** to turn a POI to any side of its lot. It stays in the lot. ([#37](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/37))
- The size filter in the Prefabs tab uses one size band for each lot size. ([#38](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/38))
- A POI that you turned in its lot keeps its turn when you drag it to a different lot. If it does not fit that lot with the turn, it faces the street. Press **Q** or **E** while you drag a POI to turn it. ([#42](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/42))
- The Bridge tool's preview shows the ramp at each end, with its length and grade. ([#43](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/43))
- In **New World**, **Land shape** and **Edges** are now above **Biomes**. The biome layouts that you can select change with the land shape. ([#67](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/67))
- The side panel tabs, the Generate stages and the ribbon tooltips are easier to read. The open tab is bold and underlined. Each stage is a card with a **Done** label when you completed it. **Per biome**, **Spacing and biomes** and **How the game does it** are links, and their text is easy to read in dark mode. The tool options strip is shorter. ([#48](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/48), [#71](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/71))

### Fixes

- An export keeps the old files of a world in a `.mapeditor-backup` folder while it replaces them. If an earlier export stopped before it finished, that folder can have the only copies of the world's files. The next export deleted it. Now the export stops and shows where the folder is. A failed export also keeps the folder if it cannot put back all the old files. ([#72](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/72))
- A project that does not have one of its layers, or has a layer of the wrong size, does not open. Before, it opened without that layer, and after the next save the layer was gone. A message tells you which layer has the problem. ([#75](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/75))
- Hold **Alt** to put a POI or trader anywhere. Before, a POI still snapped into a street-tile lot. ([#60](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/60))
- A double-click on "There are no spawn points" in the Issues tab no longer goes to the middle of the map.

### Known issues

- Bridges leave a gap in the ground along the deck, across each end of the deck and around each bridge post. The gaps are cosmetic: vehicles and boats can use the bridges. ([#5](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/5))
- Windows SmartScreen can show "Windows protected your PC" when you start the editor. The editor is not signed. Click **More info**, then **Run anyway**.

## v2.2.0-rc.3 (2026-11-17)

The third release candidate for v2.2.0. It has all the changes in v2.2.0-rc.2 and v2.2.0-rc.1 below, and these fixes. Only copies of a test version update to it. To try it, download it from the releases page.

### Fixes

- In dark mode, the text under **Spacing and biomes**, **Per biome** and **How the game does it** was black on grey. It is now easy to read. ([#71](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/71))
- An export keeps the old files of a world in a `.mapeditor-backup` folder while it replaces them. If an earlier export stopped before it finished, that folder can have the only copies of the world's files. The next export deleted it. Now the export stops and shows where the folder is. A failed export also keeps the folder if it cannot put back all the old files. ([#72](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/72))
- A project that does not have one of its layers, or has a layer of the wrong size, does not open. Before, it opened without that layer, and after the next save the layer was gone. A message tells you which layer has the problem. ([#75](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/75))
- **Package Maps** does not put the files from a stopped export (`.mapeditor-staging` and `.mapeditor-backup`) in the zip, or count them in the size. ([#77](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/77))

### Known issues

- Bridges leave a gap in the ground along the deck, across each end of the deck and around each bridge post. The gaps are cosmetic: vehicles and boats can use the bridges. ([#5](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/5))
- Windows SmartScreen can show "Windows protected your PC" when you start the editor. The editor is not signed. Click **More info**, then **Run anyway**.

## v2.2.0-rc.2 (2026-11-17)

The second release candidate for v2.2.0. It has all the changes in v2.2.0-rc.1 below, and this change. Only copies of a test version update to it. To try it, download it from the releases page.

### Changes

- In **New World**, **Land shape** and **Edges** are now above **Biomes**. The biome layouts that you can select change with the land shape. ([#67](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/67))

### Known issues

- Bridges leave a gap in the ground along the deck, across each end of the deck and around each bridge post. The gaps are cosmetic: vehicles and boats can use the bridges. ([#5](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/5))
- Windows SmartScreen can show "Windows protected your PC" when you start the editor. The editor is not signed. Click **More info**, then **Run anyway**.

## v2.2.0-rc.1 (2026-11-17)

A release candidate for v2.2.0: a test version before the full release. Only copies of a test version update to it. To try it, download it from the releases page.

### New

- **Package maps.** **Package** (or **World > Package Maps**) puts one or more maps from `GeneratedWorlds` into a mod zip, with the custom prefabs that they use. The game's own prefabs are never in the zip. Players unzip it into their `Mods` folder.
- **Teleport commands.** Right-click the map and select **Copy teleport command** to get a command for the game's console (F1) that takes you to that place. A submenu sets the direction that you look in. ([#39](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/39))
- **Teleport list.** **Teleport list...** in the Issues tab saves a Markdown file with one teleport command for each issue. Each command puts you on the ground 15 m from the issue, out of the water and not in a prefab. You look at the issue. ([#41](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/41))
- **Fix buttons.** Some issues in the Issues tab have a **Fix** button. **Level road** sets the road beside a POI to the height of the POI. **Move spawn** moves a spawn point out of the water, or into the world, to the nearest dry ground. Each fix is one undo step, and Validate then runs again. ([#40](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/40))
- **Fixed issues in the teleport list.** The teleport list also shows the issues that you fixed with a **Fix** button while the world is open. Use it to make sure that each fix is good in the game. For a moved spawn point, the command goes to its new position. ([#59](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/59))
- **One biome per island.** For **Several islands**, New World and stage 1 (Terrain) have a new biome layout, **One biome per island**. The forest island is near the middle, and the islands farther out get the harder biomes. ([#46](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/46))
- **World grid layer.** **Layers > World grid** shows the 150 m street-tile grid that towns snap to. It shows when you zoom in. ([#47](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/47))
- **Ctrl+F** puts the cursor in the search box of the Prefabs tab, or of the Placed tab when it is open. ([#44](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/44))

### Changes

- **Esc** cancels what the tool is doing and keeps the tool. Press **Esc** again to go back to **Select**. ([#35](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/35))
- Click a prefab in the Prefabs tab to start the Prefabs tool with that prefab. ([#36](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/36))
- Press **Q** or **E** to turn a POI to any side of its lot. It stays in the lot. ([#37](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/37))
- The size filter in the Prefabs tab uses one size band for each lot size. ([#38](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/38))
- A POI that you turned in its lot keeps its turn when you drag it to a different lot. If it does not fit that lot with the turn, it faces the street. Press **Q** or **E** while you drag a POI to turn it. ([#42](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/42))
- The Bridge tool's preview shows the ramp at each end, with its length and grade. ([#43](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/43))
- The side panel tabs, the Generate stages and the ribbon tooltips are easier to read. The open tab is bold and underlined. Each stage is a card with a **Done** label when you completed it. The tool options strip is shorter. ([#48](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/48))

### Fixes

- Hold **Alt** to put a POI or trader anywhere. Before, a POI still snapped into a street-tile lot. ([#60](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/60))
- A double-click on "There are no spawn points" in the Issues tab no longer goes to the middle of the map.

### Known issues

- Bridges leave a gap in the ground along the deck, across each end of the deck and around each bridge post. The gaps are cosmetic: vehicles and boats can use the bridges. ([#5](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/5))
- Windows SmartScreen can show "Windows protected your PC" when you start the editor. The editor is not signed. Click **More info**, then **Run anyway**.

## v2.1.0 (2026-11-15)

### New

- **Bridge tool.** Draw your own bridges over the sea between islands, or over a river, canyon or valley inland. Click the start and the end, then press Enter. The editor builds the bridge sections, the footings and a ramp road from each end down to the ground. You can move a bridge, make it longer or shorter, change its deck height, or delete it. The ground comes back as it was. The project keeps your bridges, and the Generate stages keep their roads and POIs off them. ([#3](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/3))
- **Car wrecks on bridges.** Bridges from stage 4 (Highways) and from the Bridge tool get a car wreck about every 100 to 150 m of deck. To get bare decks, clear **Car wrecks on bridges** in stage 4 or **Car wrecks** in the Bridge tool.
- **Export Map Image.** **File > Export Map Image** saves the whole map as a PNG, north at the top, with no windows or tools on it. Select the size and the layers and POIs to show. ([#10](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/10))
- **Friendly names.** The editor shows the game's names, for example **Pine Forest** and **Old West**, for biomes, towns, traders and POIs. Search finds the game name or the file name. ([#8](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/8))
- **Land closer to the sea edges.** Worlds with one island, several islands or a peninsula have less empty sea along the map edges. A single island is now a rounded square, so it also fills more of the corners. ([#4](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/4))
- **The opening quest's trader.** The game's first quest sends each new player to one forest trader. Validate warns if there is no such trader, or if a spawn is 750 m or more from it. Stage 6 (Spawns) has a new setting, **Near the opening quest's trader**, that puts all spawns within 750 m of that trader. ([#30](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/30))
- **Biome seed.** When you tick **Plan the biomes again for this land** in stage 1 (Terrain), a **Biome seed** row with its own **Re-roll** shows. It makes a new biome layout and does not change the terrain. ([#26](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/26))
- **Bridges in Validate.** Validate looks at each bridge. It finds steps at the deck ends, missing roads, steep approaches, ground under the deck, bad footings and too little room under the beams. It also finds the bridges in worlds that the editor did not make.

### Fixes

- Undo and redo:
  - Undo back to the point where you saved no longer asks you to save. ([#25](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/25))
  - Undo after a **Re-roll** puts back the old seed. Before, Generate then made the re-rolled result again. ([#28](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/28))
  - Undo of a stage puts back its Done mark, its status, its plan and its seeds, not only the ground. ([#15](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/15))
  - Changes in the Generate panel mark the project as unsaved. ([#14](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/14))
- Generate:
  - **Generate** is off until a world is open. ([#13](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/13))
  - A new terrain for the whole map deletes the planned towns that are not pinned. ([#12](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/12))
  - After **Build**, **Plan towns** and **Re-roll** take down the towns that are not pinned and plan new towns. Before, they did nothing. ([#23](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/23))
  - Stage 4 (Highways) no longer puts a bridge on a highway that it made before. Before, the guard rails blocked the road. ([#29](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/29))
  - Stage 5 (Wilderness) keeps its POIs and gravel roads off the decks and ramps of the bridges that you drew. ([#31](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/31))
  - A road between a town and a POI near it meets each at its own ground. Before, the road beside the POI had the town's height and made a bank against the POI's fence. ([#32](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/32))
  - Stage 5 keeps POIs farther from a town on higher or lower ground, so a road can go up or down between them. ([#33](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/33))
  - A gravel road that joins a highway meets it at the highway's height and does not lift the highway. ([#34](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/34))
  - Wilderness POIs keep their part markers in file order, as the game does. ([#21](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/21))
- Tools:
  - Lake, river and water stamps always fill with water. After each stamp, the status bar tells you how much the ground and the water changed. ([#24](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/24))
  - The Stamp tool uses the default stamp when a new game folder does not have the stamp that you selected. ([#19](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/19))
  - Double-click an issue in the Export dialog to go to it on the map. ([#27](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/27))
- Damaged files:
  - The editor skips a bad entry in `prefabs.xml` or `spawnpoints.xml` and shows a warning. The rest of the world opens. ([#20](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/20))
  - A `biomes.png` or `radiation.png` of the wrong size stays as it was, with a warning. The map works normally. ([#16](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/16))
  - Game data that the editor cannot read no longer stops later opens and Generate runs. ([#17](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/17))
  - A problem with the editor's own settings file no longer stops an export, save or open. ([#18](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/18))
  - Small fixes from a code review. ([#22](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/22))

### Known issues

- Bridges leave a gap in the ground along the deck, across each end of the deck and around each bridge post. The gaps are cosmetic: vehicles and boats can use the bridges. ([#5](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/5))
- Windows SmartScreen can show "Windows protected your PC" when you start the editor. The editor is not signed. Click **More info**, then **Run anyway**.

## v2.0.0 (2026-11-13)

The editor has a new name: **Gnostic - 7D2D Map Editor**. Copies of v1.2.1 and of the betas update to this version from **Help > Check for Updates**. They keep your settings, and the Start menu and desktop shortcuts get the new name.

This version contains all the changes in v2.0.0-beta.1, beta.3 and beta.4 below. The most important changes since v1.2.1 are:

- **Generate panel.** Make a complete world in six stages: terrain, town sites, features, highways, wilderness and spawns. Run a stage again with a new seed, and the other stages stay as they are.
- **Land shapes.** A new world can be one island, several islands or a peninsula. Stage 4 (Highways) can connect the islands with bridges.
- **Presets.** Eight built-in presets change the game's numbers for your world size. You can also save your own presets.
- **Your own prefabs.** The editor loads the prefabs in `%APPDATA%\7DaysToDie\LocalPrefabs`.
- **Export writes every prefab at ground level**, in the format that the game's own editor writes.

### Fixes

- Some bridges from stage 4 (Highways) had a step in the road at one end. The step was up to 3 m high. The editor now builds a bridge only where the road can go up or down to the ground at each end of the bridge. ([#9](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/9))
- Setup said "already installed" after you used the portable editor. The portable editor kept its POI pictures in the folder that Setup installs to. The editor now keeps the pictures in `%LOCALAPPDATA%\7D2DMapEditor-Cache` and removes the old folder. ([#11](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/11))
- **Help > About** showed version 1.0.0. It now shows the full version, for example 2.0.0. ([#7](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/7))

### Changes

- The program file is now `Gnostic.exe`. The downloads are `Gnostic-7D2DMapEditor-win-Setup.exe` and `Gnostic-7D2DMapEditor-win-Portable.zip`.
- In stage 6 (Spawns), the card tells you that **At least** is a minimum. Gravel roads from stage 5 (Wilderness) add spawn points first, with no limit, as in the game. A note shows when stage 5 has not put roads on the map. ([#6](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/6))

### Known issues

- Bridges from stage 4 (Highways) leave a gap in the ground along the deck, across each end of the deck and around each bridge post. The gaps are cosmetic: vehicles and boats can use the bridges. ([#5](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/5))
- Windows SmartScreen can show "Windows protected your PC" when you start the editor. The editor is not signed. Click **More info**, then **Run anyway**.

## v2.0.0-beta.4 (2026-11-13)

### Fixes

- In stage 1 (Terrain), **Plan the biomes again for this land** stays ticked when you run the stage again. Before, each run cleared the tick, and the next run kept the old biome layout. Only you can clear the tick. When you open a world, the tick shows if its biome layout does not fit its land. ([#2](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/2))
- The map shows a plain sea floor under water. Before, the map showed the biome colours under the sea, so the sea between islands had blocky patches of colour.

### Changes

- Tests now open projects from each earlier release, so an update cannot stop your saved projects from opening.

### Known issues

- Bridges from stage 4 (Highways) leave a gap in the ground along the deck, across each end of the deck and around each bridge post. The gaps are cosmetic: vehicles and boats can use the bridges. ([#5](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/5))
- Windows SmartScreen can show "Windows protected your PC" when you start the editor. The editor is not signed. Click **More info**, then **Run anyway**.

## v2.0.0-beta.3 (2026-11-11)

### New

- **Land shapes.** A new world can be one island, several islands or a peninsula. Select the shape in **New World** under **Land shape**, or in stage 1 (Terrain). Set the land share, the coast roughness, the number of islands and the peninsula edge. The coast has its own seed. The land goes down from a beach to a sea floor 12 m below the water. The editor fills the sea with water to 30 m.
- **Biome layouts fit the land.** On an island world, the share of each biome is a share of the land. Biomes also grow across the sea to other islands. The project keeps the biome layout. If you change the land shape, stage 1 can make the biome layout again for the new land.
- **Bridges between islands.** In stage 4 (Highways), tick **Bridge islands**. The editor then connects towns on different islands with the game's bridge sections. Each bridge goes straight north–south or east–west. The water stays open under the bridge, so boats can go under it. **Longest bridge** sets the maximum length (1,500 m by default).
- **Shortcut bridges.** This setting adds a direct bridge between two islands if the route between them crosses three or more bridges.
- **Your own prefabs.** The editor now loads the prefabs in `%APPDATA%\7DaysToDie\LocalPrefabs`. The game's prefab editor saves prefabs in this folder. If a prefab in this folder has the same name as a game prefab or a mod prefab, the editor uses your prefab. The game does the same.
- **Placed search on the map.** When the **Placed** tab shows and the search box has text, the map shows a ring around each prefab that matches. Click a row to zoom to its prefab. To also select the prefab, double-click the row or press Enter. Each row shows the biome at the centre of the prefab.

### Changes

- In stage 1 (Terrain), **Show the last run's tile types** is now off by default.
- Stage 1 (Terrain) puts no plains, hills or mountains on sea tiles.
- Stage 3 (Features) does not start a lake, river, canyon or crater in the sea of a land shape.
- No gravel road or country road connects to a highway within 80 m of a bridge.
- **Validate** does not flag a bridge as under water. For the game's bridge sections, it compares the road with the deck, not with the ground.
- The side panel shows by default. The editor remembers if you close it.

A project with bridges does not open in v2.0.0-beta.1 or earlier.

## v2.0.0-beta.1 (2026-11-09)

This is a beta for testers. It is a pre-release, so copies of v1.2.1 do not get it as an update. A beta copy gets each later beta, and then v2.0.0.

### New

- **Generate panel.** The editor can make a complete world the way the game's world generator does. The **Generate** tab has six stages. Do them in sequence. You can run a stage again with a new seed, and the other stages stay as they are. Each stage runs in the background, with progress and Cancel. Each stage is one undo step.
  1. **Terrain.** Each 256 m tile gets plains, hills or mountains. The editor then puts the game's terrain stamps on the tiles. Set the weights for each biome, the land or ocean edges, and the tiles that you paint by hand. You can generate the whole map or an area that you drag.
  2. **Town sites.** The editor plans towns and cities from the game's town types, with the game's counts for the world size. Before you build, you can move a town, re-roll it, remove it or add one. **Build** makes all the towns: streets, districts, POIs, traders and flat ground. Traders follow the game's limits for each biome. A setting gives one trader to each town. Another setting lets towns cross biome borders.
  3. **Features.** The editor puts lakes, rivers, canyons and craters into the ground. Options include long rivers that flow downhill and lakes on mountains.
  4. **Highways.** The editor connects the towns with asphalt highways. By default, one highway connects two towns. A setting allows a second highway.
  5. **Wilderness.** The editor puts POIs outside the towns and connects them to the roads with gravel roads.
  6. **Spawns.** The editor puts player spawn points near traders and roads, as the game does. You can set the minimum and maximum number and the biomes.
- **Presets.** Eight built-in presets change the game's numbers for your world size: Big cities, Frontier, Border towns, Easy start, Rugged, Flatlands, Waterways and Wasteland heavy. You can also save your own presets. The project keeps the Generate settings.
- **The Town tool uses the world's 150 m street-tile grid.** A town that you draw now aligns with the towns from Town sites. Street tiles between towns then fit. Hold **Alt** when you start the drag to start the grid at the mouse position.

### Changes

- **Export writes every prefab at ground level.** The file uses the format that the game's own editor writes. A POI stays on its ground if a game update or a mod changes the POI's height offset. Prefabs that your game folder does not have keep the format that the editor read. The export dialog tells you how many.
- **Autosave does not stop the editor.** Autosave no longer copies the whole world first. On a 16k world, this prevents a pause and saves about 1.5 GB of memory.
- **The minimap does not go blank when you edit.** The old image stays until the new image is ready.
- **A failed edit is undone.** If an edit fails, the editor puts the world back as it was and tells you. A failed road or town keeps its points or plan, so you can try again.
- **The editor skips large extra files in a world folder.** It skips a file over 256 MB, or files over 1 GB in total, and shows a warning. The world still saves as a project that opens again.

### Known issues

- Windows SmartScreen can show "Windows protected your PC" when you start the editor. The editor is not signed. Click **More info**, then **Run anyway**.

## v1.2.1 (2026-10-31)

### Fixes

- **Worlds that you saved in the game's own world editor now open at the right heights.** The game's editor stores the height of each POI differently from the world generator. The map editor read these heights the generator's way. So after a save in the game's editor, Review listed hundreds of POIs that "don't meet the road", on a map that nobody changed. A POI that you moved in such a world could also be buried in the game. This version fixes both problems. Navezgane and the Pregen maps use the same format, so the editor now reads them correctly too. The editor saves each prefab back in the format that it read.
- **Fewer road warnings.** Review now flags a POI or street tile only in one case. The road beside it is more than 1 m (one block) higher or lower than its ground. The world generator leaves many one-block steps, and players and vehicles can cross them. The same limit applies when you place a prefab.

Projects that this version saves do not open in v1.2.0 or earlier.

## v1.2.0 (2026-10-24)

### New

- **Generated biome layouts.** New World can arrange all the biomes the way the game's world generator does. You do not have to paint one biome over the whole map. Choose where the biomes start: forest or wasteland in the middle, a ring, two rings, or a line. Set the share of the map for each biome. The same seed gives the same layout.
- **Placed list.** A **Placed** tab in the side panel lists every POI, street tile and part in your world. Search by name and filter by type. To go to a prefab and select it, double-click its row or press Enter.
- **Favourite prefabs.** Click the ☆ beside a prefab in the browser or on its info card. **Favourites** in the category list shows them. Your favourites stay across worlds, projects and updates.
- **Replace from the browser.** Select a prefab on the map, then pick a different prefab in the browser. The info card shows the new prefab as the replacement. Press **Replace** to use it, or press **Back** (Esc) to keep the old prefab.
- **Tier badge.** The info card shows the tier of the prefab.
- **Compact ribbon.** On short windows, the ribbon becomes one row of icons. **View > Ribbon** keeps it full or compact.

## v1.1.2 (2026-10-20)

### Fixes

- **Safer saving.** The editor writes a world folder in full before it replaces any old file. If something fails, the editor puts the old files back. So a failed export never leaves a mix of old and new files.
- When you close the editor, it waits for a save in progress to finish. The editor pauses edits while a save runs.
- Export no longer writes an old copy of the original image for a layer that you edited.
- If the editor cannot repaint a biome map, it now keeps the map as it was. Before, it painted over the map. The map also stays as it was after an edit and an undo.
- The editor now refuses damaged or unusual project files and PNGs, and shows a message. Before, it crashed.

## v1.1.1 (2026-10-20)

### Fixes

- **No lip where a road meets a prefab.** Drawn roads now level their flat parts to whole metres, the height that prefabs sit at. So a POI, trader or street tile beside the road is level with the road. Slopes do not change. Roads that you drew before do not change.
- **Road warnings.** If you place a prefab where the road beside it is higher or lower than its ground, the editor warns you. The Issues panel lists each prefab that is not level with its road.
- The Flatten tool has a **Whole metres** box, which is on by default. The box rounds the height that you pick from the map.

## v1.1.0 (2026-10-19)

### New

- **Road snapping.** Click near the end of any road to continue it with the same surface and width. A segment within 5° of north, south, east or west snaps straight. Hold Alt to draw freely.
- **Road presets:** Highway (18 m), Main street, Town street, Residential, Country road (9 m gravel) and Track.
- **Select tool** (V) replaces Pan. Click a POI, trader, street tile or spawn point to select it. Drag it to move it. Press Q/E to turn it, PgUp/PgDn to raise or lower it, or Del to remove it. Drag empty ground to pan the map.
- **Trader tool** (A). Pick a trader or press **Random**. Then place it like any prefab.
- **Town edits.** Before you build a town, you can drag the edges or corners of its box. This changes the size of the town in 150 m tiles. Click a link road to disable it. Click it again to enable it.
- **Snapping to street tiles.** POIs and traders snap to the edges of nearby street tiles. Drop a POI over the middle of another POI to replace it. Street tiles snap to the grid of a nearby tile, or stay where you put them. Street tiles cannot overlap.
- **Stamp preview.** The cursor of the Stamp tool shows the stamp from above. The preview has the same angle and size as the stamp that you will apply.
- **Check for Updates** in the Help menu. Copies from this version onwards update themselves.
- Message boxes follow the light or dark theme.

### Changes

- Placing a POI, trader, street tile or town never changes the roads that you drew. If you place one against a road, it takes the height of the road.

## v1.0.0 (2026-10-18)

First release.

- **Open and edit** a world that the game generated, or create a new world. The size can be any multiple of 512 m, with a land or water border.
- **Terrain tools:** Raise, Lower, Flatten, Smooth, Roughen, and stamps from the game's own `Data/Stamps`. The editor blends stamps the way the world generator does. Undo and redo.
- **Paint** biomes (in the game's colours), radiation and water.
- **Roads:** draw asphalt or gravel roads of any width. The editor grades the ground under them.
- **Prefabs:** browse the game's POIs with filters and pictures. Then place, turn and move them. The editor flattens and blends the ground under them. You can also place street tiles, whole towns and spawn points.
- **Projects** (`.7dmap`) save everything in about a second. The editor autosaves every 5 minutes and offers to recover your work after a crash.
- **Validate and export.** Validate (F7) finds problems such as overlapping or flooded POIs, missing spawns or traders, and prefabs outside the world. Export (Ctrl+E) writes a world folder that the game loads directly.
- A Paint-style window with a ribbon, palette, brush sliders, status bar, and light and dark themes.
