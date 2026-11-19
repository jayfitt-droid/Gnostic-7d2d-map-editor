using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Generate;
using MapEditor.Core.Ops;

namespace MapEditor.App;

/// <summary>
/// The Generate panel. Its fields read and write <see cref="Settings"/>, the open world's generator settings, which
/// the project stores.
/// </summary>
public sealed partial class GenerateViewModel : ObservableObject
{
    public GenerateViewModel()
    {
        BiomeRows = [.. Enum.GetValues<RwgBiome>().Select(b => new BiomeWeightRow(this, b))];
    }

    private GeneratorSettings _settings = new();

    public GeneratorSettings Settings => _settings;

    /// <summary>
    /// The user changed a setting the project saves (a seed, a weight, a row, a pin), so the window can mark the
    /// project unsaved. Not raised by <see cref="Load"/> or <see cref="Refresh"/>.
    /// </summary>
    public event EventHandler? SettingsChanged;

    internal void NoteEdit() => SettingsChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Raises the change for a saved setting's property and <see cref="SettingsChanged"/>.</summary>
    private void Changed([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        OnPropertyChanged(name);
        NoteEdit();
    }

    /// <summary>Shows another world's settings (or a fresh world's). Forgets the last run's tile plan and biome mask.</summary>
    public void Load(GeneratorSettings settings, int worldSize)
    {
        _settings = settings;
        WorldSize = worldSize;
        // Tile types painted by hand are gone (#115): the Terrain tools change the ground instead.
        settings.PaintedTerrain = null;
        LastPlan = settings.TerrainPlan;
        LastMask = null;
        Area = null;
        WholeMap = true;
        Planned = [];
        SelectedFeature = null;
        RefreshTownRows();
        // Another world: the box starts from whether its biomes fit its land.
        ReplanBiomes = settings.BiomesOutOfDate;
        OnPropertyChanged(nameof(CanReplanBiomes));
        Refresh();
    }

    /// <summary>Raises a change for every field, after the settings changed underneath.</summary>
    public void Refresh()
    {
        OnPropertyChanged(string.Empty);
        foreach (var row in BiomeRows) row.Refresh();
        foreach (var row in TownRows) row.Refresh();
        foreach (var row in FeatureRows) row.Refresh();
    }

    public int WorldSize { get; private set; }

    /// <summary>256 m tiles along each edge.</summary>
    public int Tiles => WorldSize / TerrainStage.TileSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGenerate), nameof(CanEdit))]
    private bool _hasWorld;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGenerate), nameof(CanEdit))]
    private bool _isRunning;

    [ObservableProperty] private double _progress;

    /// <summary>What the terrain stage did last, or that it hasn't run.</summary>
    [ObservableProperty] private string _terrainStatus = "Not run yet.";

    public bool CanEdit => HasWorld && !IsRunning;
    public bool CanGenerate => CanEdit && (WholeMap || Area is not null) && CanGenerateLandShape;
    public bool CanGenerateLandShape => !IsDrawn || Shape.Drawing is not null;

    // ---- seeds ---------------------------------------------------------------

    public int WorldSeed
    {
        get => _settings.WorldSeed;
        set
        {
            if (value == _settings.WorldSeed) return;
            _settings.SetWorldSeed(value);
            Changed();
            OnPropertyChanged(nameof(TerrainSeed));
            OnPropertyChanged(nameof(CoastSeed));
            ShapeChanged();
        }
    }

    public int TerrainSeed
    {
        get => _settings.Terrain.Seed;
        set
        {
            _settings.Terrain.Seed = value;
            Changed();
        }
    }

    // ---- terrain weights -----------------------------------------------------

    public int Plains
    {
        get => _settings.Terrain.Weights.Plains;
        set => SetWeights(_settings.Terrain.Weights with { Plains = Math.Clamp(value, 0, 99) });
    }

    public int Hills
    {
        get => _settings.Terrain.Weights.Hills;
        set => SetWeights(_settings.Terrain.Weights with { Hills = Math.Clamp(value, 0, 99) });
    }

    public int Mountains
    {
        get => _settings.Terrain.Weights.Mountains;
        set => SetWeights(_settings.Terrain.Weights with { Mountains = Math.Clamp(value, 0, 99) });
    }

    private void SetWeights(TerrainWeights weights)
    {
        _settings.Terrain.Weights = weights;
        OnPropertyChanged(nameof(Plains));
        OnPropertyChanged(nameof(Hills));
        OnPropertyChanged(nameof(Mountains));
        // Biomes without their own weights follow these.
        foreach (var row in BiomeRows) row.Refresh();
        NoteEdit();
    }

    /// <summary>One row per RWG biome, each with its own weights when ticked.</summary>
    public IReadOnlyList<BiomeWeightRow> BiomeRows { get; }

    // ---- edges ---------------------------------------------------------------

    public IReadOnlyList<EdgeStyle> EdgeStyles { get; } = Enum.GetValues<EdgeStyle>();

    public EdgeStyle EdgeNorth { get => Edge(0); set => SetEdge(0, value); }
    public EdgeStyle EdgeEast { get => Edge(1); set => SetEdge(1, value); }
    public EdgeStyle EdgeSouth { get => Edge(2); set => SetEdge(2, value); }
    public EdgeStyle EdgeWest { get => Edge(3); set => SetEdge(3, value); }

    private EdgeStyle Edge(int i) => _settings.Terrain.Edges.Length > i ? _settings.Terrain.Edges[i] : EdgeStyle.Land;

    private void SetEdge(int i, EdgeStyle value)
    {
        if (_settings.Terrain.Edges.Length < 4) _settings.Terrain.Edges = [EdgeStyle.Land, EdgeStyle.Land, EdgeStyle.Land, EdgeStyle.Land];
        _settings.Terrain.Edges[i] = value;
        Changed(i switch { 0 => nameof(EdgeNorth), 1 => nameof(EdgeEast), 2 => nameof(EdgeSouth), _ => nameof(EdgeWest) });
    }

    // ---- land shape ----------------------------------------------------------

    public IReadOnlyList<LandShapeChoice> ShapeKinds => LandShapeChoice.All;

    public IReadOnlyList<MapEdge> MapEdges { get; } = Enum.GetValues<MapEdge>();

    private LandShape Shape => _settings.Terrain.Shape;

    public LandShapeKind ShapeKind
    {
        get => Shape.Kind;
        set
        {
            if (value == Shape.Kind) return;
            Shape.Kind = value;
            Changed();
            OnPropertyChanged(nameof(HasCoastSettings));
            OnPropertyChanged(nameof(IsDrawn));
            OnPropertyChanged(nameof(CanGenerateLandShape));
            OnPropertyChanged(nameof(CanGenerate));
            OnPropertyChanged(nameof(IsEdges));
            OnPropertyChanged(nameof(IsArchipelago));
            OnPropertyChanged(nameof(IsPeninsula));
            ShapeChanged();
        }
    }

    public bool HasCoastSettings => Shape.IsShaped && !IsDrawn;
    public bool IsDrawn => Shape.Kind == LandShapeKind.Drawn;
    public bool IsEdges => !Shape.IsShaped;
    public bool IsArchipelago => Shape.Kind == LandShapeKind.Archipelago;
    public bool IsPeninsula => Shape.Kind == LandShapeKind.Peninsula;

    public int CoastSeed
    {
        get => Shape.Seed;
        set => SetShape(() => Shape.Seed = value);
    }

    public int LandPercent
    {
        get => Shape.LandPercent;
        set => SetShape(() => Shape.LandPercent = Math.Clamp(value, LandShape.MinLandPercent, LandShape.MaxLandPercent));
    }

    public int Roughness
    {
        get => Shape.Roughness;
        set => SetShape(() => Shape.Roughness = Math.Clamp(value, 0, 100));
    }

    public int Islands
    {
        get => Shape.Islands;
        set => SetShape(() => Shape.Islands = Math.Clamp(value, LandShape.MinIslands, LandShape.MaxIslands));
    }

    public MapEdge PeninsulaEdge
    {
        get => Shape.PeninsulaEdge;
        set => SetShape(() => Shape.PeninsulaEdge = value);
    }

    private void SetShape(Action change, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        change();
        Changed(name);
        ShapeChanged();
    }

    /// <summary>The world came from New World with a generated biome layout, which can be planned again for the land.</summary>
    public bool CanReplanBiomes => _settings.Biomes is not null;

    /// <summary>
    /// Plan the biomes again for the land shape on each whole-map run. Ticked for the user when the shape changes since
    /// the biomes were planned; only the user unticks it, so it stays on across re-rolls.
    /// </summary>
    [ObservableProperty] private bool _replanBiomes;

    /// <summary>
    /// The seed the biome layout is planned with when <see cref="ReplanBiomes"/> is ticked: New World's at first. The
    /// same seed on the same land gives the same layout, so Re-roll picks a new one (#26).
    /// </summary>
    public int BiomeSeed
    {
        get => _settings.Biomes?.Seed ?? 0;
        set
        {
            if (_settings.Biomes is not { } biomes || biomes.Seed == value) return;
            biomes.Seed = value;
            Changed();
        }
    }

    /// <summary>
    /// Ticks <see cref="ReplanBiomes"/> when the shape no longer matches the one the biomes were planned for. It never
    /// unticks it: a run that planned them again leaves the user's choice for the next re-roll (issue 2).
    /// </summary>
    public void ShapeChanged()
    {
        // One biome per island needs an archipelago: off one, the layout goes back to the game's first.
        if (_settings.Biomes is { Layout: BiomeLayoutKind.PerIsland } biomes && Shape.Kind != LandShapeKind.Archipelago)
            biomes.Layout = BiomeLayoutKind.CenterForest;
        if (_settings.BiomesOutOfDate) ReplanBiomes = true;
        OnPropertyChanged(nameof(CanReplanBiomes));
        OnPropertyChanged(nameof(BiomeLayouts));
        OnPropertyChanged(nameof(BiomeLayout));
    }

    /// <summary>The layouts the biomes can be planned again with, for the land shape (#46).</summary>
    public IReadOnlyList<BiomeLayoutChoice> BiomeLayouts => BiomeLayoutChoice.For(Shape.Kind);

    /// <summary>The layout <see cref="ReplanBiomes"/> plans with: New World's at first.</summary>
    public BiomeLayoutChoice? BiomeLayout
    {
        get => _settings.Biomes is { } b ? BiomeLayouts.FirstOrDefault(l => l.Kind == b.Layout) : null;
        set
        {
            if (value is null || _settings.Biomes is not { } biomes || biomes.Layout == value.Kind) return;
            biomes.Layout = value.Kind;
            Changed();
        }
    }

    // ---- area ----------------------------------------------------------------

    /// <summary>Generate the whole map (true) or only <see cref="Area"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGenerate), nameof(IsArea))]
    private bool _wholeMap = true;

    public bool IsArea
    {
        get => !WholeMap;
        set => WholeMap = !value;
    }

    /// <summary>The tiles to generate when not the whole map, in 256 m tiles (z from the south).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGenerate), nameof(AreaText))]
    private GridRect? _area;

    public string AreaText => Area is { } a
        ? $"{(a.X1 - a.X0) * TerrainStage.TileSize} × {(a.Z1 - a.Z0) * TerrainStage.TileSize} m ({(a.X1 - a.X0) * (a.Z1 - a.Z0)} tiles)"
        : "No area yet: press Select area, then drag on the map.";

    // ---- tile types -----------------------------------------------------------

    /// <summary>The types the last terrain run gave each tile.</summary>
    public TerrainType[]? LastPlan { get; set; }

    /// <summary>The biome cells the last run's mountains painted, put back by the next run.</summary>
    public BiomeMaskRecord? LastMask { get; set; }

    // ---- town sites ----------------------------------------------------------

    public int TownSeed
    {
        get => _settings.Towns.Seed;
        set
        {
            _settings.Towns.Seed = value;
            Changed();
        }
    }

    /// <summary>The share of the map's width kept clear of towns at each edge, as a percentage.</summary>
    public int EdgeMarginPercent
    {
        get => (int)MathF.Round(_settings.Towns.EdgeMargin * 100);
        set
        {
            _settings.Towns.EdgeMargin = Math.Clamp(value, 0, 40) / 100f;
            Changed();
        }
    }

    /// <summary>Every town gets a trader, rather than RWG's 2 to 4 of each biome's own trader.</summary>
    public bool OneTraderPerTown
    {
        get => _settings.Towns.Traders == TraderMode.OnePerTown;
        set
        {
            _settings.Towns.Traders = value ? TraderMode.OnePerTown : TraderMode.RwgCaps;
            Changed();
        }
    }

    /// <summary>Towns may take cells with more than one biome, so they can straddle a border.</summary>
    public bool TownsCrossBorders
    {
        get => _settings.Towns.CrossBorders;
        set
        {
            _settings.Towns.CrossBorders = value;
            Changed();
        }
    }

    /// <summary>One row per township in the settings, for the panel's table.</summary>
    public ObservableCollection<TownRowViewModel> TownRows { get; } = [];

    /// <summary>Rebuilds the table after the rows were replaced (new world, preset, defaults).</summary>
    public void RefreshTownRows()
    {
        TownRows.Clear();
        foreach (var row in _settings.Towns.Rows)
        {
            var vm = new TownRowViewModel(row);
            vm.PropertyChanged += (_, _) => OnPropertyChanged(nameof(TownTotalText));
            vm.Edited += (_, _) => NoteEdit();
            TownRows.Add(vm);
        }
        OnPropertyChanged(nameof(TownTotalText));
    }

    /// <summary>Updates the town sites controls after applying a town preset.</summary>
    public void RefreshTownSettings()
    {
        RefreshTownRows();
        OnPropertyChanged(nameof(EdgeMarginPercent));
        OnPropertyChanged(nameof(OneTraderPerTown));
        OnPropertyChanged(nameof(TownsCrossBorders));
    }

    /// <summary>What the table adds up to: how many towns, and about how many street tiles.</summary>
    public string TownTotalText
    {
        get
        {
            int towns = TownRows.Sum(r => r.Count);
            int tiles = TownRows.Sum(r => r.Count * (r.MinTiles + r.MaxTiles) / 2);
            return towns == 0 ? "No towns asked for." : $"Asks for {towns} town{(towns == 1 ? "" : "s")}, about {tiles:N0} street tiles ({tiles * 0.0225:0.#} km² of town).";
        }
    }

    /// <summary>The towns planned and not yet built, as the map shows them.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlan), nameof(PlanText))]
    private IReadOnlyList<PlannedTown> _planned = [];

    public bool HasPlan => Planned.Count > 0;

    public string PlanText => Planned.Count == 0
        ? "No towns planned."
        : $"{Planned.Count} town{(Planned.Count == 1 ? "" : "s")} planned: {Planned.Sum(t => t.Plan.Tiles.Count)} street tiles, "
          + $"{Planned.Sum(t => t.Plan.PoiCount)} POIs, {Planned.Sum(t => t.Plan.TraderCount)} traders.";

    /// <summary>What the town sites stage did last.</summary>
    [ObservableProperty] private string _townStatus = "Not run yet.";

    // ---- features ------------------------------------------------------------

    public int FeatureSeed
    {
        get => _settings.Features.Seed;
        set
        {
            _settings.Features.Seed = value;
            Changed();
        }
    }

    public bool MountainLakes
    {
        get => _settings.Features.MountainLakes;
        set
        {
            _settings.Features.MountainLakes = value;
            Changed();
        }
    }

    public bool LongRivers
    {
        get => _settings.Features.LongRivers;
        set
        {
            _settings.Features.LongRivers = value;
            Changed();
        }
    }

    /// <summary>The most river stamps in one long river.</summary>
    public int RiverSegments
    {
        get => _settings.Features.RiverSegments;
        set
        {
            _settings.Features.RiverSegments = Math.Clamp(value, 1, 40);
            Changed();
        }
    }

    /// <summary>One row per feature kind, for the panel's table.</summary>
    public ObservableCollection<FeatureRowViewModel> FeatureRows { get; } = [];

    /// <summary>
    /// Rebuilds the feature table, filling any kind the settings lack from rwgmixer (once rwgmixer is read, the rows
    /// are kept in the settings so edits stick).
    /// </summary>
    public void RefreshFeatureRows(RwgMixer mixer)
    {
        if (mixer.Townships.Count > 0 || _settings.Features.Rows.Count > 0)
            _settings.Features.Rows = FeatureStage.RowsFor(_settings.Features, mixer);
        FeatureRows.Clear();
        foreach (var row in FeatureStage.RowsFor(_settings.Features, mixer))
        {
            var vm = new FeatureRowViewModel(row, mixer.FeatureRule(FeatureStage.Plural(row.Kind)).Counts);
            vm.Edited += (_, _) => NoteEdit();
            FeatureRows.Add(vm);
        }
    }

    public IReadOnlyList<FeatureKind> FeatureKinds { get; } = FeatureStage.Order;

    /// <summary>What a click on empty ground places with the Place features tool.</summary>
    [ObservableProperty] private FeatureKind _placeKind = FeatureKind.Lake;

    /// <summary>What the features stage did last.</summary>
    [ObservableProperty] private string _featureStatus = "Not run yet.";

    /// <summary>The feature picked on the map.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedFeature), nameof(SelectedFeatureText), nameof(SelectedFeaturePinned))]
    private Feature? _selectedFeature;

    public bool HasSelectedFeature => SelectedFeature is not null;

    public string SelectedFeatureText => SelectedFeature is { } f
        ? $"{FeatureRowViewModel.NameOf(f.Kind)}{(f.Chain != 0 ? $" (long river, {_settings.PlacedFeatures.Count(e => e.Chain == f.Chain)} stamps)" : "")}, "
          + $"{f.Size:0} m{(f.WaterLevel > 0 ? $", water at {f.WaterLevel} m" : ", dry")}{(f.Pinned ? ", pinned" : "")}"
        : "";

    public bool SelectedFeaturePinned
    {
        get => SelectedFeature?.Pinned ?? false;
        set
        {
            if (SelectedFeature is not { } f) return;
            // A long river is kept or dropped as a whole.
            foreach (var e in _settings.PlacedFeatures.Where(e => e == f || f.Chain != 0 && e.Chain == f.Chain)) e.Pinned = value;
            Changed();
            OnPropertyChanged(nameof(SelectedFeatureText));
        }
    }

    public int FeatureCount => _settings.PlacedFeatures.Count(f => f.Drawn);

    public void RefreshFeatures()
    {
        if (SelectedFeature is { } f && !_settings.PlacedFeatures.Contains(f)) SelectedFeature = null;
        OnPropertyChanged(nameof(FeatureCount));
        OnPropertyChanged(nameof(SelectedFeatureText));
        OnPropertyChanged(nameof(SelectedFeaturePinned));
    }

    // ---- highways ------------------------------------------------------------

    public int HighwaySeed
    {
        get => _settings.Highways.Seed;
        set
        {
            _settings.Highways.Seed = value;
            Changed();
        }
    }

    /// <summary>How much the seed bends routes, as a percentage of the route cost (0 for RWG's exact routes).</summary>
    public int WigglePercent
    {
        get => (int)MathF.Round(_settings.Highways.Wiggle * 100);
        set
        {
            _settings.Highways.Wiggle = Math.Clamp(value, 0, 200) / 100f;
            Changed();
        }
    }

    public int CausewayMax
    {
        get => _settings.Highways.CausewayMax;
        set
        {
            _settings.Highways.CausewayMax = Math.Clamp(value, 0, 1000);
            Changed();
        }
    }

    public bool JoinUp
    {
        get => _settings.Highways.JoinUp;
        set
        {
            _settings.Highways.JoinUp = value;
            Changed();
        }
    }

    public bool SecondRoad
    {
        get => _settings.Highways.SecondRoad;
        set
        {
            _settings.Highways.SecondRoad = value;
            Changed();
        }
    }

    public bool Bridges
    {
        get => _settings.Highways.Bridges;
        set
        {
            _settings.Highways.Bridges = value;
            Changed();
        }
    }

    public bool DeckWrecks
    {
        get => _settings.Highways.DeckWrecks;
        set
        {
            _settings.Highways.DeckWrecks = value;
            Changed();
        }
    }

    public bool BridgeShortcuts
    {
        get => _settings.Highways.BridgeShortcuts;
        set
        {
            _settings.Highways.BridgeShortcuts = value;
            Changed();
        }
    }

    public int BridgeMax
    {
        get => _settings.Highways.BridgeMax;
        set
        {
            _settings.Highways.BridgeMax = Math.Clamp(value, 20, 4000);
            Changed();
        }
    }

    /// <summary>What the highways stage did last.</summary>
    [ObservableProperty] private string _highwayStatus = "Not run yet.";

    public bool HasHighways => _settings.PlacedRoads.Count > 0;

    public void RefreshHighways() => OnPropertyChanged(nameof(HasHighways));

    // ---- wilderness POIs and gravel roads ------------------------------------

    public int WildernessSeed
    {
        get => _settings.Wilderness.Seed;
        set
        {
            _settings.Wilderness.Seed = value;
            Changed();
        }
    }

    public IReadOnlyList<string> WildernessAmounts { get; } = ["Few", "Default", "Many"];

    /// <summary>rwgmixer's count to start from: 0 Few, 1 Default, 2 Many.</summary>
    public int WildernessAmount
    {
        get => Math.Clamp(_settings.Wilderness.Amount, 0, 2);
        set
        {
            _settings.Wilderness.Amount = Math.Clamp(value, 0, 2);
            Changed();
            OnPropertyChanged(nameof(WildernessCountsText));
        }
    }

    /// <summary>rwgmixer's count, scaled, in percent.</summary>
    public int WildernessPercent
    {
        get => (int)MathF.Round(_settings.Wilderness.CountScale * 100);
        set
        {
            _settings.Wilderness.CountScale = Math.Clamp(value, 0, 500) / 100f;
            Changed();
            OnPropertyChanged(nameof(WildernessCountsText));
        }
    }

    /// <summary>The tier slider, -100 (easier) to 100 (harder).</summary>
    public double WildernessTier
    {
        get => Math.Round(_settings.Wilderness.TierBias * 100);
        set
        {
            _settings.Wilderness.TierBias = (float)Math.Clamp(Math.Round(value), -100, 100) / 100f;
            Changed();
            OnPropertyChanged(nameof(WildernessTierText));
        }
    }

    public string WildernessTierText => _settings.Wilderness.TierBias switch
    {
        0 => "The game's mix",
        < 0 => $"Easier ({-_settings.Wilderness.TierBias * 100:0}%)",
        _ => $"Harder ({_settings.Wilderness.TierBias * 100:0}%)",
    };

    public int WildernessGap
    {
        get => _settings.Wilderness.Gap;
        set
        {
            _settings.Wilderness.Gap = Math.Clamp(value, 0, 500);
            Changed();
        }
    }

    public bool WildernessRoads
    {
        get => _settings.Wilderness.Roads;
        set
        {
            _settings.Wilderness.Roads = value;
            Changed();
        }
    }

    public int WildernessMaxRoad
    {
        get => _settings.Wilderness.MaxRoad;
        set
        {
            _settings.Wilderness.MaxRoad = Math.Clamp(value, 50, 20000);
            Changed();
        }
    }

    public RwgMixer? Mixer { get; set; }

    /// <summary>How many POIs each biome gets with these settings.</summary>
    public string WildernessCountsText
    {
        get
        {
            if (Mixer is not { } mixer) return "";
            var counts = WildernessStage.Biomes.Select(b => (b, n: WildernessStage.CountFor(mixer, _settings.Wilderness, b))).ToList();
            return string.Join(", ", counts.Select(c => $"{BiomeLabel(c.b)} {c.n}")) + $": {counts.Sum(c => c.n)} in all.";
        }
    }

    private static string BiomeLabel(string biome) => GameNames.Current.Biome(biome);

    /// <summary>What the wilderness stage did last.</summary>
    [ObservableProperty] private string _wildernessStatus = "Not run yet.";

    public bool HasWilderness => _settings.PlacedWilderness.Count > 0;

    public void RefreshWilderness()
    {
        OnPropertyChanged(nameof(HasWilderness));
        OnPropertyChanged(nameof(SpawnsWithoutRoads));
        OnPropertyChanged(nameof(WildernessCountsText));
    }

    // ---- spawns ----------------------------------------------------------------

    public int SpawnSeed
    {
        get => _settings.Spawns.Seed;
        set
        {
            _settings.Spawns.Seed = value;
            Changed();
        }
    }

    private bool HasSpawnBiome(string tag) => _settings.Spawns.Biomes.Contains(tag, StringComparer.OrdinalIgnoreCase);

    private void SetSpawnBiome(string tag, bool on, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        _settings.Spawns.Biomes.RemoveAll(b => string.Equals(b, tag, StringComparison.OrdinalIgnoreCase));
        if (on) _settings.Spawns.Biomes.Add(tag);
        Changed(name);
    }

    public bool SpawnForest { get => HasSpawnBiome("forest"); set => SetSpawnBiome("forest", value); }
    public bool SpawnBurntForest { get => HasSpawnBiome("burntforest"); set => SetSpawnBiome("burntforest", value); }
    public bool SpawnDesert { get => HasSpawnBiome("desert"); set => SetSpawnBiome("desert", value); }
    public bool SpawnSnow { get => HasSpawnBiome("snow"); set => SetSpawnBiome("snow", value); }
    public bool SpawnWasteland { get => HasSpawnBiome("wasteland"); set => SetSpawnBiome("wasteland", value); }

    /// <summary>The fewest spawns the world ends up with.</summary>
    public int SpawnMin
    {
        get => _settings.Spawns.Min;
        set
        {
            _settings.Spawns.Min = Math.Clamp(value, 0, 1000);
            Changed();
        }
    }

    /// <summary>The most spawns the world may have; 0 for no limit.</summary>
    public int SpawnMax
    {
        get => _settings.Spawns.Max;
        set
        {
            _settings.Spawns.Max = Math.Clamp(value, 0, 1000);
            Changed();
        }
    }

    public int SpawnTraderDistance
    {
        get => _settings.Spawns.TraderDistance;
        set
        {
            _settings.Spawns.TraderDistance = Math.Clamp(value, 0, 20000);
            Changed();
        }
    }

    /// <summary>Spawns by the trader the opening quest sends new players to, not by any trader of their biome.</summary>
    public bool SpawnNearStarterTrader
    {
        get => _settings.Spawns.NearStarterTrader;
        set
        {
            if (_settings.Spawns.NearStarterTrader == value) return;
            _settings.Spawns.NearStarterTrader = value;
            Changed();
        }
    }

    /// <summary>What the spawns stage did last.</summary>
    [ObservableProperty] private string _spawnStatus = "Not run yet.";

    public bool HasSpawns => _settings.PlacedSpawns.Count > 0;

    /// <summary>Stage 5 has placed nothing, so a spawns run has no gravel roads and only tops up to the minimum.</summary>
    public bool SpawnsWithoutRoads => !HasWilderness;

    public void RefreshSpawns() => OnPropertyChanged(nameof(HasSpawns));

    // ---- stage cards ----------------------------------------------------------

    /// <summary>The stage card that is open (1 to 6); 0 when all are folded. Opening one folds the others.</summary>
    [ObservableProperty] private int _openStage = 1;

    /// <summary>The first sentence of a status, for the card's header when no short line was given.</summary>
    private static string FirstSentence(string text)
    {
        int i = text.IndexOf(". ", StringComparison.Ordinal);
        return i < 0 ? text : text[..(i + 1)];
    }

    /// <summary>The short line on the card's header; null shows the status's first sentence. Cleared when the status changes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TerrainStatusLine))]
    private string? _terrainStatusShort;

    public string TerrainStatusLine => TerrainStatusShort ?? FirstSentence(TerrainStatus);

    public bool TerrainDone => _settings.Done.Contains(GeneratorStage.Terrain);

    partial void OnTerrainStatusChanged(string value)
    {
        TerrainStatusShort = null;
        OnPropertyChanged(nameof(TerrainStatusLine));
        RefreshDone();
    }

    /// <summary>The short line on the card's header; null shows the status's first sentence. Cleared when the status changes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TownStatusLine))]
    private string? _townStatusShort;

    public string TownStatusLine => TownStatusShort ?? FirstSentence(TownStatus);

    public bool TownDone => _settings.Done.Contains(GeneratorStage.TownSites);

    partial void OnTownStatusChanged(string value)
    {
        TownStatusShort = null;
        OnPropertyChanged(nameof(TownStatusLine));
        RefreshDone();
    }

    /// <summary>The short line on the card's header; null shows the status's first sentence. Cleared when the status changes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FeatureStatusLine))]
    private string? _featureStatusShort;

    public string FeatureStatusLine => FeatureStatusShort ?? FirstSentence(FeatureStatus);

    public bool FeatureDone => _settings.Done.Contains(GeneratorStage.Features);

    partial void OnFeatureStatusChanged(string value)
    {
        FeatureStatusShort = null;
        OnPropertyChanged(nameof(FeatureStatusLine));
        RefreshDone();
    }

    /// <summary>The short line on the card's header; null shows the status's first sentence. Cleared when the status changes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HighwayStatusLine))]
    private string? _highwayStatusShort;

    public string HighwayStatusLine => HighwayStatusShort ?? FirstSentence(HighwayStatus);

    public bool HighwayDone => _settings.Done.Contains(GeneratorStage.Highways);

    partial void OnHighwayStatusChanged(string value)
    {
        HighwayStatusShort = null;
        OnPropertyChanged(nameof(HighwayStatusLine));
        RefreshDone();
    }

    /// <summary>The short line on the card's header; null shows the status's first sentence. Cleared when the status changes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WildernessStatusLine))]
    private string? _wildernessStatusShort;

    public string WildernessStatusLine => WildernessStatusShort ?? FirstSentence(WildernessStatus);

    public bool WildernessDone => _settings.Done.Contains(GeneratorStage.Wilderness);

    partial void OnWildernessStatusChanged(string value)
    {
        WildernessStatusShort = null;
        OnPropertyChanged(nameof(WildernessStatusLine));
        RefreshDone();
    }

    /// <summary>The short line on the card's header; null shows the status's first sentence. Cleared when the status changes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpawnStatusLine))]
    private string? _spawnStatusShort;

    public string SpawnStatusLine => SpawnStatusShort ?? FirstSentence(SpawnStatus);

    public bool SpawnDone => _settings.Done.Contains(GeneratorStage.Spawns);

    partial void OnSpawnStatusChanged(string value)
    {
        SpawnStatusShort = null;
        OnPropertyChanged(nameof(SpawnStatusLine));
        RefreshDone();
    }

    /// <summary>Raises the done marks on the cards' headers, after a stage ran or was taken out.</summary>
    public void RefreshDone()
    {
        OnPropertyChanged(nameof(TerrainDone));
        OnPropertyChanged(nameof(TownDone));
        OnPropertyChanged(nameof(FeatureDone));
        OnPropertyChanged(nameof(HighwayDone));
        OnPropertyChanged(nameof(WildernessDone));
        OnPropertyChanged(nameof(SpawnDone));
    }

    // ---- presets -------------------------------------------------------------

    public ObservableCollection<GeneratorPreset> Presets { get; } = [];

    [ObservableProperty] private string _presetName = "";

    public ObservableCollection<TownPreset> TownPresets { get; } = [];

    [ObservableProperty] private string _townPresetName = "";
}

