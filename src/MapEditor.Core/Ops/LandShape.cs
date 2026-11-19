using MapEditor.Core.Edit;
using System.Text.Json.Serialization;
using MapEditor.Core.Model;

namespace MapEditor.Core.Ops;

/// <summary>The outline of a world's land.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LandShapeKind>))]
public enum LandShapeKind
{
    /// <summary>Land to every edge; each edge's own border style decides land or ocean (the game's way).</summary>
    Edges,
    /// <summary>One island with ocean on every edge.</summary>
    Island,
    /// <summary>Several islands with ocean on every edge.</summary>
    Archipelago,
    /// <summary>Land joined to one edge, with ocean on the other three.</summary>
    Peninsula,
    /// <summary>Land and water drawn by the user.</summary>
    Drawn,
}

/// <summary>A side of the map.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MapEdge>))]
public enum MapEdge
{
    North,
    East,
    South,
    West,
}

/// <summary>A land shape's settings. The same settings on the same world size give the same coastline.</summary>
public sealed class LandShape
{
    public const int MinLandPercent = 10, MaxLandPercent = 80, MinIslands = 2, MaxIslands = 12;

    public LandShapeKind Kind { get; set; } = LandShapeKind.Edges;

    /// <summary>The coastline's own seed, apart from the terrain's, so hills can be re-rolled on the same coast.</summary>
    public int Seed { get; set; } = 1;

    /// <summary>Share of the map that is land.</summary>
    public int LandPercent { get; set; } = 45;

    /// <summary>How ragged the coastline is, 0 (smooth) to 100.</summary>
    public int Roughness { get; set; } = 50;

    /// <summary>How many islands an archipelago starts with (some may join or be too small to keep).</summary>
    public int Islands { get; set; } = 6;

    /// <summary>The edge a peninsula is joined to.</summary>
    public MapEdge PeninsulaEdge { get; set; } = MapEdge.South;

    /// <summary>The saved drawing used by <see cref="LandShapeKind.Drawn"/>.</summary>
    public LandDrawing? Drawing { get; set; }

    /// <summary>True for a shape that has an outline of its own (not <see cref="LandShapeKind.Edges"/>).</summary>
    [JsonIgnore] public bool IsShaped => Kind != LandShapeKind.Edges;

    public LandShape Clone()
    {
        var clone = (LandShape)MemberwiseClone();
        clone.Drawing = Drawing?.Clone();
        return clone;
    }

    /// <summary>Whether <paramref name="other"/> gives the same land (settings that don't apply to the kind are ignored).</summary>
    public bool SameLand(LandShape? other)
    {
        if (other is null || other.Kind != Kind) return false;
        if (Kind == LandShapeKind.Edges) return true;
        if (Kind == LandShapeKind.Drawn)
            return Drawing?.SameLand(other.Drawing) ?? (other.Drawing is null || other.Drawing.Boxes.Count == 0);
        return other.Seed == Seed && other.LandPercent == LandPercent && other.Roughness == Roughness
               && (Kind != LandShapeKind.Archipelago || other.Islands == Islands)
               && (Kind != LandShapeKind.Peninsula || other.PeninsulaEdge == PeninsulaEdge);
    }

    /// <summary>The ocean or land each edge gets: a shape's edges are ocean, but for a peninsula's own edge.</summary>
    public bool EdgeIsSea(MapEdge edge) => Kind switch
    {
        LandShapeKind.Island or LandShapeKind.Archipelago => true,
        LandShapeKind.Peninsula => edge != PeninsulaEdge,
        _ => false,
    };
}

/// <summary>
/// A land shape planned on 32 m cells: each holds its signed distance to the coast in metres, positive on land.
/// Samples between cell centres are blended, so the coast is smooth at any size.
/// </summary>
public sealed class LandField
{
    public const int Cell = 32;

    private readonly float[] _distance;

    internal LandField(int worldSize, float[] distance)
    {
        WorldSize = worldSize;
        Width = worldSize / Cell;
        _distance = distance;
    }

