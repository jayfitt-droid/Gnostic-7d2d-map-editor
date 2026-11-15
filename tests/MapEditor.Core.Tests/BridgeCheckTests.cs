using System.Numerics;
using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;
using MapEditor.Core.Validate;

namespace MapEditor.Core.Tests;

/// <summary>Finding bridges from a world's prefabs and the Validate checks on them, including the game's navonly bridges.</summary>
public sealed class BridgeCheckTests
{
    private const int Size = 1024;
    private const int SeaFrom = 400, SeaTo = 600;

    private static readonly PrefabLibrary Library = PrefabLibrary.Of(
    [
        new PrefabInfo(BridgeOp.Tile, "POIs", new Int3(14, 14, 9), -1, 0, 0, ["rwgonly"], null),
        new PrefabInfo(BridgeOp.End, "POIs", new Int3(14, 14, 9), -1, 0, 0, ["rwgonly"], null),
        // The game's own bridges (Navezgane): placed by hand in its prefabs.xml, never by RWG.
        new PrefabInfo("bridge_concrete1", "POIs", new Int3(40, 12, 14), -1, 0, 0, ["navonly"], null),
        new PrefabInfo("departure_bridge_1_signs", "POIs", new Int3(20, 8, 6), -1, 0, 0, ["navonly"], null),
        // A deck wreck: a small POI the game never picks for a world.
        new PrefabInfo("player_start1", "POIs", new Int3(4, 2, 6), -1, 0, 0, ["navonly"], null),
        new PrefabInfo("plain_poi", "POIs", new Int3(10, 8, 10), -1, 0, 1, [], null),
    ]);

    private static readonly BridgeKit Kit = BridgeKit.From(Library)!;

    /// <summary>Land at 34 m with a strait of sea across x (or z), 12 m deep, and a little relief.</summary>
    private static World Strait(bool northSouth = false)
    {
        var world = new World { Name = "T", Size = Size, Height = new Grid<ushort>(Size, Size), Water = new Grid<byte>(Size, Size), Roads = new Grid<byte>(Size, Size) };
        for (int z = 0; z < Size; z++)
            for (int x = 0; x < Size; x++)
            {
                int c = northSouth ? z : x;
                bool sea = c >= SeaFrom && c < SeaTo;
                world.Height[x, z] = TerrainBrush.ToRaw(sea ? 18 + (x + z) % 3 : 34 + (x * 7 + z * 3) % 5 * 0.2f);
                if (sea) world.Water[x, z] = StampOp.DefaultWaterLevel;
            }
        return world;
    }

    /// <summary>A bridge across the sea along <paramref name="line"/> (z for east-west, x for north-south), built as the tool does.</summary>
    private static PlacedBridge Cross(World world, int line, bool northSouth = false)
    {
        var from = northSouth ? new Vector2(line, SeaFrom - 12) : new Vector2(SeaFrom - 12, line);
        var to = northSouth ? new Vector2(line, SeaTo + 12) : new Vector2(SeaTo + 12, line);
        var snap = BridgeTool.Snap(from, to, Size)!.Value;
        return BridgeTool.Add(world, Kit, [], snap, BridgeTool.AutoY(world, snap), new EditSession("Bridge", world));
    }

    private static List<ValidationIssue> BridgeIssues(World world) =>
        [.. WorldValidator.Validate(world, Library).Where(i => i.Kind is not null)];

    private static PrefabInstance Piece(World world, string name, int gx, int gz, int rotation, int y) =>
        new() { Name = name, Position = new Int3(world.GridToWorld(gx), y, world.GridToWorld(gz)), Rotation = rotation };

    // ---- finding bridges ----

    [Fact]
    public void A_tool_built_bridge_is_found_from_its_prefabs_alone()
    {
        var world = Strait();
        var built = Cross(world, 300);

        var (bridges, strays) = BridgeChecks.Find(world, Library);
        var found = Assert.Single(bridges);
        Assert.Empty(strays);
        Assert.Equal(built.Where, found.Where);
        Assert.Equal(built.Y, found.Y);
        Assert.Equal(built.DeckLevel, found.DeckLevel);
        Assert.Equal(BridgeOp.Beams, found.Underside - found.Y);
    }

