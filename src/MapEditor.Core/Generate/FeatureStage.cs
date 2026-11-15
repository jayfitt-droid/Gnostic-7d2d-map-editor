using System.Text.Json.Serialization;
using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Generate;

/// <summary>RWG's terrain features, in the order it places them.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FeatureKind>))]
public enum FeatureKind
{
    Lake,
    River,
    Canyon,
    Crater,
}

/// <summary>How many of one feature to place, and the scale range of its stamps (rwgmixer's, before RWG's 1.4×).</summary>
public sealed class FeatureRow
{
    public FeatureKind Kind { get; set; }
    public int Count { get; set; }
    public float ScaleMin { get; set; } = 0.5f;
    public float ScaleMax { get; set; } = 1.5f;

    public FeatureRow Clone() => (FeatureRow)MemberwiseClone();
}

/// <summary>The features stage's settings.</summary>
public sealed class FeatureOptions
{
    public int Seed { get; set; } = 1;

    /// <summary>One row per kind; empty means rwgmixer's defaults for the world's size.</summary>
    public List<FeatureRow> Rows { get; set; } = [];

    /// <summary>Lakes hold water at their own ground level, so they may sit on mountain tiles too, instead of RWG's fixed 30 m.</summary>
    public bool MountainLakes { get; set; }

    /// <summary>Each river is a chain of river stamps running downhill until it reaches water or the map edge, or ends in a lake.</summary>
    public bool LongRivers { get; set; }

    /// <summary>The most river stamps in one long river.</summary>
    public int RiverSegments { get; set; } = 8;

    public FeatureOptions Clone() => new()
    {
        Seed = Seed,
        Rows = [.. Rows.Select(r => r.Clone())],
        MountainLakes = MountainLakes,
        LongRivers = LongRivers,
        RiverSegments = RiverSegments,
    };
}

/// <summary>One feature stamp pressed into the world: enough to take it out again exactly.</summary>
public sealed class Feature
{
    public FeatureKind Kind { get; set; }
    public string Stamp { get; set; } = "";
    public float X { get; set; }
    public float Z { get; set; }
    public float Size { get; set; }
    public float Rotation { get; set; }

    /// <summary>The ground the stamp is pressed relative to: it lowers the ground as RWG's would lower this height.</summary>
    public float BaseHeight { get; set; } = 35;

    /// <summary>The water surface it fills to; 0 for a dry feature.</summary>
    public byte WaterLevel { get; set; }

    /// <summary>Placed by hand: re-running the stage keeps it.</summary>
    public bool Pinned { get; set; }

    /// <summary>Whether it is in the world now (a terrain re-run takes features out; pinned ones come back on the next run).</summary>
    public bool Drawn { get; set; }

    /// <summary>The long river this stamp belongs to (its segments and end lake share the number); 0 for none.</summary>
    public int Chain { get; set; }

    /// <summary>The 150 m cells it holds, as [cx, cz].</summary>
    public List<int[]> Cells { get; set; } = [];

    /// <summary>The water cells it filled, as runs [z, x0, x1) of one row each.</summary>
    public List<int[]> Water { get; set; } = [];

    /// <summary>Cells where the ground would have gone under the 2 m floor, as [x, z, raw height not taken off].</summary>
    public List<int[]> Clipped { get; set; } = [];

    public Feature Clone() => new()
    {
        Kind = Kind,
        Stamp = Stamp,
        X = X,
        Z = Z,
        Size = Size,
        Rotation = Rotation,
        BaseHeight = BaseHeight,
        WaterLevel = WaterLevel,
        Pinned = Pinned,
        Drawn = Drawn,
        Chain = Chain,
        Cells = [.. Cells.Select(c => (int[])c.Clone())],
        Water = [.. Water.Select(r => (int[])r.Clone())],
        Clipped = [.. Clipped.Select(r => (int[])r.Clone())],
    };

    public StampPlacement Placement => new(X, Z, Size, Rotation, 1, Additive: true, HeightOffset: -BaseHeight);

    /// <summary>Where the stamp can reach, in world metres.</summary>
    public GridRect Reach(int worldSize) => StampOp.Reach(Placement, worldSize, worldSize);
}

/// <summary>What a features run did.</summary>
public sealed record FeatureResult(List<Feature> Added, List<string> Shortfalls, int Removed);

/// <summary>Which 150 m cells a feature may use, RWG's StreetTile flags for generateTerrainFeature.</summary>
internal sealed class FeatureCells
{
    private readonly byte[] _flags;
    private const byte Blocked = 1, Taken = 2, Mountain = 4, Sea = 8;

    public int Width { get; }

    private FeatureCells(int width, byte[] flags)
    {
        Width = width;
        _flags = flags;
    }

    public bool Inside(int cx, int cz) => (uint)cx < (uint)Width && (uint)cz < (uint)Width;

