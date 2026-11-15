using System.Xml.Linq;
using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Generate;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Tests;

public sealed class SpawnStageTests
{
    private const int Size = 3000;

    private static PrefabInfo Poi(string name, int size, int tier, params string[] tags) =>
        new(name, "POIs", new Int3(size, 20, size), -1, 0, tier, tags, null) { DuplicateRepeatDistance = 0 };

    private static readonly PrefabLibrary Library = PrefabLibrary.Of(
    [
        Poi("trader_rekt", 60, 0, "trader"),
        Poi("camp_01", 20, 0, "wilderness"),
        Poi("farm_01", 20, 3, "wilderness"),
    ]);

    /// <summary>Flat forest, the east third desert.</summary>
    private static World Land()
    {
        var world = new World { Name = "T", Size = Size, Height = new Grid<ushort>(Size, Size), Water = new Grid<byte>(Size, Size) };
        Array.Fill(world.Height.Data, World.MetresToHeight(40));
        var biomes = new Grid<byte>(Size / 4, Size / 4);
        for (int z = 0; z < biomes.Height; z++)
            for (int x = biomes.Width * 2 / 3; x < biomes.Width; x++)
                biomes[x, z] = 1;
        world.Biomes = new IndexedLayer(biomes, [BiomeLayout.ForestColor, BiomeLayout.DesertColor]);
        return world;
    }

    private static PrefabInstance Put(World world, string name, int gx, int gz)
    {
        var p = new PrefabInstance { Name = name, Position = new Int3(world.GridToWorld(gx), 40, world.GridToWorld(gz)) };
        world.Prefabs.Add(p);
        return p;
    }

    /// <summary>A stage 5 POI at (1810, 1510) with a gravel road east along z = 1510, a point every 10 m.</summary>
    private static WildernessPoi RoadFrom(World world, string prefab)
    {
        Put(world, prefab, 1810, 1500);
        var road = new HighwayRoad { Kind = HighwayKind.Country, Type = RoadType.Gravel, Radius = 2 };
        for (int x = 1810; x <= 2200; x += 10) road.Points.Add([x, 1510]);
        return new WildernessPoi { Name = prefab, Drawn = true, Road = road, RoadOrder = 1 };
    }

    private static SpawnResult Run(World world, List<PlacedSpawn> placed, IEnumerable<WildernessPoi> pois, SpawnOptions options, EditSession? session = null) =>
        SpawnStage.Run(world, options, placed, Library, BiomeTable.Vanilla, [], pois, session ?? new EditSession("Spawns", world));

