using System.Numerics;
using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;

namespace MapEditor.Core.Ops;

/// <summary>
/// Steps across a road: two neighbouring road pixels more than <see cref="PrefabPlacer.RoadStep"/> apart, which the game
/// shows as a wall in the road (#312). Each pixel takes the height of the nearest piece of its own road's centreline, so a
/// sharp turn on a slope, or a highway drawn over an earlier one, can leave one.
/// </summary>
public static class RoadSteps
{
    /// <summary>
    /// The highways stage eases steps bigger than this, in metres, within <see cref="ExitReach"/> of a street tile's exit,
    /// where a one-block step still shows as a ridge across the road into town. Elsewhere it eases, and Validate reports,
    /// steps over <see cref="PrefabPlacer.RoadStep"/>.
    /// </summary>
    public const float EaseStep = 0.5f;

    /// <summary>An eased step becomes a ramp that rises no more than this per metre.</summary>
    public const float EaseGrade = 0.25f;

    /// <summary>Most metres either side of a step that easing reaches.</summary>
    public const int MaxEase = 12;

    /// <summary>How many times <see cref="Ease"/> looks again for steps it left, such as one against a prefab's edge.</summary>
    private const int EasePasses = 3;

    /// <summary>Steps closer than this, in metres, are eased together.</summary>
    private const int Bucket = 32;

    /// <summary>How far from the middle of a street tile's edge (where its exit is), in metres, steps are eased and reported.</summary>
    public const int ExitReach = 60;

    /// <summary>
    /// A test for cells (indexed x + z * world size) within <see cref="ExitReach"/> of the middle of a street tile's edge,
    /// where a road meets its exit.
    /// </summary>
    public static Func<int, bool> NearStreetExit(World world, PrefabLibrary library)
    {
        const int bucket = 64;
        int size = world.Size;
        var exits = new Dictionary<(int, int), List<Vector2>>();
        foreach (var p in world.Prefabs)
        {
            if (library.Find(p.Name) is not { IsStreetTile: true }) continue;
            var f = PrefabPlacer.Footprint(world, p, library);
            float mx = (f.X0 + f.X1) / 2f, mz = (f.Z0 + f.Z1) / 2f;
            foreach (var e in new Vector2[] { new(mx, f.Z0), new(mx, f.Z1), new(f.X0, mz), new(f.X1, mz) })
            {
                var key = ((int)MathF.Floor(e.X / bucket), (int)MathF.Floor(e.Y / bucket));
                if (!exits.TryGetValue(key, out var list)) exits[key] = list = [];
                list.Add(e);
            }
        }
        return i =>
        {
            var at = new Vector2(i % size + 0.5f, i / size + 0.5f);
            int bx = (int)MathF.Floor(at.X / bucket), bz = (int)MathF.Floor(at.Y / bucket);
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    if (exits.TryGetValue((bx + dx, bz + dz), out var list) && list.Exists(e => Vector2.DistanceSquared(e, at) <= ExitReach * ExitReach))
                        return true;
            return false;
        };
    }

    /// <summary>
    /// A test for cells (indexed x + z * world size) under a prefab's footprint, where the game shows the prefab's own
    /// ground rather than the height map.
    /// </summary>
    public static Func<int, bool> UnderPrefab(World world, PrefabLibrary library)
    {
        const int bucket = 64;
        int size = world.Size;
        var footprints = new Dictionary<(int, int), List<GridRect>>();
        foreach (var p in world.Prefabs)
        {
            var f = PrefabPlacer.Footprint(world, p, library).Clamp(size, size);
            if (f.IsEmpty) continue;
            for (int bz = f.Z0 / bucket; bz <= (f.Z1 - 1) / bucket; bz++)
                for (int bx = f.X0 / bucket; bx <= (f.X1 - 1) / bucket; bx++)
                {
                    if (!footprints.TryGetValue((bx, bz), out var list)) footprints[(bx, bz)] = list = [];
                    list.Add(f);
                }
        }
        return i =>
        {
            int x = i % size, z = i / size;
            return footprints.TryGetValue((x / bucket, z / bucket), out var list) && list.Exists(f => f.Contains(x, z));
        };
    }

