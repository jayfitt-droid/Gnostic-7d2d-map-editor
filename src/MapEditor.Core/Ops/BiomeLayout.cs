using MapEditor.Core.GameData;
using MapEditor.Core.Model;

namespace MapEditor.Core.Ops;

/// <summary>Where RWG seeds each biome before growing them (WorldBuilder.BiomeLayout).</summary>
public enum BiomeLayoutKind
{
    /// <summary>Forest in the middle, the others in a ring around it.</summary>
    CenterForest,
    /// <summary>Wasteland in the middle, the others in a ring around it.</summary>
    CenterWasteland,
    /// <summary>Every biome on one ring.</summary>
    Circle,
    /// <summary>Each biome twice, on an outer and an inner ring.</summary>
    Circle2,
    /// <summary>Every biome along a line across the middle.</summary>
    Line,
    /// <summary>
    /// One biome per island of an archipelago, harder outward from the middle (#46). Not RWG's. Saved as a number: added
    /// last so older saves keep theirs.
    /// </summary>
    PerIsland,
}

/// <summary>A biome and its share of the map (a weight; the shares are compared with each other, not percentages).</summary>
public sealed record BiomeShare(string Name, uint Color, int Weight);

/// <summary>A generated biome map: the layout and each biome's share.</summary>
public sealed record BiomePlan(BiomeLayoutKind Layout, IReadOnlyList<BiomeShare> Shares);

/// <summary>
/// RWG's biome layout: the map is cut into 256 m tiles, each biome gets a share of them and a seed tile placed by the
/// layout, then biomes grow ring by ring into empty tiles next to their own. The tiles are painted into biomes.png with
/// the game's filler_biome stamp so the edges look natural.
/// </summary>
public static class BiomeLayout
{
    public const int TileSize = 256;

    public const uint ForestColor = 0x004000, BurntColor = 0xBA00FF, DesertColor = 0xFFE477, SnowColor = 0xFFFFFF, WastelandColor = 0xFFA800;

    /// <summary>RWG's default weights (WorldBuilder's *BiomeWeight). Other (mod) biomes start at 0.</summary>
    public static int DefaultWeight(uint color) => color switch
    {
        ForestColor => 13,
        BurntColor => 18,
        DesertColor => 22,
        SnowColor => 23,
        WastelandColor => 24,
        _ => 0,
    };

    /// <summary>One share per land biome (not underwater), at RWG's default weights.</summary>
    public static List<BiomeShare> DefaultShares(BiomeTable biomes) =>
        biomes.Biomes.Where(b => !b.Name.Contains("underwater", StringComparison.OrdinalIgnoreCase))
            .Select(b => new BiomeShare(b.Name, b.Color, DefaultWeight(b.Color))).ToList();

    /// <summary>A seed biome's tile quota and seed tile while the layout grows.</summary>
    private sealed class Seed(int biome, float percent, int tiles)
    {
        public int Biome { get; } = biome;
        public float Percent { get; } = percent;
        public int Tiles { get; set; } = tiles;
        public int X { get; set; }
        public int Z { get; set; }
    }

