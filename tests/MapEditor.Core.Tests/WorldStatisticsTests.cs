using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.IO;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Tests;

public sealed class WorldStatisticsTests
{
    private static World Blank(int size) => new() { Name = "Test world", Size = size, Height = new Grid<ushort>(size, size) };

    private static WorldStatistics Measure(World world, PrefabLibrary? library = null) =>
        WorldStatistics.Measure(world, BiomeTable.Vanilla, library ?? PrefabLibrary.Empty);

    [Fact]
    public void Biomes_use_dry_area_and_game_names()
    {
        var world = Blank(4);
        world.Biomes = new IndexedLayer(new Grid<byte>(2, 2, [0, 1, 0, 1]), [0x004000, 0xFFE477]);
        world.Water = new Grid<byte>(4, 4);
        world.Water.Data.AsSpan(0, 8).Fill(20);
        // Water paint lying below the terrain is dry, as on the map.
        world.Height[2, 0] = World.MetresToHeight(50);
        world.Height[3, 0] = World.MetresToHeight(60);
        world.Height[2, 1] = World.MetresToHeight(70);
        world.Height[3, 1] = World.MetresToHeight(80);

        var stats = Measure(world);
        Assert.Equal(75, stats.LandPercent);
        Assert.Equal(25, stats.WaterPercent);
        Assert.Equal(0, stats.MinHeightMetres);
        Assert.Equal(80, stats.MaxHeightMetres);
        Assert.Equal(100.0 / 3, stats.BiomeLandPercent["pine_forest"]);
        Assert.Equal(200.0 / 3, stats.BiomeLandPercent["desert"]);
        string summary = WorldSummaryFormatter.Format(stats);
        Assert.Contains("Biomes (% of land)", summary);
        Assert.Contains("pine_forest", summary);
        Assert.DoesNotContain("#004000", summary);
    }

    [Fact]
    public void Roads_convert_painted_square_metres_to_kilometres_by_surface()
    {
        var world = Blank(128);
        var roads = world.Roads = new Grid<byte>(world.Size, world.Size);
        for (int z = 0; z < 18; z++) roads.Row(z)[..100].Fill((byte)RoadType.Asphalt);
        for (int z = 18; z < 27; z++) roads.Row(z)[..100].Fill((byte)RoadType.Gravel);
        roads.Row(27).Fill((byte)RoadType.Water);

        var stats = Measure(world);
        Assert.Equal(0.1, stats.AsphaltKm);
        Assert.Equal(0.1, stats.GravelKm);
        Assert.Equal(0, stats.WaterPercent);
    }

    [Fact]
    public void Length_estimate_is_close_for_a_drawn_gravel_road()
    {
        var world = Blank(512);
        RoadOp.Draw(world, new RoadPath([new(100, 200.5f), new(400, 200.5f)], RoadType.Gravel, 4.5f, Grade: false),
            new EditSession("Draw road"));

        Assert.InRange(Measure(world).GravelKm, 0.29, 0.31);
    }

    [Fact]
    public void Counts_pois_by_tier_and_centre_biome_and_groups_street_tiles_into_towns()
    {
        var world = Blank(1024);
        world.Biomes = new IndexedLayer(new Grid<byte>(2, 2, [0, 1, 0, 1]), [0x004000, 0xFFE477]);
        var library = PrefabLibrary.Of(
        [
            new("store", "", new Int3(4, 8, 20), 0, 0, 3, [], null),
            new("house", "", new Int3(10, 10, 10), 0, 0, 2, [], null),
            new PrefabInfo("outpost", "", new Int3(10, 10, 10), 0, 0, 9, [], null)
                { TraderArea = true, HasDifficultyTier = false },
            new("lamp", "", new Int3(1, 4, 1), 0, 0, 0, ["part"], null),
            new("rwg_tile_residential_straight", "", new Int3(150, 20, 150), 0, 0, 0, [], null),
        ]);
        void Add(string name, int x, int z, int rotation = 0) =>
            world.Prefabs.Add(new PrefabInstance { Name = name, Position = new Int3(x, 0, z), Rotation = rotation });
        Add("store", -6, 0, rotation: 1);
        Add("house", -10, 0);
        Add("outpost", 0, 20);
        Add("lamp", 0, 0);
        Add("trader_missing", -30, 0);
        Add("rwg_tile_residential_straight", -450, -450);
        Add("RWG_TILE_UNKNOWN", -300, -449);
        Add("rwg_tile_residential_straight", -150, -299); // corner contact only
        world.Spawns.AddRange([new(), new()]);

        var stats = Measure(world, library);
        Assert.Equal(8, stats.Prefabs);
        Assert.Equal(4, stats.Pois);
        Assert.Equal(1, stats.PoisByTier[0]);
        Assert.Equal(1, stats.PoisByTier[2]);
        Assert.Equal(1, stats.PoisByTier[3]);
        Assert.Equal(1, stats.PoisWithUnknownTier);
        Assert.Equal(2, stats.PoisByBiome["pine_forest"]);
        Assert.Equal(2, stats.PoisByBiome["desert"]);
        Assert.Equal(3, stats.StreetTiles);
        Assert.Equal(2, stats.Towns);
        Assert.Equal(2, stats.Traders);
        Assert.Equal(2, stats.SpawnPoints);
    }

