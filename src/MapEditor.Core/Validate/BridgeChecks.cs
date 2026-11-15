using System.Globalization;
using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Validate;

/// <summary>The kinds of bridge issue, kept on <see cref="ValidationIssue.Kind"/> so a report can count them.</summary>
public static class BridgeIssue
{
    public const string DeckEnd = "deck-end", RoadReach = "road-reach", Approach = "approach",
        DeckGround = "deck-ground", Footing = "footing", Clearance = "clearance", Stray = "stray-piece", Crossing = "crossing";

    public static readonly string[] All = [DeckEnd, RoadReach, Approach, DeckGround, Footing, Clearance, Stray, Crossing];
}

/// <summary>A bridge read back from a world's prefabs: where it lies and the bottom of its sections (<see cref="Y"/>).</summary>
public sealed record FoundBridge(BridgeLine Where, int Y)
{
    public bool AlongZ => Where.AlongZ;
    public int Length => Where.Length;

    /// <summary>The deck's road level, in metres (<see cref="BridgeOp.Deck"/> above the bottom).</summary>
    public int DeckLevel => Y + BridgeOp.Deck;

    /// <summary>The underside of the beams, in metres.</summary>
    public int Underside => Y + BridgeOp.Beams;
}

/// <summary>What the checks measured for one bridge, and the issues they raised.</summary>
public sealed record BridgeReport(
    FoundBridge Bridge,
    string Label,
    float? StartStep,
    float? EndStep,
    float? StartGrade,
    float? EndGrade,
    float? MinClearance,
    IReadOnlyList<ValidationIssue> Issues)
{
    /// <summary>The larger of the two ends' steps between the deck and the ground, in metres (null if neither could be measured).</summary>
    public float? WorstStep => Larger(StartStep, EndStep);

    /// <summary>The steepest approach grade of the two ends (rise over run).</summary>
    public float? SteepestGrade => Larger(StartGrade, EndGrade);

    private static float? Larger(float? a, float? b) => a is { } x ? (b is { } y ? MathF.Max(x, y) : x) : b;
}

/// <summary>The bridges found in a world, the pieces that belong to none, and the checks' results.</summary>
public sealed record BridgeAnalysis(IReadOnlyList<BridgeReport> Bridges, IReadOnlyList<ValidationIssue> Strays)
{
    public static BridgeAnalysis Empty { get; } = new([], []);

    /// <summary>Every issue, strays first, then each bridge's.</summary>
    public IEnumerable<ValidationIssue> Issues => Strays.Concat(Bridges.SelectMany(b => b.Issues));
}

/// <summary>
/// Finds the game's modular bridges from a world's prefabs alone (no saved bridge list, no generator records) and checks
/// how each one meets the ground, its roads, the water and its own footings. A bridge is two or more
/// <see cref="BridgeOp.End"/> / <see cref="BridgeOp.Tile"/> pieces in a line, one section (<see cref="BridgeOp.Piece"/> m)
/// apart, turned the same way and at the same height.
/// </summary>
public static class BridgeChecks
{
    /// <summary>A deck end, the ground beside it, a ramp's height steps or the pillars' feet may be this far off, in metres.</summary>
    public const float Tolerance = 0.5f;

    /// <summary>A step, gap or buried deck this big is an error, not a warning, in metres.</summary>
    public const float Severe = 2f;

    /// <summary>How far out from each end the road is followed, in metres, and the run its grade is taken over.</summary>
    public const int ApproachReach = 150, GradeRun = 5;

    /// <summary>
    /// The steepest road grade (rise over run, over <see cref="GradeRun"/> m) left without a warning. A 21% bump where a ramp
    /// met a road on a slope drove fine in game (2026-11-13), so the line is a little above that.
    /// </summary>
    public const float MaxGrade = 0.25f;

    /// <summary>Room wanted between the underside of the beams and a road crossing under the bridge, in metres.</summary>
    public const float RoadClearance = BridgeOp.Clearance;

    /// <summary>Cells either side of the deck's centre line the ground is averaged over just past an end (a road's edges ease off).</summary>
    private const int EndHalfWidth = 5;

    // ---- finding ----

