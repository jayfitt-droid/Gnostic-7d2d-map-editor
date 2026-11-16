using MapEditor.Core.GameData;
using MapEditor.Core.Model;
using MapEditor.Core.Validate;

namespace MapEditor.Core.Tests;

public sealed class TeleportListTests
{
    private const int Size = 1024;

    private static World NewWorld() => new() { Name = "Test World", Size = Size, Height = new Grid<ushort>(Size, Size) };

    private static ValidationIssue Warn(string message, int x, int z) => new(IssueSeverity.Warning, message, x, z);

    [Fact]
    public void You_stand_south_of_the_spot_looking_north_when_that_side_is_clear()
    {
        var spot = Assert.Single(TeleportList.Build(NewWorld(), [Warn("A", 600, 400)], PrefabLibrary.Empty));
        // Grid (600, 400) is game (88, -112); 15 m south is z -127.
        Assert.Equal("teleport 88 -127 n", spot.Command);
    }

    [Fact]
    public void Water_on_the_south_side_moves_you_to_the_west_side()
    {
        var world = NewWorld();
        world.Water = new Grid<byte>(Size, Size);
        world.Water[600, 400 - TeleportList.Standoff] = 40;   // the ground there is 0 m, so it's under water

        var spot = Assert.Single(TeleportList.Build(world, [Warn("A", 600, 400)], PrefabLibrary.Empty));
        Assert.Equal("teleport 73 -112 e", spot.Command);
    }

    [Fact]
    public void A_prefab_on_the_south_and_west_sides_moves_you_north()
    {
        var world = NewWorld();
        var library = PrefabLibrary.Of([new PrefabInfo("block", "POIs", new Int3(30, 10, 30), 0, 0, 0, [], null)]);
        // South-west of the spot, covering both the south and the west standing points.
        world.Prefabs.Add(new PrefabInstance { Name = "block", Position = new Int3(world.GridToWorld(575), 0, world.GridToWorld(375)) });

        var spot = Assert.Single(TeleportList.Build(world, [Warn("A", 600, 400)], library));
        Assert.Equal("teleport 88 -97 s", spot.Command);
    }

    [Fact]
    public void At_the_map_edge_you_stand_on_a_side_that_is_on_the_map()
    {
        var spot = Assert.Single(TeleportList.Build(NewWorld(), [Warn("A", 5, 5)], PrefabLibrary.Empty));
        // South and west are off the map; north is the next side.
        Assert.Equal("teleport -507 -492 s", spot.Command);
    }

    [Fact]
    public void With_no_clear_side_you_stand_on_the_spot()
    {
        var world = NewWorld();
        world.Water = new Grid<byte>(Size, Size);
        foreach (var (x, z) in new[] { (600, 385), (585, 400), (600, 415), (615, 400) }) world.Water[x, z] = 40;

        var spot = Assert.Single(TeleportList.Build(world, [Warn("A", 600, 400)], PrefabLibrary.Empty));
        Assert.Equal("teleport 88 -112", spot.Command);
    }

    [Fact]
    public void Issues_at_the_same_place_share_one_spot_and_issues_without_a_place_get_no_command()
    {
        var issues = new[]
        {
            Warn("A", 600, 400),
            new ValidationIssue(IssueSeverity.Error, "No trader"),
            Warn("B", 602, 401),
            Warn("C", 900, 900),
        };
        var spots = TeleportList.Build(NewWorld(), issues, PrefabLibrary.Empty);

        Assert.Equal(3, spots.Count);
        Assert.Equal(["A", "B"], spots[0].Issues.Select(i => i.Message));
        Assert.Equal(["C"], spots[1].Issues.Select(i => i.Message));
        Assert.Null(spots[2].Command);
        Assert.Equal("No trader", Assert.Single(spots[2].Issues).Message);
    }

    [Fact]
    public void The_markdown_has_a_row_per_spot_and_lists_issues_without_a_place_last()
    {
        var issues = new[] { Warn("Overlaps | another", 600, 400), new ValidationIssue(IssueSeverity.Error, "No trader") };
        string md = TeleportList.Markdown(NewWorld(), TeleportList.Build(NewWorld(), issues, PrefabLibrary.Empty));

        Assert.Contains("# Teleport list: Test World", md);
        Assert.Contains("| 1 | Warning: Overlaps \\| another | `teleport 88 -127 n` |", md);
        Assert.Contains("## No place to go to", md);
        Assert.Contains("- Error: No trader", md);
    }

    [Fact]
    public void No_spawn_points_has_no_place_so_it_gets_no_command()
    {
        // Validate once placed it at the world centre: a double-click zoomed to 0, 0, and the list said teleport 0 0.
        var world = NewWorld();
        var issue = Assert.Single(WorldValidator.Validate(world, PrefabLibrary.Empty), i => i.Message.Contains("no spawn points"));
        Assert.False(issue.HasLocation);

        var spot = Assert.Single(TeleportList.Build(world, [issue], PrefabLibrary.Empty));
        Assert.Null(spot.Command);
        Assert.DoesNotContain("teleport", TeleportList.Markdown(world, [spot]));
    }
}