    /// <summary>
    /// Towns (planned or built) and cells holding prefabs are blocked, as RWG's township, district and Used flags; the
    /// terrain plan's mountain tiles can't start a feature; cells of features already there are taken.
    /// </summary>
    public static FeatureCells Build(World world, PrefabLibrary library, IEnumerable<TownSite> sites, TerrainType[]? terrainPlan,
        IEnumerable<Feature> standing)
    {
        int width = world.Size / FeatureStage.Cell;
        var flags = new byte[width * width];
        void Set(int cx, int cz, byte flag)
        {
            if ((uint)cx < (uint)width && (uint)cz < (uint)width) flags[cx + cz * width] |= flag;
        }

        foreach (var site in sites)
            foreach (var (cx, cz) in site.AllCells) Set(cx, cz, Blocked);
        foreach (var p in world.Prefabs)
        {
            var f = PrefabPlacer.Footprint(world, p, library);
            for (int cz = Math.Max(0, f.Z0 / FeatureStage.Cell); cz <= Math.Min(width - 1, (f.Z1 - 1) / FeatureStage.Cell); cz++)
                for (int cx = Math.Max(0, f.X0 / FeatureStage.Cell); cx <= Math.Min(width - 1, (f.X1 - 1) / FeatureStage.Cell); cx++)
                    Set(cx, cz, Blocked);
        }
        foreach (var feature in standing)
            foreach (var c in feature.Cells) Set(c[0], c[1], Taken);

        int tiles = world.Size / TerrainStage.TileSize;
        if (terrainPlan is not null && terrainPlan.Length == tiles * tiles)
            for (int cz = 0; cz < width; cz++)
                for (int cx = 0; cx < width; cx++)
                {
                    // A street tile's terrain type is the 256 m tile under its centre (StreetTile.TerrainType).
                    int tx = Math.Min((cx * FeatureStage.Cell + FeatureStage.Cell / 2) / TerrainStage.TileSize, tiles - 1);
                    int tz = Math.Min((cz * FeatureStage.Cell + FeatureStage.Cell / 2) / TerrainStage.TileSize, tiles - 1);
                    var type = terrainPlan[tx + tz * tiles];
                    if (type == TerrainType.Mountains) flags[cx + cz * width] |= Mountain;
                    // A land shape's sea tiles get no terrain; no feature starts out in that sea.
                    else if (type == TerrainType.None && IsWet(world, cx, cz)) flags[cx + cz * width] |= Sea;
                }
        return new FeatureCells(width, flags);
    }

    private static bool IsWet(World world, int cx, int cz)
    {
        if (world.Water is not { } water) return false;
        int x = Math.Min(cx * FeatureStage.Cell + FeatureStage.Cell / 2, world.Size - 1);
        int z = Math.Min(cz * FeatureStage.Cell + FeatureStage.Cell / 2, world.Size - 1);
        byte w = water[x, z];
        return w > 0 && World.HeightToMetres(world.Height[x, z]) <= w + 0.5f;
    }

    /// <summary>
    /// A cell a feature may be centred on: free, not out at sea, not a mountain (unless allowed), and with all four neighbours
    /// (RWG's GetNeighborCount() &gt; 3 keeps features off the edge row).
    /// </summary>
    public bool CanStart(int cx, int cz, bool allowMountain) =>
        cx > 0 && cz > 0 && cx < Width - 1 && cz < Width - 1
        && (_flags[cx + cz * Width] & (Blocked | Taken | Sea)) == 0
        && (allowMountain || (_flags[cx + cz * Width] & Mountain) == 0);

    /// <summary>The cells a footprint covers, or null if any is off the map, blocked or taken (other than by <paramref name="own"/>).</summary>
    public List<int[]>? Footprint(float x, float z, float half, HashSet<int>? own = null)
    {
        int cx0 = (int)MathF.Floor((x - half) / FeatureStage.Cell), cx1 = (int)MathF.Floor((x + half - 1) / FeatureStage.Cell);
        int cz0 = (int)MathF.Floor((z - half) / FeatureStage.Cell), cz1 = (int)MathF.Floor((z + half - 1) / FeatureStage.Cell);
        if (cx0 < 0 || cz0 < 0 || cx1 >= Width || cz1 >= Width) return null;
        var cells = new List<int[]>((cx1 - cx0 + 1) * (cz1 - cz0 + 1));
        for (int cz = cz0; cz <= cz1; cz++)
            for (int cx = cx0; cx <= cx1; cx++)
            {
                int i = cx + cz * Width;
                byte f = _flags[i];
                if ((f & Blocked) != 0) return null;
                if ((f & Taken) != 0 && own?.Contains(i) != true) return null;
                cells.Add([cx, cz]);
            }
        return cells;
    }

    /// <summary>The given cells, or null if any is off the map, blocked or taken (other than by <paramref name="own"/>).</summary>
    public List<int[]>? Free(IEnumerable<(int Cx, int Cz)> wanted, HashSet<int>? own = null)
    {
        var cells = new List<int[]>();
        foreach (var (cx, cz) in wanted)
        {
            if (!Inside(cx, cz)) return null;
            byte f = _flags[cx + cz * Width];
            if ((f & Blocked) != 0 || (f & Taken) != 0 && own?.Contains(cx + cz * Width) != true) return null;
            cells.Add([cx, cz]);
        }
        return cells;
    }

    public void Take(IEnumerable<int[]> cells, HashSet<int>? own = null)
    {
        foreach (var c in cells)
        {
            if (!Inside(c[0], c[1])) continue;
            _flags[c[0] + c[1] * Width] |= Taken;
            own?.Add(c[0] + c[1] * Width);
        }
    }
}

/// <summary>
/// RWG's terrain features (WorldBuilder.GenerateTerrainLast and generateTerrainFeature): lakes, rivers, canyons and
/// craters on random free 150 m cells. Unlike RWG, which draws them under the terrain stamps, they are pressed into the
/// finished ground: each lowers the ground by what its stamp would take off RWG's flat base, so hand edits stay and it
/// works on any world. Lakes and rivers fill with water; a canyon or crater that touches water fills too.
/// </summary>
public static class FeatureStage
{
    public const int Cell = TownSiteStage.Cell;