    /// <summary>
    /// The bridges in <paramref name="world"/>, found by grouping bridge pieces that are collinear, turned the same way
    /// (along z for even turns, along x for odd), at the same height and exactly one section apart. Also returns the pieces
    /// that fit no bridge (alone, or not level with a neighbour). Empty when the library lacks the bridge pieces.
    /// </summary>
    public static (List<FoundBridge> Bridges, List<PrefabInstance> Strays) Find(World world, PrefabLibrary library)
    {
        var bridges = new List<FoundBridge>();
        var strays = new List<PrefabInstance>();
        if (BridgeKit.From(library) is not { } kit) return (bridges, strays);

        var groups = new Dictionary<(bool AlongZ, int Line, int Y), List<(int Start, PrefabInstance Piece)>>();
        foreach (var p in world.Prefabs)
        {
            PrefabInfo? info = p.Name.Equals(BridgeOp.Tile, StringComparison.OrdinalIgnoreCase) ? kit.Tile
                : p.Name.Equals(BridgeOp.End, StringComparison.OrdinalIgnoreCase) ? kit.End : null;
            if (info is null) continue;
            var (fx, fz) = info.Footprint(p.Rotation);
            bool alongZ = (p.Rotation & 1) == 0;
            if ((alongZ ? (fz, fx) : (fx, fz)) != (BridgeOp.Piece, BridgeOp.Width)) continue;
            int gx = world.WorldToGrid(p.Position.X), gz = world.WorldToGrid(p.Position.Z);
            var key = (alongZ, (alongZ ? gx : gz) + BridgeOp.Width / 2, p.Position.Y);
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = [];
            list.Add((alongZ ? gz : gx, p));
        }

        foreach (var ((alongZ, line, y), all) in groups)
        {
            // Two pieces in one place can't both be part of the line: the second is a stray.
            var pieces = new List<(int Start, PrefabInstance Piece)>();
            foreach (var item in all.OrderBy(a => a.Start))
                if (pieces.Count > 0 && pieces[^1].Start == item.Start) strays.Add(item.Piece);
                else pieces.Add(item);

            int i = 0;
            while (i < pieces.Count)
            {
                int j = i + 1;
                while (j < pieces.Count && pieces[j].Start == pieces[j - 1].Start + BridgeOp.Piece) j++;
                if (j - i >= 2) bridges.Add(new FoundBridge(new BridgeLine(alongZ, line, pieces[i].Start, j - i), y));
                else strays.Add(pieces[i].Piece);
                i = j;
            }
        }
        bridges.Sort((a, b) => (a.Where.AlongZ, a.Where.Line, a.Where.Start).CompareTo((b.Where.AlongZ, b.Where.Line, b.Where.Start)));
        return (bridges, strays);
    }

    // ---- checking ----

    public static BridgeAnalysis Analyze(World world, PrefabLibrary library)
    {
        var (found, strays) = Find(world, library);
        if (found.Count == 0 && strays.Count == 0) return BridgeAnalysis.Empty;
        var stray = strays.Select(p => new ValidationIssue(IssueSeverity.Warning,
            $"{p.Name} at ({p.Position.X}, {p.Position.Z}) is not part of a bridge: it is alone, or not level with the pieces beside it.",
            Math.Clamp(world.WorldToGrid(p.Position.X), 0, world.Size - 1), Math.Clamp(world.WorldToGrid(p.Position.Z), 0, world.Size - 1),
            BridgeIssue.Stray)).ToList();
        var reports = found.Select(b => Check(world, b)).ToList();

        // Two bridges whose decks overlap: one crossing the other, or two laid along the same line.
        for (int i = 0; i < found.Count; i++)
            for (int j = i + 1; j < found.Count; j++)
            {
                GridRect a = found[i].Where.Rect, b = found[j].Where.Rect;
                if (a.X0 >= b.X1 || b.X0 >= a.X1 || a.Z0 >= b.Z1 || b.Z0 >= a.Z1) continue;
                int x = (Math.Max(a.X0, b.X0) + Math.Min(a.X1, b.X1)) / 2, z = (Math.Max(a.Z0, b.Z0) + Math.Min(a.Z1, b.Z1)) / 2;
                string how = found[i].AlongZ == found[j].AlongZ ? "runs into" : "crosses";
                var issue = new ValidationIssue(IssueSeverity.Error, $"{reports[i].Label}: {how} the bridge at {Centre(world, found[j])}.",
                    Math.Clamp(x, 0, world.Size - 1), Math.Clamp(z, 0, world.Size - 1), BridgeIssue.Crossing);
                reports[i] = reports[i] with { Issues = [.. reports[i].Issues, issue] };
            }
        return new BridgeAnalysis(reports, stray);
    }

