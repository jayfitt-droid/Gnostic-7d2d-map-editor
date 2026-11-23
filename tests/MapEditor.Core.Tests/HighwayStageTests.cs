using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Generate;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Tests;

public sealed class HighwayStageTests
{
    private const int Size = 4096;

    private static readonly PrefabLibrary Library = PrefabLibrary.Of(MakeLibrary());

    private static IEnumerable<PrefabInfo> MakeLibrary()
    {
        foreach (var district in new[] { "downtown", "gateway", "oldwest" })
            foreach (var shape in TownGenerator.ShapeNames)
                yield return new PrefabInfo($"rwg_tile_{district}_{shape}", "RWGTiles", new Int3(150, 22, 150), -12, 0, 0, ["streettile"], null);
        yield return new PrefabInfo("rwg_tile_gateway_straight3a", "RWGTiles", new Int3(150, 22, 150), -12, 0, 0, ["streettile"], null);
        yield return new PrefabInfo("rwg_tile_test_block", "RWGTiles", new Int3(150, 22, 150), -12, 0, 0, ["streettile"], null);
    }

    private static World Flat(float metres = 40)
    {
        var world = new World { Name = "T", Size = Size, Height = new Grid<ushort>(Size, Size), Water = new Grid<byte>(Size, Size) };
        Array.Fill(world.Height.Data, TerrainBrush.ToRaw(metres));
        return world;
    }

    /// <summary>A street tile on the world's 150 m grid, turned so its shape has <paramref name="rotations"/> quarter turns.</summary>
    private static void Tile(World world, string name, int cx, int cz, int rotations)
    {
        var info = Library.Find(name)!;
        world.Prefabs.Add(new PrefabInstance
        {
            Name = name,
            Position = new Int3(world.GridToWorld(cx * 150), 40, world.GridToWorld(cz * 150)),
            Rotation = TownGenerator.TileRotation(info, rotations),
        });
    }

    /// <summary>A one-tile roadside town: a gateway with its street running north-south.</summary>
    private static void Roadside(World world, int cx, int cz) => Tile(world, "rwg_tile_gateway_straight", cx, cz, 0);

    private static void PlaceAtGround(World world, string name, int x, int z, int ground)
    {
        var info = Library.Find(name)!;
        world.Prefabs.Add(new PrefabInstance
        {
            Name = name,
            Position = new Int3(world.GridToWorld(x), PrefabPlacer.PrefabY(info, ground), world.GridToWorld(z)),
        });
    }

    private static HighwayResult Run(World world, HighwayOptions? options, List<HighwayRoad> roads, EditSession? session = null) =>
        HighwayStage.Run(world, options ?? new HighwayOptions(), roads, Library, RwgMixer.Empty, [], session ?? new EditSession("Highways", world));

    private static int Asphalt(World world) => world.Roads?.Data.Count(r => r == (byte)RoadType.Asphalt) ?? 0;

    [Fact]
    public void Towns_are_read_from_the_street_tiles_with_their_open_exits()
    {
        var world = Flat();
        // A centre crossroads with a gateway to its north; a lone east-west roadside tile further off.
        Tile(world, "rwg_tile_downtown_intersection", 10, 10, 0);
        Tile(world, "rwg_tile_gateway_straight3a", 10, 11, 0);
        Tile(world, "rwg_tile_gateway_straight", 20, 5, 1);
        // An Old West pair whose streets only face each other.
        Tile(world, "rwg_tile_oldwest_cap", 5, 20, 3);
        Tile(world, "rwg_tile_oldwest_cap", 6, 20, 1);

        var towns = HighwayStage.ReadTowns(world, Library, RwgMixer.Empty, []);
        Assert.Equal(3, towns.Count);
        var big = towns.Single(t => t.Tiles.Count == 2 && t.SpawnGateway);
        // The crossroads' east, south and west and the gateway's north.
        Assert.Equal(4, big.Exits.Count);
        Assert.Contains(big.Exits, e => e.Tile.Gateway && e.Dir == 0);
        var lone = towns.Single(t => t.Tiles.Count == 1);
        Assert.Equal([1, 3], lone.Exits.Select(e => e.Dir).Order());
        // The Old West town gets one exit, on an outer edge, though its streets don't leave town.
        var west = towns.Single(t => !t.SpawnGateway);
        Assert.Single(west.Exits);
        Assert.True(west.Exits[0].Dir is 0 or 2 || west.Exits[0].Tile.Rect.X0 == 5 * 150 && west.Exits[0].Dir == 3 || west.Exits[0].Tile.Rect.X0 == 6 * 150 && west.Exits[0].Dir == 1);
    }

