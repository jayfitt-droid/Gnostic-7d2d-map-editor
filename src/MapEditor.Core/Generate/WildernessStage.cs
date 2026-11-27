using System.Numerics;
using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Generate;

/// <summary>The wilderness stage's settings.</summary>
public sealed class WildernessOptions
{
    public int Seed { get; set; } = 1;

    /// <summary>rwgmixer's count to start from: 0 Few, 1 Default, 2 Many.</summary>
    public int Amount { get; set; } = 1;

    /// <summary>Multiplies rwgmixer's count (1 is RWG's).</summary>
    public float CountScale { get; set; } = 1;

    /// <summary>Pushes the mix toward easier (-1) or harder (+1) POIs; 0 is RWG's score.</summary>
    public float TierBias { get; set; }

    /// <summary>How far, in metres, a POI keeps from towns and roads.</summary>
    public int Gap { get; set; } = 20;

    /// <summary>Lay a gravel road from each POI with a road exit to the nearest road.</summary>
    public bool Roads { get; set; } = true;

    /// <summary>The longest gravel road, in metres; a POI farther from a road than this gets none.</summary>
    public int MaxRoad { get; set; } = 1000;

    public WildernessOptions Clone() => (WildernessOptions)MemberwiseClone();
}

/// <summary>A prefab the stage put in the world, to find it again when taking it out.</summary>
public sealed class PlacedPrefab
{
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int Rotation { get; set; }

    public bool Matches(PrefabInstance p) =>
        p.Position.X == X && p.Position.Y == Y && p.Position.Z == Z && (p.Rotation & 3) == (Rotation & 3)
        && p.Name.Equals(Name, StringComparison.OrdinalIgnoreCase);

    public static PlacedPrefab Of(PrefabInstance p) => new() { Name = p.Name, X = p.Position.X, Y = p.Position.Y, Z = p.Position.Z, Rotation = p.Rotation };

    public PlacedPrefab Clone() => (PlacedPrefab)MemberwiseClone();
}

/// <summary>One wilderness POI the stage placed (with what its markers spawned) and its gravel road.</summary>
public sealed class WildernessPoi
{
    public string Name { get; set; } = "";

    /// <summary>The prefab's corner in world coords (centre of the map is 0, 0) and its rotation.</summary>
    public int X { get; set; }
    public int Z { get; set; }
    public int Rotation { get; set; }

    /// <summary>Seeds its marker prefabs and road exit, so a pinned POI comes back the same.</summary>
    public int Seed { get; set; }

    /// <summary>Placed by hand: re-running the stage keeps it.</summary>
    public bool Pinned { get; set; }

    /// <summary>Whether it is in the world now (an earlier stage's run takes it out; pinned ones come back on the next run).</summary>
    public bool Drawn { get; set; }

    /// <summary>The POI and the prefabs its markers spawned, as placed.</summary>
    public List<PlacedPrefab> Prefabs { get; set; } = [];

    /// <summary>The ground its flattening changed (see <see cref="HighwayStage.Capture(World, IEnumerable{GridRect}, Action)"/>).</summary>
    public byte[] Ground { get; set; } = [];

    /// <summary>When it was drawn, among the stage's POIs and roads; taken out newest first.</summary>
    public int Order { get; set; }

    /// <summary>Its gravel road, or null.</summary>
    public HighwayRoad? Road { get; set; }
    public int RoadOrder { get; set; }

    public WildernessPoi Clone() => new()
    {
        Name = Name, X = X, Z = Z, Rotation = Rotation, Seed = Seed, Pinned = Pinned, Drawn = Drawn,
        Prefabs = [.. Prefabs.Select(p => p.Clone())], Ground = Ground, Order = Order, Road = Road?.Clone(), RoadOrder = RoadOrder,
    };
}

/// <summary>What a wilderness run did.</summary>
public sealed record WildernessResult(List<WildernessPoi> Added, List<string> Notes, int Removed)
{
    public int Roads => Added.Count(p => p.Road is not null);
    public float RoadKilometres => Added.Where(p => p.Road is not null).Sum(p => p.Road!.Length) / 1000;
    public int Prefabs => Added.Sum(p => p.Prefabs.Count);
}

/// <summary>
/// RWG's wilderness POIs (WildernessPlanner.Plan, StreetTile.spawnWildernessPrefab) and their gravel roads
/// (WildernessPathPlanner.Plan). Per biome, rwgmixer's count of free 150 m cells are picked, favouring those far from
/// towns; each gets the best scoring wilderness prefab (PrefabManager.GetWildernessPrefab), tried at 6 random spots and
/// turns, on dry ground of one biome that varies by no more than 11 m. It sits on the median ground, flattened and
/// blended over 18 m; its POI and part markers spawn more prefabs. A gravel road runs from one of its road exits to
/// the nearest road. Extras: a count multiplier, a tier slider, a gap from towns and roads, a longest-road limit,
/// and POIs placed by hand that re-runs keep. Runs after highways, so cells a highway crosses are skipped, as RWG does.
/// </summary>
public static class WildernessStage
{
    public const int Cell = TownSiteStage.Cell;

    /// <summary>RWG's biome order (BiomeType 0 to 4) as biome tags.</summary>
    public static readonly string[] Biomes = TownSiteStage.BiomeTags;

    /// <summary>RWG's count when rwgmixer has none for the world's size.</summary>
    private const int FallbackCount = 200;

    /// <summary>How far, in metres, a gravel road runs straight out of a POI before it may turn.</summary>
    private const float Lead = 15;

    /// <summary>rwgmixer's Few, Default and Many counts for a biome: its own class (forest_wilderness), else wilderness.</summary>
    public static (int Few, int Default, int Many) Counts(RwgMixer mixer, string biome)
    {
        var (_, own) = mixer.FeatureRule(biome + "_wilderness");
        if (own != (0, 0, 0)) return own;
        var (_, shared) = mixer.FeatureRule("wilderness");
        return shared != (0, 0, 0) ? shared : (FallbackCount, FallbackCount, FallbackCount);
    }

