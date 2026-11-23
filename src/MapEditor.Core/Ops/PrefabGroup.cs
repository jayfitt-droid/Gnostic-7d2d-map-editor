using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;

namespace MapEditor.Core.Ops;

/// <summary>The horizontal destinations of a selected group, including the prefabs that ride with it.</summary>
public sealed record GroupLayout(
    IReadOnlyList<(PrefabInstance Prefab, PrefabSpot Spot)> Prefabs,
    IReadOnlyList<(SpawnPoint Spawn, double X, double Z, float Rotation)> Spawns);

/// <summary>Captures a map selection and its riders before any of them move.</summary>
public sealed class PrefabGroup
{
    private readonly World _world;
    private readonly PrefabLibrary _library;
    private readonly Func<PrefabInstance, bool> _isBridgePiece;
    private readonly HashSet<PrefabInstance> _prefabs;
    private readonly int _selectedPrefabCount;
    private readonly SpawnPoint[] _spawns;
    private readonly Dictionary<PrefabInstance, PrefabInstance> _riders = [];
    private readonly (double X, double Z) _centre;

    public PrefabGroup(World world, PrefabLibrary library, IEnumerable<PrefabInstance> prefabs, IEnumerable<SpawnPoint> spawns,
        Func<PrefabInstance, bool>? isBridgePiece = null)
    {
        _world = world;
        _library = library;
        _isBridgePiece = isBridgePiece ?? (_ => false);
        var selected = prefabs.ToHashSet();
        var selectedSpawns = spawns.ToHashSet();
        _prefabs = world.Prefabs.Where(selected.Contains).ToHashSet();
        _selectedPrefabCount = _prefabs.Count;
        _spawns = world.Spawns.Where(selectedSpawns.Contains).ToArray();

        // A whole tile carries its POIs and parts, even if one of them was also picked or sits on a picked POI.
        foreach (var parent in _prefabs.OrderByDescending(p => library.Find(p.Name)?.IsStreetTile == true))
        {
            if (_riders.ContainsKey(parent)) continue;
            foreach (var rider in PrefabPlacer.Contents(world, library, parent))
                _riders.TryAdd(rider, parent);
        }
        _prefabs.UnionWith(_riders.Keys);

        var footprints = _prefabs.Select(p => PrefabPlacer.Footprint(world, p, library)).ToArray();
        double half = world.Size / 2;
        if (footprints.Length == 0 && _spawns.Length > 0)
            _centre = (_spawns.Average(s => (double)s.X), _spawns.Average(s => (double)s.Z));
        else if (footprints.Length > 0)
            _centre = (
                (footprints.Select(r => r.X0 - half).Concat(_spawns.Select(s => (double)s.X)).Min() +
                 footprints.Select(r => r.X1 - half).Concat(_spawns.Select(s => (double)s.X)).Max()) / 2,
                (footprints.Select(r => r.Z0 - half).Concat(_spawns.Select(s => (double)s.Z)).Min() +
                 footprints.Select(r => r.Z1 - half).Concat(_spawns.Select(s => (double)s.Z)).Max()) / 2);
    }

    /// <summary>Finds footprints and spawn positions wholly inside the box, in grid metres with Z north.</summary>
    public static (IEnumerable<PrefabInstance> Prefabs, IEnumerable<SpawnPoint> Spawns) InBox(
        World world, PrefabLibrary library, double x0, double z0, double x1, double z1,
        Func<PrefabInstance, bool>? exclude = null)
    {
        double left = Math.Min(x0, x1), right = Math.Max(x0, x1);
        double bottom = Math.Min(z0, z1), top = Math.Max(z0, z1);
        int half = world.Size / 2;
        return (
            world.ShownPrefabs.Where(p =>
            {
                if (library.Find(p.Name) is { IsPart: true }) return false;
                var r = PrefabPlacer.Footprint(world, p, library);
                return r.X0 >= left && r.Z0 >= bottom && r.X1 <= right && r.Z1 <= top && exclude?.Invoke(p) != true;
            }),
            world.ShownSpawns.Where(s => s.X + half >= left && s.Z + half >= bottom && s.X + half <= right && s.Z + half <= top));
    }