    /// <summary>RWG multiplies every feature's scale by this (generateTerrainFeature).</summary>
    public const float ScaleFactor = 1.4f;

    public static readonly FeatureKind[] Order = [FeatureKind.Lake, FeatureKind.River, FeatureKind.Canyon, FeatureKind.Crater];

    /// <summary>The stamp names RWG picks from (any stamp starting with this).</summary>
    public static string StampPrefix(FeatureKind kind) => kind switch
    {
        FeatureKind.River => "river",
        FeatureKind.Canyon => "canyon",
        FeatureKind.Crater => "crater",
        _ => "lake",
    };

    public static string Plural(FeatureKind kind) => StampPrefix(kind) + "s";

    /// <summary>rwgmixer's count and scale for each kind at the world's size.</summary>
    public static List<FeatureRow> DefaultRows(RwgMixer mixer) =>
    [
        .. Order.Select(kind =>
        {
            var (scale, counts) = mixer.FeatureRule(Plural(kind));
            return new FeatureRow { Kind = kind, Count = counts.Default, ScaleMin = scale.Min, ScaleMax = scale.Max };
        }),
    ];

    /// <summary>The rows to use: the options' own, with any kind they leave out taken from rwgmixer.</summary>
    public static List<FeatureRow> RowsFor(FeatureOptions options, RwgMixer mixer)
    {
        var defaults = DefaultRows(mixer);
        return [.. defaults.Select(d => options.Rows.FirstOrDefault(r => r.Kind == d.Kind) ?? d)];
    }

    /// <summary>
    /// Takes out the features the last run placed (pinned ones stay), then places and presses in new ones as part of
    /// <paramref name="session"/>. <paramref name="features"/> is updated in place, and is part of the undo step.
    /// </summary>
    public static FeatureResult Run(World world, FeatureOptions options, List<Feature> features, StampLibrary stamps, RwgMixer mixer,
        PrefabLibrary library, IEnumerable<TownSite> sites, TerrainType[]? terrainPlan, float baseHeight, EditSession session,
        IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        var all = new GridRect(0, 0, world.Size, world.Size);
        session.TouchList(features, f => f.Clone(), all);

        // Out with the last run's features; pinned ones stay where they are.
        int removed = 0;
        foreach (var f in features.Where(f => !f.Pinned && f.Drawn).ToList())
        {
            TakeOut(world, f, stamps, session);
            removed++;
        }
        features.RemoveAll(f => !f.Pinned);

        var cells = FeatureCells.Build(world, library, sites, terrainPlan, features);
        var added = new List<Feature>();
        var shortfalls = new List<string>();
        int nextChain = features.Count == 0 ? 1 : features.Max(f => f.Chain) + 1;

        foreach (var row in RowsFor(options, mixer))
        {
            cancel.ThrowIfCancellationRequested();
            var names = stamps.WithPrefix(StampPrefix(row.Kind));
            if (row.Count <= 0) continue;
            if (names.Count == 0)
            {
                shortfalls.Add($"No {StampPrefix(row.Kind)} stamps in the game's Data/Stamps.");
                continue;
            }
            // Each kind has its own random stream, as RWG seeds each feature by its name, so changing one count leaves
            // the other kinds where they were.
            var rand = new Random(unchecked(options.Seed * 397 ^ ((int)row.Kind + 1) * 7919));
            int placed = 0;
            for (int i = 0; i < row.Count; i++)
            {
                List<Feature>? made = row.Kind == FeatureKind.River && options.LongRivers
                    ? PlaceRiver(world, cells, stamps, names, row, options, baseHeight, rand, nextChain, null)
                    : PlaceOne(world, cells, stamps, names, row, options, baseHeight, rand, added) is { } one ? [one] : null;
                if (made is null) continue;
                if (made.Any(f => f.Chain != 0)) nextChain++;
                added.AddRange(made);
                placed++;
            }
            if (placed < row.Count) shortfalls.Add($"Placed {placed} of {row.Count} {Plural(row.Kind)}.");
        }

        // Pinned features a terrain run took out come back too, filling to the new ground's level.
        var back = features.Where(f => !f.Drawn).ToList();
        foreach (var f in back.Where(f => f.Chain == 0))
            f.WaterLevel = WaterLevelFor(world, f, stamps.Get(f.Stamp), options, f.BaseHeight, features.Where(e => e.Drawn));
        var toDraw = back.Concat(added).ToList();
        Draw(world, toDraw, stamps, session, progress, cancel);
        features.AddRange(added);
        return new FeatureResult(added, shortfalls, removed);
    }

    /// <summary>
    /// Places one feature by hand at (<paramref name="x"/>, <paramref name="z"/>), pinned, and presses it in. With long
    /// rivers on, a river runs downhill from there. Nothing is checked: hand placement may go anywhere.
    /// </summary>
    public static List<Feature> PlaceByHand(World world, FeatureKind kind, float x, float z, FeatureOptions options, List<Feature> features,
        StampLibrary stamps, RwgMixer mixer, PrefabLibrary library, IEnumerable<TownSite> sites, TerrainType[]? terrainPlan, float baseHeight,
        int seed, EditSession session)
    {
        var names = stamps.WithPrefix(StampPrefix(kind));
        if (names.Count == 0) throw new InvalidOperationException($"No {StampPrefix(kind)} stamps in the game's Data/Stamps.");
        var row = RowsFor(options, mixer).First(r => r.Kind == kind);
        var rand = new Random(seed);
        session.TouchList(features, f => f.Clone(), new GridRect(0, 0, world.Size, world.Size));

        List<Feature> made;
        if (kind == FeatureKind.River && options.LongRivers)
        {
            var cells = FeatureCells.Build(world, library, sites, terrainPlan, features);
            int chain = features.Count == 0 ? 1 : features.Max(f => f.Chain) + 1;
            made = PlaceRiver(world, cells, stamps, names, row, options, baseHeight, rand, chain, (x, z)) ?? [];
        }
        else
        {
            var stamp = stamps.Get(names[rand.Next(names.Count)]);
            var f = Make(kind, stamp, x, z, RandomScale(row, rand), rand.Next(360), baseHeight);
            f.Cells = CoveredCells(world.Size, x, z, Half(f));
            f.WaterLevel = WaterLevelFor(world, f, stamp, options, baseHeight, []);
            made = [f];
        }
        foreach (var f in made) f.Pinned = true;
        Draw(world, made, stamps, session, null, default);
        features.AddRange(made);
        return made;
    }