    [Fact]
    public void North_south_and_east_west_bridges_and_two_parallel_ones_are_each_found()
    {
        var world = Strait();
        var a = Cross(world, 300);
        var b = Cross(world, 340);
        // And one running north-south across a land strip at the east side.
        var c = BridgeTool.Add(world, Kit, [], new BridgeLine(true, 800, 100, 6), 30, new EditSession("Bridge", world));

        var (bridges, strays) = BridgeChecks.Find(world, Library);
        Assert.Empty(strays);
        Assert.Equal(3, bridges.Count);
        Assert.Contains(bridges, f => f.Where == a.Where && f.Y == a.Y);
        Assert.Contains(bridges, f => f.Where == b.Where && f.Y == b.Y);
        Assert.Contains(bridges, f => f.Where == c.Where && f.Y == 30 && f.AlongZ);
        Assert.Equal(2, bridges.Count(f => !f.AlongZ));
    }

    [Fact]
    public void A_stray_piece_and_a_piece_at_another_height_belong_to_no_bridge()
    {
        var world = new World { Name = "T", Size = Size, Height = new Grid<ushort>(Size, Size) };
        // Three pieces in a row, but the middle one a metre higher: nothing is two level pieces in a line.
        world.Prefabs.Add(Piece(world, BridgeOp.End, 100, 200, 3, 30));
        world.Prefabs.Add(Piece(world, BridgeOp.Tile, 109, 200, 1, 31));
        world.Prefabs.Add(Piece(world, BridgeOp.End, 118, 200, 1, 30));
        // A lone piece, and a pair with a gap of one section between them.
        world.Prefabs.Add(Piece(world, BridgeOp.Tile, 500, 500, 0, 30));
        world.Prefabs.Add(Piece(world, BridgeOp.End, 700, 300, 3, 30));
        world.Prefabs.Add(Piece(world, BridgeOp.End, 718, 300, 1, 30));

        var (bridges, strays) = BridgeChecks.Find(world, Library);
        Assert.Empty(bridges);
        Assert.Equal(6, strays.Count);
        Assert.Equal(6, BridgeChecks.Analyze(world, Library).Strays.Count(i => i.Kind == BridgeIssue.Stray && i.Severity == IssueSeverity.Warning));
    }

    [Fact]
    public void Pieces_turned_across_the_line_do_not_join_a_bridge()
    {
        var world = new World { Name = "T", Size = Size, Height = new Grid<ushort>(Size, Size) };
        // An east-west pair (odd turns) next to each other along z: a different line, not a bridge.
        world.Prefabs.Add(Piece(world, BridgeOp.End, 100, 200, 3, 30));
        world.Prefabs.Add(Piece(world, BridgeOp.End, 100, 214, 1, 30));
        Assert.Empty(BridgeChecks.Find(world, Library).Bridges);
    }

    [Fact]
    public void Without_the_bridge_pieces_in_the_library_nothing_is_found()
    {
        var world = Strait();
        Cross(world, 300);
        Assert.Empty(BridgeChecks.Find(world, PrefabLibrary.Empty).Bridges);
        Assert.Empty(BridgeChecks.Analyze(world, PrefabLibrary.Empty).Issues);
    }

    // ---- clean bridges ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_clean_tool_built_bridge_with_ramps_has_no_bridge_issues(bool northSouth)
    {
        var world = Strait(northSouth);
        Cross(world, 300, northSouth);

        var report = Assert.Single(BridgeChecks.Analyze(world, Library).Bridges);
        Assert.Empty(report.Issues);
        Assert.Empty(BridgeIssues(world));
        Assert.True(report.WorstStep <= BridgeChecks.Tolerance);
        Assert.InRange(report.SteepestGrade!.Value, 0.02f, 0.12f); // the ramp eases, so its steepest part is about 1.5 times its grade
        Assert.Equal(BridgeOp.Clearance, report.MinClearance!.Value, 0.01);
    }

