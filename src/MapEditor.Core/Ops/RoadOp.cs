using System.Numerics;
using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;

namespace MapEditor.Core.Ops;

/// <summary>
/// A road to draw: control points in grid metres (x east, z north), the surface, and the half-width of
/// the drivable surface. RWG uses 9 m for four-lane highways and 4.5 m for two-lane country roads.
/// </summary>
public sealed record RoadPath(IReadOnlyList<Vector2> Points, RoadType Type, float Radius, bool Grade = true)
{
    /// <summary>Minimum distance over which graded terrain blends back into the original ground.</summary>
    public float BlendDistance => BlendFor(Type);

    internal static float BlendFor(RoadType type) => type == RoadType.Asphalt ? 10f : 6f;

    /// <summary>Farthest from the centreline that painting or bank grading can reach.</summary>
    internal float MaxReach => Radius + (Grade ? MaxBlendDistance : 0);

    // Gravel needs up to 31 m to ease a 14 m rise at 1:2.
    internal float MaxBlendDistance => Type == RoadType.Asphalt ? 40f : 32f;

    /// <summary>Asphalt roads this wide get a 1 m gravel shoulder at the edge, as RWG highways do.</summary>
    public bool HasShoulder => Type == RoadType.Asphalt && Radius >= 4f;
}

/// <summary>
/// Turns a road polyline into splat3 road pixels and, optionally, a graded roadbed: a smoothed height
/// profile along the road, held across its width and blended back into the terrain beyond its edge.
/// </summary>
public static class RoadOp
{
    /// <summary>Spacing of the points the control polyline is resampled to, in metres.</summary>
    public const float SampleSpacing = 4f;

    /// <summary>How far (in metres) the height profile is smoothed along the road for each surface.</summary>
    private static float ProfileSmoothing(RoadType type) => type == RoadType.Asphalt ? 60f : 16f;

    /// <summary>How far (in metres) corners are rounded off; highways sweep in wider curves.</summary>
    public static float CornerRounding(RoadType type) => type == RoadType.Asphalt ? 30f : 15f;

    /// <summary>Resamples the control points and rounds off corners; the end points stay put.</summary>
    public static List<Vector2> Centreline(IReadOnlyList<Vector2> points, float rounding = 15f)
    {
        var line = new List<Vector2>();
        for (int i = 0; i < points.Count; i++)
        {
            if (i == 0) { line.Add(points[0]); continue; }
            var a = points[i - 1];
            var b = points[i];
            float length = Vector2.Distance(a, b);
            int steps = Math.Max(1, (int)MathF.Ceiling(length / SampleSpacing));
            for (int s = 1; s <= steps; s++) line.Add(Vector2.Lerp(a, b, s / (float)steps));
        }
        if (line.Count < 3) return line;

        // Repeated [1/4 1/2 1/4] averaging spreads like a Gaussian of sigma ~ spacing * sqrt(n/2), turning
        // sharp joins into curves about that wide. Short roads are capped so they do not shrink away.
        float sigma = rounding / SampleSpacing;
        int passes = Math.Min((int)(2 * sigma * sigma), line.Count * 2);
        var copy = new Vector2[line.Count];
        for (int pass = 0; pass < passes; pass++)
        {
            line.CopyTo(copy);
            for (int i = 1; i < line.Count - 1; i++)
                line[i] = copy[i] * 0.5f + (copy[i - 1] + copy[i + 1]) * 0.25f;
        }
        return line;
    }

    /// <summary>
    /// Road surface height at each centreline point: the ground under it, smoothed along the road, and kept
    /// at least 1 m above any water it crosses.
    /// </summary>
    public static float[] HeightProfile(World world, IReadOnlyList<Vector2> line, RoadType type)
    {
        var (heights, floor) = GroundWithCauseways(world, line);

        // A 3-tap average applied n times spreads like a Gaussian of sigma ~ spacing * sqrt(2n/3).
        // Holding the floor on every pass makes the road ramp smoothly up to a causeway, not step onto it.
        float sigma = ProfileSmoothing(type) / SampleSpacing;
        int passes = Math.Max(1, (int)(1.5f * sigma * sigma));
        var copy = new float[heights.Length];
        for (int pass = 0; pass < passes; pass++)
        {
            heights.CopyTo(copy, 0);
            for (int i = 1; i < heights.Length - 1; i++)
                heights[i] = Math.Max((copy[i - 1] + copy[i] + copy[i + 1]) / 3f, floor[i]);
        }
        LevelFlats(heights, line, floor);
        return heights;
    }

