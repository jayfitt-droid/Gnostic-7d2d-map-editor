using MapEditor.Core.Edit;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Generate;

/// <summary>How many generated items came out, and what happened to the Bridge tool's bridges.</summary>
public sealed record StageChanges(
    int Towns = 0, int Features = 0, int Highways = 0, int Wilderness = 0, int Spawns = 0,
    int BridgesRebuilt = 0, int BridgesDropped = 0);

/// <summary>The stage-specific result and the changes made by the whole edit.</summary>
public sealed record StageRun<T>(T Result, StageChanges Changes, int Added, IReadOnlyList<string> Notes);

/// <summary>The site plan and the towns planned on it.</summary>
public sealed record TownPlanResult(TownSiteResult SitePlan, List<PlannedTown> Towns);

/// <summary>The towns built and the number of feature stamps directly in their way.</summary>
public sealed record TownBuildResult(List<PlannedTown> Towns, int FeaturesInTheWay);

/// <summary>The new ground and its effect on planned town sites.</summary>
public sealed record TerrainRunResult(TerrainResult Terrain, int SitesCleared, int SitesUnusable);

/// <summary>
/// Runs one Generate stage inside a caller's edit, including the take-downs it needs and the rebuilding of tool bridges.
/// The caller supplies the session and keeps other edits and saves off the world until it finishes.
/// </summary>
public sealed class GeneratorStages(World world, GeneratorSettings settings, GeneratorData data,
    List<PlacedBridge>? bridges = null, bool bridgeWrecks = true)
{
    private readonly bool _addOnly;

    // The CLI refuses completed stages before running. Keep its existing treatment of saved plans and placements.
    internal GeneratorStages(World world, GeneratorSettings settings, GeneratorData data, bool addOnly)
        : this(world, settings, data) => _addOnly = addOnly;

    private GridRect WholeWorld => new(0, 0, world.Size, world.Size);

    /// <summary>Generates terrain, in tiles covering the whole map or a selected area.</summary>
    public StageRun<TerrainRunResult> Terrain(EditSession session, TerrainOptions? options = null,
        GridRect? area = null, BiomeMaskRecord? previousMask = null, BiomeLayoutSettings? replan = null,
        bool buildingWorld = false, IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        options ??= settings.Terrain.Clone();
        GeneratorEditState.Touch(session, settings);
        var (taken, changes) = TakeDownForRun(GeneratorStage.Highways, session);
        GridRect? areaMetres = area?.Scale(TerrainStage.TileSize);
        int features = _addOnly ? 0 : FeatureStage.TakeDown(world, settings.PlacedFeatures, areaMetres, data.Stamps, session);
        int towns = _addOnly ? 0 : TakeDownTowns(settings.Sites, areaMetres, session);
        var result = TerrainStage.Run(world, options, data.Stamps, data.Mixer, _addOnly ? settings.PaintedTerrain : null,
            area, previousMask, session, progress, cancel, replan);
        changes = RestoreBridges(taken, session, changes with { Features = features, Towns = towns });

        // An area run keeps the old plan on tiles it did not generate.
        if (area is { } selected && settings.TerrainPlan is { } last && last.Length == result.Plan.Length)
        {
            int tiles = world.Size / TerrainStage.TileSize;
            for (int i = 0; i < last.Length; i++)
                if (!selected.Contains(i % tiles, i / tiles)) result.Plan[i] = last[i];
        }

        int cleared = 0, unusable = 0;
        if (!_addOnly && settings.Sites.Count > 0)
        {
            var cells = TownCells.Build(world, data.Biomes, data.Prefabs, result.Plan, settings.Towns.CrossBorders);
            var kept = settings.Sites.ToList();
            (cleared, unusable) = TownSiteStage.DropForNewGround(kept, cells, area is null);
            if (kept.Count != settings.Sites.Count) ReplaceSites(kept, session);
        }
        settings.TerrainPlan = result.Plan;
        settings.Done.Add(GeneratorStage.Terrain);
        if (buildingWorld) settings.Done.RemoveWhere(stage => stage != GeneratorStage.Terrain);
        if (features > 0 && !settings.PlacedFeatures.Any(f => f.Drawn)) settings.Done.Remove(GeneratorStage.Features);
        if (towns + cleared + unusable > 0 && !settings.Sites.Any(s => s.Built)) settings.Done.Remove(GeneratorStage.TownSites);
        if (result.BiomesPlanned && settings.Biomes is { } planned) planned.PlannedFor = options.Shape.Clone();
        return new(new(result, cleared, unusable), changes, result.Stamps, []);
    }

    /// <summary>Picks new sites around the ones kept and plans their towns without editing the world.</summary>
    public TownPlanResult PlanTownSites(TownCells cells, TownSiteOptions options, IReadOnlyList<TownSite> keep)
    {
        var plan = TownSiteStage.Plan(cells, data.Mixer, options, keep);
        return new(plan, PlanTowns(cells, plan.Sites, options.Traders));
    }

    /// <summary>Saves the planned sites and their seed as an edit without changing the ground.</summary>
    public void ApplyTownPlan(EditSession session, TownSiteResult plan, int seed)
    {
        GeneratorEditState.Touch(session, settings);
        settings.Towns.Seed = seed;
        ReplaceSites(plan.Sites, session);
        TownSiteStage.MarkBuilt(world, data.Prefabs, settings.Sites);
    }

    /// <summary>Drops the planned sites that aren't built yet as an edit. Built sites stay.</summary>
    public void ClearTownPlan(EditSession session)
    {
        GeneratorEditState.Touch(session, settings);
        ReplaceSites([.. settings.Sites.Where(s => s.Built)], session);
    }

    /// <summary>Plans a preview on the given sites against the current world.</summary>
    public List<PlannedTown> PlanTowns(TownCells cells, IReadOnlyList<TownSite> sites, TraderMode traders,
        CancellationToken cancel = default) =>
        TownSiteStage.PlanTowns(world, data.Prefabs, data.Mixer, data.Biomes, cells, sites, cancel, traders);

    /// <summary>Takes unpinned built towns down and replans, keeping pinned sites.</summary>
    public StageRun<TownPlanResult> ReplanTowns(EditSession session)
    {
        GeneratorEditState.Touch(session, settings);
        var options = settings.Towns.Clone();
        var (taken, changes) = TakeDownForRun(GeneratorStage.Highways, session);
        int towns = TakeDownTowns(settings.Sites.Where(s => !s.Pinned), null, session);
        changes = RestoreBridges(taken, session, changes with { Towns = towns });
        // Plan on the ground as it is now, with the bridges back and without the towns that came down.
        var cells = TownCells.Build(world, data.Biomes, data.Prefabs, settings.TerrainPlan, options.CrossBorders);
        var result = PlanTownSites(cells, options, settings.Sites.Where(s => s.Pinned).ToList());
        ReplaceSites(result.SitePlan.Sites, session);
        TownSiteStage.MarkBuilt(world, data.Prefabs, settings.Sites);
        if (!settings.Sites.Any(s => s.Built)) settings.Done.Remove(GeneratorStage.TownSites);
        return new(result, changes, result.Towns.Count, result.SitePlan.Shortfalls);
    }

    /// <summary>Builds the editor's planned towns, using the town cells from before the take-down.</summary>
    public StageRun<TownBuildResult> Towns(EditSession session, TownCells? cells = null, CancellationToken cancel = default)
    {
        var options = settings.Towns.Clone();
        GeneratorEditState.Touch(session, settings);
        cells ??= TownCells.Build(world, data.Biomes, data.Prefabs, settings.TerrainPlan, options.CrossBorders);
        List<string> notes = [];
        if (_addOnly)
        {
            var keep = settings.Sites.Where(s => s.Built || s.Pinned).ToList();
            var plan = TownSiteStage.Plan(cells, data.Mixer, options, keep, cancel);
            ReplaceSites(plan.Sites, session);
            TownSiteStage.MarkBuilt(world, data.Prefabs, settings.Sites);
            notes = plan.Shortfalls;
        }
        var sites = settings.Sites.Where(s => !s.Built).ToList();
        var (taken, changes) = TakeDownForRun(GeneratorStage.Highways, session);
        // Planning can change each site's reserved cells; take the snapshot before it does.
        if (sites.Count > 0) session.TouchList(settings.Sites, s => s.Clone(), GridRect.Empty);
        var towns = PlanTowns(cells, sites, options.Traders, cancel);
        int featuresInTheWay = 0;
        if (!_addOnly)
        {
            // Features in the way come out first, so the towns get flat ground.
            var townCells = sites.SelectMany(s => s.AllCells).ToHashSet();
            var inTheWay = settings.PlacedFeatures.Where(f => f.Drawn && f.Cells.Any(c => townCells.Contains((c[0], c[1])))).ToList();
            featuresInTheWay = inTheWay.Count;
            changes = changes with { Features = RemoveFeatures(session, inTheWay) };
        }
        TownSiteStage.Build(world, data.Prefabs, towns, session);
        changes = RestoreBridges(taken, session, changes);
        settings.Done.Add(GeneratorStage.TownSites);
        return new(new(towns, featuresInTheWay), changes, towns.Count, notes);
    }

    /// <summary>Presses in the features again, keeping pinned ones.</summary>
    public StageRun<FeatureResult> Features(EditSession session, IProgress<double>? progress = null,
        CancellationToken cancel = default)
    {
        GeneratorEditState.Touch(session, settings);
        var (taken, changes) = TakeDownForRun(GeneratorStage.Highways, session);
        var result = FeatureStage.Run(world, settings.Features.Clone(), settings.PlacedFeatures, data.Stamps, data.Mixer,
            data.Prefabs, settings.Sites, settings.TerrainPlan, settings.Terrain.BaseHeight, session, progress, cancel);
        changes = RestoreBridges(taken, session, changes with { Features = result.Removed });
        settings.Done.Add(GeneratorStage.Features);
        return new(result, changes, result.Added.Count, result.Shortfalls);
    }

    /// <summary>Lays the highways again, keeping clear of the tool bridges' decks and ramps.</summary>
    public StageRun<HighwayResult> Highways(EditSession session, IProgress<double>? progress = null,
        CancellationToken cancel = default)
    {
        GeneratorEditState.Touch(session, settings);
        var (taken, changes) = TakeDownForRun(GeneratorStage.Wilderness, session);
        var result = HighwayStage.Run(world, settings.Highways.Clone(), settings.PlacedRoads, data.Prefabs, data.Mixer,
            settings.Sites, session, progress, cancel, BridgeAreas(taken));
        changes = RestoreBridges(taken, session, changes with { Highways = result.Removed });
        if (result.Added.Count > 0) settings.Done.Add(GeneratorStage.Highways);
        return new(result, changes, result.Added.Count, result.Notes);
    }

    /// <summary>Places wilderness POIs and roads again, keeping clear of the tool bridges.</summary>
    public StageRun<WildernessResult> Wilderness(EditSession session, IProgress<double>? progress = null,
        CancellationToken cancel = default)
    {
        GeneratorEditState.Touch(session, settings);
        var (taken, changes) = TakeDownForRun(GeneratorStage.Spawns, session);
        int drawn = settings.PlacedWilderness.Count(p => p.Drawn);
        var result = WildernessStage.Run(world, settings.Wilderness.Clone(), settings.PlacedWilderness, data.Prefabs, data.Mixer,
            data.Biomes, settings.Sites, session, progress, cancel, BridgeAreas(taken));
        changes = RestoreBridges(taken, session, changes with { Wilderness = drawn });
        if (settings.PlacedWilderness.Count > 0) settings.Done.Add(GeneratorStage.Wilderness);
        return new(result, changes, result.Added.Count, result.Notes);
    }

    /// <summary>Places spawn points again, keeping hand-placed ones.</summary>
    public StageRun<SpawnResult> Spawns(EditSession session)
    {
        GeneratorEditState.Touch(session, settings);
        var result = SpawnStage.Run(world, settings.Spawns.Clone(), settings.PlacedSpawns, data.Prefabs, data.Biomes,
            settings.Sites, settings.PlacedWilderness, session);
        if (settings.PlacedSpawns.Count > 0) settings.Done.Add(GeneratorStage.Spawns);
        return new(result, new StageChanges(Spawns: result.Removed), result.Added, result.Notes);
    }

    /// <summary>Removes a stage's output as the panel's Remove button does, including the dependent stages.</summary>
    public StageChanges Remove(GeneratorStage stage, EditSession session)
    {
        GeneratorEditState.Touch(session, settings);
        // The Remove buttons leave tool bridges where they are. Removing features is selective or independent
        // of the later stages, while highways and wilderness can invalidate what follows.
        var changes = stage switch
        {
            GeneratorStage.Features => new StageChanges(Features: RemoveFeatures(session, settings.PlacedFeatures)),
            GeneratorStage.Highways => TakeDownFrom(stage, session),
            GeneratorStage.Wilderness => TakeDownFrom(GeneratorStage.Spawns, session) with
            {
                Wilderness = RemoveWilderness(session),
            },
            GeneratorStage.Spawns => TakeDownFrom(stage, session),
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null),
        };
        settings.Done.Remove(stage);
        return changes;
    }

    /// <summary>Drops the selected features, including any river chains they belong to.</summary>
    public void DropFeatures(EditSession session, IReadOnlyList<Feature> which) => RemoveFeatures(session, which);

    private int RemoveFeatures(EditSession session, IReadOnlyList<Feature> which)
    {
        if (which.Count == 0) return 0;
        int drawn = settings.PlacedFeatures.Count(f => f.Drawn);
        FeatureStage.Remove(world, settings.PlacedFeatures, which, data.Stamps, session);
        return drawn - settings.PlacedFeatures.Count(f => f.Drawn);
    }

    private int RemoveWilderness(EditSession session)
    {
        int drawn = settings.PlacedWilderness.Count(p => p.Drawn);
        WildernessStage.Remove(world, settings.PlacedWilderness, settings.PlacedWilderness, session);
        return drawn;
    }

    private StageChanges TakeDownFrom(GeneratorStage first, EditSession session)
    {
        // Newest first, so each puts back the ground it found. Only drawn output changes the panel's Done marks.
        int spawns = SpawnStage.TakeDown(world, settings.PlacedSpawns, session);
        int wilderness = first <= GeneratorStage.Wilderness ? WildernessStage.TakeDown(world, settings.PlacedWilderness, session) : 0;
        int highways = first <= GeneratorStage.Highways && settings.PlacedRoads.Count > 0
            ? HighwayStage.TakeDown(world, settings.PlacedRoads, session) : 0;
        if (spawns > 0) settings.Done.Remove(GeneratorStage.Spawns);
        if (wilderness > 0) settings.Done.Remove(GeneratorStage.Wilderness);
        if (highways > 0) settings.Done.Remove(GeneratorStage.Highways);
        return new(Highways: highways, Wilderness: wilderness, Spawns: spawns);
    }

    private int TakeDownTowns(IEnumerable<TownSite> sites, GridRect? area, EditSession session)
    {
        var built = sites.Where(s => s.Built).ToList();
        if (built.Count == 0) return 0;
        session.TouchList(settings.Sites, s => s.Clone(), GridRect.Empty);
        return TownSiteStage.TakeDown(world, data.Prefabs, built, area, session);
    }

    private void ReplaceSites(List<TownSite> sites, EditSession session)
    {
        session.TouchList(settings.Sites, s => s.Clone(), WholeWorld);
        settings.Sites.Clear();
        settings.Sites.AddRange(sites);
    }

    private (List<PlacedBridge> Taken, StageChanges Changes) TakeDownForRun(GeneratorStage first, EditSession session)
    {
        var taken = bridges is null ? [] : BridgeTool.TakeDownAll(world, bridges, session);
        return (taken, _addOnly ? new StageChanges() : TakeDownFrom(first, session));
    }

    private GridRect[]? BridgeAreas(List<PlacedBridge> taken) =>
        taken.Count == 0 ? null : [.. taken.Select(b => BridgeTool.KeepClearArea(b, world.Size))];

    private StageChanges RestoreBridges(List<PlacedBridge> taken, EditSession session, StageChanges changes)
    {
        if (taken.Count == 0 || bridges is null) return changes;
        int dropped = BridgeTool.RebuildAll(world, BridgeKit.From(data.Prefabs), bridges, taken, session, bridgeWrecks);
        return changes with { BridgesRebuilt = taken.Count - dropped, BridgesDropped = dropped };
    }
}