    [Fact]
    public void A_bridge_over_land_that_stands_on_its_banks_has_no_issues()
    {
        // A valley 20 m deep between two 40 m banks: the deck meets the banks, and the ground under the end pieces is the bank.
        var world = Strait();
        for (int z = 0; z < Size; z++)
            for (int x = 100; x < 300; x++)
                world.Height[x, z] = TerrainBrush.ToRaw(x is >= 140 and < 260 ? 20 : 40);
        var line = new BridgeLine(false, 500, 127, 15); // 127..262: both ends buried 13 m in the banks
        BridgeTool.Add(world, Kit, [], line, BridgeTool.AutoY(world, line), new EditSession("Bridge", world));

        Assert.Empty(BridgeIssues(world));
    }

    // ---- corrupted bridges ----

    private static ValidationIssue Single(World world, string kind)
    {
        var issues = BridgeIssues(world);
        return Assert.Single(issues, i => i.Kind == kind);
    }

    [Fact]
    public void A_bridge_issue_names_the_bridge_by_its_centre_axis_and_length()
    {
        var world = Strait();
        var built = Cross(world, 300);
        // Raise the ground past the east end by 1.8 m.
        int east = built.Where.End;
        for (int z = 280; z < 320; z++)
            for (int x = east; x < east + 3; x++)
                world.Height[x, z] = TerrainBrush.ToRaw(built.DeckLevel - 1.8f);

        var issue = Single(world, BridgeIssue.DeckEnd);
        int centreX = built.Start + built.Pieces * BridgeOp.Piece / 2 - Size / 2;
        Assert.StartsWith($"Bridge at ({centreX}, {300 - Size / 2}) E-W {built.Pieces * BridgeOp.Piece} m: east end deck 37.0 m, ground 35.2 m (step 1.8 m)", issue.Message);
        Assert.Equal(IssueSeverity.Warning, issue.Severity);
        Assert.True(issue.HasLocation);
    }

    [Fact]
    public void Ground_far_off_a_deck_end_is_an_error()
    {
        var world = Strait();
        var built = Cross(world, 300);
        for (int z = 280; z < 320; z++)
            for (int x = built.Start - 3; x < built.Start; x++)
                world.Height[x, z] = TerrainBrush.ToRaw(built.DeckLevel + 3);

        var issue = Single(world, BridgeIssue.DeckEnd);
        Assert.Equal(IssueSeverity.Error, issue.Severity);
        Assert.Contains("west end", issue.Message);
    }

    [Fact]
    public void An_end_with_no_ramp_road_has_no_road_reaching_it()
    {
        var world = Strait();
        var built = Cross(world, 300);
        // Take the west ramp's road out of the splat.
        for (int z = 280; z < 320; z++)
            for (int x = 0; x < built.Start; x++)
                world.Roads![x, z] = 0;

        var issue = Single(world, BridgeIssue.RoadReach);
        Assert.Contains("west end has no road", issue.Message);
        Assert.Equal(IssueSeverity.Warning, issue.Severity);
        Assert.DoesNotContain(BridgeIssues(world), i => i.Kind == BridgeIssue.DeckEnd);
    }

    [Fact]
    public void A_jump_in_the_road_beyond_an_end_is_flagged_with_the_steepest_grade()
    {
        var world = Strait();
        var built = Cross(world, 300);
        // A 3 m step 40 m out from the east end, across the road.
        for (int z = 280; z < 320; z++)
            for (int x = built.Where.End + 40; x < built.Where.End + 200; x++)
                world.Height[x, z] = (ushort)(world.Height[x, z] + TerrainBrush.ToRaw(3));

        var issues = BridgeIssues(world).Where(i => i.Kind == BridgeIssue.Approach).ToList();
        // About 3 m (the ramp eases on its own slope where the step is cut).
        Assert.Contains(issues, i => i.Message.Contains("east approach jumps 2.") || i.Message.Contains("east approach jumps 3."));
        Assert.Contains(issues, i => i.Message.Contains("east approach climbs"));
        Assert.DoesNotContain(issues, i => i.Message.Contains("west"));
        var report = Assert.Single(BridgeChecks.Analyze(world, Library).Bridges);
        Assert.True(report.SteepestGrade > BridgeChecks.MaxGrade);
    }

