using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Generate;

/// <summary>How many towns of one township the stage makes, and how big.</summary>
public sealed class TownRow
{
    public string Township { get; set; } = "";
    public int Count { get; set; }
    public int MinTiles { get; set; } = 1;
    public int MaxTiles { get; set; } = 1;

    /// <summary>The fewest street tiles between this township and any other.</summary>
    public int Distance { get; set; } = 5;

    /// <summary>Biome tags it may start in (forest, burntforest, desert, snow, wasteland); null for rwgmixer's list.</summary>
    public List<string>? Biomes { get; set; }

    public TownRow Clone() => new()
    {
        Township = Township, Count = Count, MinTiles = MinTiles, MaxTiles = MaxTiles, Distance = Distance,
        Biomes = Biomes is null ? null : [.. Biomes],
    };
}

/// <summary>The town sites stage's settings.</summary>
public sealed class TownSiteOptions
{
    public int Seed { get; set; } = 1;

    /// <summary>One row per township; empty until filled from rwgmixer (<see cref="TownSiteStage.DefaultRows"/>).</summary>
    public List<TownRow> Rows { get; set; } = [];

    /// <summary>Share of the map's width kept clear of towns at each edge (RWG: a tenth, an eighth for roadside stops).</summary>
    public float EdgeMargin { get; set; } = 0.1f;

    /// <summary>How Build gives towns their traders.</summary>
    public TraderMode Traders { get; set; } = TraderMode.RwgCaps;

    /// <summary>
    /// Let towns take 150 m cells with more than one biome under them, so a town can straddle a border. RWG (and
    /// false) keeps every street tile in one biome, which leaves a row of unusable cells along each border.
    /// </summary>
    public bool CrossBorders { get; set; }

    public TownSiteOptions Clone() => new()
    {
        Seed = Seed, Rows = [.. Rows.Select(r => r.Clone())], EdgeMargin = EdgeMargin, Traders = Traders, CrossBorders = CrossBorders,
    };
}

public enum TraderMode
{
    /// <summary>
    /// The game's way: each biome's own trader (rwgmixer's prefab_spawn_adjust), at least min_count and at most
    /// max_count of each across the world, in that biome's biggest towns.
    /// </summary>
    RwgCaps,
    /// <summary>Every town gets one trader, whatever its biome.</summary>
    OnePerTown,
}

/// <summary>A planned town: its township, seed and street tile cells on the world's 150 m grid.</summary>
public sealed class TownSite
{
    public string Township { get; set; } = "";
    public int Seed { get; set; }

    /// <summary>Placed by hand: re-rolling the stage keeps it.</summary>
    public bool Pinned { get; set; }

    /// <summary>Drawn as a box by hand: the town fills exactly its cells, with no outskirts or gateways round them.</summary>
    public bool Exact { get; set; }

    /// <summary>Built into the world (its prefabs are in the world's list).</summary>
    public bool Built { get; set; }

    /// <summary>The town's street tiles (cx, cz), world grid from the south-west corner.</summary>
    public List<int[]> Cells { get; set; } = [];

    /// <summary>Outskirt cells the town also takes, kept clear of other towns.</summary>
    public List<int[]> Reserve { get; set; } = [];

    public IEnumerable<(int Cx, int Cz)> Core => Cells.Select(c => (c[0], c[1]));
    public IEnumerable<(int Cx, int Cz)> AllCells => Core.Concat(Reserve.Select(c => (c[0], c[1])));

    public TownSite Clone() => new()
    {
        Township = Township, Seed = Seed, Pinned = Pinned, Built = Built, Exact = Exact,
        Cells = [.. Cells.Select(c => (int[])c.Clone())], Reserve = [.. Reserve.Select(c => (int[])c.Clone())],
    };

    /// <summary>The cells' bounding box, in cells.</summary>
    public GridRect CellBounds => AllCells.Aggregate(GridRect.Empty, (r, c) => r.Union(new GridRect(c.Cx, c.Cz, c.Cx + 1, c.Cz + 1)));

    /// <summary>The town's area in grid metres.</summary>
    public GridRect Bounds => CellBounds.Scale(TownSiteStage.Cell);
}

/// <summary>Which 150 m cells of the world can hold a street tile, and the biome each lies in.</summary>
public sealed class TownCells
{
    private readonly bool[] _valid;
    private readonly string?[] _biome;

    public int Width { get; }

