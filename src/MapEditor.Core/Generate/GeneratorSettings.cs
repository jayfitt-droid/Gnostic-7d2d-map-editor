using System.Text.Json.Serialization;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Generate;

/// <summary>The generator's stages, in the order they run.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GeneratorStage>))]
public enum GeneratorStage
{
    Terrain,
    TownSites,
    Features,
    Highways,
    Wilderness,
    Spawns,
}

/// <summary>
/// Everything the Generate panel needs to make a world again: the world seed, each stage's seed and settings, the
/// tiles painted by hand, and which stages have run. Stored in the project; presets keep only the settings.
/// </summary>
public sealed class GeneratorSettings
{
    /// <summary>Gives each stage its first seed.</summary>
    public int WorldSeed { get; set; } = 1;

    public TerrainOptions Terrain { get; set; } = new();

    /// <summary>
    /// The biome layout New World generated, so the terrain stage can plan it again for a new land shape. Null for a
    /// world painted with one biome, an opened world, or a project from before land shapes.
    /// </summary>
    public BiomeLayoutSettings? Biomes { get; set; }

    /// <summary>Terrain types painted onto 256 m tiles (x + z * tiles, z from the south); null when none are.</summary>
    public TerrainType[]? PaintedTerrain { get; set; }

    /// <summary>The types the last terrain run gave each tile; town sites keep off its mountains.</summary>
    public TerrainType[]? TerrainPlan { get; set; }

    public TownSiteOptions Towns { get; set; } = new();

    /// <summary>The planned and built towns.</summary>
    public List<TownSite> Sites { get; set; } = [];

    public FeatureOptions Features { get; set; } = new();

    /// <summary>
    /// The lakes, rivers, canyons and craters pressed into the world. Edits keep this same list (undo puts its items
    /// back), so it is filled in place, never replaced.
    /// </summary>
    public List<Feature> PlacedFeatures { get; set; } = [];

    public HighwayOptions Highways { get; set; } = new();

    /// <summary>The roads the highways stage drew, with the ground each replaced. Filled in place, as <see cref="PlacedFeatures"/>.</summary>
    public List<HighwayRoad> PlacedRoads { get; set; } = [];

    public WildernessOptions Wilderness { get; set; } = new();

    /// <summary>The wilderness POIs and gravel roads stage 5 placed. Filled in place, as <see cref="PlacedFeatures"/>.</summary>
    public List<WildernessPoi> PlacedWilderness { get; set; } = [];

    public SpawnOptions Spawns { get; set; } = new();

    /// <summary>The spawns stage 6 placed. Filled in place, as <see cref="PlacedFeatures"/>.</summary>
    public List<PlacedSpawn> PlacedSpawns { get; set; } = [];

    /// <summary>Stages that have run on this world.</summary>
    public HashSet<GeneratorStage> Done { get; set; } = [];

    /// <summary>A stage's first seed for a world seed: the same world seed always gives the same stage seeds.</summary>
    public static int StageSeed(int worldSeed, GeneratorStage stage) => Mix(worldSeed, (ulong)stage + 1);

    /// <summary>The land shape's first seed for a world seed, apart from every stage's.</summary>
    public static int LandSeed(int worldSeed) => Mix(worldSeed, 100);

    private static int Mix(int worldSeed, ulong salt)
    {
        // SplitMix64 on the pair, so neighbouring world seeds give unrelated stage seeds.
        ulong z = (ulong)(uint)worldSeed * 0x9E3779B97F4A7C15UL + salt;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (int)(z % 1_000_000) + 1;
    }

    /// <summary>Sets the world seed and every stage's seed from it.</summary>
    public void SetWorldSeed(int seed)
    {
        WorldSeed = seed;
        Terrain.Seed = StageSeed(seed, GeneratorStage.Terrain);
        Terrain.Shape.Seed = LandSeed(seed);
        Towns.Seed = StageSeed(seed, GeneratorStage.TownSites);
        Features.Seed = StageSeed(seed, GeneratorStage.Features);
        Highways.Seed = StageSeed(seed, GeneratorStage.Highways);
        Wilderness.Seed = StageSeed(seed, GeneratorStage.Wilderness);
        Spawns.Seed = StageSeed(seed, GeneratorStage.Spawns);
    }

    /// <summary>
    /// The settings a new world starts with: RWG's defaults, the New World dialog's seed, edges and land shape, and the
    /// biome layout it generated.
    /// </summary>
    public static GeneratorSettings ForNewWorld(int worldSeed, EdgeStyle[] edges, float baseHeight, LandShape? shape = null,
        BiomeLayoutSettings? biomes = null)
    {
        var settings = new GeneratorSettings();
        settings.SetWorldSeed(worldSeed);
        settings.Terrain.Edges = [.. edges];
        settings.Terrain.BaseHeight = baseHeight;
        if (shape is not null) settings.Terrain.Shape = shape.Clone();
        settings.Biomes = biomes?.Clone();
        if (settings.Biomes is not null) settings.Biomes.PlannedFor = settings.Terrain.Shape.Clone();
        return settings;
    }

    /// <summary>Whether the land shape changed since the biomes were planned, so planning them again would fit it.</summary>
    public bool BiomesOutOfDate => Biomes is { } b && !Terrain.Shape.SameLand(b.PlannedFor);

    /// <summary>A copy of just the settings, for a preset: no seeds, edges, painted tiles or progress, which belong to the world.</summary>
    public GeneratorPreset ToPreset(string name) => new()
    {
        Name = name,
        Terrain = Terrain.Clone(),
        Towns = Towns.Clone(),
        Features = Features.Clone(),
        Highways = Highways.Clone(),
        Wilderness = Wilderness.Clone(),
        Spawns = Spawns.Clone(),
    };

