using System.Runtime.InteropServices;
using System.Xml.Linq;
using MapEditor.Core.Model;

namespace MapEditor.Core.Edit;

/// <summary>
/// A copy-on-write snapshot of a world, for writing it on another thread while editing carries on (autosave).
/// Taking one copies only the small parts (prefabs, spawns, map info, the biome palette). Grid cells are read live,
/// and a tile is copied only when an edit is about to change it before the writer has reached it. That relies on
/// every in-place grid change calling <see cref="Grid{T}.BeforeChange"/> first, which <see cref="EditSession.Touch{T}"/>
/// and undo/redo do. Take it on the editing thread; dispose it once written.
/// </summary>
public sealed class WorldSnapshot : IDisposable
{
    private readonly Dictionary<object, IFrozenGrid> _grids = new(ReferenceEqualityComparer.Instance);

    private WorldSnapshot(World world)
    {
        // Layers are held by reference: a layer added after this point isn't in the snapshot, as it wasn't there.
        var copy = new World
        {
            Name = world.Name,
            Size = world.Size,
            Height = Freeze(world.Height),
            Biomes = world.Biomes is { } b ? new IndexedLayer(Freeze(b.Indices), [.. b.Palette]) : null,
            Roads = world.Roads is { } roads ? Freeze(roads) : null,
            Water = world.Water is { } water ? Freeze(water) : null,
            Radiation = world.Radiation is { } radiation ? Freeze(radiation) : null,
            MapInfo = new XDocument(world.MapInfo),
        };
        copy.Prefabs.AddRange(world.Prefabs.Select(p => p.Clone()));
        copy.Spawns.AddRange(world.Spawns.Select(s => s.Clone()));
        foreach (var (k, v) in world.PassthroughImages) copy.PassthroughImages[k] = v;
        foreach (var (k, v) in world.ExtraFiles) copy.ExtraFiles[k] = v;
        copy.Warnings.AddRange(world.Warnings);
        World = copy;
    }

    /// <summary>
    /// The world as it was, sharing the live grids. Read its grids' cells only through <see cref="Find{T}"/>,
    /// and their versions through <see cref="VersionOf{T}"/>.
    /// </summary>
    public World World { get; }

    public static WorldSnapshot Take(World world) => new(world);

    /// <summary>The frozen view of one of <see cref="World"/>'s grids, or null if it isn't one.</summary>
    public FrozenGrid<T>? Find<T>(Grid<T> grid) where T : unmanaged =>
        _grids.TryGetValue(grid, out var frozen) ? (FrozenGrid<T>)frozen : null;

    /// <summary>The grid's <see cref="Grid{T}.Version"/> when the snapshot was taken.</summary>
    public int VersionOf<T>(Grid<T> grid) where T : unmanaged => Find(grid)?.Version ?? grid.Version;

    /// <summary>Bytes of tiles kept so far because edits reached them before the writer did.</summary>
    public long KeptBytes => _grids.Values.Sum(g => g.KeptBytes);

    private Grid<T> Freeze<T>(Grid<T> grid) where T : unmanaged
    {
        if (!_grids.ContainsKey(grid)) _grids[grid] = new FrozenGrid<T>(grid);
        return grid;
    }

    public void Dispose()
    {
        foreach (var grid in _grids.Values) grid.Dispose();
    }
}

internal interface IFrozenGrid : IDisposable
{
    long KeptBytes { get; }
}

/// <summary>
/// One grid as it was when the snapshot was taken. Rows are read in bands of <see cref="Band"/> rows, top to bottom;
/// before an edit changes a tile in a band not yet written, the tile's old cells are kept.
/// </summary>
public sealed class FrozenGrid<T> : IFrozenGrid where T : unmanaged
{
    /// <summary>Tile edge in cells; matches undo's tiles, so one edit keeps the same tiles undo copies.</summary>
    public const int Band = EditSession.TileSize;

    private readonly Grid<T> _grid;
    private readonly int _tilesAcross, _bands;
    private readonly T[]?[] _kept;
    private readonly object _lock = new();
    private int _bandsWritten;
    private long _keptBytes;

