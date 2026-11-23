using System.Collections;
using System.IO.Compression;
using System.Numerics;
using System.Text.Json.Serialization;
using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Generate;

/// <summary>The highways stage's settings.</summary>
public sealed class HighwayOptions
{
    public int Seed { get; set; } = 1;

    /// <summary>How much the seed bends routes (0 for no seeded bends): route cost varies by up to this share across the map.</summary>
    public float Wiggle { get; set; } = 0.15f;

    /// <summary>The longest stretch of water, in metres, a highway may cross on a causeway; wider water is gone round.</summary>
    public int CausewayMax { get; set; } = 120;

    /// <summary>After RWG's rules, join any group of towns cut off from the rest to its nearest neighbours.</summary>
    public bool JoinUp { get; set; } = true;

    /// <summary>
    /// RWG lets two towns be joined by two highways, which leave by different exits and run side by side. Off (the
    /// default): one highway per pair, and a gateway with no other town to reach runs a country road to a highway.
    /// </summary>
    public bool SecondRoad { get; set; }

    /// <summary>
    /// Join groups of towns on separate islands with bridges: the game's rwg_bridge pieces in a straight line
    /// north-south or east-west, with the water left open under them. Used by the join-up pass.
    /// </summary>
    public bool Bridges { get; set; }

    /// <summary>The longest stretch of water, in metres, a bridge may cross.</summary>
    public int BridgeMax { get; set; } = 1500;

    /// <summary>
    /// After every island is joined, bridge islands straight across too when the way round over the bridges takes
    /// <see cref="HighwayStage.ShortcutHops"/> or more of them.
    /// </summary>
    public bool BridgeShortcuts { get; set; }

    /// <summary>Put car wrecks on bridge decks, about one per 100-150 m (chosen from <see cref="Seed"/>). On by default; saves from before it was added load with it on.</summary>
    public bool DeckWrecks { get; set; } = true;

    public HighwayOptions Clone() => (HighwayOptions)MemberwiseClone();
}

[JsonConverter(typeof(JsonStringEnumConverter<HighwayKind>))]
public enum HighwayKind
{
    /// <summary>RWG's: a gateway to a nearby town.</summary>
    Highway,
    /// <summary>RWG's: between neighbouring gateways of a big city.</summary>
    Ring,
    /// <summary>RWG's: a gravel road from a town without gateways (or a gateway left unjoined) to the nearest highway.</summary>
    Country,
    /// <summary>Extra: joins a group of towns cut off from the rest.</summary>
    JoinUp,
    /// <summary>Extra: a bridge between islands (prefabs, not road paint), which two join-up roads lead to.</summary>
    Bridge,
}

/// <summary>One road the stage drew, with the ground and road pixels it replaced, so it can be taken out exactly.</summary>
public sealed class HighwayRoad
{
    public HighwayKind Kind { get; set; }
    public RoadType Type { get; set; }
    public float Radius { get; set; }

    /// <summary>Control points in grid metres, [x, z].</summary>
    public List<float[]> Points { get; set; } = [];

    /// <summary>The cells it changed and what they held before, compressed (see <see cref="HighwayStage"/>).</summary>
    public byte[] Ground { get; set; } = [];

    /// <summary>A bridge's pieces, taken out with it.</summary>
    public List<PlacedPrefab> Prefabs { get; set; } = [];

    public HighwayRoad Clone() => new()
    {
        Kind = Kind,
        Type = Type,
        Radius = Radius,
        Points = [.. Points.Select(p => (float[])p.Clone())],
        Ground = Ground,
        Prefabs = [.. Prefabs.Select(p => p.Clone())],
    };

    public float Length
    {
        get
        {
            float l = 0;
            for (int i = 1; i < Points.Count; i++) l += MathF.Sqrt(MathF.Pow(Points[i][0] - Points[i - 1][0], 2) + MathF.Pow(Points[i][1] - Points[i - 1][1], 2));
            return l;
        }
    }
}

/// <summary>What a highways run did.</summary>
public sealed record HighwayResult(List<HighwayRoad> Added, List<string> Notes, int Removed)
{
    public int Count(HighwayKind kind) => Added.Count(r => r.Kind == kind);
    public float Kilometres => Added.Sum(r => r.Length) / 1000;
}

/// <summary>
/// RWG's highways (HighwayPlanner.Plan): gateway towns, largest first, join the cheapest of their three nearest
/// towns; big cities get a ring between their gateways; towns without gateways get a gravel road to the nearest
/// highway. Routes come from RWG's A* on a 10 m grid (PathingUtils.FindDetailedPath) and its height smoothing
/// (Path.ProcessPath). Extras: a join-up so every town can be reached, short causeways over water, and a seeded
/// wiggle so re-rolls give other routes. Works from the street tiles in the world, so RWG worlds' towns count too.
/// </summary>
public static partial class HighwayStage
{
    /// <summary>RWG's pathing grid (PATHING_GRID_TILE_SIZE).</summary>
    public const int Step = 10;

    /// <summary>RWG's highway: 4 lanes, half-width 9 m; country road: 2 lanes, 4.5 m.</summary>
    public const float HighwayRadius = 9, CountryRadius = 4.5f;

    private static float RoadReach(bool country) => country
        ? CountryRadius + RoadPath.BlendFor(RoadType.Gravel)
        : HighwayRadius + RoadPath.BlendFor(RoadType.Asphalt);

    /// <summary>Straight run out of a town exit before the route may turn, so the road meets the street square.</summary>
    private const float Lead = 30;

    // ---- the towns, read from the street tiles in the world ----

    public sealed class Tile(GridRect rect, int exits, bool gateway)
    {
        public GridRect Rect { get; } = rect;
        public int Exits { get; } = exits;
        public bool Gateway { get; } = gateway;
        public Town Town { get; set; } = null!;

        public Vector2 ExitPoint(int dir) => dir switch
        {
            0 => new(Rect.X0 + 75, Rect.Z1 - 1),
            1 => new(Rect.X1 - 1, Rect.Z0 + 75),
            2 => new(Rect.X0 + 75, Rect.Z0),
            _ => new(Rect.X0, Rect.Z0 + 75),
        };
    }

    public sealed class Exit(Tile tile, int dir)
    {
        public Tile Tile { get; } = tile;
        public int Dir { get; } = dir;
        public Vector2 Point => Tile.ExitPoint(Dir);
        public Vector2 LeadPoint => Point + Outward(Dir) * Lead;
        public bool Used { get; set; }
    }

    public sealed class Town
    {
        public int Id { get; init; }
        public List<Tile> Tiles { get; } = [];
        public List<Exit> Exits { get; } = [];
        public bool SpawnGateway { get; set; }
        public bool Big { get; set; }
        public Vector2 Centre { get; set; }
        public Dictionary<Town, int> Links { get; } = [];
        public IEnumerable<Tile> Gateways => Tiles.Where(t => t.Gateway);
    }

    private static Vector2 Outward(int dir) => dir switch { 0 => new(0, 1), 1 => new(1, 0), 2 => new(0, -1), _ => new(-1, 0) };

    /// <summary>The street shape in a street tile's name (rwg_tile_downtown_t_03 is a t).</summary>
    public static int ShapeOf(string name)
    {
        var parts = name.Split('_');
        if (parts.Length < 4) return -1;
        string token = parts[3].ToLowerInvariant();
        int digit = token.IndexOfAny("0123456789".ToCharArray());
        if (digit > 0) token = token[..digit];
        return Array.IndexOf(TownGenerator.ShapeNames, token);
    }

