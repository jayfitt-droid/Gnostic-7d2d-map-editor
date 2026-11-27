using System.IO.Compression;
using System.Numerics;
using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Generate;
using MapEditor.Core.IO;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;
using MapEditor.Core.Validate;

namespace MapEditor.Core.Tests;

/// <summary>The Bridge tool's logic: snapping, deck height, building with ramps, taking down, moving and the saved list.</summary>
public sealed class BridgeToolTests : IDisposable
{
    private const int Size = 1024;

    /// <summary>The sea in the test world, in grid metres along x.</summary>
    private const int SeaFrom = 400, SeaTo = 600;

    private static readonly PrefabLibrary Library = PrefabLibrary.Of(
    [
        new PrefabInfo(BridgeOp.Tile, "POIs", new Int3(14, 14, 9), -1, 0, 0, ["rwgonly"], null),
        new PrefabInfo(BridgeOp.End, "POIs", new Int3(14, 14, 9), -1, 0, 0, ["rwgonly"], null),
    ]);

    private static readonly BridgeKit Kit = BridgeKit.From(Library)!;

    private readonly string _dir = Directory.CreateTempSubdirectory("mapeditor-bridge-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>Land at 34 m with a strait of sea 12 m deep across x, and a little relief so ground changes are visible.</summary>
    private static World Strait()
    {
        var world = new World { Name = "T", Size = Size, Height = new Grid<ushort>(Size, Size), Water = new Grid<byte>(Size, Size), Roads = new Grid<byte>(Size, Size) };
        for (int z = 0; z < Size; z++)
            for (int x = 0; x < Size; x++)
            {
                bool sea = x >= SeaFrom && x < SeaTo;
                world.Height[x, z] = TerrainBrush.ToRaw(sea ? 18 + (x + z) % 3 : 34 + (x * 7 + z * 3) % 5 * 0.2f);
                if (sea) world.Water[x, z] = StampOp.DefaultWaterLevel;
            }
        return world;
    }

    private sealed record Copy(ushort[] Height, byte[] Roads, byte[] Water, List<(string, int, int, int, int)> Prefabs);

    private static Copy Take(World world) => new(
        (ushort[])world.Height.Data.Clone(), (byte[])world.Roads!.Data.Clone(), (byte[])world.Water!.Data.Clone(),
        [.. world.Prefabs.Select(p => (p.Name, p.Position.X, p.Position.Y, p.Position.Z, p.Rotation))]);

    private static void AssertSame(Copy before, World world)
    {
        Assert.Equal(before.Height, world.Height.Data);
        Assert.Equal(before.Roads, world.Roads!.Data);
        Assert.Equal(before.Water, world.Water!.Data);
        Assert.Equal(before.Prefabs.Order(), world.Prefabs.Select(p => (p.Name, p.Position.X, p.Position.Y, p.Position.Z, p.Rotation)).Order());
    }

    // ---- planning ----

    [Fact]
    public void A_drag_locks_to_the_nearer_axis_and_whole_pieces()
    {
        // Mostly east: east-west, through the start click's z, 5 pieces for 47 m.
        var east = BridgeTool.Snap(new Vector2(100, 200), new Vector2(147, 230), Size)!.Value;
        Assert.Equal((false, 200, 100, 5), (east.AlongZ, east.Line, east.Start, east.Pieces));
        Assert.Equal(45, east.Length);

        // Mostly north: north-south, through the start click's x.
        var north = BridgeTool.Snap(new Vector2(100, 200), new Vector2(130, 300), Size)!.Value;
        Assert.Equal((true, 100, 200, 11), (north.AlongZ, north.Line, north.Start, north.Pieces));

        // Dragging toward lower coordinates keeps the start click where it is.
        var west = BridgeTool.Snap(new Vector2(300, 200), new Vector2(255, 205), Size)!.Value;
        Assert.Equal((false, 200, 255, 5), (west.AlongZ, west.Line, west.Start, west.Pieces));
        Assert.Equal(300, west.End);

        // Equal drags go north-south.
        Assert.True(BridgeTool.Snap(new Vector2(100, 100), new Vector2(150, 150), Size)!.Value.AlongZ);
    }

    [Fact]
    public void A_bridge_is_at_least_two_pieces_and_stays_inside_the_world()
    {
        var tiny = BridgeTool.Snap(new Vector2(100, 200), new Vector2(103, 200), Size)!.Value;
        Assert.Equal(2, tiny.Pieces);
        Assert.Equal(18, tiny.Length);

        // Too near the east edge for two pieces: no bridge. Near it, the length is cut to fit.
        Assert.Null(BridgeTool.Snap(new Vector2(Size - 10, 200), new Vector2(Size - 2, 200), Size));
        var cut = BridgeTool.Snap(new Vector2(Size - 40, 200), new Vector2(Size - 2, 200), Size)!.Value;
        Assert.True(cut.End <= Size);
        Assert.Equal(4, cut.Pieces);

        // Dragging an end keeps the other end and whole pieces; moving keeps the length.
        var b = new BridgeLine(false, 200, 100, 4);
        var longer = BridgeTool.DragEnd(b, startEnd: false, 160, Size)!.Value;
        Assert.Equal((100, 7), (longer.Start, longer.Pieces));
        var earlier = BridgeTool.DragEnd(b, startEnd: true, 64, Size)!.Value;
        Assert.Equal((64, 8), (earlier.Start, earlier.Pieces));
        Assert.Equal(b.End, earlier.End);
        Assert.Equal(2, BridgeTool.DragEnd(b, startEnd: false, 90, Size)!.Value.Pieces);
        Assert.Equal(0, BridgeTool.Move(b, -500, Size).Start);
        Assert.Equal(Size - b.Length, BridgeTool.Move(b, 5000, Size).Start);
    }

    // ---- deck height ----

    [Fact]
    public void Over_water_the_beams_clear_the_sea_as_the_highway_stage_has_them()
    {
        var world = Strait();
        var line = BridgeTool.Snap(new Vector2(SeaFrom - 12, 300), new Vector2(SeaTo + 12, 300), Size)!.Value;
        int y = BridgeTool.AutoY(world, line);
        Assert.Equal(StampOp.DefaultWaterLevel + BridgeOp.Clearance, y + BridgeOp.Beams);
    }

    [Fact]
    public void Over_water_the_deck_is_never_below_the_banks()
    {
        var world = Strait();
        // Banks 20 m above the sea: the deck meets them.
        foreach (int x in new[] { SeaFrom - 10, SeaTo + 7 })
            for (int z = 0; z < Size; z++) world.Height[x, z] = TerrainBrush.ToRaw(50);
        var line = new BridgeLine(false, 300, SeaFrom - 9, 24);
        Assert.Equal(50, BridgeTool.AutoY(world, line) + BridgeOp.Deck);
    }

    [Fact]
    public void Over_land_the_deck_is_level_with_the_higher_end()
    {
        var world = Strait();
        // A valley between a 40 m hill to the west and a 36 m hill to the east, on dry land.
        for (int z = 0; z < Size; z++)
            for (int x = 0; x < Size; x++)
                if (x is >= 100 and < 200) world.Height[x, z] = TerrainBrush.ToRaw(x < 120 ? 40 : x >= 180 ? 36 : 20);
        // Its west end stands on the 40 m hill and its east end on the valley's far side at 36 m: the higher end sets the deck.
        Assert.Equal(40 - BridgeOp.Deck, BridgeTool.AutoY(world, new BridgeLine(false, 300, 117, 7)));
        // Start the bridge in the valley and the higher end is the 36 m one.
        Assert.Equal(36 - BridgeOp.Deck, BridgeTool.AutoY(world, new BridgeLine(false, 300, 140, 5)));
    }

    // ---- building and taking down ----

    private static PlacedBridge BuildAcrossSea(World world, List<PlacedBridge> list, EditSession session, int z = 300)
    {
        var line = BridgeTool.Snap(new Vector2(SeaFrom - 12, z), new Vector2(SeaTo + 12, z), Size)!.Value;
        return BridgeTool.Add(world, Kit, list, line, BridgeTool.AutoY(world, line), session);
    }

    [Fact]
    public void A_built_bridge_has_its_pieces_footings_and_a_ramp_to_each_end()
    {
        var world = Strait();
        var list = new List<PlacedBridge>();
        var bridge = BuildAcrossSea(world, list, new EditSession("Bridge", world));

        Assert.Same(bridge, Assert.Single(list));
        var pieces = world.Prefabs.OrderBy(p => p.Position.X).ToList();
        Assert.Equal(bridge.Pieces, pieces.Count);
        Assert.Equal(bridge.Prefabs.Count, pieces.Count);
        Assert.Equal((BridgeOp.End, 3), (pieces[0].Name, pieces[0].Rotation));
        Assert.Equal((BridgeOp.End, 1), (pieces[^1].Name, pieces[^1].Rotation));
        Assert.All(pieces, p => Assert.Equal(bridge.Y, p.Position.Y));
        Assert.Equal(3, bridge.Grounds.Count); // footings and two ramps

        // A ramp road runs out of each end at the deck's height, and the ground it leaves is graded to meet it.
        int west = bridge.Start, east = bridge.Start + bridge.Pieces * BridgeOp.Piece;
        Assert.NotEqual(0, world.Roads![west - 6, 300]);
        Assert.NotEqual(0, world.Roads[east + 6, 300]);
        Assert.Equal(bridge.DeckLevel, World.HeightToMetres(world.Height[west - 2, 300]), 0.6);
        Assert.Equal(bridge.DeckLevel, World.HeightToMetres(world.Height[east + 2, 300]), 0.6);
        // ...and eases back to the land beyond.
        Assert.True(World.HeightToMetres(world.Height[west - 130, 300]) < bridge.DeckLevel);
    }

    [Fact]
    public void A_bridge_edits_bounds_cover_its_ramps_and_footings_so_the_map_redraws_them()
    {
        var world = Strait();
        var heightBefore = Cells(world.Height);
        var session = new EditSession("Bridge", world);
        BuildAcrossSea(world, [], session);

        Assert.NotNull(world.Roads);
        var changed = new List<(int X, int Z)>();
        for (int z = 0; z < world.Height.Height; z++)
            for (int x = 0; x < world.Height.Width; x++)
                if (world.Height[x, z] != heightBefore[x, z] || world.Roads[x, z] != 0)
                    changed.Add((x, z));
        Assert.NotEmpty(changed);
        var b = session.Bounds;
        Assert.All(changed, c => Assert.True(c.X >= b.X0 && c.X < b.X1 && c.Z >= b.Z0 && c.Z < b.Z1, $"{c} is outside {b}"));
    }

    private static ushort[,] Cells(Grid<ushort> grid)
    {
        var copy = new ushort[grid.Width, grid.Height];
        for (int z = 0; z < grid.Height; z++)
            for (int x = 0; x < grid.Width; x++)
                copy[x, z] = grid[x, z];
        return copy;
    }

    [Fact]
    public void Taking_a_bridge_down_puts_back_the_ground_exactly()
    {
        var world = Strait();
        var before = Take(world);
        var list = new List<PlacedBridge>();
        var session = new EditSession("Bridge", world);
        var bridge = BuildAcrossSea(world, list, session);
        Assert.NotEqual(before.Height, world.Height.Data);
        Assert.NotEqual(before.Roads, world.Roads!.Data);

        BridgeTool.Remove(world, list, bridge, new EditSession("Take down", world));
        Assert.Empty(list);
        AssertSame(before, world);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Rebuilding_a_bridge_keeps_its_join_with_a_drawn_road_smooth(int dip)
    {
        const int z = 300;
        var world = Strait();
        for (int x = 0; x < Size; x++)
        {
            int d = Math.Min(Math.Abs(x - SeaFrom), Math.Abs(x - SeaTo));
            float shore = (d - 40) / 15f;
            ushort height = TerrainBrush.ToRaw(x is >= SeaFrom and < SeaTo
                ? 18 : 34 + 0.04f * d - dip * MathF.Exp(-shore * shore));
            world.Height.BeforeChange(new GridRect(x, 0, x + 1, Size));
            for (int row = 0; row < Size; row++) world.Height[x, row] = height;
        }

        var list = new List<PlacedBridge>();
        var bridge = BuildAcrossSea(world, list, new EditSession("Bridge", world), z);
        Assert.NotNull(bridge.StartRampLength);
        var end = RoadEnds.Find(world.Roads!, new Vector2(bridge.Start - bridge.StartRampLength.Value, z), 20);
        Assert.NotNull(end);
        var road = new RoadPath([end.Point, new Vector2(160, z)], RoadType.Asphalt, 6);
        RoadOp.Draw(world, road, new EditSession("Road", world), library: Library);

        float WorstStep() => Enumerable.Range(151, 386 - 150)
            .Max(x => World.HeightToMetres((ushort)Math.Abs(world.Height[x, z] - world.Height[x - 1, z])));

        float before = WorstStep();
        Assert.Equal((1, 0), BridgeTool.RebuildEvery(world, Kit, list, new EditSession("Rebuild", world)));
        float after = WorstStep();
        Assert.True(after <= before + 0.05f, $"Worst step before rebuild: {before:F2} m. After: {after:F2} m.");
    }

    [Fact]
    public void Taking_a_bridge_down_keeps_later_height_and_road_changes()
    {
        const int z = 300;
        var world = Strait();
        var roads = world.Roads!;
        var expected = Take(world);
        var list = new List<PlacedBridge>();
        var bridge = BuildAcrossSea(world, list, new EditSession("Bridge", world), z);
        Assert.NotNull(bridge.StartRampLength);
        int length = (int)bridge.StartRampLength.Value;
        var cells = Enumerable.Range(bridge.Start - length, length)
            .Where(x => roads[x, z] == (byte)RoadType.Asphalt && world.Height[x, z] != expected.Height[x + z * Size])
            .Take(2).ToArray();
        Assert.Equal(2, cells.Length);
        int regraded = cells[0], repainted = cells[1];

        world.Height.BeforeChange(new GridRect(regraded, z, regraded + 1, z + 1));
        roads.BeforeChange(new GridRect(repainted, z, repainted + 1, z + 1));
        world.Height[regraded, z] = 10000;
        roads[repainted, z] = (byte)RoadType.Gravel;
        expected.Height[regraded + z * Size] = world.Height[regraded, z];
        expected.Roads[regraded + z * Size] = (byte)RoadType.Asphalt;
        expected.Roads[repainted + z * Size] = (byte)RoadType.Gravel;

        BridgeTool.Remove(world, list, bridge, new EditSession("Take down", world));
        Assert.Empty(list);
        AssertSame(expected, world);
    }

    [Fact]
    public void Legacy_ground_records_still_restore_deltas_and_road_bytes()
    {
        var world = Strait();
        world.Height.BeforeChange(new GridRect(10, 20, 12, 21));
        world.Roads!.BeforeChange(new GridRect(10, 20, 11, 21));
        world.Height[10, 20] = 2000;
        world.Height[11, 20] = 40000;
        world.Roads![10, 20] = (byte)RoadType.Asphalt;

        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(new DeflateStream(bytes, CompressionLevel.Optimal, leaveOpen: true)))
        {
            void Run(byte kind, int x)
            {
                writer.Write(kind);
                writer.Write(20);
                writer.Write(x);
                writer.Write(1);
            }

            Run(1, 10);
            writer.Write((short)150);
            Run(3, 11);
            writer.Write(-35000);
            Run(2, 10);
            writer.Write((byte)RoadType.Gravel);
            writer.Write((byte)0);
        }

        var ground = bytes.ToArray();
        Assert.Equal((20, 10, 1), Assert.Single(HighwayStage.RoadRuns(ground)));
        HighwayStage.Restore(world, ground, new EditSession("Restore old record", world));
        Assert.Equal(2150, world.Height[10, 20]);
        Assert.Equal(5000, world.Height[11, 20]);
        Assert.Equal((byte)RoadType.Gravel, world.Roads[10, 20]);
    }

    [Fact]
    public void A_north_south_bridge_over_land_builds_and_comes_down_cleanly()
    {
        var world = Strait();
        var before = Take(world);
        var list = new List<PlacedBridge>();
        var line = BridgeTool.Snap(new Vector2(200, 100), new Vector2(200, 190), Size)!.Value;
        Assert.True(line.AlongZ);
        var bridge = BridgeTool.Add(world, Kit, list, line, BridgeTool.AutoY(world, line), new EditSession("Bridge", world));
        Assert.Equal((BridgeOp.End, 0), (world.Prefabs.OrderBy(p => p.Position.Z).First().Name, world.Prefabs.OrderBy(p => p.Position.Z).First().Rotation));
        BridgeTool.Remove(world, list, bridge, new EditSession("Take down", world));
        AssertSame(before, world);
    }

    [Fact]
    public void A_bridge_at_the_edge_of_the_world_has_no_ramp_off_it()
    {
        var world = Strait();
        var before = Take(world);
        var list = new List<PlacedBridge>();
        var line = new BridgeLine(false, 300, 0, 3);
        var bridge = BridgeTool.Add(world, Kit, list, line, 20, new EditSession("Bridge", world));
        Assert.Equal(2, bridge.Grounds.Count);
        BridgeTool.Remove(world, list, bridge, new EditSession("Take down", world));
        AssertSame(before, world);
    }

    private static readonly PrefabInfo HillTile =
        new("rwg_tile_rural_straight", "RWGTiles", new Int3(150, 22, 150), -12, 0, 0, ["streettile"], null);

    /// <summary>The bridge pieces and a street tile, so ramps can find the tile in their way.</summary>
    private static readonly PrefabLibrary TownLibrary = PrefabLibrary.Of([.. Library.All, HillTile]);

    /// <summary>
    /// The strait, with the east bank rising over 30 m from x = 640 to <paramref name="ground"/> at <paramref name="west"/>,
    /// where a street tile 150 m wide stands across z = 300 on that ground. Returns its footprint.
    /// </summary>
    private static (World World, GridRect Cells) TownOnHill(int west, int ground)
    {
        var world = Strait();
        for (int z = 0; z < Size; z++)
            for (int x = 640; x < Size; x++)
                world.Height[x, z] = TerrainBrush.ToRaw(x >= west ? ground : x >= west - 30 ? ground + (34f - ground) * (west - x) / 30f : 34);
        int y = ground + 1 + HillTile.YOffset;
        Assert.Equal(ground, HillTile.RoadLevelAbove(y));
        world.Prefabs.Add(new PrefabInstance { Name = HillTile.Name, Position = new Int3(world.GridToWorld(west), y, world.GridToWorld(225)) });
        var cells = PrefabPlacer.Footprint(world, world.Prefabs[0], TownLibrary);
        Assert.Equal(new GridRect(west, 225, west + 150, 375), cells);
        Assert.Equal(0, PrefabPlacer.RoadMismatch(world, cells, ground));
        return (world, cells);
    }

    private static PlacedBridge BuildToTown(World world, BridgeKit kit)
    {
        var line = BridgeTool.Snap(new Vector2(SeaFrom - 12, 300), new Vector2(SeaTo + 12, 300), Size)!.Value;
        return BridgeTool.Add(world, kit, [], line, BridgeTool.AutoY(world, line), new EditSession("Bridge", world));
    }

    private static List<string> ApproachWarnings(World world) =>
        [.. BridgeChecks.Analyze(world, TownLibrary).Issues.Where(i => i.Kind == BridgeIssue.Approach).Select(i => i.Message)];

    private static BridgePlan PlanAcrossSea(World world, BridgeKit kit)
    {
        var line = BridgeTool.Snap(new Vector2(SeaFrom - 12, 300), new Vector2(SeaTo + 12, 300), Size)!.Value;
        return new BridgePlan(kit, line.AlongZ, line.Line, line.Start, line.Pieces, BridgeTool.AutoY(world, line));
    }

    [Fact]
    public void A_planned_ramp_reaches_the_ground_where_the_built_ramp_ends_and_planning_changes_nothing()
    {
        var world = Strait();
        var plan = PlanAcrossSea(world, Kit);
        var before = Take(world);

        var west = BridgeTool.PlanRamp(world, plan, atStart: true)!;
        var east = BridgeTool.PlanRamp(world, plan, atStart: false)!;
        AssertSame(before, world);

        foreach (var ramp in new[] { west, east })
        {
            Assert.Equal(RampEnd.Ground, ramp.Ends);
            Assert.InRange(ramp.Length, BridgeTool.MinRamp, BridgeTool.MaxRamp);
            Assert.InRange(ramp.Grade, 0, BridgeTool.RampGrade + 0.005f);
            Assert.False(ramp.Steep);
            Assert.Null(ramp.Prefab);
        }
        Assert.Equal(plan.Start - west.Length, west.To.X);
        Assert.Equal(plan.End + east.Length, east.To.X);

        // The built ramps are paved up to those ends, plus the road's rounded end (half its width), and no further.
        BridgeTool.Add(world, Kit, [], new BridgeLine(plan.AlongZ, plan.Line, plan.Start, plan.Pieces), plan.Y, new EditSession("Bridge", world));
        int eastPaved = (int)east.To.X, westPaved = (int)west.To.X;
        while (world.Roads![eastPaved + 1, 300] != 0) eastPaved++;
        while (world.Roads[westPaved - 1, 300] != 0) westPaved--;
        float cap = BridgeOp.Width / 2f + 1;
        Assert.InRange(eastPaved, east.To.X, east.To.X + cap);
        Assert.InRange(westPaved, west.To.X - cap, west.To.X);
    }

    [Fact]
    public void A_planned_ramp_toward_a_town_stops_at_its_street_tile_and_says_how_steep()
    {
        const int TileWest = 670, TileGround = 50;
        var (world, _) = TownOnHill(TileWest, TileGround);
        var plan = PlanAcrossSea(world, BridgeKit.From(TownLibrary)!);

        var east = BridgeTool.PlanRamp(world, plan, atStart: false)!;
        Assert.Equal(RampEnd.Prefab, east.Ends);
        Assert.Equal(HillTile.Name, east.Prefab);
        Assert.Equal(TileWest, east.To.X);
        Assert.True(east.Steep);   // 16 m up in 57 m
        Assert.Equal((TileGround - plan.DeckTop) / (float)(TileWest - plan.End - PrefabPlacer.RoadTouch), east.Grade, 0.001f);

        // The west ramp is on the other side of the sea, clear of the town.
        Assert.Equal(RampEnd.Ground, BridgeTool.PlanRamp(world, plan, atStart: true)!.Ends);
    }

    [Fact]
    public void A_ramp_toward_a_town_on_a_hill_meets_its_street_tile_level()
    {
        // A street tile on a hill 16 m above the east bank, its west edge 57 m past the bridge's east end. The 6% ramp
        // never reaches that ground, so it used to run its full 200 m, straight through the tile, graded to its own
        // profile instead of the tile's ground ("doesn't meet the road beside it level" in Validate).
        const int TileWest = 670, TileGround = 50;
        var (world, cells) = TownOnHill(TileWest, TileGround);
        var bridge = BuildToTown(world, BridgeKit.From(TownLibrary)!);

        Assert.InRange(PrefabPlacer.RoadMismatch(world, cells, TileGround), 0, PrefabPlacer.RoadStep);
        // The ramp stops at the tile's edge rather than paving through it, and still leaves the deck at its height.
        Assert.NotEqual(0, world.Roads![TileWest - 2, 300]);
        Assert.Equal(0, world.Roads[TileWest + 20, 300]);
        Assert.Equal(TileGround, World.HeightToMetres(world.Height[TileWest - 2, 300]), 0.6);
        Assert.Equal(bridge.DeckLevel, World.HeightToMetres(world.Height[bridge.Where.End + 2, 300]), 0.6);
    }

    [Fact]
    public void A_ramp_that_stops_at_a_town_climbs_at_an_even_grade()
    {
        // The same town 16 m up, 57 m on: the climb is spread evenly over the run, not packed into its middle, so the
        // steepest 5 m (as Validate measures it) is close to the average from the deck to the town.
        const int TileWest = 670, TileGround = 50;
        var (world, _) = TownOnHill(TileWest, TileGround);
        var bridge = BuildToTown(world, BridgeKit.From(TownLibrary)!);
        int end = bridge.Where.End;
        float Height(int x) => World.HeightToMetres(world.Height[x, 300]);

        float average = (TileGround - bridge.DeckLevel) / (float)(TileWest - end);
        float steepest = 0;
        for (int x = end; x + BridgeChecks.GradeRun <= TileWest; x++)
            steepest = Math.Max(steepest, MathF.Abs(Height(x + BridgeChecks.GradeRun) - Height(x)) / BridgeChecks.GradeRun);
        Assert.InRange(steepest, average, average * 1.2f);
        // No kink where it leaves the deck: 2 m out it has climbed less than the even grade alone would take it.
        Assert.InRange(Height(end + 2) - bridge.DeckLevel, -0.1f, 2 * average);
    }

    [Fact]
    public void A_town_too_high_to_reach_is_reported_at_the_bridge()
    {
        // A town 26 m up, 57 m on: no even grade gets there under 25%. Validate says so at the bridge, rather than at the town.
        var (world, cells) = TownOnHill(670, 60);
        BuildToTown(world, BridgeKit.From(TownLibrary)!);

        Assert.InRange(PrefabPlacer.RoadMismatch(world, cells, 60), 0, PrefabPlacer.RoadStep);
        Assert.Contains(ApproachWarnings(world), m => m.Contains("east approach climbs"));
    }

    [Fact]
    public void A_ramp_with_room_to_reach_a_town_is_graded_within_the_warning_line()
    {
        // The same town 6 m up, 150 m on: the ramp still ends level with it, gently enough to pass Validate.
        const int TileWest = 763, TileGround = 40;
        var (world, cells) = TownOnHill(TileWest, TileGround);
        BuildToTown(world, BridgeKit.From(TownLibrary)!);

        Assert.InRange(PrefabPlacer.RoadMismatch(world, cells, TileGround), 0, PrefabPlacer.RoadStep);
        Assert.Equal(0, world.Roads![TileWest + 20, 300]);
        Assert.Empty(ApproachWarnings(world));
    }

    [Fact]
    public void A_ramp_with_no_prefab_in_its_way_is_built_as_before()
    {
        // A town well off to the side changes nothing: the ramps are those of a kit that can't see prefabs at all.
        World Build(BridgeKit kit)
        {
            var world = Strait();
            world.Prefabs.Add(new PrefabInstance { Name = HillTile.Name, Position = new Int3(world.GridToWorld(700), 40, world.GridToWorld(600)) });
            BridgeTool.Add(world, kit, [], BridgeTool.Snap(new Vector2(SeaFrom - 12, 300), new Vector2(SeaTo + 12, 300), Size)!.Value, 25,
                new EditSession("Bridge", world));
            return world;
        }
        var seeing = Build(BridgeKit.From(TownLibrary)!);
        var blind = Build(BridgeKit.From(TownLibrary)! with { Library = null });
        Assert.Equal(blind.Height.Data, seeing.Height.Data);
        Assert.Equal(blind.Roads!.Data, seeing.Roads!.Data);
        // ...and those ramps are the full RampLength ones, reaching well out from each end.
        Assert.NotEqual(0, seeing.Roads[SeaTo + 12 + 40, 300]);
    }

    [Fact]
    public void Building_is_one_undo_step_and_undo_takes_it_back()
    {
        var world = Strait();
        var before = Take(world);
        var list = new List<PlacedBridge>();
        var undo = new UndoStack();
        var edits = new EditRunner(undo);

        Assert.True(edits.Run(world, "Bridge", s => BuildAcrossSea(world, list, s)));
        Assert.Equal("Bridge", undo.UndoName);
        Assert.Single(list);
        undo.Undo();
        Assert.False(undo.CanUndo);
        Assert.Empty(list);
        AssertSame(before, world);
        undo.Redo();
        Assert.Single(list);
        Assert.Equal(list[0].Pieces, world.Prefabs.Count);
    }

    [Fact]
    public void Moving_and_lengthening_a_bridge_leaves_the_world_as_if_built_there()
    {
        var world = Strait();
        var before = Take(world);
        var list = new List<PlacedBridge>();
        var undo = new UndoStack();
        var edits = new EditRunner(undo);
        edits.Run(world, "Bridge", s => BuildAcrossSea(world, list, s));
        var first = list[0];
        int y = first.Y;

        // An east-west bridge moves east-west.
        var moved = BridgeTool.Move(first.Where, 30, Size);
        Assert.True(edits.Run(world, "Move bridge", s => BridgeTool.Replace(world, Kit, list, first, moved, y, s)));
        Assert.Single(list);
        Assert.Equal(first.Pieces, list[0].Pieces);
        Assert.Equal(first.Start + 30, list[0].Start);
        Assert.Equal(first.Pieces, world.Prefabs.Count);

        // Lengthen it by two pieces at the far end, one undo step.
        var longer = BridgeTool.DragEnd(list[0].Where, startEnd: false, list[0].Where.End + 2 * BridgeOp.Piece, Size)!.Value;
        Assert.Equal(first.Pieces + 2, longer.Pieces);
        Assert.True(edits.Run(world, "Resize bridge", s => BridgeTool.Replace(world, Kit, list, list[0], longer, y, s)));
        Assert.Equal(first.Pieces + 2, world.Prefabs.Count);
        Assert.Equal(first.Pieces + 2, list[0].Prefabs.Count);

        // The result is what building that bridge straight onto the original world gives.
        var fresh = Strait();
        BridgeTool.Place(fresh, Kit, longer, y, new EditSession("x", fresh));
        Assert.Equal(fresh.Height.Data, world.Height.Data);
        Assert.Equal(fresh.Roads!.Data, world.Roads!.Data);

        // Deleting it puts the original ground back, and undoing all of it too.
        Assert.True(edits.Run(world, "Delete bridge", s => BridgeTool.Remove(world, list, list[0], s)));
        AssertSame(before, world);
        undo.Undo();
        undo.Undo();
        undo.Undo();
        undo.Undo();
        AssertSame(before, world);
        Assert.Empty(list);
    }

    [Fact]
    public void Changing_a_bridges_height_rebuilds_it_at_the_new_height()
    {
        var world = Strait();
        var before = Take(world);
        var list = new List<PlacedBridge>();
        var bridge = BuildAcrossSea(world, list, new EditSession("Bridge", world));
        BridgeTool.Replace(world, Kit, list, bridge, bridge.Where, bridge.Y + 4, new EditSession("Height", world));
        Assert.Equal(bridge.Y + 4, list[0].Y);
        Assert.All(world.Prefabs, p => Assert.Equal(bridge.Y + 4, p.Position.Y));
        BridgeTool.Remove(world, list, list[0], new EditSession("Take down", world));
        AssertSame(before, world);
    }

    [Fact]
    public void Finding_a_bridge_by_a_point_on_its_deck()
    {
        var world = Strait();
        var list = new List<PlacedBridge>();
        var bridge = BuildAcrossSea(world, list, new EditSession("Bridge", world), 300);
        Assert.Same(bridge, BridgeTool.Find(list, new Vector2(SeaFrom + 50, 303)));
        Assert.Null(BridgeTool.Find(list, new Vector2(SeaFrom + 50, 320)));
        Assert.Null(BridgeTool.Find(list, new Vector2(SeaFrom - 100, 300)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Finding_a_bridge_on_its_drawn_deck_outline(bool alongZ)
    {
        var bridge = new PlacedBridge { AlongZ = alongZ, Start = 100, Line = 200, Pieces = 4 };
        var line = bridge.Where;
        float halfWidth = BridgeOp.Width / 2f;

        Assert.Same(bridge, BridgeTool.Find([bridge], line.At(line.Start)));
        Assert.Same(bridge, BridgeTool.Find([bridge], line.At(line.End)));
        Assert.Same(bridge, BridgeTool.Find([bridge], line.At(line.Start + 9, -halfWidth)));
        Assert.Same(bridge, BridgeTool.Find([bridge], line.At(line.Start + 9, halfWidth)));
        Assert.Null(BridgeTool.Find([bridge], line.At(line.End + 1)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_press_grabs_either_ramp_across_the_deck_width(bool alongZ)
    {
        var world = Strait();
        var line = new BridgeLine(alongZ, 250, 100, 10);
        var bridge = BridgeTool.Place(world, Kit, line, 34 - BridgeOp.Deck, new EditSession("Bridge", world));
        float halfWidth = BridgeOp.Width / 2f;

        Assert.Equal((bridge, BridgePart.Start), BridgeTool.HitTest(world, Library, [bridge], line.At(line.Start - 30, -halfWidth), 1));
        Assert.Equal((bridge, BridgePart.End), BridgeTool.HitTest(world, Library, [bridge], line.At(line.End + 30, halfWidth), 1));
        var tip = line.At(line.Start - 30 - halfWidth + 1, 0.5f);
        Assert.NotEqual(0, world.Roads![(int)tip.X, (int)tip.Y]);
        Assert.Equal((bridge, BridgePart.Start), BridgeTool.HitTest(world, Library, [bridge], tip, 1));
        Assert.Null(BridgeTool.HitTest(world, Library, [bridge], line.At(line.Start - 31 - halfWidth), 1));
        Assert.Null(BridgeTool.HitTest(world, Library, [bridge], line.At(line.Start - 31, -halfWidth), 1));
        Assert.Null(BridgeTool.HitTest(world, Library, [bridge], line.At(line.End + 15, halfWidth + 1), 1));
    }

    [Fact]
    public void Zooming_out_makes_the_deck_ends_easier_to_grab_and_keeps_its_middle()
    {
        var world = Strait();
        var bridge = new PlacedBridge { Start = 100, Line = 250, Pieces = 20 };
        var line = bridge.Where;
        var nearStart = line.At(line.Start + 35);
        var nearEnd = line.At(line.End - 35);

        Assert.Equal((bridge, BridgePart.Middle), BridgeTool.HitTest(world, Library, [bridge], nearStart, 2));
        Assert.Equal((bridge, BridgePart.Start), BridgeTool.HitTest(world, Library, [bridge], nearStart, 0.5));
        Assert.Equal((bridge, BridgePart.End), BridgeTool.HitTest(world, Library, [bridge], nearEnd, 0.5));
        Assert.Equal((bridge, BridgePart.Middle), BridgeTool.HitTest(world, Library, [bridge], line.At(line.Start + 90), 0.01));
        Assert.Equal((bridge, BridgePart.Start), BridgeTool.HitTest(world, Library, [bridge], line.At(line.Start + 2), 16));
    }

    [Fact]
    public void The_deck_wins_at_a_crossing_and_the_last_bridge_wins_at_overlapping_ramps()
    {
        var world = Strait();
        var first = new PlacedBridge { Start = 100, Line = 250, Pieces = 10, StartRampLength = 30 };
        var last = new PlacedBridge { Start = 110, Line = 250, Pieces = 10, StartRampLength = 30 };
        var crossing = new PlacedBridge { AlongZ = true, Start = 205, Line = 90, Pieces = 10 };
        var point = new Vector2(90, 250);

        Assert.Equal((last, BridgePart.Start), BridgeTool.HitTest(world, Library, [first, last], point, 1));
        Assert.Equal((crossing, BridgePart.Middle), BridgeTool.HitTest(world, Library, [crossing, first, last], point, 1));
    }

    [Fact]
    public void A_narrow_ramp_allows_a_near_miss_beside_it_without_stealing_a_road_hit_or_extending_the_tip()
    {
        var world = Strait();
        var first = new PlacedBridge { Start = 100, Line = 250, Pieces = 10, StartRampLength = 30 };
        var last = new PlacedBridge { Start = 100, Line = 265, Pieces = 10, StartRampLength = 30 };
        var beside = new Vector2(85, 253);

        Assert.Null(BridgeTool.HitTest(world, Library, [last], beside, 1));
        Assert.Equal((last, BridgePart.Start), BridgeTool.HitTest(world, Library, [last], beside, 0.25));
        Assert.Equal((first, BridgePart.Start), BridgeTool.HitTest(world, Library, [first, last], beside, 0.25));
        Assert.Null(BridgeTool.HitTest(world, Library, [last], new Vector2(65, 253), 0.25));
    }

    [Fact]
    public void A_built_ramp_keeps_its_hit_area_when_the_terrain_changes()
    {
        var world = Strait();
        var line = new BridgeLine(false, 250, 100, 10);
        int y = 40 - BridgeOp.Deck;
        var plan = new BridgePlan(Kit, line.AlongZ, line.Line, line.Start, line.Pieces, y);
        var ramp = Assert.IsType<RampPlan>(BridgeTool.PlanRamp(world, plan, false));
        var bridge = BridgeTool.Place(world, Kit, line, y, new EditSession("Bridge", world));
        Assert.Equal(ramp.Length, bridge.EndRampLength);

        Array.Fill(world.Height.Data, TerrainBrush.ToRaw(40));
        Assert.Equal(30, BridgeTool.PlanRamp(world, plan, false)!.Length);
        Assert.Equal((bridge, BridgePart.End), BridgeTool.HitTest(world, Library, [bridge], ramp.To, 1));
        Assert.Null(BridgeTool.HitTest(world, Library, [bridge], ramp.To + Vector2.UnitX * (BridgeOp.Width / 2f + 1), 1));
    }

    [Fact]
    public void Older_bridges_use_the_preview_for_ramps_and_bridges_without_ramps_grab_only_the_deck()
    {
        var world = Strait();
        var line = new BridgeLine(false, 250, 0, 10);
        int y = 34 - BridgeOp.Deck;
        var withoutRamps = BridgeTool.Place(world, Kit, line, y, new EditSession("Bridge", world), ramps: false);
        var legacy = BridgeTool.Place(world, Kit, line, y, new EditSession("Bridge", world));
        legacy.StartRampLength = legacy.EndRampLength = null;
        var onRamp = line.At(line.End + 15);

        Assert.Null(BridgeTool.HitTest(world, Library, [withoutRamps], onRamp, 1));
        Assert.Equal((legacy, BridgePart.End), BridgeTool.HitTest(world, Library, [legacy], onRamp, 1));
        Assert.Null(BridgeTool.HitTest(world, Library, [legacy], line.At(line.Start - 1), 1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_long_ramp_of_an_older_bridge_is_read_from_the_road_it_drew(bool alongZ)
    {
        var world = Strait();
        var line = new BridgeLine(alongZ, 250, 200, 10);
        int y = 40 - BridgeOp.Deck;
        var bridge = BridgeTool.Place(world, Kit, line, y, new EditSession("Bridge", world));
        float startBuilt = bridge.StartRampLength!.Value, endBuilt = bridge.EndRampLength!.Value;
        Assert.True(startBuilt > 90 && endBuilt > 90);
        bridge.StartRampLength = bridge.EndRampLength = null;

        // The ground is now graded by the ramps, so planning again finds only the shortest one.
        var plan = new BridgePlan(Kit, line.AlongZ, line.Line, line.Start, line.Pieces, y);
        Assert.Equal(BridgeTool.MinRamp, BridgeTool.PlanRamp(world, plan, true)!.Length);

        Assert.InRange(BridgeTool.RampLength(world, Library, bridge, true), startBuilt - 2, startBuilt + 2);
        Assert.InRange(BridgeTool.RampLength(world, Library, bridge, false), endBuilt - 2, endBuilt + 2);
        Assert.Null(bridge.StartRampLength);
        Assert.Equal((bridge, BridgePart.Start), BridgeTool.HitTest(world, Library, [bridge], line.At(line.Start - startBuilt + 3), 1));
        Assert.Equal((bridge, BridgePart.End), BridgeTool.HitTest(world, Library, [bridge], line.At(line.End + endBuilt - 3), 1));
        Assert.Null(BridgeTool.HitTest(world, Library, [bridge], line.At(line.End + endBuilt + 14), 1));
    }

    // ---- the stage's bridges and the tool's ----

    [Fact]
    public void Taking_down_the_stages_bridges_leaves_the_tools()
    {
        var world = Strait();
        var list = new List<PlacedBridge>();
        var mine = BuildAcrossSea(world, list, new EditSession("Mine", world), 300);

        // A bridge the Highways stage built, elsewhere, recorded as the stage records it.
        var stage = new List<HighwayRoad>();
        var plan = new BridgePlan(Kit, false, 700, SeaFrom - 9, 24, 25);
        stage.Add(BridgeOp.Build(world, plan, new EditSession("Stage", world)));
        Assert.Equal(mine.Pieces + plan.Pieces, world.Prefabs.Count);

        HighwayStage.TakeDown(world, stage, new EditSession("Re-run", world));
        Assert.Empty(stage);
        Assert.Equal(mine.Pieces, world.Prefabs.Count);
        Assert.Single(list);

        // And the other way round.
        stage.Add(BridgeOp.Build(world, plan, new EditSession("Stage", world)));
        BridgeTool.Remove(world, list, mine, new EditSession("Delete", world));
        Assert.Equal(plan.Pieces, world.Prefabs.Count);
        Assert.Single(stage);
    }

    // ---- roads onto the deck ----

    [Fact]
    public void A_road_can_snap_to_either_bridges_ends_and_meets_the_deck_at_its_level()
    {
        var world = Strait();
        var list = new List<PlacedBridge>();
        var mine = BuildAcrossSea(world, list, new EditSession("Mine", world), 300);
        var stage = new List<HighwayRoad>();
        stage.Add(BridgeOp.Build(world, new BridgePlan(Kit, true, 800, 100, 5, 25), new EditSession("Stage", world)));

        var ends = BridgeTool.Ends(list, stage);
        Assert.Equal(4, ends.Count);
        var west = BridgeTool.FindEnd(ends, new Vector2(mine.Start - 3, 304), 8)!.Value;
        Assert.Equal(new Vector2(mine.Start, 300), west.Point);
        Assert.Equal(new Vector2(-1, 0), west.Outward);
        Assert.Equal(mine.DeckLevel, west.DeckLevel);
        Assert.Null(BridgeTool.FindEnd(ends, new Vector2(mine.Start - 30, 300), 8));

        var north = BridgeTool.FindEnd(ends, new Vector2(800, 100 + 45 + 2), 8)!.Value;
        Assert.Equal((new Vector2(800, 145), new Vector2(0, 1), (float)(25 + BridgeOp.Deck)), (north.Point, north.Outward, north.DeckLevel));

        // A road from the west end heads off and its height starts at the deck's, then follows the ground.
        var road = new RoadPath([west.Point, west.Point + new Vector2(-150, 0)], RoadType.Asphalt, 4.5f);
        var line = RoadOp.Centreline(road.Points, RoadOp.CornerRounding(road.Type));
        var profile = BridgeTool.ApproachProfile(world, line, road.Type, west.DeckLevel, null, 40);
        Assert.Equal(west.DeckLevel, profile[0], 0.01);
        Assert.True(profile[^1] < west.DeckLevel - 1);
    }

    // ---- the saved list ----

    [Fact]
    public void The_bridge_list_survives_save_and_load_and_can_still_be_taken_down()
    {
        var world = Strait();
        var before = Take(world);
        var settings = new ProjectStore.ProjectSettings();
        BuildAcrossSea(world, settings.Bridges, new EditSession("A", world), 300);
        BuildAcrossSea(world, settings.Bridges, new EditSession("B", world), 500);
        var original = settings.Bridges.ToList();

        string path = Path.Combine(_dir, "bridges" + ProjectStore.Extension);
        // Saved through a copy, as the app's save and autosave do (an early build's copy left the list out).
        ProjectStore.Save(world, path, settings.Clone());
        var loaded = ProjectStore.Load(path, out var loadedSettings);

        Assert.Equal(2, loadedSettings.Bridges.Count);
        for (int i = 0; i < 2; i++)
        {
            var a = original[i];
            var b = loadedSettings.Bridges[i];
            Assert.Equal((a.AlongZ, a.Line, a.Start, a.Pieces, a.Y), (b.AlongZ, b.Line, b.Start, b.Pieces, b.Y));
            Assert.Equal((a.StartRampLength, a.EndRampLength), (b.StartRampLength, b.EndRampLength));
            Assert.Equal(a.Grounds.Count, b.Grounds.Count);
            for (int g = 0; g < a.Grounds.Count; g++) Assert.Equal(a.Grounds[g], b.Grounds[g]);
            Assert.Equal(a.Prefabs.Select(p => (p.Name, p.X, p.Y, p.Z, p.Rotation)), b.Prefabs.Select(p => (p.Name, p.X, p.Y, p.Z, p.Rotation)));
        }
        Assert.Equal(world.Prefabs.Count, loaded.Prefabs.Count);
        // Each loaded piece still belongs to its bridge, so Select and the Bridge tool pick the whole bridge.
        Assert.All(loaded.Prefabs, p => Assert.NotNull(BridgeTool.Owner(loadedSettings.Bridges, p)));
        Assert.Same(loadedSettings.Bridges[0], BridgeTool.Find(loadedSettings.Bridges, loadedSettings.Bridges[0].Where.At(loadedSettings.Bridges[0].Start + 20)));

        foreach (var b in loadedSettings.Bridges.ToList())
            BridgeTool.Remove(loaded, loadedSettings.Bridges, b, new EditSession("Delete", loaded));
        Assert.Empty(loadedSettings.Bridges);
        Assert.Equal(before.Height, loaded.Height.Data);
        Assert.Equal(before.Roads, loaded.Roads!.Data);
        Assert.Empty(loaded.Prefabs);
    }

    [Fact]
    public void Copying_the_project_settings_keeps_its_bridges_apart_from_the_originals()
    {
        var world = Strait();
        var settings = new ProjectStore.ProjectSettings { ExportFolder = "x" };
        BuildAcrossSea(world, settings.Bridges, new EditSession("A", world), 300);

        var copy = settings.Clone();
        Assert.Equal("x", copy.ExportFolder);
        var (a, b) = (settings.Bridges[0], Assert.Single(copy.Bridges));
        Assert.NotSame(a, b);
        Assert.Equal((a.AlongZ, a.Line, a.Start, a.Pieces, a.Y, a.Grounds.Count, a.Prefabs.Count),
            (b.AlongZ, b.Line, b.Start, b.Pieces, b.Y, b.Grounds.Count, b.Prefabs.Count));

        // Editing on while a save runs doesn't change what is being saved.
        a.Y += 4;
        settings.Bridges.Clear();
        Assert.Single(copy.Bridges);
        Assert.NotEqual(a.Y, b.Y);
    }

    [Fact]
    public void A_project_with_no_bridges_saves_and_loads_an_empty_list()
    {
        var world = Strait();
        string path = Path.Combine(_dir, "none" + ProjectStore.Extension);
        ProjectStore.Save(world, path, new ProjectStore.ProjectSettings());
        ProjectStore.Load(path, out var settings);
        Assert.Empty(settings.Bridges);
        // Format 3 added the bridge list; later formats keep it.
        Assert.True(ProjectStore.FormatVersion >= 3);
    }

    [Fact]
    public void The_ground_kept_clear_for_a_bridge_covers_its_deck_and_the_longest_ramp_at_each_end()
    {
        var eastWest = new PlacedBridge { AlongZ = false, Line = 300, Start = 400, Pieces = 10 };
        var area = BridgeTool.KeepClearArea(eastWest, Size);
        Assert.Equal(new GridRect(400 - 200, 300 - BridgeOp.Width / 2, 490 + 200, 300 + BridgeOp.Width / 2), area);

        // Near the edge it stops at the world.
        var northSouth = new PlacedBridge { AlongZ = true, Line = 50, Start = 100, Pieces = 4 };
        Assert.Equal(new GridRect(50 - BridgeOp.Width / 2, 0, 50 + BridgeOp.Width / 2, 136 + 200), BridgeTool.KeepClearArea(northSouth, Size));
    }

    [Fact]
    public void A_piece_belongs_to_the_bridge_that_built_it_and_not_to_the_stages()
    {
        var world = Strait();
        var list = new List<PlacedBridge>();
        var a = BuildAcrossSea(world, list, new EditSession("A", world), 300);
        var b = BuildAcrossSea(world, list, new EditSession("B", world), 500);
        var stage = BridgeOp.Build(world, new BridgePlan(Kit, true, 800, 100, 5, 25), new EditSession("Stage", world));

        foreach (var piece in world.Prefabs)
        {
            int z = world.WorldToGrid(piece.Position.Z);
            var owner = BridgeTool.Owner(list, piece);
            var stageOwner = BridgeTool.StageOwner([stage], piece);
            if (z is 293) Assert.Same(a, owner);
            else if (z is 493) Assert.Same(b, owner);
            else Assert.Null(owner);
            // The stage's pieces (locked in Select and Prefabs) are told apart from the tool's.
            if (owner is null) Assert.Same(stage, stageOwner);
            else Assert.Null(stageOwner);
        }
        Assert.Equal(a.Pieces + b.Pieces + stage.Prefabs.Count, world.Prefabs.Count);
        Assert.Null(BridgeTool.Owner(list, new PrefabInstance { Name = "other", Position = new Int3(0, 0, 0) }));
        Assert.Null(BridgeTool.StageOwner([stage], new PrefabInstance { Name = "other", Position = new Int3(0, 0, 0) }));
        Assert.Null(BridgeTool.StageOwner(null, world.Prefabs[0]));
    }

    [Fact]
    public void A_bridge_that_no_longer_fits_or_has_no_pieces_to_build_with_is_dropped_on_rebuild()
    {
        var world = Strait();
        var list = new List<PlacedBridge>();
        var good = BuildAcrossSea(world, list, new EditSession("A", world), 300);
        var edge = BridgeTool.Add(world, Kit, list, new BridgeLine(false, 500, 0, 3), 20, new EditSession("B", world));
        var session = new EditSession("Stage", world);
        var taken = BridgeTool.TakeDownAll(world, list, session);
        Assert.Empty(world.Prefabs);

        // A record that sits off the map (as if the world were smaller) is dropped; the other is built again in its place.
        edge.Start = Size - 10;
        Assert.Equal(1, BridgeTool.RebuildAll(world, Kit, list, taken, session));
        Assert.Single(list);
        Assert.Equal(good.Where, list[0].Where);

        var taken2 = BridgeTool.TakeDownAll(world, list, session);
        Assert.Equal(1, BridgeTool.RebuildAll(world, null, list, taken2, session));
        Assert.Empty(list);
    }
}
