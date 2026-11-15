using System.Numerics;
using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Generate;

/// <summary>The spawns stage's settings.</summary>
public sealed class SpawnOptions
{
    public int Seed { get; set; } = 1;

    /// <summary>Biome tags spawns may go in, each needing a trader of its own biome nearby; empty for any biome and any trader.</summary>
    public List<string> Biomes { get; set; } = ["forest"];

    /// <summary>The fewest spawns the world ends up with (spawns already there count); RWG's is 12.</summary>
    public int Min { get; set; } = 12;

    /// <summary>The most spawns the world may have (spawns already there count); 0 for no limit, as RWG.</summary>
    public int Max { get; set; }

    /// <summary>How far, in metres, a spawn by a gravel road may be from a trader; RWG's is 900.</summary>
    public int TraderDistance { get; set; } = 900;

    /// <summary>
    /// Spawns by the trader the opening quest sends new players to (<see cref="StarterTrader"/>), instead of by any
    /// trader of their biome: road spawns within the trader distance of it, top-up spawns nearest it, all inside
    /// <see cref="StarterTrader.FarSpawn"/> (where validation warns). On for new
    /// projects; projects saved before format 4 load with it off.
    /// </summary>
    public bool NearStarterTrader { get; set; } = true;

    public SpawnOptions Clone() => new()
    {
        Seed = Seed, Biomes = [.. Biomes], Min = Min, Max = Max, TraderDistance = TraderDistance, NearStarterTrader = NearStarterTrader,
    };
}

/// <summary>A spawn the stage put in the world, to find it again when taking it out.</summary>
public sealed class PlacedSpawn
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float RotationY { get; set; }

    /// <summary>Whether it came from a gravel road (else from the top-up).</summary>
    public bool ByRoad { get; set; }

    public bool Matches(SpawnPoint s) => s.X == X && s.Y == Y && s.Z == Z && s.RotationY == RotationY;

    public PlacedSpawn Clone() => (PlacedSpawn)MemberwiseClone();
}

/// <summary>What a spawns run did.</summary>
public sealed record SpawnResult(int ByRoad, int TopUp, int Removed, int Kept, int Traders, List<string> Notes)
{
    public int Added => ByRoad + TopUp;
}

/// <summary>
/// RWG's player spawns (WildernessPathPlanner.createTraderSpawnIfAble, WorldBuilder.CreatePlayerSpawn and the
/// top-up after the wilderness roads). Along each gravel road of the wilderness stage, points 2 to n-3 get a spawn
/// where the point is in a ticked biome, its 150 m cell holds prefabs that are all tier 0 or 1, a trader of the same
/// biome is within the trader distance, and no spawn is within 60 m. Then, while the world has fewer than the
/// minimum, the centres of unused wilderness cells of the ticked biomes, nearest a trader first, get one (kept 60 m
/// apart too, which RWG doesn't). Each faces a random way and stands on the ground. Extras: the biomes, the minimum
/// and a maximum, and the trader distance; spawns already in the world stay and count.
/// </summary>
public static class SpawnStage
{
    public const int Cell = TownSiteStage.Cell;

    /// <summary>How far apart, in metres, spawns keep (PlayerSpawn.IsTooClose).</summary>
    public const float Spacing = 60;