    /// <summary>
    /// The ground under each centreline point, with every stretch over water raised to a causeway, and the floor
    /// each point must stay on (1 m over water there, else 0).
    /// </summary>
    public static (float[] Heights, float[] Floor) GroundWithCauseways(World world, IReadOnlyList<Vector2> line)
    {
        var heights = new float[line.Count];
        var floor = new float[line.Count];
        var wet = new bool[line.Count];
        for (int i = 0; i < line.Count; i++)
        {
            int x = Math.Clamp((int)line[i].X, 0, world.Size - 1), z = Math.Clamp((int)line[i].Y, 0, world.Size - 1);
            byte water = world.Water?[x, z] ?? 0;
            heights[i] = World.HeightToMetres(world.Height[x, z]);
            floor[i] = water > 0 ? water + 1 : 0;
            wet[i] = water > 0 && heights[i] < water + 0.5f;
        }

        // Over water the road rides a causeway carried straight from one bank to the other, like a bridge
        // deck, so neither the lake bed nor the water level pulls the approaches down into the land.
        for (int i = 0; i < line.Count; i++)
        {
            if (!wet[i]) continue;
            int end = i;
            while (end + 1 < line.Count && wet[end + 1]) end++;
            float? before = i > 0 ? heights[i - 1] : null;
            float? after = end + 1 < line.Count ? heights[end + 1] : null;
            for (int j = i; j <= end; j++)
            {
                float t = (j - i + 1f) / (end - i + 2f);
                float deck = (before, after) switch
                {
                    ({ } a, { } b) => a + (b - a) * t,
                    ({ } a, null) => a,
                    (null, { } b) => b,
                    _ => floor[j],
                };
                heights[j] = Math.Max(deck, floor[j]);
            }
            i = end;
        }
        for (int i = 0; i < line.Count; i++) heights[i] = Math.Max(heights[i], floor[i]);
        return (heights, floor);
    }

    /// <summary>Steepest grade (rise over run) a stretch of road can have and still count as flat.</summary>
    public const float FlatGrade = 0.01f;

    /// <summary>Most a flat stretch's height may vary from end to end, in metres.</summary>
    public const float FlatRange = 1f;

    /// <summary>Shortest stretch of road, in metres, that is levelled.</summary>
    public const float FlatMinLength = 20f;

    /// <summary>Distance, in metres, over which a levelled stretch blends back into the road beyond it.</summary>
    public const float FlatTransition = 20f;

