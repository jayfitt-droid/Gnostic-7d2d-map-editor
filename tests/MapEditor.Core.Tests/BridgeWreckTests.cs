using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Generate;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;
using MapEditor.Core.Validate;

namespace MapEditor.Core.Tests;

/// <summary>Car wrecks on bridge decks: where they stand, that the same bridge always gets the same ones, and that they come and go with it.</summary>
public sealed class BridgeWreckTests
{
    private const int Size = 2048;

    /// <summary>The game's real sizes (x, y, z) for the wreck prefabs; the car blocks' spots are in <see cref="BridgeOp.WreckPrefabs"/>.</summary>
    private static readonly PrefabLibrary Library = PrefabLibrary.Of(
    [
        new PrefabInfo(BridgeOp.Tile, "POIs", new Int3(14, 14, 9), -1, 0, 0, ["rwgonly"], null),
        new PrefabInfo(BridgeOp.End, "POIs", new Int3(14, 14, 9), -1, 0, 0, ["rwgonly"], null),
        new PrefabInfo("player_start1", "POIs", new Int3(6, 1, 2), 0, 0, 0, ["navonly"], null),
        new PrefabInfo("player_start2", "POIs", new Int3(3, 1, 8), 0, 0, 0, ["navonly"], null),
        new PrefabInfo("player_start3", "POIs", new Int3(6, 1, 5), 0, 0, 0, ["navonly"], null),
        new PrefabInfo("player_start4", "POIs", new Int3(5, 1, 7), 0, 0, 0, ["navonly"], null),
        new PrefabInfo("player_start5", "POIs", new Int3(6, 1, 7), 0, 0, 0, ["navonly"], null),
        new PrefabInfo("player_start6", "POIs", new Int3(6, 1, 6), 0, 0, 0, ["navonly"], null),
        new PrefabInfo("part_ambulance_01", "Parts", new Int3(5, 3, 8), 0, 2, 0, ["part"], null),
        new PrefabInfo("part_armytruck_01", "Parts", new Int3(5, 3, 13), 0, 2, 0, ["part"], null),
    ]);

    private static readonly BridgeKit Kit = BridgeKit.From(Library)!;

    private static World Flat(int height = 34)
    {
        var world = new World { Name = "T", Size = Size, Height = new Grid<ushort>(Size, Size), Water = new Grid<byte>(Size, Size), Roads = new Grid<byte>(Size, Size) };
        for (int i = 0; i < world.Height.Data.Length; i++) world.Height.Data[i] = TerrainBrush.ToRaw(height);
        return world;
    }

    private static bool IsWreck(PrefabInstance p) => p.Name is not (BridgeOp.Tile or BridgeOp.End);

    private static List<PrefabInstance> Wrecks(World world) => world.Prefabs.Where(IsWreck).ToList();

    private static List<PrefabInstance> WrecksAlong(World world, BridgeLine line) =>
        [.. Wrecks(world).OrderBy(p => line.AlongZ ? p.Position.Z : p.Position.X)];

    /// <summary>The wreck's car block in grid metres: its prefab's spot at the turn it has.</summary>
    private static (int Along, int Across) CarBlock(World world, PrefabInstance p, BridgeLine line)
    {
        var spec = Kit.Wrecks.Single(w => w.Info.Name == p.Name);
        var (bx, bz) = BridgeOp.CarCell(spec, p.Rotation);
        int x = world.WorldToGrid(p.Position.X) + bx, z = world.WorldToGrid(p.Position.Z) + bz;
        return line.AlongZ ? (z, x - (line.Line - BridgeOp.Width / 2)) : (x, z - (line.Line - BridgeOp.Width / 2));
    }

    private static PlacedBridge Add(World world, BridgeLine line, bool wrecks = true, EditSession? session = null) =>
        BridgeTool.Add(world, Kit, [], line, 40, session ?? new EditSession("Bridge", world), wrecks);

    // ---- placement ----

