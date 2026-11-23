using System.Runtime.ExceptionServices;
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

        var generator = new GeneratorStages(world, settings, data, addOnly: true);
        var reports = new List<StageReport>();
        foreach (var stage in run)
        {
            cancel.ThrowIfCancellationRequested();
            progress?.Report((stage, 0));
            var stageProgress = progress is null ? null : new StageProgress(stage, progress);
            var started = DateTime.UtcNow;
            var edits = new EditRunner(new UndoStack());
            edits.Failed += (_, failure) => ExceptionDispatchInfo.Throw(failure.Error);
            string summary = "";
            edits.Run(world, Name(stage), session => summary = stage switch
            {
                GeneratorStage.Terrain => Terrain(generator.Terrain(session, progress: stageProgress, cancel: cancel).Result.Terrain, settings.Terrain.Seed),
                GeneratorStage.TownSites => Towns(generator.Towns(session, cancel: cancel), settings.Towns.Seed),
                GeneratorStage.Features => Features(generator.Features(session, stageProgress, cancel).Result, settings.Features.Seed),
                GeneratorStage.Highways => Highways(generator.Highways(session, stageProgress, cancel).Result, settings.Highways.Seed),
                GeneratorStage.Wilderness => Wilderness(generator.Wilderness(session, stageProgress, cancel).Result, settings.Wilderness),
                GeneratorStage.Spawns => Spawns(generator.Spawns(session).Result, settings.Spawns.Seed, world.Spawns.Count),
                _ => throw new ArgumentOutOfRangeException(nameof(stages), stage, null),
            });
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

    private static string Terrain(TerrainResult result, int seed)
    {
        int Count(TerrainType t) => result.Plan.Count(p => p == t);
        return $"{result.Stamps:N0} stamps; {Count(TerrainType.Plains)} plains, {Count(TerrainType.Hills)} hills and "
               + $"{Count(TerrainType.Mountains)} mountain tiles (seed {seed})";
    }

    private static string Towns(StageRun<TownBuildResult> run, int seed)
    {
        var towns = run.Result.Towns;
        int pois = towns.Sum(t => t.Plan.PoiCount), traders = towns.Sum(t => t.Plan.TraderCount);
        return $"{towns.Count} towns, {pois:N0} POIs, {traders} traders (seed {seed})"
               + (run.Notes.Count > 0 ? ". No room for all of: " + string.Join("; ", run.Notes) : "");
    }

    private static string Features(FeatureResult result, int seed)
    {
        // A river is one chain of stamps; count each chain once.
        var placed = result.Added.Where(f => f.Chain == 0 || f.Kind == FeatureKind.River)
            .GroupBy(f => f.Chain != 0 ? (object)f.Chain : f).Select(c => c.First().Kind)
            .GroupBy(k => k).OrderBy(k => k.Key).Select(k => $"{k.Count()} {FeatureStage.Plural(k.Key)}").ToList();
        return (placed.Count == 0 ? "nothing placed" : string.Join(", ", placed)) + $" (seed {seed})"
               + (result.Shortfalls.Count > 0 ? ". " + string.Join(" ", result.Shortfalls) : "");
    }

    private static string Highways(HighwayResult result, int seed)
    {
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
        return (parts.Count == 0 ? "no roads" : string.Join(", ", parts) + $", {result.Kilometres:0} km") + $" (seed {seed})"
               + (result.Notes.Count > 0 ? ". " + string.Join(" ", result.Notes) : "");
    }

    private static string Wilderness(WildernessResult result, WildernessOptions options)
    {
        return $"{result.Added.Count} POIs, " + (options.Roads ? $"{result.Roads} gravel roads, {result.RoadKilometres:0} km" : "no roads")
               + $" (seed {options.Seed})" + (result.Notes.Count > 0 ? ". " + string.Join(" ", result.Notes) : "");
    }

    private static string Spawns(SpawnResult result, int seed, int total)
    {
        return $"{result.Added} spawns ({result.ByRoad} by gravel roads, {result.TopUp} topping up), {total} in all, "
               + $"{result.Traders} traders (seed {seed})" + (result.Notes.Count > 0 ? ". " + string.Join(" ", result.Notes) : "");
    }

    private sealed class StageProgress(GeneratorStage stage, IProgress<(GeneratorStage, double)> target) : IProgress<double>
    {
        public void Report(double value) => target.Report((stage, value));
    }
}