    private static string Centre(World world, FoundBridge b)
    {
        var centre = b.Where.At(b.Where.Start + b.Length / 2f);
        return string.Create(CultureInfo.InvariantCulture, $"({centre.X - world.Size / 2:0}, {centre.Y - world.Size / 2:0})");
    }

    /// <summary>The label every issue about a bridge starts with: its centre in world coordinates, axis and length.</summary>
    public static string Label(World world, FoundBridge b)
    {
        return $"Bridge at {Centre(world, b)} {(b.AlongZ ? "N-S" : "E-W")} {b.Length} m";
    }

    private static string Side(FoundBridge b, bool atStart) =>
        b.AlongZ ? (atStart ? "south" : "north") : (atStart ? "west" : "east");

    private static string M(float v) => v.ToString("0.0", CultureInfo.InvariantCulture);

    public static BridgeReport Check(World world, FoundBridge b)
    {
        string label = Label(world, b);
        var issues = new List<ValidationIssue>();
        void Add(IssueSeverity severity, string kind, string text, int x, int z) =>
            issues.Add(new(severity, $"{label}: {text}", Math.Clamp(x, 0, world.Size - 1), Math.Clamp(z, 0, world.Size - 1), kind));

        float? startStep = null, endStep = null, startGrade = null, endGrade = null;
        foreach (bool atStart in new[] { true, false })
        {
            string side = Side(b, atStart);
            int past = atStart ? b.Where.Start - 1 : b.Where.End;
            if (past < 0 || past >= world.Size) continue; // the bridge ends at the edge of the world

            // Deck end: the deck's road level against the ground just past the end, across the middle of the deck.
            float ground = 0;
            for (int across = -EndHalfWidth; across < EndHalfWidth; across++)
                ground += Height(world, b.Where, past, across);
            ground /= 2 * EndHalfWidth;
            float step = b.DeckLevel - ground;
            if (atStart) startStep = MathF.Abs(step); else endStep = MathF.Abs(step);
            var at = Cell(b.Where, past, 0);
            if (MathF.Abs(step) > Tolerance)
                Add(MathF.Abs(step) >= Severe ? IssueSeverity.Error : IssueSeverity.Warning, BridgeIssue.DeckEnd,
                    $"{side} end deck {M(b.DeckLevel)} m, ground {M(ground)} m (step {M(MathF.Abs(step))} m)", at.X, at.Z);

            // Road reach: a road or ramp cell in the splat just past the end.
            if (world.Roads is not null)
            {
                bool road = false;
                for (int d = 0; d < 3 && !road; d++)
                    for (int across = -3; across <= 2 && !road; across++)
                    {
                        var (cx, cz) = Cell(b.Where, atStart ? b.Where.Start - 1 - d : b.Where.End + d, across);
                        road = IsRoad(world, cx, cz);
                    }
                if (!road)
                    Add(IssueSeverity.Warning, BridgeIssue.RoadReach, $"{side} end has no road or ramp reaching it.", at.X, at.Z);
            }

            // Approach: follow the road out along the axis for up to 150 m.
            var (grade, gradeAt, jump, jumpAt) = Approach(world, b, atStart);
            if (atStart) startGrade = grade; else endGrade = grade;
            if (grade is { } g && g > MaxGrade)
                Add(IssueSeverity.Warning, BridgeIssue.Approach, $"{side} approach climbs {g * 100:0.#}% over {GradeRun} m (more than {MaxGrade * 100:0}%).", gradeAt.X, gradeAt.Z);
            if (jump is { } j && j > Tolerance)
                Add(IssueSeverity.Warning, BridgeIssue.Approach, $"{side} approach jumps {M(j)} m in one metre.", jumpAt.X, jumpAt.Z);
        }

        AlongDeck(world, b, Add);
        Footings(world, b, Add);
        float? clearance = Clearance(world, b, Add);
        return new BridgeReport(b, label, startStep, endStep, startGrade, endGrade, clearance, issues);
    }

    private delegate void AddIssue(IssueSeverity severity, string kind, string text, int x, int z);

