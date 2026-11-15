using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Generate;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;
using MapEditor.Core.Validate;

namespace MapEditor.Core.Tests;

public sealed class BridgeTests
{
    private const int Size = 4096;

    /// <summary>The sea between the islands, in grid metres along x.</summary>
    private const int SeaFrom = 1800, SeaTo = 2300;

    private static readonly PrefabLibrary Library = PrefabLibrary.Of(MakeLibrary());

    private static IEnumerable<PrefabInfo> MakeLibrary()
    {
        foreach (var shape in TownGenerator.ShapeNames)
            yield return new PrefabInfo($"rwg_tile_gateway_{shape}", "RWGTiles", new Int3(150, 22, 150), -12, 0, 0, ["streettile"], null);
        yield return new PrefabInfo(HighwayStage.BridgeTile, "POIs", new Int3(14, 14, 9), -1, 0, 0, ["rwgonly"], null);
        yield return new PrefabInfo(HighwayStage.BridgeEnd, "POIs", new Int3(14, 14, 9), -1, 0, 0, ["rwgonly"], null);
        yield return new PrefabInfo(Plateau, "POIs", new Int3(150, 10, 150), 0, 0, 0, [], null);
        // The deck wrecks, at the game's sizes.
        yield return new PrefabInfo("player_start1", "POIs", new Int3(6, 1, 2), 0, 0, 0, ["navonly"], null);
        yield return new PrefabInfo("player_start2", "POIs", new Int3(3, 1, 8), 0, 0, 0, ["navonly"], null);
        yield return new PrefabInfo("player_start3", "POIs", new Int3(6, 1, 5), 0, 0, 0, ["navonly"], null);
        yield return new PrefabInfo("player_start4", "POIs", new Int3(5, 1, 7), 0, 0, 0, ["navonly"], null);
        yield return new PrefabInfo("player_start5", "POIs", new Int3(6, 1, 7), 0, 0, 0, ["navonly"], null);
        yield return new PrefabInfo("player_start6", "POIs", new Int3(6, 1, 6), 0, 0, 0, ["navonly"], null);
        yield return new PrefabInfo("part_ambulance_01", "Parts", new Int3(5, 3, 8), 0, 2, 0, ["part"], null);
        yield return new PrefabInfo("part_armytruck_01", "Parts", new Int3(5, 3, 13), 0, 2, 0, ["part"], null);
    }

    /// <summary>A big flat POI whose ground roads keep to, like a town's.</summary>
    private const string Plateau = "test_plateau";

    /// <summary>Two islands at 34 m, split north to south by a strait of sea 12 m deep, with a roadside town on each.</summary>
    private static World TwoIslands()
    {
        var world = new World { Name = "T", Size = Size, Height = new Grid<ushort>(Size, Size), Water = new Grid<byte>(Size, Size) };
        for (int z = 0; z < Size; z++)
            for (int x = 0; x < Size; x++)
            {
                bool sea = x >= SeaFrom && x < SeaTo;
                world.Height[x, z] = TerrainBrush.ToRaw(sea ? 18 : 34);
                if (sea) world.Water[x, z] = StampOp.DefaultWaterLevel;
            }
        Town(world, 7, 13);
        Town(world, 19, 13);
        return world;
    }

    private static void Town(World world, int cx, int cz)
    {
        var info = Library.Find("rwg_tile_gateway_straight")!;
        world.Prefabs.Add(new PrefabInstance
        {
            Name = info.Name,
            Position = new Int3(world.GridToWorld(cx * 150), 34, world.GridToWorld(cz * 150)),
            Rotation = TownGenerator.TileRotation(info, 0),
        });
    }

    private static HighwayResult Run(World world, HighwayOptions options, List<HighwayRoad> roads, EditSession? session = null) =>
        HighwayStage.Run(world, options, roads, Library, RwgMixer.Empty, [], session ?? new EditSession("Highways", world),
            cancel: TestContext.Current.CancellationToken);