    /// <summary>
    /// Levels each flat stretch of the profile to a whole metre (its average height, rounded). Prefabs sit at
    /// whole metres, so a road at 40.11 m beside a trader at 40.00 m shows a lip; levelled, both are at 40.00 m
    /// and the road is flat. Sloping road and causeways are left alone; the change fades out beyond a stretch.
    /// </summary>
    public static void LevelFlats(float[] heights, IReadOnlyList<Vector2> line, float[] floor)
    {
        int n = heights.Length;
        if (n < 3) return;
        var along = new float[n];
        for (int i = 1; i < n; i++) along[i] = along[i - 1] + Vector2.Distance(line[i - 1], line[i]);

        // Flat stretches: runs of samples joined by gentle steps, off the water, varying by no more than FlatRange.
        var runs = new List<(int Start, int End, float Level)>();
        for (int i = 0; i < n;)
        {
            int end = i;
            float lo = heights[i], hi = heights[i];
            while (end + 1 < n && floor[end + 1] == 0 && floor[i] == 0)
            {
                float step = MathF.Abs(heights[end + 1] - heights[end]);
                float run = Math.Max(along[end + 1] - along[end], 1e-3f);
                float next = heights[end + 1];
                if (step / run > FlatGrade || Math.Max(hi, next) - Math.Min(lo, next) > FlatRange) break;
                lo = Math.Min(lo, next);
                hi = Math.Max(hi, next);
                end++;
            }
            if (end > i && along[end] - along[i] >= FlatMinLength)
            {
                float sum = 0;
                for (int j = i; j <= end; j++) sum += heights[j];
                float level = MathF.Round(sum / (end - i + 1));
                runs.Add((i, end, level));
            }
            i = end + 1;
        }
        SpaceRuns(runs, heights, along);
        if (runs.Count == 0) return;

        // Each run is set to its level. Between and beyond runs the change from the old height (at the run's
        // end) eases away over FlatTransition, or eases from one run's change to the next where they are close.
        var offset = new float[n];
        var original = (float[])heights.Clone();
        for (int r = 0; r < runs.Count; r++)
        {
            var (start, end, level) = runs[r];
            for (int j = start; j <= end; j++) heights[j] = level;
            float endShift = level - original[end];
            float startShift = level - original[start];

            int gapEnd = r + 1 < runs.Count ? runs[r + 1].Start : n;
            float nextShift = r + 1 < runs.Count ? runs[r + 1].Level - original[runs[r + 1].Start] : 0;
            bool close = r + 1 < runs.Count && along[gapEnd] - along[end] <= 2 * FlatTransition;
            for (int j = end + 1; j < gapEnd; j++)
            {
                if (close)
                {
                    float t = (along[j] - along[end]) / (along[gapEnd] - along[end]);
                    offset[j] = endShift + (nextShift - endShift) * SmoothStep(t);
                }
                else
                {
                    offset[j] += endShift * (1 - SmoothStep((along[j] - along[end]) / FlatTransition));
                    if (r + 1 < runs.Count)
                        offset[j] += nextShift * (1 - SmoothStep((along[gapEnd] - along[j]) / FlatTransition));
                }
            }
            if (r == 0)
                for (int j = 0; j < start; j++)
                    offset[j] = startShift * (1 - SmoothStep((along[start] - along[j]) / FlatTransition));
        }
        for (int j = 0; j < n; j++)
            if (offset[j] != 0) heights[j] = Math.Max(heights[j] + offset[j], floor[j]);
    }

    /// <summary>Average grade of the ramp between two levelled stretches at different whole metres.</summary>
    public const float FlatRampGrade = 0.02f;

    /// <summary>
    /// Makes neighbouring runs fit together. Close runs at the same level that nothing much rises or dips
    /// between are joined into one. Runs at different levels are cut back at the ends that face each other
    /// until the gap holds a ramp no steeper than <see cref="FlatRampGrade"/>; a run cut shorter than
    /// <see cref="FlatMinLength"/> is dropped.
    /// </summary>
    private static void SpaceRuns(List<(int Start, int End, float Level)> runs, float[] heights, float[] along)
    {
        for (bool changed = true; changed;)
        {
            changed = false;
            for (int r = 0; r + 1 < runs.Count && !changed; r++)
            {
                var a = runs[r];
                var b = runs[r + 1];
                float gap = along[b.Start] - along[a.End];
                if (a.Level == b.Level)
                {
                    if (gap > 2 * FlatTransition) continue;
                    bool level = true;
                    for (int j = a.End + 1; j < b.Start; j++)
                        level &= MathF.Abs(heights[j] - a.Level) <= FlatRange / 2;
                    if (!level) continue;
                    runs[r] = (a.Start, b.End, a.Level);
                    runs.RemoveAt(r + 1);
                    changed = true;
                    continue;
                }
                float need = MathF.Abs(a.Level - b.Level) / FlatRampGrade;
                if (gap >= need) continue;
                float cut = (need - gap) / 2;
                int aEnd = a.End, bStart = b.Start;
                while (aEnd > a.Start && along[a.End] - along[aEnd] < cut) aEnd--;
                while (bStart < b.End && along[bStart] - along[b.Start] < cut) bStart++;
                runs[r + 1] = (bStart, b.End, b.Level);
                runs[r] = (a.Start, aEnd, a.Level);
                for (int k = r + 1; k >= r; k--)
                    if (along[runs[k].End] - along[runs[k].Start] < FlatMinLength) runs.RemoveAt(k);
                changed = true;
            }
        }
    }