    /// <summary>The grid cell <paramref name="along"/> metres along the bridge's axis and <paramref name="across"/> from its centre line.</summary>
    private static (int X, int Z) Cell(BridgeLine line, int along, int across) =>
        line.AlongZ ? (line.Line + across, along) : (along, line.Line + across);

    private static bool InWorld(World world, int x, int z) => x >= 0 && z >= 0 && x < world.Size && z < world.Size;

    private static float Height(World world, BridgeLine line, int along, int across)
    {
        var (x, z) = Cell(line, along, across);
        return InWorld(world, x, z) ? World.HeightToMetres(world.Height[x, z]) : 0;
    }

    private static bool IsRoad(World world, int x, int z) =>
        InWorld(world, x, z) && world.Roads is { } roads && roads[x, z] is var r && r != 0 && r != (byte)RoadType.Water;

    private static (float? Grade, (int X, int Z) GradeAt, float? Jump, (int X, int Z) JumpAt) Approach(World world, FoundBridge b, bool atStart)
    {
        var heights = new List<float>();
        var cells = new List<(int X, int Z)>();
        for (int i = 0; i < ApproachReach; i++)
        {
            int along = atStart ? b.Where.Start - 1 - i : b.Where.End + i;
            var (x0, z0) = Cell(b.Where, along, -1);
            var (x1, z1) = Cell(b.Where, along, 0);
            if (!InWorld(world, x0, z0) || !InWorld(world, x1, z1) || !(IsRoad(world, x0, z0) || IsRoad(world, x1, z1))) break;
            heights.Add((World.HeightToMetres(world.Height[x0, z0]) + World.HeightToMetres(world.Height[x1, z1])) / 2);
            cells.Add((x1, z1));
        }
        if (heights.Count <= GradeRun) return (null, default, null, default);
        float grade = 0, jump = 0;
        (int, int) gradeAt = cells[0], jumpAt = cells[0];
        for (int i = 0; i + GradeRun < heights.Count; i++)
        {
            float g = MathF.Abs(heights[i + GradeRun] - heights[i]) / GradeRun;
            if (g > grade) (grade, gradeAt) = (g, cells[i]);
        }
        for (int i = 0; i + 1 < heights.Count; i++)
        {
            float j = MathF.Abs(heights[i + 1] - heights[i]);
            if (j > jump) (jump, jumpAt) = (j, cells[i]);
        }
        return (grade, gradeAt, jump, jumpAt);
    }

    /// <summary>Ground that rises over the deck anywhere, or reaches the beams between the banks at the two ends.</summary>
    private static void AlongDeck(World world, FoundBridge b, AddIssue add)
    {
        float worstBeams = float.MinValue, worstDeck = float.MinValue;
        (int X, int Z) beamsAt = default, deckAt = default;
        for (int across = -BridgeOp.Width / 2; across < BridgeOp.Width / 2; across++)
        {
            // Walk the column of ground under the deck. A bank that runs on from an end to within BridgeTool.BankDepth of the
            // beams is an abutment the bridge stands on (the rule the Bridge tool sets its deck height by): only ground that
            // rises again between the banks is flagged.
            int length = b.Length;
            var ground = new float?[length];
            for (int i = 0; i < length; i++)
            {
                var (x, z) = Cell(b.Where, b.Where.Start + i, across);
                if (!InWorld(world, x, z)) continue;
                ground[i] = World.HeightToMetres(world.Height[x, z]);
                if (ground[i]!.Value - b.DeckLevel > worstDeck) (worstDeck, deckAt) = (ground[i]!.Value - b.DeckLevel, (x, z));
            }
            bool Reaches(int i) => ground[i] is { } g && g - b.Underside > -BridgeTool.BankDepth;
            int from = 0, to = length;
            while (from < to && Reaches(from)) from++;
            while (to > from && Reaches(to - 1)) to--;
            for (int i = from; i < to; i++)
                if (ground[i] is { } g && g - b.Underside > worstBeams)
                    (worstBeams, beamsAt) = (g - b.Underside, Cell(b.Where, b.Where.Start + i, across));
        }
        if (worstDeck > Tolerance)
            add(IssueSeverity.Error, BridgeIssue.DeckGround, $"ground rises {M(worstDeck)} m above the deck (deck {M(b.DeckLevel)} m).", deckAt.X, deckAt.Z);
        else if (worstBeams > -Tolerance)
            add(IssueSeverity.Warning, BridgeIssue.DeckGround,
                $"ground reaches the beams: {M(worstBeams + b.Underside)} m against the underside at {M(b.Underside)} m.", beamsAt.X, beamsAt.Z);
    }

