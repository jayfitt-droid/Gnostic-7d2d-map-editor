using MapEditor.Core.GameData;
using MapEditor.Core.Model;

namespace MapEditor.Core.Tests;

public sealed class PrefabBiomeTests
{
    private const int Size = 1024;
    private const uint Forest = 0x004000, Desert = 0xFFE477, Burnt = 0xBA00FF, Unknown = 0x123456;

    /// <summary>West half forest, east half desert; biome cells are 8 m.</summary>
    private static World Land(params uint[] palette)
    {
        var world = new World { Name = "T", Size = Size, Height = new Grid<ushort>(Size, Size) };
        var biomes = new Grid<byte>(Size / 8, Size / 8);
        for (int z = 0; z < biomes.Height; z++)
            for (int x = biomes.Width / 2; x < biomes.Width; x++)
                biomes[x, z] = 1;
        world.Biomes = new IndexedLayer(biomes, palette.Length > 0 ? [.. palette] : [Forest, Desert]);
        return world;
    }

    private static PrefabInfo Info(int sx, int sz) => new("poi", "POIs", new Int3(sx, 10, sz), 0, 0, 1, [], null);

    private static PrefabInstance At(int wx, int wz, int rotation = 0) =>
        new() { Name = "poi", Position = new Int3(wx, 0, wz), Rotation = rotation };

    [Fact]
    public void Uses_the_rwg_tag_names()
    {
        var world = Land();
        Assert.Equal("forest", WorldQuery.BiomeOf(world, BiomeTable.Vanilla, At(-300, 0), Info(20, 20)));
        Assert.Equal("desert", WorldQuery.BiomeOf(world, BiomeTable.Vanilla, At(300, 0), Info(20, 20)));
        var burnt = Land(Burnt, Burnt);
        Assert.Equal("burntforest", WorldQuery.BiomeOf(burnt, BiomeTable.Vanilla, At(0, 0), Info(20, 20)));
    }

    [Fact]
    public void Reads_the_biome_at_the_centre_not_the_corner()
    {
        var world = Land();
        // The corner is 40 m into the forest; the centre of a 100 m prefab is 10 m into the desert.
        Assert.Equal("desert", WorldQuery.BiomeOf(world, BiomeTable.Vanilla, At(-40, 0), Info(100, 20)));
        Assert.Equal("forest", WorldQuery.BiomeOf(world, BiomeTable.Vanilla, At(-40, 0), Info(20, 20)));
    }

    [Fact]
    public void Turns_the_footprint_with_the_rotation()
    {
        var world = Land();
        // 20 m east-west and 100 m north-south; a quarter turn makes it 100 m east-west, so the centre crosses over.
        Assert.Equal("forest", WorldQuery.BiomeOf(world, BiomeTable.Vanilla, At(-40, 0), Info(20, 100)));
        Assert.Equal("desert", WorldQuery.BiomeOf(world, BiomeTable.Vanilla, At(-40, 0, rotation: 1), Info(20, 100)));
    }

    [Fact]
    public void Null_without_a_biome_map_or_for_an_unknown_colour()
    {
        var bare = new World { Name = "T", Size = Size, Height = new Grid<ushort>(Size, Size) };
        Assert.Null(WorldQuery.BiomeOf(bare, BiomeTable.Vanilla, At(0, 0), Info(20, 20)));
        Assert.Null(WorldQuery.BiomeOf(Land(Unknown, Unknown), BiomeTable.Vanilla, At(0, 0), Info(20, 20)));
    }

    [Fact]
    public void A_prefab_missing_from_the_library_uses_its_corner()
    {
        var world = Land();
        Assert.Equal("forest", WorldQuery.BiomeOf(world, BiomeTable.Vanilla, At(-1, 0), null));
        Assert.Equal("desert", WorldQuery.BiomeOf(world, BiomeTable.Vanilla, At(0, 0), null));
    }

    [Fact]
    public void A_prefab_on_the_world_edge_is_clamped_inside()
    {
        var world = Land();
        Assert.Equal("desert", WorldQuery.BiomeOf(world, BiomeTable.Vanilla, At(Size / 2 - 1, Size / 2 - 1), Info(50, 50)));
    }
}
