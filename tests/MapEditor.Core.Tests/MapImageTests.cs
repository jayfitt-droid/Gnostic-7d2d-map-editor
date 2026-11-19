using MapEditor.Core.GameData;
using MapEditor.Core.IO;
using MapEditor.Core.Model;
using MapEditor.Core.Rendering;

namespace MapEditor.Core.Tests;

public sealed class MapImageTests
{
    private static World FlatWorld(int size)
    {
        var world = new World { Name = "I", Size = size, Height = new Grid<ushort>(size, size) };
        Array.Fill(world.Height.Data, World.MetresToHeight(20));
        world.Water = new Grid<byte>(size, size);
        world.Roads = new Grid<byte>(size, size);
        return world;
    }

    private static MapImage Image(World world, int size, MapLayers layers = MapLayers.Water, bool pois = false, bool grid = false) =>
        new(world, BiomeTable.Vanilla, PrefabLibrary.Empty, new MapImageOptions(size, layers, pois, grid));

    private static uint[] Rows(MapImage image)
    {
        var all = new uint[image.Size * image.Size];
        for (int y = 0; y < image.Size; y++) image.ReadRow(y, all.AsSpan(y * image.Size, image.Size));
        return all;
    }

    private static bool IsBlueish(uint bgra) => (bgra & 0xFF) > ((bgra >> 16) & 0xFF) + 40;

    [Theory]
    [InlineData(512, 0, 512)]
    [InlineData(512, 256, 256)]
    [InlineData(512, 4096, 512)]   // never larger than the world
    [InlineData(1024, 300, 300)]
    public void Output_is_square_at_the_requested_size(int world, int requested, int expected)
    {
        var image = Image(FlatWorld(world), requested);
        Assert.Equal(expected, image.Size);
        Assert.Equal(expected * expected, Rows(image).Length);
    }

    [Theory]
    [InlineData(0)]   // full: level 0, copied
    [InlineData(256)] // two levels down
    [InlineData(300)] // not a power of two: scaled between levels
    public void North_is_at_the_top(int requested)
    {
        var world = FlatWorld(1024);
        for (int z = 768; z < 1024; z++)          // the northern quarter is sea
            for (int x = 0; x < 1024; x++) world.Water![x, z] = 30;
        var image = Image(world, requested);
        var px = Rows(image);
        int n = image.Size;

        Assert.True(IsBlueish(px[2 * n + n / 2]));
        Assert.False(IsBlueish(px[(n - 3) * n + n / 2]));
        Assert.False(IsBlueish(px[n / 2 * n + n / 2]));
    }

    [Fact]
    public void West_is_at_the_left()
    {
        var world = FlatWorld(512);
        for (int z = 0; z < 512; z++)
            for (int x = 0; x < 100; x++) world.Water![x, z] = 30;
        var px = Rows(Image(world, 128));
        Assert.True(IsBlueish(px[64 * 128 + 2]));
        Assert.False(IsBlueish(px[64 * 128 + 125]));
    }

    [Fact]
    public void Scaled_down_picture_is_opaque_and_matches_the_full_one_where_the_terrain_is_flat()
    {
        var world = FlatWorld(512);
        var full = Rows(Image(world, 0, MapLayers.None));
        var small = Rows(Image(world, 128, MapLayers.None));
        Assert.All(full, p => Assert.Equal(0xFFu, p >> 24));
        Assert.All(small, p => Assert.Equal(full[0], p));
    }

    [Fact]
    public void Water_in_the_north_west_corner_stays_there_when_scaled()
    {
        var world = FlatWorld(512);
        for (int z = 448; z < 512; z++)
            for (int x = 0; x < 64; x++) world.Water![x, z] = 30; // north-west 64 m square
        var px = Rows(Image(world, 128));
        Assert.True(IsBlueish(px[0]));
        Assert.True(IsBlueish(px[15 * 128 + 15]));
        Assert.False(IsBlueish(px[20 * 128 + 20]));
        Assert.False(IsBlueish(px[127 * 128 + 127]));
    }

    [Fact]
    public void Pois_are_outlined_only_when_asked()
    {
        var world = FlatWorld(512);
        // 8 m footprint (the default for a prefab the library doesn't know) in the far north, centred east.
        world.Prefabs.Add(new PrefabInstance { Name = "some_poi", Position = new Int3(0, 0, 248) });

        var without = Rows(Image(world, 0, MapLayers.None, pois: false));
        var with = Rows(Image(world, 0, MapLayers.None, pois: true));

        uint outline = PoiColours.Premultiply(PoiColours.Prefab);
        Assert.Equal(outline, with[256]);                    // top edge of the footprint: x 256, row 0
        Assert.NotEqual(outline, without[256]);
        Assert.Equal(without[300 * 512 + 100], with[300 * 512 + 100]);   // far from it nothing changes
    }

    [Fact]
    public void Pois_are_drawn_at_the_picture_scale()
    {
        var world = FlatWorld(1024);
        // The footprint's north-west corner is at x 512, row 0; at half scale its edge is still one pixel thick.
        world.Prefabs.Add(new PrefabInstance { Name = "some_poi", Position = new Int3(0, 0, 504) });
        var px = Rows(Image(world, 512, MapLayers.None, pois: true));
        Assert.Equal(PoiColours.Premultiply(PoiColours.Prefab), px[256]);
        Assert.Equal(PoiColours.Premultiply(PoiColours.Prefab), px[258]);
        Assert.NotEqual(PoiColours.Premultiply(PoiColours.Prefab), px[262]);
    }