    /// <summary>Under each row of pillars the ground should meet the piece bottom: BridgeOp raises a footing to it.</summary>
    private static void Footings(World world, FoundBridge b, AddIssue add)
    {
        int floating = 0;
        float worst = 0;
        (int X, int Z) worstAt = default;
        for (int piece = 0; piece < b.Where.Pieces; piece++)
        {
            int row = b.Where.Start + piece * BridgeOp.Piece + BridgeOp.PierRow;
            float low = float.MaxValue;
            (int X, int Z) lowAt = default;
            for (int along = row - 1; along < row + 2; along++)
                for (int across = -BridgeOp.Width / 2; across < BridgeOp.Width / 2; across++)
                {
                    var (x, z) = Cell(b.Where, along, across);
                    if (!InWorld(world, x, z)) continue;
                    float g = World.HeightToMetres(world.Height[x, z]);
                    if (g < low) (low, lowAt) = (g, (x, z));
                }
            if (low == float.MaxValue || b.Y - low <= Tolerance) continue;
            floating++;
            if (b.Y - low > worst) (worst, worstAt) = (b.Y - low, lowAt);
        }
        // Ground above the piece bottom is a pillar standing in a bank: fine until it reaches the beams (see AlongDeck).
        if (floating > 0)
            add(worst >= Severe ? IssueSeverity.Error : IssueSeverity.Warning, BridgeIssue.Footing,
                $"{floating} of {b.Where.Pieces} rows of pillars float: the ground is up to {M(worst)} m under the piece bottom ({M(b.Y)} m).", worstAt.X, worstAt.Z);
    }

    /// <summary>The least room under the beams over water (all sections) and over a road crossing the middle sections.</summary>
    private static float? Clearance(World world, FoundBridge b, AddIssue add)
    {
        float water = float.MaxValue, road = float.MaxValue;
        (int X, int Z) waterAt = default, roadAt = default;
        int last = b.Where.Pieces - 1;
        for (int piece = 0; piece <= last; piece++)
        {
            bool middle = piece != 0 && piece != last;
            for (int d = 0; d < BridgeOp.Piece; d++)
                for (int across = -BridgeOp.Width / 2; across < BridgeOp.Width / 2; across++)
                {
                    var (x, z) = Cell(b.Where, b.Where.Start + piece * BridgeOp.Piece + d, across);
                    if (!InWorld(world, x, z)) continue;
                    float g = World.HeightToMetres(world.Height[x, z]);
                    if (world.Water?[x, z] is > 0 and var w && g < w && b.Underside - w < water) (water, waterAt) = (b.Underside - w, (x, z));
                    if (middle && IsRoad(world, x, z) && b.Underside - g < road) (road, roadAt) = (b.Underside - g, (x, z));
                }
        }
        if (water < BridgeOp.Clearance - 0.01f)
            add(IssueSeverity.Warning, BridgeIssue.Clearance, $"beams are {M(water)} m above the water (want {BridgeOp.Clearance} m).", waterAt.X, waterAt.Z);
        if (road < RoadClearance - 0.01f)
            add(IssueSeverity.Warning, BridgeIssue.Clearance, $"beams are {M(road)} m above a road passing under (want {M(RoadClearance)} m).", roadAt.X, roadAt.Z);
        float least = MathF.Min(water, road);
        return least == float.MaxValue ? null : least;
    }

    /// <summary>
    /// Whether a prefab's footprint (<paramref name="cells"/>) stands on a bridge deck: its centre lies inside one of the
    /// <paramref name="bridgeCells"/>. Deck wrecks are such prefabs (navonly, so the game's RWG never picks them).
    /// </summary>
    internal static bool OnBridge(GridRect cells, IReadOnlyList<GridRect> bridgeCells)
    {
        int mx = (cells.X0 + cells.X1) / 2, mz = (cells.Z0 + cells.Z1) / 2;
        foreach (var r in bridgeCells)
            if (mx >= r.X0 && mx < r.X1 && mz >= r.Z0 && mz < r.Z1) return true;
        return false;
    }
}
