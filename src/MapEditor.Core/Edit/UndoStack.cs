using MapEditor.Core.Model;

namespace MapEditor.Core.Edit;

/// <summary>A rectangle of grid cells, [X0, X1) x [Z0, Z1).</summary>
public readonly record struct GridRect(int X0, int Z0, int X1, int Z1)
{
    public static readonly GridRect Empty = new(0, 0, 0, 0);

    public bool IsEmpty => X1 <= X0 || Z1 <= Z0;

    public bool Contains(int x, int z) => x >= X0 && x < X1 && z >= Z0 && z < Z1;

    public GridRect Union(GridRect other) =>
        IsEmpty ? other : other.IsEmpty ? this
        : new(Math.Min(X0, other.X0), Math.Min(Z0, other.Z0), Math.Max(X1, other.X1), Math.Max(Z1, other.Z1));

    public GridRect Clamp(int width, int height) =>
        new(Math.Max(X0, 0), Math.Max(Z0, 0), Math.Min(X1, width), Math.Min(Z1, height));

    /// <summary>Scales a rectangle from one grid resolution to another (e.g. biome cells to metres).</summary>
    public GridRect Scale(int factor) => new(X0 * factor, Z0 * factor, X1 * factor, Z1 * factor);

    public static GridRect AroundCircle(float cx, float cz, float radius) =>
        new((int)MathF.Floor(cx - radius), (int)MathF.Floor(cz - radius), (int)MathF.Ceiling(cx + radius) + 1, (int)MathF.Ceiling(cz + radius) + 1);
}

/// <summary>
/// Collects "before" copies of the grid tiles one edit (for example a brush stroke) touches, so the
/// edit can be undone. Call <see cref="Touch{T}"/> before changing cells. Given the world, it also notes its
/// layers and biome palette, so a layer or colour the edit adds is undone too.
/// </summary>
public sealed class EditSession(string name, World? world = null)
{
    internal const int TileSize = 64;
    private readonly Dictionary<(object Grid, int Tile), TileSnapshot> _tiles = [];
    private readonly Dictionary<object, int> _scales = [];
    private readonly LayerSnapshot? _layers = world is null ? null : new LayerSnapshot(world);

    public string Name { get; } = name;

    /// <summary>Bounds of everything touched, in world metres.</summary>
    public GridRect Bounds { get; private set; } = GridRect.Empty;

    /// <summary>
    /// Whether the edit changed anything: a list it touched, a layer or colour it added, or a cell that now differs
    /// from before. A brush dab or stamp that touched cells without changing them leaves no undo step (#22).
    /// </summary>
    public bool HasChanges => _layers?.Changed == true || _states.Values.Any(s => s.Differs) || _tiles.Values.Any(t => t.Differs);

    /// <summary>
    /// Puts back everything the edit changed so far, for an edit that failed partway. The session is empty afterwards;
    /// <see cref="Bounds"/> still says where it reached, for redrawing.
    /// </summary>
    public void Rollback()
    {
        foreach (var tile in _tiles.Values) tile.Swap();
        if (_layers?.Changed == true) _layers.Swap();
        foreach (var state in _states.Values) state.Swap();
        _tiles.Clear();
        _states.Clear();
    }

    /// <summary>
    /// Snapshots the tiles of <paramref name="grid"/> overlapping <paramref name="rect"/> (in that grid's
    /// cells) the first time they are touched. <paramref name="metresPerCell"/> converts to world bounds.
    /// </summary>
    public void Touch<T>(Grid<T> grid, GridRect rect, int metresPerCell = 1) where T : unmanaged
    {
        rect = rect.Clamp(grid.Width, grid.Height);
        if (rect.IsEmpty) return;
        grid.BeforeChange(rect);
        _scales[grid] = metresPerCell;
        int tilesAcross = (grid.Width + TileSize - 1) / TileSize;
        for (int tz = rect.Z0 / TileSize; tz <= (rect.Z1 - 1) / TileSize; tz++)
            for (int tx = rect.X0 / TileSize; tx <= (rect.X1 - 1) / TileSize; tx++)
            {
                var key = (grid, tx + tz * tilesAcross);
                if (!_tiles.ContainsKey(key)) _tiles[key] = TileSnapshot<T>.Capture(grid, tx, tz);
            }
        grid.MarkChanged();
        Bounds = Bounds.Union(rect.Scale(metresPerCell));
    }