    public int WorldSize { get; }

    /// <summary>Cells along each edge.</summary>
    public int Width { get; }

    /// <summary>A cell's distance to the coast in metres; positive on land.</summary>
    public float this[int cx, int cz] => _distance[cx + cz * Width];

    public bool IsLandCell(int cx, int cz) => _distance[cx + cz * Width] > 0;

    /// <summary>The distance to the coast at a point in world metres (z from the south), positive on land.</summary>
    public float Sample(float x, float z)
    {
        float fx = Math.Clamp(x / Cell - 0.5f, 0, Width - 1), fz = Math.Clamp(z / Cell - 0.5f, 0, Width - 1);
        int x0 = (int)fx, z0 = (int)fz;
        int x1 = Math.Min(x0 + 1, Width - 1), z1 = Math.Min(z0 + 1, Width - 1);
        float tx = fx - x0, tz = fz - z0;
        float a = _distance[x0 + z0 * Width], b = _distance[x1 + z0 * Width];
        float c = _distance[x0 + z1 * Width], d = _distance[x1 + z1 * Width];
        return (a + (b - a) * tx) + ((c + (d - c) * tx) - (a + (b - a) * tx)) * tz;
    }

    public bool IsLand(float x, float z) => Sample(x, z) > 0;

    /// <summary>Share of the cells that are land.</summary>
    public float LandShare() => _distance.Count(d => d > 0) / (float)_distance.Length;

    /// <summary>
    /// The islands: each cell's island number as [x, z] (-1 for sea), and each island's middle in world metres (z from
    /// the south) and size in cells. Cells that touch side by side are one island.
    /// </summary>
    public (int[,] Labels, List<(float X, float Z, int Cells)> Islands) Islands()
    {
        var labels = new int[Width, Width];
        for (int z = 0; z < Width; z++)
            for (int x = 0; x < Width; x++)
                labels[x, z] = -1;
        var islands = new List<(float X, float Z, int Cells)>();
        var queue = new Queue<(int X, int Z)>();
        for (int z0 = 0; z0 < Width; z0++)
            for (int x0 = 0; x0 < Width; x0++)
            {
                if (labels[x0, z0] >= 0 || !IsLandCell(x0, z0)) continue;
                int id = islands.Count, cells = 0;
                double sx = 0, sz = 0;
                labels[x0, z0] = id;
                queue.Enqueue((x0, z0));
                while (queue.TryDequeue(out var c))
                {
                    cells++;
                    sx += c.X;
                    sz += c.Z;
                    foreach (var (dx, dz) in Sides)
                    {
                        int x = c.X + dx, z = c.Z + dz;
                        if (x < 0 || z < 0 || x >= Width || z >= Width || labels[x, z] >= 0 || !IsLandCell(x, z)) continue;
                        labels[x, z] = id;
                        queue.Enqueue((x, z));
                    }
                }
                islands.Add(((float)(sx / cells + 0.5) * Cell, (float)(sz / cells + 0.5) * Cell, cells));
            }
        return (labels, islands);
    }

    private static readonly (int, int)[] Sides = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    /// <summary>Share of land in each tile of <paramref name="tileSize"/> metres, as [x, z].</summary>
    public float[,] TileLand(int tileSize)
    {
        int tiles = WorldSize / tileSize, per = Math.Max(1, tileSize / Cell);
        var result = new float[tiles, tiles];
        for (int tz = 0; tz < tiles; tz++)
            for (int tx = 0; tx < tiles; tx++)
            {
                int land = 0, all = 0;
                for (int cz = tz * per; cz < Math.Min(Width, (tz + 1) * per); cz++)
                    for (int cx = tx * per; cx < Math.Min(Width, (tx + 1) * per); cx++)
                    {
                        all++;
                        if (_distance[cx + cz * Width] > 0) land++;
                    }
                result[tx, tz] = all == 0 ? 0 : land / (float)all;
            }
        return result;
    }

