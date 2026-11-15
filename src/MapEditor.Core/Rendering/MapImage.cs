using MapEditor.Core.GameData;
using MapEditor.Core.IO;
using MapEditor.Core.Model;

namespace MapEditor.Core.Rendering;

/// <summary>What to put in a map image.</summary>
/// <param name="Size">Width and height in pixels; 0 (or more than the world) means the whole world at 1 pixel per metre.</param>
/// <param name="Layers">The terrain layers to draw.</param>
/// <param name="Pois">Draw prefab (POI) outlines on top.</param>
public sealed record MapImageOptions(int Size, MapLayers Layers, bool Pois);

/// <summary>
/// Renders a whole world to one square north-up picture, without any editor marks. Terrain comes from
/// <see cref="MapRenderer"/> at the mip level that fits the size, scaled down to it by area averaging; POI outlines
/// are drawn at the picture's scale. Rows are produced on demand, top to bottom, from a band of rows and one or two
/// strips of tiles, so a 16k picture never exists in memory whole.
/// </summary>
public sealed class MapImage
{
    private const int BandRows = 256;

    private readonly World _world;
    private readonly MapRenderer _renderer;
    private readonly MapImageOptions _options;
    private readonly PrefabLibrary _prefabLibrary;

    private readonly int _level;
    private readonly double _factor;     // source pixels per output pixel (at least 1)
    private readonly double _sourceSpan; // the world's width in source pixels
    private readonly int _sourceWidth;   // width of a rendered strip: tiles across * tile size
    private readonly (int First, int Count, float[] Weights)[] _columns;
    private readonly Strip?[] _strips = new Strip?[2]; // newest first
    private readonly uint[] _band;
    private readonly uint[] _overlay;
    private readonly List<PoiBox> _pois = [];
    private int _bandIndex = -1;

    private sealed class Strip(int ty, uint[] pixels)
    {
        public int Ty { get; } = ty;
        public uint[] Pixels { get; } = pixels;
    }

    private readonly record struct PoiBox(int X0, int Y0, int X1, int Y1, PoiKind Kind);
    private enum PoiKind { Street, Part, Poi, Trader }

    public MapImage(World world, BiomeTable biomes, PrefabLibrary prefabs, MapImageOptions options)
    {
        _world = world;
        _options = options;
        _prefabLibrary = prefabs;
        _renderer = new MapRenderer(world, biomes);
        Size = OutputSize(world.Size, options.Size);

        // The finest-enough level: the largest one that still has at least one source pixel per output pixel.
        _level = 0;
        while (_level + 1 < _renderer.Levels && (world.Size >> (_level + 1)) >= Size) _level++;
        _sourceSpan = (double)world.Size / (1 << _level);
        _factor = _sourceSpan / Size;
        _sourceWidth = _renderer.TilesAcross(_level) * MapRenderer.TileSize;
        _columns = new (int, int, float[])[Size];
        for (int ox = 0; ox < Size; ox++) _columns[ox] = Taps(ox, _sourceSpan, _sourceWidth);
        _band = new uint[Size * BandRows];
        _overlay = options.Pois ? new uint[Size * BandRows] : [];
        if (options.Pois) CollectPois();
    }

    /// <summary>Width and height of the picture.</summary>
    public int Size { get; }

    /// <summary>The picture's size for a requested one: the world's own size for 0, never larger than that.</summary>
    public static int OutputSize(int worldSize, int requested) => requested <= 0 ? worldSize : Math.Min(requested, worldSize);

    /// <summary>
    /// Writes the picture as a PNG with straight alpha. Run it off the UI thread; <paramref name="progress"/> gets 0..1
    /// as bands start.
    /// </summary>
    public void WritePng(string path, IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        var row = new uint[Size];
        Png.WriteRows(path, Size, Size, PngPixelFormat.Rgba8, (y, bytes) =>
        {
            cancel.ThrowIfCancellationRequested();
            if (y % BandRows == 0) progress?.Report((double)y / Size);
            ReadRow(y, row);
            for (int x = 0; x < row.Length; x++) ToStraightRgba(row[x], bytes.Slice(x * 4, 4));
        });
        progress?.Report(1);
    }