    private TownCells(int width, bool[] valid, string?[] biome)
    {
        Width = width;
        _valid = valid;
        _biome = biome;
    }

    public bool Inside(int cx, int cz) => (uint)cx < (uint)Width && (uint)cz < (uint)Width;
    public bool IsValid(int cx, int cz) => Inside(cx, cz) && _valid[cx + cz * Width];
    public string? Biome(int cx, int cz) => Inside(cx, cz) ? _biome[cx + cz * Width] : null;

    /// <summary>
    /// StreetTile.IsValidForStreetTile on the world as it is: dry, in one biome, free of radiation, not a mountain
    /// tile (from the terrain stage's plan, or a slope test like RWG's where there is none: a sample 75 m out more
    /// than 20 m off the middle), and not holding prefabs already there. With <paramref name="crossBorders"/> a cell
    /// may hold more than one biome, and counts as the biome at its middle.
    /// </summary>
    public static TownCells Build(World world, BiomeTable biomes, PrefabLibrary library, TerrainType[]? terrainPlan, bool crossBorders = false)
    {
        int width = world.Size / TownSiteStage.Cell;
        var valid = new bool[width * width];
        var biome = new string?[width * width];
        int terrainTiles = world.Size / TerrainStage.TileSize;
        if (terrainPlan is not null && terrainPlan.Length != terrainTiles * terrainTiles) terrainPlan = null;

        var taken = new bool[width * width];
        foreach (var p in world.Prefabs)
        {
            var f = PrefabPlacer.Footprint(world, p, library);
            for (int cz = Math.Max(0, f.Z0 / TownSiteStage.Cell); cz <= Math.Min(width - 1, (f.Z1 - 1) / TownSiteStage.Cell); cz++)
                for (int cx = Math.Max(0, f.X0 / TownSiteStage.Cell); cx <= Math.Min(width - 1, (f.X1 - 1) / TownSiteStage.Cell); cx++)
                    taken[cx + cz * width] = true;
        }

        var b = world.Biomes;
        int bs = b is null ? 1 : Math.Max(1, world.Size / b.Indices.Width);
        string? Tag(byte index) =>
            index < b!.Palette.Count && biomes.FindByColor(b.Palette[index]) is { } def ? TownGenerator.BiomeTag(def.Name) : null;
        Parallel.For(0, width, cz =>
        {
            for (int cx = 0; cx < width; cx++)
            {
                int i = cx + cz * width;
                int x0 = cx * TownSiteStage.Cell, z0 = cz * TownSiteStage.Cell;
                var rect = new GridRect(x0, z0, x0 + TownSiteStage.Cell, z0 + TownSiteStage.Cell);
                if (b is not null)
                {
                    // One biome under the whole cell.
                    byte first = b.Indices[x0 / bs, z0 / bs];
                    bool mixed = false;
                    for (int z = z0 / bs; z <= (rect.Z1 - 1) / bs && !mixed; z++)
                        for (int x = x0 / bs; x <= (rect.X1 - 1) / bs && !mixed; x++)
                            mixed = b.Indices[Math.Min(x, b.Indices.Width - 1), Math.Min(z, b.Indices.Height - 1)] != first;
                    if (mixed && !crossBorders)
                    {
                        biome[i] = Tag(first);
                        continue;
                    }
                    biome[i] = Tag(mixed ? b.Indices[Math.Min((x0 + 75) / bs, b.Indices.Width - 1), Math.Min((z0 + 75) / bs, b.Indices.Height - 1)] : first);
                }
                if (taken[i] || IsWet(world, rect) || HasRadiation(world, rect)) continue;
                int cxm = x0 + 75, czm = z0 + 75;
                if (terrainPlan is not null)
                {
                    int tx = Math.Min(cxm / TerrainStage.TileSize, terrainTiles - 1), tz = Math.Min(czm / TerrainStage.TileSize, terrainTiles - 1);
                    if (terrainPlan[tx + tz * terrainTiles] == TerrainType.Mountains) continue;
                }
                else if (Steep(world, cxm, czm)) continue;
                valid[i] = true;
            }
        });
        return new TownCells(width, valid, biome);
    }

    private static bool IsWet(World world, GridRect rect)
    {
        if (world.Water is not { } water) return false;
        for (int z = rect.Z0; z < rect.Z1; z += 3)
            for (int x = rect.X0; x < rect.X1; x += 3)
            {
                byte w = water[x, z];
                if (w > 0 && World.HeightToMetres(world.Height[x, z]) <= w + 0.5f) return true;
            }
        return false;
    }