    /// <summary>
    /// Plans the 256 m tiles: each holds an index into <paramref name="shares"/>. Biomes with weight 0 get none.
    /// </summary>
    /// <param name="land">
    /// The land tiles of an island or peninsula world, as [x, z]: the shares are of the land, the layout is laid over
    /// the land, and biomes grow across the sea without it counting towards their share. Null for a world of land.
    /// </param>
    public static int[,] PlanTiles(int worldSize, IReadOnlyList<BiomeShare> shares, BiomeLayoutKind layout, Random rand, bool[,]? land = null)
    {
        int w = worldSize / TileSize;
        var tiles = new int[w, w];
        for (int x = 0; x < w; x++)
            for (int z = 0; z < w; z++)
                tiles[x, z] = -1;

        int fallback = Fallback(shares);
        if (fallback < 0) throw new ArgumentException("At least one biome needs a share above 0.", nameof(shares));

        int landTiles = 0;
        float landX = 0, landZ = 0;
        if (land is not null && land.GetLength(0) == w && land.GetLength(1) == w)
            for (int x = 0; x < w; x++)
                for (int z = 0; z < w; z++)
                    if (land[x, z])
                    {
                        landTiles++;
                        landX += x + 0.5f;
                        landZ += z + 0.5f;
                    }
        if (landTiles == 0) land = null;
        bool IsSea(int x, int z) => land is not null && !land[x, z];

        int copies = layout == BiomeLayoutKind.Circle2 ? 2 : 1;
        var seeds = Seeds(shares, land is null ? w * w : landTiles, copies);
        uint? centreColor = layout switch
        {
            BiomeLayoutKind.CenterForest => ForestColor,
            BiomeLayoutKind.CenterWasteland => WastelandColor,
            _ => null,
        };
        bool IsCentre(Seed s) => centreColor is { } c && shares[s.Biome].Color == c;

        // Seed tiles. On a shaped world the layout is laid over the land: around its middle, as wide as its area.
        float midX = w * 0.5f, midZ = w * 0.5f, radius = w * 0.4f;
        if (land is not null)
        {
            midX = landX / landTiles;
            midZ = landZ / landTiles;
            radius = 0.71f * MathF.Sqrt(landTiles / MathF.PI);
        }
        if (layout == BiomeLayoutKind.Line)
        {
            double a = rand.Next(4) * Math.PI / 2;
            float dx = (float)Math.Cos(a) * radius, dz = (float)Math.Sin(a) * radius;
            for (int i = 0; i < seeds.Count; i++)
            {
                float t = seeds.Count == 1 ? 0 : i / (float)(seeds.Count - 1);
                Plant(seeds[i], (int)(midX - dx + 2 * dx * t), (int)(midZ - dz + 2 * dz * t));
            }
        }
        else
        {
            // The centre biome sits in the middle, the rest on a ring (Circle2: the second half on an inner ring).
            int split = layout == BiomeLayoutKind.Circle2 ? seeds.Count / 2 : -1;
            int slots = layout switch
            {
                BiomeLayoutKind.Circle => seeds.Count,
                BiomeLayoutKind.Circle2 => split,
                _ => seeds.Count - 1,
            };
            double angle = rand.NextDouble() * 360;
            double step = 360.0 / Math.Max(1, slots);
            if (rand.NextDouble() < 0.5) step = -step;
            for (int i = 0; i < seeds.Count; i++)
            {
                if (IsCentre(seeds[i]))
                {
                    Plant(seeds[i], (int)midX, (int)midZ);
                    continue;
                }
                if (i == split)
                {
                    angle += rand.Next(70, 200);
                    radius *= 0.5f;
                }
                double rad = angle * Math.PI / 180;
                Plant(seeds[i], (int)(midX + Math.Cos(rad) * radius), (int)(midZ + Math.Sin(rad) * radius));
                angle += step;
            }
        }

        void Plant(Seed s, int x, int z)
        {
            s.X = Math.Clamp(x, 0, w - 1);
            s.Z = Math.Clamp(z, 0, w - 1);
            if (land is not null) (s.X, s.Z) = NearestFreeLand(s.X, s.Z);
            s.Tiles--;
            tiles[s.X, s.Z] = s.Biome;
        }

        // The nearest land tile no biome has started on yet, searching in growing square rings.
        (int, int) NearestFreeLand(int x, int z)
        {
            for (int r = 0; r < w; r++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                        int px = x + dx, pz = z + dz;
                        if (px >= 0 && pz >= 0 && px < w && pz < w && !IsSea(px, pz) && tiles[px, pz] < 0) return (px, pz);
                    }
            return (x, z);
        }

        // Grow each biome to its quota, a few tiles per round (more for bigger shares). The centre biome keeps off the
        // map edge, as RWG's does.
        int centreEdge = 1 + worldSize / 2048;
        bool grew;
        do
        {
            grew = false;
            foreach (var s in seeds)
            {
                if (s.Tiles <= 0) continue;
                int edge = IsCentre(s) ? centreEdge : 0;
                int tries = 1 + (int)(s.Percent * 4);
                for (int k = 0; k < tries && s.Tiles > 0; k++)
                {
                    var claimed = Grow(tiles, w, s, edge, rand, IsSea);
                    if (claimed == Claim.None) break;
                    // Sea is crossed for free: a biome's share is of the land.
                    if (claimed == Claim.Land) s.Tiles--;
                    grew = true;
                }
            }
        } while (grew);

        // Then every biome but the fallback takes whatever empty tiles it can still reach; the rest become the fallback.
        do
        {
            grew = false;
            foreach (var s in seeds)
                if (s.Biome != fallback && Grow(tiles, w, s, 0, rand, IsSea) != Claim.None) grew = true;
        } while (grew);

        for (int x = 0; x < w; x++)
            for (int z = 0; z < w; z++)
                if (tiles[x, z] < 0) tiles[x, z] = fallback;
        return tiles;
    }