    private static List<PrefabInstance> Pieces(World world) =>
        world.Prefabs.Where(p => p.Name is HighwayStage.BridgeTile or HighwayStage.BridgeEnd).ToList();

    [Fact]
    public void Without_bridges_islands_stay_apart()
    {
        var world = TwoIslands();
        var result = Run(world, new HighwayOptions(), []);
        Assert.Empty(Pieces(world));
        Assert.Contains(result.Notes, n => n.Contains("2 groups"));
    }

    [Fact]
    public void The_stage_keeps_its_roads_and_bridges_off_a_drawn_bridges_deck()
    {
        // Where the stage puts its bridge when the strait is free...
        var first = TwoIslands();
        Run(first, new HighwayOptions { Bridges = true }, []);
        var taken = Assert.Single(BridgeChecks.Find(first, Library).Bridges).Where.Rect;

        // ...is kept clear when a Bridge tool bridge stands there (taken down while the stage runs, as the app does).
        var world = TwoIslands();
        var roads = new List<HighwayRoad>();
        HighwayStage.Run(world, new HighwayOptions { Bridges = true }, roads, Library, RwgMixer.Empty, [], new EditSession("Highways", world),
            cancel: TestContext.Current.CancellationToken, keepClear: [taken]);

        foreach (var b in BridgeChecks.Find(world, Library).Bridges)
        {
            var r = b.Where.Rect;
            Assert.False(r.X0 < taken.X1 && taken.X0 < r.X1 && r.Z0 < taken.Z1 && taken.Z0 < r.Z1, $"a stage bridge at {r} overlaps the drawn one at {taken}");
        }
        foreach (var road in roads)
            foreach (var p in road.Points)
                Assert.False(p[0] > taken.X0 && p[0] < taken.X1 && p[1] > taken.Z0 && p[1] < taken.Z1, $"a road point ({p[0]}, {p[1]}) is on the drawn bridge");
    }

    [Fact]
    public void A_bridge_joins_two_islands_in_a_straight_line_with_water_left_under_it()
    {
        var world = TwoIslands();
        var roads = new List<HighwayRoad>();
        var result = Run(world, new HighwayOptions { Bridges = true }, roads);

        Assert.DoesNotContain(result.Notes, n => n.Contains("groups"));
        var bridge = Assert.Single(roads, r => r.Kind == HighwayKind.Bridge);
        var pieces = Pieces(world).OrderBy(p => p.Position.X).ToList();
        Assert.Equal(bridge.Prefabs.Count(p => p.Name is HighwayStage.BridgeTile or HighwayStage.BridgeEnd), pieces.Count);
        // East-west: one row of sections 9 m apart, all at one height, wide enough to reach land at both ends.
        Assert.Single(pieces.Select(p => p.Position.Z).Distinct());
        Assert.Single(pieces.Select(p => p.Position.Y).Distinct());
        for (int i = 1; i < pieces.Count; i++) Assert.Equal(HighwayStage.BridgePiece, pieces[i].Position.X - pieces[i - 1].Position.X);
        Assert.True(world.WorldToGrid(pieces[0].Position.X) < SeaFrom);
        Assert.True(world.WorldToGrid(pieces[^1].Position.X) + HighwayStage.BridgePiece > SeaTo);
        // End pieces at the ends, capped side out (west end turned 3, east end 1); sections between turned 1.
        Assert.Equal((HighwayStage.BridgeEnd, 3), (pieces[0].Name, pieces[0].Rotation));
        Assert.Equal((HighwayStage.BridgeEnd, 1), (pieces[^1].Name, pieces[^1].Rotation));
        Assert.All(pieces.Skip(1).SkipLast(1), p => Assert.Equal((HighwayStage.BridgeTile, 1), (p.Name, p.Rotation)));

        // The beams clear the water by the set room, and the water stays open between the piers.
        int y = pieces[0].Position.Y;
        Assert.Equal(StampOp.DefaultWaterLevel + HighwayStage.BridgeClearance, y + HighwayStage.BridgeBeams);
        int line = world.WorldToGrid(pieces[0].Position.Z) + HighwayStage.BridgeWidth / 2;
        int gap = world.WorldToGrid(pieces[pieces.Count / 2].Position.X); // a section's start, between pier rows
        Assert.Equal(StampOp.DefaultWaterLevel, world.Water![gap, line]);
        Assert.True(World.HeightToMetres(world.Height[gap, line]) < y - 4, "the footings meet between the piers");
        // Under its pier row the ground is raised to the pillars' feet, still under water.
        Assert.Equal(y, World.HeightToMetres(world.Height[gap + 4, line]), 1);
        Assert.Equal(StampOp.DefaultWaterLevel, world.Water[gap + 4, line]);

        // A road from each town meets the deck at its height.
        var joins = roads.Where(r => r.Kind == HighwayKind.JoinUp).ToList();
        Assert.Equal(2, joins.Count);
        int west = world.WorldToGrid(pieces[0].Position.X);
        Assert.Equal(y + HighwayStage.BridgeDeck, World.HeightToMetres(world.Height[west - 2, line]), 0);
    }

