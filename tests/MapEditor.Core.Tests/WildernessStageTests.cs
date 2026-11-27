using System.Numerics;
using System.Xml.Linq;
using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Generate;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Tests;

public sealed class WildernessStageTests
{
    private const int Size = 4096;
    private const uint Forest = 0x004000, Desert = 0xFFE477;

    private static readonly RwgMixer Mixer = RwgMixer.Parse(XDocument.Parse("""
        <rwgmixer>
          <world name="t">
            <property name="world_size" value="0,99000"/>
            <property class="wilderness"><property name="count" value="3, 6, 9"/></property>
            <property class="forest_wilderness"><property name="count" value="4, 8, 12"/></property>
          </world>
        </rwgmixer>
        """), Size);

    private static readonly PrefabMarker Exit = new(new Int3(12, 0, 0), new Int3(6, 1, 4), MarkerType.RoadExit);

    private static PrefabInfo Poi(string name, int size, int tier, string[] tags, params PrefabMarker[] markers) =>
        new(name, "POIs", new Int3(size, 20, size), -1, 0, tier, tags, null) { Markers = markers, DuplicateRepeatDistance = 0 };

    private static readonly PrefabLibrary Library = PrefabLibrary.Of(
    [
        Poi("cabin_01", 30, 1, ["wilderness"], Exit),
        Poi("camp_01", 20, 0, ["wilderness"]),
        Poi("farm_01", 40, 2, ["wilderness"], Exit, new PrefabMarker(new Int3(5, 0, 5), new Int3(10, 1, 10), MarkerType.POISpawn, Tags: "shed")),
        Poi("shed_01", 10, 0, ["wilderness", "shed"]),
        Poi("house_01", 30, 1, ["residential"]),
        new PrefabInfo("rwg_tile_gateway_straight", "RWGTiles", new Int3(150, 22, 150), -12, 0, 0, ["streettile"], null),
    ]);

    /// <summary>Gentle hills, the west half forest and the east desert.</summary>
    private static World Land()
    {
        var world = new World { Name = "T", Size = Size, Height = new Grid<ushort>(Size, Size), Water = new Grid<byte>(Size, Size) };
        for (int z = 0; z < Size; z++)
            for (int x = 0; x < Size; x++)
                world.Height[x, z] = TerrainBrush.ToRaw(40 + 3 * MathF.Sin(x / 200f) * MathF.Cos(z / 170f));
        var biomes = new Grid<byte>(Size / 4, Size / 4);
        for (int z = 0; z < biomes.Height; z++)
            for (int x = biomes.Width / 2; x < biomes.Width; x++)
                biomes[x, z] = 1;
        world.Biomes = new IndexedLayer(biomes, [Forest, Desert]);
        return world;
    }

    private static void Highway(World world, float z)
    {
        var path = new RoadPath([new Vector2(100, z), new Vector2(Size - 100, z)], RoadType.Asphalt, 9);
        RoadOp.Draw(world, path, new EditSession("Road", world));
    }

    private static WildernessResult Run(World world, List<WildernessPoi> pois, WildernessOptions? options = null, EditSession? session = null,
        IReadOnlyList<GridRect>? keepClear = null) =>
        WildernessStage.Run(world, options ?? new WildernessOptions(), pois, Library, Mixer, BiomeTable.Vanilla, [],
            session ?? new EditSession("Wilderness", world), keepClear: keepClear);

    private static string? BiomeAt(World world, int gx, int gz) => TownGenerator.BiomeTagAt(world, BiomeTable.Vanilla, gx, gz);

    [Fact]
    public void Counts_come_from_the_biome_class_else_the_shared_one()
    {
        Assert.Equal((4, 8, 12), WildernessStage.Counts(Mixer, "forest"));
        Assert.Equal((3, 6, 9), WildernessStage.Counts(Mixer, "desert"));
        Assert.Equal(12, WildernessStage.CountFor(Mixer, new WildernessOptions { CountScale = 1.5f }, "forest"));
        Assert.Equal(9, WildernessStage.CountFor(Mixer, new WildernessOptions { Amount = 2 }, "snow"));
    }