    [Fact]
    public void Gateway_towns_are_joined_and_every_town_can_be_reached()
    {
        var world = Flat();
        Roadside(world, 6, 6);
        Roadside(world, 12, 8);
        Roadside(world, 20, 6);
        Roadside(world, 8, 20);
        var roads = new List<HighwayRoad>();
        var result = Run(world, null, roads);

        Assert.Empty(result.Notes);
        Assert.NotEmpty(roads);
        Assert.All(roads.Where(r => r.Kind != HighwayKind.Country), r => Assert.Equal(RoadType.Asphalt, r.Type));
        Assert.True(Asphalt(world) > 10_000);
        // Every town's tile has a road leading off one of its exits.
        foreach (var (cx, cz) in new[] { (6, 6), (12, 8), (20, 6), (8, 20) })
            Assert.Contains(roads, r => r.Points.Any(p => Math.Abs(p[0] - (cx * 150 + 75)) < 1 && (Math.Abs(p[1] - cz * 150) < 1 || Math.Abs(p[1] - (cz * 150 + 149)) < 1)));
    }

    [Fact]
    public void A_highway_leaves_room_for_its_full_width_beside_a_street_tile()
    {
        const int size = 1024;
        var world = new World { Name = "T", Size = size, Height = new Grid<ushort>(size, size) };
        ushort high = World.MetresToHeight(50), low = World.MetresToHeight(48);
        Array.Fill(world.Height.Data, high);

        PlaceAtGround(world, "rwg_tile_gateway_straight", 220, 110, 50);
        PlaceAtGround(world, "rwg_tile_downtown_straight", 300, 350, 50);
        PlaceAtGround(world, "rwg_tile_downtown_straight", 300, 500, 48);
        PlaceAtGround(world, "rwg_tile_gateway_straight", 220, 740, 50);
        for (int z = 500; z < 650; z++)
            world.Height.Row(z).Slice(300, 150).Fill(low);

        var result = Run(world, new HighwayOptions { Wiggle = 0, JoinUp = false }, []);
        Assert.Single(result.Added, r => r.Kind == HighwayKind.Highway);
        var roads = world.Roads!;
        // A road right beside the tile would lose one shoulder and part of its pavement.
        var roadRow = roads.Row(500)[..300].ToArray();
        Assert.Equal(16, roadRow.Count(r => r == (byte)RoadType.Asphalt));
        Assert.Equal(2, roadRow.Count(r => r == (byte)RoadType.Gravel));
        Assert.InRange(Array.FindLastIndex(roadRow, r => r != 0), 0, 289);
        Assert.Equal(((byte)RoadType.None, low, low), (roads[300, 500], world.Height[300, 500], world.Height[307, 500]));
        Assert.Equal((byte)RoadType.Asphalt, roads[295, 259]);
        Assert.Equal((byte)RoadType.Asphalt, roads[295, 740]);
    }

    [Fact]
    public void A_rounded_highway_leaves_clearance_around_a_street_tile_corner()
    {
        const int size = 1024;
        var world = new World { Name = "T", Size = size, Height = new Grid<ushort>(size, size) };
        Array.Fill(world.Height.Data, World.MetresToHeight(50));
        PlaceAtGround(world, "rwg_tile_gateway_straight", 220, 110, 50);
        PlaceAtGround(world, "rwg_tile_test_block", 300, 350, 50);
        PlaceAtGround(world, "rwg_tile_gateway_straight", 415, 740, 50);

        var result = Run(world, new HighwayOptions { Wiggle = 0, JoinUp = false }, []);
        Assert.Single(result.Added, r => r.Kind == HighwayKind.Highway);
        var roads = world.Roads!;
        Assert.Equal((byte)RoadType.Asphalt, roads[295, 259]);
        Assert.Equal((byte)RoadType.Asphalt, roads[490, 740]);
        // The grading needs a 10 m strip outside the tile free of road paint, including at the bend.
        for (int z = 340; z < 510; z++)
            Assert.True(roads.Row(z)[290..460].IndexOfAnyExcept((byte)RoadType.None) < 0, $"Road too close to tile at Z={z}");
    }

    [Fact]
    public void A_second_road_between_two_towns_is_off_unless_asked_for()
    {
        Assert.False(new HighwayOptions().SecondRoad);
        Assert.True(new HighwayOptions { SecondRoad = true }.Clone().SecondRoad);
    }

    [Fact]
    public void New_settings_wiggle_routes_15_percent_and_allow_120_m_causeways()
    {
        var options = new HighwayOptions();
        Assert.Equal(0.15f, options.Wiggle);
        Assert.Equal(120, options.CausewayMax);
    }

    [Fact]
    public void A_saved_wiggle_and_causeway_load_as_they_were_saved()
    {
        var saved = new HighwayOptions { Wiggle = 0.3f, CausewayMax = 60 };
        var json = System.Text.Json.JsonSerializer.Serialize(saved);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<HighwayOptions>(json)!;
        Assert.Equal(0.3f, loaded.Wiggle);
        Assert.Equal(60, loaded.CausewayMax);
    }

