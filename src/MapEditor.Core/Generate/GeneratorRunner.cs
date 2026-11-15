using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;

namespace MapEditor.Core.Generate;

/// <summary>The game data the Generate stages read.</summary>
public sealed record GeneratorData(StampLibrary Stamps, RwgMixer Mixer, PrefabLibrary Prefabs, BiomeTable Biomes);

/// <summary>What one stage did, in a line, and how long it took.</summary>
public sealed record StageReport(GeneratorStage Stage, string Summary, TimeSpan Time);

/// <summary>
/// Runs Generate stages one after another with no one watching, as the Generate panel does when each button is
/// pressed in turn: terrain, town sites (planned and built), features, highways, wilderness, spawns. It keeps the same
/// bookkeeping in <see cref="GeneratorSettings"/> (the terrain plan, the sites, the placed lists and
/// <see cref="GeneratorSettings.Done"/>), so a project saved afterwards opens in the app ready to carry on.
/// </summary>
/// <remarks>
/// It only adds stages. A stage that has run, or any later one, would have to be taken down first (the app does
/// that), so asking for one is refused.
/// </remarks>
public static class GeneratorRunner
{
    /// <summary>The stages in the order they run.</summary>
    public static IReadOnlyList<GeneratorStage> Order { get; } =
    [
        GeneratorStage.Terrain, GeneratorStage.TownSites, GeneratorStage.Features,
        GeneratorStage.Highways, GeneratorStage.Wilderness, GeneratorStage.Spawns,
    ];

    /// <summary>
    /// Runs <paramref name="stages"/> in <see cref="Order"/>, whatever order they are given in.
    /// <paramref name="progress"/> gets the running stage and how far through it is (0 to 1), and
    /// <paramref name="finished"/> gets each stage's report as soon as it is done.
    /// </summary>
    public static List<StageReport> Run(World world, GeneratorSettings settings, GeneratorData data, IEnumerable<GeneratorStage> stages,
        IProgress<(GeneratorStage Stage, double Done)>? progress = null, Action<StageReport>? finished = null,
        CancellationToken cancel = default)
    {
        var wanted = stages.ToHashSet();
        var run = Order.Where(wanted.Contains).ToList();
        if (run.Count == 0) return [];
        if (world.Biomes is null) throw new InvalidOperationException("The world has no biome map, which the terrain follows.");
        var already = Order.SkipWhile(s => s != run[0]).Where(settings.Done.Contains).ToList();
        if (already.Count > 0)
            throw new InvalidOperationException($"The {Name(already[0])} stage has already run on this world. Run stages only after the last one that ran.");

        // As the Generate panel does when it loads a world: empty town rows take the game's numbers, and the
        // feature rows follow the mixer.
        if (settings.Towns.Rows.Count == 0) settings.Towns.Rows = TownSiteStage.DefaultRows(data.Mixer);
        if (data.Mixer.Townships.Count > 0 || settings.Features.Rows.Count > 0)
            settings.Features.Rows = FeatureStage.RowsFor(settings.Features, data.Mixer);

        var reports = new List<StageReport>();
        foreach (var stage in run)
        {
            cancel.ThrowIfCancellationRequested();
            progress?.Report((stage, 0));
            var stageProgress = progress is null ? null : new StageProgress(stage, progress);
            var started = DateTime.UtcNow;
            var session = new EditSession(Name(stage), world);
            string summary = stage switch
            {
                GeneratorStage.Terrain => Terrain(world, settings, data, session, stageProgress, cancel),
                GeneratorStage.TownSites => Towns(world, settings, data, session, cancel),
                GeneratorStage.Features => Features(world, settings, data, session, stageProgress, cancel),
                GeneratorStage.Highways => Highways(world, settings, data, session, stageProgress, cancel),
                GeneratorStage.Wilderness => Wilderness(world, settings, data, session, stageProgress, cancel),
                GeneratorStage.Spawns => Spawns(world, settings, data, session),
                _ => throw new ArgumentOutOfRangeException(nameof(stages), stage, null),
            };
            progress?.Report((stage, 1));
            var report = new StageReport(stage, summary, DateTime.UtcNow - started);
            reports.Add(report);
            finished?.Invoke(report);
        }
        return reports;
    }

    /// <summary>A stage's name as the Generate panel shows it, in lower case.</summary>
    public static string Name(GeneratorStage stage) => stage switch
    {
        GeneratorStage.TownSites => "towns",
        _ => stage.ToString().ToLowerInvariant(),
    };

    private static string Terrain(World world, GeneratorSettings settings, GeneratorData data, EditSession session,
        IProgress<double>? progress, CancellationToken cancel)
    {
        var options = settings.Terrain.Clone();
        var result = TerrainStage.Run(world, options, data.Stamps, data.Mixer, settings.PaintedTerrain, area: null, previousMask: null,
            session, progress, cancel);
        settings.TerrainPlan = result.Plan;
        settings.Done.Add(GeneratorStage.Terrain);
        int Count(TerrainType t) => result.Plan.Count(p => p == t);
        return $"{result.Stamps:N0} stamps; {Count(TerrainType.Plains)} plains, {Count(TerrainType.Hills)} hills and "
               + $"{Count(TerrainType.Mountains)} mountain tiles (seed {options.Seed})";
    }