    /// <summary>
    /// Snapshots a whole list (prefabs, spawns) the first time it is touched, so adding, removing or changing
    /// its items can be undone. <paramref name="bounds"/> (world metres) is what the change affects on the map.
    /// </summary>
    public void TouchList<T>(List<T> list, Func<T, T> clone, GridRect bounds) where T : class
    {
        var key = ((object)list, -1);
        if (!_tiles.ContainsKey(key)) _tiles[key] = new ListSnapshot<T>(list, clone);
        Bounds = Bounds.Union(bounds);
    }

    private readonly Dictionary<object, StateSnapshot> _states = [];

    /// <summary>
    /// Saves some state outside the world (the generator's Done marks, plans and status lines) the first time it is
    /// touched, so undo and redo swap it with the edit. <paramref name="capture"/> reads the state now and
    /// <paramref name="restore"/> puts a captured value back; both run on the thread that touches, undoes or rolls back,
    /// so call this from the UI thread. It counts as a change on its own only if a comparison is supplied.
    /// </summary>
    public void TouchState(object key, Func<object?> capture, Action<object?> restore, Func<object?, object?, bool>? same = null)
    {
        if (!_states.ContainsKey(key)) _states[key] = new StateSnapshot(capture, restore, same);
    }

    public UndoStep ToStep()
    {
        List<TileSnapshot> parts = [.. _tiles.Values];
        if (_layers?.Changed == true) parts.Add(_layers);
        parts.AddRange(_states.Values);
        return new(Name, parts, Bounds);
    }
}

/// <summary>State outside the world, captured before an edit; swapping exchanges it with the live state.</summary>
internal sealed class StateSnapshot(Func<object?> capture, Action<object?> restore, Func<object?, object?, bool>? same) : TileSnapshot
{
    private object? _stored = capture();

    public override long Bytes => 256;
    public override bool Differs => same is not null && !same(_stored, capture());

    public override void Swap()
    {
        var live = capture();
        restore(_stored);
        _stored = live;
    }
}

/// <summary>One undoable edit. <see cref="Swap"/> exchanges stored and live data, so it serves undo and redo.</summary>
public sealed class UndoStep(string name, List<TileSnapshot> tiles, GridRect bounds)
{
    public string Name { get; } = name;
    public GridRect Bounds { get; } = bounds;
    public long Bytes { get; } = tiles.Sum(t => t.Bytes);

    public void Swap()
    {
        foreach (var tile in tiles) tile.Swap();
    }
}

public abstract class TileSnapshot
{
    public abstract long Bytes { get; }
    public abstract void Swap();

    /// <summary>Whether the live data may differ from what was stored. Only a grid tile can tell for sure.</summary>
    public virtual bool Differs => true;
}

internal sealed class TileSnapshot<T> : TileSnapshot where T : unmanaged
{
    private readonly Grid<T> _grid;
    private readonly int _x0, _z0, _w, _h;
    private readonly T[] _data;

    private TileSnapshot(Grid<T> grid, int x0, int z0, int w, int h)
    {
        _grid = grid;
        (_x0, _z0, _w, _h) = (x0, z0, w, h);
        _data = new T[w * h];
    }

    public static TileSnapshot<T> Capture(Grid<T> grid, int tx, int tz)
    {
        int x0 = tx * EditSession.TileSize, z0 = tz * EditSession.TileSize;
        var snap = new TileSnapshot<T>(grid, x0, z0,
            Math.Min(EditSession.TileSize, grid.Width - x0), Math.Min(EditSession.TileSize, grid.Height - z0));
        for (int z = 0; z < snap._h; z++)
            grid.Row(z0 + z).Slice(x0, snap._w).CopyTo(snap._data.AsSpan(z * snap._w, snap._w));
        return snap;
    }

    public override long Bytes => (long)_data.Length * System.Runtime.CompilerServices.Unsafe.SizeOf<T>();

    public override bool Differs
    {
        get
        {
            for (int z = 0; z < _h; z++)
            {
                var live = System.Runtime.InteropServices.MemoryMarshal.AsBytes(_grid.Row(_z0 + z).Slice(_x0, _w));
                var stored = System.Runtime.InteropServices.MemoryMarshal.AsBytes(_data.AsSpan(z * _w, _w));
                if (!live.SequenceEqual(stored)) return true;
            }
            return false;
        }
    }

    public override void Swap()
    {
        _grid.BeforeChange(new GridRect(_x0, _z0, _x0 + _w, _z0 + _h));
        var row = new T[_w];
        for (int z = 0; z < _h; z++)
        {
            var live = _grid.Row(_z0 + z).Slice(_x0, _w);
            var stored = _data.AsSpan(z * _w, _w);
            live.CopyTo(row);
            stored.CopyTo(live);
            row.CopyTo(stored);
        }
        _grid.MarkChanged();
    }
}