    /// <summary>How many POIs the options ask for in a biome.</summary>
    public static int CountFor(RwgMixer mixer, WildernessOptions options, string biome)
    {
        var c = Counts(mixer, biome);
        int n = options.Amount switch { 0 => c.Few, 2 => c.Many, _ => c.Default };
        return Math.Max(0, (int)MathF.Round(n * Math.Max(0, options.CountScale)));
    }

    // ---- choosing prefabs ----

    /// <summary>PrefabManager.GetWildernessPrefab and getScoreForPrefab over the game's wilderness prefabs.</summary>
    internal sealed class Picker
    {
        private sealed record Candidate(PrefabInfo Info, HashSet<string> Tags, HashSet<string> Themes, SpawnAdjust? Rule, bool RuleByName);

        private readonly World _world;
        private readonly PrefabLibrary _library;
        private readonly float _tierWeight;
        private readonly List<Candidate> _candidates;
        private readonly Dictionary<string, int> _used = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<Vector2>> _byName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<Vector2>> _byTheme = new(StringComparer.OrdinalIgnoreCase);

        public Picker(World world, PrefabLibrary library, RwgMixer mixer, WildernessOptions options)
        {
            _world = world;
            _library = library;
            // RWG adds tier / 5; the slider turns that from favouring easy POIs (-1) to strongly favouring hard ones (+1).
            _tierWeight = 0.2f + 0.6f * Math.Clamp(options.TierBias, -1, 1);
            string[] skip = ["streettile", "part", "navonly", "devonly", "testonly", "biomeonly"];
            var list = library.All
                .Where(p => p.Tags.Count > 0 && !p.IsStreetTile && !p.IsPart && !p.IsInternal && !p.IsTrader && !skip.Any(p.HasTag) && p.HasTag("wilderness"))
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(p =>
                {
                    var tags = new HashSet<string>(p.Tags, StringComparer.OrdinalIgnoreCase);
                    var themes = new HashSet<string>(p.ThemeTags, StringComparer.OrdinalIgnoreCase);
                    var (rule, byName) = FirstRule(mixer, p, tags, themes);
                    return new Candidate(p, tags, themes, rule, byName);
                })
                .ToList();
            // RWG shuffles its prefab list with the seed, so ties go a different way each world.
            var rand = new Random(options.Seed ^ 0x2545f491);
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rand.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            _candidates = list;
            foreach (var p in world.Prefabs) Register(p);
        }

        public int Count => _candidates.Count;

        private static (SpawnAdjust?, bool) FirstRule(RwgMixer mixer, PrefabInfo p, HashSet<string> tags, HashSet<string> themes)
        {
            foreach (var rule in mixer.SpawnAdjusts)
            {
                bool byName = rule.PartialName.Length > 0 && p.Name.Contains(rule.PartialName, StringComparison.OrdinalIgnoreCase);
                if (byName || rule.Tags.Any(tags.Contains) || rule.Tags.Any(themes.Contains)) return (rule, byName);
            }
            return (null, false);
        }

        /// <summary>The best scoring prefab for a spot: matching the marker's tags, fitting the size, far enough from its repeats. Null if none.</summary>
        public PrefabInfo? Best(string[] markerTags, int minX, int minZ, int maxX, int maxZ, Vector2 centre, string? biome)
        {
            return Pick(markerTags, minX, minZ, maxX, maxZ, centre, biome, true) ?? Pick(markerTags, minX, minZ, maxX, maxZ, centre, biome, false);
        }

        private PrefabInfo? Pick(string[] markerTags, int minX, int minZ, int maxX, int maxZ, Vector2 centre, string? biome, bool nameCheck)
        {
            Candidate? best = null;
            float bestScore = float.MinValue;
            foreach (var c in _candidates)
            {
                if (markerTags.Length > 0 && !markerTags.Any(c.Tags.Contains) && !markerTags.Any(c.Themes.Contains)) continue;
                if (!SizeValid(c.Info, minX, minZ, maxX, maxZ)) continue;
                if (!ThemeValid(c, centre, c.Info.ThemeRepeatDistance)) continue;
                if (nameCheck && !NameValid(c.Info, centre, c.Info.DuplicateRepeatDistance)) continue;
                float score = Score(c, biome);
                if (score > bestScore) (best, bestScore) = (c, score);
            }
            return best?.Info;
        }

        private static bool SizeValid(PrefabInfo p, int minX, int minZ, int maxX, int maxZ)
        {
            int sx = p.Size.X, sz = p.Size.Z;
            if ((maxX | maxZ) != 0 && (sx > maxX || sz > maxZ) && (sz > maxX || sx > maxZ)) return false;
            if ((minX | minZ) == 0 || sx >= minX && sz >= minZ) return true;
            return sz >= minX && sx >= minZ;
        }

        private bool ThemeValid(Candidate c, Vector2 centre, int distance)
        {
            float limit = (float)distance * distance;
            foreach (var theme in c.Themes)
                if (_byTheme.TryGetValue(theme, out var list) && list.Any(p => Vector2.DistanceSquared(p, centre) < limit))
                    return false;
            return true;
        }

        private bool NameValid(PrefabInfo p, Vector2 centre, int distance)
        {
            float limit = (float)distance * distance;
            return !_byName.TryGetValue(p.Name, out var list) || !list.Any(c => Vector2.DistanceSquared(c, centre) < limit);
        }

