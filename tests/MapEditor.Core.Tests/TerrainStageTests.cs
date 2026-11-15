using System.Xml.Linq;
using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Generate;
using MapEditor.Core.IO;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Tests;

public sealed class TerrainStageTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mapeditor-terrain-").FullName;
    private readonly StampLibrary _stamps;

    public TerrainStageTests()
    {
        // Game-like stamps: a constant 255 m height with the shape in a round alpha falloff.
        foreach (var name in new[] { "plains_01", "hills_01", "mountains_01", "desert_mountains_01", "land_border_01" })
            WriteRoundStamp(Path.Combine(_dir, name + ".raw"), 256);
        _stamps = StampLibrary.Load(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static void WriteRoundStamp(string path, int size)
    {
        var bytes = new byte[size * size * 6];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = MathF.Sqrt(MathF.Pow(x - size / 2f, 2) + MathF.Pow(y - size / 2f, 2)) / (size / 2f);
                float alpha = Math.Clamp(1 - d, 0, 1) * 0.2f;
                int o = (x + y * size) * 6;
                BitConverter.TryWriteBytes(bytes.AsSpan(o), (ushort)65535);
                BitConverter.TryWriteBytes(bytes.AsSpan(o + 2), (ushort)(alpha * 65535));
            }
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>A world with one biome on the west half and another on the east half.</summary>
    private static World TwoBiomes(int size, uint west, uint east)
    {
        var world = new World { Name = "T", Size = size, Height = new Grid<ushort>(size, size) };
        Array.Fill(world.Height.Data, TerrainBrush.ToRaw(40));
        var indices = new Grid<byte>(size / 8, size / 8);
        for (int z = 0; z < indices.Height; z++)
            for (int x = indices.Width / 2; x < indices.Width; x++)
                indices[x, z] = 1;
        world.Biomes = new IndexedLayer(indices, [west, east]);
        world.Water = new Grid<byte>(size, size);
        return world;
    }

    private static readonly RwgMixer Mixer = RwgMixer.Parse(XDocument.Parse("""
        <rwgmixer><world name="any"><property name="world_size" value="0,99999"/>
          <property class="plains"><property name="scale" value="1, 1"/><property name="clusters" value="2, .5, .5"/></property>
          <property class="hills"><property name="clusters" value="2, .3, 1"/></property>
          <property class="mountains"><property name="scale" value="1.2, 1.4"/><property name="clusters" value="3, .5, .76"/>
            <property name="use_biome_mask" value="true"/></property>
          <property class="desert_mountains"><property name="clusters" value="1, .4, .6"/><property name="biome_mask_min" value=".05"/></property>
        </world></rwgmixer>
        """), 4096);

    private TerrainResult Run(World world, TerrainOptions options, TerrainType[]? painted = null, GridRect? area = null,
        BiomeMaskRecord? previous = null, EditSession? session = null) =>
        TerrainStage.Run(world, options, _stamps, Mixer, painted, area, previous, session ?? new EditSession("Terrain", world));

    [Fact]
    public void A_biomes_own_terrain_class_falls_back_to_the_terrains_per_setting()
    {
        var desert = Mixer.TerrainRule("desert", "mountains");
        Assert.Equal((1.2f, 1.4f), desert.Scale);             // from mountains
        Assert.Equal(1, desert.ClusterCount);                  // from desert_mountains
        Assert.Equal(0.6f, desert.ClusterStrength, 3);
        Assert.True(desert.UseBiomeMask);                      // from mountains
        Assert.Equal(0.05f, desert.BiomeMaskMin, 3);           // from desert_mountains
        Assert.Equal(3, Mixer.TerrainRule("snow", "mountains").ClusterCount);
        Assert.Equal(85 / 256f, Mixer.TerrainRule("forest", "canyons").ClusterRadius, 3); // GetTerrainProperties' default
    }

    [Fact]
    public void Weights_set_how_many_tiles_get_each_type_and_wasteland_gets_no_mountains()
    {
        var world = TwoBiomes(4096, BiomeLayout.ForestColor, BiomeLayout.WastelandColor);
        var options = new TerrainOptions { Weights = new TerrainWeights(1, 0, 1), Seed = 7 };
        var plan = Run(world, options).Plan;

        int tiles = 16;
        int Count(int x0, int x1, TerrainType t) =>
            Enumerable.Range(0, tiles).Sum(z => Enumerable.Range(x0, x1 - x0).Count(x => plan[x + z * tiles] == t));
        Assert.Equal(64, Count(0, 8, TerrainType.Mountains));
        Assert.Equal(64, Count(0, 8, TerrainType.Plains));
        Assert.Equal(0, Count(8, 16, TerrainType.Mountains));
        Assert.Equal(128, Count(8, 16, TerrainType.Plains));

        // A biome's own weights win, so wasteland can have mountains when asked.
        options.BiomeWeights[RwgBiome.Wasteland] = new TerrainWeights(0, 0, 1);
        plan = Run(world, options).Plan;
        Assert.Equal(128, Count(8, 16, TerrainType.Mountains));
    }

    [Fact]
    public void Painted_tiles_are_kept_and_hills_come_in_aligned_blocks()
    {
        var world = TwoBiomes(2048, BiomeLayout.ForestColor, BiomeLayout.ForestColor);
        var painted = new TerrainType[64];
        painted[3 + 5 * 8] = TerrainType.Mountains;
        painted[0] = TerrainType.Plains;
        var plan = Run(world, new TerrainOptions { Weights = new TerrainWeights(0, 1, 0), Seed = 3 }, painted).Plan;

        Assert.Equal(TerrainType.Mountains, plan[3 + 5 * 8]);
        Assert.Equal(TerrainType.Plains, plan[0]);
        // Every other tile is hills: blocks fill all four tiles except painted ones.
        Assert.Equal(62, plan.Count(t => t == TerrainType.Hills));
    }

    [Fact]
    public void The_same_seed_gives_the_same_terrain_and_another_seed_differs()
    {
        var a = TwoBiomes(2048, BiomeLayout.ForestColor, BiomeLayout.DesertColor);
        var b = TwoBiomes(2048, BiomeLayout.ForestColor, BiomeLayout.DesertColor);
        var c = TwoBiomes(2048, BiomeLayout.ForestColor, BiomeLayout.DesertColor);
        Run(a, new TerrainOptions { Seed = 11 });
        Run(b, new TerrainOptions { Seed = 11 });
        Run(c, new TerrainOptions { Seed = 12 });

        Assert.Equal(a.Height.Data, b.Height.Data);
        Assert.Equal(a.Biomes!.Indices.Data, b.Biomes!.Indices.Data);
        Assert.NotEqual(a.Height.Data, c.Height.Data);
        Assert.True(a.Height.Data.Max() > TerrainBrush.ToRaw(36), "stamps should raise the ground");
    }

    [Fact]
    public void An_area_changes_only_its_tiles_and_the_blend_around_them()
    {
        var world = TwoBiomes(4096, BiomeLayout.ForestColor, BiomeLayout.ForestColor);
        var before = (ushort[])world.Height.Data.Clone();
        var area = new GridRect(4, 4, 8, 8); // 1024..2048 m
        var session = new EditSession("Terrain", world);
        var result = Run(world, new TerrainOptions { Seed = 5 }, area: area, session: session);

        Assert.All(result.Plan.Select((t, i) => (t, i)).Where(p => !area.Contains(p.i % 16, p.i / 16)), p => Assert.Equal(TerrainType.None, p.t));
        Assert.Equal(before[100 + 100 * 4096], world.Height[100, 100]);
        Assert.Equal(before[1024 - TerrainStage.Feather - 1 + 1500 * 4096], world.Height[1024 - TerrainStage.Feather - 1, 1500]);
        Assert.NotEqual(before[1500 + 1500 * 4096], world.Height[1500, 1500]);
    }

    [Fact]
    public void A_rerun_puts_back_the_last_runs_mountain_biome_but_keeps_later_paint()
    {
        var world = TwoBiomes(4096, BiomeLayout.ForestColor, BiomeLayout.SnowColor);
        var original = (byte[])world.Biomes!.Indices.Data.Clone();
        var mountains = new TerrainOptions { Weights = new TerrainWeights(0, 0, 1), Seed = 2 };
        var first = Run(world, mountains);
        Assert.True(first.Mask.Count > 0, "mountains should spread their biome across the split");

        // Paint one masked cell by hand; a flat re-run puts back the rest and keeps that one.
        var grid = world.Biomes.Indices;
        int painted = Enumerable.Range(0, grid.Data.Length).First(i => grid.Data[i] != original[i]);
        grid.Data[painted] = 2;
        world.Biomes.Palette.Add(BiomeLayout.DesertColor);
        Run(world, new TerrainOptions { Weights = new TerrainWeights(1, 0, 0), Seed = 2 }, previous: first.Mask);

        for (int i = 0; i < grid.Data.Length; i++)
            Assert.Equal(i == painted ? (byte)2 : original[i], grid.Data[i]);
    }

    [Fact]
    public void A_cancelled_run_leaves_the_world_as_it_was()
    {
        var world = TwoBiomes(2048, BiomeLayout.ForestColor, BiomeLayout.DesertColor);
        var height = (ushort[])world.Height.Data.Clone();
        var biomes = (byte[])world.Biomes!.Indices.Data.Clone();
        var runner = new EditRunner(new UndoStack());
        Exception? error = null;
        runner.Failed += (_, e) => error = e.Error;
        using var cancel = new CancellationTokenSource();
        var progress = new SyncProgress(p => { if (p > 0.3) cancel.Cancel(); });

        bool ok = runner.Run(world, "Generate terrain", s =>
            TerrainStage.Run(world, new TerrainOptions { Seed = 9 }, _stamps, Mixer, null, null, null, s, progress, cancel.Token));

        Assert.False(ok);
        Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.Equal(height, world.Height.Data);
        Assert.Equal(biomes, world.Biomes.Indices.Data);
    }

    [Fact]
    public void Stage_seeds_follow_the_world_seed_and_settings_survive_a_project()
    {
        Assert.Equal(GeneratorSettings.StageSeed(42, GeneratorStage.Terrain), GeneratorSettings.StageSeed(42, GeneratorStage.Terrain));
        Assert.NotEqual(GeneratorSettings.StageSeed(42, GeneratorStage.Terrain), GeneratorSettings.StageSeed(43, GeneratorStage.Terrain));

        var settings = GeneratorSettings.ForNewWorld(42, [EdgeStyle.Water, EdgeStyle.Land, EdgeStyle.None, EdgeStyle.Land], 35);
        settings.Terrain.BiomeWeights[RwgBiome.Snow] = new TerrainWeights(1, 2, 3);
        settings.PaintedTerrain = new TerrainType[64];
        settings.PaintedTerrain[9] = TerrainType.Hills;
        settings.Done.Add(GeneratorStage.Terrain);

        var world = TwoBiomes(2048, BiomeLayout.ForestColor, BiomeLayout.SnowColor);
        world.ExtraFiles[WorldFiles.Level] = [1, 2, 3];
        string path = Path.Combine(_dir, "g.7dmap");
        ProjectStore.Save(world, path, new ProjectStore.ProjectSettings { Generator = settings });
        ProjectStore.Load(path, out var loaded);

        var g = loaded.Generator!;
        Assert.Equal(42, g.WorldSeed);
        Assert.Equal(settings.Terrain.Seed, g.Terrain.Seed);
        Assert.Equal(EdgeStyle.Water, g.Terrain.Edges[0]);
        Assert.Equal(new TerrainWeights(1, 2, 3), g.Terrain.BiomeWeights[RwgBiome.Snow]);
        Assert.Equal(TerrainType.Hills, g.PaintedTerrain![9]);
        Assert.Contains(GeneratorStage.Terrain, g.Done);

        // A preset carries the weights but not the world's seed or edges.
        var preset = settings.ToPreset("Mine");
        var other = GeneratorSettings.ForNewWorld(1, [EdgeStyle.Land, EdgeStyle.Land, EdgeStyle.Land, EdgeStyle.Land], 35);
        other.Apply(preset);
        Assert.Equal(new TerrainWeights(1, 2, 3), other.Terrain.BiomeWeights[RwgBiome.Snow]);
        Assert.Equal(GeneratorSettings.StageSeed(1, GeneratorStage.Terrain), other.Terrain.Seed);
        Assert.Equal(EdgeStyle.Land, other.Terrain.Edges[0]);
    }

    /// <summary>Reports on the calling thread (Progress&lt;T&gt; posts to the thread pool).</summary>
    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