    /// <summary>Groups the world's street tiles into towns (tiles touching edge to edge) and finds their open exits.</summary>
    public static List<Town> ReadTowns(World world, PrefabLibrary library, RwgMixer mixer, IEnumerable<TownSite> sites)
    {
        var tiles = new Dictionary<(int, int), Tile>();
        foreach (var p in world.Prefabs)
        {
            if (library.Find(p.Name) is not { IsStreetTile: true } info) continue;
            int shape = ShapeOf(info.Name);
            if (shape < 0) continue;
            int rotations = 0;
            for (int r = 0; r < 4; r++)
                if (TownGenerator.TileRotation(info, r) == (p.Rotation & 3)) { rotations = r; break; }
            var rect = PrefabPlacer.Footprint(world, p, library);
            tiles[(rect.X0, rect.Z0)] = new Tile(rect, TownGenerator.ShapeExitsAt(shape, rotations), info.District == "gateway");
        }
        Tile? Neighbour(Tile t, int dir)
        {
            var o = Outward(dir);
            int x = t.Rect.X0 + (int)o.X * (t.Rect.X1 - t.Rect.X0), z = t.Rect.Z0 + (int)o.Y * (t.Rect.Z1 - t.Rect.Z0);
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    if (tiles.TryGetValue((x + dx, z + dz), out var n)) return n;
            return null;
        }

        var siteOf = new Dictionary<(int, int), TownSite>();
        foreach (var s in sites)
            foreach (var c in s.AllCells) siteOf[c] = s;
        var types = mixer.Townships.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

        var towns = new List<Town>();
        var seen = new HashSet<Tile>();
        foreach (var start in tiles.Values.OrderBy(t => t.Rect.Z0).ThenBy(t => t.Rect.X0))
        {
            if (!seen.Add(start)) continue;
            var town = new Town { Id = towns.Count };
            var queue = new Queue<Tile>([start]);
            while (queue.TryDequeue(out var t))
            {
                t.Town = town;
                town.Tiles.Add(t);
                for (int dir = 0; dir < 4; dir++)
                    if (Neighbour(t, dir) is { } n && seen.Add(n)) queue.Enqueue(n);
            }
            foreach (var t in town.Tiles)
                for (int dir = 0; dir < 4; dir++)
                    if ((t.Exits & (1 << dir)) != 0 && Neighbour(t, dir) is null) town.Exits.Add(new Exit(t, dir));
            town.Centre = new Vector2((float)town.Tiles.Average(t => (t.Rect.X0 + t.Rect.X1) / 2.0), (float)town.Tiles.Average(t => (t.Rect.Z0 + t.Rect.Z1) / 2.0));

            // The township it was planned as, if the town sites stage made it; otherwise read it from the tiles.
            var site = town.Tiles.Select(t => ((t.Rect.X0 + 75) / TownSiteStage.Cell, (t.Rect.Z0 + 75) / TownSiteStage.Cell))
                .Select(c => siteOf.GetValueOrDefault(c)).FirstOrDefault(s => s is not null);
            if (site is not null && types.TryGetValue(site.Township, out var type))
            {
                town.SpawnGateway = type.SpawnGateway && town.Gateways.Any();
                town.Big = type.IsBig;
            }
            else
            {
                town.SpawnGateway = town.Gateways.Any();
                town.Big = town.Tiles.Count >= 60;
            }
            towns.Add(town);
        }

        // A town whose streets all stay inside it (a small Old West town) gets the outer edge facing the nearest
        // other town as its exit, so a road can still reach it.
        foreach (var town in towns.Where(t => t.Exits.Count == 0))
        {
            var near = towns.Where(o => o != town).OrderBy(o => Vector2.DistanceSquared(o.Centre, town.Centre)).FirstOrDefault()?.Centre
                       ?? new Vector2(world.Size / 2f, world.Size / 2f);
            (Tile Tile, int Dir)? best = null;
            foreach (var t in town.Tiles)
                for (int dir = 0; dir < 4; dir++)
                    if (Neighbour(t, dir) is null && (best is null || Vector2.DistanceSquared(t.ExitPoint(dir), near) < Vector2.DistanceSquared(best.Value.Tile.ExitPoint(best.Value.Dir), near)))
                        best = (t, dir);
            if (best is { } b) town.Exits.Add(new Exit(b.Tile, b.Dir));
        }
        return towns;
    }

    // ---- the pathing grid and RWG's A* ----

    internal sealed partial class PathGrid
    {
        /// <summary>How far round a prefab, in metres, a road keeps to its ground.</summary>
        public const int HoldReach = 20;

        /// <summary><see cref="NoJoin"/>: a bridge's ramps, where no other road may join a highway.</summary>
        public const byte Blocked = 1, Water = 2, Road = 4, Prefab = 8, NoJoin = 16;
        public const byte HighwayClearance = 32, CountryClearance = 64;

        /// <summary>How far round a bridge no road may join a highway, so none drops off its ramps.</summary>
        public const int BridgeRamp = 80;
        public int Width { get; }
        public float[] Height { get; }
        public byte[] Flags { get; }
        public float[] Wiggle { get; }

        /// <summary>
        /// The ground a road must keep to in each cell, or NaN: a prefab's own ground (towns included) inside it and
        /// for <see cref="HoldReach"/> around it, so a road meets a street tile or POI level, as RWG's poiHeightMask
        /// holds its roads to the flattened ground.
        /// </summary>
        public float[] Hold { get; }

        private readonly World _world;
        private const int StreetBucket = PrefabPlacer.StreetTileSize;
        private readonly Dictionary<(int X, int Z), List<GridRect>> _streets = [];

        public PathGrid(World world, PrefabLibrary library, HighwayOptions options)
        {
            _world = world;
            int w = Width = world.Size / Step;
            Height = new float[w * w];
            Flags = new byte[w * w];
            Wiggle = new float[w * w];
            Hold = new float[w * w];
            _roadPoints = new Vector2[w * w];
            _roadEdges = new byte[w * w];
            _nearRoad = new int[w * w];
            _roadDistance = new byte[w * w];
            LegacyRoad = new bool[w * w];
            Array.Fill(Hold, float.NaN);
            var noise = new Noise(options.Seed);
            float wiggle = Math.Clamp(options.Wiggle, 0, 2);
            var water = world.Water;
            var roads = world.Roads;
            Parallel.For(0, w, cz =>
            {
                for (int cx = 0; cx < w; cx++)
                {
                    int i = cx + cz * w, x = cx * Step + Step / 2, z = cz * Step + Step / 2;
                    float h = World.HeightToMetres(world.Height[x, z]);
                    Height[i] = h;
                    byte wl = water?[x, z] ?? 0;
                    if (wl > 0 && h <= wl + 0.5f)
                    {
                        Flags[i] |= Water;
                        // Over water a road rides a causeway 1 m up, so that is the height its steps climb to.
                        Height[i] = wl + 1;
                    }
                    // Keep clear of the map's very edge, as RWG does.
                    if (cx < 3 || cz < 3 || cx >= w - 3 || cz >= w - 3) Flags[i] |= Blocked;
                    // Low-frequency noise, so a re-roll bends whole stretches of road rather than making them weave.
                    float n = noise.Fbm(x / 900f, z / 900f, 3);
                    Wiggle[i] = 1 + wiggle * (n + 1) / 2;
                }
            });

            // Prefabs, town tiles among them, are in the way; roads near them keep to their ground. Round a prefab
            // the ground held is a blend by distance of every prefab within reach, so where two prefabs' reaches
            // overlap a road meets each at its own ground and slopes evenly between them (#32).
            var weightSum = new float[w * w];
            var weightedGround = new float[w * w];
            var insideAny = new bool[w * w];
            foreach (var p in world.Prefabs)
            {
                var f = PrefabPlacer.Footprint(world, p, library);
                if (library.Find(p.Name) is not { } info || info.IsPart) continue;
                // Roads meet a bridge at its deck, not the foot of its pillars.
                float ground = info.RoadLevelAbove(p.Position.Y);
                if (info.IsBridge) MarkNoJoin(f);
                int r = HoldReach / Step;
                for (int cz = Math.Max(0, f.Z0 / Step - r); cz <= Math.Min(w - 1, (f.Z1 - 1) / Step + r); cz++)
                    for (int cx = Math.Max(0, f.X0 / Step - r); cx <= Math.Min(w - 1, (f.X1 - 1) / Step + r); cx++)
                    {
                        int i = cx + cz * w;
                        bool inside = cx >= f.X0 / Step && cx <= (f.X1 - 1) / Step && cz >= f.Z0 / Step && cz <= (f.Z1 - 1) / Step;
                        if (inside)
                        {
                            Flags[i] |= Blocked | Prefab;
                            Hold[i] = ground;
                            insideAny[i] = true;
                            continue;
                        }
                        float x = cx * Step + Step / 2f, z = cz * Step + Step / 2f;
                        float dx = Math.Max(0, Math.Max(f.X0 - x, x - f.X1)), dz = Math.Max(0, Math.Max(f.Z0 - z, z - f.Z1));
                        float weight = 1 / Math.Max(1, MathF.Sqrt(dx * dx + dz * dz));
                        weightSum[i] += weight;
                        weightedGround[i] += weight * ground;
                    }
            }
            for (int i = 0; i < weightSum.Length; i++)
                if (weightSum[i] > 0 && !insideAny[i]) Hold[i] = weightedGround[i] / weightSum[i];

            // Water wider than a causeway is gone round: a wet cell further than half of it from dry land blocks.
            int reach = Math.Max(0, options.CausewayMax / 2 / Step);
            var dist = new int[w * w];
            var queue = new Queue<int>();
            for (int i = 0; i < w * w; i++)
            {
                if ((Flags[i] & Water) == 0) queue.Enqueue(i);
                else dist[i] = int.MaxValue;
            }
            while (queue.TryDequeue(out int i))
            {
                int cx = i % w, cz = i / w;
                foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int nx = cx + dx, nz = cz + dz;
                    if ((uint)nx >= (uint)w || (uint)nz >= (uint)w) continue;
                    int n = nx + nz * w;
                    if (dist[n] <= dist[i] + 1) continue;
                    dist[n] = dist[i] + 1;
                    queue.Enqueue(n);
                }
            }
            for (int i = 0; i < w * w; i++)
                if ((Flags[i] & Water) != 0 && dist[i] > reach) Flags[i] |= Blocked;
            if (roads is not null) IndexExistingRoads(roads);
        }

        public int CellOf(Vector2 p) =>
            Math.Clamp((int)(p.X / Step), 0, Width - 1) + Math.Clamp((int)(p.Y / Step), 0, Width - 1) * Width;

        /// <summary>Indexes street tiles for the final centreline check and marks cells too close to route through.</summary>
        public void ProtectStreet(GridRect rect)
        {
            if (rect.IsEmpty) return;
            for (int z = rect.Z0 / StreetBucket; z <= (rect.Z1 - 1) / StreetBucket; z++)
                for (int x = rect.X0 / StreetBucket; x <= (rect.X1 - 1) / StreetBucket; x++)
                {
                    if (!_streets.TryGetValue((x, z), out var tiles)) _streets[(x, z)] = tiles = [];
                    tiles.Add(rect);
                }

            float highway = RoadReach(false), country = RoadReach(true);
            int pad = (int)MathF.Ceiling(highway);
            for (int cz = Math.Max(0, (rect.Z0 - pad) / Step); cz <= Math.Min(Width - 1, (rect.Z1 + pad) / Step); cz++)
                for (int cx = Math.Max(0, (rect.X0 - pad) / Step); cx <= Math.Min(Width - 1, (rect.X1 + pad) / Step); cx++)
                {
                    int cell = cx + cz * Width;
                    var p = Centre(cell);
                    float distance = Math.Max(Math.Max(rect.X0 - p.X, p.X - rect.X1), Math.Max(rect.Z0 - p.Y, p.Y - rect.Z1));
                    if (distance < highway) Flags[cell] |= HighwayClearance;
                    if (distance < country) Flags[cell] |= CountryClearance;
                }
        }