    [Fact]
    public void Pois_go_on_flat_ground_of_one_biome_in_their_own_cells()
    {
        var world = Land();
        var pois = new List<WildernessPoi>();
        var result = Run(world, pois, new WildernessOptions { Roads = false });

        // 8 in the forest, 6 in the desert; the other biomes have no land.
        Assert.Equal(14, result.Added.Count);
        Assert.Equal(8, result.Added.Count(p => BiomeAt(world, world.WorldToGrid(p.X), world.WorldToGrid(p.Z)) == "forest"));
        Assert.Contains(result.Notes, n => n.Contains("snow"));
        Assert.DoesNotContain(pois.SelectMany(p => p.Prefabs), p => p.Name == "house_01");
        var cells = new HashSet<(int, int)>();
        foreach (var poi in pois)
        {
            var info = Library.Find(poi.Name)!;
            var f = PrefabPlacer.Footprint(world, info, poi.X, poi.Z, poi.Rotation);
            Assert.True(cells.Add((f.X0 / 150, f.Z0 / 150)), "two POIs in one cell");
            // Away from the map's edge row.
            Assert.InRange(f.X0, 150, Size - 150);
            // Flat at its own height under it, and standing on that ground.
            int ground = (int)MathF.Round(World.HeightToMetres(world.Height[f.X0, f.Z0]));
            Assert.Equal(ground, (int)MathF.Round(World.HeightToMetres(world.Height[f.X1 - 1, f.Z1 - 1])));
            Assert.Equal(PrefabPlacer.PrefabY(info, ground), poi.Prefabs[0].Y);
        }
        // The farm's lot gets a shed.
        if (pois.FirstOrDefault(p => p.Name == "farm_01") is { } farm)
            Assert.Contains(farm.Prefabs, p => p.Name == "shed_01");
        Assert.Equal(result.Prefabs, world.Prefabs.Count);
    }

    [Fact]
    public void Gravel_roads_run_from_road_exits_to_the_highway()
    {
        var world = Land();
        Highway(world, 2000);
        int asphalt = world.Roads!.Data.Count(r => r == (byte)RoadType.Asphalt);
        var pois = new List<WildernessPoi>();
        var result = Run(world, pois, new WildernessOptions { MaxRoad = 3000, Gap = 30 });

        var withExit = pois.Where(p => p.Name is "cabin_01" or "farm_01").ToList();
        Assert.NotEmpty(withExit);
        Assert.All(withExit, p => Assert.NotNull(p.Road));
        Assert.All(pois.Where(p => p.Name is "camp_01" or "shed_01"), p => Assert.Null(p.Road));
        Assert.All(withExit, p => Assert.Equal(RoadType.Gravel, p.Road!.Type));
        Assert.True(world.Roads.Data.Count(r => r == (byte)RoadType.Gravel) > 1000);
        // No POI on or within the gap of the highway, and the cells it crosses hold none.
        foreach (var poi in pois)
        {
            var f = PrefabPlacer.Footprint(world, Library.Find(poi.Name)!, poi.X, poi.Z, poi.Rotation);
            Assert.True(f.Z1 + 30 <= 2000 - 9 || f.Z0 - 30 >= 2000 + 9, $"{poi.Name} at {f.Z0}-{f.Z1}");
            Assert.NotEqual(2000 / 150, (f.Z0 + f.Z1) / 2 / 150);
        }

        // With a short limit, POIs far from it get no road and are told of.
        var near = Land();
        Highway(near, 2000);
        var few = new List<WildernessPoi>();
        var r2 = Run(near, few, new WildernessOptions { MaxRoad = 200 });
        Assert.Contains(r2.Notes, n => n.Contains("more than 200 m"));
        Assert.Contains(few, p => p.Name is "cabin_01" or "farm_01" && p.Road is null);
        Assert.True(asphalt > 0);
    }

    [Fact]
    public void Pois_and_gravel_roads_keep_off_a_drawn_bridges_deck_and_ramps()
    {
        // Where a POI with a road exit and the start of its gravel road go when the land is free...
        var options = new WildernessOptions { MaxRoad = 3000 };
        var first = Land();
        Highway(first, 2000);
        var firstPois = new List<WildernessPoi>();
        Run(first, firstPois, options);
        var poi = firstPois.First(p => p.Road is not null);
        var f = PrefabPlacer.Footprint(first, Library.Find(poi.Name)!, poi.X, poi.Z, poi.Rotation);
        var taken = new GridRect(f.X0 - 20, f.Z0 - 20, f.X1 + 20, f.Z1 + 20);
        Assert.Contains(first.Roads!.Data.Select((r, i) => (r, i)), c => c.r == (byte)RoadType.Gravel && Inside(taken, c.i));

        // ...is kept clear when a Bridge tool bridge stands there (taken down while the stage runs, as the app does) (#31).
        var world = Land();
        Highway(world, 2000);
        var pois = new List<WildernessPoi>();
        Run(world, pois, options, keepClear: [taken]);
        Assert.NotEmpty(pois);
        int gap = options.Gap;
        foreach (var p in pois)
            foreach (var placed in p.Prefabs)
            {
                var r = PrefabPlacer.Footprint(world, Library.Find(placed.Name)!, placed.X, placed.Z, placed.Rotation);
                Assert.False(r.X0 - gap < taken.X1 && r.X1 + gap > taken.X0 && r.Z0 - gap < taken.Z1 && r.Z1 + gap > taken.Z0,
                    $"{placed.Name} at {r} is within {gap} m of the drawn bridge at {taken}");
            }
        Assert.DoesNotContain(world.Roads!.Data.Select((r, i) => (r, i)), c => c.r == (byte)RoadType.Gravel && Inside(taken, c.i));

        static bool Inside(GridRect a, int i) => i % Size >= a.X0 && i % Size < a.X1 && i / Size >= a.Z0 && i / Size < a.Z1;
    }