    /// <summary>Takes features out of the world and the list; a long river goes as a whole.</summary>
    public static void Remove(World world, List<Feature> features, IEnumerable<Feature> which, StampLibrary stamps, EditSession session)
    {
        var chains = which.Where(f => f.Chain != 0).Select(f => f.Chain).ToHashSet();
        var gone = features.Where(f => which.Contains(f) || f.Chain != 0 && chains.Contains(f.Chain)).ToList();
        if (gone.Count == 0) return;
        session.TouchList(features, f => f.Clone(), new GridRect(0, 0, world.Size, world.Size));
        foreach (var f in gone.Where(f => f.Drawn)) TakeOut(world, f, stamps, session);
        features.RemoveAll(gone.Contains);
    }

    /// <summary>
    /// Takes out every drawn feature reaching <paramref name="area"/> (world metres; null for all), for a terrain run.
    /// Pinned ones stay in the list, not drawn; the rest are dropped. Returns how many came out.
    /// </summary>
    public static int TakeDown(World world, List<Feature> features, GridRect? area, StampLibrary stamps, EditSession session)
    {
        var hit = features.Where(f => f.Drawn && (area is not { } a || Overlaps(f.Reach(world.Size), a))).ToList();
        if (hit.Count == 0) return 0;
        // A long river comes out whole, so none of it is left half drawn.
        var chains = hit.Where(f => f.Chain != 0).Select(f => f.Chain).ToHashSet();
        hit = features.Where(f => f.Drawn && (hit.Contains(f) || f.Chain != 0 && chains.Contains(f.Chain))).ToList();
        session.TouchList(features, f => f.Clone(), new GridRect(0, 0, world.Size, world.Size));
        foreach (var f in hit)
        {
            TakeOut(world, f, stamps, session);
            f.Drawn = false;
        }
        features.RemoveAll(f => !f.Pinned && !f.Drawn);
        return hit.Count;
    }

    private static bool Overlaps(GridRect a, GridRect b) => a.X0 < b.X1 && b.X0 < a.X1 && a.Z0 < b.Z1 && b.Z0 < a.Z1;

    private static float RandomScale(FeatureRow row, Random rand)
    {
        float min = Math.Min(row.ScaleMin, row.ScaleMax), max = Math.Max(row.ScaleMin, row.ScaleMax);
        return (min + (float)rand.NextDouble() * (max - min)) * ScaleFactor;
    }

    private static Feature Make(FeatureKind kind, StampImage stamp, float x, float z, float scale, float rotation, float baseHeight) => new()
    {
        Kind = kind,
        Stamp = stamp.Name,
        X = x,
        Z = z,
        Size = stamp.Size * scale,
        Rotation = rotation,
        BaseHeight = baseHeight,
    };

    /// <summary>Half the width of the rotated stamp's bounding box.</summary>
    private static float Half(Feature f)
    {
        float a = f.Rotation * MathF.PI / 180f;
        return f.Size / 2 * (MathF.Abs(MathF.Cos(a)) + MathF.Abs(MathF.Sin(a)));
    }

    private static List<int[]> CoveredCells(int worldSize, float x, float z, float half)
    {
        int width = worldSize / Cell;
        var cells = new List<int[]>();
        for (int cz = Math.Max(0, (int)MathF.Floor((z - half) / Cell)); cz <= Math.Min(width - 1, (int)MathF.Floor((z + half - 1) / Cell)); cz++)
            for (int cx = Math.Max(0, (int)MathF.Floor((x - half) / Cell)); cx <= Math.Min(width - 1, (int)MathF.Floor((x + half - 1) / Cell)); cx++)
                cells.Add([cx, cz]);
        return cells;
    }

    /// <summary>
    /// RWG's placement: a random stamp, scale and turn, then the free cells in random order until one has its whole
    /// footprint free. Null if none has.
    /// </summary>
    private static Feature? PlaceOne(World world, FeatureCells cells, StampLibrary stamps, IReadOnlyList<string> names, FeatureRow row,
        FeatureOptions options, float baseHeight, Random rand, IEnumerable<Feature> earlier)
    {
        var stamp = stamps.Get(names[rand.Next(names.Count)]);
        float scale = RandomScale(row, rand);
        int rotation = rand.Next(360);
        bool mountains = row.Kind == FeatureKind.Lake && options.MountainLakes;

        foreach (var (cx, cz) in Shuffled(cells, rand, mountains))
        {
            float x = cx * Cell + Cell / 2f, z = cz * Cell + Cell / 2f;
            var f = Make(row.Kind, stamp, x, z, scale, rotation, baseHeight);
            if (cells.Footprint(x, z, Half(f)) is not { } covered) continue;
            cells.Take(covered);
            f.Cells = covered;
            f.WaterLevel = WaterLevelFor(world, f, stamp, options, baseHeight, earlier);
            return f;
        }
        return null;
    }