    [Fact]
    public void A_ramp_runs_on_until_it_meets_a_bank_that_keeps_rising_past_its_first_30_m()
    {
        // As bridge 6 of the 20-bridges test world: level with the deck for 30 m past the end, then a 5 m bank over
        // the next 10 m. A ramp sized from the ground 30 m out stopped short and left a 40% lip.
        var world = Strait();
        var line = BridgeTool.Snap(new Vector2(SeaFrom - 12, 300), new Vector2(SeaTo + 12, 300), Size)!.Value;
        int y = BridgeTool.AutoY(world, line);
        float deck = y + BridgeOp.Deck;
        for (int z = 270; z < 330; z++)
            for (int x = line.End; x < Size; x++)
            {
                float d = x - line.End;
                float rise = d < 30 ? 0 : d < 40 ? (d - 30) / 10 * 5 : 5;
                world.Height[x, z] = TerrainBrush.ToRaw(deck + rise);
            }
        float ramp = BridgeTool.RampLength(world, new Vector2(line.End, 300), new Vector2(1, 0), deck, Size - line.End - 1);
        Assert.True(ramp > 40, $"ramp {ramp} m stops before the bank");

        BridgeTool.Add(world, Kit, [], line, y, new EditSession("Bridge", world));
        var report = Assert.Single(BridgeChecks.Analyze(world, Library).Bridges);
        Assert.True(report.SteepestGrade <= BridgeChecks.MaxGrade, $"steepest grade {report.SteepestGrade:P0}");
        Assert.Empty(BridgeIssues(world));
    }

    [Fact]
    public void A_new_bridge_clears_a_hill_between_its_ends()
    {
        // As bridge 23 of the 20-bridges test world: an island in the middle of the strait higher than the banks. The
        // deck used to be set from the ends and the water only, and the hill came 2.5 m up through it.
        var world = Strait();
        for (int z = 280; z < 320; z++)
            for (int x = 480; x < 520; x++)
            {
                world.Height[x, z] = TerrainBrush.ToRaw(38);
                world.Water![x, z] = 0;
            }
        var line = BridgeTool.Snap(new Vector2(SeaFrom - 12, 300), new Vector2(SeaTo + 12, 300), Size)!.Value;
        int y = BridgeTool.AutoY(world, line);
        Assert.True(y + BridgeOp.Beams >= 38 + BridgeTool.HillClearance, $"beams at {y + BridgeOp.Beams} m");

        BridgeTool.Add(world, Kit, [], line, y, new EditSession("Bridge", world));
        Assert.DoesNotContain(BridgeIssues(world), i => i.Kind == BridgeIssue.DeckGround);
    }

    [Fact]
    public void Rebuild_all_brings_bridges_built_by_older_rules_up_to_date_as_one_undo_step()
    {
        // A bridge built at the height the old rule gave (water and ends only), over an island that comes up through it.
        var world = Strait();
        for (int z = 280; z < 320; z++)
            for (int x = 480; x < 520; x++)
            {
                world.Height[x, z] = TerrainBrush.ToRaw(38);
                world.Water![x, z] = 0;
            }
        var line = BridgeTool.Snap(new Vector2(SeaFrom - 12, 300), new Vector2(SeaTo + 12, 300), Size)!.Value;
        var bridges = new List<PlacedBridge>();
        int oldY = StampOp.DefaultWaterLevel + BridgeOp.Clearance - BridgeOp.Beams;
        BridgeTool.Add(world, Kit, bridges, line, oldY, new EditSession("Bridge", world));
        Assert.Contains(BridgeIssues(world), i => i.Kind == BridgeIssue.DeckGround);
        var heightBefore = world.Height.Data.ToArray();

        var session = new EditSession("Rebuild bridges", world);
        var (rebuilt, dropped) = BridgeTool.RebuildEvery(world, Kit, bridges, session);

        Assert.Equal((1, 0), (rebuilt, dropped));
        var now = Assert.Single(bridges);
        Assert.Equal(BridgeTool.AutoY(world, line), now.Y);
        Assert.True(now.Y > oldY);
        Assert.Equal(line, now.Where);
        Assert.Empty(BridgeIssues(world));

        // One undo step puts the old bridge back.
        session.ToStep().Swap();
        Assert.Equal(oldY, Assert.Single(bridges).Y);
        Assert.Equal(heightBefore, world.Height.Data);
    }