    /// <summary>Tiles of <paramref name="tileSize"/> metres that are at least half land, as [x, z].</summary>
    public bool[,] LandTiles(int tileSize)
    {
        var share = TileLand(tileSize);
        var mask = new bool[share.GetLength(0), share.GetLength(1)];
        for (int x = 0; x < share.GetLength(0); x++)
            for (int z = 0; z < share.GetLength(1); z++)
                mask[x, z] = share[x, z] >= 0.5f;
        return mask;
    }
}

/// <summary>
/// Plans a land shape (an island, an archipelago or a peninsula) and presses it into the ground: the sea floor sits
/// below the water, the land slopes up from a beach, and the sea is filled to the game's water level.
/// </summary>
public static class LandShapes
{
    /// <summary>The sea floor, in metres (the water sits at 30).</summary>
    public const float SeaFloor = 18;

    /// <summary>Metres out from the coast the sea floor is reached.</summary>
    public const float ShelfWidth = 192;

    /// <summary>Metres in from the coast the ground has fully risen to the terrain.</summary>
    public const float BeachWidth = 224;

    /// <summary>Land and sea patches smaller than this many cells (about two 256 m tiles) are merged into their surroundings.</summary>
    private const int MinPatchCells = 128;

    /// <summary>Plans the shape's land on a world of <paramref name="worldSize"/> metres; null for <see cref="LandShapeKind.Edges"/>.</summary>
    public static LandField? Build(int worldSize, LandShape shape)
    {
        if (!shape.IsShaped) return null;
        int w = worldSize / LandField.Cell;
        if (shape.Kind == LandShapeKind.Drawn)
        {
            var drawn = new bool[w * w];
            var cuts = new sbyte[drawn.Length];
            Array.Fill(cuts, (sbyte)-1);
            shape.Drawing?.Rasterize(worldSize, drawn, cuts);
            RemoveSmallPatches(drawn, cuts, w);
            return new LandField(worldSize, SignedDistance(drawn, w));
        }
        var rand = new Random(shape.Seed);
        var noise = new Noise(shape.Seed ^ 0x5EA5);
        float rough = Math.Clamp(shape.Roughness, 0, 100) / 100f;
        // Coastline wiggles of about 2.5 km and smaller, whatever the map's size.
        float freq = worldSize / 2500f;

        // How far in from each edge the ocean is kept: at least a 256 m tile, more on big maps.
        float margin = Math.Max(256f, worldSize * 0.05f) / worldSize;
        var baseShape = BaseShape(shape, rand);
        var value = new float[w * w];
        var forced = new sbyte[w * w]; // -1 sea, +1 land, 0 free
        Parallel.For(0, w, cz =>
        {
            for (int cx = 0; cx < w; cx++)
            {
                float u = (cx + 0.5f) / w, v = (cz + 0.5f) / w;
                // The shape is bent by broad noise (so straight lines wander), then its coast roughened by finer noise.
                float wu = u + (0.02f + 0.06f * rough) * noise.Fbm(u * 3 + 17.3f, v * 3 + 5.1f, 3);
                float wv = v + (0.02f + 0.06f * rough) * noise.Fbm(u * 3 + 41.7f, v * 3 + 29.9f, 3);
                float b = baseShape(wu, wv);
                float n = noise.Fbm(u * freq, v * freq, 5);
                float s = b + rough * 0.8f * n;

                // Keep the ocean along the sea edges (the margin), fading in over 1.5 margins so the coast doesn't run along them.
                float edgeDist = float.MaxValue;
                foreach (var edge in Enum.GetValues<MapEdge>())
                    if (shape.EdgeIsSea(edge)) edgeDist = Math.Min(edgeDist, DistanceToEdge(edge, u, v));
                int i = cx + cz * w;
                if (edgeDist < margin) forced[i] = -1;
                else if (edgeDist < margin * 2.5f) s -= 0.6f * Sq(1 - (edgeDist - margin) / (margin * 1.5f));
                value[i] = s;
            }
        });

        if (shape.Kind == LandShapeKind.Peninsula)
        {
            // The peninsula's neck: a stretch of its own edge, around its middle line, is always land.
            int neck = Math.Max(2, w / 16);
            for (int along = w / 2 - neck; along <= w / 2 + neck; along++)
                for (int depth = 0; depth < 2; depth++)
                {
                    var (cx, cz) = EdgeCell(shape.PeninsulaEdge, along, depth, w);
                    forced[cx + cz * w] = 1;
                }
        }

        // Land where the value is in the top LandPercent of the free cells.
        int target = (int)MathF.Round(Math.Clamp(shape.LandPercent, LandShape.MinLandPercent, LandShape.MaxLandPercent) / 100f * w * w);
        var land = new bool[w * w];
        var free = new List<float>(w * w);
        int forcedLand = 0;
        for (int i = 0; i < value.Length; i++)
        {
            if (forced[i] == 0) free.Add(value[i]);
            else if (forced[i] > 0) forcedLand++;
        }
        free.Sort();
        int want = Math.Clamp(target - forcedLand, 0, free.Count);
        float threshold = want == 0 ? float.MaxValue : free[free.Count - want];
        for (int i = 0; i < value.Length; i++)
            land[i] = forced[i] > 0 || forced[i] == 0 && value[i] >= threshold;

        RemoveSmallPatches(land, forced, w);
        return new LandField(worldSize, SignedDistance(land, w));
    }