    private static List<(int, int)> Shuffled(FeatureCells cells, Random rand, bool allowMountain)
    {
        var list = new List<(int, int)>();
        for (int cz = 0; cz < cells.Width; cz++)
            for (int cx = 0; cx < cells.Width; cx++)
                if (cells.CanStart(cx, cz, allowMountain)) list.Add((cx, cz));
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rand.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }

    /// <summary>RWG's water surface (WorldBuilder.WaterHeight).</summary>
    private const byte RwgWater = StampOp.DefaultWaterLevel;

    /// <summary>
    /// The water a feature fills to. Lakes and rivers: RWG's 30 m, or for mountain lakes as far under their own rim
    /// as RWG's lakes sit under its base. Canyons and craters: only if they touch water already there or placed
    /// earlier in the run, at that water's level.
    /// </summary>
    private static byte WaterLevelFor(World world, Feature f, StampImage stamp, FeatureOptions options, float baseHeight, IEnumerable<Feature> earlier)
    {
        switch (f.Kind)
        {
            case FeatureKind.Lake when options.MountainLakes:
                return Level(RimGround(world, f, stamp) - (baseHeight - RwgWater));
            case FeatureKind.Lake:
            case FeatureKind.River:
                return RwgWater;
        }

        // RWG checks the stamp's area against the water stamps' areas; here, against water in the world or placed
        // earlier in this run.
        float half = Half(f);
        var box = new GridRect((int)(f.X - half), (int)(f.Z - half), (int)(f.X + half), (int)(f.Z + half)).Clamp(world.Size, world.Size);
        byte level = 0;
        foreach (var e in earlier)
        {
            if (e.WaterLevel == 0) continue;
            float eh = Half(e);
            var eb = new GridRect((int)(e.X - eh), (int)(e.Z - eh), (int)(e.X + eh), (int)(e.Z + eh));
            if (Overlaps(box, eb)) level = Math.Max(level, e.WaterLevel);
        }
        if (world.Water is { } water)
            for (int z = box.Z0; z < box.Z1; z += 4)
            {
                var w = water.Row(z);
                var h = world.Height.Row(z);
                for (int x = box.X0; x < box.X1; x += 4)
                    if (w[x] > level && h[x] / 257f - 0.5f <= w[x]) level = w[x];
            }
        return level;
    }

    private static byte Level(float metres) => (byte)Math.Clamp((int)MathF.Round(metres), 1, 250);

    /// <summary>The lowest ground along the stamp's faint edge, before it is pressed in.</summary>
    private static float RimGround(World world, Feature f, StampImage stamp)
    {
        float min = float.MaxValue;
        ForEachSample(world.Size, f, stamp, 8, (x, z, alpha) =>
        {
            if (alpha is > 0.01f and < 0.1f) min = Math.Min(min, world.Height[x, z] / 257f);
        });
        return min == float.MaxValue ? world.Height[(int)Math.Clamp(f.X, 0, world.Size - 1), (int)Math.Clamp(f.Z, 0, world.Size - 1)] / 257f : min;
    }

    /// <summary>The 150 m cells where the stamp's alpha shows (sampled every 25 m); off-map cells are included, to be refused.</summary>
    private static HashSet<(int, int)> ShapeCells(int worldSize, Feature f, StampImage stamp)
    {
        var cells = new HashSet<(int, int)>();
        var rect = GridRect.AroundCircle(f.X, f.Z, f.Size / 2 * 1.4143f);
        float scale = f.Size / stamp.Size;
        float angle = -f.Rotation * MathF.PI / 180f;
        float cos = MathF.Cos(angle), sin = MathF.Sin(angle), centre = stamp.Size / 2f;
        for (int z = rect.Z0; z < rect.Z1; z += 25)
            for (int x = rect.X0; x < rect.X1; x += 25)
            {
                float dx = x - f.X, dz = z - f.Z;
                if (stamp.SampleAlpha((dx * cos + dz * sin) / scale + centre, (-dx * sin + dz * cos) / scale + centre) <= 0.02f) continue;
                cells.Add(((int)MathF.Floor(x / (float)Cell), (int)MathF.Floor(z / (float)Cell)));
            }
        return cells;
    }

    /// <summary>Calls <paramref name="visit"/> with the stamp's alpha at every <paramref name="step"/>-th cell it reaches.</summary>
    private static void ForEachSample(int worldSize, Feature f, StampImage stamp, int step, Action<int, int, float> visit)
    {
        var rect = f.Reach(worldSize);
        float scale = f.Size / stamp.Size;
        float angle = -f.Rotation * MathF.PI / 180f;
        float cos = MathF.Cos(angle), sin = MathF.Sin(angle), centre = stamp.Size / 2f;
        for (int z = rect.Z0; z < rect.Z1; z += step)
            for (int x = rect.X0; x < rect.X1; x += step)
            {
                float dx = x - f.X, dz = z - f.Z;
                visit(x, z, stamp.SampleAlpha((dx * cos + dz * sin) / scale + centre, (-dx * sin + dz * cos) / scale + centre));
            }
    }