    /// <summary>Row <paramref name="y"/> (0 is the north edge) as premultiplied BGRA. Cheapest read top to bottom.</summary>
    public void ReadRow(int y, Span<uint> bgra)
    {
        if ((uint)y >= (uint)Size) throw new ArgumentOutOfRangeException(nameof(y));
        if (bgra.Length < Size) throw new ArgumentException("Row buffer too small.", nameof(bgra));
        int band = y / BandRows;
        if (band != _bandIndex) BuildBand(band);
        _band.AsSpan((y - band * BandRows) * Size, Size).CopyTo(bgra);
    }

    private static void ToStraightRgba(uint p, Span<byte> rgba)
    {
        uint a = p >> 24;
        uint r = (p >> 16) & 0xFF, g = (p >> 8) & 0xFF, b = p & 0xFF;
        if (a != 0 && a != 255)
        {
            r = Math.Min(255, (r * 255 + a / 2) / a);
            g = Math.Min(255, (g * 255 + a / 2) / a);
            b = Math.Min(255, (b * 255 + a / 2) / a);
        }
        rgba[0] = (byte)r; rgba[1] = (byte)g; rgba[2] = (byte)b; rgba[3] = (byte)a;
    }

    // ---- terrain -----------------------------------------------------------

    private void BuildBand(int band)
    {
        int y0 = band * BandRows, rows = Math.Min(BandRows, Size - y0);
        var accum = new float[Size * 4];
        for (int r = 0; r < rows; r++)
            ResampleRow(y0 + r, _band.AsSpan(r * Size, Size), accum);
        if (_options.Pois) DrawPois(y0, rows);
        _bandIndex = band;
    }

    /// <summary>The source pixels output pixel <paramref name="o"/> covers, with area weights that sum to 1.</summary>
    private (int First, int Count, float[] Weights) Taps(int o, double span, int available)
    {
        double a = o * _factor, b = Math.Min(span, (o + 1) * _factor);
        int first = Math.Min((int)Math.Floor(a), available - 1);
        int last = Math.Clamp((int)Math.Ceiling(b) - 1, first, available - 1);
        var w = new float[last - first + 1];
        double total = 0;
        for (int i = first; i <= last; i++)
        {
            double overlap = Math.Max(0, Math.Min(b, i + 1) - Math.Max(a, i));
            w[i - first] = (float)overlap;
            total += overlap;
        }
        if (total <= 0) { w[0] = 1; total = 1; }
        for (int i = 0; i < w.Length; i++) w[i] = (float)(w[i] / total);
        return (first, w.Length, w);
    }

    private void ResampleRow(int oy, Span<uint> output, float[] accum)
    {
        if (_factor == 1)
        {
            GetSourceRow(oy)[..Size].CopyTo(output);
            return;
        }
        var (first, count, weights) = Taps(oy, _sourceSpan, (int)Math.Ceiling(_sourceSpan));
        Array.Clear(accum);
        for (int i = 0; i < count; i++)
        {
            var src = GetSourceRow(first + i);
            float wy = weights[i];
            for (int ox = 0; ox < Size; ox++)
            {
                var (cx, cn, cw) = _columns[ox];
                float a = 0, r = 0, g = 0, b = 0;
                for (int k = 0; k < cn; k++)
                {
                    uint p = src[cx + k];
                    float w = cw[k];
                    a += (p >> 24) * w; r += ((p >> 16) & 0xFF) * w; g += ((p >> 8) & 0xFF) * w; b += (p & 0xFF) * w;
                }
                int j = ox * 4;
                accum[j] += a * wy; accum[j + 1] += r * wy; accum[j + 2] += g * wy; accum[j + 3] += b * wy;
            }
        }
        for (int ox = 0; ox < Size; ox++)
        {
            int j = ox * 4;
            output[ox] = (uint)(accum[j] + 0.5f) << 24 | (uint)(accum[j + 1] + 0.5f) << 16 | (uint)(accum[j + 2] + 0.5f) << 8 | (uint)(accum[j + 3] + 0.5f);
        }
    }