        /// <summary>Checks the drawn segments, since smoothing can move a route inside the clearance of its path cells.</summary>
        public bool ClearsStreets(List<Vector2> line, bool country, Exit from, Exit? to)
        {
            float reach = RoadReach(country);
            float radius = country ? CountryRadius : HighwayRadius;
            for (int i = 1; i < line.Count; i++)
            {
                var a = line[i - 1];
                var b = line[i];
                var lo = Vector2.Min(a, b) - new Vector2(reach);
                var hi = Vector2.Max(a, b) + new Vector2(reach);
                for (int z = Math.Max(0, (int)lo.Y / StreetBucket); z <= (int)hi.Y / StreetBucket; z++)
                    for (int x = Math.Max(0, (int)lo.X / StreetBucket); x <= (int)hi.X / StreetBucket; x++)
                    {
                        if (!_streets.TryGetValue((x, z), out var tiles)) continue;
                        foreach (var tile in tiles)
                        {
                            if (lo.X >= tile.X1 || hi.X <= tile.X0 || lo.Y >= tile.Z1 || hi.Y <= tile.Z0) continue;
                            if (!AtExit(from, tile, a, b) && (to is null || !AtExit(to, tile, a, b))) return false;
                        }
                    }
            }
            return true;

            bool AtExit(Exit exit, GridRect tile, Vector2 a, Vector2 b)
            {
                if (exit.Tile.Rect != tile) return false;
                var outward = Outward(exit.Dir);
                var across = new Vector2(-outward.Y, outward.X);
                a -= exit.Point;
                b -= exit.Point;
                return Vector2.Dot(a, outward) >= 0 && Vector2.Dot(b, outward) >= 0
                    && MathF.Abs(Vector2.Dot(a, across)) <= radius && MathF.Abs(Vector2.Dot(b, across)) <= radius;
            }
        }

        /// <summary>
        /// The ground held at a point (grid metres), or NaN: between the centres of four held cells, blended
        /// bilinearly, so a road climbs evenly between two prefabs' grounds rather than a step per cell (#32);
        /// else its own cell's.
        /// </summary>
        public float HoldAt(Vector2 p)
        {
            float fx = p.X / Step - 0.5f, fz = p.Y / Step - 0.5f;
            int x0 = (int)MathF.Floor(fx), z0 = (int)MathF.Floor(fz);
            if (x0 >= 0 && z0 >= 0 && x0 + 1 < Width && z0 + 1 < Width)
            {
                float a = Hold[x0 + z0 * Width], b = Hold[x0 + 1 + z0 * Width], c = Hold[x0 + (z0 + 1) * Width], d = Hold[x0 + 1 + (z0 + 1) * Width];
                if (!float.IsNaN(a) && !float.IsNaN(b) && !float.IsNaN(c) && !float.IsNaN(d))
                {
                    float tx = fx - x0, tz = fz - z0;
                    return (a * (1 - tx) + b * tx) * (1 - tz) + (c * (1 - tx) + d * tx) * tz;
                }
            }
            return Hold[CellOf(p)];
        }

        /// <summary>
        /// The bare ground's height in metres at a point (grid metres), to the metre rather than the cell's one sample,
        /// or null over water or off the map.
        /// </summary>
        public float? GroundAt(Vector2 p)
        {
            int x = (int)p.X, z = (int)p.Y;
            if (p.X < 0 || p.Y < 0 || x >= _world.Size || z >= _world.Size) return null;
            float h = World.HeightToMetres(_world.Height[x, z]);
            return (_world.Water?[x, z] ?? 0) is var wl and > 0 && h <= wl + 0.5f ? null : h;
        }

        /// <summary>Marks the cells within <see cref="BridgeRamp"/> of a bridge (grid metres) as no place to join a highway.</summary>
        /// <summary>
        /// Marks an area (grid metres) as taken, as a prefab's footprint is, so no road is routed through it and no bridge is
        /// planned across it; with its approaches, as a bridge's are, kept out of joins.
        /// </summary>
        public void KeepClear(GridRect area)
        {
            // Widened by half a deck and a metre: a new bridge is checked along its centre line.
            int pad = BridgeOp.Width / 2 + 1;
            area = new GridRect(area.X0 - pad, area.Z0 - pad, area.X1 + pad, area.Z1 + pad);
            int w = Width;
            for (int cz = Math.Max(0, area.Z0 / Step); cz <= Math.Min(w - 1, (area.Z1 - 1) / Step); cz++)
                for (int cx = Math.Max(0, area.X0 / Step); cx <= Math.Min(w - 1, (area.X1 - 1) / Step); cx++)
                    Flags[cx + cz * w] |= Blocked | Prefab;
            MarkNoJoin(area);
        }

        public void MarkNoJoin(GridRect bridge)
        {
            int w = Width;
            for (int cz = Math.Max(0, (bridge.Z0 - BridgeRamp) / Step); cz <= Math.Min(w - 1, (bridge.Z1 + BridgeRamp) / Step); cz++)
                for (int cx = Math.Max(0, (bridge.X0 - BridgeRamp) / Step); cx <= Math.Min(w - 1, (bridge.X1 + BridgeRamp) / Step); cx++)
                    Flags[cx + cz * w] |= NoJoin;
        }

        private int[]? _land;

        /// <summary>Each dry cell's land mass (cells joined edge to edge), or -1 for water.</summary>
        public int[] Land()
        {
            if (_land is not null) return _land;
            int w = Width;
            var land = new int[w * w];
            Array.Fill(land, -1);
            var stack = new Stack<int>();
            int next = 0;
            for (int start = 0; start < land.Length; start++)
            {
                if (land[start] >= 0 || (Flags[start] & Water) != 0) continue;
                land[start] = next;
                stack.Push(start);
                while (stack.TryPop(out int i))
                {
                    int x = i % w, z = i / w;
                    foreach (int n in new[] { x > 0 ? i - 1 : -1, x < w - 1 ? i + 1 : -1, z > 0 ? i - w : -1, z < w - 1 ? i + w : -1 })
                        if (n >= 0 && land[n] < 0 && (Flags[n] & Water) == 0)
                        {
                            land[n] = next;
                            stack.Push(n);
                        }
                }
                next++;
            }
            return _land = land;
        }

