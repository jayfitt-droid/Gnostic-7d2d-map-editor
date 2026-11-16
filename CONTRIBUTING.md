# Contributing

Bug reports and ideas go in [issues](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues). This page is for changes to the code.

## Build and test

```
dotnet build MapEditor.slnx -c Release
dotnet test MapEditor.slnx -c Release
```

This needs Windows and the .NET 10 SDK (pinned in `global.json`). `dotnet test` doesn't build the WPF editor, so also run `dotnet build src/MapEditor.App -c Release`. Set `MAPEDITOR_TEST_WORLD` to a world folder to also round-trip a real world in the tests.

| Project | Purpose |
|---|---|
| `src/MapEditor.Core` | World model, file I/O (its own PNG codec, no alpha premultiply), `.7dmap` projects, validation, terrain/paint/road/prefab/town operations, the generator, game-install discovery |
| `src/MapEditor.App` | WPF editor (Fluent theme, follows the Windows light/dark setting). It has no test project: put logic that can be tested in Core |
| `src/MapEditor.Cli` | `mapeditor-cli` with `info`, `roundtrip`, `export`, `validate`, `compare`, `render`, `new` and `installs`, for testing and scripting |
| `tests/MapEditor.Core.Tests` | xUnit v3 tests |

## Rules for code

- **Every edit goes through `EditRunner`** (Core): `Run` for one-shot edits, `Begin`/`Step`/`Commit` for strokes and drags, `RunInBackgroundAsync` for long stages. A failed edit rolls back through `EditSession.Rollback`. MainWindow listens to `EditRunner.Committed`; tool controllers don't push undo steps themselves.
- **Any in-place grid change outside an `EditSession` must call `Grid.BeforeChange` first.** Autosave holds the layers by reference (`WorldSnapshot`) and relies on it to keep the old cells.
- **Prefabs are written in the game's ground-level form** (Y at ground level, `y_is_groundlevel="true"`), via `GroundLevel.ForFile`. Prefabs the library doesn't know keep the form they were read in.
- **Never break `.7dmap` saves.** Old projects must still load: bump `ProjectStore.FormatVersion` and migrate in Load. Most saved enums are stored as numbers: only add members at the end, never reorder or remove. Enums marked `JsonStringEnumConverter` (`GeneratorStage`, `FeatureKind`, `HighwayKind`, `LandShapeKind`, `MapEdge`) are stored by name: never rename a member. `SaveCompatibilityTests` loads a sample save from each release.
- **Game behaviour is reimplemented, never copied.** Game assets are read from the user's install at runtime, never bundled.

## Pull requests

- Every change goes through a pull request. Make one branch per issue off an up-to-date `main`, for example `issue-39-teleport-copy`.
- Tests must pass, and CI (`build`) must pass, before a merge. PRs are rebase-merged, so each commit lands on `main` as written: keep commits tidy, and commit changes that share no files separately.
- Refer to an issue as `(#39)`. Don't write closing keywords such as `Fixes #39`: issues close when the release that fixes them is out.
- A change that players will notice also updates `docs/USER-MANUAL.md` (written in Simplified Technical English) and, if it fits there, the README.

## Releases

Releases follow [docs/RELEASING.md](docs/RELEASING.md).
