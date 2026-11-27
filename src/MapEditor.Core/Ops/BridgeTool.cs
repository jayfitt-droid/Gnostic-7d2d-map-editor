using System.Numerics;
using System.Text.Json.Serialization;
using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Generate;
using MapEditor.Core.Model;

namespace MapEditor.Core.Ops;

/// <summary>
/// Where a bridge lies, with no heights: along z (north-south) or x, centred on <see cref="Line"/>, from
/// <see cref="Start"/> for <see cref="Pieces"/> whole sections, all in grid metres.
/// </summary>
public readonly record struct BridgeLine(bool AlongZ, int Line, int Start, int Pieces)
{
    public int Length => Pieces * BridgeOp.Piece;
    public int End => Start + Length;

    public Vector2 At(float along, float across = 0) => AlongZ ? new(Line + across, along) : new(along, Line + across);

    /// <summary>The cells the bridge covers, in grid metres.</summary>
    public GridRect Rect => AlongZ
        ? new GridRect(Line - BridgeOp.Width / 2, Start, Line + BridgeOp.Width / 2, End)
        : new GridRect(Start, Line - BridgeOp.Width / 2, End, Line + BridgeOp.Width / 2);
}

/// <summary>A bridge end a road can be carried on from: where the deck stops, the way out, and the deck's road level.</summary>
public readonly record struct BridgeEnd(Vector2 Point, Vector2 Outward, float DeckLevel);

/// <summary>
/// A bridge the user built with the Bridge tool, kept in the project's list: where it lies, how high, and what it
/// changed (the ground under its footings and ramps, in the order made, and its prefabs), so it can be moved, resized
/// or taken down whole.
/// </summary>
public sealed class PlacedBridge
{
    public bool AlongZ { get; set; }
    public int Line { get; set; }
    public int Start { get; set; }
    public int Pieces { get; set; }

    /// <summary>The bottom of its sections, in metres (the deck's top block is <see cref="BridgeOp.Deck"/> above).</summary>
    public int Y { get; set; }

    /// <summary>What each part changed, compressed (see <see cref="HighwayStage"/>): footings first, then each ramp.</summary>
    public List<byte[]> Grounds { get; set; } = [];

    /// <summary>Length of the road at the start, in metres. Zero means no ramp; null means an older record.</summary>
    public float? StartRampLength { get; set; }

    /// <summary>Length of the road at the end, in metres. Zero means no ramp; null means an older record.</summary>
    public float? EndRampLength { get; set; }

    public List<PlacedPrefab> Prefabs { get; set; } = [];

    [JsonIgnore] public BridgeLine Where => new(AlongZ, Line, Start, Pieces);

    /// <summary>The road level of the deck, in metres.</summary>
    [JsonIgnore] public int DeckLevel => Y + BridgeOp.Deck;

    public PlacedBridge Clone() => new()
    {
        AlongZ = AlongZ, Line = Line, Start = Start, Pieces = Pieces, Y = Y,
        Grounds = [.. Grounds],
        StartRampLength = StartRampLength, EndRampLength = EndRampLength,
        Prefabs = [.. Prefabs.Select(p => p.Clone())],
    };
}

/// <summary>The part of a bridge a drag grabs: the middle to move it, or an end to resize it.</summary>
public enum BridgePart { Middle, Start, End }

/// <summary>Why a bridge's ramp ends where it does.</summary>
public enum RampEnd
{
    /// <summary>It reaches the ground at <see cref="BridgeTool.RampGrade"/> or gentler.</summary>
    Ground,
    /// <summary>A prefab is in the way: it ends at its edge, level with its ground, at whatever grade that takes.</summary>
    Prefab,
    /// <summary>It runs as far as it may (<see cref="BridgeTool.MaxRamp"/> or the world's edge) without reaching the ground.</summary>
    Longest,
}

/// <summary>
/// Where one of a bridge's ramps runs, in grid metres: from the deck end <see cref="From"/> to <see cref="To"/>. The
/// <see cref="Grade"/> is its average rise over run; <see cref="Prefab"/> names the prefab it stops at, if any.
/// </summary>
public sealed record RampPlan(Vector2 From, Vector2 To, float Length, float Grade, RampEnd Ends, string? Prefab)
{
    /// <summary>Steeper than the grade ramps are sized for.</summary>
    public bool Steep => Grade > BridgeTool.RampGrade + 0.005f;
}

/// <summary>
/// The Bridge tool's logic: snapping a drag to a bridge, choosing its deck height, building it with ramps down to the
/// ground, and moving, resizing and taking it down. Every change is made in the caller's <see cref="EditSession"/>.
/// </summary>
public static class BridgeTool
{
    /// <summary>Shortest bridge: the two end pieces.</summary>
    public const int MinPieces = 2;