    [Fact]
    public void Two_bridges_that_cross_or_lie_on_one_line_are_an_error()
    {
        var world = Strait();
        var eastWest = Cross(world, 300);
        // A north-south bridge across it, and a second east-west one laid half over the first.
        BridgeOp.Build(world, new BridgePlan(Kit, true, SeaFrom + 100, 250, 12, eastWest.Y), new EditSession("N-S", world));
        BridgeOp.Build(world, new BridgePlan(Kit, false, 305, SeaFrom + 20, 6, eastWest.Y + 1), new EditSession("Along", world));

        var crossings = BridgeIssues(world).Where(i => i.Kind == BridgeIssue.Crossing).ToList();
        Assert.Contains(crossings, i => i.Severity == IssueSeverity.Error && i.Message.Contains("crosses the bridge at"));
        Assert.Contains(crossings, i => i.Message.Contains("runs into the bridge at"));

        // A single clean bridge has none.
        var alone = Strait();
        Cross(alone, 300);
        Assert.DoesNotContain(BridgeIssues(alone), i => i.Kind == BridgeIssue.Crossing);
    }

    [Fact]
    public void Ground_that_rises_to_the_beams_between_the_banks_is_flagged()
    {
        var world = Strait();
        var built = Cross(world, 300);
        // A hill under the middle of the deck whose top is 0.2 m under the beams.
        int mid = built.Start + built.Pieces * BridgeOp.Piece / 2;
        for (int z = 295; z < 305; z++)
            for (int x = mid - 5; x < mid + 5; x++)
                world.Height[x, z] = TerrainBrush.ToRaw(built.Y + BridgeOp.Beams - 0.2f);

        var issue = Single(world, BridgeIssue.DeckGround);
        Assert.Equal(IssueSeverity.Warning, issue.Severity);
        Assert.Contains("ground reaches the beams", issue.Message);

        // Higher than the deck, it buries the deck: an error.
        for (int z = 295; z < 305; z++)
            for (int x = mid - 5; x < mid + 5; x++)
                world.Height[x, z] = TerrainBrush.ToRaw(built.DeckLevel + 2);
        var buried = Single(world, BridgeIssue.DeckGround);
        Assert.Equal(IssueSeverity.Error, buried.Severity);
        Assert.Contains("above the deck", buried.Message);
    }

    [Fact]
    public void A_footing_that_is_lower_than_the_piece_bottom_leaves_pillars_floating()
    {
        var world = Strait();
        var built = Cross(world, 300);
        Assert.Empty(BridgeIssues(world));
        // Dig the footing under one row of pillars back to the sea floor.
        int row = built.Start + 5 * BridgeOp.Piece + BridgeOp.PierRow;
        for (int z = 290; z < 310; z++)
            for (int x = row - 2; x < row + 3; x++)
                world.Height[x, z] = TerrainBrush.ToRaw(19);

        var issue = Single(world, BridgeIssue.Footing);
        Assert.Contains("1 of", issue.Message);
        Assert.Contains($"{built.Y - 19:0.0} m under the piece bottom", issue.Message);
        Assert.Equal(IssueSeverity.Error, issue.Severity);

        // A small gap is a warning.
        for (int z = 290; z < 310; z++)
            for (int x = row - 2; x < row + 3; x++)
                world.Height[x, z] = TerrainBrush.ToRaw(built.Y - 1);
        Assert.Equal(IssueSeverity.Warning, Single(world, BridgeIssue.Footing).Severity);
    }

    [Fact]
    public void Water_up_under_the_deck_leaves_too_little_clearance()
    {
        var world = Strait();
        var built = Cross(world, 300);
        for (int z = 280; z < 320; z++)
            for (int x = SeaFrom; x < SeaTo; x++)
                world.Water![x, z] = StampOp.DefaultWaterLevel + 3;

        var issue = Single(world, BridgeIssue.Clearance);
        Assert.Contains("beams are 2.0 m above the water", issue.Message);
        Assert.Equal(2, BridgeChecks.Analyze(world, Library).Bridges[0].MinClearance!.Value, 0.01);
    }