    [Fact]
    public void A_bridge_keeps_off_a_steep_shore_when_a_gentle_one_is_in_reach()
    {
        // The shortest, nearest crossing is level with the towns, but the east shore there climbs 1 m in 2 for 50 m.
        var world = TwoIslands();
        const int hillFrom = 1925, hillTo = 2125;
        for (int z = hillFrom; z < hillTo; z++)
            for (int x = SeaTo; x < SeaTo + 200; x++)
                world.Height[x, z] = TerrainBrush.ToRaw(Math.Min(34 + (x - SeaTo) / 2f, 84));

        var roads = new List<HighwayRoad>();
        Run(world, new HighwayOptions { Bridges = true }, roads);

        var bridge = Assert.Single(BridgeChecks.Find(world, Library).Bridges);
        Assert.True(bridge.Where.Rect.Z1 <= hillFrom || bridge.Where.Rect.Z0 >= hillTo,
            $"the bridge at {bridge.Where.Rect} lands on the steep shore");
    }

    [Fact]
    public void The_road_off_a_bridge_end_stays_on_the_bridges_line_for_its_whole_lead()
    {
        // The towns are far apart along z, so each road has to turn off the bridge's line to reach its town.
        var world = TwoIslands();
        world.Prefabs.Clear();
        Town(world, 7, 8);
        Town(world, 19, 18);
        var roads = new List<HighwayRoad>();
        Run(world, new HighwayOptions { Bridges = true }, roads);

        var rect = Assert.Single(BridgeChecks.Find(world, Library).Bridges).Where.Rect;
        float line = (rect.Z0 + rect.Z1) / 2f;
        var joins = roads.Where(r => r.Kind == HighwayKind.JoinUp).ToList();
        Assert.Equal(2, joins.Count);
        foreach (var road in joins)
        {
            var centre = RoadOp.Centreline([.. road.Points.Select(p => new System.Numerics.Vector2(p[0], p[1]))], RoadOp.CornerRounding(road.Type));
            // Only the stretch from the deck's end out to the old lead point (40 m): it must run straight.
            foreach (var p in centre.Where(p => p.X < rect.X0 && p.X > rect.X0 - 40 || p.X > rect.X1 && p.X < rect.X1 + 40))
                Assert.True(Math.Abs(p.Y - line) <= 2, $"the road is {Math.Abs(p.Y - line):0.0} m off the bridge's line at ({p.X:0}, {p.Y:0})");
        }
    }