    internal FrozenGrid(Grid<T> grid)
    {
        _grid = grid;
        Version = grid.Version;
        _tilesAcross = (grid.Width + Band - 1) / Band;
        _bands = (grid.Height + Band - 1) / Band;
        _kept = new T[]?[_tilesAcross * _bands];
        grid.AddWatcher(OnBeforeChange);
    }

    public int Version { get; }
    public int Width => _grid.Width;
    public int Height => _grid.Height;

    public long KeptBytes => Interlocked.Read(ref _keptBytes);

    private void OnBeforeChange(GridRect rect)
    {
        rect = rect.Clamp(_grid.Width, _grid.Height);
        if (rect.IsEmpty) return;
        lock (_lock)
        {
            for (int tz = Math.Max(rect.Z0 / Band, _bandsWritten); tz <= (rect.Z1 - 1) / Band; tz++)
                for (int tx = rect.X0 / Band; tx <= (rect.X1 - 1) / Band; tx++)
                {
                    int i = tx + tz * _tilesAcross;
                    if (_kept[i] is not null) continue;
                    int x0 = tx * Band, z0 = tz * Band;
                    int w = Math.Min(Band, _grid.Width - x0), h = Math.Min(Band, _grid.Height - z0);
                    var tile = new T[w * h];
                    for (int z = 0; z < h; z++) _grid.Row(z0 + z).Slice(x0, w).CopyTo(tile.AsSpan(z * w, w));
                    _kept[i] = tile;
                    Interlocked.Add(ref _keptBytes, (long)tile.Length * Marshal.SizeOf<T>());
                }
        }
    }

    /// <summary>
    /// Calls <paramref name="visit"/> with each band of rows (first row, cells) as they were, south to north.
    /// With <paramref name="final"/>, each band is marked written once read, so later edits there keep nothing; read
    /// it that way once, last.
    /// </summary>
    public void ReadBands(Action<int, ReadOnlySpan<T>> visit, bool final)
    {
        int width = _grid.Width;
        var buffer = new T[Band * width];
        for (int band = 0; band < _bands; band++)
        {
            int z0 = band * Band, rows = Math.Min(Band, _grid.Height - z0);
            var cells = buffer.AsSpan(0, rows * width);
            CopyBand(band, cells, final);
            visit(z0, cells);
        }
    }

    // Lets readers consume several layers in step without keeping a full copy of any raster.
    internal void CopyBand(int band, Span<T> cells, bool final)
    {
        int width = _grid.Width;
        int z0 = band * Band, rows = Math.Min(Band, _grid.Height - z0);
        lock (_lock)
        {
            _grid.Data.AsSpan(z0 * width, rows * width).CopyTo(cells);
            for (int tx = 0; tx < _tilesAcross; tx++)
            {
                if (_kept[tx + band * _tilesAcross] is not { } tile) continue;
                int x0 = tx * Band, w = Math.Min(Band, width - x0);
                for (int z = 0; z < rows; z++) tile.AsSpan(z * w, w).CopyTo(cells.Slice(z * width + x0, w));
            }
            if (final)
            {
                _bandsWritten = band + 1;
                for (int tx = 0; tx < _tilesAcross; tx++) _kept[tx + band * _tilesAcross] = null;
            }
        }
    }

    /// <summary>Writes the cells as they were, as raw little-endian samples (the layout of <see cref="Grid{T}.Data"/>).</summary>
    public void WriteTo(Stream stream) => ReadBands((_, cells) => stream.Write(MemoryMarshal.AsBytes(cells)), final: true);

    /// <summary>Whether the cells as they were equal <paramref name="cells"/> (laid out as <see cref="Grid{T}.Data"/>).</summary>
    public bool ContentEquals(T[] cells)
    {
        if (cells.Length != _grid.Data.Length) return false;
        bool equal = true;
        ReadBands((z0, band) =>
        {
            if (equal && !MemoryMarshal.AsBytes(band).SequenceEqual(MemoryMarshal.AsBytes(cells.AsSpan(z0 * _grid.Width, band.Length)))) equal = false;
        }, final: false);
        return equal;
    }

    public void Dispose() => _grid.RemoveWatcher(OnBeforeChange);
}
