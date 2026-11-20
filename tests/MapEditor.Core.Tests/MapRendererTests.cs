using MapEditor.Core.GameData;
using MapEditor.Core.Model;
using MapEditor.Core.Rendering;

namespace MapEditor.Core.Tests;

public sealed class MapRendererTests
{
    private static World FlatWorld(int size)
    {
        var world = new World { Name = "R", Size = size, Height = new Grid<ushort>(size, size) };
        Array.Fill(world.Height.Data, World.MetresToHeight(20));
        world.Water = new Grid<byte>(size, size);
        world.Roads = new Grid<byte>(size, size);
        return world;
    }

    [Theory]
    [InlineData(256, 1)]
    [InlineData(512, 2)]
    [InlineData(8192, 6)]
    [InlineData(10240, 7)]
    public void Top_level_fits_the_world_in_one_tile(int size, int levels)
    {
        Assert.Equal(levels, MapRenderer.LevelCount(size));
        var renderer = new MapRenderer(FlatWorld(size), BiomeTable.Vanilla);
        Assert.Equal(1, renderer.TilesAcross(levels - 1));
    }

    [Fact]
    public void Tiles_are_north_up()
    {
        var world = FlatWorld(512);
        world.Water![0, 511] = 30; // north-west corner
        var renderer = new MapRenderer(world, BiomeTable.Vanilla);
        var tile = new uint[MapRenderer.TileSize * MapRenderer.TileSize];

        renderer.RenderTile(0, 0, 0, MapLayers.Water, tile);

        Assert.True(IsBlueish(tile[0]));
        Assert.False(IsBlueish(tile[1]));
        Assert.False(IsBlueish(tile[(MapRenderer.TileSize - 1) * MapRenderer.TileSize]));
    }

    [Fact]
    public void Water_looks_the_same_over_any_biome()
    {
        // West half snow, east half burnt forest; water over the north half only.
        var world = FlatWorld(512);
        var indices = new Grid<byte>(64, 64);
        for (int z = 0; z < 64; z++)
            for (int x = 32; x < 64; x++)
                indices[x, z] = 1;
        world.Biomes = new IndexedLayer(indices, [0xFFFFFF, 0xBA00FF]);
        for (int z = 256; z < 512; z++)
            for (int x = 0; x < 512; x++)
                world.Water![x, z] = 30;
        var renderer = new MapRenderer(world, BiomeTable.Vanilla);
        var tile = new uint[MapRenderer.TileSize * MapRenderer.TileSize];

        renderer.RenderTile(1, 0, 0, MapLayers.All, tile);

        // Level 1 is 2 m per pixel and rows run north to south: pixel (x, row) is world (2x, 511 - 2row).
        uint Pixel(int x, int row) => tile[row * MapRenderer.TileSize + x];
        Assert.Equal(Pixel(50, 25), Pixel(200, 25));        // water over snow, water over burnt forest
        Assert.True(IsBlueish(Pixel(50, 25)));
        Assert.NotEqual(Pixel(50, 200), Pixel(200, 200));   // dry land still shows its biome
    }

    [Fact]
    public void Water_uses_the_coast_palette_at_each_depth()
    {
        var world = FlatWorld(256);
        const byte surface = 30;
        const int north = 255;
        // The 0–12 m depth range maps to the preview's −4 to −320 m shore distances.
        (float Depth, uint Colour)[] samples =
        [
            (0, 0xFF70A2BC), (12f * 28 / 316, 0xFF457699), (12f * 111 / 316, 0xFF2B4E6E),
            (12, 0xFF203952), (18, 0xFF203952),
        ];
        for (int x = 0; x < samples.Length; x++)
        {
            world.Height[x, north] = World.MetresToHeight(surface - samples[x].Depth);
            world.Water![x, north] = surface;
        }
        var renderer = new MapRenderer(world, BiomeTable.Vanilla);
        var tile = new uint[MapRenderer.TileSize * MapRenderer.TileSize];

        renderer.RenderTile(0, 0, 0, MapLayers.All, tile);

        Assert.Equal(samples.Select(s => s.Colour), tile.Take(samples.Length));

        renderer.RenderTile(0, 0, 0, MapLayers.All & ~MapLayers.Water, tile);

        for (int x = 0; x < samples.Length; x++)
            Assert.NotEqual(samples[x].Colour, tile[x]);
    }

    [Fact]
    public void Thin_roads_survive_at_coarse_levels()
    {
        var world = FlatWorld(2048);
        for (int z = 0; z < 2048; z++) world.Roads![1000, z] = (byte)RoadType.Asphalt;
        var renderer = new MapRenderer(world, BiomeTable.Vanilla);
        var tile = new uint[MapRenderer.TileSize * MapRenderer.TileSize];

        renderer.RenderTile(3, 0, 0, MapLayers.Roads, tile); // 8 m per pixel

        uint road = tile[1000 / 8];
        uint ground = tile[10];
        Assert.NotEqual(ground, road);
    }

    [Fact]
    public void Pixels_outside_the_world_are_transparent()
    {
        var renderer = new MapRenderer(FlatWorld(384), BiomeTable.Vanilla);
        var tile = new uint[MapRenderer.TileSize * MapRenderer.TileSize];
        renderer.RenderTile(0, 1, 1, MapLayers.All, tile);
        Assert.NotEqual(0u, tile[0]);
        Assert.Equal(0u, tile[200]);
        Assert.Equal(0u, tile[200 * MapRenderer.TileSize]);
    }

    private static bool IsBlueish(uint bgra) => (bgra & 0xFF) > ((bgra >> 16) & 0xFF) + 40;
}