    /// <summary>A row of rendered source pixels. Keeps the last two strips of tiles, as neighbouring rows can straddle one.</summary>
    private ReadOnlySpan<uint> GetSourceRow(int sy)
    {
        int ty = sy / MapRenderer.TileSize;
        var strip = _strips[0]?.Ty == ty ? _strips[0] : _strips[1]?.Ty == ty ? _strips[1] : null;
        if (strip is null)
        {
            var pixels = _strips[1]?.Pixels ?? new uint[_sourceWidth * MapRenderer.TileSize];
            strip = RenderStrip(ty, pixels);
            _strips[1] = _strips[0];
            _strips[0] = strip;
        }
        return strip.Pixels.AsSpan((sy - ty * MapRenderer.TileSize) * _sourceWidth, _sourceWidth);
    }

    private Strip RenderStrip(int ty, uint[] pixels)
    {
        Parallel.For(0, _renderer.TilesAcross(_level), () => new uint[MapRenderer.TileSize * MapRenderer.TileSize], (tx, _, tile) =>
        {
            _renderer.RenderTile(_level, tx, ty, _options.Layers, tile);
            for (int py = 0; py < MapRenderer.TileSize; py++)
                tile.AsSpan(py * MapRenderer.TileSize, MapRenderer.TileSize)
                    .CopyTo(pixels.AsSpan(py * _sourceWidth + tx * MapRenderer.TileSize, MapRenderer.TileSize));
            return tile;
        }, _ => { });
        return new Strip(ty, pixels);
    }

    // ---- POIs --------------------------------------------------------------

    /// <summary>The footprints to outline in picture pixels, in the order the map view draws them: street tiles, POIs, traders.</summary>
    private void CollectPois()
    {
        int size = _world.Size, half = size / 2;
        double k = (double)Size / size;
        var all = _world.ShownPrefabs.ToArray();
        PoiBox Box(PrefabInstance p, PoiKind kind)
        {
            var (sx, sz) = _prefabLibrary.Find(p.Name)?.Footprint(p.Rotation) ?? (8, 8);
            double u = p.Position.X + half, v = size - (p.Position.Z + half + sz);
            return new PoiBox(Floor(u * k), Floor(v * k), Floor((u + sx) * k), Floor((v + sz) * k), kind);
        }
        foreach (var p in all)
            if (p.Name.StartsWith("rwg_tile_", StringComparison.Ordinal)) _pois.Add(Box(p, PoiKind.Street));
        var traders = new List<PoiBox>();
        foreach (var p in all)
        {
            if (p.Name.StartsWith("rwg_tile_", StringComparison.Ordinal)) continue;
            var info = _prefabLibrary.Find(p.Name);
            if (info?.IsPart == true)
            {
                // Parts are small props: outlined only when big enough to tell apart.
                if (k >= 0.5) _pois.Add(Box(p, PoiKind.Part));
            }
            else if (info?.IsTrader == true) traders.Add(Box(p, PoiKind.Trader));
            else _pois.Add(Box(p, PoiKind.Poi));
        }
        _pois.AddRange(traders);
    }

    private static int Floor(double v) => (int)Math.Floor(Math.Clamp(v, -1e6, 1e6));

    private static readonly uint Prefab = PoiColours.Premultiply(PoiColours.Prefab);
    private static readonly uint PrefabTint = PoiColours.Premultiply(PoiColours.PrefabTint);
    private static readonly uint Part = PoiColours.Premultiply(PoiColours.Part);
    private static readonly uint StreetTile = PoiColours.Premultiply(PoiColours.StreetTile);
    private static readonly uint StreetTileTint = PoiColours.Premultiply(PoiColours.StreetTileTint);
    private static readonly uint Trader = PoiColours.Premultiply(PoiColours.Trader);
    private static readonly uint TraderTint = PoiColours.Premultiply(PoiColours.TraderTint);
    private static readonly uint Halo = PoiColours.Premultiply(PoiColours.Halo);