    /// <summary>
    /// Presses features into the world, all the ground first and then all the water, as RWG draws its lower layer
    /// before its water layer, so one feature's ground can't strand another's water.
    /// </summary>
    private static void Draw(World world, List<Feature> features, StampLibrary stamps, EditSession session, IProgress<double>? progress,
        CancellationToken cancel)
    {
        for (int i = 0; i < features.Count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            var f = features[i];
            var stamp = stamps.Get(f.Stamp);
            session.Touch(world.Height, f.Reach(world.Size));
            f.Clipped = Carve(world.Height, f, stamp);
            progress?.Report((i + 1) / (2.0 * Math.Max(1, features.Count)));
        }
        for (int i = 0; i < features.Count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            var f = features[i];
            f.Water = f.WaterLevel > 0 ? Fill(world, f, stamps.Get(f.Stamp), session) : [];
            f.Drawn = true;
            progress?.Report(0.5 + (i + 1) / (2.0 * Math.Max(1, features.Count)));
        }
    }

    /// <summary>
    /// Fills the feature's water mask (its alpha for stamps without one, as RWG's DrawWaterStamp) wherever the ground
    /// is at or under its surface and no water is there yet. Returns the cells it filled.
    /// </summary>
    private static List<int[]> Fill(World world, Feature f, StampImage stamp, EditSession session)
    {
        var rect = f.Reach(world.Size);
        if (rect.IsEmpty) return [];
        var water = world.Water ??= new Grid<byte>(world.Size, world.Size);
        session.Touch(water, rect);
        float scale = f.Size / stamp.Size;
        float angle = -f.Rotation * MathF.PI / 180f;
        float cos = MathF.Cos(angle), sin = MathF.Sin(angle), centre = stamp.Size / 2f;
        byte level = f.WaterLevel;
        var rows = new List<int[]>[rect.Z1 - rect.Z0];

        Parallel.For(rect.Z0, rect.Z1, z =>
        {
            var runs = new List<int[]>();
            var w = water.Row(z);
            var h = world.Height.Row(z);
            float dz = z - f.Z;
            int start = -1;
            for (int x = rect.X0; x <= rect.X1; x++)
            {
                bool wet = false;
                if (x < rect.X1 && w[x] == 0 && h[x] / 257f - 0.5f <= level)
                {
                    float dx = x - f.X;
                    float sx = (dx * cos + dz * sin) / scale + centre, sy = (-dx * sin + dz * cos) / scale + centre;
                    wet = (stamp.HasWater ? stamp.SampleWater(sx, sy) : stamp.SampleAlpha(sx, sy)) >= 1e-5f;
                }
                if (wet)
                {
                    w[x] = level;
                    if (start < 0) start = x;
                }
                else if (start >= 0)
                {
                    runs.Add([z, start, x]);
                    start = -1;
                }
            }
            rows[z - rect.Z0] = runs;
        });
        return [.. rows.SelectMany(r => r)];
    }

    /// <summary>RWG keeps the ground at least this high (AdjustHeights).</summary>
    private static readonly ushort Floor = TerrainBrush.ToRaw(2);

    /// <summary>
    /// Lowers the ground by what the stamp takes off RWG's base (its height minus the base, times its alpha), keeping
    /// it above the 2 m floor. Returns the cells the floor held up and by how much, so it can be taken out exactly.
    /// </summary>
    private static List<int[]> Carve(Grid<ushort> height, Feature f, StampImage stamp)
    {
        var rect = f.Reach(height.Width);
        var clipped = new List<int[]>[Math.Max(0, rect.Z1 - rect.Z0)];
        Walk(height, f, stamp, (z, row, x, delta) =>
        {
            int now = row[x], want = now + delta;
            int floor = Math.Min(now, Floor);
            int set = Math.Clamp(want, floor, ushort.MaxValue);
            row[x] = (ushort)set;
            if (set != want) (clipped[z - rect.Z0] ??= []).Add([x, z, set - want]);
        });
        return [.. clipped.Where(c => c is not null).SelectMany(c => c)];
    }

    /// <summary>Puts back exactly what <see cref="Carve"/> took off.</summary>
    private static void Uncarve(Grid<ushort> height, Feature f, StampImage stamp)
    {
        Walk(height, f, stamp, (_, row, x, delta) => row[x] = (ushort)Math.Clamp(row[x] - delta, 0, ushort.MaxValue));
        foreach (var c in f.Clipped) height[c[0], c[1]] = (ushort)Math.Clamp(height[c[0], c[1]] - c[2], 0, ushort.MaxValue);
        f.Clipped = [];
    }

    private delegate void CellChange(int z, Span<ushort> row, int x, int delta);

    /// <summary>The stamp's ground change at every cell it reaches, in raw height units, row by row on every core.</summary>
    private static void Walk(Grid<ushort> height, Feature f, StampImage stamp, CellChange change)
    {
        var rect = f.Reach(height.Width);
        if (rect.IsEmpty) return;
        float scale = f.Size / stamp.Size;
        float angle = -f.Rotation * MathF.PI / 180f;
        float cos = MathF.Cos(angle), sin = MathF.Sin(angle), centre = stamp.Size / 2f;
        Parallel.For(rect.Z0, rect.Z1, z =>
        {
            var row = height.Row(z);
            float dz = z - f.Z;
            for (int x = rect.X0; x < rect.X1; x++)
            {
                float dx = x - f.X;
                float sx = (dx * cos + dz * sin) / scale + centre, sy = (-dx * sin + dz * cos) / scale + centre;
                float alpha = stamp.SampleAlpha(sx, sy);
                if (alpha < 1e-5f) continue;
                int delta = (int)MathF.Round((stamp.SampleHeight(sx, sy) - f.BaseHeight) * alpha * 257f);
                if (delta != 0) change(z, row, x, delta);
            }
        });
    }

