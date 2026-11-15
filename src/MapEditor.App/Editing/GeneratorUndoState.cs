using MapEditor.Core.Edit;
using MapEditor.Core.Generate;
using MapEditor.Core.Ops;

namespace MapEditor.App.Editing;

/// <summary>
/// The generator's bookkeeping that lives outside the world: which stages are done, the terrain tile plan and biome
/// mask, the shape the biomes were planned for, each stage card's status line, and the seeds. A generator edit saves it
/// in its undo step, so Ctrl+Z on a stage brings back the plan, marks, text and seeds from before the run, not just the
/// ground (#15).
/// </summary>
internal sealed record GeneratorUndoState(
    HashSet<GeneratorStage> Done,
    TerrainType[]? TerrainPlan,
    TerrainType[]? LastPlan,
    BiomeMaskRecord? LastMask,
    BiomeLayoutSettings? Biomes,
    LandShape? PlannedFor,
    string[] Statuses,
    string?[] ShortStatuses,
    int[] Seeds)
{
    /// <summary>
    /// Saves <paramref name="gen"/>'s and the panel's state in <paramref name="session"/> (UI thread).
    /// <paramref name="seedsBefore"/>: the seeds from before a Re-roll changed one ahead of the run, so undo puts the
    /// old seed back with the old ground.
    /// </summary>
    public static void Touch(EditSession session, GeneratorSettings gen, GenerateViewModel panel, int[]? seedsBefore = null)
    {
        // Only the first capture (the state before the edit) takes them; later captures are the live state for redo.
        var before = seedsBefore;
        session.TouchState(gen, () =>
        {
            var state = Capture(gen, panel);
            if (before is not null) state = state with { Seeds = before };
            before = null;
            return state;
        }, state => ((GeneratorUndoState)state!).Restore(gen, panel));
    }

    /// <summary>Every seed the undo state keeps, in its order.</summary>
    public static int[] SeedsOf(GeneratorSettings gen) =>
        [gen.WorldSeed, gen.Terrain.Seed, gen.Terrain.Shape.Seed, gen.Towns.Seed, gen.Features.Seed, gen.Highways.Seed, gen.Wilderness.Seed, gen.Spawns.Seed, gen.Biomes?.Seed ?? 0];

    private static GeneratorUndoState Capture(GeneratorSettings gen, GenerateViewModel g) => new(
        [.. gen.Done],
        (TerrainType[]?)gen.TerrainPlan?.Clone(),
        (TerrainType[]?)g.LastPlan?.Clone(),
        g.LastMask,
        gen.Biomes,
        gen.Biomes?.PlannedFor.Clone(),
        [g.TerrainStatus, g.TownStatus, g.FeatureStatus, g.HighwayStatus, g.WildernessStatus, g.SpawnStatus],
        [g.TerrainStatusShort, g.TownStatusShort, g.FeatureStatusShort, g.HighwayStatusShort, g.WildernessStatusShort, g.SpawnStatusShort],
        SeedsOf(gen));

    private void Restore(GeneratorSettings gen, GenerateViewModel g)
    {
        gen.Done = [.. Done];
        // Copies, so the state kept for redo never shares an array or shape with the live settings.
        gen.TerrainPlan = (TerrainType[]?)TerrainPlan?.Clone();
        g.LastPlan = (TerrainType[]?)LastPlan?.Clone();
        g.LastMask = LastMask;
        gen.Biomes = Biomes;
        if (Biomes is not null && PlannedFor is not null) Biomes.PlannedFor = PlannedFor.Clone();
        // A status change clears its short line, so set the status first.
        (g.TerrainStatus, g.TownStatus, g.FeatureStatus, g.HighwayStatus, g.WildernessStatus, g.SpawnStatus) =
            (Statuses[0], Statuses[1], Statuses[2], Statuses[3], Statuses[4], Statuses[5]);
        (g.TerrainStatusShort, g.TownStatusShort, g.FeatureStatusShort, g.HighwayStatusShort, g.WildernessStatusShort, g.SpawnStatusShort) =
            (ShortStatuses[0], ShortStatuses[1], ShortStatuses[2], ShortStatuses[3], ShortStatuses[4], ShortStatuses[5]);
        // The seeds the run used come back too, so the seed boxes match the ground and plans again. Set directly, not
        // through SetWorldSeed, which would work every stage's seed out again from the world seed.
        (gen.WorldSeed, gen.Terrain.Seed, gen.Terrain.Shape.Seed, gen.Towns.Seed, gen.Features.Seed, gen.Highways.Seed, gen.Wilderness.Seed, gen.Spawns.Seed) =
            (Seeds[0], Seeds[1], Seeds[2], Seeds[3], Seeds[4], Seeds[5], Seeds[6], Seeds[7]);
        // The biome seed is kept on the layout, which the state holds by reference, so it is put back by value (#26).
        if (Biomes is not null) Biomes.Seed = Seeds[8];
        g.RefreshDone();
        g.ShapeChanged();
        // Redraws every box from the settings (the seeds above), without marking them as the user's edits.
        g.Refresh();
    }
}