/// <summary>A biome's row in the Generate panel: its own plains, hills and mountains weights, when ticked.</summary>
public sealed class BiomeWeightRow(GenerateViewModel owner, RwgBiome biome) : ObservableObject
{
    public RwgBiome Biome { get; } = biome;

    /// <summary>The game's name for the biome, e.g. "Pine Forest".</summary>
    public string Name => GameNames.Current.Biome(Biome switch
    {
        RwgBiome.Forest => "pine_forest",
        RwgBiome.BurntForest => "burnt_forest",
        RwgBiome.Desert => "desert",
        RwgBiome.Snow => "snow",
        _ => "wasteland",
    });

    private TerrainOptions Terrain => owner.Settings.Terrain;

    public bool Own
    {
        get => Terrain.BiomeWeights.ContainsKey(Biome);
        set
        {
            if (value == Own) return;
            if (value) Terrain.BiomeWeights[Biome] = Terrain.WeightsFor(Biome);
            else Terrain.BiomeWeights.Remove(Biome);
            Refresh();
            owner.NoteEdit();
        }
    }

    public int Plains
    {
        get => Terrain.WeightsFor(Biome).Plains;
        set => Set(Terrain.WeightsFor(Biome) with { Plains = Math.Clamp(value, 0, 99) });
    }