    private static bool HasRadiation(World world, GridRect rect)
    {
        if (world.Radiation is not { } rad) return false;
        int rs = Math.Max(1, world.Size / rad.Width);
        for (int z = rect.Z0 / rs; z <= (rect.Z1 - 1) / rs; z++)
            for (int x = rect.X0 / rs; x <= (rect.X1 - 1) / rs; x++)
                if (rad.Contains(x, z) && rad[x, z] > 0) return true;
        return false;
    }

    private static bool Steep(World world, int x, int z)
    {
        float mid = World.HeightToMetres(world.Height[x, z]);
        for (int dz = -75; dz <= 75; dz += 75)
            for (int dx = -75; dx <= 75; dx += 75)
            {
                int sx = Math.Clamp(x + dx, 0, world.Size - 1), sz = Math.Clamp(z + dz, 0, world.Size - 1);
                if (MathF.Abs(World.HeightToMetres(world.Height[sx, sz]) - mid) > 20) return true;
            }
        return false;
    }
}

/// <summary>A town site and the town planned on it.</summary>
public sealed record PlannedTown(TownSite Site, TownPlan Plan);

/// <summary>What a planning run made and what it couldn't fit.</summary>
public sealed record TownSiteResult(List<TownSite> Sites, List<string> Shortfalls);

/// <summary>
/// RWG's town planning (TownPlanner.Plan) on the world's 150 m grid: for each township, largest first, the biome with
/// the most free cells is tried first; a start cell away from the edges and other towns is grown into a street
/// layout of a random size in the township's range, smaller sizes tried when it won't fit. The towns themselves are
/// planned and built by the Town tool's code (<see cref="TownGenerator"/>), each lot's POIs following its own biome.
/// </summary>
public static class TownSiteStage
{
    public const int Cell = TownGenerator.Tile;

    private static readonly (int X, int Z)[] Dir4 = [(0, 1), (1, 0), (0, -1), (-1, 0)];
    private static readonly (int X, int Z)[] Dir8 = [(0, 1), (1, 1), (1, 0), (1, -1), (0, -1), (-1, -1), (-1, 0), (-1, 1)];

    /// <summary>RWG's five biome tags, in its BiomeType order.</summary>
    public static readonly string[] BiomeTags = ["forest", "burntforest", "desert", "snow", "wasteland"];

    /// <summary>A row per buildable township with rwgmixer's Default count and tile range for the world's size.</summary>
    public static List<TownRow> DefaultRows(RwgMixer mixer) =>
        [.. mixer.BuildableTownships.Select(t => new TownRow
        {
            Township = t.Name,
            Count = t.Counts.Default,
            MinTiles = Math.Max(1, t.Tiles.Min),
            MaxTiles = Math.Max(1, t.Tiles.Max),
            Distance = t.Distance,
        })];