    /// <summary>
    /// Takes out the spawns the last run placed (spawns placed or moved by hand stay), then places new ones as part of
    /// <paramref name="session"/>. <paramref name="placed"/> is updated in place, and is part of the undo step.
    /// </summary>
    public static SpawnResult Run(World world, SpawnOptions options, List<PlacedSpawn> placed, PrefabLibrary library, BiomeTable biomes,
        IEnumerable<TownSite> sites, IEnumerable<WildernessPoi> wilderness, EditSession session)
    {
        int removed = TakeDown(world, placed, session);
        var all = new GridRect(0, 0, world.Size, world.Size);
        session.TouchList(placed, p => p.Clone(), all);
        session.TouchList(world.Spawns, s => s.Clone(), all);
        var notes = new List<string>();
        int kept = world.Spawns.Count;

        var ticked = options.Biomes.Select(b => b.Trim()).Where(b => b.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool BiomeOk(string? biome) => ticked.Count == 0 || biome is not null && ticked.Contains(biome);

        var traders = Traders(world, library, biomes);
        if (traders.Count == 0) notes.Add("The world has no traders, so no spawns go by the gravel roads.");

        // Near the opening quest's trader: it alone counts, for spawns of any ticked biome.
        int traderCount = traders.Count;
        StarterCandidate? starter = null;
        if (options.NearStarterTrader && traders.Count > 0)
        {
            starter = StarterTrader.Find(world, library, biomes);
            if (starter is null)
                notes.Add("The opening quest finds no trader (none in pine forest with a tier-1 POI within 500 m), so spawns go by any trader.");
            else
            {
                traders = [(starter.Centre, null)];
                notes.Add($"Spawns go by {starter.Trader.Name} at ({world.GridToWorld((int)starter.Centre.X)}, {world.GridToWorld((int)starter.Centre.Y)}), " +
                          "the trader the opening quest sends new players to.");
            }
        }
        var cells = new CellInfo(world, library, sites);
        var taken = world.Spawns.Select(s => new Vector2(world.WorldToGrid((int)MathF.Round(s.X)), world.WorldToGrid((int)MathF.Round(s.Z)))).ToList();
        var rand = new Random(unchecked(options.Seed + 7919));
        int max = options.Max > 0 ? options.Max : int.MaxValue;
        float reach = Math.Max(0, options.TraderDistance);
        // By the starter trader, every spawn stays inside the distance validation warns at.
        if (starter is not null) reach = Math.Min(reach, StarterTrader.FarSpawn);

        bool Room(Vector2 at) => taken.All(t => Vector2.DistanceSquared(t, at) >= Spacing * Spacing);

        void Add(int gx, int gz, bool byRoad)
        {
            var spawn = new SpawnPoint
            {
                X = world.GridToWorld(gx), Y = SpawnHelper.StandingHeight(world, gx, gz), Z = world.GridToWorld(gz), RotationY = rand.Next(360),
            };
            world.Spawns.Add(spawn);
            placed.Add(new PlacedSpawn { X = spawn.X, Y = spawn.Y, Z = spawn.Z, RotationY = spawn.RotationY, ByRoad = byRoad });
            taken.Add(new Vector2(gx, gz));
        }

        // By the gravel roads.
        int byRoad = 0;
        if (traders.Count > 0)
            foreach (var poi in wilderness.Where(p => p.Drawn && p.Road is { Points.Count: >= 5 }).OrderBy(p => p.RoadOrder))
            {
                var points = poi.Road!.Points;
                for (int i = 2; i < points.Count - 2 && world.Spawns.Count < max; i++)
                {
                    int gx = (int)points[i][0], gz = (int)points[i][1];
                    if ((uint)gx >= (uint)world.Size || (uint)gz >= (uint)world.Size || Wet(world, gx, gz)) continue;
                    string? biome = TownGenerator.BiomeTagAt(world, biomes, gx, gz);
                    if (!BiomeOk(biome)) continue;
                    int cell = cells.IndexOf(gx, gz);
                    if (!cells.HasPrefabs[cell] || cells.MaxTier[cell] > 1) continue;
                    var at = new Vector2(gx, gz);
                    if (!traders.Any(t => (ticked.Count == 0 || starter is not null || string.Equals(t.Biome, biome, StringComparison.OrdinalIgnoreCase))
                                          && Vector2.DistanceSquared(t.Centre, at) < reach * reach)) continue;
                    if (!Room(at)) continue;
                    Add(gx, gz, true);
                    byRoad++;
                }
            }

        // The top-up, nearest a trader first (the map's middle when there are none).
        int topUp = 0;
        int min = Math.Min(Math.Max(0, options.Min), max);
        if (world.Spawns.Count < min)
        {
            var middle = new Vector2(world.Size / 2f, world.Size / 2f);
            var free = new List<(int Gx, int Gz, float D)>();
            for (int cz = 1; cz < cells.Width - 1; cz++)
                for (int cx = 1; cx < cells.Width - 1; cx++)
                {
                    int i = cx + cz * cells.Width;
                    if (cells.Town[i] || cells.Used[i]) continue;
                    int gx = cx * Cell + Cell / 2, gz = cz * Cell + Cell / 2;
                    if (Wet(world, gx, gz) || Radiated(world, gx, gz) || !BiomeOk(TownGenerator.BiomeTagAt(world, biomes, gx, gz))) continue;
                    var at = new Vector2(gx, gz);
                    float d = traders.Count == 0 ? Vector2.DistanceSquared(at, middle) : traders.Min(t => Vector2.DistanceSquared(t.Centre, at));
                    if (starter is not null && d >= StarterTrader.FarSpawn * StarterTrader.FarSpawn) continue;
                    free.Add((gx, gz, d));
                }
            foreach (var (gx, gz, _) in free.OrderBy(f => f.D).ThenBy(f => f.Gz).ThenBy(f => f.Gx))
            {
                if (world.Spawns.Count >= min) break;
                if (!Room(new Vector2(gx, gz))) continue;
                Add(gx, gz, false);
                topUp++;
            }
            if (world.Spawns.Count < min)
                notes.Add($"Only {world.Spawns.Count} of the {min} spawns asked for: no more room in the ticked biomes" +
                          (starter is not null ? $" within {StarterTrader.FarSpawn:0} m of the opening quest's trader." : "."));
        }
        return new SpawnResult(byRoad, topUp, removed, kept, traderCount, notes);
    }

    /// <summary>
    /// Takes the spawns the stage placed out of the world, for a re-run or an earlier stage's run. A spawn moved,
    /// turned or deleted by hand no longer matches, so it stays (it is the user's now). Returns how many came out.
    /// </summary>
    public static int TakeDown(World world, List<PlacedSpawn> placed, EditSession session)
    {
        if (placed.Count == 0) return 0;
        var all = new GridRect(0, 0, world.Size, world.Size);
        session.TouchList(placed, p => p.Clone(), all);
        session.TouchList(world.Spawns, s => s.Clone(), all);
        var gone = new HashSet<SpawnPoint>();
        foreach (var p in placed)
            if (world.Spawns.FirstOrDefault(s => !gone.Contains(s) && p.Matches(s)) is { } hit) gone.Add(hit);
        world.Spawns.RemoveAll(gone.Contains);
        placed.Clear();
        return gone.Count;
    }

    /// <summary>The traders in the world: each one's centre in grid metres and the biome there.</summary>
    public static List<(Vector2 Centre, string? Biome)> Traders(World world, PrefabLibrary library, BiomeTable biomes)
    {
        var list = new List<(Vector2, string?)>();
        foreach (var p in world.Prefabs)
        {
            if (library.Find(p.Name) is not { IsTrader: true, IsPart: false, IsStreetTile: false }) continue;
            var f = PrefabPlacer.Footprint(world, p, library);
            int x = (f.X0 + f.X1) / 2, z = (f.Z0 + f.Z1) / 2;
            list.Add((new Vector2(x, z), TownGenerator.BiomeTagAt(world, biomes, x, z)));
        }
        return list;
    }

    private static bool Wet(World world, int gx, int gz)
    {
        byte w = world.Water?[gx, gz] ?? 0;
        return w > 0 && World.HeightToMetres(world.Height[gx, gz]) <= w + 0.5f;
    }

    private static bool Radiated(World world, int gx, int gz)
    {
        if (world.Radiation is not { } rad) return false;
        int rs = Math.Max(1, world.Size / rad.Width);
        return rad.Contains(gx / rs, gz / rs) && rad[gx / rs, gz / rs] > 0;
    }

    /// <summary>The 150 m cells: which hold prefabs (and the hardest tier among them), and which are town.</summary>
    private sealed class CellInfo
    {
        public int Width { get; }
        public bool[] HasPrefabs { get; }
        public int[] MaxTier { get; }
        public bool[] Used { get; }
        public bool[] Town { get; }

        public CellInfo(World world, PrefabLibrary library, IEnumerable<TownSite> sites)
        {
            int w = Width = Math.Max(1, world.Size / Cell);
            HasPrefabs = new bool[w * w];
            MaxTier = new int[w * w];
            Used = new bool[w * w];
            Town = new bool[w * w];
            foreach (var s in sites)
                foreach (var (cx, cz) in s.AllCells)
                    if ((uint)cx < (uint)w && (uint)cz < (uint)w) Town[cx + cz * w] = true;
            foreach (var p in world.Prefabs)
            {
                if (library.Find(p.Name) is not { IsPart: false } info) continue;
                var f = PrefabPlacer.Footprint(world, p, library);
                for (int cz = Math.Max(0, f.Z0 / Cell); cz <= Math.Min(w - 1, (f.Z1 - 1) / Cell); cz++)
                    for (int cx = Math.Max(0, f.X0 / Cell); cx <= Math.Min(w - 1, (f.X1 - 1) / Cell); cx++)
                    {
                        int i = cx + cz * w;
                        HasPrefabs[i] = Used[i] = true;
                        MaxTier[i] = Math.Max(MaxTier[i], info.DifficultyTier);
                        if (info.IsStreetTile) Town[i] = true;
                    }
            }
        }

        public int IndexOf(int gx, int gz) => Math.Min(Width - 1, gx / Cell) + Math.Min(Width - 1, gz / Cell) * Width;
    }
}