    public int Hills
    {
        get => Terrain.WeightsFor(Biome).Hills;
        set => Set(Terrain.WeightsFor(Biome) with { Hills = Math.Clamp(value, 0, 99) });
    }

    public int Mountains
    {
        get => Terrain.WeightsFor(Biome).Mountains;
        set => Set(Terrain.WeightsFor(Biome) with { Mountains = Math.Clamp(value, 0, 99) });
    }

    private void Set(TerrainWeights weights)
    {
        Terrain.BiomeWeights[Biome] = weights;
        Refresh();
        owner.NoteEdit();
    }

    public void Refresh() => OnPropertyChanged(string.Empty);
}

/// <summary>A township's row in the Generate panel's town table.</summary>
public sealed class TownRowViewModel(TownRow row) : ObservableObject
{
    public TownRow Row { get; } = row;

    public string Name => Row.Township;

    /// <summary>The township's friendly name, e.g. "Old West". The game has none, so the editor keeps its own list (<see cref="GameNames.TownshipNames"/>).</summary>
    public string DisplayName => GameNames.Township(Row.Township);

    /// <summary>What the game's own town types are, in plain words (from rwgmixer's biomes and flags); empty for others.</summary>
    public string Description => Row.Township switch
    {
        "oldwest" => "Desert ghost town. No trader, no roads out",
        "forest_countrytown" => "Small village in the forest",
        "bforest_countrytown" => "Small village in the burnt forest",
        "bforest_town" => "Town in the burnt forest",
        "countrytown" => "Small village in desert or snow",
        "town" => "Town in desert or snow",
        "city" => "City in desert or snow",
        "wasteland_city" => "City in the wasteland",
        "citybig" => "The biggest city, in the wasteland",
        "roadside" => "Single roadside stop. No trader",
        _ => "",
    };