    [Fact]
    public void A_road_between_a_town_and_a_poi_close_by_keeps_to_each_ones_ground_at_its_edge()
    {
        // A street tile at 46 m and, 23 m east of it (just past the gap), a POI at 40 m. The town tile comes first in
        // the list, as towns do; the held ground round each overlaps between them (#32).
        var world = Land();
        var tile = Library.Find("rwg_tile_gateway_straight")!;
        var poi = Library.Find("cabin_01")!;
        world.Prefabs.Add(new PrefabInstance { Name = tile.Name, Position = new Int3(world.GridToWorld(1000), PrefabPlacer.PrefabY(tile, 46), world.GridToWorld(1000)) });
        world.Prefabs.Add(new PrefabInstance { Name = poi.Name, Position = new Int3(world.GridToWorld(1173), PrefabPlacer.PrefabY(poi, 40), world.GridToWorld(1050)) });

        var grid = new HighwayStage.PathGrid(world, Library, new HighwayOptions());
        float Hold(int x, int z) => grid.Hold[grid.CellOf(new Vector2(x, z))];

        Assert.Equal(40, Hold(1180, 1065));
        Assert.Equal(46, Hold(1140, 1065));
        // The cell 8 m from the POI's edge keeps near the POI's ground, the one 5 m from the tile's near the tile's.
        float nearPoi = Hold(1165, 1065), nearTile = Hold(1155, 1065);
        Assert.InRange(nearPoi, 40, 43);
        Assert.InRange(nearTile, 44, 46);
        Assert.True(nearTile > nearPoi);
        // A road reads it as an even climb, not a step per 10 m cell: from the POI to the tile, under 1 m a metre.
        for (int x = 1185; x > 1140; x--)
            Assert.InRange(grid.HoldAt(new Vector2(x - 1, 1065)) - grid.HoldAt(new Vector2(x, 1065)), 0, 1);
    }

    [Fact]
    public void A_gravel_road_meets_a_highway_at_its_height_and_leaves_its_ground_alone()
    {
        // Flat ground at 38 m with a north-south highway, and a street tile at 42 m 15 m east of it. A gravel road comes
        // down past the tile's corner onto the highway, where the tile's held ground reaches.
        const int size = 1024;
        var world = new World { Name = "T", Size = size, Height = new Grid<ushort>(size, size), Water = new Grid<byte>(size, size) };
        world.Height.Data.AsSpan().Fill(TerrainBrush.ToRaw(38));
        RoadOp.Draw(world, new RoadPath([new Vector2(500, 50), new Vector2(500, 950)], RoadType.Asphalt, 5), new EditSession("Road", world));
        var tile = Library.Find("rwg_tile_gateway_straight")!;
        world.Prefabs.Add(new PrefabInstance { Name = tile.Name, Position = new Int3(world.GridToWorld(520), PrefabPlacer.PrefabY(tile, 42), world.GridToWorld(400)) });
        var asphalt = Enumerable.Range(0, size * size).Where(i => world.Roads!.Data[i] == (byte)RoadType.Asphalt).ToList();
        var before = asphalt.ToDictionary(i => i, i => world.Height.Data[i]);

        var grid = new HighwayStage.PathGrid(world, Library, new HighwayOptions());
        var path = new RoadPath([new Vector2(600, 620), new Vector2(560, 580), new Vector2(502, 540)], RoadType.Gravel, 3);
        float[]? profile = null;
        RoadOp.Draw(world, path, new EditSession("Gravel", world), (w, line) => profile = HighwayStage.RwgProfile(w, line, true, grid));

        // It ends on the highway at the highway's height, and the highway's ground is as it was (#34).
        Assert.InRange(profile![^1], 37.5f, 38.5f);
        Assert.All(asphalt, i => Assert.Equal(before[i], world.Height.Data[i]));
    }