    /// <summary>
    /// Applies one horizontal delta and a turn about the group's centre. A step is 90 degrees for a group with
    /// prefabs, or 45 degrees for a spawn-only group, as in Select. Positive steps turn counter-clockwise.
    /// </summary>
    public GroupLayout Layout(int dx, int dz, int steps)
    {
        int turns = steps & 3;
        var (cos, sin) = (_prefabs.Count, turns) switch
        {
            (0, _) => (Math.Cos((steps & 7) * Math.PI / 4), Math.Sin((steps & 7) * Math.PI / 4)),
            (_, 1) => (0d, 1d),
            (_, 2) => (-1d, 0d),
            (_, 3) => (0d, -1d),
            _ => (1d, 0d),
        };
        double offsetX = _centre.X - (_centre.X * cos - _centre.Z * sin);
        double offsetZ = _centre.Z - (_centre.X * sin + _centre.Z * cos);
        if (_prefabs.Count > 0)
        {
            // A tie shifts the centre by half a cell on that axis. Use the centre's half of the cell
            // to break the tie in the opposite direction on the inverse turn, even with fractional spawns.
            offsetX = _centre.X - Math.Floor(_centre.X) < 0.5 ? Math.Floor(offsetX + 0.5) : Math.Ceiling(offsetX - 0.5);
            offsetZ = _centre.Z - Math.Floor(_centre.Z) < 0.5 ? Math.Floor(offsetZ + 0.5) : Math.Ceiling(offsetZ - 0.5);
        }
        offsetX += dx;
        offsetZ += dz;
        var prefabs = new List<(PrefabInstance, PrefabSpot)>(_prefabs.Count);
        foreach (var p in _prefabs)
        {
            var (sx, sz) = _library.Find(p.Name)?.Footprint(p.Rotation) ?? (1, 1);
            int x = p.Position.X, z = p.Position.Z;
            (x, z) = turns switch
            {
                1 => (-z - sz, x),
                2 => (-x - sx, -z - sz),
                3 => (z, -x - sx),
                _ => (x, z),
            };
            prefabs.Add((p, new PrefabSpot(x + (int)offsetX, p.Position.Y, z + (int)offsetZ, (p.Rotation + turns) & 3)));
        }
        int degrees = steps * (_prefabs.Count > 0 ? 90 : 45);
        return new GroupLayout(prefabs, _spawns.Select(s => (s,
            s.X * cos - s.Z * sin + offsetX, s.X * sin + s.Z * cos + offsetZ,
            degrees == 0 ? s.RotationY : ((s.RotationY - degrees) % 360 + 360) % 360)).ToArray());
    }