    /// <summary>
    /// Plans the town sites: keeps <paramref name="pinned"/> (and any built ones) and adds the rows' towns around
    /// them. Nothing in the world changes.
    /// </summary>
    public static TownSiteResult Plan(TownCells cells, RwgMixer mixer, TownSiteOptions options, IEnumerable<TownSite> pinned,
        CancellationToken cancel = default)
    {
        var sites = pinned.Select(s => s.Clone()).ToList();
        var shortfalls = new List<string>();
        var types = mixer.Townships.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        var rows = options.Rows.Where(r => r.Count > 0 && types.ContainsKey(r.Township))
            .Select((r, i) => (Row: r, Index: i)).OrderByDescending(r => r.Row.MaxTiles).ThenBy(r => r.Index).ToList();
        var occupied = Occupied(sites);
        var buffers = Buffers(sites);

        foreach (var (row, rowIndex) in rows)
        {
            var type = types[row.Township];
            var allowed = AllowedBiomes(row, type);
            var rand = new Random(Hash(options.Seed, rowIndex, 1982));
            bool roadside = type.IsRoadside;
            int margin = (int)(cells.Width * (roadside ? options.EdgeMargin * 1.25f : options.EdgeMargin));
            int lo = margin, hi = cells.Width - margin;
            int retries = 80, smallerRetries = 30;
            bool smaller = false;
            string? biome = null;
            // Count is the total for the township: kept towns (built or pinned) are already part of it.
            int made = sites.Count(s => s.Township.Equals(row.Township, StringComparison.OrdinalIgnoreCase));
            int min = Math.Max(1, Math.Min(row.MinTiles, row.MaxTiles)), max = Math.Max(min, row.MaxTiles);

            for (int i = made; i < row.Count; i++)
            {
                cancel.ThrowIfCancellationRequested();
                int distance = smaller ? 1 : roadside ? 3 : row.Distance;
                biome ??= MostFree(cells, occupied, allowed);
                List<(int, int)>? starts = null;
                string? first = biome;
                for (int b = 0; b < 5 && biome is not null; b++)
                {
                    starts = [];
                    for (int cz = lo + 1; cz < hi; cz++)
                        for (int cx = lo + 1; cx < hi; cx++)
                            if (cells.IsValid(cx, cz) && !occupied.Contains((cx, cz)) && cells.Biome(cx, cz) == biome
                                && !TooClose(sites, types, (cx, cz), roadside, distance))
                                starts.Add((cx, cz));
                    if (starts.Count > 0) break;
                    biome = Next(biome, allowed);
                    if (biome == first) break;
                }

                TownSite? site = null;
                if (starts is { Count: > 0 })
                {
                    int size = rand.Next(max >= 10 ? (min + max) / 2 : min, max + 1);
                    int floor = min;
                    if (smaller && floor >= 4)
                    {
                        floor = 1;
                        size = 4;
                    }
                    for (; size >= floor && site is null; size--)
                    {
                        int index = rand.Next(starts.Count);
                        int step = rand.NextDouble() < 0.5 ? starts.Count - 1 : 1;
                        for (int k = 0; k < starts.Count && site is null; k++)
                        {
                            var start = starts[index];
                            index = (index + step) % starts.Count;
                            for (int attempt = 0; attempt < 12 && site is null; attempt++)
                            {
                                var layout = StreetLayout(cells, buffers, occupied, start, size, rand, roadside);
                                if (layout.Count < size) continue;
                                var reserve = type.OutskirtPercent > 0 ? Grow(cells, occupied, type, layout, !smaller) : [];
                                if (reserve is null) continue;
                                site = new TownSite
                                {
                                    Township = type.Name,
                                    Seed = Hash(options.Seed, rowIndex, i + 7),
                                    Cells = [.. layout.Select(c => new[] { c.Item1, c.Item2 })],
                                    Reserve = [.. reserve.Select(c => new[] { c.Item1, c.Item2 })],
                                };
                            }
                        }
                    }
                }

                if (site is null)
                {
                    // RWG's retries: other biomes first, then towns of four tiles anywhere close.
                    if (retries > 0)
                    {
                        retries--;
                        i--;
                        biome = biome is null ? null : Next(biome, allowed);
                        continue;
                    }
                    if (min >= 4 && smallerRetries > 0)
                    {
                        smallerRetries--;
                        i--;
                        smaller = true;
                        biome = biome is null ? null : Next(biome, allowed);
                        continue;
                    }
                }
                retries = 80;
                smallerRetries = 30;
                smaller = false;
                if (site is null)
                {
                    biome = null;
                    continue;
                }
                sites.Add(site);
                occupied.UnionWith(site.AllCells);
                buffers.Add(Expand(site.CellBounds, 1));
                made++;
                biome = Next(biome!, allowed);
            }
            if (made < row.Count) shortfalls.Add($"{GameNames.Township(row.Township)}: {made} of {row.Count}");
        }
        return new TownSiteResult(sites, shortfalls);
    }