    /// <summary>Steepest ramp grade (rise over run) the ramp length is sized for, and the shortest and longest ramp in metres.</summary>
    public const float RampGrade = 0.06f;
    public const float MinRamp = 30, MaxRamp = 200;

    /// <summary>How far past a ramp's end the ground must stay within reach, so a bank just beyond doesn't leave a lip.</summary>
    private const int RampSettle = 15;

    /// <summary>Room kept between the beams and ground under the middle of a bridge.</summary>
    public const float HillClearance = 0.5f;

    /// <summary>How far below the beams ground running on from an end still counts as the bank the bridge stands on.</summary>
    public const float BankDepth = 2;

    // ---- planning ----

    /// <summary>
    /// Snaps two clicks to a bridge: the line locks to the nearer of north-south and east-west (through the start
    /// click), the length to whole pieces (at least <see cref="MinPieces"/>) counted from the start click toward the
    /// end click. Returns null when it can't fit in the world.
    /// </summary>
    public static BridgeLine? Snap(Vector2 start, Vector2 end, int worldSize)
    {
        float dx = end.X - start.X, dz = end.Y - start.Y;
        bool alongZ = MathF.Abs(dz) >= MathF.Abs(dx);
        float d = alongZ ? dz : dx;
        int from = (int)MathF.Round(alongZ ? start.Y : start.X);
        int pieces = Math.Max(MinPieces, (int)MathF.Round(MathF.Abs(d) / BridgeOp.Piece));
        int line = (int)MathF.Round(alongZ ? start.X : start.Y);
        if (d >= 0)
            pieces = Math.Min(pieces, (worldSize - from) / BridgeOp.Piece);
        else
            pieces = Math.Min(pieces, from / BridgeOp.Piece);
        return Fit(new BridgeLine(alongZ, line, d >= 0 ? from : from - pieces * BridgeOp.Piece, pieces), worldSize);
    }

    /// <summary>The bridge with one end dragged to <paramref name="along"/> (grid metres along its line), the other end fixed.</summary>
    public static BridgeLine? DragEnd(BridgeLine bridge, bool startEnd, float along, int worldSize)
    {
        if (startEnd)
        {
            int pieces = Math.Max(MinPieces, (int)MathF.Round((bridge.End - along) / BridgeOp.Piece));
            pieces = Math.Min(pieces, bridge.End / BridgeOp.Piece);
            return Fit(bridge with { Start = bridge.End - pieces * BridgeOp.Piece, Pieces = pieces }, worldSize);
        }
        int count = Math.Max(MinPieces, (int)MathF.Round((along - bridge.Start) / BridgeOp.Piece));
        count = Math.Min(count, (worldSize - bridge.Start) / BridgeOp.Piece);
        return Fit(bridge with { Pieces = count }, worldSize);
    }

    /// <summary>The bridge shifted by <paramref name="delta"/> metres along its line, kept inside the world.</summary>
    public static BridgeLine Move(BridgeLine bridge, int delta, int worldSize) =>
        bridge with { Start = Math.Clamp(bridge.Start + delta, 0, Math.Max(0, worldSize - bridge.Length)) };

    private static BridgeLine? Fit(BridgeLine b, int size)
    {
        if (b.Pieces < MinPieces || b.Start < 0 || b.End > size) return null;
        int half = BridgeOp.Width / 2;
        if (size < BridgeOp.Width) return null;
        return b with { Line = Math.Clamp(b.Line, half, size - half) };
    }

    // ---- deck height ----

    /// <summary>
    /// The bottom of the sections for a new bridge. Over water the beams clear the water by <see cref="BridgeOp.Clearance"/>
    /// (as the highway stage's bridges do), but never lower than the higher end's ground; over land the deck is level with
    /// the higher end's ground. Either way the deck is never below the ground anywhere under it, and the beams clear ground
    /// that rises between the banks by <see cref="HillClearance"/>, so a hill in the middle never comes up through the deck.
    /// A bank running on under an end at or above the beams is an abutment the bridge stands on, as Validate treats it.
    /// </summary>
    public static int AutoY(World world, BridgeLine line)
    {
        float wet = float.MinValue;
        for (int along = line.Start; along < line.End; along++)
        {
            var (x, z) = Cell(world, line.At(along + 0.5f));
            if (world.Water?[x, z] is { } w and > 0 && World.HeightToMetres(world.Height[x, z]) < w) wet = Math.Max(wet, w);
        }
        float top = Math.Max(GroundBeyond(world, line, true), GroundBeyond(world, line, false));
        if (wet > float.MinValue) top = Math.Max(top, wet + BridgeOp.Clearance - BridgeOp.Beams + BridgeOp.Deck);
        int y = Math.Max(0, (int)MathF.Round(top) - BridgeOp.Deck);

        // Under the deck: never above the road, and between the banks clear of the beams. Raising the beams can turn more of
        // a bank into ground between the banks, so measure again until the height settles.
        for (int pass = 0; pass < 16; pass++)
        {
            var (highest, hill) = GroundUnder(world, line, y + BridgeOp.Beams);
            float needed = Math.Max(highest - BridgeOp.Deck, hill + HillClearance - BridgeOp.Beams);
            if (needed <= y) break;
            y = (int)MathF.Ceiling(needed - 0.01f);
        }
        return y;
    }

