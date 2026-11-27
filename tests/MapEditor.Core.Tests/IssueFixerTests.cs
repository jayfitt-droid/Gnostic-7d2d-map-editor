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

    private static readonly PrefabInfo Hut = new("hut", "POIs", new Int3(5, 5, 5), -1, 0, 1, [], null);

    /// <summary>
    /// The spawn search as it was before #284: every cell of each ring's square, and a scan of every prefab for each
    /// dry cell. The cell it picks, or null if it finds none.
    /// </summary>
    private static (int X, int Z)? SlowSpawnSpot(World world, PrefabLibrary library, SpawnPoint spawn)
    {
        int lo = IssueFixer.EdgeMargin, hi = world.Size - 1 - IssueFixer.EdgeMargin;
        int sx = Math.Clamp(world.WorldToGrid((int)MathF.Floor(spawn.X)), lo, hi);
        int sz = Math.Clamp(world.WorldToGrid((int)MathF.Floor(spawn.Z)), lo, hi);
        for (int r = 0; r <= IssueFixer.SpawnSearch; r++)
        {
            (int X, int Z)? best = null;
            int bestD = int.MaxValue;
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                    int x = sx + dx, z = sz + dz;
                    if (x < lo || z < lo || x > hi || z > hi || dx * dx + dz * dz >= bestD) continue;
                    bool dry = world.Water?[x, z] is not (> 0 and var w) || World.HeightToMetres(world.Height[x, z]) > w + 0.5f;
                    if (!dry || WorldQuery.PrefabAt(world, world.GridToWorld(x), world.GridToWorld(z), library) is not null) continue;
                    best = (x, z);
                    bestD = dx * dx + dz * dz;
                }
            if (best is not null) return best;
        }
        return null;
    }

    private static void Lake(World world, int x0, int z0, int x1, int z1)
    {
        for (int z = Math.Max(0, z0); z <= Math.Min(world.Size - 1, z1); z++)
            for (int x = Math.Max(0, x0); x <= Math.Min(world.Size - 1, x1); x++)
            {
                world.Height[x, z] = TerrainBrush.ToRaw(Ground - 5);
                world.Water![x, z] = Ground;
            }
    }

    /// <summary>A grid of huts, <paramref name="count"/> by <paramref name="count"/>, side by side from grid (x0, z0).</summary>
    private static void Town(World world, int x0, int z0, int count)
    {
        for (int i = 0; i < count; i++)
            for (int j = 0; j < count; j++)
                world.Prefabs.Add(new PrefabInstance { Name = Hut.Name, Position = new Int3(world.GridToWorld(x0 + 5 * i), Ground, world.GridToWorld(z0 + 5 * j)) });
    }

    [Fact]
    public void A_moved_spawn_lands_where_the_old_full_search_put_it()
    {
        const int size = 512;
        // Footprints that differ in x and z, so rotation counts, and a POI the library doesn't know (1 m square).
        var shed = new PrefabInfo("shed", "POIs", new Int3(3, 4, 7), -1, 0, 1, [], null);
        var block = new PrefabInfo("block", "POIs", new Int3(12, 6, 9), -1, 0, 1, [], null);
        var library = PrefabLibrary.Of([Hut, shed, block]);
        string[] names = [Hut.Name, shed.Name, block.Name, "not_in_the_library"];
        // Kept clear of random lakes and POIs: a square lake for ties, and a town to search several rings out of.
        GridRect[] kept = [new(90, 90, 150, 150), new(380, 380, 450, 450)];
        bool Clear(int x0, int z0, int x1, int z1) => kept.All(k => x1 < k.X0 || x0 > k.X1 || z1 < k.Z0 || z0 > k.Z1);
        int farthest = 0;

        for (int seed = 1; seed <= 6; seed++)
        {
            var rng = new Random(seed);
            var world = new World
            {
                Name = "T", Size = size, Height = new Grid<ushort>(size, size), Roads = new Grid<byte>(size, size), Water = new Grid<byte>(size, size),
            };
            for (int i = 0; i < world.Height.Data.Length; i++) world.Height.Data[i] = TerrainBrush.ToRaw(Ground);
            Lake(world, 110, 110, 130, 130);   // spawn at its centre: four cells tie 11 m out
            Town(world, 390, 390, 10);         // spawn at its centre: 25 m to the nearest free cell
            for (int n = 0; n < 30; n++)
            {
                int x = rng.Next(-20, size), z = rng.Next(-20, size), w = rng.Next(5, 60), d = rng.Next(5, 60);
                if (Clear(x, z, x + w, z + d)) Lake(world, x, z, x + w, z + d);
            }
            for (int n = 0; n < 400; n++)
            {
                int x = rng.Next(-15, size + 5), z = rng.Next(-15, size + 5);
                if (!Clear(x - 15, z - 15, x + 15, z + 15)) continue;
                world.Prefabs.Add(new PrefabInstance
                {
                    Name = names[rng.Next(names.Length)], Position = new Int3(world.GridToWorld(x), Ground, world.GridToWorld(z)), Rotation = rng.Next(4),
                });
            }

            var spawns = new List<SpawnPoint>
            {
                new() { X = world.GridToWorld(120), Y = Ground, Z = world.GridToWorld(120) },
                new() { X = world.GridToWorld(414) + 0.5f, Y = Ground, Z = world.GridToWorld(414) + 0.5f },
            };
            for (int n = 0; n < 40; n++)
                spawns.Add(new() { X = world.GridToWorld(rng.Next(-60, size + 60)) + (float)rng.NextDouble(), Y = Ground, Z = world.GridToWorld(rng.Next(-60, size + 60)) + (float)rng.NextDouble() });

            foreach (var spawn in spawns)
            {
                world.Spawns.Add(spawn);
                var expected = SlowSpawnSpot(world, library, spawn);
                Assert.NotNull(expected);
                var (sx, sz) = (world.WorldToGrid((int)MathF.Floor(spawn.X)), world.WorldToGrid((int)MathF.Floor(spawn.Z)));
                farthest = Math.Max(farthest, Math.Max(Math.Abs(expected.Value.X - sx), Math.Abs(expected.Value.Z - sz)));

                Assert.NotNull(IssueFixer.Apply(world, library, new IssueFix(FixKind.MoveSpawn, Spawn: spawn), new EditSession("Fix", world)));
                Assert.Equal(expected.Value, (world.WorldToGrid((int)spawn.X), world.WorldToGrid((int)spawn.Z)));
            }
            // Ties go to the first cell in the old scan's order: lowest z, then lowest x.
            Assert.Equal((120, 109), (world.WorldToGrid((int)spawns[0].X), world.WorldToGrid((int)spawns[0].Z)));
            Assert.Equal((414, 389), (world.WorldToGrid((int)spawns[1].X), world.WorldToGrid((int)spawns[1].Z)));
        }
        Assert.True(farthest >= 25, $"The farthest move was only {farthest} rings.");
    }

    [Fact]
    public void A_spawn_under_water_in_a_large_town_moves_at_once()
    {
        var world = Flat();
        var library = PrefabLibrary.Of([Hut, Trader]);
        Town(world, 352, 352, 64);   // 4096 huts, grid 352 to 671
        Lake(world, 510, 510, 514, 514);
        var spawn = new SpawnPoint { X = world.GridToWorld(512), Y = Ground, Z = world.GridToWorld(512) };
        world.Spawns.Add(spawn);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        Assert.NotNull(IssueFixer.Apply(world, library, new IssueFix(FixKind.MoveSpawn, Spawn: spawn), new EditSession("Fix", world)));
        clock.Stop();

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"The fix took {clock.Elapsed.TotalSeconds:0.0} s.");
        // Just past the town's high-x side: of the two nearest free cells, the one with the lower z.
        Assert.Equal((672, 512), (world.WorldToGrid((int)spawn.X), world.WorldToGrid((int)spawn.Z)));
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
