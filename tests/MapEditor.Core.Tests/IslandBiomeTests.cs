using MapEditor.Core.GameData;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Tests;

/// <summary>One biome per island (#46).</summary>
public sealed class IslandBiomeTests
{
    private const int Size = 4096;

    private static readonly List<BiomeShare> Shares = BiomeLayout.DefaultShares(BiomeTable.Vanilla);

    private static LandField Archipelago(int seed, int islands = 6) =>
        LandShapes.Build(Size, new LandShape { Kind = LandShapeKind.Archipelago, Seed = seed, Islands = islands, LandPercent = 35 })!;

    /// <summary>The biome colour at the middle of land cell (cx, cz).</summary>
    private static uint ColourAt(IndexedLayer layer, int cx, int cz)
    {
        int per = LandField.Cell / (Size / layer.Indices.Width);
        return layer.Palette[layer.Indices[cx * per + per / 2, cz * per + per / 2]];
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void Every_island_is_one_biome(int seed)
    {
        var land = Archipelago(seed);
        var (labels, islands) = land.Islands();
        Assert.True(islands.Count >= 2);
        var layer = BiomeLayout.PaintPerIsland(Size, Shares, land);

        var colours = new Dictionary<int, HashSet<uint>>();
        for (int cz = 0; cz < land.Width; cz++)
            for (int cx = 0; cx < land.Width; cx++)
                if (labels[cx, cz] >= 0)
                {
                    if (!colours.TryGetValue(labels[cx, cz], out var set)) colours[labels[cx, cz]] = set = [];
                    set.Add(ColourAt(layer, cx, cz));
                }
        Assert.Equal(islands.Count, colours.Count);
        Assert.All(colours.Values, set => Assert.Single(set));
    }

    private static (float X, float Z, int Cells) Isle(float x, float z, int cells) => (x, z, cells);

    [Fact]
    public void The_forest_island_is_the_one_whose_size_fits_forests_share()
    {
        // Forest 1 : wasteland 3. The big island sits in the middle, the small one off to the side: by distance alone
        // the big one would be forest; by area the small one fits forest's quarter.
        var islands = new[] { Isle(2048, 2048, 300), Isle(2600, 2048, 100) };
        Assert.Equal([1, 0], BiomeLayout.AssignIslands(islands, [1, 3], 2048, 2048));
    }

    [Fact]
    public void Islands_that_fit_equally_start_from_the_middle_and_go_outward_in_order()
    {
        // Five equal islands in a row, five equal shares: the middle one is forest, then outward one biome each.
        var islands = new[] { Isle(1000, 0, 50), Isle(1500, 0, 50), Isle(2000, 0, 50), Isle(2500, 0, 50), Isle(3000, 0, 50) };
        var steps = BiomeLayout.AssignIslands(islands, [1, 1, 1, 1, 1], 2000, 0);
        Assert.Equal(0, steps[2]);
        // Farther from the start, never an easier biome.
        var byDistance = Enumerable.Range(0, 5).OrderBy(i => Math.Abs(islands[i].X - 2000)).Select(i => steps[i]).ToList();
        Assert.Equal(byDistance.Order(), byDistance);
        Assert.Equal(4, byDistance[^1]);
        Assert.Equal([0, 1, 2, 3, 4], steps.Order());
    }

    [Fact]
    public void Each_biomes_land_follows_its_share_of_the_area()
    {
        // Shares 1 : 2 : 3 over islands of 10, 20 and 30 cells in that order outward: each biome gets the island its size.
        var islands = new[] { Isle(100, 0, 10), Isle(200, 0, 20), Isle(300, 0, 30) };
        Assert.Equal([0, 1, 2], BiomeLayout.AssignIslands(islands, [1, 2, 3], 100, 0));
        // With the big island nearest the start, the order outward still holds: every biome is used, in turn.
        var swapped = new[] { Isle(100, 0, 10), Isle(200, 0, 30), Isle(300, 0, 20) };
        Assert.Equal([0, 1, 2], BiomeLayout.AssignIslands(swapped, [1, 2, 3], 100, 0).Order());
    }

    [Fact]
    public void With_as_many_islands_as_biomes_every_biome_gets_one()
    {
        // The game's shares, and one island so big that by area alone it would cover two biomes' bands and leave one out.
        var islands = new[] { Isle(100, 0, 13), Isle(200, 0, 60), Isle(300, 0, 9), Isle(400, 0, 9), Isle(500, 0, 9) };
        Assert.Equal([0, 1, 2, 3, 4], BiomeLayout.AssignIslands(islands, [13, 18, 22, 23, 24], 100, 0));
    }

    [Fact]
    public void With_more_islands_than_biomes_every_biome_still_gets_one_in_order()
    {
        var islands = new[] { Isle(100, 0, 13), Isle(200, 0, 60), Isle(300, 0, 5), Isle(400, 0, 5), Isle(500, 0, 9), Isle(600, 0, 8), Isle(700, 0, 8) };
        var steps = BiomeLayout.AssignIslands(islands, [13, 18, 22, 23, 24], 100, 0);
        Assert.Equal([0, 1, 2, 3, 4], steps.Distinct().Order());
        Assert.Equal(steps.Order(), steps);   // these islands run outward in this order
    }

    [Fact]
    public void With_fewer_islands_than_biomes_each_island_gets_its_own()
    {
        var islands = new[] { Isle(100, 0, 20), Isle(200, 0, 40), Isle(300, 0, 40) };
        var steps = BiomeLayout.AssignIslands(islands, [13, 18, 22, 23, 24], 100, 0);
        Assert.Equal(3, steps.Distinct().Count());
        Assert.Equal(0, steps[0]);
        Assert.Equal(steps.Order(), steps);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(1234)]
    public void A_real_archipelago_with_enough_islands_uses_every_biome(int seed)
    {
        var land = Archipelago(seed, islands: 8);
        var (_, islands) = land.Islands();
        if (islands.Count < 5) return;
        var layer = BiomeLayout.PaintPerIsland(Size, Shares, land);
        Assert.Equal(5, layer.Palette.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void On_a_real_archipelago_forest_is_not_the_biggest_island(int seed)
    {
        // With the game's shares forest has the smallest part of the land.
        var land = Archipelago(seed);
        var (labels, islands) = land.Islands();
        if (islands.Count < 3) return;
        var layer = BiomeLayout.PaintPerIsland(Size, Shares, land);
        int biggest = Enumerable.Range(0, islands.Count).MaxBy(i => islands[i].Cells);
        for (int cz = 0; cz < land.Width; cz++)
            for (int cx = 0; cx < land.Width; cx++)
                if (labels[cx, cz] == biggest)
                {
                    Assert.NotEqual(BiomeLayout.ForestColor, ColourAt(layer, cx, cz));
                    return;
                }
    }

    [Fact]
    public void The_same_land_gives_the_same_biomes()
    {
        var a = BiomeLayout.PaintPerIsland(Size, Shares, Archipelago(3));
        var b = BiomeLayout.PaintPerIsland(Size, Shares, Archipelago(3));
        Assert.Equal(a.Indices.Data, b.Indices.Data);
        Assert.Equal(a.Palette, b.Palette);
    }

    [Fact]
    public void A_biome_with_no_share_is_skipped()
    {
        var shares = Shares.Select(s => s.Color == BiomeLayout.WastelandColor ? s with { Weight = 0 } : s).ToList();
        var layer = BiomeLayout.PaintPerIsland(Size, shares, Archipelago(7));
        Assert.DoesNotContain(BiomeLayout.WastelandColor, layer.Palette);
        Assert.Contains(BiomeLayout.ForestColor, layer.Palette);
    }

    [Fact]
    public void The_new_layout_is_saved_after_the_old_ones() =>
        // BiomeLayoutKind is saved as a number: the old members keep theirs.
        Assert.Equal(5, (int)BiomeLayoutKind.PerIsland);
}