    /// <summary>
    /// Lays out a town of <paramref name="size"/> tiles from <paramref name="start"/>, for a town added or moved by
    /// hand: the same growth as the stage, ignoring spacing. Empty if the start can't take a town.
    /// </summary>
    public static TownSite? Regrow(TownCells cells, RwgMixer mixer, IReadOnlyList<TownSite> others, string township, (int, int) start,
        int size, int seed, GridRect? within = null)
    {
        if (mixer.Townships.FirstOrDefault(t => t.Name.Equals(township, StringComparison.OrdinalIgnoreCase)) is not { } type) return null;
        var occupied = Occupied(others);
        if (!cells.IsValid(start.Item1, start.Item2) || occupied.Contains(start)) return null;
        var buffers = Buffers(others);
        if (within is { } box)
        {
            // A drawn box is the whole town: the usable cells of the box joined to the start, and no outskirts.
            var wanted = new List<(int, int)> { start };
            var seen = new HashSet<(int, int)> { start };
            for (int i = 0; i < wanted.Count; i++)
                foreach (var d in Dir4)
                {
                    var n = (wanted[i].Item1 + d.X, wanted[i].Item2 + d.Z);
                    if (n.Item1 < box.X0 || n.Item1 >= box.X1 || n.Item2 < box.Z0 || n.Item2 >= box.Z1) continue;
                    if (!occupied.Contains(n) && CheckPos(cells, buffers, n) && seen.Add(n)) wanted.Add(n);
                }
            return new TownSite { Township = type.Name, Seed = seed, Cells = [.. wanted.Select(c => new[] { c.Item1, c.Item2 })], Exact = true };
        }
        var rand = new Random(seed);
        for (int s = Math.Max(1, size); s >= 1; s--)
        {
            var layout = StreetLayout(cells, buffers, occupied, start, s, rand, type.IsRoadside);
            if (layout.Count < s) continue;
            var reserve = type.OutskirtPercent > 0 ? Grow(cells, occupied, type, layout, cancelIfInvalid: false) ?? [] : [];
            return new TownSite
            {
                Township = type.Name, Seed = seed,
                Cells = [.. layout.Select(c => new[] { c.Item1, c.Item2 })],
                Reserve = [.. reserve.Select(c => new[] { c.Item1, c.Item2 })],
            };
        }
        return null;
    }

    /// <summary>
    /// Plans the towns on the sites, one after another, so each counts the street tiles and POIs of those before it.
    /// </summary>
    public static List<PlannedTown> PlanTowns(World world, PrefabLibrary library, RwgMixer mixer, BiomeTable biomes, TownCells cells,
        IReadOnlyList<TownSite> sites, CancellationToken cancel = default, TraderMode traders = TraderMode.RwgCaps)
    {
        var types = mixer.Townships.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        var quotas = traders == TraderMode.RwgCaps ? TraderQuotas(world, library, mixer, cells, sites.Where(s => !s.Built && types.ContainsKey(s.Township))) : null;
        var planned = new List<PrefabInstance>();
        var result = new List<PlannedTown>();
        foreach (var site in sites)
        {
            cancel.ThrowIfCancellationRequested();
            if (site.Built || !types.TryGetValue(site.Township, out var type)) continue;
            // Street tiles only go on cells that can hold one; a site planned before the ground changed may no longer fit.
            var core = site.Core.Where(c => cells.IsValid(c.Cx, c.Cz)).ToList();
            if (core.Count == 0) continue;
            // Room for outskirts and gateways: valid cells near the town that no other town takes.
            var others = Occupied(sites.Where(s => s != site));
            var box = site.CellBounds;
            var valid = new HashSet<(int, int)>();
            for (int cz = box.Z0 - 3; cz < box.Z1 + 3; cz++)
                for (int cx = box.X0 - 3; cx < box.X1 + 3; cx++)
                    if (cells.IsValid(cx, cz) && !others.Contains((cx, cz))) valid.Add((cx, cz));
            float scale = Math.Max(1f, core.Count / (float)Math.Max(1, type.Tiles.Max));
            var options = new TownOptions(type, site.Seed)
            {
                ReplacePrefabs = false,
                ConnectRoads = false,
                LimitScale = scale,
                Planned = [.. planned],
                Traders = quotas is null ? 1 : quotas.GetValueOrDefault(site),
                TraderCaps = quotas is not null,
                NoOutskirts = site.Exact,
            };
            var plan = TownGenerator.PlanCells(world, library, mixer, biomes, core, valid, options);
            planned.AddRange(plan.Prefabs);
            // The plan's outskirts and gateways are the cells the town really takes, so later towns keep off them
            // and taking the town down finds all of it.
            var coreSet = core.ToHashSet();
            if (plan.Tiles.Count > 0)
                site.Reserve = [.. plan.Tiles.Select(t => (t.Cx, t.Cz)).Where(c => !coreSet.Contains(c)).Select(c => new[] { c.Item1, c.Item2 })];
            result.Add(new PlannedTown(site, plan));
        }
        return result;
    }