    /// <summary>The shape before noise, on map coordinates u, v in 0..1 (v from the south): higher is more land-like.</summary>
    private static Func<float, float, float> BaseShape(LandShape shape, Random rand)
    {
        switch (shape.Kind)
        {
            case LandShapeKind.Island:
            {
                // A slightly off-centre, slightly stretched rounded square (a superellipse), so it reaches toward the corners.
                float cu = 0.5f + (float)(rand.NextDouble() - 0.5) * 0.08f, cv = 0.5f + (float)(rand.NextDouble() - 0.5) * 0.08f;
                double turn = rand.NextDouble() * Math.PI;
                float cos = (float)Math.Cos(turn), sin = (float)Math.Sin(turn);
                float stretch = 1 + (float)rand.NextDouble() * 0.35f;
                return (u, v) =>
                {
                    float du = u - cu, dv = v - cv;
                    float a = (du * cos + dv * sin) / stretch, b = -du * sin + dv * cos;
                    float r = MathF.Pow(MathF.Pow(MathF.Abs(a), 4) + MathF.Pow(MathF.Abs(b), 4), 0.25f);
                    return 1 - r * 2;
                };
            }
            case LandShapeKind.Archipelago:
            {
                int count = Math.Clamp(shape.Islands, LandShape.MinIslands, LandShape.MaxIslands);
                var centres = new List<(float U, float V, float R)>();
                float spacing = 0.7f / MathF.Sqrt(count);
                for (int tries = 0; centres.Count < count && tries < 2000; tries++)
                {
                    float u = 0.08f + (float)rand.NextDouble() * 0.84f, v = 0.08f + (float)rand.NextDouble() * 0.84f;
                    // Keep the islands apart, more strictly at first.
                    float apart = spacing * (tries < 1000 ? 1 : 0.6f);
                    if (centres.Any(c => Sq(c.U - u) + Sq(c.V - v) < apart * apart)) continue;
                    centres.Add((u, v, 0.6f + (float)rand.NextDouble() * 0.8f));
                }
                float scale = 2.2f / spacing;
                return (u, v) =>
                {
                    float best = float.MinValue, second = float.MinValue;
                    foreach (var (cu, cv, r) in centres)
                    {
                        float score = r - MathF.Sqrt(Sq(u - cu) + Sq(v - cv)) * scale;
                        if (score > best) (best, second) = (score, best);
                        else if (score > second) second = score;
                    }
                    // The same slope as the single island's, so the coast's noise is as rough.
                    best = best * 2 / scale;
                    if (second == float.MinValue) return best;
                    // A channel where two islands meet, so they stay apart even with a lot of land.
                    float gap = (best - second * 2 / scale) / 0.12f;
                    return gap < 1 ? best - 0.8f * Sq(1 - gap) : best;
                };
            }
            default:
            {
                // A lobe from the middle of its edge, its middle line wandering a little.
                float bend = (float)(rand.NextDouble() - 0.5) * 0.3f;
                float wobble = (float)rand.NextDouble() * 6;
                var edge = shape.PeninsulaEdge;
                return (u, v) =>
                {
                    float depth = DistanceToEdge(edge, u, v);
                    float across = edge is MapEdge.North or MapEdge.South ? u : v;
                    float middle = 0.5f + bend * depth + 0.06f * MathF.Sin(depth * 5 + wobble);
                    // Narrower at the neck than out in the sea, then rounding off to its tip.
                    float a = depth / 0.85f;
                    float width = 0.3f * (0.3f + 0.9f * MathF.Sin(MathF.PI * Math.Min(a, 1) * 0.8f + 0.15f));
                    float b = (across - middle) / width;
                    return 1 - MathF.Sqrt(a * a + b * b);
                };
            }
        }
    }

