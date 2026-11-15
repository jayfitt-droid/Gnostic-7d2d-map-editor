using System.Numerics;
using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Generate;
using MapEditor.Core.Model;

namespace MapEditor.Core.Ops;

/// <summary>The game's two bridge prefabs, found in the prefab library.</summary>
public sealed record BridgeKit(PrefabInfo Tile, PrefabInfo End)
{
    /// <summary>The deck wrecks the game's library has (any of <see cref="BridgeOp.WreckPrefabs"/> it lacks is left out; none means no wrecks).</summary>
    public IReadOnlyList<DeckWreck> Wrecks { get; init; } = [];

    /// <summary>
    /// The library the kit came from, so the Bridge tool's ramps can keep to the ground of the prefabs in their way; null
    /// (a kit made by hand) leaves ramps to the ground alone.
    /// </summary>
    public PrefabLibrary? Library { get; init; }

    public static BridgeKit? From(PrefabLibrary library) =>
        library.Find(BridgeOp.Tile) is { } tile && library.Find(BridgeOp.End) is { } end
            ? new BridgeKit(tile, end)
            {
                Wrecks = BridgeOp.WreckPrefabs
                    .Select(w => library.Find(w.Name) is { } info ? new DeckWreck(info, w.CarX, w.CarZ, w.Weight) : null)
                    .OfType<DeckWreck>().ToList(),
                Library = library,
            }
            : null;
}

/// <summary>A car wreck prefab for a bridge deck: where its car block stands in the prefab (layer 0), and how often it is picked.</summary>
public sealed record DeckWreck(PrefabInfo Info, int CarX, int CarZ, int Weight);

/// <summary>
/// A bridge in grid metres: along z (north-south) or x, centred on <see cref="Line"/>, from <see cref="Start"/> for
/// <see cref="Pieces"/> sections, its bottom at <see cref="Y"/>.
/// </summary>
public sealed record BridgePlan(BridgeKit Kit, bool AlongZ, int Line, int Start, int Pieces, int Y)
{
    /// <summary>
    /// Adds car wrecks to the deck when set (the value salts the choice, so the same bridge and salt always get the same
    /// wrecks); null adds none.
    /// </summary>
    public int? WreckSeed { get; init; }

    public int End => Start + Pieces * BridgeOp.Piece;
    public int DeckTop => Y + BridgeOp.Deck;

    public Vector2 At(float along, float across = 0) => AlongZ ? new(Line + across, along) : new(along, Line + across);

    /// <summary>The cells the bridge covers, in grid metres.</summary>
    public GridRect Rect => AlongZ
        ? new GridRect(Line - BridgeOp.Width / 2, Start, Line + BridgeOp.Width / 2, End)
        : new GridRect(Start, Line - BridgeOp.Width / 2, End, Line + BridgeOp.Width / 2);

    /// <summary>Each section's spot: an end piece at each end, capped side outward, sections between.</summary>
    public IEnumerable<(PrefabInfo Info, int Gx, int Gz, int Rotation)> Sections()
    {
        for (int i = 0; i < Pieces; i++)
        {
            int along = Start + i * BridgeOp.Piece;
            bool first = i == 0, last = i == Pieces - 1;
            var info = first || last ? Kit.End : Kit.Tile;
            // Turns take local +z to world -x, -z, +x; local z = 0 faces out at each end.
            int rotation = AlongZ ? (last ? 2 : 0) : (first ? 3 : 1);
            yield return AlongZ ? (info, Line - BridgeOp.Width / 2, along, rotation) : (info, along, Line - BridgeOp.Width / 2, rotation);
        }
    }

    /// <summary>The ground under each row of pillars, in grid metres.</summary>
    public IEnumerable<GridRect> Piers()
    {
        for (int i = 0; i < Pieces; i++)
        {
            int along = Start + i * BridgeOp.Piece + BridgeOp.PierRow;
            yield return AlongZ
                ? new GridRect(Line - BridgeOp.Width / 2, along - 1, Line + BridgeOp.Width / 2, along + 2)
                : new GridRect(along - 1, Line - BridgeOp.Width / 2, along + 2, Line + BridgeOp.Width / 2);
        }
    }
}

/// <summary>
/// Builds the game's modular bridge: end pieces and sections placed along a line, with footings raised under the
/// pillars. Used by the highways stage and (later) the Bridge tool.
/// </summary>
public static class BridgeOp
{
    /// <summary>The game's modular bridge: a 14 m wide, 9 m long section, and the same with its rails capped at local z = 0.</summary>
    public const string Tile = "rwg_bridge_tile_01", End = "rwg_bridge_end_01";