    /// <summary>
    /// The highest ground anywhere under the deck, and the highest between the banks: in each column along the deck, ground
    /// running on from either end within <see cref="BankDepth"/> of <paramref name="underside"/> or above is a bank (a
    /// generous depth, so a bumpy bank isn't cut short and its far side taken for a hill).
    /// </summary>
    private static (float Highest, float Hill) GroundUnder(World world, BridgeLine line, float underside)
    {
        float highest = float.MinValue, hill = float.MinValue;
        var ground = new float[line.Length];
        for (int across = -BridgeOp.Width / 2; across < BridgeOp.Width / 2; across++)
        {
            for (int i = 0; i < line.Length; i++)
            {
                var (x, z) = Cell(world, line.At(line.Start + i + 0.5f, across + 0.5f));
                ground[i] = World.HeightToMetres(world.Height[x, z]);
                highest = Math.Max(highest, ground[i]);
            }
            int from = 0, to = line.Length;
            while (from < to && ground[from] > underside - BankDepth) from++;
            while (to > from && ground[to - 1] > underside - BankDepth) to--;
            for (int i = from; i < to; i++) hill = Math.Max(hill, ground[i]);
        }
        return (highest, hill);
    }

    /// <summary>The highest ground across the deck in the first metre outside one end.</summary>
    private static float GroundBeyond(World world, BridgeLine line, bool atStart)
    {
        float best = 0;
        for (int across = -BridgeOp.Width / 2; across <= BridgeOp.Width / 2; across += 2)
        {
            var (x, z) = Cell(world, line.At(atStart ? line.Start - 1 : line.End, across));
            best = Math.Max(best, World.HeightToMetres(world.Height[x, z]));
        }
        return best;
    }

    private static (int X, int Z) Cell(World world, Vector2 p) =>
        (Math.Clamp((int)MathF.Floor(p.X), 0, world.Size - 1), Math.Clamp((int)MathF.Floor(p.Y), 0, world.Size - 1));

    // ---- building and taking down ----

    /// <summary>
    /// Builds a bridge at <paramref name="line"/> with its sections' bottom at <paramref name="y"/>, plus a ramp road from
    /// each deck end down to the ground, and returns its record. The caller adds it to its list.
    /// </summary>
    public static PlacedBridge Place(World world, BridgeKit kit, BridgeLine line, int y, EditSession session, bool ramps = true, bool wrecks = true)
    {
        // The wrecks come from the bridge's axis, line, start and length alone (salt 0), so Move, Resize, Rebuild all and a
        // height change each give the same result for the same place, and the height never reshuffles them.
        var plan = new BridgePlan(kit, line.AlongZ, line.Line, line.Start, line.Pieces, y) { WreckSeed = wrecks ? 0 : null };
        var built = BridgeOp.Build(world, plan, session);
        var bridge = new PlacedBridge
        {
            AlongZ = line.AlongZ, Line = line.Line, Start = line.Start, Pieces = line.Pieces, Y = y,
            Grounds = [built.Ground],
            Prefabs = built.Prefabs,
            StartRampLength = 0,
            EndRampLength = 0,
        };
        if (ramps)
            foreach (bool atStart in new[] { true, false })
                if (PlanRamp(world, plan, atStart) is { } ramp)
                {
                    bridge.Grounds.Add(DrawRamp(world, plan, atStart, ramp, session));
                    if (atStart) bridge.StartRampLength = ramp.Length;
                    else bridge.EndRampLength = ramp.Length;
                }
        return bridge;
    }