    private static float SmoothStep(float t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>Draws the road into <see cref="World.Roads"/> (and grades the terrain if asked). Returns the changed area.</summary>
    /// <param name="profile">The road's height at each centreline point; the editor's own (<see cref="HeightProfile"/>) when null.</param>
    /// <param name="library">The dimensions of placed prefabs. Unknown prefabs use their anchor cell.</param>
    public static GridRect Draw(World world, RoadPath road, EditSession session, Func<World, List<Vector2>, float[]>? profile = null,
        PrefabLibrary? library = null)
    {
        if (road.Points.Count < 2 || road.Radius <= 0) return GridRect.Empty;
        world.Roads ??= new Grid<byte>(world.Size, world.Size);
        var line = Centreline(road.Points, CornerRounding(road.Type));
        return Draw(world, road, line, profile?.Invoke(world, line) ?? HeightProfile(world, line, road.Type), session, library);
    }

    /// <summary>Draws a road along a centreline already made (<see cref="Centreline"/>) at the given heights.</summary>
    public static GridRect Draw(World world, RoadPath road, List<Vector2> line, float[] profile, EditSession session,
        PrefabLibrary? library = null) =>
        Draw(world, road, line, profile, session, library, null);

    /// <summary>Draws a road with optional paint and grading limits for each cell, indexed as x + z * world size.</summary>
    internal static GridRect Draw(World world, RoadPath road, List<Vector2> line, float[] profile, EditSession session,
        PrefabLibrary? library, Func<int, (bool Paint, bool Grade)>? cellEdits)
    {
        if (line.Count < 2 || road.Radius <= 0) return GridRect.Empty;
        world.Roads ??= new Grid<byte>(world.Size, world.Size);
        const int tileSize = 16;
        int size = world.Size, tilesAcross = (size + tileSize - 1) / tileSize;
        float reach = road.MaxReach;
        var tiles = new Dictionary<int, List<int>>();
        var searchBounds = GridRect.Empty;
        var segments = new (Vector2 Start, Vector2 Offset, float InverseLengthSq)[line.Count - 1];
        for (int i = 0; i < segments.Length; i++)
        {
            var a = line[i];
            var b = line[i + 1];
            var ab = b - a;
            segments[i] = (a, ab, 1 / Math.Max(ab.LengthSquared(), 1e-6f));
            var area = SegmentBounds(a, b, reach);
            searchBounds = searchBounds.Union(area);
            foreach (int tile in TilesIn(area))
            {
                if (!tiles.TryGetValue(tile, out var candidates)) tiles[tile] = candidates = [];
                candidates.Add(i);
            }
        }

        var footprints = new Dictionary<int, List<(GridRect Rect, bool Bridge)>>();
        library ??= PrefabLibrary.Empty;
        foreach (var prefab in world.Prefabs)
        {
            bool bridge = library.Find(prefab.Name) is { IsBridge: true };
            if (!road.Grade && !bridge) continue;
            var rect = PrefabPlacer.Footprint(world, prefab, library);
            foreach (int tile in TilesIn(Intersect(searchBounds, rect)))
            {
                if (!tiles.ContainsKey(tile)) continue;
                if (!footprints.TryGetValue(tile, out var occupied)) footprints[tile] = occupied = [];
                occupied.Add((rect, bridge));
            }
        }

        Span<float> distances = stackalloc float[tileSize * tileSize];
        Span<float> roadHeights = stackalloc float[tileSize * tileSize];
        Span<bool> protectedCells = stackalloc bool[tileSize * tileSize];
        Span<bool> bridgeDeck = stackalloc bool[tileSize * tileSize];
        var roads = world.Roads.Data;
        var height = world.Height.Data;
        float core = road.HasShoulder ? road.Radius - 1 : road.Radius;
        float radiusSq = road.Radius * road.Radius, coreSq = core * core;
        float minimum = road.BlendDistance, maximum = road.MaxBlendDistance;
        var bounds = GridRect.Empty;
        foreach (var (tile, candidates) in tiles)
        {
            int x0 = tile % tilesAcross * tileSize, z0 = tile / tilesAcross * tileSize;
            var rect = new GridRect(x0, z0, x0 + tileSize, z0 + tileSize).Clamp(size, size);
            int width = rect.X1 - x0;
            float tileReach = road.Grade
                ? road.Radius + BankWidth(MaxHeightDifference(world.Height, rect, profile, candidates), minimum, maximum)
                : reach;
            float reachSq = tileReach * tileReach;

            // The nearest segment determines each cell's road height. A tile's largest possible
            // height difference bounds the search while preserving that choice.
            distances.Fill(reachSq);
            foreach (int i in candidates)
            {
                var (a, ab, inverseLengthSq) = segments[i];
                var area = Intersect(rect, SegmentBounds(a, line[i + 1], tileReach));
                float startHeight = profile[i], rise = profile[i + 1] - startHeight;
                for (int z = area.Z0; z < area.Z1; z++)
                    for (int x = area.X0; x < area.X1; x++)
                    {
                        var p = new Vector2(x + 0.5f, z + 0.5f);
                        float t = Math.Clamp(Vector2.Dot(p - a, ab) * inverseLengthSq, 0, 1);
                        float d = Vector2.DistanceSquared(p, a + ab * t);
                        int cell = x - x0 + (z - z0) * width;
                        if (d >= distances[cell]) continue;
                        distances[cell] = d;
                        roadHeights[cell] = startHeight + rise * t;
                    }
            }

            protectedCells.Clear();
            bridgeDeck.Clear();
            if (footprints.TryGetValue(tile, out var occupied))
                foreach (var (footprint, bridge) in occupied)
                {
                    var area = Intersect(rect, footprint);
                    for (int z = area.Z0; z < area.Z1; z++)
                    {
                        int start = area.X0 - x0 + (z - z0) * width, length = area.X1 - area.X0;
                        protectedCells.Slice(start, length).Fill(true);
                        if (bridge) bridgeDeck.Slice(start, length).Fill(true);
                    }
                }

            bool touchedRoads = false, touchedHeight = false;
            for (int z = z0; z < rect.Z1; z++)
                for (int x = x0; x < rect.X1; x++)
                {
                    int cell = x - x0 + (z - z0) * width;
                    float distanceSq = distances[cell];
                    if (distanceSq >= reachSq) continue;
                    int key = x + z * size;
                    var (paint, grade) = cellEdits?.Invoke(key) ?? (true, true);
                    var existing = (RoadType)roads[key];
                    bool onRoad = distanceSq <= radiusSq;
                    // The prefab carries the road on its deck. Paint the approach up to the deck edge.
                    if (paint && onRoad && !bridgeDeck[cell])
                    {
                        var type = distanceSq <= coreSq ? road.Type : RoadType.Gravel;
                        // Asphalt wins where roads meet, so gravel joining a highway does not cut through it.
                        if (existing != type && (existing != RoadType.Asphalt || type == RoadType.Asphalt))
                        {
                            if (!touchedRoads) session.Touch(world.Roads, rect);
                            touchedRoads = true;
                            roads[key] = (byte)type;
                        }
                    }
                    // Keep existing road pixels in the bank and keep asphalt under a gravel road (#34).
                    if (!road.Grade || !grade || protectedCells[cell] || (!onRoad && existing is RoadType.Gravel or RoadType.Asphalt)
                        || (road.Type != RoadType.Asphalt && existing == RoadType.Asphalt)) continue;
                    float roadHeight = roadHeights[cell];
                    float delta = World.HeightToMetres(height[key]) - roadHeight;
                    float bankHeight = onRoad ? 0 : BankHeight(MathF.Sqrt(distanceSq) - road.Radius, delta, minimum, maximum);
                    if (bankHeight == delta) continue;
                    ushort raw = TerrainBrush.ToRaw(roadHeight + bankHeight);
                    if (raw == height[key]) continue;
                    if (!touchedHeight) session.Touch(world.Height, rect);
                    touchedHeight = true;
                    height[key] = raw;
                }
            if (touchedRoads || touchedHeight) bounds = bounds.Union(rect);
        }
        return bounds;

        IEnumerable<int> TilesIn(GridRect area)
        {
            var rect = area.Clamp(size, size);
            if (rect.IsEmpty) yield break;
            for (int tz = rect.Z0 / tileSize; tz <= (rect.Z1 - 1) / tileSize; tz++)
                for (int tx = rect.X0 / tileSize; tx <= (rect.X1 - 1) / tileSize; tx++)
                    yield return tx + tz * tilesAcross;
        }
    }

    // A cell is sampled at its centre (x + 0.5, z + 0.5).
    private static GridRect SegmentBounds(Vector2 a, Vector2 b, float reach) =>
        new((int)MathF.Ceiling(MathF.Min(a.X, b.X) - reach - 0.5f), (int)MathF.Ceiling(MathF.Min(a.Y, b.Y) - reach - 0.5f),
            (int)MathF.Floor(MathF.Max(a.X, b.X) + reach - 0.5f) + 1, (int)MathF.Floor(MathF.Max(a.Y, b.Y) + reach - 0.5f) + 1);

    private static GridRect Intersect(GridRect a, GridRect b) =>
        new(Math.Max(a.X0, b.X0), Math.Max(a.Z0, b.Z0), Math.Min(a.X1, b.X1), Math.Min(a.Z1, b.Z1));

    private static float MaxHeightDifference(Grid<ushort> ground, GridRect rect, float[] profile, List<int> candidates)
    {
        ushort low = ushort.MaxValue, high = 0;
        for (int z = rect.Z0; z < rect.Z1; z++)
            foreach (ushort raw in ground.Row(z)[rect.X0..rect.X1])
            {
                low = Math.Min(low, raw);
                high = Math.Max(high, raw);
            }
        float roadLow = float.PositiveInfinity, roadHigh = float.NegativeInfinity;
        foreach (int i in candidates)
        {
            roadLow = Math.Min(roadLow, Math.Min(profile[i], profile[i + 1]));
            roadHigh = Math.Max(roadHigh, Math.Max(profile[i], profile[i + 1]));
        }
        return Math.Max(roadHigh - World.HeightToMetres(low), World.HeightToMetres(high) - roadLow);
    }

    private static float BankWidth(float rise, float minimum, float maximum) =>
        Math.Clamp(2 * rise + minimum / 2, minimum, maximum);

    /// <summary>
    /// Height above the road at one bank cell. The width uses the difference between that cell's
    /// original ground and the nearest centreline height. The grade rises linearly near the shoulder,
    /// holds at up to 1:2, and falls linearly to zero at the outer edge. The two eased portions take
    /// half the minimum width out of the effective run, so the width grows to twice the rise plus
    /// that half-width until it reaches the cap.
    /// </summary>
    private static float BankHeight(float distance, float delta, float minimum, float maximum)
    {
        float width = BankWidth(MathF.Abs(delta), minimum, maximum);
        if (distance >= width) return delta;

        float start = 0.9f * minimum, end = 0.1f * minimum;
        float slope = delta / (width - minimum / 2);
        if (distance < start) return slope * distance * (distance / (2 * start));
        float left = width - distance;
        if (left < end) return delta - slope * left * (left / (2 * end));
        return slope * (distance - start / 2);
    }

    /// <summary>Removes road pixels under the brush tip; the terrain is left as it is.</summary>
    public static GridRect Erase(World world, float cx, float cz, BrushTip tip, EditSession session)
    {
        if (world.Roads is not { } roads) return GridRect.Empty;
        var rect = tip.Bounds(cx, cz).Clamp(roads.Width, roads.Height);
        bool touched = false;
        for (int z = rect.Z0; z < rect.Z1; z++)
        {
            var row = roads.Row(z);
            for (int x = rect.X0; x < rect.X1; x++)
            {
                if (row[x] == 0 || tip.Distance(x - cx, z - cz) >= 1) continue;
                if (!touched)
                {
                    session.Touch(roads, rect);
                    touched = true;
                }
                row[x] = 0;
            }
        }
        return touched ? rect : GridRect.Empty;
    }

    /// <summary>Length of the polyline in metres.</summary>
    public static float Length(IReadOnlyList<Vector2> points)
    {
        float length = 0;
        for (int i = 1; i < points.Count; i++) length += Vector2.Distance(points[i - 1], points[i]);
        return length;
    }
}