    [Fact]
    public void A_bridge_gets_about_one_wreck_per_hundred_to_a_hundred_and_fifty_metres()
    {
        var world = Flat();
        var line = new BridgeLine(true, 700, 100, 50); // 450 m
        var bridge = Add(world, line);
        var wrecks = WrecksAlong(world, line);

        Assert.InRange(wrecks.Count, 3, 4);
        Assert.Equal(bridge.Prefabs.Count - 50, wrecks.Count);
        var spots = wrecks.Select(w => CarBlock(world, w, line).Along).ToList();
        for (int i = 1; i < spots.Count; i++) Assert.InRange(spots[i] - spots[i - 1], BridgeOp.WreckGapMin, BridgeOp.WreckGapMax);
        // Never on an end section, and the first about half a gap in.
        Assert.All(wrecks, w => Assert.InRange(w.Position.Z, world.GridToWorld(line.Start + BridgeOp.Piece), world.GridToWorld(line.End - BridgeOp.Piece)));
        Assert.InRange(spots[0] - line.Start, BridgeOp.WreckGapMin / 2, BridgeOp.WreckGapMax / 2);
    }

    [Fact]
    public void A_short_bridge_has_none_and_a_longer_one_scales()
    {
        var world = Flat();
        Add(world, new BridgeLine(true, 200, 100, 4)); // 36 m
        Assert.Empty(Wrecks(world));
        Add(world, new BridgeLine(true, 400, 100, 12)); // 108 m: one
        Assert.Single(Wrecks(world));
        var big = new BridgeLine(true, 700, 100, 100); // 900 m
        Add(world, big);
        Assert.InRange(Wrecks(world).Count(w => world.WorldToGrid(w.Position.X) > 600), 6, 9);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Wrecks_alternate_lanes_and_directions_and_stay_off_the_rails(bool alongZ)
    {
        var world = Flat();
        var line = new BridgeLine(alongZ, 900, 100, 100);
        Add(world, line);
        var wrecks = WrecksAlong(world, line);
        Assert.True(wrecks.Count >= 6);

        var lanes = wrecks.Select(w => CarBlock(world, w, line).Across).ToList();
        Assert.All(lanes, a => Assert.Contains(a, new[] { 4, 9 }));
        for (int i = 1; i < lanes.Count; i++) Assert.NotEqual(lanes[i - 1], lanes[i]);

        // Turned like the bridge pieces (north-south: 0 or 2; east-west: 1 or 3), and both ways appear.
        Assert.All(wrecks, w => Assert.Equal(alongZ ? 0 : 1, w.Rotation & 1));
        Assert.Equal(2, wrecks.Select(w => w.Rotation).Distinct().Count());

        // The footprint stays in blocks 1-12 across the 14-block deck, and the wreck's bottom is one above the deck's top block.
        foreach (var w in wrecks)
        {
            var (fx, fz) = Library.Find(w.Name)!.Footprint(w.Rotation);
            int across = (alongZ ? world.WorldToGrid(w.Position.X) : world.WorldToGrid(w.Position.Z)) - (line.Line - BridgeOp.Width / 2);
            int width = alongZ ? fx : fz;
            Assert.InRange(across, 1, 13 - width);
            Assert.Equal(40 + BridgeOp.Deck + 1, w.Position.Y);
        }
    }

    [Fact]
    public void The_mix_has_the_common_cars_and_now_and_then_the_ambulance_and_army_truck()
    {
        var world = Flat();
        var names = new HashSet<string>();
        // Many bridges, each its own draw.
        for (int i = 0; i < 40; i++)
            foreach (var w in BridgeOp.Build(world, new BridgePlan(Kit, true, 14 + i * 40, 100, 100, 40) { WreckSeed = 0 }, new EditSession("b", world)).Prefabs.Where(p => !p.Name.StartsWith("rwg_")))
                names.Add(w.Name);
        Assert.True(names.Count(n => n.StartsWith("player_start")) >= 5, string.Join(",", names));
        Assert.Contains("part_ambulance_01", names);
        Assert.Contains("part_armytruck_01", names);
    }

    // ---- the same bridge, the same wrecks ----

    [Fact]
    public void The_same_bridge_gets_the_same_wrecks_whatever_its_height_and_the_world()
    {
        var line = new BridgeLine(false, 500, 300, 60);
        var a = Flat();
        Add(a, line);
        var b = Flat(20);
        BridgeTool.Add(b, Kit, [], line, 55, new EditSession("Bridge", b));


        Assert.Equal(Shape(a, 0), Shape(b, 15)); // only the height differs
        Assert.NotEmpty(Wrecks(a));

        // Another place, another draw.
        var c = Flat();
        Add(c, line with { Line = 540 });
        Assert.NotEqual(Shape(a, 0), Shape(c, 0));
    }

    private static string Shape(World world, int heightShift) =>
        string.Join(";", Wrecks(world).OrderBy(p => p.Position.X).ThenBy(p => p.Position.Z)
            .Select(p => $"{p.Name}|{p.Position.X},{p.Position.Z},{p.Rotation}|{p.Position.Y - heightShift}"));

    [Fact]
    public void The_stage_salts_the_choice_with_its_seed()
    {
        var plan = new BridgePlan(Kit, true, 700, 100, 50, 40);
        string Run(int? seed)
        {
            var world = Flat();
            return string.Join(";", BridgeOp.Build(world, plan with { WreckSeed = seed }, new EditSession("b", world)).Prefabs.Skip(50).Select(p => $"{p.Name}|{p.X},{p.Z},{p.Rotation}"));
        }
        Assert.Equal(Run(7), Run(7));
        Assert.NotEqual(Run(7), Run(8));
        Assert.Equal("", Run(null));
    }

    // ---- part of the bridge ----

    [Fact]
    public void Wrecks_are_recorded_with_the_bridge_and_taken_down_with_it()
    {
        var world = Flat();
        var before = (ushort[])world.Height.Data.Clone();
        var line = new BridgeLine(true, 700, 100, 50);
        var bridges = new List<PlacedBridge>();
        var made = BridgeTool.Add(world, Kit, bridges, line, 40, new EditSession("Bridge", world));
        Assert.NotEmpty(Wrecks(world));
        Assert.All(Wrecks(world), w => Assert.Contains(made.Prefabs, r => r.Matches(w)));
        Assert.Equal(world.Prefabs.Count, made.Prefabs.Count);

        BridgeTool.Remove(world, bridges, made, new EditSession("Delete", world));
        Assert.Empty(world.Prefabs);
        Assert.Equal(before, world.Height.Data);
    }

    [Fact]
    public void Undo_and_redo_take_the_wrecks_out_and_put_them_back()
    {
        var world = Flat();
        var undo = new UndoStack();
        var runner = new EditRunner(undo);
        var bridges = new List<PlacedBridge>();
        Assert.True(runner.Run(world, "Bridge", s => BridgeTool.Add(world, Kit, bridges, new BridgeLine(true, 700, 100, 50), 40, s)));
        int all = world.Prefabs.Count;
        Assert.NotEmpty(Wrecks(world));
        undo.Undo();
        Assert.Empty(world.Prefabs);
        undo.Redo();
        Assert.Equal(all, world.Prefabs.Count);
        Assert.NotEmpty(Wrecks(world));
    }

    [Fact]
    public void Moving_and_resizing_a_bridge_gives_it_the_wrecks_of_its_new_place_and_rebuilding_in_place_keeps_them()
    {
        var world = Flat();
        var bridges = new List<PlacedBridge>();
        var line = new BridgeLine(true, 700, 100, 50);
        var first = BridgeTool.Add(world, Kit, bridges, line, 40, new EditSession("Bridge", world));
        string was = Shape(world, 0);

        var again = BridgeTool.Replace(world, Kit, bridges, first, line, 40, new EditSession("Same", world));
        Assert.Equal(was, Shape(world, 0));

        var longer = line with { Pieces = 70 };
        var moved = BridgeTool.Replace(world, Kit, bridges, again, longer, 40, new EditSession("Resize", world));
        Assert.Equal(moved.Prefabs.Count, world.Prefabs.Count);
        Assert.NotEqual(was, Shape(world, 0));

        var (rebuilt, dropped) = BridgeTool.RebuildEvery(world, Kit, bridges, new EditSession("All", world));
        Assert.Equal((1, 0), (rebuilt, dropped));
        Assert.Equal(bridges[0].Prefabs.Count, world.Prefabs.Count);
    }

    [Fact]
    public void With_the_option_off_a_bridge_has_no_wrecks()
    {
        var world = Flat();
        var made = Add(world, new BridgeLine(true, 700, 100, 50), wrecks: false);
        Assert.Empty(Wrecks(world));
        Assert.Equal(50, made.Prefabs.Count);

        // Rebuilding with it off drops the wrecks a bridge had; with it on, puts them back.
        var bridges = new List<PlacedBridge> { made };
        BridgeTool.RebuildEvery(world, Kit, bridges, new EditSession("All", world));
        Assert.NotEmpty(Wrecks(world));
        BridgeTool.RebuildEvery(world, Kit, bridges, new EditSession("All", world), wrecks: false);
        Assert.Empty(Wrecks(world));
    }

    [Fact]
    public void A_library_without_the_wreck_prefabs_builds_bare_decks()
    {
        var world = Flat();
        var bare = BridgeKit.From(PrefabLibrary.Of([Library.Find(BridgeOp.Tile)!, Library.Find(BridgeOp.End)!]))!;
        Assert.Empty(bare.Wrecks);
        var made = BridgeTool.Add(world, bare, [], new BridgeLine(true, 700, 100, 50), 40, new EditSession("Bridge", world));
        Assert.Equal(50, made.Prefabs.Count);
    }

    [Fact]
    public void A_click_on_a_wreck_finds_the_tools_bridge_and_the_stages_bridge()
    {
        var world = Flat();
        var bridges = new List<PlacedBridge>();
        var made = BridgeTool.Add(world, Kit, bridges, new BridgeLine(true, 700, 100, 50), 40, new EditSession("Bridge", world));
        var wreck = Wrecks(world)[0];
        Assert.Same(made, BridgeTool.Owner(bridges, wreck));
        Assert.Null(BridgeTool.StageOwner([], wreck));

        var stage = BridgeOp.Build(world, new BridgePlan(Kit, false, 1400, 100, 50, 40) { WreckSeed = 3 }, new EditSession("b", world));
        var stageWreck = world.Prefabs.First(p => IsWreck(p) && world.WorldToGrid(p.Position.Z) > 1300);
        Assert.Same(stage, BridgeTool.StageOwner([stage], stageWreck));
        Assert.Null(BridgeTool.Owner(bridges, stageWreck));
    }

    // ---- Validate ----

    [Fact]
    public void Validate_finds_a_bridge_with_wrecks_clean_and_does_not_take_a_wreck_for_a_piece()
    {
        var world = new World { Name = "T", Size = 1024, Height = new Grid<ushort>(1024, 1024), Water = new Grid<byte>(1024, 1024), Roads = new Grid<byte>(1024, 1024) };
        for (int z = 0; z < 1024; z++)
            for (int x = 0; x < 1024; x++)
            {
                bool sea = x >= 400 && x < 600;
                world.Height[x, z] = TerrainBrush.ToRaw(sea ? 18 : 34);
                if (sea) world.Water[x, z] = StampOp.DefaultWaterLevel;
            }
        var snap = BridgeTool.Snap(new System.Numerics.Vector2(388, 300), new System.Numerics.Vector2(612, 300), 1024)!.Value;
        var made = BridgeTool.Add(world, Kit, [], snap, BridgeTool.AutoY(world, snap), new EditSession("Bridge", world));
        var wrecks = Wrecks(world);
        Assert.NotEmpty(wrecks);
        // A wreck of each kind of tag (navonly cars, part-tagged ambulance and army truck) on the deck.
        Assert.Equal(made.Prefabs.Count, world.Prefabs.Count);

        var found = BridgeChecks.Find(world, Library);
        var one = Assert.Single(found.Bridges);
        Assert.Equal(snap, one.Where);
        Assert.Empty(found.Strays);

        var issues = WorldValidator.Validate(world, Library);
        Assert.DoesNotContain(issues, i => i.Kind is not null);
        Assert.DoesNotContain(issues, i => wrecks.Any(w => i.Message.Contains(w.Name)) || i.Message.Contains("under water") || i.Message.Contains("overlaps"));
    }
}