    [Fact]
    public void A_north_south_bridge_turns_its_end_pieces_outward()
    {
        // The same strait running east to west, with a town north and south of it.
        var world = new World { Name = "T", Size = Size, Height = new Grid<ushort>(Size, Size), Water = new Grid<byte>(Size, Size) };
        for (int z = 0; z < Size; z++)
            for (int x = 0; x < Size; x++)
            {
                bool sea = z >= SeaFrom && z < SeaTo;
                world.Height[x, z] = TerrainBrush.ToRaw(sea ? 18 : 34);
                if (sea) world.Water[x, z] = StampOp.DefaultWaterLevel;
            }
        Town(world, 13, 7);
        Town(world, 13, 19);
        Run(world, new HighwayOptions { Bridges = true }, []);

        var pieces = Pieces(world).OrderBy(p => p.Position.Z).ToList();
        Assert.NotEmpty(pieces);
        Assert.Single(pieces.Select(p => p.Position.X).Distinct());
        Assert.Equal((HighwayStage.BridgeEnd, 0), (pieces[0].Name, pieces[0].Rotation));
        Assert.Equal((HighwayStage.BridgeEnd, 2), (pieces[^1].Name, pieces[^1].Rotation));
        Assert.All(pieces.Skip(1).SkipLast(1), p => Assert.Equal((HighwayStage.BridgeTile, 0), (p.Name, p.Rotation)));
    }

    [Fact]
    public void Validating_a_bridged_world_leaves_its_bridge_alone()
    {
        var world = TwoIslands();
        Run(world, new HighwayOptions { Bridges = true }, []);
        Assert.NotEmpty(Pieces(world));
        Assert.Equal(HighwayStage.BridgeDeck, Library.Find(HighwayStage.BridgeTile)!.RoadLevelAbove(0));

        var issues = WorldValidator.Validate(world, Library);
        Assert.DoesNotContain(issues, i => i.Message.Contains("rwg_bridge"));
    }

    [Fact]
    public void Roads_near_a_bridge_meet_its_deck_and_none_joins_on_its_ramps()
    {
        var world = TwoIslands();
        Run(world, new HighwayOptions { Bridges = true }, []);
        var pieces = Pieces(world).OrderBy(p => p.Position.X).ToList();
        int y = pieces[0].Position.Y;
        int west = world.WorldToGrid(pieces[0].Position.X), line = world.WorldToGrid(pieces[0].Position.Z) + HighwayStage.BridgeWidth / 2;

        // As a later stage (wilderness) sees the world: just past the end, roads keep to the deck and may not join.
        var grid = new HighwayStage.PathGrid(world, Library, new HighwayOptions());
        int nearEnd = grid.CellOf(new System.Numerics.Vector2(west - 15, line));
        Assert.Equal(y + HighwayStage.BridgeDeck, grid.Hold[nearEnd]);
        Assert.NotEqual(0, grid.Flags[nearEnd] & HighwayStage.PathGrid.NoJoin);
        int farOff = grid.CellOf(new System.Numerics.Vector2(west - HighwayStage.PathGrid.BridgeRamp - 50, line));
        Assert.Equal(0, grid.Flags[farOff] & HighwayStage.PathGrid.NoJoin);
    }

    /// <summary>Four islands split by a cross of sea, a town on each; they can only be bridged to the two beside them.</summary>
    private static World FourIslands()
    {
        var world = new World { Name = "T", Size = Size, Height = new Grid<ushort>(Size, Size), Water = new Grid<byte>(Size, Size) };
        for (int z = 0; z < Size; z++)
            for (int x = 0; x < Size; x++)
            {
                bool sea = x >= SeaFrom && x < SeaTo || z >= SeaFrom && z < SeaTo;
                world.Height[x, z] = TerrainBrush.ToRaw(sea ? 18 : 34);
                if (sea) world.Water[x, z] = StampOp.DefaultWaterLevel;
            }
        Town(world, 7, 7);
        Town(world, 19, 7);
        Town(world, 7, 19);
        Town(world, 19, 19);
        return world;
    }