    /// <summary>Moves or turns the whole group in one edit. Returns the reason if the destination is blocked.</summary>
    public string? Apply(int dx, int dz, int steps, bool flatten, bool replaceCovered, EditSession session)
    {
        if (dx == 0 && dz == 0 && steps % (_prefabs.Count > 0 ? 4 : 8) == 0) return null;
        if (MovementProblem() is { } problem) return problem;
        var layout = Layout(dx, dz, steps);
        double half = _world.Size / 2;
        if (layout.Spawns.Any(t => t.X + half < 0 || t.Z + half < 0 ||
                                   t.X + half >= _world.Size || t.Z + half >= _world.Size))
            return "The selection would move a spawn point outside the map.";

        var targets = layout.Prefabs.Select(t =>
        {
            var info = _library.Find(t.Prefab.Name)!;
            return (t.Prefab, t.Spot, Info: info,
                Cells: PrefabPlacer.Footprint(_world, info, t.Spot.X, t.Spot.Z, t.Spot.Rotation));
        }).ToArray();
        if (targets.Any(t => t.Cells != t.Cells.Clamp(_world.Size, _world.Size)))
            return "The selection would move a prefab outside the map.";

        var covered = new HashSet<PrefabInstance>();
        if (replaceCovered)
            foreach (var t in targets)
                covered.UnionWith(PrefabPlacer.Covered(_world, _library, t.Info, t.Spot, _prefabs));

        PrefabInstance? turnBlocker = null;
        if (targets.Length > 0)
            foreach (var p in _world.Prefabs)
            {
                if (_prefabs.Contains(p)) continue;
                var cells = PrefabPlacer.Footprint(_world, p, _library);
                if (!targets.Any(t => Overlaps(t.Cells, cells))) continue;
                if (_isBridgePiece(p)) return "The selection would overlap a bridge.";
                var info = _library.Find(p.Name);
                if (info?.IsBridge == true &&
                    (!covered.Contains(p) || !targets.Any(t => !t.Info.IsStreetTile && !t.Info.IsPart && Contains(t.Cells, cells))))
                    return "The selection would overlap a bridge.";
                if (!replaceCovered && info?.IsStreetTile != true)
                    turnBlocker ??= p;
            }

        var ignore = new HashSet<PrefabInstance>(_prefabs);
        ignore.UnionWith(covered);
        foreach (var t in targets)
            if (PrefabPlacer.TileInTheWay(_world, _library, t.Info, t.Spot, ignore) is { } tile)
                return $"The selection would partly overlap {tile.Name}.";
        if (turnBlocker is not null)
            return $"The selection would turn onto {turnBlocker.Name}.";

        var removed = new HashSet<PrefabInstance>(covered);
        foreach (var p in covered)
            removed.UnionWith(PrefabPlacer.Contents(_world, _library, p));
        removed.ExceptWith(_prefabs);
        if (removed.Any(_isBridgePiece))
            return "The selection would replace part of a bridge.";

        var roots = targets.Where(t => !_riders.ContainsKey(t.Prefab)).OrderBy(t => t.Info.IsStreetTile).ToArray();
        var tiles = roots.Where(t => t.Info.IsStreetTile).ToArray();
        int? tileGround = tiles.Select(t => PrefabPlacer.NeighbourTileGround(
                _world, _library, t.Info, t.Spot.X, t.Spot.Z, t.Spot.Rotation, ignore)).FirstOrDefault(h => h.HasValue)
            ?? tiles.Select(t => PrefabPlacer.RoadGround(_world, t.Cells)).FirstOrDefault(h => h.HasValue);
        if (tileGround is null && tiles.Length > 0)
        {
            var heights = tiles.Select(t => PrefabPlacer.MedianGround(_world, t.Cells)).Order().ToArray();
            tileGround = (heights[(heights.Length - 1) / 2] + heights[heights.Length / 2]) / 2;
        }

        var ground = roots.ToDictionary(t => t.Prefab, t => t.Info.IsStreetTile ? tileGround!.Value
            : (t.Info.IsPart ? null : PrefabPlacer.RoadGround(_world, t.Cells)) ?? PrefabPlacer.MedianGround(_world, t.Cells));
        var yDeltas = roots.ToDictionary(t => t.Prefab,
            t => PrefabPlacer.PrefabY(t.Info, ground[t.Prefab]) - t.Prefab.Position.Y);
        if (targets.Length > 0)
        {
            TouchPrefabs(session, _prefabs.Concat(removed), targets.Select(t => t.Cells));
            _world.Prefabs.RemoveAll(removed.Contains);
            foreach (var t in targets)
            {
                var root = _riders.GetValueOrDefault(t.Prefab, t.Prefab);
                t.Prefab.Position = t.Spot.Position with { Y = t.Prefab.Position.Y + yDeltas[root] };
                t.Prefab.Rotation = t.Spot.Rotation;
            }
        }

        if (flatten)
        {
            var surfaces = roots.Where(t => !t.Info.IsPart).ToArray();
            foreach (var t in surfaces)
                PrefabPlacer.Flatten(_world, t.Cells, ground[t.Prefab],
                    t.Info.IsStreetTile ? PrefabPlacer.StreetTileBlend : PrefabPlacer.PoiBlend, session);
            // A blend from one member can reach inside another. Restore their footprints, with tiles last.
            if (surfaces.Length > 1)
                foreach (var t in surfaces)
                {
                    var cells = t.Cells.Clamp(_world.Size, _world.Size);
                    if (cells.IsEmpty) continue;
                    ushort height = World.MetresToHeight(ground[t.Prefab]);
                    for (int z = cells.Z0; z < cells.Z1; z++)
                        _world.Height.Row(z)[cells.X0..cells.X1].Fill(height);
                }
        }

        foreach (var (spawn, x, z, rotation) in layout.Spawns)
        {
            float wx = (float)x, wz = (float)z;
            if (spawn.X != wx || spawn.Z != wz)
            {
                SpawnHelper.Move(_world, spawn, (int)Math.Floor(x + half), (int)Math.Floor(z + half), session);
                spawn.X = wx;
                spawn.Z = wz;
            }
            if (spawn.RotationY != rotation)
                SpawnHelper.Rotate(_world, spawn, rotation - spawn.RotationY, session);
        }
        return null;
    }

