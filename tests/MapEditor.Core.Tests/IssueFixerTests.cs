using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;
using MapEditor.Core.Validate;

namespace MapEditor.Core.Tests;

public sealed class IssueFixerTests
{
    private const int Size = 1024;
    private const int Ground = 30;

    // A trader keeps "no trader" out of the list; YOffset -1 makes a POI at y = Ground stand on Ground.
    private static readonly PrefabInfo House = new("house", "POIs", new Int3(20, 10, 20), -1, 0, 1, [], null);
    private static readonly PrefabInfo Trader = new("trader_test", "POIs", new Int3(20, 10, 20), -1, 0, 1, [], null);
    private static readonly PrefabLibrary Library = PrefabLibrary.Of([House, Trader]);

    /// <summary>Flat ground at 30 m, with a spawn on dry land and a trader far from the tests' spots.</summary>
    private static World Flat()
    {
        var world = new World
        {
            Name = "T", Size = Size, Height = new Grid<ushort>(Size, Size), Roads = new Grid<byte>(Size, Size), Water = new Grid<byte>(Size, Size),
        };
        for (int i = 0; i < world.Height.Data.Length; i++) world.Height.Data[i] = TerrainBrush.ToRaw(Ground);
        world.Prefabs.Add(new PrefabInstance { Name = Trader.Name, Position = new Int3(world.GridToWorld(900), Ground, world.GridToWorld(900)) });
        world.Spawns.Add(new SpawnPoint { X = world.GridToWorld(880), Y = Ground + 1, Z = world.GridToWorld(880) });
        return world;
    }

    private static List<ValidationIssue> Validate(World world) => WorldValidator.Validate(world, Library);

    /// <summary>A house at grid (500, 500) with a road along its east side whose ground is 3 m higher than the house's.</summary>
    private static (World World, PrefabInstance House) HouseBelowRoad()
    {
        var world = Flat();
        var house = new PrefabInstance { Name = House.Name, Position = new Int3(world.GridToWorld(500), Ground, world.GridToWorld(500)) };
        world.Prefabs.Add(house);
        for (int z = 450; z < 570; z++)
            for (int x = 520; x < 530; x++)
            {
                world.Roads![x, z] = 1;
                world.Height[x, z] = TerrainBrush.ToRaw(Ground + 3);
            }
        return (world, house);
    }

    [Fact]
    public void A_road_off_a_pois_level_is_levelled_to_it_and_the_warning_goes()
    {
        var (world, house) = HouseBelowRoad();
        var issue = Assert.Single(Validate(world), i => i.Message.Contains("doesn't meet the road"));
        Assert.Equal(new IssueFix(FixKind.LevelRoad, house), issue.Fix);

        Assert.NotNull(IssueFixer.Apply(world, Library, issue.Fix!, new EditSession("Fix", world)));

        Assert.DoesNotContain(Validate(world), i => i.Message.Contains("doesn't meet the road"));
        // The road beside the house is at its level; far along the road it is as it was.
        Assert.Equal(Ground, World.HeightToMetres(world.Height[522, 510]), 0.05);
        Assert.Equal(Ground + 3, World.HeightToMetres(world.Height[525, 455]), 0.05);
        Assert.NotEqual(0, world.Roads![522, 510]);   // still a road
    }

    [Fact]
    public void A_stale_road_fix_for_a_bridge_leaves_the_heights_alone()
    {
        var world = Flat();
        var tile = new PrefabInfo(BridgeOp.Tile, "POIs", new Int3(14, 14, 9), -1, 0, 0, ["rwgonly"], null);
        var library = PrefabLibrary.Of([tile, Trader]);
        var bridge = new PrefabInstance { Name = tile.Name, Position = new Int3(world.GridToWorld(500), Ground + 20, world.GridToWorld(500)) };
        world.Prefabs.Add(bridge);
        for (int x = 450; x < 570; x++) world.Roads![x, 504] = (byte)RoadType.Asphalt;
        var before = (ushort[])world.Height.Data.Clone();

        var result = IssueFixer.Apply(world, library, new IssueFix(FixKind.LevelRoad, bridge), new EditSession("Fix", world));

        Assert.Equal(before, world.Height.Data);
        Assert.Null(result);
    }

    [Fact]
    public void A_road_fix_is_one_step_that_rolls_back()
    {
        var (world, _) = HouseBelowRoad();
        var before = (ushort[])world.Height.Data.Clone();
        var issue = Validate(world).Single(i => i.Fix is not null);
        var session = new EditSession("Fix", world);
        IssueFixer.Apply(world, Library, issue.Fix!, session);
        session.Rollback();
        Assert.Equal(before, world.Height.Data);
    }