    [Theory]
    [InlineData(40)]
    [InlineData(52)]
    public void Pois_keep_room_from_a_town_on_higher_ground_for_a_road_to_climb_between(int townGround)
    {
        // Street tiles on a checkerboard of stage 5's 150 m cells, so every free cell has a tile on each side. A POI
        // there is 20 m or more from them; tiles 12 m above the land need 4 x 12 + 6 = 54 m (#33).
        var world = Land();
        var tile = Library.Find("rwg_tile_gateway_straight")!;
        for (int z = 0; z + 150 <= Size; z += 150)
            for (int x = (z / 150 % 2) * 150; x + 150 <= Size; x += 300)
                world.Prefabs.Add(new PrefabInstance { Name = tile.Name, Position = new Int3(world.GridToWorld(x), PrefabPlacer.PrefabY(tile, townGround), world.GridToWorld(z)) });
        var tiles = world.Prefabs.Select(p => PrefabPlacer.Footprint(world, p, Library)).ToList();
        var pois = new List<WildernessPoi>();
        Run(world, pois, new WildernessOptions { Roads = false, CountScale = 6 });

        int checkedPois = 0;
        foreach (var p in pois)
        {
            var info = Library.Find(p.Name)!;
            var r = PrefabPlacer.Footprint(world, info, p.X, p.Z, p.Rotation);
            int ground = info.RoadLevelAbove(world.Prefabs.First(q => q.Name == p.Name && q.Position.X == p.X && q.Position.Z == p.Z).Position.Y);
            int need = Math.Max(20, 4 * Math.Abs(townGround - ground) + 6);
            foreach (var t in tiles)
            {
                int dx = Math.Max(0, Math.Max(t.X0 - r.X1, r.X0 - t.X1)), dz = Math.Max(0, Math.Max(t.Z0 - r.Z1, r.Z0 - t.Z1));
                Assert.True(Math.Max(dx, dz) >= need, $"{p.Name} at ground {ground} is {Math.Max(dx, dz)} m from a tile at {townGround} m (needs {need})");
            }
            checkedPois++;
        }
        // On level ground POIs still go 20 m from a town.
        if (townGround == 40) Assert.True(checkedPois > 0);
    }