    /// <summary>
    /// How many trader lots each town keeps under RWG's caps: per biome, its trader's rule (rwgmixer
    /// prefab_spawn_adjust naming a trader prefab and the biome) allows max_count less those already in the world;
    /// the biggest towns of the biome get one each, and a second each, biggest first, while the biome is under
    /// min_count. Null (one per town) when rwgmixer ties no trader to a biome.
    /// </summary>
    internal static Dictionary<TownSite, int>? TraderQuotas(World world, PrefabLibrary library, RwgMixer mixer, TownCells cells,
        IEnumerable<TownSite> sites)
    {
        var traderNames = library.All.Where(p => p.IsTrader && !p.IsPart && !p.IsStreetTile).Select(p => p.Name).ToList();
        var rules = new Dictionary<string, SpawnAdjust>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in mixer.SpawnAdjusts)
        {
            if (rule.PartialName.Length == 0 || rule.BiomeTags.Count == 0
                || !traderNames.Any(n => n.Contains(rule.PartialName, StringComparison.OrdinalIgnoreCase))) continue;
            foreach (var b in rule.BiomeTags) rules.TryAdd(b.Trim(), rule);
        }
        if (rules.Count == 0) return null;

        var quotas = new Dictionary<TownSite, int>();
        var byBiome = sites
            .Select(s => (Site: s, Biome: s.Core.Select(c => cells.Biome(c.Cx, c.Cz)).Where(b => b is not null)
                .GroupBy(b => b!, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault()))
            .Where(e => e.Biome is not null && rules.ContainsKey(e.Biome))
            .GroupBy(e => e.Biome!, StringComparer.OrdinalIgnoreCase);
        foreach (var group in byBiome)
        {
            var rule = rules[group.Key];
            int existing = world.Prefabs.Count(p => p.Name.Contains(rule.PartialName, StringComparison.OrdinalIgnoreCase));
            int left = Math.Max(0, rule.MaxCount - existing), need = Math.Max(0, rule.MinCount - existing);
            var towns = group.Select(e => e.Site).OrderByDescending(s => s.Cells.Count).ToList();
            int given = 0;
            for (int round = 0; round < 2; round++)
                foreach (var town in towns)
                {
                    if (given >= left || round > 0 && given >= need) break;
                    quotas[town] = quotas.GetValueOrDefault(town) + 1;
                    given++;
                }
        }
        return quotas;
    }

    /// <summary>Builds the planned towns into the world as part of <paramref name="session"/>. Returns what changed.</summary>
    public static GridRect Build(World world, PrefabLibrary library, IReadOnlyList<PlannedTown> towns, EditSession session)
    {
        var changed = GridRect.Empty;
        foreach (var town in towns)
        {
            changed = changed.Union(TownGenerator.Apply(world, library, town.Plan, session));
            town.Site.Built = true;
        }
        return changed;
    }

    /// <summary>
    /// Takes built towns down: removes the prefabs standing on their cells, for a terrain re-run that clears the later
    /// stages. The sites stay, unbuilt, so Build can put them up again on the new ground. Returns the towns taken down.
    /// </summary>
    public static int TakeDown(World world, PrefabLibrary library, IEnumerable<TownSite> sites, GridRect? area, EditSession session)
    {
        var down = sites.Where(s => s.Built && (area is not { } a || Overlaps(s.Bounds, a))).ToList();
        if (down.Count == 0) return 0;
        var cells = down.SelectMany(s => s.AllCells).ToHashSet();
        session.TouchList(world.Prefabs, p => p.Clone(), down.Aggregate(GridRect.Empty, (r, s) => r.Union(s.Bounds)).Clamp(world.Size, world.Size));
        world.Prefabs.RemoveAll(p =>
        {
            var f = PrefabPlacer.Footprint(world, p, library);
            int cx = (f.X0 + f.X1) / 2 / Cell, cz = (f.Z0 + f.Z1) / 2 / Cell;
            return cells.Contains((cx, cz));
        });
        foreach (var s in down) s.Built = false;
        return down.Count;
    }

    /// <summary>
    /// Drops the sites that new ground leaves behind. After a whole-map terrain run (<paramref name="wholeMap"/>) every
    /// site that is not pinned goes, and a pinned one stays only where all its street cells are still usable. After an
    /// area run only unbuilt sites are looked at, and one goes when any of its street cells is no longer usable.
    /// <paramref name="cells"/> must be built from the new ground. Returns how many unpinned sites were cleared and
    /// how many others were dropped because their ground is now water, mountains or radiation.
    /// </summary>
    public static (int Cleared, int Unusable) DropForNewGround(List<TownSite> sites, TownCells cells, bool wholeMap)
    {
        int cleared = 0, unusable = 0;
        for (int i = sites.Count - 1; i >= 0; i--)
        {
            var site = sites[i];
            if (wholeMap && !site.Pinned)
            {
                sites.RemoveAt(i);
                cleared++;
            }
            else if ((wholeMap || !site.Built) && site.Core.Any(c => !cells.IsValid(c.Cx, c.Cz)))
            {
                sites.RemoveAt(i);
                unusable++;
            }
        }
        return (cleared, unusable);
    }

