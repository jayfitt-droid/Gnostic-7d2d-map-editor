# Releasing

The checklist for every release, beta or full. The source, tags and releases are all in this repo; pushing a `v*` tag builds the release and publishes it here (`.github/workflows/release.yml`). Installed copies check this repo's releases from **Help > Check for Updates** (`src/MapEditor.App/Updates.cs`).

## Before tagging

1. **Issues.** Every issue in the release's milestone is closed, or moved to a later milestone. Bugs come before features.
2. **Tests.** `dotnet test MapEditor.slnx -c Release` passes with no warnings.
3. **Changelog.** Add the release to the top of `CHANGELOG.md`: `## vX (date)`, then `### New`, `### Fixes`, `### Changes` as needed. Write for players, in Simplified Technical English (lint with the `asd-ste100` skill's `ste-lint.py`). Link public issues as `([#N](https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor/issues/N))`.
4. **Known issues.** List the open bugs players should know about:

   ```bash
   gh issue list -R jayfitt-droid/Gnostic-7d2d-map-editor -l known-issue --state open
   ```

   Add a `### Known issues` section last in the entry: one short sentence per issue, what players see and how much it matters, with its link. Leave the section out when the list is empty. A bug fixed in this release moves to Fixes; remove its `known-issue` label when the release is out. Label a bug `known-issue` only when it is confirmed and not fixed on `main`. Also keep the standing items that have no issue, such as the SmartScreen warning while the build is not signed. Copy the same list to the **Known issues** section of `release/mod-page.md` and `.bbcode` (above Install).
5. **User manual.** Update `docs/USER-MANUAL.md` for anything that changed how a tool works. A lasting game limit (one with no fix) goes in its Known issues section.

## Tagging

6. Commit "Add vX to the changelog" on a branch, open a PR and rebase-merge it when CI passes. Then `git switch main`, `git pull`, tag the merged changelog commit `vX` and push the tag. A `-` in the tag (`v2.1.0-beta.1`) makes a pre-release that only beta copies update to.
7. Watch the release workflow (`.github/workflows/release.yml`). If Publish fails, fix the cause (the workflow needs `contents: write`) and rerun with `gh run rerun <id> --failed`.

## After tagging

8. **Save sample.** `powershell -File tools/save-samples/make-sample.ps1 -Tag vX`, add the tag to `SaveCompatibilityTests.Samples` and `PresetSamples`, run the tests, commit on a branch and merge it through a PR. Never remake a sample that is committed.
9. **Release notes.** Copy the release's changelog entry (without its heading) to the release: `gh release edit vX -R jayfitt-droid/Gnostic-7d2d-map-editor --notes-file notes.md`.
10. **Game Link.** Build the companion mod from the release source on Windows with 7 Days to Die 3.4 installed. Check that `mod/GnosticGameLink/dist/GnosticGameLink-<version>.zip` contains `GnosticGameLink/ModInfo.xml` and `GnosticGameLink/GnosticGameLink.dll` only. Attach the ZIP as a separate asset on the editor release page after the workflow publishes the release. Use the mod version in `ModInfo.xml` for the ZIP name. [Build the Game Link mod](../README.md#build-the-game-link-mod).
11. **Issues and mod page.** Close the issues the release fixed, and reply on the mod site to reports that came from there. Update `release/mod-page.md` and `.bbcode` if the release changes what the page says, and paste the BBCode onto the mod site. For a full release with new features, replace the **New in vX** section near the top of both files: three to five one-line highlights, the most eye-catching feature first, so players see them before the long feature list. Fix-only releases keep the last one.