        private float Score(Candidate c, string? biome)
        {
            float bias = 1, weight = 1;
            int used = _used.GetValueOrDefault(c.Info.Name);
            if (c.Rule is { } rule)
            {
                if (c.RuleByName && rule.BiomeTags.Count > 0 && (biome is null || !rule.BiomeTags.Contains(biome, StringComparer.OrdinalIgnoreCase)))
                    return float.MinValue;
                weight = rule.Weight;
                bias += rule.Bias;
                if (used < rule.MinCount) bias += rule.MinCount - used;
                if (used > 0 && used >= rule.MaxCount) weight = 0;
            }
            float score = bias + c.Info.DifficultyTier * _tierWeight;
            if (used > 0) score /= used + 1f;
            return score * weight;
        }

        /// <summary>Notes a prefab for the repeat and theme checks of later picks.</summary>
        public void Register(PrefabInstance p)
        {
            _used[p.Name] = _used.GetValueOrDefault(p.Name) + 1;
            if (_library.Find(p.Name) is not { } info) return;
            var (sx, sz) = info.Footprint(p.Rotation);
            var centre = new Vector2(_world.WorldToGrid(p.Position.X) + sx / 2, _world.WorldToGrid(p.Position.Z) + sz / 2);
            if (!_byName.TryGetValue(p.Name, out var names)) _byName[p.Name] = names = [];
            names.Add(centre);
            foreach (var theme in info.ThemeTags)
            {
                if (!_byTheme.TryGetValue(theme, out var list)) _byTheme[theme] = list = [];
                list.Add(centre);
            }
        }
    }

    // ---- the cells ----

    /// <summary>The 150 m cells: which may take a POI, each one's biome, and how far it is from the nearest town.</summary>
    internal sealed class Cells
    {
        public int Width { get; }
        public bool[] Valid { get; }
        public bool[] Used { get; }
        public string?[] Biome { get; }
        public long[] TownDistance { get; }

        public Cells(World world, PrefabLibrary library, BiomeTable biomes, IEnumerable<TownSite> sites, IEnumerable<Vector2> towns)
        {
            int w = Width = world.Size / Cell;
            Valid = new bool[w * w];
            Used = new bool[w * w];
            Biome = new string?[w * w];
            TownDistance = new long[w * w];
            var townCells = new bool[w * w];
            foreach (var s in sites)
                foreach (var (cx, cz) in s.AllCells)
                    if ((uint)cx < (uint)w && (uint)cz < (uint)w) townCells[cx + cz * w] = true;
            foreach (var p in world.Prefabs)
            {
                if (library.Find(p.Name) is not { IsPart: false } info) continue;
                var f = PrefabPlacer.Footprint(world, p, library);
                for (int cz = Math.Max(0, f.Z0 / Cell); cz <= Math.Min(w - 1, (f.Z1 - 1) / Cell); cz++)
                    for (int cx = Math.Max(0, f.X0 / Cell); cx <= Math.Min(w - 1, (f.X1 - 1) / Cell); cx++)
                    {
                        Used[cx + cz * w] = true;
                        if (info.IsStreetTile) townCells[cx + cz * w] = true;
                    }
            }
            var centres = towns.ToList();
            Parallel.For(0, w, cz =>
            {
                for (int cx = 0; cx < w; cx++)
                {
                    int i = cx + cz * w, x0 = cx * Cell, z0 = cz * Cell, mx = x0 + 75, mz = z0 + 75;
                    Biome[i] = TownGenerator.BiomeTagAt(world, biomes, mx, mz);
                    long best = long.MaxValue;
                    foreach (var c in centres) best = Math.Min(best, (long)Vector2.DistanceSquared(c, new Vector2(mx, mz)));
                    TownDistance[i] = best;
                    // RWG: the edge row counts as radiation; town districts, wet cells and highways are out.
                    if (cx < 1 || cz < 1 || cx >= w - 1 || cz >= w - 1 || townCells[i]) continue;
                    if (Radiated(world, mx, mz) || MostlyWater(world, x0, z0) || HasHighway(world, x0, z0)) continue;
                    Valid[i] = true;
                }
            });
        }

        private static bool Radiated(World world, int mx, int mz)
        {
            if (world.Radiation is not { } rad) return false;
            int rs = Math.Max(1, world.Size / rad.Width);
            for (int dz = -75; dz <= 75; dz += 75)
                for (int dx = -75; dx <= 75; dx += 75)
                {
                    int x = Math.Clamp(mx + dx, 0, world.Size - 1) / rs, z = Math.Clamp(mz + dz, 0, world.Size - 1) / rs;
                    if (rad.Contains(x, z) && rad[x, z] > 0) return true;
                }
            return false;
        }

        private static bool MostlyWater(World world, int x0, int z0)
        {
            if (world.Water is not { } water) return false;
            int n = 0, wet = 0;
            for (int z = z0; z < z0 + Cell; z += 3)
                for (int x = x0; x < x0 + Cell; x += 3)
                {
                    n++;
                    if (water[x, z] > 0) wet++;
                }
            return wet > n * 0.9f;
        }

        private static bool HasHighway(World world, int x0, int z0)
        {
            if (world.Roads is not { } roads) return false;
            for (int z = z0; z < z0 + Cell; z += 2)
            {
                var row = roads.Row(z);
                for (int x = x0; x < x0 + Cell; x += 2)
                    if (row[x] == (byte)RoadType.Asphalt) return true;
            }
            return false;
        }
    }

    // ---- running ----

    private sealed class Context
    {
        public required World World { get; init; }
        public required PrefabLibrary Library { get; init; }
        public required BiomeTable Biomes { get; init; }
        public required WildernessOptions Options { get; init; }
        public required Picker Picker { get; init; }
        public required EditSession Session { get; init; }

        /// <summary>Footprints of the prefabs in the world (grid metres), street tiles flagged, with the ground a road meets them at.</summary>
        public List<(GridRect Rect, bool Town, int Ground)> Occupied { get; } = [];

        /// <summary>Areas (grid metres) kept free though nothing stands there now: see <see cref="Run"/>'s keepClear.</summary>
        public IReadOnlyList<GridRect> KeepClear { get; init; } = [];
        public int NextOrder { get; set; }
    }