    /// <summary>A bridge section's length and width, in metres.</summary>
    public const int Piece = 9, Width = 14;

    /// <summary>
    /// Heights in a bridge section, from its bottom: the underside of its beams, and its deck's top block, which is the
    /// heightmap value a road meets it at (as <see cref="PrefabInfo.RoadLevelAbove"/> has it).
    /// </summary>
    public const int Beams = 10, Deck = 12;

    /// <summary>Room between the water and the underside of a bridge's beams, for boats.</summary>
    public const int Clearance = 5;

    /// <summary>
    /// The deck wreck prefabs, each with its car block's spot in the prefab at layer 0 (x, z) and a weight for how often it
    /// is picked: the six <c>player_start</c> cars commonly, the ambulance and army truck (which have sleepers) rarely.
    /// See docs/BRIDGES.md.
    /// </summary>
    internal static readonly (string Name, int CarX, int CarZ, int Weight)[] WreckPrefabs =
    [
        ("player_start1", 3, 1, 4), ("player_start2", 0, 5, 4), ("player_start3", 3, 2, 4),
        ("player_start4", 3, 3, 4), ("player_start5", 3, 5, 4), ("player_start6", 3, 0, 4),
        ("part_ambulance_01", 2, 4, 2), ("part_armytruck_01", 3, 6, 2),
    ];

    /// <summary>Metres of deck between wrecks: each gap is drawn between these.</summary>
    public const int WreckGapMin = 100, WreckGapMax = 150;

    /// <summary>The middle of each lane across the 14 m deck, in blocks from its edge, and the blocks a wreck's footprint stays within (the rail poles stand on the outer two).</summary>
    private const int LaneA = 4, LaneB = 9, WreckFrom = 1, WreckTo = 13;

    /// <summary>The row of pillars in each section, metres from its start.</summary>
    internal const int PierRow = 4;

    /// <summary>
    /// Places a bridge's sections and raises footings under its pillars where the sea floor is below them, so the
    /// pillars stand on the ground and the water stays open between them. Returns its record for taking it down.
    /// </summary>
    public static HighwayRoad Build(World world, BridgePlan plan, EditSession session)
    {
        const int Skirt = 6;
        var piers = plan.Piers().ToList();
        var ground = HighwayStage.Capture(world, piers.Select(r => new GridRect(r.X0 - Skirt - 1, r.Z0 - Skirt - 1, r.X1 + Skirt + 1, r.Z1 + Skirt + 1)), () =>
        {
            foreach (var pier in piers)
            {
                var area = new GridRect(pier.X0 - Skirt, pier.Z0 - Skirt, pier.X1 + Skirt, pier.Z1 + Skirt).Clamp(world.Size, world.Size);
                if (area.IsEmpty) continue;
                session.Touch(world.Height, area);
                for (int z = area.Z0; z < area.Z1; z++)
                {
                    var row = world.Height.Row(z);
                    int dz = z < pier.Z0 ? pier.Z0 - z : z >= pier.Z1 ? z - pier.Z1 + 1 : 0;
                    for (int x = area.X0; x < area.X1; x++)
                    {
                        int dx = x < pier.X0 ? pier.X0 - x : x >= pier.X1 ? x - pier.X1 + 1 : 0;
                        // Up to the pillars' feet, sloping away steeply, so each row of pillars stands on its own footing.
                        float top = plan.Y - 2 * MathF.Max(dx, dz);
                        ushort raw = TerrainBrush.ToRaw(top);
                        if (raw > row[x]) row[x] = raw;
                    }
                }
            }
        });

        var made = plan.Sections().Select(s => new PrefabInstance
        {
            Name = s.Info.Name,
            Position = new Int3(world.GridToWorld(s.Gx), plan.Y, world.GridToWorld(s.Gz)),
            Rotation = s.Rotation,
        }).ToList();
        if (plan.WreckSeed is { } seed) made.AddRange(Wrecks(world, plan, seed));
        session.TouchList(world.Prefabs, p => p.Clone(), plan.Rect.Clamp(world.Size, world.Size));
        world.Prefabs.AddRange(made);
        return new HighwayRoad
        {
            Kind = HighwayKind.Bridge,
            Type = RoadType.None,
            Radius = Width / 2f,
            Points = [[plan.At(plan.Start).X, plan.At(plan.Start).Y], [plan.At(plan.End).X, plan.At(plan.End).Y]],
            Ground = ground,
            Prefabs = [.. made.Select(PlacedPrefab.Of)],
        };
    }