    /// <summary>
    /// The road pixels in <paramref name="area"/> more than <paramref name="over"/> metres above or below the road pixel
    /// east or north of them, with the larger difference in metres. A pair with a cell where <paramref name="skip"/> is
    /// true (indexed x + z * world size) is left out.
    /// </summary>
    public static List<(int X, int Z, float Step)> Find(World world, GridRect area, Func<int, bool>? skip = null,
        float over = PrefabPlacer.RoadStep)
    {
        var found = new List<(int X, int Z, float Step)>();
        if (world.Roads is not { } roads) return found;
        area = area.Clamp(world.Size, world.Size);
        int size = world.Size;
        var paint = roads.Data;
        var height = world.Height.Data;
        for (int z = area.Z0; z < area.Z1; z++)
            for (int x = area.X0; x < area.X1; x++)
            {
                int i = x + z * size;
                if (!RoadPaint.IsRoad(paint[i]) || skip?.Invoke(i) == true) continue;
                float step = Math.Max(x + 1 < size ? Difference(i, i + 1) : 0, z + 1 < size ? Difference(i, i + size) : 0);
                if (step > over) found.Add((x, z, step));
            }
        return found;

        float Difference(int a, int b) => RoadPaint.IsRoad(paint[b]) && skip?.Invoke(b) != true
            ? MathF.Abs(World.HeightToMetres(height[a]) - World.HeightToMetres(height[b]))
            : 0;
    }