    /// <summary>
    /// Sets each site's Built flag from the world: built when a street tile stands on one of its cells. Keeps the
    /// flags right after undo or redo of a build or a take-down.
    /// </summary>
    public static void MarkBuilt(World world, PrefabLibrary library, IEnumerable<TownSite> sites)
    {
        var tiles = new HashSet<(int, int)>();
        foreach (var p in world.Prefabs)
        {
            if (!p.Name.StartsWith("rwg_tile_", StringComparison.OrdinalIgnoreCase)) continue;
            var f = PrefabPlacer.Footprint(world, p, library);
            tiles.Add(((f.X0 + f.X1) / 2 / Cell, (f.Z0 + f.Z1) / 2 / Cell));
        }
        foreach (var s in sites) s.Built = s.Core.Any(tiles.Contains);
    }

    private static GridRect Expand(GridRect r, int by) => new(r.X0 - by, r.Z0 - by, r.X1 + by, r.Z1 + by);

    private static bool Overlaps(GridRect a, GridRect b) => a.X0 < b.X1 && a.X1 > b.X0 && a.Z0 < b.Z1 && a.Z1 > b.Z0;

    private static HashSet<(int, int)> Occupied(IEnumerable<TownSite> sites) => sites.SelectMany(s => s.AllCells).ToHashSet();

    /// <summary>Each town's buffer: its cells' box one cell larger all round (Township.BufferArea).</summary>
    private static List<GridRect> Buffers(IEnumerable<TownSite> sites) => [.. sites.Select(s => Expand(s.CellBounds, 1))];

    private static List<string> AllowedBiomes(TownRow row, TownshipType type)
    {
        var list = row.Biomes ?? [.. type.Biomes];
        return list.Count == 0 ? [.. BiomeTags] : [.. BiomeTags.Where(b => list.Contains(b, StringComparer.OrdinalIgnoreCase))];
    }

    /// <summary>getBiomeWithMostAvailableSpace: the allowed biome with the most free cells.</summary>
    private static string? MostFree(TownCells cells, HashSet<(int, int)> occupied, List<string> allowed)
    {
        var counts = new Dictionary<string, int>();
        for (int cz = 0; cz < cells.Width; cz++)
            for (int cx = 0; cx < cells.Width; cx++)
                if (cells.IsValid(cx, cz) && !occupied.Contains((cx, cz)) && cells.Biome(cx, cz) is { } b && allowed.Contains(b))
                    counts[b] = counts.GetValueOrDefault(b) + 1;
        return counts.Count == 0 ? null : allowed.Where(counts.ContainsKey).OrderByDescending(b => counts[b]).First();
    }

    /// <summary>nextBiomeType: the next allowed biome in RWG's order.</summary>
    private static string? Next(string current, List<string> allowed)
    {
        int i = Array.IndexOf(BiomeTags, current);
        for (int k = 0; k < 4; k++)
        {
            i = (i + 1) % BiomeTags.Length;
            if (allowed.Contains(BiomeTags[i])) return BiomeTags[i];
        }
        return allowed.Contains(current) ? current : null;
    }

    /// <summary>IsTooClose: within <paramref name="distance"/> cells of any town's street tile (roadside stops keep 5 apart).</summary>
    private static bool TooClose(List<TownSite> sites, Dictionary<string, TownshipType> types, (int X, int Z) c, bool roadside, int distance)
    {
        foreach (var s in sites)
        {
            bool otherRoadside = types.TryGetValue(s.Township, out var t) && t.IsRoadside;
            foreach (var (x, z) in s.Core)
            {
                int dx = Math.Abs(c.X - x), dz = Math.Abs(c.Z - z);
                if (roadside && otherRoadside && dx < 5 && dz < 5 || dx < distance && dz < distance) return true;
            }
        }
        return false;
    }

    private static bool InBuffer(List<GridRect> buffers, (int X, int Z) c)
    {
        foreach (var b in buffers)
            if (b.Contains(c.X, c.Z)) return true;
        return false;
    }