    [Fact]
    public void A_spawn_under_water_moves_to_the_nearest_dry_ground()
    {
        var world = Flat();
        // A lake from x 300 to 400, 5 m deep; the spawn stands 30 m in from its west shore.
        for (int z = 0; z < Size; z++)
            for (int x = 300; x < 400; x++)
            {
                world.Height[x, z] = TerrainBrush.ToRaw(Ground - 5);
                world.Water![x, z] = Ground;
            }
        var spawn = new SpawnPoint { X = world.GridToWorld(330), Y = Ground, Z = world.GridToWorld(500) };
        world.Spawns.Add(spawn);

        var issue = Assert.Single(Validate(world), i => i.Message.Contains("under water"));
        Assert.Equal(new IssueFix(FixKind.MoveSpawn, Spawn: spawn), issue.Fix);
        IssueFixer.Apply(world, Library, issue.Fix!, new EditSession("Fix", world));

        Assert.DoesNotContain(Validate(world), i => i.Message.Contains("under water"));
        Assert.InRange(world.WorldToGrid((int)spawn.X), 295, 299);   // just over the west shore
        Assert.Equal(world.GridToWorld(500), spawn.Z);
        Assert.Equal(Ground + 1, spawn.Y);
    }

    [Fact]
    public void A_spawn_outside_the_world_moves_just_inside_it()
    {
        var world = Flat();
        var spawn = new SpawnPoint { X = world.GridToWorld(Size + 40), Y = Ground, Z = world.GridToWorld(200) };
        world.Spawns.Add(spawn);

        var issue = Assert.Single(Validate(world), i => i.Message.Contains("outside the world"));
        Assert.NotNull(issue.Fix);
        IssueFixer.Apply(world, Library, issue.Fix!, new EditSession("Fix", world));

        Assert.DoesNotContain(Validate(world), i => i.Message.Contains("outside the world"));
        Assert.InRange(world.WorldToGrid((int)spawn.X), Size - IssueFixer.EdgeMargin - 2, Size - 1);
        Assert.Equal(world.GridToWorld(200), spawn.Z);
    }

    [Fact]
    public void Warnings_without_a_safe_fix_offer_none()
    {
        var world = Flat();
        world.Prefabs.Add(new PrefabInstance { Name = House.Name, Position = new Int3(world.GridToWorld(100), Ground, world.GridToWorld(100)) });
        world.Prefabs.Add(new PrefabInstance { Name = House.Name, Position = new Int3(world.GridToWorld(110), Ground, world.GridToWorld(110)) });
        var overlap = Assert.Single(Validate(world), i => i.Message.Contains("overlaps"));
        Assert.Null(overlap.Fix);
    }

    [Fact]
    public void A_fixed_spawn_stays_in_the_teleport_list_at_its_new_place_until_the_fix_is_undone()
    {
        var world = Flat();
        var spawn = new SpawnPoint { X = world.GridToWorld(Size + 40), Y = Ground, Z = world.GridToWorld(200) };
        world.Spawns.Add(spawn);
        var issue = Assert.Single(Validate(world), i => i.Fix is not null);
        var session = new EditSession("Fix", world);
        string done = IssueFixer.Apply(world, Library, issue.Fix!, session)!;
        var fixedIssue = FixedIssue.Of(world, issue, done);

        // Outside the world the issue had no place; fixed, it is listed where the spawn is now (#59).
        var spot = Assert.Single(TeleportList.Build(world, Validate(world), Library, [fixedIssue]), s => s.Fixed.Count > 0);
        Assert.Empty(spot.Issues);
        Assert.Same(fixedIssue, Assert.Single(spot.Fixed));
        Assert.Equal((world.WorldToGrid((int)spawn.X), 200), (spot.X, spot.Z));
        Assert.NotNull(spot.Command);
        Assert.Contains("Fixed: " + issue.Message + " " + done, TeleportList.Markdown(world, [spot]));

        // Undone, Validate offers the fix again, so it is an open issue and not a fixed one.
        session.Rollback();
        var spots = TeleportList.Build(world, Validate(world), Library, [fixedIssue]);
        Assert.DoesNotContain(spots, s => s.Fixed.Count > 0);
        Assert.Contains(spots, s => s.Issues.Any(i => i.Message.Contains("outside the world")));
    }

    [Fact]
    public void A_fixed_road_shares_a_spot_with_an_issue_beside_it_and_drops_out_when_its_poi_is_gone()
    {
        var (world, house) = HouseBelowRoad();
        var issue = Validate(world).Single(i => i.Fix is not null);
        var fixedIssue = FixedIssue.Of(world, issue, IssueFixer.Apply(world, Library, issue.Fix!, new EditSession("Fix", world))!);
        Assert.Equal((issue.X, issue.Z), (fixedIssue.X, fixedIssue.Z));

        var near = new ValidationIssue(IssueSeverity.Warning, "Near", issue.X + 2, issue.Z - 2);
        var spot = Assert.Single(TeleportList.Build(world, [near], Library, [fixedIssue]));
        Assert.Same(near, Assert.Single(spot.Issues));
        Assert.Single(spot.Fixed);

        world.Prefabs.Remove(house);
        Assert.Empty(TeleportList.Build(world, [], Library, [fixedIssue]));
    }
}