    /// <summary>
    /// Car wrecks standing on the deck: about one per <see cref="WreckGapMin"/>-<see cref="WreckGapMax"/> m, the first half a
    /// gap in, never on the end sections. They alternate lanes and (every second wreck) direction. Each is picked by weight,
    /// turned like the bridge's pieces (0 or 2 on a north-south bridge, 1 or 3 on an east-west one, so the car lies along the
    /// deck), raised so its bottom layer is one above the deck's top block, and placed so its car block is on a lane's middle
    /// with the footprint within blocks 1-12 across. All choices come from a generator seeded by <paramref name="seed"/> and the
    /// bridge's axis, line, start and length, so the same bridge always gets the same wrecks.
    /// </summary>
    internal static List<PrefabInstance> Wrecks(World world, BridgePlan plan, int seed)
    {
        var made = new List<PrefabInstance>();
        var wrecks = plan.Kit.Wrecks;
        if (wrecks.Count == 0) return made;
        var rng = new Random(Mix(seed, plan.AlongZ ? 1 : 0, plan.Line, plan.Start, plan.Pieces));
        int total = wrecks.Sum(w => w.Weight);
        int lane = rng.Next(2), dir = rng.Next(2);
        int length = plan.Pieces * Piece;
        double at = rng.Next(WreckGapMin, WreckGapMax + 1) / 2.0;
        for (int k = 0; at < length; k++)
        {
            var wreck = Pick(wrecks, rng.Next(total));
            int nextGap = rng.Next(WreckGapMin, WreckGapMax + 1);
            int rotation = (plan.AlongZ ? 0 : 1) + 2 * dir;
            var (fx, fz) = wreck.Info.Footprint(rotation);
            var (bx, bz) = CarCell(wreck, rotation);
            int fAlong = plan.AlongZ ? fz : fx, fAcross = plan.AlongZ ? fx : fz;
            int bAlong = plan.AlongZ ? bz : bx, bAcross = plan.AlongZ ? bx : bz;
            int along = plan.Start + (int)Math.Round(at) - bAlong;
            if (along + fAlong > plan.End - Piece) break;
            // A wreck wider than the deck (a mod's, or a game update's) is skipped; clamping first would throw (#22).
            bool fits = fAcross <= WreckTo - WreckFrom;
            int across = fits ? Math.Clamp((lane == 0 ? LaneA : LaneB) - bAcross, WreckFrom, WreckTo - fAcross) : 0;
            if (along >= plan.Start + Piece && fits)
            {
                int gx = plan.AlongZ ? plan.Line - Width / 2 + across : along;
                int gz = plan.AlongZ ? along : plan.Line - Width / 2 + across;
                made.Add(new PrefabInstance
                {
                    Name = wreck.Info.Name,
                    Position = new Int3(world.GridToWorld(gx), plan.Y + Deck + 1, world.GridToWorld(gz)),
                    Rotation = rotation,
                });
            }
            at += nextGap;
            lane ^= 1;
            if (k % 2 == 1) dir ^= 1;
        }
        return made;
    }

    private static DeckWreck Pick(IReadOnlyList<DeckWreck> wrecks, int roll)
    {
        foreach (var w in wrecks)
        {
            if (roll < w.Weight) return w;
            roll -= w.Weight;
        }
        return wrecks[^1];
    }

    /// <summary>Where a wreck's car block lies in its footprint once the prefab is turned by <paramref name="rotation"/> (local +z goes to world -x, -z, +x for turns 1-3).</summary>
    internal static (int X, int Z) CarCell(DeckWreck w, int rotation)
    {
        int sx = w.Info.Size.X, sz = w.Info.Size.Z;
        return (rotation & 3) switch
        {
            1 => (sz - 1 - w.CarZ, w.CarX),
            2 => (sx - 1 - w.CarX, sz - 1 - w.CarZ),
            3 => (w.CarZ, sx - 1 - w.CarX),
            _ => (w.CarX, w.CarZ),
        };
    }

    /// <summary>A stable mix of the inputs (never string or object hashes, which change between runs).</summary>
    private static int Mix(params int[] values)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (int v in values) h = (h ^ (uint)v) * 16777619;
            return (int)h;
        }
    }
}