    /// <summary>Takes a drawn feature out: puts back the ground it lowered and clears the water it filled, if still its.</summary>
    private static void TakeOut(World world, Feature f, StampLibrary stamps, EditSession session)
    {
        var rect = f.Reach(world.Size);
        session.Touch(world.Height, rect);
        Uncarve(world.Height, f, stamps.Get(f.Stamp));
        if (world.Water is { } water && f.Water.Count > 0)
        {
            session.Touch(water, rect);
            foreach (var run in f.Water)
            {
                var row = water.Row(run[0]);
                for (int x = run[1]; x < run[2]; x++)
                    if (row[x] == f.WaterLevel) row[x] = 0;
            }
        }
        f.Water = [];
    }

    // ---- Long rivers ----

    private static readonly Dictionary<string, ((float X, float Y) A, (float X, float Y) B)?> Ends = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Where the river in a stamp leaves its edges, in stamp pixels: the two biggest stretches of the border its
    /// water (or alpha) touches, each moved 32 px in so joined stamps overlap. Null if it doesn't cross the stamp.
    /// </summary>
    internal static ((float X, float Y) A, (float X, float Y) B)? RiverEnds(StampImage stamp)
    {
        lock (Ends)
            if (Ends.TryGetValue(stamp.Name, out var known)) return known;

        int s = stamp.Size, band = Math.Max(2, s / 64);
        // Border pixels as a position along the perimeter, so a run round a corner stays one stretch.
        var points = new List<(int T, int X, int Y)>();
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                if (x >= band && y >= band && x < s - band && y < s - band) continue;
                float m = stamp.HasWater ? stamp.SampleWater(x, y) : 0;
                if (m <= 0 && stamp.SampleAlpha(x, y) <= 0.02f) continue;
                int t = y < band ? x : x >= s - band ? s + y : y >= s - band ? 3 * s - x : 4 * s - y;
                points.Add((t, x, y));
            }
        ((float, float), (float, float))? result = null;
        if (points.Count > 0)
        {
            points.Sort((a, b) => a.T.CompareTo(b.T));
            var groups = new List<List<(int T, int X, int Y)>> { new() { points[0] } };
            for (int i = 1; i < points.Count; i++)
            {
                if (points[i].T - points[i - 1].T > 24) groups.Add([]);
                groups[^1].Add(points[i]);
            }
            // The perimeter wraps: a stretch over the start joins the last one.
            if (groups.Count > 1 && points[0].T + 4 * s - points[^1].T <= 24)
            {
                groups[0].AddRange(groups[^1]);
                groups.RemoveAt(groups.Count - 1);
            }
            var two = groups.OrderByDescending(g => g.Count).Take(2).ToList();
            if (two.Count == 2)
            {
                (float, float) End(List<(int T, int X, int Y)> g)
                {
                    float cx = (float)g.Average(p => p.X), cy = (float)g.Average(p => p.Y);
                    float dx = s / 2f - cx, dy = s / 2f - cy, len = MathF.Max(1, MathF.Sqrt(dx * dx + dy * dy));
                    return (cx + dx / len * 32, cy + dy / len * 32);
                }
                result = (End(two[0]), End(two[1]));
            }
        }
        lock (Ends) Ends[stamp.Name] = result;
        return result;
    }

    /// <summary>How far, in metres, a long river may climb above the lowest ground it has reached.</summary>
    private const float ClimbAllowed = 6;

    /// <summary>Stamp pixel to world metres for a stamp of <paramref name="size"/> metres centred at (x, z), turned clockwise.</summary>
    private static (float X, float Z) ToWorld(StampImage stamp, float x, float z, float size, float rotation, (float X, float Y) px)
    {
        float scale = size / stamp.Size;
        float angle = -rotation * MathF.PI / 180f;
        float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
        float u = (px.X - stamp.Size / 2f) * scale, v = (px.Y - stamp.Size / 2f) * scale;
        return (x + u * cos - v * sin, z + u * sin + v * cos);
    }

    private static float GroundAt(World world, float x, float z)
    {
        int ix = (int)Math.Clamp(x, 0, world.Size - 1), iz = (int)Math.Clamp(z, 0, world.Size - 1);
        float sum = 0;
        int n = 0;
        for (int dz = -30; dz <= 30; dz += 30)
            for (int dx = -30; dx <= 30; dx += 30)
            {
                sum += world.Height[Math.Clamp(ix + dx, 0, world.Size - 1), Math.Clamp(iz + dz, 0, world.Size - 1)] / 257f;
                n++;
            }
        return sum / n;
    }

    private static bool IsWet(World world, float x, float z)
    {
        if (world.Water is not { } water) return false;
        int ix = (int)Math.Clamp(x, 0, world.Size - 1), iz = (int)Math.Clamp(z, 0, world.Size - 1);
        byte w = water[ix, iz];
        return w > 0 && world.Height[ix, iz] / 257f - 0.5f <= w;
    }

    /// <summary>
    /// A long river: river stamps joined end to end, each turned to run as far downhill as it can (and never turning
    /// back more than 75°), until one reaches water or the map's edge row, it can't go down any more, or it has
    /// <see cref="FeatureOptions.RiverSegments"/> stamps. One that ends dry ends in a lake. The water steps down with
    /// the ground, each stamp's at most as high as the one above it. Null if not even one stamp fits.
    /// </summary>
    private static List<Feature>? PlaceRiver(World world, FeatureCells cells, StampLibrary stamps, IReadOnlyList<string> names, FeatureRow row,
        FeatureOptions options, float baseHeight, Random rand, int chain, (float X, float Z)? from)
    {
        float offset = baseHeight - RwgWater;
        // Rivers start on high ground: random free cells from the highest quarter.
        List<(int, int)> starts;
        if (from is { } f0) starts = [((int)(f0.X / Cell), (int)(f0.Z / Cell))];
        else
        {
            var free = Shuffled(cells, rand, false);
            var heights = free.Select(c => GroundAt(world, c.Item1 * Cell + Cell / 2f, c.Item2 * Cell + Cell / 2f)).ToList();
            float high = heights.Count == 0 ? 0 : heights.Order().ElementAt(heights.Count * 3 / 4);
            starts = [.. free.Where((c, i) => heights[i] >= high).Take(40)];
        }
        foreach (var (scx, scz) in starts)
        {
            var own = new HashSet<int>();
            var segments = new List<Feature>();
            float px = from?.X ?? scx * Cell + Cell / 2f, pz = from?.Z ?? scz * Cell + Cell / 2f;
            float? heading = null;
            byte level = Level(GroundAt(world, px, pz) - offset);
            float lowest = GroundAt(world, px, pz);
            bool reached = false;

            for (int k = 0; k < Math.Max(1, options.RiverSegments) && !reached; k++)
            {
                var stamp = stamps.Get(names[rand.Next(names.Count)]);
                if (RiverEnds(stamp) is not { } ends) break;
                float scale = RandomScale(row, rand), size = stamp.Size * scale;
                float hereGround = GroundAt(world, px, pz);

                (Feature F, float Dx, float Dz, float Score, List<int[]> Cells)? best = null;
                int turn0 = rand.Next(24);
                for (int flip = 0; flip < 2; flip++)
                {
                    var up = flip == 0 ? ends.A : ends.B;
                    var down = flip == 0 ? ends.B : ends.A;
                    for (int t = 0; t < 24; t++)
                    {
                        float rotation = (turn0 + t) * 15 % 360;
                        // Where the centre goes so the upstream end lands on the current point.
                        var u = ToWorld(stamp, 0, 0, size, rotation, up);
                        float cx = px - u.X, cz = pz - u.Z;
                        var d = ToWorld(stamp, cx, cz, size, rotation, down);
                        float dirX = d.X - px, dirZ = d.Z - pz;
                        float dir = MathF.Atan2(dirZ, dirX) * 180 / MathF.PI;
                        if (heading is { } hd && MathF.Abs(((dir - hd) % 360 + 540) % 360 - 180) > 75) continue;
                        if (d.X < 0 || d.Z < 0 || d.X >= world.Size || d.Z >= world.Size) continue;
                        float ground = GroundAt(world, d.X, d.Z);
                        // Stamp terrain is bumpy at this scale, so a river may climb a little; its bed is cut deep
                        // enough to hold the water, which only steps down.
                        if (ground > lowest + ClimbAllowed) continue;
                        var f = Make(FeatureKind.River, stamp, cx, cz, scale, rotation, baseHeight);
                        if (best is { } b && ground >= b.Score) continue;
                        // A long river needs only the cells its stamp really covers, not its whole square: it
                        // winds between towns.
                        if (cells.Free(ShapeCells(world.Size, f, stamp), own) is not { } covered) continue;
                        best = (f, d.X, d.Z, ground, covered);
                    }
                }
                if (best is not { } pick) break;

                cells.Take(pick.Cells, own);
                pick.F.Cells = pick.Cells;
                pick.F.Chain = chain;
                level = Math.Min(level, Level(Math.Min(hereGround, pick.Score) - offset));
                lowest = Math.Min(lowest, pick.Score);
                pick.F.WaterLevel = level;
                segments.Add(pick.F);
                heading = MathF.Atan2(pick.Dz - pz, pick.Dx - px) * 180 / MathF.PI;
                px = pick.Dx;
                pz = pick.Dz;
                int ecx = (int)(px / Cell), ecz = (int)(pz / Cell);
                reached = IsWet(world, px, pz) || ecx <= 0 || ecz <= 0 || ecx >= cells.Width - 1 || ecz >= cells.Width - 1;
            }
            if (segments.Count == 0) continue;

            // Ends dry: the river runs into a lake.
            if (!reached && stamps.WithPrefix(StampPrefix(FeatureKind.Lake)) is { Count: > 0 } lakes)
            {
                var stamp = stamps.Get(lakes[rand.Next(lakes.Count)]);
                var lakeRow = new FeatureRow { ScaleMin = row.ScaleMin, ScaleMax = row.ScaleMax };
                // Centred past the mouth along the river's heading, so the two overlap little and don't dig twice as deep.
                float lakeScale = RandomScale(lakeRow, rand) * 0.7f;
                float ahead = stamp.Size * lakeScale * 0.35f, hr = (heading ?? 0) * MathF.PI / 180;
                float lx = px + MathF.Cos(hr) * ahead, lz = pz + MathF.Sin(hr) * ahead;
                var lake = Make(FeatureKind.Lake, stamp, lx, lz, lakeScale, rand.Next(360), baseHeight);
                if (cells.Free(ShapeCells(world.Size, lake, stamp), own) is { } covered)
                {
                    cells.Take(covered, own);
                    lake.Cells = covered;
                    lake.Chain = chain;
                    lake.WaterLevel = Math.Min(level, Level(RimGround(world, lake, stamp) - offset));
                    segments.Add(lake);
                }
            }
            return segments;
        }
        return null;
    }
}