    [Fact]
    public void A_road_passing_under_the_bridge_needs_room_too()
    {
        var world = Strait();
        var built = Cross(world, 300);
        // A road across the deck's line on a rise 2 m under the beams, well inside the middle sections.
        int at = built.Start + 10 * BridgeOp.Piece;
        for (int z = 200; z < 400; z++)
            for (int x = at; x < at + 6; x++)
            {
                world.Height[x, z] = TerrainBrush.ToRaw(built.Y + BridgeOp.Beams - 2);
                world.Roads![x, z] = (byte)RoadType.Asphalt;
            }

        var issue = Single(world, BridgeIssue.Clearance);
        Assert.Contains("2.0 m above a road passing under", issue.Message);
    }

    // ---- the checks that must leave bridges alone ----

    [Fact]
    public void A_navonly_bridge_over_water_is_not_flagged_as_under_water_or_overlapping()
    {
        var world = Strait();
        // Navezgane-style: a bridge prefab over the strait with its signs prefab on top of it.
        world.Prefabs.Add(Piece(world, "bridge_concrete1", 420, 300, 0, 28));
        world.Prefabs.Add(Piece(world, "departure_bridge_1_signs", 430, 302, 0, 38));

        var issues = WorldValidator.Validate(world, Library);
        Assert.DoesNotContain(issues, i => i.Message.Contains("under water") || i.Message.Contains("overlaps"));
    }

    [Fact]
    public void A_deck_wreck_on_a_bridge_is_not_flagged_but_an_ordinary_poi_is()
    {
        var world = Strait();
        var built = Cross(world, 300);
        int mid = built.Start + 10 * BridgeOp.Piece;
        // A wreck on the deck, as the next step places them: one above the deck's top block.
        world.Prefabs.Add(Piece(world, "player_start1", mid + 2, 300 - 3, 1, built.DeckLevel + 1));
        Assert.DoesNotContain(WorldValidator.Validate(world, Library), i => i.Message.Contains("player_start1"));

        // An ordinary POI over the same deck is still a problem.
        world.Prefabs.Add(Piece(world, "plain_poi", mid + 20, 300 - 5, 0, built.DeckLevel + 1));
        var issues = WorldValidator.Validate(world, Library);
        Assert.Contains(issues, i => i.Message.StartsWith("plain_poi is") && i.Message.Contains("under water"));
        Assert.Contains(issues, i => i.Message.Contains("overlaps plain_poi"));
    }

    // ---- the report ----

    [Fact]
    public void The_report_has_a_summary_a_table_and_a_line_per_issue()
    {
        var world = Strait();
        var good = Cross(world, 300);
        var bad = Cross(world, 340);
        for (int z = 320; z < 360; z++)
            for (int x = bad.Where.End; x < bad.Where.End + 3; x++)
                world.Height[x, z] = TerrainBrush.ToRaw(bad.DeckLevel - 1.8f);

        var issues = WorldValidator.Validate(world, Library);
        var bridges = BridgeChecks.Analyze(world, Library).Bridges;
        Assert.NotEqual(good.Where, bad.Where);

        string text = ValidationReport.Format(world, issues, bridges, bridgesOnly: false);
        var lines = text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("2 bridge(s) found. 0 errors, ", lines[0]);
        Assert.Contains("(bridge issues: deck-end 1, approach 2)", lines[0]); // the raised ground is a step in the road too
        Assert.Equal(2, lines.Count(l => l.Contains(" E-W ") && l.Contains("37 m") && !l.StartsWith("Warning")));
        Assert.Contains(lines, l => l.StartsWith("Warning") && l.Contains("east end deck 37.0 m, ground 35.2 m (step 1.8 m)") && l.Contains("(at "));
        Assert.Contains(lines, l => l.Contains("There is no trader"));

        // --bridges-only drops everything that isn't about a bridge.
        string only = ValidationReport.Format(world, issues, bridges, bridgesOnly: true);
        Assert.DoesNotContain("There is no trader", only);
        Assert.Contains("3 warnings (bridge issues: deck-end 1, approach 2)", only);
        Assert.Contains("3 issue(s).", only);

        var clean = Strait();
        Cross(clean, 300);
        string ok = ValidationReport.Format(clean, BridgeIssues(clean), BridgeChecks.Analyze(clean, Library).Bridges, bridgesOnly: true);
        Assert.Contains("No problems found.", ok);
        Assert.StartsWith("1 bridge(s) found. 0 errors, 0 warnings.", ok);
    }
}