    /// <summary>
    /// A short asphalt road from a deck end out along the bridge's line, drawn by the ordinary road code, with its height
    /// held at the deck's at the end and easing down (or up) to the ground. Returns what it changed.
    /// </summary>
    private static byte[] DrawRamp(World world, BridgePlan plan, bool atStart, RampPlan ramp, EditSession session)
    {
        float deck = plan.DeckTop;
        var path = new RoadPath([ramp.From, ramp.To], RoadType.Asphalt, BridgeOp.Width / 2f);
        var library = plan.Kit.Library ?? PrefabLibrary.Of([plan.Kit.Tile, plan.Kit.End, .. plan.Kit.Wrecks.Select(w => w.Info)]);
        // A prefab in the way (a town's street tile, a POI): the ramp ends at its edge, level with its ground there, as the
        // highway stage's roads meet prefabs. It climbs (or drops) at one even grade on the way, the gentlest the room allows.
        if (ramp.Ends == RampEnd.Prefab)
        {
            float ground = PrefabAhead(world, plan, ramp.From, Direction(plan, atStart), ramp.Length)!.Value.Ground;
            return HighwayStage.Capture(world, path, () =>
                RoadOp.Draw(world, path, session, (_, line) => HeldProfile(line, deck, ground, Math.Max(0, ramp.Length - HeldLevel)), library));
        }
        return HighwayStage.Capture(world, path, () =>
            RoadOp.Draw(world, path, session, (w, line) => ApproachProfile(w, line, path.Type, deck, null, ramp.Length), library));
    }

    private static Vector2 Direction(BridgePlan plan, bool atStart) =>
        plan.AlongZ ? new Vector2(0, atStart ? -1 : 1) : new Vector2(atStart ? -1 : 1, 0);

    /// <summary>
    /// Where the ramp at one end of <paramref name="plan"/> would run, as <see cref="Place"/> builds it, without changing
    /// anything (the Bridge tool's preview shows it). Null when the bridge ends at the edge of the world.
    /// </summary>
    public static RampPlan? PlanRamp(World world, BridgePlan plan, bool atStart)
    {
        var from = plan.At(atStart ? plan.Start : plan.End);
        var dir = Direction(plan, atStart);
        float room = (plan.AlongZ ? dir.Y : dir.X) < 0 ? (plan.AlongZ ? from.Y : from.X) : world.Size - (plan.AlongZ ? from.Y : from.X);
        room -= 1;
        if (room < 8) return null;

        float deck = plan.DeckTop;
        float length = RampLength(world, from, dir, deck, room);
        if (PrefabAhead(world, plan, from, dir, length) is var (edge, ground, name))
        {
            float stop = Math.Min(edge, Math.Min(room, MaxRamp));
            if (stop < 1) return null;
            float run = Math.Max(1, stop - HeldLevel);
            return new RampPlan(from, from + dir * stop, stop, MathF.Abs(ground - deck) / run, RampEnd.Prefab, name);
        }

        var (x, z) = Cell(world, from + dir * length);
        float rise = MathF.Abs(World.HeightToMetres(world.Height[x, z]) - deck);
        bool reached = rise <= RampGrade * length + 0.01f;
        return new RampPlan(from, from + dir * length, length, rise / length, reached ? RampEnd.Ground : RampEnd.Longest, null);
    }

    /// <summary>
    /// The nearest prefab a ramp of <paramref name="length"/> from <paramref name="from"/> would come within
    /// <see cref="HighwayStage.PathGrid.HoldReach"/> of (beside the ramp or ahead of it): how far along the ramp its
    /// footprint starts, its road level and its name. Bridge pieces, deck wrecks and parts don't count. Null when none is that close, or when the
    /// kit has no prefab library to measure footprints with.
    /// </summary>
    private static (float Edge, float Ground, string Name)? PrefabAhead(World world, BridgePlan plan, Vector2 from, Vector2 dir, float length)
    {
        if (plan.Kit.Library is not { } library) return null;
        const int Reach = HighwayStage.PathGrid.HoldReach;
        float half = BridgeOp.Width / 2f;
        bool alongZ = plan.AlongZ;
        float sign = alongZ ? dir.Y : dir.X;
        float start = alongZ ? from.Y : from.X, across = alongZ ? from.X : from.Y;
        (float Edge, float Ground, string Name)? best = null;
        foreach (var p in world.Prefabs)
        {
            if (library.Find(p.Name) is not { IsPart: false, IsBridge: false } info || IsWreck(p.Name)) continue;
            var f = PrefabPlacer.Footprint(world, p, library);
            float a0 = alongZ ? f.Z0 : f.X0, a1 = alongZ ? f.Z1 : f.X1, l0 = alongZ ? f.X0 : f.Z0, l1 = alongZ ? f.X1 : f.Z1;
            // Beside the ramp's line, within reach of its side.
            if (l0 - (across + half) > Reach || (across - half) - l1 > Reach) continue;
            // How far ahead the footprint starts; one behind the end is no concern.
            float near = sign > 0 ? a0 - start : start - a1, far = sign > 0 ? a1 - start : start - a0;
            if (far <= 0) continue;
            float edge = Math.Max(0, near);
            if (edge - Reach >= length || (best is { } b && b.Edge <= edge)) continue;
            best = (edge, info.RoadLevelAbove(p.Position.Y), p.Name);
        }
        return best;
    }