    /// <summary>StreetLayoutCheckPos: two cells in from the edge, valid, outside every town's buffer.</summary>
    private static bool CheckPos(TownCells cells, List<GridRect> buffers, (int X, int Z) c) =>
        c.X >= 2 && c.Z >= 2 && c.X < cells.Width - 2 && c.Z < cells.Width - 2 && cells.IsValid(c.X, c.Z) && !InBuffer(buffers, c);

    /// <summary>
    /// GetStreetLayout: from the start cell, a square core for towns over 16 tiles, then random growth into valid
    /// neighbours until the town has its size.
    /// </summary>
    private static List<(int, int)> StreetLayout(TownCells cells, List<GridRect> buffers, HashSet<(int, int)> occupied, (int X, int Z) start,
        int size, Random rand, bool roadside)
    {
        var tiles = new List<(int, int)>();
        if (InBuffer(buffers, start)) return tiles;
        if (roadside)
            foreach (var d in Dir8)
                if (occupied.Contains((start.X + d.X, start.Z + d.Z))) return tiles;
        if (size == 1)
        {
            if (cells.IsValid(start.X, start.Z)) tiles.Add(start);
            return tiles;
        }
        var frontier = new List<(int X, int Z)>();
        if (size <= 16)
        {
            tiles.Add(start);
            frontier.Add(start);
        }
        else
        {
            int side = (int)MathF.Sqrt(size) - 1;
            int from = side / -2, to = side / 2 - 1;
            for (int dz = from; dz <= to; dz++)
                for (int dx = from; dx <= to; dx++)
                {
                    var c = (start.X + dx, start.Z + dz);
                    if (!CheckPos(cells, buffers, c))
                    {
                        tiles.Clear();
                        return tiles;
                    }
                    tiles.Add(c);
                    if (dz == from || dz == to || dx == from || dx == to) frontier.Add(c);
                }
            if (tiles.Count >= size) return tiles;
        }
        var set = tiles.ToHashSet();
        while (frontier.Count > 0)
        {
            int pick = rand.Next(frontier.Count);
            var t = frontier[pick];
            frontier.RemoveAt(pick);
            int turn = rand.Next(4);
            for (int k = 0; k < 4; k++)
            {
                var d = Dir4[(k + turn) & 3];
                var n = (t.X + d.X, t.Z + d.Z);
                if (!CheckPos(cells, buffers, n) || !set.Add(n)) continue;
                tiles.Add(n);
                if (tiles.Count >= size) return tiles;
                frontier.Add(n);
            }
        }
        return tiles;
    }

    /// <summary>
    /// TownPlanner.Grow: the outskirt cells round the layout (4-way, 8-way when the share is whole), thinned to the
    /// township's share. Null when they would touch another town, or (if asked) when one isn't valid.
    /// </summary>
    private static List<(int, int)>? Grow(TownCells cells, HashSet<(int, int)> occupied, TownshipType type, List<(int, int)> layout,
        bool cancelIfInvalid)
    {
        var dirs = type.OutskirtPercent < 1 ? Dir4 : Dir8;
        var inLayout = layout.ToHashSet();
        var result = new List<(int, int)>();
        foreach (var (x, z) in layout)
            foreach (var d in dirs)
            {
                var n = (x + d.X, z + d.Z);
                if (!cells.Inside(n.Item1, n.Item2) || inLayout.Contains(n) || result.Contains(n)) continue;
                if (!cells.IsValid(n.Item1, n.Item2))
                {
                    if (cancelIfInvalid) return null;
                    continue;
                }
                foreach (var e in Dir8)
                    if (occupied.Contains((n.Item1 + e.X, n.Item2 + e.Z))) return null;
                result.Add(n);
            }
        if (type.OutskirtPercent < 1)
        {
            float drop = 1 - type.OutskirtPercent, sum = 0;
            for (int i = result.Count - 1; i >= 0; i--)
            {
                sum += drop;
                if (sum < 1) continue;
                sum--;
                result.RemoveAt(i);
            }
        }
        return result;
    }

    /// <summary>A seed for one choice, the same on every run.</summary>
    private static int Hash(int seed, int a, int b)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (int v in (ReadOnlySpan<int>)[seed, a, b])
            {
                h = (h ^ (uint)v) * 16777619;
                h ^= h >> 15;
            }
            return (int)(h & int.MaxValue);
        }
    }
}