    [Fact]
    public void The_seed_bends_the_routes_and_the_same_seed_repeats()
    {
        List<float[]> Route(int seed)
        {
            var world = Flat();
            Roadside(world, 4, 4);
            Roadside(world, 22, 20);
            var roads = new List<HighwayRoad>();
            Run(world, new HighwayOptions { Seed = seed, JoinUp = true }, roads);
            return roads.SelectMany(r => r.Points).ToList();
        }
        var a = Route(1);
        var again = Route(1);
        int first = Enumerable.Range(0, Math.Min(a.Count, again.Count)).FirstOrDefault(i => !a[i].SequenceEqual(again[i]), -1);
        Assert.True(first < 0 && a.Count == again.Count, $"counts {a.Count} {again.Count}, first difference at {first}: " +
            (first >= 0 ? $"{a[first][0]},{a[first][1]} vs {again[first][0]},{again[first][1]}" : ""));
        Assert.NotEqual(a, Route(2), (p, q) => p.SequenceEqual(q));
    }

    [Fact]
    public void A_rerun_and_undo_put_the_ground_and_roads_back_exactly()
    {
        var world = Flat();
        // Rolling ground, so grading changes heights.
        for (int z = 0; z < Size; z++)
            for (int x = 0; x < Size; x++)
                world.Height[x, z] = TerrainBrush.ToRaw(40 + 6 * MathF.Sin(x / 300f) * MathF.Cos(z / 250f));
        Roadside(world, 5, 5);
        Roadside(world, 20, 18);
        Roadside(world, 6, 21);
        var ground = (ushort[])world.Height.Data.Clone();
        var roads = new List<HighwayRoad>();
        var session = new EditSession("Highways", world);
        Run(world, null, roads, session);
        Assert.NotEqual(ground, world.Height.Data);
        Assert.All(roads, r => Assert.NotEmpty(r.Ground));

        var step = session.ToStep();
        step.Swap();
        Assert.Equal(ground, world.Height.Data);
        Assert.Empty(roads);
        step.Swap();
        Assert.NotEmpty(roads);

        HighwayStage.TakeDown(world, roads, new EditSession("Down", world));
        Assert.Empty(roads);
        Assert.Equal(ground, world.Height.Data);
        Assert.Equal(0, Asphalt(world));
    }

    /// <summary>Two towns either side of a north-south strip of water <paramref name="width"/> m wide, the full height of the map.</summary>
    private static World Across(int width)
    {
        var world = Flat();
        int x0 = Size / 2 - width / 2;
        for (int z = 0; z < Size; z++)
            for (int x = x0; x < x0 + width; x++)
            {
                world.Height[x, z] = TerrainBrush.ToRaw(25);
                world.Water![x, z] = 30;
            }
        Tile(world, "rwg_tile_gateway_straight", 8, 13, 1);
        Tile(world, "rwg_tile_gateway_straight", 18, 13, 1);
        return world;
    }

    [Fact]
    public void A_highway_crosses_a_river_on_a_causeway_but_not_wide_water()
    {
        var river = Across(40);
        var roads = new List<HighwayRoad>();
        var result = Run(river, null, roads);
        Assert.Empty(result.Notes);
        Assert.NotEmpty(roads);
        // On the causeway the road stands at least 1 m over the water.
        int mid = Size / 2;
        var onRoad = Enumerable.Range(0, Size).Where(z => river.Roads![mid, z] == (byte)RoadType.Asphalt).ToList();
        Assert.NotEmpty(onRoad);
        Assert.All(onRoad, z => Assert.True(World.HeightToMetres(river.Height[mid, z]) >= 30.9f));

        var lake = Across(400);
        var none = new List<HighwayRoad>();
        var cut = Run(lake, null, none);
        Assert.Contains(cut.Notes, n => n.Contains("groups"));
        Assert.Equal(0, Enumerable.Range(0, Size).Count(z => lake.Roads?[Size / 2, z] is (byte)RoadType.Asphalt));
    }

    [Fact]
    public void A_town_without_gateways_gets_a_country_road_to_the_nearest_highway()
    {
        var world = Flat();
        Roadside(world, 5, 8);
        Roadside(world, 20, 8);
        Tile(world, "rwg_tile_oldwest_straight", 13, 16, 0);
        var roads = new List<HighwayRoad>();
        Run(world, new HighwayOptions { JoinUp = false }, roads);
        // One from each end of its street.
        var country = roads.Where(r => r.Kind == HighwayKind.Country).ToList();
        Assert.Equal(2, country.Count);
        Assert.All(country, r => Assert.Equal((RoadType.Gravel, HighwayStage.CountryRadius), (r.Type, r.Radius)));
    }
}