    private static Context MakeContext(World world, PrefabLibrary library, RwgMixer mixer, BiomeTable biomes, WildernessOptions options,
        List<WildernessPoi> pois, EditSession session, IReadOnlyList<GridRect>? keepClear = null)
    {
        var ctx = new Context
        {
            World = world, Library = library, Biomes = biomes, Options = options, Session = session,
            Picker = new Picker(world, library, mixer, options), KeepClear = keepClear ?? [],
        };
        foreach (var p in world.Prefabs)
            if (library.Find(p.Name) is { IsPart: false } info)
                ctx.Occupied.Add((PrefabPlacer.Footprint(world, p, library), info.IsStreetTile, info.RoadLevelAbove(p.Position.Y)));
        ctx.NextOrder = pois.Count == 0 ? 1 : pois.Max(p => Math.Max(p.Order, p.RoadOrder)) + 1;
        return ctx;
    }

    /// <summary>
    /// Takes out the POIs and roads the last run placed (pinned ones stay), then places new ones and lays their roads as
    /// part of <paramref name="session"/>. <paramref name="pois"/> is updated in place, and is part of the undo step.
    /// </summary>
    /// <param name="keepClear">
    /// Areas (grid metres) no new POI (with its <see cref="WildernessOptions.Gap"/>) or gravel road may cross, though nothing
    /// stands there now: the Bridge tool's bridges and their ramps (<see cref="BridgeTool.KeepClearArea"/>), which are taken
    /// down while the stage changes the ground and built again after it (#31).
    /// </param>
    public static WildernessResult Run(World world, WildernessOptions options, List<WildernessPoi> pois, PrefabLibrary library, RwgMixer mixer,
        BiomeTable biomes, IEnumerable<TownSite> sites, EditSession session, IProgress<double>? progress = null, CancellationToken cancel = default,
        IReadOnlyList<GridRect>? keepClear = null)
    {
        var all = new GridRect(0, 0, world.Size, world.Size);
        session.TouchList(pois, p => p.Clone(), all);
        session.TouchList(world.Prefabs, p => p.Clone(), all);
        int removed = pois.Count(p => !p.Pinned && p.Drawn);
        TakeDown(world, pois, session);
        var notes = new List<string>();
        var ctx = MakeContext(world, library, mixer, biomes, options, pois, session, keepClear);
        if (ctx.Picker.Count == 0)
        {
            notes.Add("The game folder has no wilderness prefabs.");
            return new WildernessResult([], notes, removed);
        }

        // Pinned POIs an earlier stage took out come back first, on the ground as it is now.
        foreach (var p in pois.Where(p => !p.Drawn)) Redraw(ctx, p);

        var siteList = sites.ToList();
        var towns = HighwayStage.ReadTowns(world, library, mixer, siteList).Select(t => t.Centre).ToList();
        var cells = new Cells(world, library, biomes, siteList, towns);
        // Cells holding a POI already (pinned ones just drawn) are used.
        foreach (var p in pois) MarkUsed(world, library, cells, p);

        var added = new List<WildernessPoi>();
        var rand = new Random(unchecked(options.Seed + 409651));
        int wanted = Biomes.Sum(b => CountFor(mixer, options, b));
        for (int bi = 0; bi < Biomes.Length; bi++)
        {
            string biome = Biomes[bi];
            int count = CountFor(mixer, options, biome);
            if (count == 0) continue;
            // Farthest from a town first; RWG's low-biased pick favours the start of the list.
            var list = Enumerable.Range(0, cells.Width * cells.Width)
                .Where(i => cells.Valid[i] && cells.Biome[i] == biome)
                .OrderByDescending(i => cells.TownDistance[i]).ThenBy(i => i)
                .ToList();
            int placed = 0;
            for (int n = 0; n < count; n++)
            {
                cancel.ThrowIfCancellationRequested();
                list.RemoveAll(i => cells.Used[i]);
                if (list.Count == 0) break;
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    double r = rand.NextDouble();
                    int i = list[(int)(r * r * list.Count)];
                    if (cells.Used[i]) continue;
                    if (SpawnInCell(ctx, cells, i, biome) is not { } poi) continue;
                    added.Add(poi);
                    pois.Add(poi);
                    placed++;
                    break;
                }
                progress?.Report((options.Roads ? 0.5 : 1.0) * (added.Count) / Math.Max(1, wanted));
            }
            if (placed < count) notes.Add($"Placed {placed} of {count} in {BiomeName(biome)}: no more room.");
        }