    [Fact]
    public void Shortcut_bridges_close_a_long_way_round()
    {
        var plain = FourIslands();
        var roads = new List<HighwayRoad>();
        Run(plain, new HighwayOptions { Bridges = true }, roads);
        // Joining four islands in a ring takes three bridges, so the two at the chain's ends are three apart.
        Assert.Equal(3, roads.Count(r => r.Kind == HighwayKind.Bridge));

        var world = FourIslands();
        roads = [];
        var result = Run(world, new HighwayOptions { Bridges = true, BridgeShortcuts = true }, roads);
        Assert.Equal(4, roads.Count(r => r.Kind == HighwayKind.Bridge));
        Assert.Contains(result.Notes, n => n.Contains("1 shortcut bridge"));
        // No bridge crosses another.
        var rects = roads.Where(r => r.Kind == HighwayKind.Bridge).Select(r => (
            X0: Math.Min(r.Points[0][0], r.Points[1][0]) - 7, X1: Math.Max(r.Points[0][0], r.Points[1][0]) + 7,
            Z0: Math.Min(r.Points[0][1], r.Points[1][1]) - 7, Z1: Math.Max(r.Points[0][1], r.Points[1][1]) + 7)).ToList();
        for (int i = 0; i < rects.Count; i++)
            for (int j = i + 1; j < rects.Count; j++)
                Assert.False(rects[i].X0 < rects[j].X1 && rects[j].X0 < rects[i].X1 && rects[i].Z0 < rects[j].Z1 && rects[j].Z0 < rects[i].Z1,
                    $"bridges {i} and {j} cross");
    }

    [Fact]
    public void Water_wider_than_the_longest_bridge_is_not_bridged()
    {
        var world = TwoIslands();
        var result = Run(world, new HighwayOptions { Bridges = true, BridgeMax = SeaTo - SeaFrom - 100 }, []);
        Assert.Empty(Pieces(world));
        Assert.Contains(result.Notes, n => n.Contains("2 groups") && n.Contains("Bridges"));
    }

    [Fact]
    public void A_bridge_keeps_clear_of_ground_too_high_to_ramp_up_to()
    {
        // A POI 4 m above the deck, 50 m back from the west shore, right where the best crossing would land. Roads keep
        // to its ground for 20 m round it, so a bridge there would meet that ground with a 4 m step.
        var world = TwoIslands();
        int deckTop = StampOp.DefaultWaterLevel + HighwayStage.BridgeClearance - HighwayStage.BridgeBeams + HighwayStage.BridgeDeck;
        const int X0 = SeaFrom - 200, Z0 = 13 * 150 - 75;
        world.Prefabs.Add(new PrefabInstance
        {
            Name = Plateau,
            Position = new Int3(world.GridToWorld(X0), deckTop + 4 + 1, world.GridToWorld(Z0)),
        });
        Assert.Equal(deckTop + 4, Library.Find(Plateau)!.RoadLevelAbove(deckTop + 4 + 1));

        var roads = new List<HighwayRoad>();
        Run(world, new HighwayOptions { Bridges = true }, roads);

        var bridge = Assert.Single(roads, r => r.Kind == HighwayKind.Bridge);
        float line = bridge.Points[0][1];
        const int Keep = HighwayStage.PathGrid.HoldReach + HighwayStage.BridgeWidth / 2;
        Assert.True(line < Z0 - Keep || line > Z0 + 150 + Keep, $"the bridge lands by the raised ground (line z {line})");
    }

