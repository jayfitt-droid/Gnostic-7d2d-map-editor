# Gnostic - 7D2D Map Editor

**A Windows map editor for 7 Days to Die. Edit a world the game generated, build one by hand, or generate a complete world in six stages and change any part of it.**

## New in v2.1.0

**Draw your own bridges.** The new Bridge tool builds a bridge with footings and ramps over the sea, a river or a valley. Move it, resize it or take it down, and the ground comes back as it was.

**Export the map as a picture.** File > Export Map Image saves the whole map as a PNG with no windows or tools on it, at up to 1 pixel per metre.

**Friendly names.** Pine Forest and Old West, not pine_forest and oldwest, for biomes, towns, traders and POIs.

**More land on island worlds.** Islands come closer to the map edges, with less empty sea.

**Spawns near the first trader.** Validate and stage 6 keep spawns near the trader that the game's opening quest sends new players to.

Also 21 fixes. The [changelog](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/blob/main/CHANGELOG.md) lists them all.

## Features

**Generate**
- Make a complete world in six stages: terrain, town sites, lakes and rivers, highways, wilderness POIs and spawns. Re-roll any stage and keep the rest.
- One island, a chain of islands or a peninsula, with highway bridges between the islands.
- Eight presets, or save your own.

**Terrain**
- Raise, Lower, Flatten, Smooth, Roughen.
- Stamps from the game's own `Data/Stamps`, blended the way the world generator does.

**Paint**
- Biomes in the game's colours, radiation and water.
- New worlds can start with one biome or a generated layout of all biomes (forest or wasteland in the middle, rings or a line).
- New worlds can be land to the edges, one island, several islands or a peninsula.

**Roads**
- Asphalt or gravel, any width. The ground under them is graded.
- Roads snap to each other and to straight lines. Presets match the game's highways and country roads.
- Draw your own bridges, with ramps to the ground and car wrecks on the deck.

**Prefabs, towns and spawns**
- Browse the game's POIs, mod POIs and your own `LocalPrefabs` with pictures, tiers and filters. Mark favourites.
- Search the POIs in your world. Each match gets a ring on the map, and a click zooms to it.
- Place, move, turn, raise and replace POIs, traders and street tiles. They snap to street tiles and sit level with the road.
- Draw a whole town on the 150 m street-tile grid.
- Place player spawn points.

**Safe to use**
- Undo and redo for every edit.
- Projects (`.7dmap`) save in about a second. Autosave every 5 minutes, with recovery after a crash.
- **Validate** (F7) finds overlapping or flooded POIs, missing traders or spawns, prefabs off the map, POIs that don't meet their road, bridge problems and spawns far from the first trader.
- Export writes all files first and only then replaces the old ones, so a failed export never breaks a world.

**Easy to learn**
- A Paint-style window: ribbon, palette, brush sliders and status bar.
- Light and dark themes that follow Windows.
- Export the whole map as a PNG picture.
- Package your maps and their custom prefabs as a mod zip to share.
- A full [user manual](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/blob/main/docs/USER-MANUAL.md).

---

## Requirements

- Windows 10 or 11 (64-bit)
- 7 Days to Die, version 3.x

The editor reads stamps, prefabs and biome colours from your own copy of the game. It does not include game files.

---

## Known issues

- Bridges leave a gap in the ground along the deck, across each end of the deck and around each bridge post. The gaps are cosmetic: vehicles and boats can use the bridges. ([#5](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/5))
- Windows SmartScreen can show "Windows protected your PC" when you start the editor. The editor is not signed. Click **More info**, then **Run anyway**.

## Install

Download one of these files from GitHub. The links always get the latest version.

- **[Gnostic-7D2DMapEditor-win-Setup.exe](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/releases/latest/download/Gnostic-7D2DMapEditor-win-Setup.exe)** – installs for your Windows user. It does not need administrator rights.
- **[Gnostic-7D2DMapEditor-win-Portable.zip](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/releases/latest/download/Gnostic-7D2DMapEditor-win-Portable.zip)** – does not install. Unzip it and run `Gnostic - 7D2D Map Editor.exe`.

Older versions and the release notes are on the [releases page](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/releases).

The installed version updates itself from **Help > Check for Updates**.

The editor finds the game through Steam. If it can't, use **Settings > Game Folder** and pick the folder that contains `Data`.

**Windows SmartScreen** can show "Windows protected your PC", because the editor is not signed. Click **More info**, then **Run anyway**.

---

## Quick start

1. **New World** (Ctrl+N), or **Open** (Ctrl+O) an existing world's `map_info.xml` or `dtm.raw`.
2. Use the tools, the **Generate** panel, or both.
3. **Save** (Ctrl+S) as a `.7dmap` project while you work.
4. **Export** (Ctrl+E) into the game's `GeneratedWorlds` folder.
5. Start a **new game** on the world.

Press **F1** in the editor for the keyboard shortcuts. The [user manual](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/blob/main/docs/USER-MANUAL.md) explains every tool, with example workflows.

---

## Bugs and feedback

To report a bug, use the [bug report form](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/new?template=bug_report.yml) on GitHub. The form asks for the details we need to find the problem. For an idea, use the [feature request form](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/new?template=feature_request.yml).

No GitHub account? Post in the comments with the editor version (**Help > About**), the world size, the tool or stage that you used, and what happened. We copy each report to GitHub and reply with its link. If the editor crashed, attach `crash.log` from `%APPDATA%\7D2DMapEditor`.

## Support my work

The editor is free, and I build it in my spare time. If it saves you time or helps you make a world you like, you can buy me a coffee on [Ko-fi](https://ko-fi.com/jayfitt). Thank you!

---

*This is a fan-made tool. It is not made by or affiliated with The Fun Pimps.*