/// <summary>
/// A world's layers and biome palette before an edit. An edit can add a layer (the first road on a world without
/// roads) or a palette colour; swapping puts the other set back.
/// </summary>
internal sealed class LayerSnapshot(World world) : TileSnapshot
{
    private IndexedLayer? _biomes = world.Biomes;
    private List<uint>? _palette = world.Biomes?.Palette.ToList();
    private Grid<byte>? _roads = world.Roads, _water = world.Water, _radiation = world.Radiation;

    public bool Changed =>
        !ReferenceEquals(_biomes, world.Biomes) || !ReferenceEquals(_roads, world.Roads) || !ReferenceEquals(_water, world.Water)
        || !ReferenceEquals(_radiation, world.Radiation)
        || _biomes is not null && !_biomes.Palette.SequenceEqual(_palette!);

    public override long Bytes => 64 + (_palette?.Count ?? 0) * 4L;

    public override void Swap()
    {
        var biomes = world.Biomes;
        var palette = biomes?.Palette.ToList();
        (_roads, world.Roads) = (world.Roads, _roads);
        (_water, world.Water) = (world.Water, _water);
        (_radiation, world.Radiation) = (world.Radiation, _radiation);
        world.Biomes = _biomes;
        if (_biomes is not null)
        {
            _biomes.Palette.Clear();
            _biomes.Palette.AddRange(_palette!);
        }
        _biomes = biomes;
        _palette = palette;
    }
}

/// <summary>The items of a list before an edit; items are cloned so later changes to live items cannot reach them.</summary>
internal sealed class ListSnapshot<T> : TileSnapshot where T : class
{
    private readonly List<T> _list;
    private List<T> _items;

    public ListSnapshot(List<T> list, Func<T, T> clone)
    {
        _list = list;
        _items = list.Select(clone).ToList();
    }

    public override long Bytes => _items.Count * 64L;

    public override void Swap()
    {
        // The live items are no longer in the world once swapped out, so they can be kept as they are.
        var live = _list.ToList();
        _list.Clear();
        _list.AddRange(_items);
        _items = live;
    }
}

/// <summary>Undo/redo history with a memory budget; the oldest steps are dropped first.</summary>
public sealed class UndoStack(long maxBytes = 1L << 30)
{
    private readonly LinkedList<UndoStep> _undo = new();
    private readonly Stack<UndoStep> _redo = new();
    private long _bytes;

    /// <summary>Stands for "no steps to undo" as a save point.</summary>
    private static readonly object Start = new();
    /// <summary>The newest undo step when the world was last saved (or <see cref="Start"/>); null when no undo or redo can get back there.</summary>
    private object? _saved = Start;

    /// <summary>Where the history is now, for <see cref="MarkSaved"/>. Take it before a save starts.</summary>
    public object Position => _undo.Last?.Value ?? Start;

    /// <summary>The world is as it was at the last save, as far as undo steps go (undo or redo back to it counts).</summary>
    public bool AtSavePoint => _saved is not null && ReferenceEquals(_saved, Position);

    /// <summary>Notes that the world as it was at <paramref name="position"/> is what was saved.</summary>
    public void MarkSaved(object position) => _saved = position;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoName => _undo.Last?.Value.Name;
    public string? RedoName => _redo.TryPeek(out var s) ? s.Name : null;

    public event EventHandler? Changed;

    public void Push(EditSession session)
    {
        if (!session.HasChanges) return;
        var step = session.ToStep();
        _undo.AddLast(step);
        _bytes += step.Bytes;
        foreach (var s in _redo) _bytes -= s.Bytes;
        _redo.Clear();
        while (_bytes > maxBytes && _undo.Count > 1)
        {
            var oldest = _undo.First!.Value;
            // Dropping the oldest step moves the bottom of the history up to just after it.
            if (ReferenceEquals(_saved, Start)) _saved = null;
            else if (ReferenceEquals(_saved, oldest)) _saved = Start;
            _bytes -= oldest.Bytes;
            _undo.RemoveFirst();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Undoes the latest step and returns it (for its bounds), or null if there is none.</summary>
    public UndoStep? Undo()
    {
        if (_undo.Last is not { } node) return null;
        _undo.RemoveLast();
        node.Value.Swap();
        _redo.Push(node.Value);
        Changed?.Invoke(this, EventArgs.Empty);
        return node.Value;
    }

    public UndoStep? Redo()
    {
        if (!_redo.TryPop(out var step)) return null;
        step.Swap();
        _undo.AddLast(step);
        Changed?.Invoke(this, EventArgs.Empty);
        return step;
    }

    /// <summary>Empties the history. The world as it is now becomes the save point (a world just opened or closed).</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _bytes = 0;
        _saved = Start;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