    /// <summary>The biome that fills what nothing else reaches: forest, as in RWG, or the biggest share without forest.</summary>
    public static int Fallback(IReadOnlyList<BiomeShare> shares)
    {
        int best = -1;
        for (int i = 0; i < shares.Count; i++)
        {
            if (shares[i].Weight <= 0) continue;
            if (shares[i].Color == ForestColor) return i;
            if (best < 0 || shares[i].Weight > shares[best].Weight) best = i;
        }
        return best;
    }

    /// <summary>Each biome's share and tile quota, biggest first; leftover tiles go round the list one at a time.</summary>
    private static List<Seed> Seeds(IReadOnlyList<BiomeShare> shares, int totalTiles, int copies)
    {
        float total = shares.Sum(s => Math.Max(0, s.Weight)) * (float)copies;
        var seeds = new List<Seed>();
        for (int c = 0; c < copies; c++)
        {
            seeds.AddRange(shares.Select((s, i) => (s, i)).Where(p => p.s.Weight > 0)
                .Select(p =>
                {
                    float percent = p.s.Weight / total;
                    return new Seed(p.i, percent, Math.Max(1, (int)MathF.Floor(percent * totalTiles)));
                })
                .OrderByDescending(s => s.Percent));
        }
        int given = seeds.Sum(s => s.Tiles);
        for (int i = 0; given < totalTiles; given++, i = (i + 1) % seeds.Count) seeds[i].Tiles++;
        return seeds;
    }

    /// <summary>
    /// Claims the nearest empty tile beside one of the biome's own, searching outwards from its seed (jittered by up to
    /// 2 tiles) in growing square rings. Says whether it was land or sea, or that none is in reach.
    /// </summary>
    private static Claim Grow(int[,] tiles, int w, Seed s, int edge, Random rand, Func<int, int, bool> isSea)
    {
        int max = w - 1 - edge;
        int cx = s.X + rand.Next(-2, 3), cz = s.Z + rand.Next(-2, 3);
        int claimedX = 0, claimedZ = 0;
        for (int r = 1; r < 40; r++)
        {
            for (int d = 0; d <= r; d++)
            {
                if (TryClaim(cx - d, cz - r) || TryClaim(cx + d, cz - r) || TryClaim(cx - d, cz + r) || TryClaim(cx + d, cz + r)
                    || TryClaim(cx - r, cz - d) || TryClaim(cx - r, cz + d) || TryClaim(cx + r, cz - d) || TryClaim(cx + r, cz + d))
                    return isSea(claimedX, claimedZ) ? Claim.Sea : Claim.Land;
            }
        }
        return Claim.None;

        bool TryClaim(int x, int z)
        {
            if (x < edge || x > max || z < edge || z > max || tiles[x, z] >= 0) return false;
            bool beside = x > 0 && tiles[x - 1, z] == s.Biome || x < w - 1 && tiles[x + 1, z] == s.Biome
                          || z > 0 && tiles[x, z - 1] == s.Biome || z < w - 1 && tiles[x, z + 1] == s.Biome;
            if (!beside) return false;
            tiles[x, z] = s.Biome;
            (claimedX, claimedZ) = (x, z);
            return true;
        }
    }

    private enum Claim
    {
        None,
        Land,
        Sea,
    }

    /// <summary>The biomes in the order a player progresses through them: forest first, wasteland last.</summary>
    private static readonly uint[] Progression = [ForestColor, BurntColor, DesertColor, SnowColor, WastelandColor];

