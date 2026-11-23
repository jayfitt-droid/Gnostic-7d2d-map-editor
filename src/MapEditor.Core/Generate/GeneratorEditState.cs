using MapEditor.Core.Edit;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Generate;

/// <summary>The generator settings that a stage edit can change outside its world and placed lists.</summary>
public static class GeneratorEditState
{
    /// <summary>
    /// Saves the settings in the undo step. A caller that changes a seed as part of the edit can call this before
    /// changing it, or supply the seeds from before the edit was started.
    /// </summary>
    public static void Touch(EditSession session, GeneratorSettings settings, int[]? seedsBefore = null)
    {
        var before = seedsBefore;
        session.TouchState(settings, () =>
        {
            var state = new State([.. settings.Done], (TerrainType[]?)settings.TerrainPlan?.Clone(),
                settings.Biomes, settings.Biomes?.PlannedFor.Clone(), before ?? SeedsOf(settings));
            before = null;
            return state;
        }, value =>
        {
            var state = (State)value!;
            settings.Done = [.. state.Done];
            settings.TerrainPlan = (TerrainType[]?)state.Plan?.Clone();
            settings.Biomes = state.Biomes;
            var seeds = state.Seeds;
            (settings.WorldSeed, settings.Terrain.Seed, settings.Terrain.Shape.Seed, settings.Towns.Seed,
                settings.Features.Seed, settings.Highways.Seed, settings.Wilderness.Seed, settings.Spawns.Seed) =
                (seeds[0], seeds[1], seeds[2], seeds[3], seeds[4], seeds[5], seeds[6], seeds[7]);
            if (settings.Biomes is { } biomes)
            {
                if (state.PlannedFor is not null) biomes.PlannedFor = state.PlannedFor.Clone();
                biomes.Seed = seeds[8];
            }
        });
    }

    /// <summary>Captures the seeds before a Re-roll changes one.</summary>
    public static int[] SeedsOf(GeneratorSettings settings) =>
        [settings.WorldSeed, settings.Terrain.Seed, settings.Terrain.Shape.Seed, settings.Towns.Seed, settings.Features.Seed,
            settings.Highways.Seed, settings.Wilderness.Seed, settings.Spawns.Seed, settings.Biomes?.Seed ?? 0];

    private sealed record State(HashSet<GeneratorStage> Done, TerrainType[]? Plan,
        BiomeLayoutSettings? Biomes, LandShape? PlannedFor, int[] Seeds);
}