    /// <summary>How far (u, v) is from an edge, as a share of the map.</summary>
    private static float DistanceToEdge(MapEdge edge, float u, float v) => edge switch
    {
        MapEdge.North => 1 - v,
        MapEdge.East => 1 - u,
        MapEdge.South => v,
        _ => u,
    };

    /// <summary>The cell <paramref name="along"/> an edge and <paramref name="depth"/> cells in from it.</summary>
    private static (int X, int Z) EdgeCell(MapEdge edge, int along, int depth, int w) => edge switch
    {
        MapEdge.North => (along, w - 1 - depth),
        MapEdge.East => (w - 1 - depth, along),
        MapEdge.South => (along, depth),
        _ => (depth, along),
    };

    private static float Sq(float x) => x * x;

    /// <summary>
    /// Merges specks of land into the sea and small ponds into the land, so the coast is clean. Forced cells keep
    /// their kind, and sea that reaches the map's edge is never filled.
    /// </summary>
    private static void RemoveSmallPatches(bool[] land, sbyte[] forced, int w)
    {
        var seen = new bool[land.Length];
        var patch = new List<int>();
        var stack = new Stack<int>();
        for (int start = 0; start < land.Length; start++)
        {
            if (seen[start]) continue;
            bool kind = land[start];
            patch.Clear();
            bool keep = false;
            stack.Push(start);
            seen[start] = true;
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                patch.Add(i);
                int x = i % w, z = i / w;
                if (forced[i] != 0) keep = true;
                if (!kind && (x == 0 || z == 0 || x == w - 1 || z == w - 1)) keep = true;
                if (x > 0) Visit(i - 1);
                if (x < w - 1) Visit(i + 1);
                if (z > 0) Visit(i - w);
                if (z < w - 1) Visit(i + w);
            }
            if (!keep && patch.Count < MinPatchCells)
                foreach (int i in patch) land[i] = !kind;

            void Visit(int j)
            {
                if (seen[j] || land[j] != kind) return;
                seen[j] = true;
                stack.Push(j);
            }
        }
    }

    /// <summary>Each cell's distance to the nearest cell of the other kind, in metres: positive on land, negative at sea.</summary>
    private static float[] SignedDistance(bool[] land, int w)
    {
        var toSea = Distance(land, w, true);
        var toLand = Distance(land, w, false);
        var result = new float[land.Length];
        for (int i = 0; i < land.Length; i++)
            result[i] = land[i] ? (toSea[i] - 0.5f) * LandField.Cell : -(toLand[i] - 0.5f) * LandField.Cell;
        return result;
    }

    /// <summary>
    /// Distance in cells from each cell of <paramref name="kind"/> to the nearest cell that isn't (a two-pass chamfer
    /// transform); 0 elsewhere. With no such cell, distances run to the map's size.
    /// </summary>
    private static float[] Distance(bool[] land, int w, bool kind)
    {
        const float Diagonal = 1.4142135f;
        var d = new float[land.Length];
        float far = w * 2f;
        for (int i = 0; i < d.Length; i++) d[i] = land[i] == kind ? far : 0;
        for (int z = 0; z < w; z++)
            for (int x = 0; x < w; x++)
            {
                int i = x + z * w;
                if (d[i] == 0) continue;
                float best = d[i];
                if (x > 0) best = Math.Min(best, d[i - 1] + 1);
                if (z > 0)
                {
                    best = Math.Min(best, d[i - w] + 1);
                    if (x > 0) best = Math.Min(best, d[i - w - 1] + Diagonal);
                    if (x < w - 1) best = Math.Min(best, d[i - w + 1] + Diagonal);
                }
                d[i] = best;
            }
        for (int z = w - 1; z >= 0; z--)
            for (int x = w - 1; x >= 0; x--)
            {
                int i = x + z * w;
                if (d[i] == 0) continue;
                float best = d[i];
                if (x < w - 1) best = Math.Min(best, d[i + 1] + 1);
                if (z < w - 1)
                {
                    best = Math.Min(best, d[i + w] + 1);
                    if (x < w - 1) best = Math.Min(best, d[i + w + 1] + Diagonal);
                    if (x > 0) best = Math.Min(best, d[i + w - 1] + Diagonal);
                }
                d[i] = best;
            }
        return d;
    }

    /// <summary>
    /// Presses the land shape into <paramref name="height"/> over <paramref name="region"/> (world metres), whose cell
    /// (0, 0) sits at world (<paramref name="offsetX"/>, <paramref name="offsetZ"/>): the sea's ground drops to the sea
    /// floor and the land rises from a beach at the water's edge to the terrain inland.
    /// </summary>
    public static void Press(Grid<ushort> height, LandField field, GridRect region, int offsetX = 0, int offsetZ = 0)
    {
        const float water = StampOp.DefaultWaterLevel;
        Parallel.For(region.Z0, region.Z1, z =>
        {
            var row = height.Row(z - offsetZ);
            for (int x = region.X0; x < region.X1; x++)
            {
                float s = field.Sample(x, z);
                if (s >= BeachWidth) continue;
                ref ushort raw = ref row[x - offsetX];
                float h = raw / 257f;
                float shaped;
                if (s <= 0)
                {
                    // The shelf: from the water's edge down to the sea floor.
                    float t = Math.Min(1, -s / ShelfWidth);
                    shaped = water - (water - SeaFloor) * t * t * (3 - 2 * t);
                    h = Math.Min(h, shaped);
                }
                else
                {
                    // The beach: from just above the water up into the terrain, which never pokes out higher than it was.
                    float t = s / BeachWidth;
                    t = t * t * (3 - 2 * t);
                    float beach = water + 0.6f + s * 0.02f;
                    h = Math.Min(h, beach + (h - beach) * t);
                    if (h < water + 0.6f && s > LandField.Cell) h = water + 0.6f;
                }
                raw = TerrainBrush.ToRaw(Math.Max(h, 2));
            }
        });
    }

    /// <summary>
    /// Fills the sea with water over <paramref name="region"/>: every cell off the coast, and low ground near it, whose
    /// ground is under the water's surface. Lakes and rivers already there are kept.
    /// </summary>
    public static void FillSea(Grid<ushort> height, Grid<byte> water, LandField field, GridRect region)
    {
        const byte level = StampOp.DefaultWaterLevel;
        Parallel.For(region.Z0, region.Z1, z =>
        {
            var h = height.Row(z);
            var w = water.Row(z);
            for (int x = region.X0; x < region.X1; x++)
            {
                if (w[x] != 0 || field.Sample(x, z) >= LandField.Cell) continue;
                if (h[x] / 257f - 0.5f <= level) w[x] = level;
            }
        });
    }
}