    public int Count
    {
        get => Row.Count;
        set { Row.Count = Math.Clamp(value, 0, 999); Changed(); }
    }

    public int MinTiles
    {
        get => Row.MinTiles;
        set { Row.MinTiles = Math.Clamp(value, 1, 400); Changed(); OnPropertyChanged(nameof(SizeText)); }
    }

    public int MaxTiles
    {
        get => Row.MaxTiles;
        set { Row.MaxTiles = Math.Clamp(value, 1, 400); Changed(); OnPropertyChanged(nameof(SizeText)); }
    }

    /// <summary>The size range in metres, roughly: a town of n tiles is about the square root of n tiles across.</summary>
    public string SizeText
    {
        get
        {
            static int Across(int tiles) => (int)(Math.Round(Math.Sqrt(tiles) * TownSiteStage.Cell / 50) * 50);
            return Across(Row.MinTiles) == Across(Row.MaxTiles) ? $"{Across(Row.MinTiles)} m" : $"{Across(Row.MinTiles)}–{Across(Row.MaxTiles)} m";
        }
    }

    /// <summary>The fewest street tiles to any other town.</summary>
    public int Distance
    {
        get => Row.Distance;
        set { Row.Distance = Math.Clamp(value, 1, 50); Changed(); }
    }

    /// <summary>The biomes it may start in, comma separated; empty for rwgmixer's list.</summary>
    public string Biomes
    {
        get => Row.Biomes is null ? "" : string.Join(", ", Row.Biomes);
        set
        {
            var list = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(b => b.ToLowerInvariant()).Where(b => TownSiteStage.BiomeTags.Contains(b)).Distinct().ToList();
            Row.Biomes = list.Count == 0 ? null : list;
            Changed();
        }
    }