    /// <summary>
    /// Takes a preset's settings, keeping this world's seeds, edges, base height, painted tiles and progress. With
    /// <paramref name="mixer"/> (this world's size), empty town and feature rows take the game's numbers, and a
    /// built-in preset then scales them.
    /// </summary>
    public void Apply(GeneratorPreset preset, GameData.RwgMixer? mixer = null)
    {
        ApplySettings(preset);
        if (mixer is null) return;
        if (Towns.Rows.Count == 0) Towns.Rows = TownSiteStage.DefaultRows(mixer);
        Features.Rows = FeatureStage.RowsFor(Features, mixer);
        preset.Adjust?.Invoke(this, mixer);
    }

    private void ApplySettings(GeneratorPreset preset)
    {
        var own = Terrain;
        Terrain = preset.Terrain.Clone();
        Terrain.Seed = own.Seed;
        Terrain.Edges = own.Edges;
        Terrain.BaseHeight = own.BaseHeight;
        Terrain.Shape = own.Shape;
        // A preset from before town sites, or the game's defaults, leaves the rows to come from rwgmixer.
        int townSeed = Towns.Seed;
        Towns = preset.Towns.Clone();
        Towns.Seed = townSeed;
        int featureSeed = Features.Seed;
        Features = preset.Features.Clone();
        Features.Seed = featureSeed;
        int highwaySeed = Highways.Seed;
        Highways = preset.Highways.Clone();
        Highways.Seed = highwaySeed;
        int wildSeed = Wilderness.Seed;
        Wilderness = (preset.Wilderness ?? new()).Clone();
        Wilderness.Seed = wildSeed;
        int spawnSeed = Spawns.Seed;
        Spawns = (preset.Spawns ?? new()).Clone();
        Spawns.Seed = spawnSeed;
    }

    public GeneratorSettings Clone() => new()
    {
        WorldSeed = WorldSeed,
        Terrain = Terrain.Clone(),
        Biomes = Biomes?.Clone(),
        PaintedTerrain = PaintedTerrain is null ? null : [.. PaintedTerrain],
        TerrainPlan = TerrainPlan is null ? null : [.. TerrainPlan],
        Towns = Towns.Clone(),
        Sites = [.. Sites.Select(t => t.Clone())],
        Features = Features.Clone(),
        PlacedFeatures = [.. PlacedFeatures.Select(f => f.Clone())],
        Highways = Highways.Clone(),
        PlacedRoads = [.. PlacedRoads.Select(r => r.Clone())],
        Wilderness = Wilderness.Clone(),
        PlacedWilderness = [.. PlacedWilderness.Select(p => p.Clone())],
        Spawns = Spawns.Clone(),
        PlacedSpawns = [.. PlacedSpawns.Select(p => p.Clone())],
        Done = [.. Done],
    };
}

/// <summary>A generated biome layout's settings, kept so it can be planned again to fit a new land shape.</summary>
public sealed class BiomeLayoutSettings
{
    public BiomeLayoutKind Layout { get; set; }

    public List<BiomeShare> Shares { get; set; } = [];

    /// <summary>The New World seed the layout was made with.</summary>
    public int Seed { get; set; }

    /// <summary>The land shape the biomes were last planned for.</summary>
    public LandShape PlannedFor { get; set; } = new();

    public static BiomeLayoutSettings From(BiomePlan plan, int seed, LandShape? shape) => new()
    {
        Layout = plan.Layout,
        Shares = [.. plan.Shares],
        Seed = seed,
        PlannedFor = shape?.Clone() ?? new LandShape(),
    };

    public BiomePlan ToPlan() => new(Layout, Shares);

    public BiomeLayoutSettings Clone() => new() { Layout = Layout, Shares = [.. Shares], Seed = Seed, PlannedFor = PlannedFor.Clone() };
}

/// <summary>Named generator settings, kept in the app's settings.</summary>
public sealed class GeneratorPreset
{
    public string Name { get; set; } = "";
    public TerrainOptions Terrain { get; set; } = new();

    /// <summary>Town rows and the edge margin; empty rows mean rwgmixer's defaults for the world's size.</summary>
    public TownSiteOptions Towns { get; set; } = new();

    /// <summary>Feature rows and options; empty rows mean rwgmixer's defaults for the world's size.</summary>
    public FeatureOptions Features { get; set; } = new();

    public HighwayOptions Highways { get; set; } = new();

    /// <summary>Null in a preset saved before stage 5.</summary>
    public WildernessOptions? Wilderness { get; set; } = new();

    /// <summary>Null in a preset saved before stage 6.</summary>
    public SpawnOptions? Spawns { get; set; } = new();

    /// <summary>One line on what the preset changes, shown under the preset list; empty for saved presets.</summary>
    [JsonIgnore] public string Summary { get; init; } = "";

    /// <summary>Comes with the editor: it can't be deleted or saved over.</summary>
    [JsonIgnore] public bool BuiltIn { get; init; }

    /// <summary>
    /// A built-in preset's changes, made after the game's town and feature numbers for the world's size are filled in,
    /// so counts scale with the world.
    /// </summary>
    [JsonIgnore] public Action<GeneratorSettings, GameData.RwgMixer>? Adjust { get; init; }

    /// <summary>RWG's own settings.</summary>
    public static GeneratorPreset GameDefaults => new() { Name = "Game defaults", Summary = "The game's own settings.", BuiltIn = true };
}