    private static float Gap(World world) =>
        world.Spawns.SelectMany((a, i) => world.Spawns.Skip(i + 1).Select(b => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z))))
            .DefaultIfEmpty(float.MaxValue).Min();

    [Fact]
    public void Gravel_roads_get_spawns_in_easy_cells_near_a_trader_60_m_apart()
    {
        var world = Land();
        Put(world, "trader_rekt", 1500, 1500);
        var placed = new List<PlacedSpawn>();
        var result = Run(world, placed, [RoadFrom(world, "camp_01")], new SpawnOptions { Min = 0 });

        // Points 2 on in the camp's cell (x 1800 to 1949): 1830, 1890; past it the cells hold no prefabs.
        Assert.Equal(2, result.ByRoad);
        Assert.Equal(0, result.TopUp);
        Assert.All(world.Spawns, s => Assert.InRange(world.WorldToGrid((int)s.X), 1800, 1949));
        Assert.True(Gap(world) >= SpawnStage.Spacing);
        Assert.All(world.Spawns, s => Assert.Equal(SpawnHelper.StandingHeight(world, world.WorldToGrid((int)s.X), world.WorldToGrid((int)s.Z)), s.Y));
        Assert.Equal(2, placed.Count);
    }

    [Fact]
    public void A_hard_poi_far_trader_or_unticked_biome_gives_no_road_spawns()
    {
        var hard = Land();
        Put(hard, "trader_rekt", 1500, 1500);
        Assert.Equal(0, Run(hard, [], [RoadFrom(hard, "farm_01")], new SpawnOptions { Min = 0 }).ByRoad);

        var far = Land();
        Put(far, "trader_rekt", 200, 200);
        var road = RoadFrom(far, "camp_01");
        Assert.Equal(0, Run(far, [], [road], new SpawnOptions { Min = 0 }).ByRoad);
        Assert.Equal(2, Run(far, [], [road], new SpawnOptions { Min = 0, TraderDistance = 2500 }).ByRoad);

        var desert = Land();
        Put(desert, "trader_rekt", 1500, 1500);
        Assert.Equal(0, Run(desert, [], [RoadFrom(desert, "camp_01")], new SpawnOptions { Min = 0, Biomes = ["desert"] }).ByRoad);
    }

    [Fact]
    public void The_top_up_reaches_the_minimum_counting_hand_spawns_and_keeps_under_the_maximum()
    {
        var world = Land();
        Put(world, "trader_rekt", 1500, 1500);
        world.Spawns.Add(new SpawnPoint { X = 0, Y = 41, Z = 0 });
        var placed = new List<PlacedSpawn>();
        var result = Run(world, placed, [], new SpawnOptions());

        Assert.Equal(12, world.Spawns.Count);
        Assert.Equal(11, result.TopUp);
        Assert.Equal(1, result.Kept);
        Assert.True(Gap(world) >= SpawnStage.Spacing);
        // Forest cells only (west of 2000), the nearest the trader first.
        Assert.All(placed, p => Assert.True(world.WorldToGrid((int)p.X) < 2000));
        Assert.All(placed, p => Assert.True(Math.Abs(world.WorldToGrid((int)p.X) - 1530) < 500 && Math.Abs(world.WorldToGrid((int)p.Z) - 1530) < 500));

        // A re-run replaces only its own; the hand-placed one stays.
        Run(world, placed, [], new SpawnOptions { Min = 5 });
        Assert.Equal(5, world.Spawns.Count);
        Assert.Contains(world.Spawns, s => s is { X: 0, Z: 0 });

        Run(world, placed, [RoadFrom(world, "camp_01")], new SpawnOptions { Min = 12, Max = 2 });
        Assert.Equal(2, world.Spawns.Count);
    }

    [Fact]
    public void A_spawn_moved_by_hand_stays_on_take_down_and_undo_puts_everything_back()
    {
        var world = Land();
        Put(world, "trader_rekt", 1500, 1500);
        var placed = new List<PlacedSpawn>();
        Run(world, placed, [], new SpawnOptions());
        Assert.Equal(12, world.Spawns.Count);

        var moved = world.Spawns[3];
        SpawnHelper.Move(world, moved, 600, 600, new EditSession("Move", world));
        var before = world.Spawns.Select(s => (s.X, s.Y, s.Z, s.RotationY)).ToList();
        var session = new EditSession("Generate terrain", world);
        Assert.Equal(11, SpawnStage.TakeDown(world, placed, session));
        Assert.Same(moved, Assert.Single(world.Spawns));
        Assert.Empty(placed);

        session.Rollback();
        Assert.Equal(before, world.Spawns.Select(s => (s.X, s.Y, s.Z, s.RotationY)));
        Assert.Equal(12, placed.Count);
    }

    [Fact]
    public void Without_traders_the_top_up_starts_from_the_middle()
    {
        var world = Land();
        var result = Run(world, [], [], new SpawnOptions { Min = 3 });
        Assert.Equal(3, result.TopUp);
        Assert.Contains(result.Notes, n => n.Contains("no traders"));
        Assert.All(world.Spawns, s => Assert.True(MathF.Abs(s.X) < 300 && MathF.Abs(s.Z) < 300));
    }
}