        public Vector2 Centre(int cell) => new(cell % Width * Step + Step / 2f, cell / Width * Step + Step / 2f);
    }

    private static readonly (int X, int Z)[] Neighbours8 = [(0, 1), (1, 1), (1, 0), (1, -1), (0, -1), (-1, -1), (-1, 0), (-1, 1)];

    private sealed class Scratch(int cells)
    {
        public readonly float[] G = new float[cells];
        public readonly int[] Parent = new int[cells];
        public readonly int[] Seen = new int[cells];
        public readonly int[] Closed = new int[cells];
        public int Stamp;
    }

    private static readonly ThreadLocal<Scratch?> Scratches = new();

    /// <summary>RWG's octile distance (PathingUtils.DiagonalDist).</summary>
    private static float Diagonal(int a, int b, int w)
    {
        float dx = MathF.Abs(a % w - b % w), dz = MathF.Abs(a / w - b / w);
        return dx + dz - 0.585786f * MathF.Min(dx, dz);
    }

    /// <summary>
    /// RWG's A* from <paramref name="start"/> to any of <paramref name="goals"/>: 8-way on the 10 m grid, no step
    /// rising more than 11 m (country roads 12 m), height costing extra (highways more), no cutting a blocked corner,
    /// within 2 km of the ends. Extras: the seeded wiggle, cheaper along roads, costlier beside them and over water.
    /// Returns the cells, start first, or null.
    /// </summary>
    internal static List<int>? FindPath(PathGrid grid, int start, IReadOnlyList<int> goals, bool country, bool clearStreets = false) =>
        Search(grid, start, goals, country, clearStreets, !country && grid.HasRoads, out bool exhausted)
        // Keep the original search available for a route whose road-sharing search spends its node budget.
        ?? (exhausted && !country && grid.HasRoads ? Search(grid, start, goals, country, clearStreets, false, out _) : null);

    private static List<int>? Search(PathGrid grid, int start, IReadOnlyList<int> goals, bool country, bool clearStreets,
        bool shareRoads, out bool exhausted)
    {
        exhausted = false;
        if (goals.Count == 0) return null;
        byte clearance = !clearStreets ? (byte)0 : country ? PathGrid.CountryClearance : PathGrid.HighwayClearance;
        if ((grid.Flags[start] & clearance) != 0) return null;
        int w = grid.Width;
        var s = Scratches.Value;
        if (s is null || s.G.Length != w * w) Scratches.Value = s = new Scratch(w * w);
        int stamp = ++s.Stamp;
        var goalSet = goals.ToHashSet();
        int minX = start % w, maxX = minX, minZ = start / w, maxZ = minZ;
        foreach (int g in goals)
        {
            minX = Math.Min(minX, g % w); maxX = Math.Max(maxX, g % w);
            minZ = Math.Min(minZ, g / w); maxZ = Math.Max(maxZ, g / w);
        }
        minX = Math.Max(0, minX - 200); minZ = Math.Max(0, minZ - 200);
        maxX = Math.Min(w - 1, maxX + 200); maxZ = Math.Min(w - 1, maxZ + 200);
        float maxStep = country ? 12 : 11;
        float H(int c)
        {
            float best = float.MaxValue;
            foreach (int g in goals) best = MathF.Min(best, Diagonal(c, g, w));
            return best * (shareRoads ? SharingHeuristic : 1.4f);
        }
        bool Blocked(int c) => (grid.Flags[c] & (PathGrid.Blocked | clearance)) != 0;

        var open = new PriorityQueue<int, float>();
        s.G[start] = 0;
        s.Seen[start] = stamp;
        s.Parent[start] = -1;
        open.Enqueue(start, H(start));
        int budget = 400_000;
        while (open.TryDequeue(out int node, out _))
        {
            if (s.Closed[node] == stamp) continue;
            s.Closed[node] = stamp;
            if (goalSet.Contains(node))
            {
                var path = new List<int>();
                for (int c = node; c >= 0; c = s.Parent[c]) path.Add(c);
                path.Reverse();
                return path;
            }
            if (--budget < 0)
            {
                exhausted = true;
                return null;
            }
            int nx0 = node % w, nz0 = node / w;
            float hNode = grid.Height[node];
            for (int k = 0; k < 8; k++)
            {
                var (dx, dz) = Neighbours8[k];
                int nx = nx0 + dx, nz = nz0 + dz;
                if (nx < minX || nz < minZ || nx > maxX || nz > maxZ) continue;
                int nb = nx + nz * w;
                if (s.Closed[nb] == stamp) continue;
                bool goal = goalSet.Contains(nb);
                if (goal && (grid.Flags[nb] & clearance) != 0) continue;
                if (!goal && Blocked(nb)) continue;
                bool diagonal = dx != 0 && dz != 0;
                // A diagonal step may not cut past a blocked cell.
                if (diagonal && !goal && (Blocked(nx0 + dx + nz0 * w) || Blocked(nx0 + (nz0 + dz) * w))) continue;
                float dh = MathF.Abs(grid.Height[nb] - hNode);
                if (dh > maxStep) continue;
                float heightCost = (country ? dh : MathF.Max(0, dh - 0.5f) * 3) * 0.2f;
                float move = (diagonal ? 1.414214f : 1f) * grid.Wiggle[nb];
                byte f = grid.Flags[nb];
                if (shareRoads) move *= grid.RoadCost(node, nb, k);
                else if (grid.LegacyRoad[nb]) move *= 0.5f;
                if ((f & PathGrid.Water) != 0) move *= 4;
                float g = s.G[node] + move + heightCost;
                if (s.Seen[nb] == stamp && g >= s.G[nb]) continue;
                s.Seen[nb] = stamp;
                s.G[nb] = g;
                s.Parent[nb] = node;
                open.Enqueue(nb, g + H(nb));
            }
        }
        return null;
    }

    // ---- the network ----

    private sealed class Planned(HighwayKind kind, List<Vector2> points, Town? from, Town? to)
    {
        public Planned(HighwayKind kind, RoadRoute road, Town? from, Town? to) : this(kind, road.Points, from, to)
        {
            Line = road.Line;
            Cells = road.Cells;
            FromExit = road.From;
            ToExit = road.To;
        }

        public HighwayKind Kind { get; } = kind;
        public List<Vector2> Points { get; } = points;
        public List<Vector2> Line { get; } = [];
        public Town? From { get; } = from;
        public Town? To { get; } = to;
        public List<int> Cells { get; } = [];
        public Exit? FromExit { get; }
        public Exit? ToExit { get; }
        public bool JoinsRoad { get; init; }
        public bool Country => Kind == HighwayKind.Country;
        public BridgePlan? Bridge { get; init; }
    }

    private sealed record RoadRoute(List<Vector2> Points, List<Vector2> Line, List<int> Cells, float Length, Exit From, Exit? To);

    /// <summary>A route between two town exits.</summary>
    private static RoadRoute? Route(PathGrid grid, Exit from, IReadOnlyList<Exit> to, bool country, out Exit? reached)
    {
        var goals = to.Select(e => grid.CellOf(e.LeadPoint)).ToList();
        var route = FindRoad(grid, from, goals, country, cell =>
        {
            var exit = to[goals.IndexOf(cell)];
            return (exit.LeadPoint, exit.Point, exit);
        });
        reached = route is null ? null : to[goals.IndexOf(route.Cells[^1])];
        return route;
    }

    /// <summary>Keeps the usual route if it clears the tiles. Otherwise reroutes with room for its paint and grading.</summary>
    private static RoadRoute? FindRoad(PathGrid grid, Exit from, IReadOnlyList<int> goals, bool country,
        Func<int, (Vector2 Lead, Vector2 End, Exit? Exit)> finish)
    {
        int start = grid.CellOf(from.LeadPoint);
        if (FindPath(grid, start, goals, country) is not { } cells) return null;
        if (BuildRoute(cells, round: true) is { } usual) return usual;

        var remaining = goals.Distinct().ToList();
        while (FindPath(grid, start, remaining, country, clearStreets: true) is { } clearCells)
        {
            // Keep the path cells near the ends if a wider curve would cut back toward a tile.
            if ((BuildRoute(clearCells, round: true) ?? BuildRoute(clearCells, round: false)) is { } clear) return clear;
            remaining.Remove(clearCells[^1]);
        }
        return null;

        RoadRoute? BuildRoute(List<int> route, bool round)
        {
            var (lead, end, exit) = finish(route[^1]);
            var points = new List<Vector2> { from.Point, from.LeadPoint };
            points.AddRange(round ? Inner(grid, route, from.LeadPoint, lead) : route.Select(grid.Centre));
            points.Add(lead);
            if (lead != end) points.Add(end);
            float length = Length(points);
            var type = country ? RoadType.Gravel : RoadType.Asphalt;
            if (round) RoundOffCorners(points, country);
            var line = RoadOp.Centreline(points, round ? RoadOp.CornerRounding(type) : Step);
            if (grid.ClearsStreets(line, country, from, exit)) return new RoadRoute(points, line, route, length, from, exit);
            if (round) return null;
            line = RoadOp.Centreline(points, 0);
            return grid.ClearsStreets(line, country, from, exit) ? new RoadRoute(points, line, route, length, from, exit) : null;
        }
    }

    /// <summary>The route's cell centres, leaving out those within 40 m of its ends (Path.CreatePath).</summary>
    private static IEnumerable<Vector2> Inner(PathGrid grid, List<int> cells, Vector2 a, Vector2 b) =>
        cells.Select(grid.Centre).Where(p => Vector2.Distance(p, a) >= 40 && Vector2.Distance(p, b) >= 40);

    private static float Length(List<Vector2> points)
    {
        float l = 0;
        for (int i = 1; i < points.Count; i++) l += Vector2.Distance(points[i - 1], points[i]);
        return l;
    }

    private sealed class Groups(int count)
    {
        private readonly int[] _parent = [.. Enumerable.Range(0, count)];
        public int Find(int i) => _parent[i] == i ? i : _parent[i] = Find(_parent[i]);
        public void Join(int a, int b) => _parent[Find(a)] = Find(b);
    }

    private const int NearestTowns = 4;

    /// <summary>Plans every road: RWG's highways, rings and country roads, then the join-up (with bridges, given <paramref name="kit"/>).</summary>
    private static List<Planned> PlanNetwork(PathGrid grid, List<Town> towns, HighwayOptions options, List<string> notes,
        IProgress<double>? progress, CancellationToken cancel, BridgeKit? kit = null)
    {
        var planned = new List<Planned>();
        var groups = new Groups(towns.Count);
        var highwayCells = new Dictionary<int, Town?>();
        void Commit(Planned p)
        {
            var pieces = JoinExistingRoads(grid, p);
            planned.AddRange(pieces);
            foreach (int cell in p.Cells) grid.LegacyRoad[cell] = true;
            if (!p.Country && p.Bridge is null)
                foreach (var piece in pieces)
                    foreach (int cell in grid.IndexRoad(piece.Line))
                        highwayCells.TryAdd(cell, p.From);
            if (p.From is { } a && p.To is { } b)
            {
                groups.Join(a.Id, b.Id);
                a.Links[b] = a.Links.GetValueOrDefault(b) + 1;
                b.Links[a] = b.Links.GetValueOrDefault(a) + 1;
            }
        }

        // RWG: gateway towns, largest first.
        var gatewayTowns = towns.Where(t => t.SpawnGateway).OrderByDescending(t => t.Tiles.Count).ToList();
        for (int ti = 0; ti < gatewayTowns.Count; ti++)
        {
            cancel.ThrowIfCancellationRequested();
            var town = gatewayTowns[ti];
            foreach (var gateway in town.Gateways.ToList())
            {
                var exits = town.Exits.Where(e => e.Tile == gateway).ToList();
                if (exits.Count == 0 || exits.Any(e => e.Used)) continue;
                // The nearest towns it has at most one link to; the cheapest of the first three reachable wins.
                var nearest = gatewayTowns.Where(t => t != town).OrderBy(t => Vector2.DistanceSquared(gateway.ExitPoint(0), t.Centre)).ToList();
                // With one road per pair, only the nearest few towns count: a gateway whose neighbours are all joined
                // already runs a country road instead of a long highway to a far town.
                if (!options.SecondRoad) nearest = nearest.Take(NearestTowns).ToList();
                var candidates = nearest.Where(t => town.Links.GetValueOrDefault(t) <= (options.SecondRoad ? 1 : 0) && t.Exits.Any(e => !e.Used)).ToList();
                (Planned P, Exit From, Exit To, float Cost)? best = null;
                int found = 0;
                for (int start = 0; start < candidates.Count && found < 3 && start < 12; start += 3)
                {
                    var batch = candidates.Skip(start).Take(3).ToList();
                    var results = new (Planned, Exit, Exit, float)?[batch.Count];
                    Parallel.For(0, batch.Count, bi =>
                    {
                        var other = batch[bi];
                        foreach (var from in exits)
                        {
                            var targets = other.Exits.Where(e => !e.Used).OrderBy(e => Vector2.DistanceSquared(e.Point, from.Point)).Take(3).ToList();
                            if (Route(grid, from, targets, false, out var reached) is not { } r || reached is null) continue;
                            float cost = r.Length;
                            if (town.Links.GetValueOrDefault(other) > 0) cost *= town.Tiles.Count <= 1 || other.Tiles.Count <= 1 ? 4 : 1.6f;
                            if (results[bi] is not { } had || cost < had.Item4)
                                results[bi] = (new Planned(HighwayKind.Highway, r, town, other), from, reached, cost);
                        }
                    });
                    foreach (var r in results)
                    {
                        if (r is not { } got) continue;
                        found++;
                        if (best is null || got.Item4 < best.Value.Cost) best = got;
                        if (found >= 3) break;
                    }
                }
                if (best is { } b)
                {
                    b.From.Used = true;
                    b.To.Used = true;
                    Commit(b.P);
                }
            }

            // RWG: a big city's gateways joined in a ring, each to the next clockwise.
            if (town.Big)
            {
                var ring = town.Gateways.OrderBy(g => -MathF.Atan2(g.Rect.Z0 + 75 - town.Centre.Y, g.Rect.X0 + 75 - town.Centre.X)).ToList();
                for (int i = 0; i < ring.Count && ring.Count > 2; i++)
                {
                    var a = town.Exits.Where(e => e.Tile == ring[i]).ToList();
                    var b2 = town.Exits.Where(e => e.Tile == ring[(i + 1) % ring.Count]).ToList();
                    RoadRoute? bestRing = null;
                    foreach (var from in a)
                        if (Route(grid, from, b2, false, out _) is { } r && (bestRing is null || r.Length < bestRing.Length)) bestRing = r;
                    if (bestRing is { } br) Commit(new Planned(HighwayKind.Ring, br, null, null));
                }
            }
            progress?.Report(0.6 * (ti + 1) / Math.Max(1, gatewayTowns.Count));
        }

        // RWG: towns without gateways run a country road from each exit to the nearest highway. A gateway left
        // unjoined does too (RWG removes it instead, which would mean taking down built tiles).
        var dirt = towns.Where(t => !t.SpawnGateway).SelectMany(t => t.Exits.Where(e => !e.Used))
            .Concat(towns.Where(t => t.SpawnGateway).SelectMany(t => t.Gateways.Where(g => !t.Exits.Any(e => e.Tile == g && e.Used))
                .Select(g => t.Exits.FirstOrDefault(e => e.Tile == g)).OfType<Exit>()))
            .ToList();
        int unjoined = 0;
        foreach (var exit in dirt)
        {
            cancel.ThrowIfCancellationRequested();
            if (highwayCells.Count == 0) break;
            var start = exit.LeadPoint;
            // The nearest highway points, a few spread out, as targets.
            var goals = new List<int>();
            foreach (int c in highwayCells.Keys.OrderBy(c => Vector2.DistanceSquared(grid.RoadPoint(c), start)))
            {
                if ((grid.Flags[c] & (PathGrid.NoJoin | PathGrid.Blocked | PathGrid.CountryClearance)) != 0) continue;
                if (goals.Any(g => Vector2.Distance(grid.Centre(g), grid.Centre(c)) < 60)) continue;
                goals.Add(c);
                if (goals.Count >= 6) break;
            }
            if (FindRoad(grid, exit, goals, true, cell =>
                {
                    var point = grid.RoadPoint(cell);
                    return (point, point, null);
                }) is not { } route)
            {
                unjoined++;
                continue;
            }
            exit.Used = true;
            var p = new Planned(HighwayKind.Country, route, exit.Tile.Town, highwayCells[route.Cells[^1]]);
            Commit(p);
        }
        if (unjoined > 0) notes.Add($"{unjoined} town exit{(unjoined == 1 ? "" : "s")} found no way to a highway.");
        progress?.Report(0.75);

        // Extra: join every group of towns to the rest, closest towns first.
        if (options.JoinUp && towns.Count > 1)
        {
            var failed = new HashSet<(int, int)>();
            for (int guard = 0; guard < towns.Count * 2; guard++)
            {
                cancel.ThrowIfCancellationRequested();
                var pairs = (from a in towns
                             from b in towns
                             where a.Id < b.Id && groups.Find(a.Id) != groups.Find(b.Id) && !failed.Contains((a.Id, b.Id))
                             orderby Vector2.DistanceSquared(a.Centre, b.Centre)
                             select (a, b)).Take(1).ToList();
                if (pairs.Count == 0) break;
                var (ta, tb) = pairs[0];
                (RoadRoute Road, Exit From, Exit To)? bestJoin = null;
                foreach (var from in ta.Exits.OrderBy(e => e.Used).ThenBy(e => Vector2.DistanceSquared(e.Point, tb.Centre)).Take(2))
                {
                    var targets = tb.Exits.OrderBy(e => e.Used).ThenBy(e => Vector2.DistanceSquared(e.Point, from.Point)).Take(3).ToList();
                    if (Route(grid, from, targets, false, out var reached) is { } r && reached is not null && (bestJoin is null || r.Length < bestJoin.Value.Road.Length))
                        bestJoin = (r, from, reached);
                }
                if (bestJoin is not { } j)
                {
                    // Towns on separate islands: a bridge, and a road from each town to its end.
                    if (kit is not null && PlanBridge(grid, kit, ta, tb, options) is { } bridged)
                    {
                        foreach (var p in bridged) Commit(p);
                        continue;
                    }
                    failed.Add((ta.Id, tb.Id));
                    continue;
                }
                j.From.Used = true;
                j.To.Used = true;
                Commit(new Planned(HighwayKind.JoinUp, j.Road, ta, tb));
            }
            if (kit is not null && options.BridgeShortcuts)
            {
                int shortcuts = AddShortcuts(grid, kit, towns, planned, options, Commit, cancel);
                if (shortcuts > 0)
                    notes.Add($"Added {shortcuts} shortcut bridge{(shortcuts == 1 ? "" : "s")} where the way round took {ShortcutHops} or more bridges.");
            }
            int cut = towns.Select(t => groups.Find(t.Id)).Distinct().Count();
            if (cut > 1)
                notes.Add($"The towns are still in {cut} groups no road could join (water, mountains or the edge in the way)."
                          + (kit is not null ? $" Bridges run straight north-south or east-west, up to {options.BridgeMax} m over water." : ""));
        }
        progress?.Report(0.8);
        return planned;
    }

    // ---- drawing, with RWG's height profile ----

    /// <summary>
    /// RWG's road heights (Path.ProcessPath) on the editor's centreline: the ground, points over the route's average
    /// + 8 m pulled 30% toward it (highways), then smoothed (50 passes at RWG's 10 m spacing for highways, 4 for
    /// country roads), keeping to a prefab's ground in and within 20 m of it. Plus the editor's: at least 1 m over water on a
    /// causeway, and whole-metre flats beside prefabs.
    /// </summary>
    internal static float[] RwgProfile(World world, List<Vector2> line, bool country, PathGrid grid, bool joinsRoad = false)
    {
        var (heights, floor) = RoadOp.GroundWithCauseways(world, line);
        var held = line.Select(grid.HoldAt).ToArray();
        // Use the existing pavement's height where a road joins or crosses it (#34).
        var pavement = new bool[line.Count];
        if ((country || joinsRoad) && world.Roads is { } roads)
            for (int i = 0; i < line.Count; i++)
            {
                int x = Math.Clamp((int)line[i].X, 0, world.Size - 1), z = Math.Clamp((int)line[i].Y, 0, world.Size - 1);
                if (!IsRoad(roads[x, z])) continue;
                held[i] = World.HeightToMetres(world.Height[x, z]);
                pavement[i] = true;
            }
        var hold = held.Select(h => !float.IsNaN(h)).ToArray();
        for (int i = 0; i < heights.Length; i++)
            if (hold[i]) heights[i] = Math.Max(held[i], floor[i]);
        if (!country && heights.Length > 0)
        {
            float cap = heights.Average() + 8;
            for (int i = 0; i < heights.Length; i++)
                if (heights[i] > cap && !hold[i]) heights[i] = cap * 0.3f + heights[i] * 0.7f;
        }
        // RWG smooths path points 10 m apart; the centreline's are 4 m apart, so the same spread takes (10/4)² as many passes.
        float ratio = Step / RoadOp.SampleSpacing;
        int passes = (int)MathF.Round((country ? 4 : 50) * ratio * ratio);
        var copy = new float[heights.Length];
        for (int pass = 0; pass < passes; pass++)
        {
            heights.CopyTo(copy, 0);
            for (int i = 1; i < heights.Length - 1; i++)
                if (!hold[i]) heights[i] = Math.Max((copy[i - 1] + copy[i] + copy[i + 1]) / 3f, floor[i]);
        }
        RoadOp.LevelFlats(heights, line, floor);
        for (int i = 0; i < heights.Length; i++)
            if (pavement[i]) heights[i] = held[i];
        return heights;
    }

    /// <summary>RWG's Path.RoundOffCorners, four times, on the route's control points.</summary>
    internal static void RoundOffCorners(List<Vector2> points, bool country)
    {
        float weight = country ? 0.3f : 0.4f;
        int skip = (country ? 0 : 2) + 1;
        for (int pass = 0; pass < 4; pass++)
            for (int i = skip; i < points.Count - skip; i++)
                points[i] = points[i] * (1 - weight) + (points[i - 1] + points[i + 1]) * (weight / 2);
    }

    /// <summary>
    /// Plans and draws every highway as part of <paramref name="session"/>, after taking out the last run's.
    /// <paramref name="roads"/> is updated in place and is part of the undo step.
    /// </summary>
    /// <param name="keepClear">
    /// Areas (grid metres) no new road or bridge may cross, though nothing stands there now: the Bridge tool's bridges and
    /// their ramps (<see cref="BridgeTool.KeepClearArea"/>), which are taken down while the stage changes the ground and built
    /// again after it.
    /// </param>
    public static HighwayResult Run(World world, HighwayOptions options, List<HighwayRoad> roads, PrefabLibrary library, RwgMixer mixer,
        IEnumerable<TownSite> sites, EditSession session, IProgress<double>? progress = null, CancellationToken cancel = default,
        IReadOnlyList<GridRect>? keepClear = null)
    {
        session.TouchList(roads, r => r.Clone(), new GridRect(0, 0, world.Size, world.Size));
        int removed = TakeDown(world, roads, session);
        var notes = new List<string>();
        var towns = ReadTowns(world, library, mixer, sites);
        if (towns.Count == 0)
        {
            notes.Add("There are no towns to join: build towns first.");
            return new HighwayResult([], notes, removed);
        }
        var grid = new PathGrid(world, library, options);
        foreach (var area in keepClear ?? []) grid.KeepClear(area);
        int size = world.Size;
        var streetCells = new BitArray(world.Height.Data.Length);
        foreach (var prefab in world.Prefabs)
        {
            if (library.Find(prefab.Name) is not { IsStreetTile: true } info) continue;
            var rect = PrefabPlacer.Footprint(world, info, prefab.Position.X, prefab.Position.Z, prefab.Rotation).Clamp(size, size);
            grid.ProtectStreet(rect);
            for (int z = rect.Z0; z < rect.Z1; z++)
                for (int x = rect.X0; x < rect.X1; x++)
                    streetCells[x + z * size] = true;
        }
        BridgeKit? kit = null;
        if (options.Bridges)
        {
            kit = BridgeKit.From(library);
            if (kit is null) notes.Add($"No bridges: the game's {BridgeTile} and {BridgeEnd} prefabs weren't found. Check the game folder.");
        }
        var planned = PlanNetwork(grid, towns, options, notes, progress, cancel, kit);

        var added = new List<HighwayRoad>();
        // Bridges first, so the roads to them are drawn up to their decks.
        foreach (var p in planned.Where(p => p.Bridge is not null))
            added.Add(BridgeOp.Build(world, options.DeckWrecks ? p.Bridge! with { WreckSeed = options.Seed } : p.Bridge!, session));
        // Highways first, then country roads, so asphalt is down where gravel meets it.
        var order = planned.Where(p => !p.Country && p.Bridge is null).Concat(planned.Where(p => p.Country)).ToList();
        var exits = towns.SelectMany(t => t.Exits).ToLookup(e => e.Point, e => e.Tile.Rect);
        for (int i = 0; i < order.Count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            var p = order[i];
            var type = p.Country ? RoadType.Gravel : RoadType.Asphalt;
            float radius = p.Country ? CountryRadius : HighwayRadius;
            var path = new RoadPath(p.Points, type, radius);
            var road = new HighwayRoad { Kind = p.Kind, Type = type, Radius = radius, Points = [.. p.Points.Select(v => new[] { v.X, v.Y })] };
            var line = p.Line;
            var profile = RwgProfile(world, line, p.Country, grid, p.JoinsRoad);
            var previousPaint = world.Roads;
            var start = line[0];
            var end = line[^1];
            var startTiles = exits[start];
            var endTiles = exits[end];
            // Keep the tile's ground and only paint the road's end cap at its street opening.
            (bool Paint, bool Grade) CellEdits(int index)
            {
                if (!streetCells[index])
                {
                    byte existing = previousPaint?.Data[index] ?? 0;
                    // Keep the existing road's grade. New asphalt covers its gravel shoulder at the join.
                    return p.JoinsRoad && IsRoad(existing) ? (existing != (byte)RoadType.Asphalt, false) : (true, true);
                }
                int x = index % size, z = index / size;
                var centre = new Vector2(x + 0.5f, z + 0.5f);
                bool paint = (Vector2.DistanceSquared(centre, start) <= radius * radius && startTiles.Any(r => r.Contains(x, z)))
                    || (Vector2.DistanceSquared(centre, end) <= radius * radius && endTiles.Any(r => r.Contains(x, z)));
                return (paint, false);
            }
            road.Ground = Capture(world, path, line, () => RoadOp.Draw(world, path, line, profile, session, library, CellEdits));
            added.Add(road);
            progress?.Report(0.8 + 0.2 * (i + 1) / order.Count);
        }
        roads.AddRange(added);
        return new HighwayResult(added, notes, removed);
    }

    /// <summary>
    /// Takes out every road in <paramref name="roads"/>, newest first, putting back the ground and road pixels each
    /// replaced. Returns how many came out.
    /// </summary>
    public static int TakeDown(World world, List<HighwayRoad> roads, EditSession session)
    {
        if (roads.Count == 0) return 0;
        session.TouchList(roads, r => r.Clone(), new GridRect(0, 0, world.Size, world.Size));
        int n = roads.Count;
        for (int i = roads.Count - 1; i >= 0; i--) Restore(world, roads[i].Ground, session);

        // A bridge's pieces: each takes out one prefab that matches it exactly.
        var wanted = roads.SelectMany(r => r.Prefabs).ToList();
        if (wanted.Count > 0)
        {
            session.TouchList(world.Prefabs, p => p.Clone(), new GridRect(0, 0, world.Size, world.Size));
            var gone = new HashSet<PrefabInstance>();
            foreach (var w in wanted)
                if (world.Prefabs.FirstOrDefault(p => !gone.Contains(p) && w.Matches(p)) is { } hit) gone.Add(hit);
            world.Prefabs.RemoveAll(gone.Contains);
        }
        roads.Clear();
        return n;
    }

    // ---- bridges between islands ----

    /// <summary>The game's modular bridge pieces and sizes; the bridge itself is built by <see cref="BridgeOp"/>.</summary>
    public const string BridgeTile = BridgeOp.Tile, BridgeEnd = BridgeOp.End;

    /// <summary>A bridge section's length and width, in metres.</summary>
    public const int BridgePiece = BridgeOp.Piece, BridgeWidth = BridgeOp.Width;

    /// <summary>Heights in a bridge section, from its bottom (see <see cref="BridgeOp"/>).</summary>
    public const int BridgeBeams = BridgeOp.Beams, BridgeDeck = BridgeOp.Deck;

    /// <summary>Room between the water and the underside of a bridge's beams, for boats.</summary>
    public const int BridgeClearance = BridgeOp.Clearance;

    /// <summary>How far each end reaches past the water onto land, and the straight run a road makes onto it.</summary>
    private const int BridgeOverlap = 12, BridgeLead = 40;

    /// <summary>
    /// How far out from a bridge end, in metres, an approach road runs dead straight: the route is found to this point and
    /// only turns there. The drawn road rounds each corner over about 60 m (<see cref="RoadOp.CornerRounding"/>), and a
    /// corner at <see cref="BridgeLead"/> pulled the road off the centre line by 8 m or more inside the lead, onto
    /// whatever hillside lay beside it. At 50 m past the lead the rounding is down to a metre.
    /// </summary>
    private const int ApproachRun = BridgeLead + 50;

    /// <summary>
    /// The best straight crossing between the land masses of <paramref name="ta"/> and <paramref name="tb"/>, with a
    /// road from each town to its end: short over water first, then close to the towns. Null if none is in reach.
    /// </summary>
    private static List<Planned>? PlanBridge(PathGrid grid, BridgeKit kit, Town ta, Town tb, HighwayOptions options)
    {
        var land = grid.Land();
        int la = land[grid.CellOf(ta.Centre)], lb = land[grid.CellOf(tb.Centre)];
        if (la < 0 || lb < 0 || la == lb) return null;
        int w = grid.Width, maxCells = Math.Max(1, options.BridgeMax / Step);
        int margin = 6;

        // Every straight run of water from one land mass to the other, along both axes, scored.
        var found = new List<(float Score, bool AlongZ, int Line, int Lo, int Hi, bool LoIsA)>();
        foreach (bool alongZ in new[] { true, false })
            for (int line = margin; line < w - margin; line++)
            {
                int Cell(int along, int across) => alongZ ? across + along * w : along + across * w;
                int lastDry = -1, lastLand = -1;
                for (int t = 0; t < w; t++)
                {
                    int c = Cell(t, line);
                    if ((grid.Flags[c] & PathGrid.Water) != 0) continue;
                    int here = land[c];
                    int span = t - lastDry - 1;
                    if (lastDry >= margin && t < w - margin && span >= 1 && span <= maxCells
                        && (lastLand == la && here == lb || lastLand == lb && here == la)
                        && Open(lastDry, t))
                    {
                        bool loIsA = lastLand == la;
                        var lo = grid.Centre(Cell(lastDry, line));
                        var hi = grid.Centre(Cell(t, line));
                        float score = span * Step * 3 + Vector2.Distance(loIsA ? lo : hi, ta.Centre) + Vector2.Distance(loIsA ? hi : lo, tb.Centre);
                        found.Add((score, alongZ, line, lastDry, t, loIsA));
                    }
                    lastDry = t;
                    lastLand = here;
                }

                // The bridge's full width stays over water between its ends, and its ends are clear of prefabs.
                bool Open(int lo, int hi)
                {
                    for (int t = lo; t <= hi; t++)
                        for (int d = -1; d <= 1; d++)
                        {
                            int c = Cell(t, line + d);
                            if ((grid.Flags[c] & PathGrid.Prefab) != 0) return false;
                            if (t > lo + 1 && t < hi - 1 && (grid.Flags[c] & PathGrid.Water) == 0) return false;
                        }
                    return true;
                }
            }

        // The roads to a crossing are slow to find, so only six crossings with room to ramp onto get that far.
        int tries = 0;
        foreach (var (_, alongZ, line, lo, hi, loIsA) in found.OrderBy(f => f.Score))
        {
            if (tries == 6) break;
            int centre = line * Step + Step / 2;
            // The water's edges at the bridge's middle line, to the metre; the ends reach onto land past them.
            int shoreLo = (lo + 1) * Step, shoreHi = hi * Step;
            int length = shoreHi - shoreLo + 2 * BridgeOverlap;
            int pieces = Math.Max(2, (length + BridgePiece - 1) / BridgePiece);
            int start = (shoreLo + shoreHi) / 2 - pieces * BridgePiece / 2;
            var plan = new BridgePlan(kit, alongZ, centre, start, pieces, StampOp.DefaultWaterLevel + BridgeClearance - BridgeBeams);

            Vector2 Outward(bool low) => alongZ ? new(0, low ? -1 : 1) : new(low ? -1 : 1, 0);
            Vector2 EndPoint(bool low) => plan.At(low ? plan.Start : plan.End);
            if (OnRoad(grid, plan)) continue;
            if (!RampRoom(grid, plan, EndPoint(true), Outward(true)) || !RampRoom(grid, plan, EndPoint(false), Outward(false))) continue;
            tries++;
            var approachA = Approach(grid, ta, EndPoint(loIsA), EndPoint(loIsA) + Outward(loIsA) * ApproachRun);
            if (approachA is null) continue;
            var approachB = Approach(grid, tb, EndPoint(!loIsA), EndPoint(!loIsA) + Outward(!loIsA) * ApproachRun);
            if (approachB is null)
            {
                approachA.Value.Exit.Used = false;
                continue;
            }

            // Roads meet the deck: the bridge and a little past its ends keep them at its height. Other roads keep off
            // it, and none joins the highway on its ramps.
            var rect = plan.Rect;
            grid.MarkNoJoin(rect);
            var held = AlongZ(plan) ? new GridRect(rect.X0, rect.Z0 - DeckHold, rect.X1, rect.Z1 + DeckHold) : new GridRect(rect.X0 - DeckHold, rect.Z0, rect.X1 + DeckHold, rect.Z1);
            for (int cz = Math.Max(0, held.Z0 / Step); cz <= Math.Min(w - 1, (held.Z1 - 1) / Step); cz++)
                for (int cx = Math.Max(0, held.X0 / Step); cx <= Math.Min(w - 1, (held.X1 - 1) / Step); cx++)
                {
                    int c = cx + cz * w;
                    grid.Hold[c] = plan.DeckTop;
                    // Its cells count as a prefab, so no later bridge crosses it.
                    if (cx * Step >= rect.X0 - Step && cx * Step < rect.X1 && cz * Step >= rect.Z0 - Step && cz * Step < rect.Z1)
                        grid.Flags[c] |= PathGrid.Blocked | PathGrid.Prefab;
                }
            return
            [
                new Planned(HighwayKind.Bridge, [EndPoint(true), EndPoint(false)], ta, tb) { Bridge = plan },
                new Planned(HighwayKind.JoinUp, approachA.Value.Road, null, null),
                new Planned(HighwayKind.JoinUp, approachB.Value.Road, null, null),
            ];
        }
        return null;

        static bool AlongZ(BridgePlan p) => p.AlongZ;
    }

    /// <summary>
    /// Whether a road planned before the bridge (a highway between towns, a country road) runs within a cell of its deck
    /// or the stretch past its ends that roads keep to its height. Highways are planned before bridges, so a bridge
    /// landing on one would lift it to the deck and run it through the bridge's sides.
    /// </summary>
    private static bool OnRoad(PathGrid grid, BridgePlan plan)
    {
        var rect = plan.Rect;
        var near = plan.AlongZ
            ? new GridRect(rect.X0 - Step, rect.Z0 - DeckHold - Step, rect.X1 + Step, rect.Z1 + DeckHold + Step)
            : new GridRect(rect.X0 - DeckHold - Step, rect.Z0 - Step, rect.X1 + DeckHold + Step, rect.Z1 + Step);
        int w = grid.Width;
        for (int cz = Math.Max(0, near.Z0 / Step); cz <= Math.Min(w - 1, (near.Z1 - 1) / Step); cz++)
            for (int cx = Math.Max(0, near.X0 / Step); cx <= Math.Min(w - 1, (near.X1 - 1) / Step); cx++)
                if ((grid.Flags[cx + cz * w] & PathGrid.Road) != 0) return true;
        return false;
    }

    /// <summary>The run, in metres, a road off a bridge takes to climb or drop each metre: 1 in 8.</summary>
    public const int RampRun = 8;

    /// <summary>How far past a bridge's end, in metres, roads keep to its deck (the cells <see cref="PlanBridge"/> holds).</summary>
    private const int DeckHold = 10;

    /// <summary>
    /// How far, in metres, the bare ground under a bridge's straight lead may sit over the deck's 1 in
    /// <see cref="RampRun"/> slope. Shore ground on crossings that ramp fine rises by about that slope or a little
    /// more, so 2 m lets a gentle rise pass while a hillside (about 1 in 2) is over it within a few metres of the hold.
    /// </summary>
    private const float GroundAbove = 2f;

    /// <summary>
    /// How far, in metres, bare ground may sit under the deck's slope: the deck's height over the water and a little
    /// more. Dry shore is never lower than the water, and the road may leave the deck level and drop to it, so the
    /// usual shore (6 to 7 m under the deck) passes and a pit does not.
    /// </summary>
    private const float GroundBelow = 8f;

    /// <summary>
    /// Whether the road off a bridge end has room to reach the ground it will meet. No prefab on the straight
    /// <see cref="ApproachRun"/> onto the deck. Held ground (a town's or POI's, which roads don't regrade) ahead of the
    /// end, to <see cref="BridgeLead"/> either side where the road may turn off, no further from the deck's height than a
    /// 1 in <see cref="RampRun"/> slope from where the deck's hold stops. And the bare ground under the road's straight
    /// lead, as wide as the bridge and as long as the held ground reaches, no further from the deck's height than that
    /// slope, to <see cref="GroundAbove"/> over and <see cref="GroundBelow"/> under. The road follows bare ground and
    /// has no grade cap, so a crossing at the foot of a hill would climb it steeply, or drop off a cliff.
    /// </summary>
    private static bool RampRoom(PathGrid grid, BridgePlan plan, Vector2 end, Vector2 outward)
    {
        var across = new Vector2(outward.Y, outward.X);
        for (int along = 0; along <= ApproachRun + PathGrid.HoldReach; along += Step / 2)
            for (int side = -BridgeLead; side <= BridgeLead; side += Step / 2)
            {
                var at = end + outward * along + across * side;
                if (at.X < 0 || at.Y < 0 || at.X >= grid.Width * Step || at.Y >= grid.Width * Step) continue;
                int c = grid.CellOf(at);
                if (along <= ApproachRun && Math.Abs(side) <= BridgeWidth && (grid.Flags[c] & PathGrid.Prefab) != 0) return false;
                float room = Math.Max(0, Vector2.Distance(at, end) - DeckHold) / RampRun;
                float held = grid.Hold[c];
                if (!float.IsNaN(held))
                {
                    if (Math.Abs(held - plan.DeckTop) > room + 0.5f) return false;
                    continue;
                }
                if (Math.Abs(side) <= BridgeWidth / 2 && grid.GroundAt(at) is { } ground
                    && (ground - plan.DeckTop > room + GroundAbove || plan.DeckTop - ground > room + GroundBelow)) return false;
            }
        return true;
    }

    /// <summary>A shortcut bridge is added between two islands whose way round takes this many bridges or more.</summary>
    public const int ShortcutHops = 3;

    /// <summary>
    /// Bridges islands (land masses with towns) whose trip over the bridges already planned takes
    /// <see cref="ShortcutHops"/> or more, nearest pairs first, recounting after each. Returns how many it added.
    /// </summary>
    private static int AddShortcuts(PathGrid grid, BridgeKit kit, List<Town> towns, List<Planned> planned, HighwayOptions options,
        Action<Planned> commit, CancellationToken cancel)
    {
        var land = grid.Land();
        var townsOn = towns.GroupBy(t => land[grid.CellOf(t.Centre)]).Where(g => g.Key >= 0).ToDictionary(g => g.Key, g => g.ToList());
        var islands = townsOn.Keys.Order().ToList();
        if (islands.Count < 3) return 0;

        // The closest pair of towns between each two islands, and how far apart they are.
        var pairs = new List<(int A, int B, Town Ta, Town Tb, float Distance)>();
        for (int i = 0; i < islands.Count; i++)
            for (int j = i + 1; j < islands.Count; j++)
            {
                var best = (from ta in townsOn[islands[i]] from tb in townsOn[islands[j]]
                            orderby Vector2.DistanceSquared(ta.Centre, tb.Centre) select (ta, tb)).First();
                pairs.Add((islands[i], islands[j], best.ta, best.tb, Vector2.Distance(best.ta.Centre, best.tb.Centre)));
            }

        var failed = new HashSet<(int, int)>();
        int added = 0;
        while (true)
        {
            cancel.ThrowIfCancellationRequested();
            var links = IslandLinks(grid, planned);
            var hops = islands.ToDictionary(a => a, a => Hops(links, a));
            var pick = pairs
                .Where(p => !failed.Contains((p.A, p.B)) && hops[p.A].TryGetValue(p.B, out int h) && h >= ShortcutHops)
                .OrderBy(p => p.Distance)
                .Select(p => ((int A, int B, Town Ta, Town Tb, float Distance)?)p)
                .FirstOrDefault();
            if (pick is not { } next) return added;
            if (PlanBridge(grid, kit, next.Ta, next.Tb, options) is { } bridged)
            {
                foreach (var p in bridged) commit(p);
                added++;
            }
            else failed.Add((next.A, next.B));
        }
    }

    /// <summary>
    /// Which islands the planned roads link: a bridge links its two towns' islands (one hop); a road crossing a little
    /// water on a causeway links the land it runs between at no hop.
    /// </summary>
    private static Dictionary<int, List<(int To, int Hop)>> IslandLinks(PathGrid grid, List<Planned> planned)
    {
        var land = grid.Land();
        var links = new Dictionary<int, List<(int, int)>>();
        void Link(int a, int b, int hop)
        {
            if (a < 0 || b < 0 || a == b) return;
            (links.TryGetValue(a, out var la) ? la : links[a] = []).Add((b, hop));
            (links.TryGetValue(b, out var lb) ? lb : links[b] = []).Add((a, hop));
        }
        foreach (var p in planned)
        {
            if (p.Bridge is not null)
            {
                if (p.From is { } a && p.To is { } b) Link(land[grid.CellOf(a.Centre)], land[grid.CellOf(b.Centre)], 1);
                continue;
            }
            int last = -1;
            foreach (int c in p.Cells)
            {
                if (land[c] < 0) continue;
                Link(last, land[c], 0);
                last = land[c];
            }
        }
        return links;
    }

    /// <summary>The fewest bridges from <paramref name="from"/> to each island it reaches.</summary>
    private static Dictionary<int, int> Hops(Dictionary<int, List<(int To, int Hop)>> links, int from)
    {
        var best = new Dictionary<int, int> { [from] = 0 };
        var queue = new LinkedList<int>([from]);
        while (queue.First is { } first)
        {
            queue.RemoveFirst();
            int at = first.Value;
            if (!links.TryGetValue(at, out var next)) continue;
            foreach (var (to, hop) in next)
            {
                int d = best[at] + hop;
                if (best.TryGetValue(to, out int had) && had <= d) continue;
                best[to] = d;
                if (hop == 0) queue.AddFirst(to);
                else queue.AddLast(to);
            }
        }
        return best;
    }

    /// <summary>A road from the town's nearest open exit to a bridge end, the last stretch straight onto it.</summary>
    private static (RoadRoute Road, Exit Exit)? Approach(PathGrid grid, Town town, Vector2 end, Vector2 lead)
    {
        foreach (var exit in town.Exits.OrderBy(e => e.Used).ThenBy(e => Vector2.DistanceSquared(e.Point, lead)).Take(3))
        {
            if (FindRoad(grid, exit, [grid.CellOf(lead)], false, _ => (lead, end, null)) is not { } road) continue;
            exit.Used = true;
            return (road, exit);
        }
        return null;
    }

    // ---- the ground a road replaced ----

    private const int Block = 64;

    /// <summary>
    /// Runs <paramref name="draw"/> and returns what it changed, compressed: as row runs, every height cell that
    /// differs afterwards with the change that undoes it, and every road cell with its value before.
    /// </summary>
    internal static byte[] Capture(World world, RoadPath road, Action draw) =>
        Capture(world, road, RoadOp.Centreline(road.Points, RoadOp.CornerRounding(road.Type)), draw);

    private static byte[] Capture(World world, RoadPath road, List<Vector2> line, Action draw)
    {
        float reach = road.MaxReach + 2;
        return Capture(world, line.Select(p => new GridRect((int)(p.X - reach), (int)(p.Y - reach), (int)(p.X + reach) + 1, (int)(p.Y + reach) + 1)), draw);
    }

    /// <summary>As the road version, for whatever <paramref name="draw"/> changes inside <paramref name="areas"/> (grid metres).</summary>
    internal static byte[] Capture(World world, IEnumerable<GridRect> areas, Action draw)
    {
        world.Roads ??= new Grid<byte>(world.Size, world.Size);
        int size = world.Size, across = (size + Block - 1) / Block;
        var blocks = new SortedSet<int>();
        foreach (var a in areas)
        {
            var c = a.Clamp(size, size);
            if (c.IsEmpty) continue;
            for (int bz = c.Z0 / Block; bz <= (c.Z1 - 1) / Block; bz++)
                for (int bx = c.X0 / Block; bx <= (c.X1 - 1) / Block; bx++)
                    blocks.Add(bx + bz * across);
        }

        GridRect RectOf(int b) => new GridRect(b % across * Block, b / across * Block, b % across * Block + Block, b / across * Block + Block).Clamp(size, size);
        var heightBefore = new Dictionary<int, ushort[]>();
        var roadBefore = new Dictionary<int, byte[]>();
        foreach (int b in blocks)
        {
            var r = RectOf(b);
            var hb = new ushort[(r.X1 - r.X0) * (r.Z1 - r.Z0)];
            var rb = new byte[hb.Length];
            for (int z = r.Z0; z < r.Z1; z++)
            {
                world.Height.Row(z)[r.X0..r.X1].CopyTo(hb.AsSpan((z - r.Z0) * (r.X1 - r.X0)));
                world.Roads.Row(z)[r.X0..r.X1].CopyTo(rb.AsSpan((z - r.Z0) * (r.X1 - r.X0)));
            }
            heightBefore[b] = hb;
            roadBefore[b] = rb;
        }

        draw();

        using var ms = new MemoryStream();
        using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        using (var w = new BinaryWriter(deflate))
        {
            foreach (bool heights in new[] { true, false })
                foreach (int b in blocks)
                {
                    var r = RectOf(b);
                    int width = r.X1 - r.X0;
                    for (int z = r.Z0; z < r.Z1; z++)
                    {
                        int x = r.X0;
                        while (x < r.X1)
                        {
                            int o = (z - r.Z0) * width + x - r.X0;
                            bool changed = heights ? world.Height[x, z] != heightBefore[b][o] : world.Roads[x, z] != roadBefore[b][o];
                            if (!changed) { x++; continue; }
                            int x0 = x;
                            while (x < r.X1 && (heights ? world.Height[x, z] != heightBefore[b][(z - r.Z0) * width + x - r.X0]
                                       : world.Roads[x, z] != roadBefore[b][(z - r.Z0) * width + x - r.X0])) x++;
                            // Heights as the change to undo, which is small along a road and packs well (kind 1, 16 bits).
                            // A change too big for 16 bits (over about 127 m, a deep cut or a tall footing) is written
                            // as kind 3, 32 bits, so it comes back exactly; saves from before #22 only hold kinds 1 and 2.
                            bool wide = heights && Enumerable.Range(x0, x - x0).Any(i =>
                                heightBefore[b][(z - r.Z0) * width + i - r.X0] - world.Height[i, z] is < short.MinValue or > short.MaxValue);
                            w.Write(heights ? (wide ? (byte)3 : (byte)1) : (byte)2);
                            w.Write(z);
                            w.Write(x0);
                            w.Write(x - x0);
                            for (int i = x0; i < x; i++)
                            {
                                int oi = (z - r.Z0) * width + i - r.X0;
                                int delta = heightBefore[b][oi] - world.Height[i, z];
                                if (!heights) w.Write(roadBefore[b][oi]);
                                else if (wide) w.Write(delta);
                                else w.Write((short)delta);
                            }
                        }
                    }
                }
            w.Write((byte)0);
        }
        return ms.ToArray();
    }

    /// <summary>Writes back what <see cref="Capture"/> recorded.</summary>
    internal static void Restore(World world, byte[] ground, EditSession session)
    {
        if (ground.Length == 0) return;
        world.Roads ??= new Grid<byte>(world.Size, world.Size);
        using var r = new BinaryReader(new DeflateStream(new MemoryStream(ground), CompressionMode.Decompress));
        while (true)
        {
            byte kind = r.ReadByte();
            if (kind == 0) break;
            int z = r.ReadInt32(), x0 = r.ReadInt32(), n = r.ReadInt32();
            var rect = new GridRect(x0, z, x0 + n, z + 1);
            if (kind is 1 or 3)
            {
                session.Touch(world.Height, rect);
                var row = world.Height.Row(z);
                for (int i = 0; i < n; i++)
                    row[x0 + i] = (ushort)Math.Clamp(row[x0 + i] + (kind == 1 ? r.ReadInt16() : r.ReadInt32()), 0, ushort.MaxValue);
            }
            else
            {
                session.Touch(world.Roads, rect);
                var row = world.Roads.Row(z);
                for (int i = 0; i < n; i++) row[x0 + i] = r.ReadByte();
            }
        }
    }
}