    /// <summary>Raises or lowers the selected prefabs and their riders. Returns a status message for the edit.</summary>
    public string? NudgeHeight(int blocks, EditSession session)
    {
        if (MovementProblem() is { } problem) return problem;
        if (blocks == 0) return null;
        if (_selectedPrefabCount == 0)
            return _spawns.Length == 0 ? null : "Spawn points can't be raised or lowered. Select a prefab.";

        TouchPrefabs(session, _prefabs, []);
        foreach (var p in _prefabs) p.Position = p.Position with { Y = p.Position.Y + blocks };
        string action = blocks > 0 ? "Raised" : "Lowered";
        long distance = Math.Abs((long)blocks);
        return $"{action} {_selectedPrefabCount} prefab{(_selectedPrefabCount == 1 ? "" : "s")} {distance} block{(distance == 1 ? "" : "s")}.";
    }

    /// <summary>Removes the selected prefabs, their riders and the selected spawn points.</summary>
    public string? Delete(EditSession session)
    {
        if (_prefabs.Any(_isBridgePiece)) return "The selection contains part of a bridge.";
        if (_prefabs.Count > 0)
        {
            TouchPrefabs(session, _prefabs, []);
            _world.Prefabs.RemoveAll(_prefabs.Contains);
        }
        foreach (var spawn in _spawns) SpawnHelper.Delete(_world, spawn, session);
        return null;
    }

    private static bool Overlaps(GridRect a, GridRect b) =>
        a.X0 < b.X1 && a.X1 > b.X0 && a.Z0 < b.Z1 && a.Z1 > b.Z0;

    private static bool Contains(GridRect box, GridRect item) =>
        box.X0 <= item.X0 && box.X1 >= item.X1 && box.Z0 <= item.Z0 && box.Z1 >= item.Z1;

    private string? MovementProblem()
    {
        if (_prefabs.Any(_isBridgePiece)) return "The selection contains part of a bridge.";
        var unknown = _prefabs.FirstOrDefault(p => _library.Find(p.Name) is null);
        return unknown is null ? null : $"{unknown.Name} is missing from the game folder's prefabs.";
    }

    private void TouchPrefabs(EditSession session, IEnumerable<PrefabInstance> before, IEnumerable<GridRect> after)
    {
        var cells = before.Select(p => PrefabPlacer.Footprint(_world, p, _library)).Concat(after)
            .Aggregate(GridRect.Empty, (bounds, r) => bounds.Union(r));
        session.TouchList(_world.Prefabs, p => p.Clone(), cells.Clamp(_world.Size, _world.Size));
    }
}