    /// <summary>Draws the outlines into a transparent layer (later ones replace earlier, as in the map view), then lays it over the band.</summary>
    private void DrawPois(int y0, int rows)
    {
        Array.Clear(_overlay, 0, Size * rows);
        foreach (var p in _pois)
        {
            // Everything drawn reaches at most 3 px past its box.
            if (p.Y1 + 4 < y0 || p.Y0 - 4 > y0 + rows) continue;
            switch (p.Kind)
            {
                case PoiKind.Street:
                    Fill(y0, rows, p, StreetTileTint);
                    Outline(y0, rows, p, StreetTile, Halo);
                    break;
                case PoiKind.Part:
                    Outline(y0, rows, p, Part, null);
                    break;
                case PoiKind.Poi:
                    Fill(y0, rows, p, PrefabTint);
                    Outline(y0, rows, p, Prefab, Halo);
                    break;
                default:
                    Fill(y0, rows, p, TraderTint);
                    Outline(y0, rows, p, Trader, Halo);
                    if (p.X1 - p.X0 > 8 && p.Y1 - p.Y0 > 8)
                        Outline(y0, rows, p with { X0 = p.X0 + 1, Y0 = p.Y0 + 1, X1 = p.X1 - 1, Y1 = p.Y1 - 1 }, Trader, null);
                    else // keep them findable when small
                        Outline(y0, rows, p with { X0 = p.X0 - 3, Y0 = p.Y0 - 3, X1 = p.X1 + 3, Y1 = p.Y1 + 3 }, Trader, Halo);
                    break;
            }
        }
        for (int r = 0; r < rows; r++)
        {
            var dst = _band.AsSpan(r * Size, Size);
            var over = _overlay.AsSpan(r * Size, Size);
            for (int x = 0; x < Size; x++)
                if (over[x] != 0) dst[x] = Over(over[x], dst[x]);
        }
    }

    /// <summary>Premultiplied source-over.</summary>
    private static uint Over(uint s, uint d)
    {
        uint inv = 255 - (s >> 24);
        uint a = (s >> 24) + (d >> 24) * inv / 255;
        uint r = ((s >> 16) & 0xFF) + ((d >> 16) & 0xFF) * inv / 255;
        uint g = ((s >> 8) & 0xFF) + ((d >> 8) & 0xFF) * inv / 255;
        uint b = (s & 0xFF) + (d & 0xFF) * inv / 255;
        return Math.Min(a, 255u) << 24 | Math.Min(r, 255u) << 16 | Math.Min(g, 255u) << 8 | Math.Min(b, 255u);
    }

    // The same shapes the map view's OverlayRaster draws, in picture pixels.

    private void Fill(int bandY, int rows, PoiBox p, uint colour)
    {
        if (p.X1 - p.X0 < 3 || p.Y1 - p.Y0 < 3) return;
        FillClipped(bandY, rows, p.X0, p.Y0, p.X1, p.Y1, colour);
    }

    private void Outline(int bandY, int rows, PoiBox p, uint colour, uint? halo)
    {
        int x0 = p.X0, y0 = p.Y0, x1 = p.X1, y1 = p.Y1;
        if (halo is { } h)
        {
            if (x1 - x0 >= 4 && y1 - y0 >= 4)
            {
                Ring(bandY, rows, x0 - 1, y0 - 1, x1 + 1, y1 + 1, h);
                Ring(bandY, rows, x0 + 1, y0 + 1, x1 - 1, y1 - 1, h);
            }
            else FillClipped(bandY, rows, x0 - 1, y0 - 1, x0 + 3, y0 + 3, h);
        }
        if (x1 - x0 < 2 || y1 - y0 < 2)
        {
            FillClipped(bandY, rows, x0, y0, x0 + 2, y0 + 2, colour);
            return;
        }
        Ring(bandY, rows, x0, y0, x1, y1, colour);
    }

    private void Ring(int bandY, int rows, int x0, int y0, int x1, int y1, uint colour)
    {
        FillClipped(bandY, rows, x0, y0, x1, y0 + 1, colour);
        FillClipped(bandY, rows, x0, y1 - 1, x1, y1, colour);
        FillClipped(bandY, rows, x0, y0, x0 + 1, y1, colour);
        FillClipped(bandY, rows, x1 - 1, y0, x1, y1, colour);
    }

    private void FillClipped(int bandY, int rows, int x0, int y0, int x1, int y1, uint colour)
    {
        x0 = Math.Max(x0, 0); x1 = Math.Min(x1, Size);
        y0 = Math.Max(y0, bandY); y1 = Math.Min(y1, bandY + rows);
        if (x0 >= x1 || y0 >= y1) return;
        for (int y = y0; y < y1; y++)
            _overlay.AsSpan((y - bandY) * Size + x0, x1 - x0).Fill(colour);
    }
}
