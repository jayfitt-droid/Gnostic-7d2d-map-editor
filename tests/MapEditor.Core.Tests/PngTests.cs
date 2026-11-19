using MapEditor.Core.IO;

namespace MapEditor.Core.Tests;

public sealed class PngTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mapeditor-png-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Row_source_failure_preserves_the_existing_png()
    {
        string path = Path.Combine(_dir, "map.png");
        Png.WriteRgba(path, 1, 1, [1, 2, 3, 255]);
        byte[] original = File.ReadAllBytes(path);
        var failure = new InvalidOperationException("Row source failed.");

        var thrown = Assert.Throws<InvalidOperationException>(() => Png.WriteRows(path, 1, 3, PngPixelFormat.Rgba8, (y, row) =>
        {
            if (y == 1) throw failure;
            row.Fill(255);
        }));

        Assert.Same(failure, thrown);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(path, Assert.Single(Directory.EnumerateFileSystemEntries(_dir)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Successful_write_saves_the_png_without_a_temporary_file(bool overwrite)
    {
        string path = Path.Combine(_dir, "map.png");
        if (overwrite) Png.WriteRgba(path, 1, 1, [1, 2, 3, 255]);
        byte[] pixel = [4, 5, 6, 255];

        Png.WriteRows(path, 1, 1, PngPixelFormat.Rgba8, (_, row) => pixel.CopyTo(row));

        Assert.Equal(pixel, Png.ReadRgba(path, out _, out _));
        Assert.Equal(path, Assert.Single(Directory.EnumerateFileSystemEntries(_dir)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Rgba_round_trips_with_every_filter(int? filter)
    {
        const int w = 37, h = 23;
        var pixels = new byte[w * h * 4];
        new Random(filter ?? 99).NextBytes(pixels);
        string path = Path.Combine(_dir, $"rgba{filter}.png");

        Png.WriteRows(path, w, h, PngPixelFormat.Rgba8,
            (y, row) => pixels.AsSpan(y * w * 4, w * 4).CopyTo(row), forceFilter: filter);

        var decoded = Png.ReadRgba(path, out int dw, out int dh);
        Assert.Equal((w, h), (dw, dh));
        Assert.Equal(pixels, decoded);
    }

    [Fact]
    public void Zero_alpha_pixels_keep_their_colour()
    {
        // splat4 stores water height in B with A = 0; premultiplying would wipe it.
        byte[] pixels = [0, 0, 30, 0, 0, 0, 0, 0];
        string path = Path.Combine(_dir, "water.png");
        Png.WriteRgba(path, 2, 1, pixels);
        Assert.Equal(pixels, Png.ReadRgba(path, out _, out _));
    }

    [Fact]
    public void Gray16_round_trips()
    {
        const int w = 64, h = 5;
        var values = new ushort[w * h];
        for (int i = 0; i < values.Length; i++) values[i] = (ushort)(i * 977);
        string path = Path.Combine(_dir, "gray16.png");

        Png.WriteRows(path, w, h, PngPixelFormat.Gray16, (y, row) =>
        {
            for (int x = 0; x < w; x++) BitConverter.TryWriteBytes(row[(x * 2)..], values[y * w + x]);
        });

        var info = Png.ReadInfo(path);
        Assert.Equal((16, 0), (info.BitDepth, info.ColorType));
        var decoded = new ushort[w * h];
        Png.ReadRows(path, PngPixelFormat.Gray16, null, (y, row) =>
        {
            for (int x = 0; x < w; x++) decoded[y * w + x] = BitConverter.ToUInt16(row[(x * 2)..]);
        });
        Assert.Equal(values, decoded);
    }
}
