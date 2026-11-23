using MapEditor.Core.Edit;
using MapEditor.Core.Generate;

namespace MapEditor.App.Editing;

/// <summary>
/// The terrain preview and status lines the panel restores with the Core generator settings on undo or redo (#15).
/// </summary>
internal sealed record GeneratorUndoState(
    TerrainType[]? LastPlan,
    BiomeMaskRecord? LastMask,
    string[] Statuses,
    string?[] ShortStatuses)
{
    /// <summary>
    /// Saves <paramref name="gen"/>'s and the panel's state in <paramref name="session"/> (UI thread).
    /// <paramref name="seedsBefore"/>: the seeds from before a Re-roll changed one ahead of the run, so undo puts the
    /// old seed back with the old ground.
    /// </summary>
    public static void Touch(EditSession session, GeneratorSettings gen, GenerateViewModel panel, int[]? seedsBefore = null)
    {
        GeneratorEditState.Touch(session, gen, seedsBefore);
        session.TouchState(panel, () => Capture(panel), state => ((GeneratorUndoState)state!).Restore(panel));
    }

    private static GeneratorUndoState Capture(GenerateViewModel g) => new(
        (TerrainType[]?)g.LastPlan?.Clone(),
        g.LastMask,
        [g.TerrainStatus, g.TownStatus, g.FeatureStatus, g.HighwayStatus, g.WildernessStatus, g.SpawnStatus],
        [g.TerrainStatusShort, g.TownStatusShort, g.FeatureStatusShort, g.HighwayStatusShort, g.WildernessStatusShort, g.SpawnStatusShort]);

    private void Restore(GenerateViewModel g)
    {
        g.LastPlan = (TerrainType[]?)LastPlan?.Clone();
        g.LastMask = LastMask;
        // A status change clears its short line, so set the status first.
        (g.TerrainStatus, g.TownStatus, g.FeatureStatus, g.HighwayStatus, g.WildernessStatus, g.SpawnStatus) =
            (Statuses[0], Statuses[1], Statuses[2], Statuses[3], Statuses[4], Statuses[5]);
        (g.TerrainStatusShort, g.TownStatusShort, g.FeatureStatusShort, g.HighwayStatusShort, g.WildernessStatusShort, g.SpawnStatusShort) =
            (ShortStatuses[0], ShortStatuses[1], ShortStatuses[2], ShortStatuses[3], ShortStatuses[4], ShortStatuses[5]);
        g.RefreshDone();
        g.ShapeChanged();
        // Redraws every box from the settings (the seeds above), without marking them as the user's edits.
        g.Refresh();
    }
}