    /// <summary>The user changed this row (not raised by <see cref="Refresh"/>).</summary>
    public event EventHandler? Edited;

    private void Changed([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        OnPropertyChanged(name);
        Edited?.Invoke(this, EventArgs.Empty);
    }

    public void Refresh() => OnPropertyChanged(string.Empty);
}


/// <summary>A feature kind's row in the Generate panel: how many, and its size range (rwgmixer's scale).</summary>
public sealed class FeatureRowViewModel(FeatureRow row, (int Few, int Default, int Many) game) : ObservableObject
{
    public FeatureRow Row { get; } = row;

    public static string NameOf(FeatureKind kind) => kind switch
    {
        FeatureKind.River => "River",
        FeatureKind.Canyon => "Canyon",
        FeatureKind.Crater => "Crater",
        _ => "Lake",
    };

    public string Name => NameOf(Row.Kind) + "s";

    /// <summary>The game's Few and Many, beside the count.</summary>
    public string GameNote => game == default ? "" : $"few {game.Few}, many {game.Many}";

    public int Count
    {
        get => Row.Count;
        set { Row.Count = Math.Clamp(value, 0, 999); Changed(); }
    }

    public float ScaleMin
    {
        get => Row.ScaleMin;
        set { Row.ScaleMin = Math.Clamp(value, 0.1f, 10); Changed(); }
    }

    public float ScaleMax
    {
        get => Row.ScaleMax;
        set { Row.ScaleMax = Math.Clamp(value, 0.1f, 10); Changed(); }
    }

    /// <summary>The user changed this row (not raised by <see cref="Refresh"/>).</summary>
    public event EventHandler? Edited;

    private void Changed([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        OnPropertyChanged(name);
        Edited?.Invoke(this, EventArgs.Empty);
    }

    public void Refresh() => OnPropertyChanged(string.Empty);
}