    private static bool IsWreck(string name) =>
        BridgeOp.WreckPrefabs.Any(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>How far a ramp that stops at a prefab runs level at its ground before the prefab's edge, in metres.</summary>
    private const float HeldLevel = PrefabPlacer.RoadTouch;

    /// <summary>How long the eases at each end of a held ramp's even grade are, in metres, so it has no kink at the deck or the top.</summary>
    private const float HeldEase = 4;

    /// <summary>
    /// A ramp's heights from the deck to <paramref name="ground"/> over <paramref name="run"/> metres, then level: one even
    /// grade, eased in and out over <see cref="HeldEase"/> metres at each end (the grade grows and shrinks linearly there).
    /// </summary>
    internal static float[] HeldProfile(List<Vector2> line, float deck, float ground, float run)
    {
        var heights = new float[line.Count];
        float rise = ground - deck, ease = Math.Min(HeldEase, run / 2);
        // The even grade: each ease climbs half as much as a full-grade stretch its length would.
        float grade = run - ease > 0 ? rise / (run - ease) : 0;
        float along = 0;
        for (int i = 0; i < line.Count; i++)
        {
            if (i > 0) along += Vector2.Distance(line[i - 1], line[i]);
            float s = Math.Clamp(along, 0, run);
            float climbed = run <= 0 ? rise
                : s < ease ? grade * s * s / (2 * ease)
                : s <= run - ease ? grade * (s - ease / 2)
                : rise - grade * (run - s) * (run - s) / (2 * ease);
            heights[i] = deck + climbed;
        }
        return heights;
    }

    /// <summary>
    /// How long a ramp from a deck end at <paramref name="deck"/> must be: the first distance (from <see cref="MinRamp"/>)
    /// where a <see cref="RampGrade"/> slope from the deck reaches the ground, and the ground stays within that slope for
    /// <see cref="RampSettle"/> metres beyond, so a bank just past the ramp's end doesn't leave a lip. At most
    /// <see cref="MaxRamp"/> and the room to the world's edge.
    /// </summary>
    public static float RampLength(World world, Vector2 from, Vector2 dir, float deck, float room)
    {
        float most = Math.Min(room, MaxRamp);
        bool Within(float d)
        {
            var (x, z) = Cell(world, from + dir * d);
            return MathF.Abs(World.HeightToMetres(world.Height[x, z]) - deck) <= RampGrade * d;
        }
        for (float d = MinRamp; d < most; d++)
        {
            bool settled = true;
            for (int k = 0; k <= RampSettle && d + k <= room && settled; k++) settled = Within(d + k);
            if (settled) return d;
        }
        return most;
    }

    /// <summary>
    /// The road's own height profile (<see cref="RoadOp.HeightProfile"/>), eased to a bridge deck's level over
    /// <paramref name="reach"/> metres at the start and/or the end, so the road meets the deck without a step.
    /// </summary>
    public static float[] ApproachProfile(World world, List<Vector2> line, RoadType type, float? startDeck, float? endDeck, float reach)
    {
        var heights = RoadOp.HeightProfile(world, line, type);
        if (heights.Length == 0 || reach <= 0) return heights;
        var along = new float[line.Count];
        for (int i = 1; i < line.Count; i++) along[i] = along[i - 1] + Vector2.Distance(line[i - 1], line[i]);
        float total = along[^1];
        for (int i = 0; i < heights.Length; i++)
        {
            if (startDeck is { } s) heights[i] = Ease(s, heights[i], along[i] / reach);
            if (endDeck is { } e) heights[i] = Ease(e, heights[i], (total - along[i]) / reach);
        }
        return heights;
    }

    private static float Ease(float deck, float ground, float t)
    {
        t = Math.Clamp(t, 0, 1);
        return deck + (ground - deck) * (t * t * (3 - 2 * t));
    }

    /// <summary>Takes a bridge out: the ground under its footings and ramps goes back as it was, and its pieces are removed.</summary>
    public static void TakeDown(World world, PlacedBridge bridge, EditSession session)
    {
        for (int i = bridge.Grounds.Count - 1; i >= 0; i--) HighwayStage.Restore(world, bridge.Grounds[i], session);
        if (bridge.Prefabs.Count == 0) return;
        session.TouchList(world.Prefabs, p => p.Clone(), bridge.Where.Rect.Clamp(world.Size, world.Size));
        var gone = new HashSet<PrefabInstance>();
        foreach (var w in bridge.Prefabs)
            if (world.Prefabs.FirstOrDefault(p => !gone.Contains(p) && w.Matches(p)) is { } hit) gone.Add(hit);
        world.Prefabs.RemoveAll(gone.Contains);
    }

    /// <summary>Builds a bridge and adds it to <paramref name="bridges"/>.</summary>
    public static PlacedBridge Add(World world, BridgeKit kit, List<PlacedBridge> bridges, BridgeLine line, int y, EditSession session, bool wrecks = true)
    {
        session.TouchList(bridges, b => b.Clone(), line.Rect.Clamp(world.Size, world.Size));
        var made = Place(world, kit, line, y, session, wrecks: wrecks);
        bridges.Add(made);
        return made;
    }

    /// <summary>Takes a bridge down and removes it from <paramref name="bridges"/>.</summary>
    public static void Remove(World world, List<PlacedBridge> bridges, PlacedBridge bridge, EditSession session)
    {
        session.TouchList(bridges, b => b.Clone(), bridge.Where.Rect.Clamp(world.Size, world.Size));
        TakeDown(world, bridge, session);
        bridges.Remove(bridge);
    }

    /// <summary>
    /// Moves, lengthens or shortens a bridge (or changes its height): takes it down, which puts the ground back, and
    /// builds it again at <paramref name="line"/> and <paramref name="y"/>, in its place in the list.
    /// </summary>
    public static PlacedBridge Replace(World world, BridgeKit kit, List<PlacedBridge> bridges, PlacedBridge old, BridgeLine line, int y, EditSession session, bool wrecks = true)
    {
        int index = bridges.IndexOf(old);
        if (index < 0) throw new ArgumentException("The bridge isn't in the list.", nameof(old));
        session.TouchList(bridges, b => b.Clone(), old.Where.Rect.Union(line.Rect).Clamp(world.Size, world.Size));
        TakeDown(world, old, session);
        var made = Place(world, kit, line, y, session, wrecks: wrecks);
        bridges[index] = made;
        return made;
    }

    /// <summary>
    /// Takes every bridge down and builds it again where it is, with today's deck height and ramp rules: for bridges built
    /// before a rule changed, or after the ground under their ramps was edited. Returns how many were rebuilt and dropped.
    /// </summary>
    public static (int Rebuilt, int Dropped) RebuildEvery(World world, BridgeKit? kit, List<PlacedBridge> bridges, EditSession session, bool wrecks = true)
    {
        var taken = TakeDownAll(world, bridges, session);
        int dropped = RebuildAll(world, kit, bridges, taken, session, wrecks);
        return (taken.Count - dropped, dropped);
    }

    /// <summary>
    /// The ground a bridge needs kept free while it is down for a stage: its deck and, ahead of each end, the longest ramp
    /// it could be rebuilt with (grid metres, inside the world).
    /// </summary>
    public static GridRect KeepClearArea(PlacedBridge bridge, int worldSize)
    {
        var r = bridge.Where.Rect;
        int ramp = (int)MathF.Ceiling(MaxRamp);
        r = bridge.AlongZ ? r with { Z0 = r.Z0 - ramp, Z1 = r.Z1 + ramp } : r with { X0 = r.X0 - ramp, X1 = r.X1 + ramp };
        return r.Clamp(worldSize, worldSize);
    }

    // ---- when a generator stage changes the ground ----

    /// <summary>
    /// Takes every bridge in <paramref name="bridges"/> down (putting its ground back) before a stage changes the ground, in
    /// the stage's own edit. The records stay in the list, to be replaced by <see cref="RebuildAll"/>; returns them.
    /// </summary>
    public static List<PlacedBridge> TakeDownAll(World world, List<PlacedBridge> bridges, EditSession session)
    {
        if (bridges.Count == 0) return [];
        session.TouchList(bridges, b => b.Clone(), new GridRect(0, 0, world.Size, world.Size));
        var taken = bridges.ToList();
        // Newest first, so each puts back the ground it found.
        for (int i = taken.Count - 1; i >= 0; i--) TakeDown(world, taken[i], session);
        return taken;
    }

    /// <summary>
    /// Builds again each bridge <see cref="TakeDownAll"/> took down, on the same line, start and pieces, with the deck
    /// height worked out again from the ground as it is now (<see cref="AutoY"/>), in the same slot of the list. A bridge that
    /// can't be built any more (it no longer fits the world, or there is no <paramref name="kit"/>) is dropped from the list.
    /// Returns how many were dropped.
    /// </summary>
    public static int RebuildAll(World world, BridgeKit? kit, List<PlacedBridge> bridges, List<PlacedBridge> taken, EditSession session, bool wrecks = true)
    {
        if (taken.Count == 0) return 0;
        session.TouchList(bridges, b => b.Clone(), new GridRect(0, 0, world.Size, world.Size));
        int dropped = 0;
        foreach (var old in taken)
        {
            int index = bridges.IndexOf(old);
            if (index < 0) continue;
            var line = old.Where;
            if (kit is null || !Fits(line, world.Size))
            {
                bridges.RemoveAt(index);
                dropped++;
                continue;
            }
            bridges[index] = Place(world, kit, line, AutoY(world, line), session, wrecks: wrecks);
        }
        return dropped;
    }

    /// <summary>
    /// Whether <paramref name="bridge"/> is in or near <paramref name="region"/>: its deck, or the longest ramp it could
    /// have (<see cref="KeepClearArea"/>), overlaps it. New ground there takes the bridge down (#302).
    /// </summary>
    public static bool Near(PlacedBridge bridge, GridRect region, int worldSize)
    {
        var r = KeepClearArea(bridge, worldSize);
        return r.X0 < region.X1 && r.X1 > region.X0 && r.Z0 < region.Z1 && r.Z1 > region.Z0;
    }

    /// <summary>Whether a bridge at <paramref name="line"/> lies inside the world.</summary>
    public static bool Fits(BridgeLine line, int worldSize) =>
        line.Pieces >= MinPieces && line.Start >= 0 && line.End <= worldSize
        && line.Line - BridgeOp.Width / 2 >= 0 && line.Line + BridgeOp.Width / 2 <= worldSize;

    // ---- finding bridges ----

    /// <summary>The tool's bridge a prefab is a piece of, if any (so a click on a piece can pick the whole bridge).</summary>
    public static PlacedBridge? Owner(IEnumerable<PlacedBridge> bridges, PrefabInstance piece) =>
        bridges.FirstOrDefault(b => b.Prefabs.Any(w => w.Matches(piece)));

    /// <summary>
    /// The Highways stage's bridge <paramref name="piece"/> belongs to, if any. Its pieces are locked: the stage takes
    /// them down by its record, so one moved or deleted by hand would be left behind or missed.
    /// </summary>
    public static HighwayRoad? StageOwner(IEnumerable<HighwayRoad>? roads, PrefabInstance piece) =>
        roads?.FirstOrDefault(r => r.Kind == HighwayKind.Bridge && r.Prefabs.Any(w => w.Matches(piece)));

    /// <summary>The bridge whose deck or outline covers <paramref name="grid"/> (grid metres); the last one in the list wins.</summary>
    public static PlacedBridge? Find(IEnumerable<PlacedBridge> bridges, Vector2 grid) =>
        bridges.LastOrDefault(b => b.Where.Rect is var r && grid.X >= r.X0 && grid.X <= r.X1 && grid.Y >= r.Z0 && grid.Y <= r.Z1);

    /// <summary>
    /// The bridge and part a press at <paramref name="grid"/> grabs. Decks win over ramps, and the last bridge in
    /// the list wins ties. When the ramp is narrow on screen, allow a near miss beside its road if no road was hit.
    /// <paramref name="scale"/> is the number of screen DIPs per grid metre.
    /// </summary>
    public static (PlacedBridge Bridge, BridgePart Part)? HitTest(World world, PrefabLibrary library,
        IReadOnlyList<PlacedBridge> bridges, Vector2 grid, double scale)
    {
        if (Find(bridges, grid) is { } deck)
        {
            var line = deck.Where;
            float along = line.AlongZ ? grid.Y : grid.X;
            double zone = Math.Min(Math.Max(24 / scale, 3), line.Length / 3d);
            if (along - line.Start < zone) return (deck, BridgePart.Start);
            if (line.End - along < zone) return (deck, BridgePart.End);
            return (deck, BridgePart.Middle);
        }

        if (grid.X < 0 || grid.Y < 0 || grid.X > world.Size || grid.Y > world.Size) return null;
        const float halfWidth = BridgeOp.Width / 2f;
        double grabHalfWidth = Math.Max(halfWidth, 4 / scale);
        (PlacedBridge Bridge, BridgePart Part)? near = null;
        for (int i = bridges.Count - 1; i >= 0; i--)
        {
            var bridge = bridges[i];
            var line = bridge.Where;
            float across = line.AlongZ ? grid.X : grid.Y;
            float sideways = across - line.Line;
            if (MathF.Abs(sideways) > grabHalfWidth) continue;
            float along = line.AlongZ ? grid.Y : grid.X;
            bool atStart = along < line.Start;
            float distance = atStart ? line.Start - along : along - line.End;
            if (distance <= 0 || distance > MaxRamp + halfWidth) continue;
            float length = RampLength(world, library, bridge, atStart);
            if (length <= 0) continue;

            // The road painter rounds the tip of the ramp with the same radius as the road.
            float pastTip = Math.Max(0, distance - length);
            var hit = (bridge, atStart ? BridgePart.Start : BridgePart.End);
            if (sideways * sideways + pastTip * pastTip <= halfWidth * halfWidth)
                return hit;
            if (pastTip == 0) near ??= hit;
        }
        return near;
    }

    /// <summary>
    /// The built ramp's length, or zero if none was drawn. A record from an older project has no length: it is read from
    /// the road the ramp drew (see <see cref="RecordedRampLength"/>), and only if that is not there, planned again.
    /// </summary>
    public static float RampLength(World world, PrefabLibrary library, PlacedBridge bridge, bool atStart)
    {
        if ((atStart ? bridge.StartRampLength : bridge.EndRampLength) is { } length) return length;
        if (bridge.Grounds.Count <= 1) return 0;
        if (RecordedRampLength(bridge, atStart) is { } recorded) return recorded;
        if (BridgeKit.From(library) is not { } kit) return 0;
        var line = bridge.Where;
        var plan = new BridgePlan(kit, line.AlongZ, line.Line, line.Start, line.Pieces, bridge.Y);
        return PlanRamp(world, plan, atStart)?.Length ?? 0;
    }

    /// <summary>Where each ramp record's road cells lie along a bridge's line, read once per record (never saved).</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<byte[], RoadSpan> RoadSpans = new();

    private sealed record RoadSpan(int Low, int High);

    /// <summary>
    /// The length of one ramp of an older bridge, from the road cells its record holds: how far along the line they run
    /// from the deck end, less the radius the road painter rounds its tip with. Null when no record has road cells there.
    /// </summary>
    private static float? RecordedRampLength(PlacedBridge bridge, bool atStart)
    {
        var line = bridge.Where;
        float middle = (line.Start + line.End) / 2f;
        for (int i = 1; i < bridge.Grounds.Count; i++)
        {
            var ground = bridge.Grounds[i];
            var span = RoadSpans.GetValue(ground, g => ReadRoadSpan(g, bridge));
            if (span.High < span.Low || ((span.Low + span.High + 1) / 2f < middle) != atStart) continue;
            float reach = atStart ? line.Start - span.Low : span.High + 1 - line.End;
            return Math.Max(0, reach - BridgeOp.Width / 2f);
        }
        return null;
    }

    /// <summary>The first and last cell along the bridge's line (high below low when none) of the record's road cells on the line.</summary>
    private static RoadSpan ReadRoadSpan(byte[] ground, PlacedBridge bridge)
    {
        int low = int.MaxValue, high = int.MinValue;
        foreach (var (z, x0, count) in HighwayStage.RoadRuns(ground))
        {
            if (bridge.AlongZ)
            {
                if (x0 > bridge.Line || x0 + count <= bridge.Line - 1) continue;
                low = Math.Min(low, z);
                high = Math.Max(high, z);
            }
            else
            {
                if (z < bridge.Line - 1 || z > bridge.Line) continue;
                low = Math.Min(low, x0);
                high = Math.Max(high, x0 + count - 1);
            }
        }
        return new RoadSpan(low, high);
    }

    /// <summary>The ends of every bridge the world has a record of: the tool's own and the Highways stage's.</summary>
    public static List<BridgeEnd> Ends(IEnumerable<PlacedBridge> bridges, IEnumerable<HighwayRoad>? stage = null)
    {
        var ends = new List<BridgeEnd>();
        foreach (var b in bridges)
        {
            var line = b.Where;
            var (a, z) = (line.At(line.Start), line.At(line.End));
            var out0 = line.AlongZ ? new Vector2(0, -1) : new Vector2(-1, 0);
            ends.Add(new BridgeEnd(a, out0, b.DeckLevel));
            ends.Add(new BridgeEnd(z, -out0, b.DeckLevel));
        }
        foreach (var r in stage?.Where(r => r.Kind == HighwayKind.Bridge && r.Points.Count == 2 && r.Prefabs.Count > 0) ?? [])
        {
            var a = new Vector2(r.Points[0][0], r.Points[0][1]);
            var z = new Vector2(r.Points[1][0], r.Points[1][1]);
            var along = Vector2.Normalize(z - a);
            float level = r.Prefabs[0].Y + BridgeOp.Deck;
            ends.Add(new BridgeEnd(a, -along, level));
            ends.Add(new BridgeEnd(z, along, level));
        }
        return ends;
    }

    /// <summary>The bridge end nearest <paramref name="near"/> within <paramref name="reach"/> metres, or null.</summary>
    public static BridgeEnd? FindEnd(IEnumerable<BridgeEnd> ends, Vector2 near, float reach)
    {
        BridgeEnd? best = null;
        float bestDistance = reach;
        foreach (var e in ends)
        {
            float d = Vector2.Distance(e.Point, near);
            if (d > bestDistance) continue;
            bestDistance = d;
            best = e;
        }
        return best;
    }

    /// <summary>One line describing a bridge for the status bar.</summary>
    public static string Describe(BridgeLine line, int deckLevel) =>
        $"{line.Pieces} pieces, {line.Length} m {(line.AlongZ ? "north-south" : "east-west")}, deck at {deckLevel} m";
}