    private static string Towns(World world, GeneratorSettings settings, GeneratorData data, EditSession session, CancellationToken cancel)
    {
        var options = settings.Towns.Clone();
        var cells = TownCells.Build(world, data.Biomes, data.Prefabs, settings.TerrainPlan, options.CrossBorders);
        var keep = settings.Sites.Where(s => s.Built || s.Pinned).ToList();
        var planned = TownSiteStage.Plan(cells, data.Mixer, options, keep, cancel);
        settings.Sites = planned.Sites;
        TownSiteStage.MarkBuilt(world, data.Prefabs, settings.Sites);
        var towns = TownSiteStage.PlanTowns(world, data.Prefabs, data.Mixer, data.Biomes, cells,
            settings.Sites.Where(s => !s.Built).ToList(), cancel, options.Traders);
        TownSiteStage.Build(world, data.Prefabs, towns, session);
        settings.Done.Add(GeneratorStage.TownSites);
        int pois = towns.Sum(t => t.Plan.PoiCount), traders = towns.Sum(t => t.Plan.TraderCount);
        return $"{towns.Count} towns, {pois:N0} POIs, {traders} traders (seed {options.Seed})"
               + (planned.Shortfalls.Count > 0 ? ". No room for all of: " + string.Join("; ", planned.Shortfalls) : "");
    }

    private static string Features(World world, GeneratorSettings settings, GeneratorData data, EditSession session,
        IProgress<double>? progress, CancellationToken cancel)
    {
        var options = settings.Features.Clone();
        var result = FeatureStage.Run(world, options, settings.PlacedFeatures, data.Stamps, data.Mixer, data.Prefabs, settings.Sites.ToList(),
            settings.TerrainPlan, settings.Terrain.BaseHeight, session, progress, cancel);
        settings.Done.Add(GeneratorStage.Features);
        // A river is one chain of stamps; count each chain once.
        var placed = result.Added.Where(f => f.Chain == 0 || f.Kind == FeatureKind.River)
            .GroupBy(f => f.Chain != 0 ? (object)f.Chain : f).Select(c => c.First().Kind)
            .GroupBy(k => k).OrderBy(k => k.Key).Select(k => $"{k.Count()} {FeatureStage.Plural(k.Key)}").ToList();
        return (placed.Count == 0 ? "nothing placed" : string.Join(", ", placed)) + $" (seed {options.Seed})"
               + (result.Shortfalls.Count > 0 ? ". " + string.Join(" ", result.Shortfalls) : "");
    }

    private static string Highways(World world, GeneratorSettings settings, GeneratorData data, EditSession session,
        IProgress<double>? progress, CancellationToken cancel)
    {
        var options = settings.Highways.Clone();
        var result = HighwayStage.Run(world, options, settings.PlacedRoads, data.Prefabs, data.Mixer, settings.Sites.ToList(), session,
            progress, cancel);
        if (result.Added.Count > 0) settings.Done.Add(GeneratorStage.Highways);
        var parts = new List<string>();
        void Part(HighwayKind kind, string one, string many)
        {
            int n = result.Count(kind);
            if (n > 0) parts.Add($"{n} {(n == 1 ? one : many)}");
        }
        Part(HighwayKind.Highway, "highway", "highways");
        Part(HighwayKind.Ring, "ring road", "ring roads");
        Part(HighwayKind.Country, "country road", "country roads");
        Part(HighwayKind.JoinUp, "join-up road", "join-up roads");
        Part(HighwayKind.Bridge, "bridge", "bridges");
        return (parts.Count == 0 ? "no roads" : string.Join(", ", parts) + $", {result.Kilometres:0} km") + $" (seed {options.Seed})"
               + (result.Notes.Count > 0 ? ". " + string.Join(" ", result.Notes) : "");
    }

    private static string Wilderness(World world, GeneratorSettings settings, GeneratorData data, EditSession session,
        IProgress<double>? progress, CancellationToken cancel)
    {
        var options = settings.Wilderness.Clone();
        var result = WildernessStage.Run(world, options, settings.PlacedWilderness, data.Prefabs, data.Mixer, data.Biomes,
            settings.Sites.ToList(), session, progress, cancel);
        if (settings.PlacedWilderness.Count > 0) settings.Done.Add(GeneratorStage.Wilderness);
        return $"{result.Added.Count} POIs, " + (options.Roads ? $"{result.Roads} gravel roads, {result.RoadKilometres:0} km" : "no roads")
               + $" (seed {options.Seed})" + (result.Notes.Count > 0 ? ". " + string.Join(" ", result.Notes) : "");
    }

    private static string Spawns(World world, GeneratorSettings settings, GeneratorData data, EditSession session)
    {
        var options = settings.Spawns.Clone();
        var result = SpawnStage.Run(world, options, settings.PlacedSpawns, data.Prefabs, data.Biomes, settings.Sites.ToList(),
            settings.PlacedWilderness.ToList(), session);
        if (settings.PlacedSpawns.Count > 0) settings.Done.Add(GeneratorStage.Spawns);
        return $"{result.Added} spawns ({result.ByRoad} by gravel roads, {result.TopUp} topping up), {world.Spawns.Count} in all, "
               + $"{result.Traders} traders (seed {options.Seed})" + (result.Notes.Count > 0 ? ". " + string.Join(" ", result.Notes) : "");
    }

    private sealed class StageProgress(GeneratorStage stage, IProgress<(GeneratorStage, double)> target) : IProgress<double>
    {
        public void Report(double value) => target.Report((stage, value));
    }
}