    [Fact]
    public void Empty_world_has_land_but_no_features()
    {
        var stats = Measure(Blank(8));
        Assert.Equal(100, stats.LandPercent);
        Assert.Equal(0, stats.WaterPercent);
        Assert.Equal(0, stats.MinHeightMetres);
        Assert.Equal(0, stats.MaxHeightMetres);
        Assert.Empty(stats.BiomeLandPercent);
        Assert.Empty(stats.PoisByTier);
        Assert.Empty(stats.PoisByBiome);
        Assert.Equal(0, stats.AsphaltKm);
        Assert.Equal(0, stats.GravelKm);
        Assert.Equal(0, stats.Pois);
        Assert.Equal(0, stats.StreetTiles);
        Assert.Equal(0, stats.Towns);
        Assert.Equal(0, stats.Traders);
        Assert.Equal(0, stats.SpawnPoints);
    }

    [Fact]
    public void Fully_flooded_world_has_no_land_share()
    {
        var world = Blank(2);
        world.Biomes = new IndexedLayer(new Grid<byte>(1, 1), [0x004000]);
        world.Water = new Grid<byte>(2, 2, [1, 1, 1, 1]);

        var stats = Measure(world);
        Assert.Equal(0, stats.LandPercent);
        Assert.Equal(100, stats.WaterPercent);
        Assert.Empty(stats.BiomeLandPercent);
    }

    [Fact]
    public void Snapshot_statistics_match_the_committed_world_before_later_edits()
    {
        var world = Blank(256);
        world.Water = new Grid<byte>(world.Size, world.Size);
        world.Roads = new Grid<byte>(world.Size, world.Size);
        world.Biomes = new IndexedLayer(new Grid<byte>(128, 128), [0x004000]);
        world.Prefabs.Add(new PrefabInstance { Name = "house", Position = new Int3(12, 0, 30) });
        world.Spawns.Add(new());
        world.Warnings.Add("Test warning");
        string expected = WorldSummaryFormatter.Format(Measure(world));
        using var snapshot = WorldSnapshot.Take(world);

        var runner = new EditRunner(new UndoStack());
        runner.Run(world, "Change world", edit =>
        {
            var cell = new GridRect(140, 158, 141, 159);
            edit.Touch(world.Height, cell);
            world.Height[140, 158] = World.MetresToHeight(5);
            edit.Touch(world.Water, cell);
            world.Water[140, 158] = 20;
            edit.Touch(world.Roads, cell);
            world.Roads[140, 158] = (byte)RoadType.Asphalt;
            edit.Touch(world.Biomes.Indices, new GridRect(70, 79, 71, 80), metresPerCell: 2);
            world.Biomes.Palette.Add(0xFFE477);
            world.Biomes.Indices[70, 79] = 1;
            edit.TouchList(world.Spawns, s => s.Clone(), cell);
            world.Spawns.Clear();
        });
        world.Name = "Edited";
        world.Warnings.Clear();

        var stats = WorldStatistics.Measure(snapshot, BiomeTable.Vanilla, PrefabLibrary.Empty, TestContext.Current.CancellationToken);
        Assert.Equal(expected, WorldSummaryFormatter.Format(stats));
        Assert.Equal(0, stats.AsphaltKm);
        Assert.Equal(1, stats.PoisByBiome["pine_forest"]);
        Assert.Equal(1, stats.SpawnPoints);
        Assert.Equal("Test warning", Assert.Single(stats.Warnings));
        Assert.True(snapshot.KeptBytes > 0);
    }
}