    /// <summary>
    /// A 1 km world at 40 m with a highway along z = 200 and a plateau at 70 m north of z = 320, its edge a 1:1 slope.
    /// <paramref name="ramp"/>: east of x = 500 the plateau falls at 1:10 to the plain, so a flatter way down goes round.
    /// </summary>
    private static World Plateau(bool ramp)
    {
        const int size = 1024;
        var world = new World { Name = "T", Size = size, Height = new Grid<ushort>(size, size), Water = new Grid<byte>(size, size) };
        world.Biomes = new IndexedLayer(new Grid<byte>(size / 4, size / 4), [Forest]);
        for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++)
            {
                float top = !ramp || x < 500 ? 70 : Math.Max(40, 70 - (x - 500) / 10f);
                world.Height[x, z] = TerrainBrush.ToRaw(40 + (top - 40) * Math.Clamp((z - 290) / 30f, 0, 1));
            }
        RoadOp.Draw(world, new RoadPath([new Vector2(50, 200), new Vector2(size - 50, 200)], RoadType.Asphalt, 9), new EditSession("Road", world));
        return world;
    }

    /// <summary>The steepest grade of the ground along a road's centreline, over each 8 m (two centreline points).</summary>
    private static float SteepestGrade(World world, HighwayRoad road)
    {
        var line = RoadOp.Centreline([.. road.Points.Select(p => new Vector2(p[0], p[1]))], RoadOp.CornerRounding(road.Type));
        float Ground(Vector2 p) => World.HeightToMetres(world.Height[(int)p.X, (int)p.Y]);
        float steepest = 0;
        for (int i = 2; i < line.Count; i++)
            steepest = Math.Max(steepest, MathF.Abs(Ground(line[i]) - Ground(line[i - 2])) / Math.Max(1, Vector2.Distance(line[i], line[i - 2])));
        return steepest;
    }

    [Fact]
    public void A_driveway_off_a_steep_plateau_goes_round_by_the_flatter_way_and_keeps_to_a_drivable_grade()
    {
        // Straight down to the highway is a 30 m drop on a 1:1 slope; round by the ramp in the east it is far gentler (#332).
        var world = Plateau(ramp: true);
        var ground = (ushort[])world.Height.Data.Clone();
        var roads = (byte[])world.Roads!.Data.Clone();
        var pois = new List<WildernessPoi>();
        var notes = new List<string>();
        var poi = WildernessStage.PlaceByHand(world, Library.Find("cabin_01"), 300, 480, 0, new WildernessOptions { MaxRoad = 3000 }, pois,
            Library, Mixer, BiomeTable.Vanilla, 5, new EditSession("Place", world), notes);

        Assert.NotNull(poi?.Road);
        Assert.Empty(notes);
        // It reaches the highway, and nowhere along it is steeper than the cap (1 in 4), with some room for sampling whole metres.
        var end = poi.Road.Points[^1];
        Assert.InRange(end[1], 190, 210);
        float steepest = SteepestGrade(world, poi.Road);
        Assert.True(steepest <= 0.3f, $"The driveway climbs {steepest:0.00} m a metre");

        // Taking the stage down puts the ground and the roads back.
        WildernessStage.TakeDown(world, pois, new EditSession("Down", world));
        Assert.Equal(ground, world.Height.Data);
        Assert.Equal(roads, world.Roads.Data);
    }

    [Fact]
    public void The_grade_cap_cuts_and_fills_between_fixed_points_and_reports_ends_too_steep_for_it()
    {
        // 4 m apart: level at 10 m, a 6 m bank down into a 10 m wide dip at 4 m, and back up. The ends are held.
        var line = Enumerable.Range(0, 21).Select(i => new Vector2(4 * i, 0)).ToList();
        float[] heights = [10, 10, 10, 10, 10, 10, 10, 10, 4, 4, 4, 4, 10, 10, 10, 10, 10, 10, 10, 10, 10];
        var fixedAt = line.Select((_, i) => i == 0 || i == line.Count - 1).ToArray();
        var floor = new float[line.Count];
        var old = (float[])heights.Clone();

        Assert.True(HighwayStage.CapGrade(heights, line, fixedAt, floor, 0.25f));
        Assert.Equal(10, heights[0]);
        Assert.Equal(10, heights[^1]);
        for (int i = 1; i < line.Count; i++)
            Assert.InRange(MathF.Abs(heights[i] - heights[i - 1]), 0, 0.25f * 4 + 1e-4f);
        // The banks are cut down as far as the dip is filled up: the deepest change each way is the same.
        float cut = Enumerable.Range(0, line.Count).Max(i => old[i] - heights[i]);
        float fill = Enumerable.Range(0, line.Count).Max(i => heights[i] - old[i]);
        Assert.Equal(cut, fill, 0.01f);
        Assert.True(cut > 0);

        // Held ends 12 m apart in height over 20 m: no profile keeps to the cap, so the road climbs evenly between them.
        var shortLine = line.Take(6).ToList();
        float[] steep = [0, 0, 0, 0, 0, 12];
        Assert.False(HighwayStage.CapGrade(steep, shortLine, [true, false, false, false, false, true], new float[6], 0.25f));
        Assert.Equal([0, 2.4f, 4.8f, 7.2f, 9.6f, 12], steep.Select(h => MathF.Round(h, 2)));
    }

    [Fact]
    public void A_poi_on_a_mesa_with_no_drivable_way_down_gets_no_driveway_and_a_note()
    {
        // A plateau 200 m across with a 1:1 slope all round.
        var world = Plateau(ramp: false);
        for (int z = 0; z < world.Size; z++)
            for (int x = 0; x < world.Size; x++)
            {
                float d = Math.Max(Math.Abs(x - 300), Math.Abs(z - 480));
                world.Height[x, z] = TerrainBrush.ToRaw(Math.Clamp(170 - d, 40, 70));
            }
        var ground = (ushort[])world.Height.Data.Clone();
        var pois = new List<WildernessPoi>();
        var notes = new List<string>();
        var poi = WildernessStage.PlaceByHand(world, Library.Find("cabin_01"), 300, 480, 0, new WildernessOptions { MaxRoad = 3000 }, pois,
            Library, Mixer, BiomeTable.Vanilla, 5, new EditSession("Place", world), notes);

        Assert.NotNull(poi);
        Assert.Null(poi.Road);
        Assert.Contains(notes, n => n.Contains("1 POI has no way to a road that is not blocked or too steep"));
        WildernessStage.TakeDown(world, pois, new EditSession("Down", world));
        Assert.Equal(ground, world.Height.Data);
    }

    [Fact]
    public void A_rerun_takedown_and_undo_put_everything_back_exactly()
    {
        var world = Land();
        Highway(world, 2100);
        var ground = (ushort[])world.Height.Data.Clone();
        var roads = (byte[])world.Roads!.Data.Clone();
        var pois = new List<WildernessPoi>();
        var session = new EditSession("Wilderness", world);
        Run(world, pois, null, session);
        Assert.NotEqual(ground, world.Height.Data);
        Assert.NotEmpty(world.Prefabs);

        var step = session.ToStep();
        step.Swap();
        Assert.Equal(ground, world.Height.Data);
        Assert.Empty(world.Prefabs);
        Assert.Empty(pois);
        step.Swap();
        Assert.NotEmpty(pois);

        // A re-run with another seed takes the last run out first.
        Run(world, pois, new WildernessOptions { Seed = 9 });
        WildernessStage.TakeDown(world, pois, new EditSession("Down", world));
        Assert.Empty(pois);
        Assert.Empty(world.Prefabs);
        Assert.Equal(ground, world.Height.Data);
        Assert.Equal(roads, world.Roads.Data);
    }

    [Fact]
    public void Pinned_pois_stay_through_reruns_and_come_back_after_a_takedown()
    {
        var world = Land();
        var pois = new List<WildernessPoi>();
        var options = new WildernessOptions { Roads = false };
        var pinned = WildernessStage.PlaceByHand(world, Library.Find("camp_01"), 1000, 1000, 1, options, pois, Library, Mixer,
            BiomeTable.Vanilla, 5, new EditSession("Place", world));
        Assert.NotNull(pinned);
        Assert.True(pinned.Pinned && pinned.Drawn);
        Assert.Single(world.Prefabs);

        var result = Run(world, pois, options);
        Assert.Contains(pinned, pois);
        Assert.True(pinned.Drawn);
        Assert.Single(world.Prefabs, p => p.Name == "camp_01" && p.Position.X == pinned.X && p.Position.Z == pinned.Z);
        Assert.Equal(pois.Count, result.Added.Count + 1);

        WildernessStage.TakeDown(world, pois, new EditSession("Down", world));
        Assert.Equal([pinned], pois);
        Assert.False(pinned.Drawn);
        Assert.Empty(world.Prefabs);
        Run(world, pois, options);
        Assert.True(pinned.Drawn);

        // Remove takes it out for good.
        WildernessStage.Remove(world, pois, [pinned], new EditSession("Remove", world));
        Assert.DoesNotContain(pinned, pois);
        Assert.DoesNotContain(world.Prefabs, p => p.Name == "camp_01" && p.Position.X == pinned.X && p.Position.Z == pinned.Z);
    }

    [Fact]
    public void The_tier_slider_moves_the_mix()
    {
        double Tier(float bias)
        {
            var world = Land();
            var pois = new List<WildernessPoi>();
            Run(world, pois, new WildernessOptions { Roads = false, TierBias = bias, CountScale = 2 });
            return pois.Average(p => Library.Find(p.Name)!.DifficultyTier);
        }
        Assert.True(Tier(-1) < Tier(0));
        Assert.True(Tier(1) > Tier(0));
    }

    [Fact]
    public void Cells_of_towns_are_left_alone()
    {
        var world = Land();
        // A town tile in every forest cell but one column.
        for (int cz = 1; cz < Size / 150 - 1; cz++)
            for (int cx = 1; cx < 12; cx++)
                world.Prefabs.Add(new PrefabInstance { Name = "rwg_tile_gateway_straight", Position = new Int3(world.GridToWorld(cx * 150), 40, world.GridToWorld(cz * 150)), Rotation = 0 });
        int tiles = world.Prefabs.Count;
        var pois = new List<WildernessPoi>();
        Run(world, pois, new WildernessOptions { Roads = false, Gap = 0 });
        foreach (var poi in pois)
        {
            var f = PrefabPlacer.Footprint(world, Library.Find(poi.Name)!, poi.X, poi.Z, poi.Rotation);
            Assert.False(f.X0 < 12 * 150 && f.X1 > 150, $"{poi.Name} at {f.X0}");
        }
        Assert.Equal(tiles, world.Prefabs.Count(p => p.Name.StartsWith("rwg_tile_")));
    }
}