    /// <summary>
    /// Which step of the progression (0 = forest) each island gets, as each biome's share of the land by area asks (#46).
    /// The start island is the one whose size best fits the first biome's share, the nearer the middle the better when
    /// two fit about as well. From it the islands, in order of distance, are split into one run per biome in progression
    /// order, so the order holds outward. With at least as many islands as biomes every biome gets one or more; with
    /// fewer each island gets a biome of its own. Of those splits it takes the one whose areas come closest to the
    /// shares. An island can't be split, so the match is close, not exact.
    /// </summary>
    public static int[] AssignIslands(IReadOnlyList<(float X, float Z, int Cells)> islands, IReadOnlyList<int> weights, float midX, float midZ)
    {
        var steps = new int[islands.Count];
        if (islands.Count == 0 || weights.Count <= 1) return steps;
        double area = islands.Sum(i => (double)i.Cells), total = weights.Sum();
        var bandEnd = new double[weights.Count];
        double sum = 0;
        for (int b = 0; b < weights.Count; b++) bandEnd[b] = (sum += weights[b]) / total * area;

        double far = Math.Max(1, islands.Max(i => Math.Sqrt(Sq(i.X - midX) + Sq(i.Z - midZ))));
        int start = Enumerable.Range(0, islands.Count).MinBy(i =>
            Math.Abs(islands[i].Cells - bandEnd[0]) / area + 0.1 * Math.Sqrt(Sq(islands[i].X - midX) + Sq(islands[i].Z - midZ)) / far);

        var order = Enumerable.Range(0, islands.Count)
            .OrderBy(i => i == start ? -1 : Sq(islands[i].X - islands[start].X) + Sq(islands[i].Z - islands[start].Z)).ToList();
        int n = order.Count, k = weights.Count;
        var target = new double[k];
        for (int b = 0; b < k; b++) target[b] = (b == 0 ? bandEnd[0] : bandEnd[b] - bandEnd[b - 1]);
        var prefix = new double[n + 1];
        for (int j = 0; j < n; j++) prefix[j + 1] = prefix[j] + islands[order[j]].Cells;

        // best[b, j]: the least area error with biomes 0..b given the first j islands in order. Each biome takes a run
        // of islands; with fewer islands than biomes a run is one island and a biome may be left out (its whole share
        // missed), else every biome takes at least one. The start island is always the first biome's.
        bool spare = n < k;
        var best = new double[k, n + 1];
        var from = new int[k, n + 1];
        for (int b = 0; b < k; b++)
            for (int j = 0; j <= n; j++)
                best[b, j] = double.PositiveInfinity;
        for (int j = 1; j <= (spare ? 1 : n); j++) best[0, j] = Math.Abs(prefix[j] - target[0]);
        for (int b = 1; b < k; b++)
            for (int j = 1; j <= n; j++)
            {
                if (spare && best[b - 1, j] + target[b] < best[b, j])
                {
                    best[b, j] = best[b - 1, j] + target[b];
                    from[b, j] = j;   // left out: no islands
                }
                for (int i = spare ? j - 1 : b; i < j; i++)
                {
                    if (i < 1) continue;
                    double cost = best[b - 1, i] + Math.Abs(prefix[j] - prefix[i] - target[b]);
                    if (cost < best[b, j])
                    {
                        best[b, j] = cost;
                        from[b, j] = i;
                    }
                }
            }
        for (int b = k - 1, j = n; b >= 0; b--)
        {
            int i = b == 0 ? 0 : from[b, j];
            for (int x = i; x < j; x++) steps[order[x]] = b;
            j = i;
        }
        return steps;
    }

    /// <summary>
    /// One biome per island (<see cref="BiomeLayoutKind.PerIsland"/>, #46), painted into a layer of
    /// <paramref name="worldSize"/>/8 cells. Forest first, then the harder biomes outward, each with about its share of
    /// the land by area (<see cref="AssignIslands"/>). Biomes with no share are left
    /// out; mod biomes with a share come after wasteland. The sea takes the nearest island's biome.
    /// </summary>
    public static IndexedLayer PaintPerIsland(int worldSize, IReadOnlyList<BiomeShare> shares, LandField land)
    {
        var order = Progression.Where(c => shares.Any(s => s.Color == c && s.Weight > 0)).ToList();
        order.AddRange(shares.Where(s => s.Weight > 0 && !order.Contains(s.Color)).Select(s => s.Color));
        if (order.Count == 0) order.Add(shares[Math.Max(0, Fallback(shares))].Color);

        var (labels, islands) = land.Islands();
        var colourOf = new uint[Math.Max(1, islands.Count)];
        if (islands.Count > 0)
        {
            var weights = order.Select(c => shares.Where(s => s.Color == c).Sum(s => s.Weight)).ToList();
            var steps = AssignIslands(islands, weights, worldSize / 2f, worldSize / 2f);
            for (int i = 0; i < islands.Count; i++) colourOf[i] = order[steps[i]];
        }
        else colourOf[0] = order[0];

        // The sea takes the nearest island's number, spreading out from every coast at once.
        int w = land.Width;
        var nearest = (int[,])labels.Clone();
        var queue = new Queue<(int X, int Z)>();
        for (int z = 0; z < w; z++)
            for (int x = 0; x < w; x++)
                if (nearest[x, z] >= 0) queue.Enqueue((x, z));
        while (queue.TryDequeue(out var c))
            foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int x = c.X + dx, z = c.Z + dz;
                if (x < 0 || z < 0 || x >= w || z >= w || nearest[x, z] >= 0) continue;
                nearest[x, z] = nearest[c.X, c.Z];
                queue.Enqueue((x, z));
            }