    [Fact]
    public void A_bridge_keeps_off_a_highway_planned_before_it()
    {
        // Two towns by the west shore, joined by a highway along it, and one town across the strait. Highways are planned
        // before bridges, so the bridge must not land on that highway: its deck hold would lift the road to deck height
        // and run it through the bridge's sides (IslandMap8k).
        // The west shore is moved in to just past the highway that joins the two west towns (it runs north-south at x 1725).
        var world = TwoIslands();
        world.Prefabs.Clear();
        for (int z = 0; z < Size; z++)
            for (int x = 1745; x < SeaFrom; x++)
            {
                world.Height[x, z] = TerrainBrush.ToRaw(18);
                world.Water![x, z] = StampOp.DefaultWaterLevel;
            }
        Town(world, 11, 8);
        Town(world, 11, 18);
        Town(world, 19, 13);
        var roads = new List<HighwayRoad>();
        Run(world, new HighwayOptions { Bridges = true }, roads);

        var bridge = Assert.Single(BridgeChecks.Find(world, Library).Bridges);
        var rect = bridge.Where.Rect;
        foreach (var road in roads.Where(r => r.Kind is HighwayKind.Highway or HighwayKind.Country))
        {
            var centre = RoadOp.Centreline([.. road.Points.Select(p => new System.Numerics.Vector2(p[0], p[1]))], RoadOp.CornerRounding(road.Type));
            foreach (var p in centre)
                Assert.False(p.X > rect.X0 - road.Radius && p.X < rect.X1 + road.Radius && p.Y > rect.Z0 - road.Radius && p.Y < rect.Z1 + road.Radius,
                    $"the {road.Kind} road at ({p.X:0}, {p.Y:0}) runs across the bridge at {rect}");
        }
        Assert.DoesNotContain(BridgeChecks.Analyze(world, Library).Issues, i => i.Kind == BridgeIssue.Clearance);
    }

    [Fact]
    public void Taking_the_highways_down_takes_the_bridge_and_its_footings_out()
    {
        var world = TwoIslands();
        var before = (ushort[])world.Height.Data.Clone();
        int prefabs = world.Prefabs.Count;
        var roads = new List<HighwayRoad>();
        Run(world, new HighwayOptions { Bridges = true }, roads);
        Assert.NotEmpty(Pieces(world));

        HighwayStage.TakeDown(world, roads, new EditSession("Down", world));
        Assert.Empty(Pieces(world));
        Assert.Equal(prefabs, world.Prefabs.Count);
        Assert.Equal(before, world.Height.Data);
    }

    [Fact]
    public void Undo_puts_the_bridge_back_and_takes_it_out_again()
    {
        var world = TwoIslands();
        var undo = new UndoStack();
        var runner = new EditRunner(undo);
        Assert.True(runner.Run(world, "Highways", session => Run(world, new HighwayOptions { Bridges = true }, [], session)));
        int pieces = Pieces(world).Count;
        Assert.True(pieces > 0);
        undo.Undo();
        Assert.Empty(Pieces(world));
        undo.Redo();
        Assert.Equal(pieces, Pieces(world).Count);
    }

    private static List<PrefabInstance> Wrecks(World world) =>
        world.Prefabs.Where(p => p.Name is not (HighwayStage.BridgeTile or HighwayStage.BridgeEnd) && !p.Name.StartsWith("rwg_tile_")).ToList();

    [Fact]
    public void A_stage_bridge_gets_wrecks_from_the_highway_seed_recorded_with_its_pieces()
    {
        var world = TwoIslands();
        var roads = new List<HighwayRoad>();
        Run(world, new HighwayOptions { Seed = 7, Bridges = true }, roads);
        var bridge = Assert.Single(roads, r => r.Kind == HighwayKind.Bridge);
        var wrecks = Wrecks(world);
        Assert.NotEmpty(wrecks); // a 500 m crossing
        Assert.All(wrecks, w => Assert.Contains(bridge.Prefabs, r => r.Matches(w)));
        // The pieces come first in the record (Ends reads the first one's height).
        Assert.All(bridge.Prefabs.Take(bridge.Prefabs.Count - wrecks.Count), r => Assert.StartsWith("rwg_bridge_", r.Name));
        // A stage bridge's wreck is the stage's: locked in the Prefabs tool, not the Bridge tool's.
        Assert.Same(bridge, BridgeTool.StageOwner(roads, wrecks[0]));
        Assert.Null(BridgeTool.Owner([], wrecks[0]));

        // The same seed gives the same wrecks; another seed, other ones.
        string Key(World w) => string.Join(";", Wrecks(w).OrderBy(p => p.Position.X).Select(p => $"{p.Name}|{p.Position.X},{p.Position.Y},{p.Position.Z}|{p.Rotation}"));
        var same = TwoIslands();
        Run(same, new HighwayOptions { Seed = 7, Bridges = true }, []);
        Assert.Equal(Key(world), Key(same));
        var other = TwoIslands();
        Run(other, new HighwayOptions { Seed = 8, Bridges = true }, []);
        Assert.NotEqual(Key(world), Key(other));
    }