        if (options.Roads)
        {
            var needRoad = pois.Where(p => p.Drawn && p.Road is null).ToList();
            LayRoads(ctx, needRoad, notes, progress, cancel);
        }
        return new WildernessResult(added, notes, removed);
    }

    public static string BiomeName(string biome) => biome switch
    {
        "forest" => "the forest",
        "burntforest" => "the burnt forest",
        "desert" => "the desert",
        "snow" => "the snow",
        "wasteland" => "the wasteland",
        _ => biome,
    };

    private static void MarkUsed(World world, PrefabLibrary library, Cells cells, WildernessPoi p)
    {
        if (library.Find(p.Name) is not { } info) return;
        var f = PrefabPlacer.Footprint(world, info, p.X, p.Z, p.Rotation);
        int w = cells.Width;
        for (int cz = Math.Max(0, f.Z0 / Cell); cz <= Math.Min(w - 1, (f.Z1 - 1) / Cell); cz++)
            for (int cx = Math.Max(0, f.X0 / Cell); cx <= Math.Min(w - 1, (f.X1 - 1) / Cell); cx++)
                cells.Used[cx + cz * w] = true;
    }

    /// <summary>StreetTile.spawnWildernessPrefab: the cell's best prefab at up to 6 random spots and turns. Null if none fits.</summary>
    private static WildernessPoi? SpawnInCell(Context ctx, Cells cells, int index, string biome)
    {
        var world = ctx.World;
        int cx = index % cells.Width, cz = index / cells.Width;
        int x0 = cx * Cell, z0 = cz * Cell;
        var centre = new Vector2(x0 + 75, z0 + 75);
        var info = ctx.Picker.Best([], 0, 0, 0, 0, centre, biome);
        if (info is null) return null;
        int seed = Hash(ctx.Options.Seed, cx, cz, 4096953);
        var rand = new Random(seed);
        for (int attempt = 0; attempt < 6; attempt++)
        {
            int rotation = (info.RotationToFaceNorth + rand.Next(4)) & 3;
            var (sx, sz) = info.Footprint(rotation);
            int gx, gz;
            if (sx >= 110 || sz >= 110)
            {
                gx = x0 + 75 - sx / 2;
                gz = z0 + 75 - sz / 2;
            }
            else
            {
                gx = x0 + 20 + rand.Next(110 - sx);
                gz = z0 + 20 + rand.Next(110 - sz);
            }
            if (gx < 0 || gz < 0 || gx + sx > world.Size || gz + sz > world.Size) continue;
            var rect = new GridRect(gx, gz, gx + sx, gz + sz);
            if (!Fits(ctx, rect, out int median) || median + info.YOffset < 2) continue;
            var poi = new WildernessPoi { Name = info.Name, X = world.GridToWorld(gx), Z = world.GridToWorld(gz), Rotation = rotation, Seed = seed };
            Draw(ctx, poi, info);
            MarkUsed(world, ctx.Library, cells, poi);
            return poi;
        }
        return null;
    }

    /// <summary>
    /// RWG's test of a footprint: dry, one biome, every cell within 11 m of the centre; with its median ground. The
    /// editor's: clear of other prefabs, <see cref="WildernessOptions.Gap"/> from towns and roads, and further from a
    /// town on other ground (<see cref="TownRoom"/>).
    /// </summary>
    private static bool Fits(Context ctx, GridRect rect, out int median)
    {
        median = 0;
        var world = ctx.World;
        int gap = Math.Max(0, ctx.Options.Gap);
        foreach (var (o, town, _) in ctx.Occupied)
        {
            int g = town ? gap : 0;
            if (o.X0 - g < rect.X1 && o.X1 + g > rect.X0 && o.Z0 - g < rect.Z1 && o.Z1 + g > rect.Z0) return false;
        }
        // A drawn bridge and its ramps are a road, so the POI keeps the gap from them too.
        foreach (var o in ctx.KeepClear)
            if (o.X0 - gap < rect.X1 && o.X1 + gap > rect.X0 && o.Z0 - gap < rect.Z1 && o.Z1 + gap > rect.Z0) return false;
        if (world.Roads is { } roads)
        {
            var near = new GridRect(rect.X0 - gap, rect.Z0 - gap, rect.X1 + gap, rect.Z1 + gap).Clamp(world.Size, world.Size);
            for (int z = near.Z0; z < near.Z1; z++)
            {
                var row = roads.Row(z);
                for (int x = near.X0; x < near.X1; x++)
                    if (row[x] != 0) return false;
            }
        }

        int mx = (rect.X0 + rect.X1) / 2, mz = (rect.Z0 + rect.Z1) / 2;
        int mid = (int)MathF.Ceiling(World.HeightToMetres(world.Height[mx, mz]));
        var b = world.Biomes;
        int bs = b is null ? 1 : Math.Max(1, world.Size / b.Indices.Width);
        byte biome = b is null ? (byte)0 : b.Indices[Math.Min(mx / bs, b.Indices.Width - 1), Math.Min(mz / bs, b.Indices.Height - 1)];
        var water = world.Water;
        for (int z = rect.Z0; z < rect.Z1; z++)
        {
            var h = world.Height.Row(z);
            var wr = water is null ? default : water.Row(z);
            for (int x = rect.X0; x < rect.X1; x++)
            {
                if (water is not null && wr[x] > 0) return false;
                int ceil = (int)MathF.Ceiling(World.HeightToMetres(h[x]));
                if (Math.Abs(ceil - mid) > PrefabPlacer.MaxGroundVariation) return false;
            }
            if (b is not null)
                for (int x = rect.X0; x < rect.X1; x += bs)
                    if (b.Indices[Math.Min(x / bs, b.Indices.Width - 1), Math.Min(z / bs, b.Indices.Height - 1)] != biome) return false;
        }
        median = PrefabPlacer.MedianGround(world, rect);
        // A town on higher or lower ground needs room for a road to climb between them and meet both level (#33).
        foreach (var (o, town, ground) in ctx.Occupied)
        {
            if (!town) continue;
            int g = Math.Max(gap, TownRoom(Math.Abs(ground - median)));
            if (o.X0 - g < rect.X1 && o.X1 + g > rect.X0 && o.Z0 - g < rect.Z1 && o.Z1 + g > rect.Z0) return false;
        }
        return true;
    }

    /// <summary>
    /// The room, in metres, a POI keeps from a town whose ground is <paramref name="rise"/> metres from its own: a road's
    /// climb at 1 in <see cref="TownClimbRun"/>, with <see cref="PrefabPlacer.RoadTouch"/> metres level at each end.
    /// </summary>
    internal static int TownRoom(int rise) => rise * TownClimbRun + 2 * PrefabPlacer.RoadTouch;

    /// <summary>The run, in metres, a road between a POI and a town takes to climb each metre: 1 in 4.</summary>
    private const int TownClimbRun = 4;

    /// <summary>Draws a POI at its spot: flattens its ground, adds it and its marker prefabs, records what it changed.</summary>
    private static void Draw(Context ctx, WildernessPoi poi, PrefabInfo info)
    {
        var world = ctx.World;
        var rect = PrefabPlacer.Footprint(world, info, poi.X, poi.Z, poi.Rotation);
        int ground = PrefabPlacer.MedianGround(world, rect);
        var spot = new PrefabSpot(poi.X, PrefabPlacer.PrefabY(info, ground), poi.Z, poi.Rotation);
        int blend = PrefabPlacer.PoiBlend;
        var area = new GridRect(rect.X0 - blend - 1, rect.Z0 - blend - 1, rect.X1 + blend + 1, rect.Z1 + blend + 1);
        poi.Ground = HighwayStage.Capture(world, [area],
            () => PrefabPlacer.Flatten(world, rect, ground, blend, ctx.Session, library: ctx.Library));
        poi.Order = ctx.NextOrder++;

        var made = new List<PrefabInstance> { new() { Name = info.Name, Position = spot.Position, Rotation = spot.Rotation } };
        ctx.Picker.Register(made[0]);
        SpawnMarkers(ctx, info, spot, poi.Seed, 0, made);
        ctx.Session.TouchList(world.Prefabs, p => p.Clone(), rect.Clamp(world.Size, world.Size));
        world.Prefabs.AddRange(made);
        poi.Prefabs = [.. made.Select(PlacedPrefab.Of)];
        ctx.Occupied.Add((rect, false, ground));
        poi.Drawn = true;
    }

    /// <summary>StreetTile.SpawnMarkerPartsAndPrefabsWilderness: every POI marker gets a wilderness prefab of its size, then the parts.</summary>
    private static void SpawnMarkers(Context ctx, PrefabInfo parent, PrefabSpot spot, int seed, int depth, List<PrefabInstance> made)
    {
        if (depth > 4) return;
        var world = ctx.World;
        foreach (var lot in parent.RotatedMarkers(spot.Rotation).Where(m => m.Type == MarkerType.POISpawn))
        {
            var tags = lot.Tags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var centre = new Vector2(world.WorldToGrid(spot.X) + lot.Start.X + lot.Size.X / 2, world.WorldToGrid(spot.Z) + lot.Start.Z + lot.Size.Z / 2);
            string? biome = TownGenerator.BiomeTagAt(world, ctx.Biomes, (int)centre.X, (int)centre.Y);
            if (ctx.Picker.Best(tags, lot.Size.X, lot.Size.Z, lot.Size.X, lot.Size.Z, centre, biome) is not { } pick) continue;
            var child = PrefabPlacer.LotSpot(spot, lot, pick);
            var instance = new PrefabInstance { Name = pick.Name, Position = child.Position, Rotation = child.Rotation };
            made.Add(instance);
            ctx.Picker.Register(instance);
            SpawnMarkers(ctx, pick, child, Hash(seed, child.X, child.Z, depth + 1), depth + 1, made);
        }
        foreach (var part in PrefabPlacer.MarkerParts(ctx.Library, parent, spot, Hash(seed, spot.X, spot.Z, depth + 9), "wilderness", shuffle: false))
        {
            made.Add(part);
            ctx.Picker.Register(part);
        }
    }

    /// <summary>Draws a pinned POI again after an earlier stage took it out.</summary>
    private static void Redraw(Context ctx, WildernessPoi poi)
    {
        if (ctx.Library.Find(poi.Name) is not { } info) return;
        poi.Road = null;
        Draw(ctx, poi, info);
    }

    // ---- by hand ----

    /// <summary>
    /// Places a POI by hand, pinned, centred on grid point (<paramref name="gx"/>, <paramref name="gz"/>): the given
    /// prefab, or the best scoring one there. Nothing is checked: hand placement may go anywhere. It gets a gravel road
    /// if roads are on. Null if there is no prefab to place.
    /// </summary>
    public static WildernessPoi? PlaceByHand(World world, PrefabInfo? info, float gx, float gz, int rotation, WildernessOptions options,
        List<WildernessPoi> pois, PrefabLibrary library, RwgMixer mixer, BiomeTable biomes, int seed, EditSession session, List<string>? notes = null)
    {
        var ctx = MakeContext(world, library, mixer, biomes, options, pois, session);
        info ??= ctx.Picker.Best([], 0, 0, 0, 0, new Vector2(gx, gz), TownGenerator.BiomeTagAt(world, biomes, (int)gx, (int)gz));
        if (info is null) return null;
        // Only once there is something to place, so a click that places nothing leaves no undo step (#22).
        session.TouchList(pois, p => p.Clone(), new GridRect(0, 0, world.Size, world.Size));
        rotation &= 3;
        var (sx, sz) = info.Footprint(rotation);
        int x0 = Math.Clamp((int)MathF.Round(gx - sx / 2f), 0, Math.Max(0, world.Size - sx));
        int z0 = Math.Clamp((int)MathF.Round(gz - sz / 2f), 0, Math.Max(0, world.Size - sz));
        var poi = new WildernessPoi { Name = info.Name, X = world.GridToWorld(x0), Z = world.GridToWorld(z0), Rotation = rotation, Seed = seed, Pinned = true };
        Draw(ctx, poi, info);
        pois.Add(poi);
        if (options.Roads) LayRoads(ctx, [poi], notes ?? [], null, default);
        return poi;
    }

    /// <summary>Takes POIs (with their prefabs and roads) out of the world and the list.</summary>
    public static int Remove(World world, List<WildernessPoi> pois, IEnumerable<WildernessPoi> which, EditSession session)
    {
        var gone = which.Where(pois.Contains).ToList();
        if (gone.Count == 0) return 0;
        var all = new GridRect(0, 0, world.Size, world.Size);
        session.TouchList(pois, p => p.Clone(), all);
        TakeOut(world, gone, session);
        pois.RemoveAll(gone.Contains);
        return gone.Count;
    }

    /// <summary>
    /// Takes every drawn POI and road out, newest first, putting back the ground each changed, for a re-run or an
    /// earlier stage's run. Pinned POIs stay in the list, not drawn; the rest are dropped. Returns how many came out.
    /// </summary>
    public static int TakeDown(World world, List<WildernessPoi> pois, EditSession session)
    {
        var drawn = pois.Where(p => p.Drawn).ToList();
        if (drawn.Count == 0 && pois.All(p => p.Pinned)) return 0;
        session.TouchList(pois, p => p.Clone(), new GridRect(0, 0, world.Size, world.Size));
        TakeOut(world, drawn, session);
        pois.RemoveAll(p => !p.Pinned);
        return drawn.Count;
    }

    private static void TakeOut(World world, List<WildernessPoi> which, EditSession session)
    {
        if (which.Count == 0) return;
        session.TouchList(world.Prefabs, p => p.Clone(), new GridRect(0, 0, world.Size, world.Size));
        var pieces = new List<(int Order, byte[] Ground)>();
        foreach (var p in which.Where(p => p.Drawn))
        {
            pieces.Add((p.Order, p.Ground));
            if (p.Road is { } r) pieces.Add((p.RoadOrder, r.Ground));
        }
        foreach (var (_, ground) in pieces.OrderByDescending(x => x.Order)) HighwayStage.Restore(world, ground, session);

        // Each placed prefab takes out one prefab that matches it exactly.
        var wanted = which.Where(p => p.Drawn).SelectMany(p => p.Prefabs).ToList();
        var gone = new HashSet<PrefabInstance>();
        foreach (var w in wanted)
            if (world.Prefabs.FirstOrDefault(p => !gone.Contains(p) && w.Matches(p)) is { } hit) gone.Add(hit);
        world.Prefabs.RemoveAll(gone.Contains);
        foreach (var p in which)
        {
            p.Drawn = false;
            p.Road = null;
            p.Ground = [];
            p.Prefabs = [];
        }
    }

    // ---- gravel roads ----

    /// <summary>A POI's road exit: the middle of one of its RoadExit markers (RWG picks one at random), and the road's half-width.</summary>
    internal static (Vector2 Point, float Radius, GridRect Footprint)? ExitOf(World world, PrefabLibrary library, WildernessPoi poi)
    {
        if (library.Find(poi.Name) is not { } info) return null;
        var exits = info.RotatedMarkers(poi.Rotation).Where(m => m.Type == MarkerType.RoadExit).ToList();
        if (exits.Count == 0) return null;
        var m = exits[new Random(Hash(poi.Seed, poi.X, poi.Z, 77)).Next(exits.Count)];
        var rect = PrefabPlacer.Footprint(world, info, poi.X, poi.Z, poi.Rotation);
        var point = new Vector2(rect.X0 + m.Start.X + m.Size.X / 2f, rect.Z0 + m.Start.Z + m.Size.Z / 2f);
        // RWG: half the exit's larger side (most are 3 m, a narrow track), at most its country road's.
        float radius = Math.Clamp(Math.Max(m.Size.X, m.Size.Z) * 0.5f, 1.5f, HighwayStage.CountryRadius);
        return (point, radius, rect);
    }

    /// <summary>Out of the footprint from the exit, straight across the nearest edge.</summary>
    private static Vector2 LeadFrom(Vector2 exit, GridRect f)
    {
        float west = exit.X - f.X0, east = f.X1 - exit.X, south = exit.Y - f.Z0, north = f.Z1 - exit.Y;
        float min = MathF.Min(MathF.Min(west, east), MathF.Min(south, north));
        if (min == west) return new(f.X0 - Lead, exit.Y);
        if (min == east) return new(f.X1 + Lead, exit.Y);
        if (min == south) return new(exit.X, f.Z0 - Lead);
        return new(exit.X, f.Z1 + Lead);
    }

    /// <summary>
    /// WildernessPathPlanner.Plan: wider roads first, then those nearest a road; each runs RWG's country-road A* to the
    /// nearest road (asphalt or gravel, the roads laid before it included), or with no roads at all to the nearest POI.
    /// Roads that would be longer than <see cref="WildernessOptions.MaxRoad"/> aren't laid.
    /// </summary>
    private static void LayRoads(Context ctx, List<WildernessPoi> pois, List<string> notes, IProgress<double>? progress, CancellationToken cancel)
    {
        var world = ctx.World;
        var grid = new HighwayStage.PathGrid(world, ctx.Library, new HighwayOptions { Seed = ctx.Options.Seed, Wiggle = 0.3f, CausewayMax = 0 });
        foreach (var area in ctx.KeepClear) grid.KeepClear(area);
        int w = grid.Width;
        var exits = new List<(WildernessPoi Poi, Vector2 Exit, Vector2 Lead, float Radius)>();
        int noExit = 0;
        foreach (var p in pois)
        {
            if (ExitOf(world, ctx.Library, p) is not { } e) { noExit++; continue; }
            exits.Add((p, e.Point, LeadFrom(e.Point, e.Footprint), e.Radius));
        }
        if (exits.Count == 0) return;

        bool anyRoad = grid.Flags.Any(f => (f & HighwayStage.PathGrid.Road) != 0);
        var nearest = RoadDistance(grid);
        exits = [.. exits
            .OrderByDescending(e => e.Radius < 2.4f ? e.Radius : 3f)
            .ThenBy(e => nearest[grid.CellOf(e.Lead)])];

        int tooFar = 0, noPath = 0;
        float maxRoad = Math.Max(50, ctx.Options.MaxRoad);
        for (int i = 0; i < exits.Count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            var (poi, exit, lead, radius) = exits[i];
            progress?.Report(0.5 + 0.5 * (i + 1) / exits.Count);
            int start = grid.CellOf(lead);
            List<int> goals;
            if (anyRoad)
            {
                // Recomputed as roads are laid, so later roads can end on earlier ones.
                // The spread is 4-way, so it can run up to √2 over the straight distance.
                if (nearest[start] * HighwayStage.Step > maxRoad * 1.42f) { tooFar++; continue; }
                goals = RoadGoals(grid, start, (int)(maxRoad / HighwayStage.Step));
            }
            else
            {
                goals = [.. exits.Where(o => o.Poi != poi && Vector2.Distance(o.Lead, lead) <= maxRoad)
                    .OrderBy(o => Vector2.DistanceSquared(o.Lead, lead)).Take(3).Select(o => grid.CellOf(o.Lead))];
            }
            if (goals.Count == 0) { tooFar++; continue; }
            if (HighwayStage.FindPath(grid, start, goals, country: true) is not { } cells) { noPath++; continue; }
            var end = grid.Centre(cells[^1]);
            var points = new List<Vector2> { exit, lead };
            points.AddRange(cells.Select(grid.Centre).Where(c => Vector2.Distance(c, lead) >= 20 && Vector2.Distance(c, end) >= 20));
            points.Add(end);
            float length = 0;
            for (int k = 1; k < points.Count; k++) length += Vector2.Distance(points[k - 1], points[k]);
            if (length > maxRoad * 1.25f) { tooFar++; continue; }

            HighwayStage.RoundOffCorners(points, true);
            var path = new RoadPath(points, RoadType.Gravel, radius);
            var road = new HighwayRoad { Kind = HighwayKind.Country, Type = RoadType.Gravel, Radius = radius, Points = [.. points.Select(v => new[] { v.X, v.Y })] };
            road.Ground = HighwayStage.Capture(world, path, () =>
            {
                RoadOp.Draw(world, path, ctx.Session, (wd, line) => HighwayStage.RwgProfile(wd, line, true, grid), ctx.Library);
                // A sharp turn on a slope, or this road over a highway, can leave a step across the road (#312).
                RoadSteps.Ease(world, RoadOp.Centreline(path.Points, RoadOp.CornerRounding(path.Type)), radius, ctx.Library, ctx.Session);
            });
            poi.Road = road;
            poi.RoadOrder = ctx.NextOrder++;
            grid.MarkLaidRoad(cells);
            anyRoad = true;
            UpdateDistance(grid, nearest, cells);
        }
        if (noExit > 0) notes.Add($"{noExit} POI{(noExit == 1 ? " has" : "s have")} no road exit, so no road.");
        if (tooFar > 0) notes.Add($"{tooFar} POI{(tooFar == 1 ? " is" : "s are")} more than {maxRoad:0} m from a road, so no road.");
        if (noPath > 0) notes.Add($"{noPath} POI{(noPath == 1 ? "" : "s")} found no way to a road.");
    }

    /// <summary>Each cell's distance, in cells, to the nearest road cell (a breadth-first spread, 4-way).</summary>
    private static int[] RoadDistance(HighwayStage.PathGrid grid)
    {
        int w = grid.Width;
        var dist = new int[w * w];
        Array.Fill(dist, int.MaxValue / 2);
        var queue = new Queue<int>();
        for (int i = 0; i < dist.Length; i++)
            if ((grid.Flags[i] & HighwayStage.PathGrid.Road) != 0) { dist[i] = 0; queue.Enqueue(i); }
        Spread(w, dist, queue);
        return dist;
    }

    private static void UpdateDistance(HighwayStage.PathGrid grid, int[] dist, List<int> cells)
    {
        var queue = new Queue<int>();
        foreach (int c in cells) { dist[c] = 0; queue.Enqueue(c); }
        Spread(grid.Width, dist, queue);
    }

    private static void Spread(int w, int[] dist, Queue<int> queue)
    {
        while (queue.TryDequeue(out int i))
        {
            int cx = i % w, cz = i / w, d = dist[i] + 1;
            if (cx > 0 && dist[i - 1] > d) { dist[i - 1] = d; queue.Enqueue(i - 1); }
            if (cx < w - 1 && dist[i + 1] > d) { dist[i + 1] = d; queue.Enqueue(i + 1); }
            if (cz > 0 && dist[i - w] > d) { dist[i - w] = d; queue.Enqueue(i - w); }
            if (cz < w - 1 && dist[i + w] > d) { dist[i + w] = d; queue.Enqueue(i + w); }
        }
    }

    /// <summary>The nearest road cells to <paramref name="start"/>, a few spread out, as the A*'s goals.</summary>
    private static List<int> RoadGoals(HighwayStage.PathGrid grid, int start, int reach)
    {
        int w = grid.Width, sx = start % w, sz = start / w;
        var found = new List<(int Cell, int D2)>();
        for (int z = Math.Max(0, sz - reach); z <= Math.Min(w - 1, sz + reach); z++)
            for (int x = Math.Max(0, sx - reach); x <= Math.Min(w - 1, sx + reach); x++)
            {
                int c = x + z * w;
                if ((grid.Flags[c] & HighwayStage.PathGrid.Road) == 0
                    || (grid.Flags[c] & (HighwayStage.PathGrid.Blocked | HighwayStage.PathGrid.NoJoin)) != 0) continue;
                int d2 = (x - sx) * (x - sx) + (z - sz) * (z - sz);
                if (d2 <= reach * reach) found.Add((c, d2));
            }
        var goals = new List<int>();
        foreach (var (c, _) in found.OrderBy(f => f.D2))
        {
            if (goals.Any(g => Math.Abs(g % w - c % w) + Math.Abs(g / w - c / w) < 4)) continue;
            goals.Add(c);
            if (goals.Count >= 8) break;
        }
        return goals;
    }

    /// <summary>A seed for one choice, the same on every run.</summary>
    internal static int Hash(int seed, int a, int b, int c)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (int v in (ReadOnlySpan<int>)[seed, a, b, c])
            {
                h = (h ^ (uint)v) * 16777619;
                h ^= h >> 15;
            }
            return (int)(h & int.MaxValue);
        }
    }
}