        var palette = colourOf.Distinct().ToList();
        int cells = worldSize / 8, per = Math.Max(1, LandField.Cell / 8);
        var grid = new Grid<byte>(cells, cells);
        for (int z = 0; z < cells; z++)
            for (int x = 0; x < cells; x++)
            {
                int island = nearest[Math.Min(w - 1, x / per), Math.Min(w - 1, z / per)];
                grid[x, z] = (byte)palette.IndexOf(colourOf[Math.Max(0, island)]);
            }
        return new IndexedLayer(grid, palette);
    }

    private static float Sq(float v) => v * v;

    /// <summary>
    /// Paints planned tiles into a biome layer of <paramref name="worldSize"/>/8 cells. Each tile presses the filler
    /// stamp's shape in its biome (randomly scaled and turned) over a fallback-biome background, so neighbours bite into
    /// each other. Without the stamp, tiles are painted as squares.
    /// </summary>
    public static IndexedLayer Paint(int worldSize, IReadOnlyList<BiomeShare> shares, int[,] tiles, StampImage? filler, Random rand)
    {
        int cells = worldSize / 8, perTile = TileSize / 8, w = worldSize / TileSize;
        // Palette: the biomes the plan uses, in share order.
        var used = Enumerable.Range(0, shares.Count).Where(i => tiles.Cast<int>().Contains(i)).ToList();
        var paletteIndex = new byte[shares.Count];
        for (int i = 0; i < used.Count; i++) paletteIndex[used[i]] = (byte)i;
        var grid = new Grid<byte>(cells, cells);
        Array.Fill(grid.Data, paletteIndex[Fallback(shares)]);

        for (int tz = 0; tz < w; tz++)
        {
            for (int tx = 0; tx < w; tx++)
            {
                byte value = paletteIndex[tiles[tx, tz]];
                if (filler is null)
                {
                    for (int z = tz * perTile; z < (tz + 1) * perTile; z++)
                        for (int x = tx * perTile; x < (tx + 1) * perTile; x++)
                            grid[x, z] = value;
                    continue;
                }
                float scale = perTile / (float)filler.Size * 1.5f + (float)rand.NextDouble() * 0.2f;
                float angle = rand.Next(4) * 90 + rand.Next(-20, 20);
                PressShape(grid, filler, tx * perTile + perTile / 2f, tz * perTile + perTile / 2f, scale, angle, value);
            }
        }
        return new IndexedLayer(grid, used.Select(i => shares[i].Color).ToList());
    }

    /// <summary>Sets every cell where the stamp's alpha, scaled and turned about (cx, cz), is above 0.1.</summary>
    private static void PressShape(Grid<byte> grid, StampImage stamp, float cx, float cz, float scale, float degrees, byte value)
    {
        float half = stamp.Size * scale * 0.7072f; // half the diagonal, so any turn fits
        double rad = degrees * Math.PI / 180;
        float cos = (float)Math.Cos(rad), sin = (float)Math.Sin(rad);
        int x0 = Math.Max(0, (int)(cx - half)), x1 = Math.Min(grid.Width - 1, (int)(cx + half));
        int z0 = Math.Max(0, (int)(cz - half)), z1 = Math.Min(grid.Height - 1, (int)(cz + half));
        float mid = stamp.Size / 2f;
        for (int z = z0; z <= z1; z++)
        {
            float dz = (z + 0.5f - cz) / scale;
            for (int x = x0; x <= x1; x++)
            {
                float dx = (x + 0.5f - cx) / scale;
                // Turn the cell back into the stamp's own frame.
                float u = dx * cos + dz * sin + mid, v = -dx * sin + dz * cos + mid;
                if (stamp.SampleAlpha(u, v) > 0.1f) grid[x, z] = value;
            }
        }
    }
}
