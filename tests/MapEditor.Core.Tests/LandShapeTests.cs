using System.Text.Json;
using System.Xml.Linq;
using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Generate;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Tests;

public sealed class LandShapeTests
{
    private static LandShape Shape(LandShapeKind kind, int seed = 7, int land = 45, MapEdge edge = MapEdge.South) =>
        new() { Kind = kind, Seed = seed, LandPercent = land, PeninsulaEdge = edge };

    /// <summary>Whether any cell on the edge's outer row is land.</summary>
    private static bool TouchesEdge(LandField f, MapEdge edge)
    {
        for (int i = 0; i < f.Width; i++)
        {
            var (x, z) = edge switch
            {
                MapEdge.North => (i, f.Width - 1),
                MapEdge.East => (f.Width - 1, i),
                MapEdge.South => (i, 0),
                _ => (0, i),
            };
            if (f.IsLandCell(x, z)) return true;
        }
        return false;
    }

    /// <summary>The number of separate land areas (cells joined edge to edge).</summary>
    private static int LandAreas(LandField f)
    {
        int w = f.Width, areas = 0;
        var seen = new bool[w * w];
        var stack = new Stack<int>();
        for (int start = 0; start < w * w; start++)
        {
            if (seen[start] || !f.IsLandCell(start % w, start / w)) continue;
            areas++;
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int i = stack.Pop(), x = i % w, z = i / w;
                foreach (var (nx, nz) in new[] { (x - 1, z), (x + 1, z), (x, z - 1), (x, z + 1) })
                {
                    if (nx < 0 || nz < 0 || nx >= w || nz >= w) continue;
                    int j = nx + nz * w;
                    if (seen[j] || !f.IsLandCell(nx, nz)) continue;
                    seen[j] = true;
                    stack.Push(j);
                }
            }
        }
        return areas;
    }

    [Fact]
    public void Edges_has_no_land_field()
    {
        Assert.Null(LandShapes.Build(4096, new LandShape()));
    }

    [Fact]
    public void A_saved_drawn_shape_builds_its_own_coast()
    {
        var shape = JsonSerializer.Deserialize<LandShape>("""
            {"Kind":"Drawn","Drawing":{"Boxes":[{"X0":512,"Z0":1024,"X1":2560,"Z1":3072}]}}
            """)!;
        var land = LandShapes.Build(4096, shape)!;
        Assert.True(land.IsLand(1536, 2048));
        Assert.False(land.IsLand(3200, 2048));
    }

    [Theory]
    [InlineData(4096)]
    [InlineData(8192)]
    [InlineData(16384)]
    public void An_island_leaves_ocean_on_every_edge(int size)
    {
        for (int seed = 1; seed <= 4; seed++)
        {
            var f = LandShapes.Build(size, Shape(LandShapeKind.Island, seed))!;
            foreach (var edge in Enum.GetValues<MapEdge>()) Assert.False(TouchesEdge(f, edge), $"seed {seed}: land on the {edge} edge");
            Assert.True(f.IsLand(size / 2f, size / 2f), $"seed {seed}: no land in the middle");
        }
    }

    [Theory]
    [InlineData(4096)]
    [InlineData(16384)]
    public void An_archipelago_is_several_islands_with_ocean_on_every_edge(int size)
    {
        for (int seed = 1; seed <= 4; seed++)
        {
            var f = LandShapes.Build(size, Shape(LandShapeKind.Archipelago, seed, land: 35))!;
            foreach (var edge in Enum.GetValues<MapEdge>()) Assert.False(TouchesEdge(f, edge), $"seed {seed}: land on the {edge} edge");
            Assert.True(LandAreas(f) >= 2, $"seed {seed}: one land area");
        }
    }

    [Theory]
    [InlineData(MapEdge.North, 4096)]
    [InlineData(MapEdge.East, 6144)]
    [InlineData(MapEdge.South, 8192)]
    [InlineData(MapEdge.West, 16384)]
    public void A_peninsula_touches_exactly_its_own_edge(MapEdge edge, int size)
    {
        for (int seed = 1; seed <= 4; seed++)
        {
            var f = LandShapes.Build(size, Shape(LandShapeKind.Peninsula, seed, edge: edge))!;
            foreach (var e in Enum.GetValues<MapEdge>())
                Assert.True(TouchesEdge(f, e) == (e == edge), $"seed {seed}: the {e} edge is {(TouchesEdge(f, e) ? "land" : "sea")}");
        }
    }

    /// <summary>Metres from an edge to the nearest land cell's centre (the map's width if there is none).</summary>
    private static float LandReach(LandField f, MapEdge edge)
    {
        int best = f.Width;
        for (int cz = 0; cz < f.Width; cz++)
            for (int cx = 0; cx < f.Width; cx++)
            {
                if (!f.IsLandCell(cx, cz)) continue;
                int d = edge switch { MapEdge.North => f.Width - 1 - cz, MapEdge.East => f.Width - 1 - cx, MapEdge.South => cz, _ => cx };
                best = Math.Min(best, d);
            }
        return (best + 0.5f) * LandField.Cell;
    }

    [Theory]
    [InlineData(LandShapeKind.Island)]
    [InlineData(LandShapeKind.Archipelago)]
    [InlineData(LandShapeKind.Peninsula)]
    public void Land_comes_close_to_every_sea_edge_at_the_default_land_share(LandShapeKind kind)
    {
        // 8192 m worlds: the planner works on 32 m cells, so this is fast.
        // At 45% land a single shape can't come within 500 m of all its edges (the hard margin alone is ~410 m), so
        // the limits are per kind, just above what the planner reaches; they guard against the old, emptier edges.
        float limit = kind switch { LandShapeKind.Archipelago => 900, LandShapeKind.Island => 1300, _ => 2000 };
        for (int seed = 1; seed <= 4; seed++)
        {
            var shape = Shape(kind, seed, new LandShape().LandPercent);
            var f = LandShapes.Build(8192, shape)!;
            foreach (var edge in Enum.GetValues<MapEdge>())
                if (shape.EdgeIsSea(edge))
                    Assert.True(LandReach(f, edge) <= limit, $"{kind} seed {seed}: land is {LandReach(f, edge)} m from the {edge} edge");
        }
    }

    [Theory]
    [InlineData(LandShapeKind.Island, 20)]
    [InlineData(LandShapeKind.Island, 60)]
    [InlineData(LandShapeKind.Peninsula, 40)]
    [InlineData(LandShapeKind.Archipelago, 30)]
    public void The_land_share_follows_the_setting(LandShapeKind kind, int percent)
    {
        var f = LandShapes.Build(8192, Shape(kind, 3, percent))!;
        Assert.InRange(f.LandShare(), percent / 100f - 0.04f, percent / 100f + 0.04f);
    }

    [Fact]
    public void The_same_seed_gives_the_same_coast_and_another_seed_another()
    {
        var a = LandShapes.Build(4096, Shape(LandShapeKind.Island, 11))!;
        var b = LandShapes.Build(4096, Shape(LandShapeKind.Island, 11))!;
        var c = LandShapes.Build(4096, Shape(LandShapeKind.Island, 12))!;
        bool same = true, differs = false;
        for (int z = 0; z < a.Width; z++)
            for (int x = 0; x < a.Width; x++)
            {
                same &= a[x, z] == b[x, z];
                differs |= a[x, z] != c[x, z];
            }
        Assert.True(same);
        Assert.True(differs);
    }

    [Fact]
    public void A_new_island_world_has_wet_edges_and_dry_land_in_the_middle()
    {
        var shape = Shape(LandShapeKind.Island, 5);
        var world = NewWorldGenerator.Create(new NewWorldOptions("I", 4096, BaseHeight: 35, NoiseAmplitude: 0, Seed: 5, Shape: shape),
            StampLibrary.Empty, null);
        var water = world.Water!;
        foreach (var (x, z) in new[] { (0, 0), (4095, 0), (0, 4095), (4095, 4095), (2048, 0), (2048, 4095), (0, 2048), (4095, 2048) })
        {
            Assert.Equal(StampOp.DefaultWaterLevel, water[x, z]);
            Assert.True(World.HeightToMetres(world.Height[x, z]) < StampOp.DefaultWaterLevel);
        }
        Assert.Equal(0, water[2048, 2048]);
        Assert.Equal(35, World.HeightToMetres(world.Height[2048, 2048]), 1);
    }

    [Fact]
    public void Biomes_planned_for_an_island_share_its_land_by_weight()
    {
        var shares = BiomeLayout.DefaultShares(BiomeTable.Vanilla);
        int total = shares.Sum(s => s.Weight);
        var land = LandShapes.Build(8192, Shape(LandShapeKind.Island, 2, 40))!.LandTiles(BiomeLayout.TileSize);
        foreach (var layout in new[] { BiomeLayoutKind.Circle, BiomeLayoutKind.Line, BiomeLayoutKind.CenterWasteland })
        {
            var tiles = BiomeLayout.PlanTiles(8192, shares, layout, new Random(4), land);
            var counts = new int[shares.Count];
            int landTiles = 0;
            for (int x = 0; x < 32; x++)
                for (int z = 0; z < 32; z++)
                {
                    Assert.True(tiles[x, z] >= 0);
                    if (!land[x, z]) continue;
                    landTiles++;
                    counts[tiles[x, z]]++;
                }
            for (int i = 0; i < shares.Count; i++)
            {
                double got = counts[i] / (double)landTiles, want = shares[i].Weight / (double)total;
                bool centre = layout == BiomeLayoutKind.CenterWasteland && shares[i].Name == "wasteland";
                Assert.True(counts[i] > 0, $"{layout}: no {shares[i].Name} on the land");
                Assert.InRange(got, centre ? want / 2 : want - 0.1, want + 0.1);
            }
        }
    }

    [Fact]
    public void Without_a_land_mask_the_biome_layout_is_unchanged()
    {
        var shares = BiomeLayout.DefaultShares(BiomeTable.Vanilla);
        var all = new bool[24, 24];
        for (int x = 0; x < 24; x++)
            for (int z = 0; z < 24; z++)
                all[x, z] = true;
        var a = BiomeLayout.PlanTiles(6144, shares, BiomeLayoutKind.Circle, new Random(9));
        var b = BiomeLayout.PlanTiles(6144, shares, BiomeLayoutKind.Circle, new Random(9), null);
        Assert.Equal(a.Cast<int>(), b.Cast<int>());
        var c = BiomeLayout.PlanTiles(6144, shares, BiomeLayoutKind.Circle, new Random(9), all);
        // All land plans the same tiles, but seeds sit on the land's middle rather than the map's (the same here).
        Assert.Equal(a.Cast<int>().Distinct().Count(), c.Cast<int>().Distinct().Count());
    }

    private static readonly RwgMixer Mixer = RwgMixer.Parse(XDocument.Parse("""
        <rwgmixer><world name="any"><property name="world_size" value="0,99999"/>
          <property class="plains"><property name="scale" value="1, 1"/><property name="clusters" value="2, .5, .5"/></property>
          <property class="hills"><property name="clusters" value="2, .3, 1"/></property>
          <property class="mountains"><property name="clusters" value="3, .5, .76"/></property>
        </world></rwgmixer>
        """), 4096);

    [Fact]
    public void The_terrain_stage_keeps_the_sea_bare_and_towns_off_it()
    {
        var shape = Shape(LandShapeKind.Peninsula, 8, 45, MapEdge.West);
        var plan = new BiomePlan(BiomeLayoutKind.Circle, BiomeLayout.DefaultShares(BiomeTable.Vanilla));
        var world = NewWorldGenerator.Create(new NewWorldOptions("P", 4096, BaseHeight: 35, NoiseAmplitude: 0, Seed: 8, BiomePlan: plan,
            Shape: shape), StampLibrary.Empty, null);
        var options = new TerrainOptions { Seed = 3, Shape = shape };
        var result = TerrainStage.Run(world, options, StampLibrary.Empty, Mixer, null, null, null, new EditSession("T", world), cancel: TestContext.Current.CancellationToken);

        var field = LandShapes.Build(4096, shape)!;
        var share = field.TileLand(TerrainStage.TileSize);
        for (int z = 0; z < 16; z++)
            for (int x = 0; x < 16; x++)
                if (share[x, z] < 0.5f) Assert.Equal(TerrainType.None, result.Plan[x + z * 16]);
                else Assert.NotEqual(TerrainType.None, result.Plan[x + z * 16]);

        // Sea on the three other edges, land on its own.
        Assert.Equal(StampOp.DefaultWaterLevel, world.Water![4095, 2048]);
        Assert.Equal(StampOp.DefaultWaterLevel, world.Water[2048, 0]);
        Assert.Equal(StampOp.DefaultWaterLevel, world.Water[2048, 4095]);
        Assert.Equal(0, world.Water[0, 2048]);

        var cells = TownCells.Build(world, BiomeTable.Vanilla, PrefabLibrary.Of([]), result.Plan, crossBorders: true);
        int valid = 0;
        for (int cz = 0; cz < cells.Width; cz++)
            for (int cx = 0; cx < cells.Width; cx++)
            {
                if (!cells.IsValid(cx, cz)) continue;
                valid++;
                int gx = cx * TownSiteStage.Cell + 75, gz = cz * TownSiteStage.Cell + 75;
                Assert.True(field.IsLand(gx, gz), $"town cell {cx},{cz} is in the sea");
            }
        Assert.True(valid > 50);
    }

    [Fact]
    public void Planning_biomes_again_fits_them_to_a_new_shape()
    {
        var plan = new BiomePlan(BiomeLayoutKind.CenterForest, BiomeLayout.DefaultShares(BiomeTable.Vanilla));
        var world = NewWorldGenerator.Create(new NewWorldOptions("B", 4096, NoiseAmplitude: 0, Seed: 4, BiomePlan: plan),
            StampLibrary.Empty, null);
        var before = (byte[])world.Biomes!.Indices.Data.Clone();
        var settings = GeneratorSettings.ForNewWorld(4, [EdgeStyle.Land, EdgeStyle.Land, EdgeStyle.Land, EdgeStyle.Land], 35,
            biomes: BiomeLayoutSettings.From(plan, 4, null));
        Assert.False(settings.BiomesOutOfDate);
        settings.Terrain.Shape = Shape(LandShapeKind.Island, 4, 40);
        Assert.True(settings.BiomesOutOfDate);

        var session = new EditSession("T", world);
        var result = TerrainStage.Run(world, settings.Terrain, StampLibrary.Empty, Mixer, null, null, null, session, cancel: TestContext.Current.CancellationToken,
            replanBiomes: settings.Biomes);
        Assert.True(result.BiomesPlanned);
        Assert.NotEqual(before, world.Biomes.Indices.Data);

        // An area run never plans biomes.
        var area = TerrainStage.Run(world, settings.Terrain, StampLibrary.Empty, Mixer, null, new GridRect(2, 2, 4, 4), null,
            new EditSession("A", world), cancel: TestContext.Current.CancellationToken, replanBiomes: settings.Biomes);
        Assert.False(area.BiomesPlanned);
    }

    [Fact]
    public void Planning_biomes_alone_leaves_the_ground_and_a_new_seed_changes_the_layout()
    {
        var plan = new BiomePlan(BiomeLayoutKind.CenterForest, BiomeLayout.DefaultShares(BiomeTable.Vanilla));
        var world = NewWorldGenerator.Create(new NewWorldOptions("B", 4096, NoiseAmplitude: 0, Seed: 4, BiomePlan: plan),
            StampLibrary.Empty, null);
        var layout = BiomeLayoutSettings.From(plan, 4, null);
        var shape = Shape(LandShapeKind.Island, 4, 40);
        var height = (ushort[])world.Height.Data.Clone();

        // The same seed on the same land gives the same layout (#26)...
        Assert.True(TerrainStage.PlanBiomes(world, layout, shape, StampLibrary.Empty, new EditSession("same", world)));
        var first = (byte[])world.Biomes!.Indices.Data.Clone();
        Assert.True(TerrainStage.PlanBiomes(world, layout, shape, StampLibrary.Empty, new EditSession("same", world)));
        Assert.Equal(first, world.Biomes.Indices.Data);

        // ...and a new seed gives another, without touching the ground. Undo puts the old layout back.
        layout.Seed = 99;
        var session = new EditSession("reroll", world);
        Assert.True(TerrainStage.PlanBiomes(world, layout, shape, StampLibrary.Empty, session));
        Assert.NotEqual(first, world.Biomes.Indices.Data);
        Assert.Equal(height, world.Height.Data);
        session.Rollback();
        Assert.Equal(first, world.Biomes.Indices.Data);
    }

    [Fact]
    public void Shape_and_biome_layout_survive_a_save()
    {
        var settings = GeneratorSettings.ForNewWorld(12, [EdgeStyle.Land, EdgeStyle.Water, EdgeStyle.Land, EdgeStyle.Land], 35,
            Shape(LandShapeKind.Archipelago, 99, 30),
            BiomeLayoutSettings.From(new BiomePlan(BiomeLayoutKind.Line, BiomeLayout.DefaultShares(BiomeTable.Vanilla)), 12, null));
        settings.Terrain.Shape.Islands = 9;
        var back = JsonSerializer.Deserialize<GeneratorSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(LandShapeKind.Archipelago, back.Terrain.Shape.Kind);
        Assert.Equal(9, back.Terrain.Shape.Islands);
        Assert.Equal(30, back.Terrain.Shape.LandPercent);
        Assert.Equal(BiomeLayoutKind.Line, back.Biomes!.Layout);
        Assert.Equal(5, back.Biomes.Shares.Count);
        Assert.Equal(LandShapeKind.Archipelago, back.Biomes.PlannedFor.Kind);
        Assert.True(back.BiomesOutOfDate); // islands changed after planning

        // An old project's settings load as land to the edges.
        var old = JsonSerializer.Deserialize<GeneratorSettings>("""{"WorldSeed":3,"Terrain":{"Seed":4}}""")!;
        Assert.Equal(LandShapeKind.Edges, old.Terrain.Shape.Kind);
        Assert.Null(old.Biomes);
    }

    [Fact]
    public void A_preset_keeps_the_worlds_shape()
    {
        var settings = GeneratorSettings.ForNewWorld(1, [EdgeStyle.Land, EdgeStyle.Land, EdgeStyle.Land, EdgeStyle.Land], 35,
            Shape(LandShapeKind.Island, 5));
        settings.Apply(GeneratorPreset.GameDefaults);
        Assert.Equal(LandShapeKind.Island, settings.Terrain.Shape.Kind);
    }

    [Fact]
    public void The_world_seed_sets_the_coast_seed()
    {
        var s = new GeneratorSettings();
        s.SetWorldSeed(77);
        Assert.Equal(GeneratorSettings.LandSeed(77), s.Terrain.Shape.Seed);
        Assert.NotEqual(s.Terrain.Seed, s.Terrain.Shape.Seed);
    }
}