    [Fact]
    public void Grid_is_off_by_default()
    {
        var world = FlatWorld(512);
        world.Prefabs.Add(new PrefabInstance { Name = "some_poi", Position = new Int3(0, 0, 248) });

        var omitted = new MapImage(world, BiomeTable.Vanilla, PrefabLibrary.Empty, new MapImageOptions(0, MapLayers.None, true));
        var off = Image(world, 0, MapLayers.None, pois: true, grid: false);

        Assert.Equal(Rows(omitted), Rows(off));
    }

    [Fact]
    public void Grid_draws_light_lines_and_dark_shadows()
    {
        var world = FlatWorld(512);
        var without = Rows(Image(world, 512, MapLayers.None));
        var with = Rows(Image(world, 512, MapLayers.None, grid: true));

        uint terrain = without[100 * 512 + 100] & 0xFF;
        uint line = 0xFF000000u | (120 + terrain * 135 / 255) * 0x010101u;
        uint shadow = 0xFF000000u | (terrain * 200 / 255) * 0x010101u;
        Assert.Equal(line, with[100 * 512 + 150]);
        Assert.Equal(shadow, with[100 * 512 + 151]);
        Assert.Equal(line, with[212 * 512 + 100]);
        Assert.Equal(shadow, with[213 * 512 + 100]);
        Assert.Equal(without[100 * 512 + 100], with[100 * 512 + 100]);
    }

    [Theory]
    [InlineData(512, new int[] { 0, 150, 300, 450 }, new int[] { 62, 212, 362, 511 })]
    [InlineData(256, new int[] { 0, 75, 150, 225 }, new int[] { 31, 106, 181, 255 })]
    [InlineData(16, new int[] { 0, 4, 9, 14 }, new int[] { 1, 6, 11, 15 })]
    public void Grid_is_counted_from_the_south_west_corner_at_picture_scale(int size, int[] vertical, int[] horizontal)
    {
        var world = FlatWorld(512);
        var without = Rows(Image(world, size, MapLayers.None));
        var with = Rows(Image(world, size, MapLayers.None, grid: true));
        int sample = size / 5; // A row and column clear of grid lines and shadows for each size.

        Assert.Equal(vertical, Enumerable.Range(0, size).Where(x => with[sample * size + x] > without[sample * size + x]));
        Assert.Equal(horizontal, Enumerable.Range(0, size).Where(y => with[y * size + sample] > without[y * size + sample]));
    }

    [Fact]
    public void Horizontal_grid_shadow_continues_into_the_next_band()
    {
        var world = FlatWorld(512);
        const int size = 362; // The line 150 m from the south edge scales to row 255.
        var without = Rows(Image(world, size, MapLayers.None));
        var with = Rows(Image(world, size, MapLayers.None, grid: true));

        Assert.True(with[255 * size + 100] > without[255 * size + 100]);
        Assert.True(with[256 * size + 100] < without[256 * size + 100]);
        Assert.Equal(without[257 * size + 100], with[257 * size + 100]);
    }

    [Fact]
    public void Pois_are_outlined_over_the_grid()
    {
        var world = FlatWorld(512);
        // The top of this 8 m footprint lies on the horizontal grid line at row 212.
        world.Prefabs.Add(new PrefabInstance { Name = "some_poi", Position = new Int3(-106, 0, 36) });
        var grid = Rows(Image(world, 512, MapLayers.None, grid: true));
        var both = Rows(Image(world, 512, MapLayers.None, pois: true, grid: true));

        int edge = 212 * 512 + 153;
        uint outline = PoiColours.Premultiply(PoiColours.Prefab);
        Assert.NotEqual(outline, grid[edge]);
        Assert.Equal(outline, both[edge]);
        Assert.Equal(grid[212 * 512 + 170], both[212 * 512 + 170]);
    }

    [Fact]
    public void Cancelled_export_preserves_the_existing_png()
    {
        string dir = Directory.CreateTempSubdirectory("mapeditor-mapimage-").FullName;
        string path = Path.Combine(dir, "map.png");
        try
        {
            Png.WriteRgba(path, 1, 1, [1, 2, 3, 255]);
            byte[] original = File.ReadAllBytes(path);
            var image = Image(FlatWorld(512), 256);

            Assert.Throws<OperationCanceledException>(() => image.WritePng(path, cancel: new CancellationToken(true)));

            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Equal(path, Assert.Single(Directory.EnumerateFileSystemEntries(dir)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Written_png_has_the_picture_size_and_is_opaque()
    {
        var world = FlatWorld(512);
        string path = Path.Combine(Path.GetTempPath(), "mapimage-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            Image(world, 256).WritePng(path, cancel: TestContext.Current.CancellationToken);
            var info = Png.ReadInfo(path);
            Assert.Equal((256, 256), (info.Width, info.Height));
            var rgba = Png.ReadRgba(path, out int w, out int h);
            Assert.Equal((256, 256), (w, h));
            Assert.Equal(255, rgba[3]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