    [Fact]
    public void With_deck_wrecks_off_the_stage_places_none()
    {
        var world = TwoIslands();
        var roads = new List<HighwayRoad>();
        Run(world, new HighwayOptions { Seed = 7, Bridges = true, DeckWrecks = false }, roads);
        Assert.Empty(Wrecks(world));
        Assert.Equal(Pieces(world).Count, Assert.Single(roads, r => r.Kind == HighwayKind.Bridge).Prefabs.Count);
    }

    [Fact]
    public void Taking_the_highways_down_and_undo_take_the_wrecks_out_too()
    {
        var world = TwoIslands();
        int before = world.Prefabs.Count;
        var undo = new UndoStack();
        var roads = new List<HighwayRoad>();
        Assert.True(new EditRunner(undo).Run(world, "Highways", s => Run(world, new HighwayOptions { Bridges = true }, roads, s)));
        Assert.NotEmpty(Wrecks(world));
        int all = world.Prefabs.Count;
        undo.Undo();
        Assert.Equal(before, world.Prefabs.Count);
        undo.Redo();
        Assert.Equal(all, world.Prefabs.Count);
        HighwayStage.TakeDown(world, roads, new EditSession("Down", world));
        Assert.Equal(before, world.Prefabs.Count);
    }

    [Fact]
    public void Validating_a_world_with_a_bridge_and_its_wrecks_finds_no_bridge_issue()
    {
        var world = TwoIslands();
        Run(world, new HighwayOptions { Bridges = true }, []);
        Assert.NotEmpty(Wrecks(world));
        var issues = WorldValidator.Validate(world, Library);
        Assert.DoesNotContain(issues, i => i.Message.Contains("player_start") || i.Message.Contains("part_") || i.Message.Contains("overlaps"));
        Assert.Empty(BridgeChecks.Find(world, Library).Strays);
    }

    /// <summary>
    /// Pins a seeded stage-4 run with bridges and shortcuts: world, prefabs and bridge records must not drift. With the wrecks
    /// on it differs from off by the wrecks alone. Both were re-pinned when approach roads began to run straight for
    /// <c>ApproachRun</c> (the road's control points moved).
    /// </summary>
    [Theory]
    [InlineData(false, "054C9C0B2760F844C0F9977EF3E9A9E2FE05FBAEA3F32781996BE49BC934D970")]
    [InlineData(true, "1375FF4097E763F28A4C11B92E94BD44E5D93759AB1FB1FD3D84547D888CF871")]
    public void A_seeded_bridge_run_gives_the_same_world_and_records(bool wrecks, string expected)
    {
        var world = TwoIslands();
        var roads = new List<HighwayRoad>();
        Run(world, new HighwayOptions { Seed = 7, Bridges = true, BridgeShortcuts = true, DeckWrecks = wrecks }, roads);

        var sb = new System.Text.StringBuilder();
        foreach (var p in world.Prefabs.OrderBy(p => p.Name).ThenBy(p => p.Position.X).ThenBy(p => p.Position.Z))
            sb.Append($"{p.Name}|{p.Position.X},{p.Position.Y},{p.Position.Z}|{p.Rotation};");
        foreach (var r in roads)
            sb.Append($"R{r.Kind}|{r.Type}|{r.Radius}|{string.Join(',', r.Points.Select(q => $"{q[0]}:{q[1]}"))}|{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(r.Ground))}|{r.Prefabs.Count};");
        var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        hash.AppendData(System.Text.Encoding.UTF8.GetBytes(sb.ToString()));
        for (int z = 0; z < Size; z++)
            for (int x = 0; x < Size; x++)
                hash.AppendData(BitConverter.GetBytes(world.Height[x, z]));
        Assert.Equal(expected, Convert.ToHexString(hash.GetHashAndReset()));
    }
}