    /// <summary>
    /// Groups steps into spots: steps within <see cref="Bucket"/> m of each other (through a chain of steps) share one.
    /// </summary>
    public static List<List<(int X, int Z, float Step)>> Spots(List<(int X, int Z, float Step)> steps)
    {
        var buckets = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < steps.Count; i++)
        {
            var key = (steps[i].X / Bucket, steps[i].Z / Bucket);
            if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = [];
            list.Add(i);
        }
        var spots = new List<List<(int X, int Z, float Step)>>();
        var seen = new HashSet<(int, int)>();
        foreach (var start in buckets.Keys)
        {
            if (!seen.Add(start)) continue;
            var spot = new List<(int X, int Z, float Step)>();
            var queue = new Queue<(int X, int Z)>([start]);
            while (queue.TryDequeue(out var b))
            {
                spot.AddRange(buckets[b].Select(i => steps[i]));
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        var next = (b.X + dx, b.Z + dz);
                        if (buckets.ContainsKey(next) && seen.Add(next)) queue.Enqueue(next);
                    }
            }
            spots.Add(spot);
        }
        return spots;
    }

    /// <summary>
    /// Ramps the ground across each step on the road along <paramref name="line"/> (its pixels within
    /// <paramref name="radius"/> + 1 m of the line, and pixels of other roads beside them): steps over
    /// <see cref="EaseStep"/> within <see cref="ExitReach"/> of a street tile's exit, and steps over
    /// <see cref="PrefabPlacer.RoadStep"/> anywhere. Each cell near a step takes the average ground of a square round
    /// it, wide enough that the step rises no more than <see cref="EaseGrade"/> per metre. Prefab footprints keep their
    /// ground and are left out of the average and the steps, as the game shows the prefab's own ground there. Returns the
    /// changed area.
    /// </summary>
    internal static GridRect Ease(World world, IReadOnlyList<Vector2> line, float radius, PrefabLibrary library, EditSession session)
    {
        if (world.Roads is null || line.Count == 0) return GridRect.Empty;
        int size = world.Size, across = (size + Bucket - 1) / Bucket;
        var blocks = new HashSet<int>();
        int reach = (int)MathF.Ceiling(radius) + 1;
        foreach (var p in line)
        {
            var r = new GridRect((int)p.X - reach, (int)p.Y - reach, (int)p.X + reach + 1, (int)p.Y + reach + 1).Clamp(size, size);
            if (r.IsEmpty) continue;
            for (int bz = r.Z0 / Bucket; bz <= (r.Z1 - 1) / Bucket; bz++)
                for (int bx = r.X0 / Bucket; bx <= (r.X1 - 1) / Bucket; bx++)
                    blocks.Add(bx + bz * across);
        }

        var locked = UnderPrefab(world, library);
        var nearExit = NearStreetExit(world, library);
        var changed = GridRect.Empty;
        for (int pass = 0; pass < EasePasses; pass++)
        {
            var steps = blocks.SelectMany(b => Find(world, new GridRect(b % across * Bucket, b / across * Bucket,
                b % across * Bucket + Bucket, b / across * Bucket + Bucket), locked, EaseStep))
                .Where(s => s.Step > PrefabPlacer.RoadStep || nearExit(s.X + s.Z * size)).ToList();
            if (steps.Count == 0) break;
            foreach (var spot in Spots(steps))
            {
                // A box average spreads a step of h over 2r + 1 m; a step left by an earlier pass gets a wider one.
                float worst = spot.Max(s => s.Step);
                int r = Math.Clamp((int)MathF.Ceiling((worst / EaseGrade - 1) / 2) + 2 * pass, 2, MaxEase);
                changed = changed.Union(Blur(world, spot, r, locked, session));
            }
        }
        return changed;
    }

    /// <summary>
    /// Sets each cell within <paramref name="r"/> of a step to the mean of the square of side 2r + 1 round it. Cells
    /// where <paramref name="locked"/> is true keep their height and count for nothing in the mean.
    /// </summary>
    private static GridRect Blur(World world, List<(int X, int Z, float Step)> spot, int r, Func<int, bool> locked, EditSession session)
    {
        int size = world.Size;
        // The step's other pixel is one east or north of it.
        var band = new GridRect(spot.Min(s => s.X) - r, spot.Min(s => s.Z) - r, spot.Max(s => s.X) + 2 + r, spot.Max(s => s.Z) + 2 + r)
            .Clamp(size, size);
        var source = new GridRect(band.X0 - r, band.Z0 - r, band.X1 + r, band.Z1 + r).Clamp(size, size);
        int bw = band.X1 - band.X0, bh = band.Z1 - band.Z0, sw = source.X1 - source.X0, sh = source.Z1 - source.Z0;

        var near = new bool[bw * bh];
        foreach (var (x, z, _) in spot)
            for (int cz = Math.Max(band.Z0, z - r); cz < Math.Min(band.Z1, z + r + 2); cz++)
                for (int cx = Math.Max(band.X0, x - r); cx < Math.Min(band.X1, x + r + 2); cx++)
                    near[cx - band.X0 + (cz - band.Z0) * bw] = true;

        // Summed heights and cell counts, so each square's mean takes four lookups of each.
        var sum = new long[(sw + 1) * (sh + 1)];
        var count = new int[sum.Length];
        for (int z = 0; z < sh; z++)
        {
            long row = 0;
            int cells = 0;
            var heights = world.Height.Row(source.Z0 + z);
            for (int x = 0; x < sw; x++)
            {
                if (!locked(source.X0 + x + (source.Z0 + z) * size))
                {
                    row += heights[source.X0 + x];
                    cells++;
                }
                int o = x + 1 + (z + 1) * (sw + 1);
                sum[o] = sum[o - (sw + 1)] + row;
                count[o] = count[o - (sw + 1)] + cells;
            }
        }

        session.Touch(world.Height, band);
        var changed = GridRect.Empty;
        for (int z = band.Z0; z < band.Z1; z++)
            for (int x = band.X0; x < band.X1; x++)
            {
                if (!near[x - band.X0 + (z - band.Z0) * bw] || locked(x + z * size)) continue;
                int x0 = Math.Max(source.X0, x - r) - source.X0, x1 = Math.Min(source.X1, x + r + 1) - source.X0;
                int z0 = Math.Max(source.Z0, z - r) - source.Z0, z1 = Math.Min(source.Z1, z + r + 1) - source.Z0;
                int a = x0 + z0 * (sw + 1), b = x1 + z0 * (sw + 1), c = x0 + z1 * (sw + 1), d = x1 + z1 * (sw + 1);
                long total = sum[d] - sum[c] - sum[b] + sum[a];
                int cells = count[d] - count[c] - count[b] + count[a];
                var mean = (ushort)Math.Round((double)total / cells);
                if (world.Height[x, z] == mean) continue;
                world.Height[x, z] = mean;
                changed = changed.Union(new GridRect(x, z, x + 1, z + 1));
            }
        return changed;
    }
}
