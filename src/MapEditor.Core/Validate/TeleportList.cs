using System.Globalization;
using System.Text;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;

namespace MapEditor.Core.Validate;

/// <summary>
/// One place to go in the game: the issues there, the issues fixed there (#59), and the teleport command that takes you to
/// it. <see cref="X"/>/<see cref="Z"/> are grid cells; <see cref="Command"/> is null for issues with no place on the map.
/// </summary>
public sealed record TeleportSpot(int? X, int? Z, string? Command, IReadOnlyList<ValidationIssue> Issues)
{
    /// <summary>The standing grid cell and facing used by <see cref="Command"/>, if there is one.</summary>
    public (int X, int Z, Facing? Facing)? StandingCell { get; init; }

    /// <summary>Issues fixed with a Fix button at this spot, to check in the game.</summary>
    public IReadOnlyList<FixedIssue> Fixed { get; init; } = [];
}

/// <summary>
/// Turns Validate's issues into a list of teleport commands, to check a world in the game spot by spot. Each command puts
/// the player on the ground <see cref="Standoff"/> m from the issue, on the first clear side (south, west, north, east:
/// on the map, out of the water, outside every prefab), looking at it.
/// </summary>
public static class TeleportList
{
    /// <summary>How far from the issue the player stands, in metres.</summary>
    public const int Standoff = 15;

    /// <summary>Issues this close to a spot already in the list (in metres, along each axis) share it.</summary>
    public const int SamePlace = 5;

    /// <summary>The sides to stand on, in order: the offset from the spot and the way to look back at it.</summary>
    private static readonly (int Dx, int Dz, Facing Look)[] Sides =
        [(0, -Standoff, Facing.N), (-Standoff, 0, Facing.E), (0, Standoff, Facing.S), (Standoff, 0, Facing.W)];

    /// <summary>
    /// The spots for <paramref name="issues"/>, in their order, then the spots for <paramref name="fixedIssues"/> whose fix
    /// still holds (sharing a spot with an issue nearby), then one spot for the issues with no place.
    /// </summary>
    public static IReadOnlyList<TeleportSpot> Build(World world, IEnumerable<ValidationIssue> issues, PrefabLibrary prefabs,
        IEnumerable<FixedIssue>? fixedIssues = null)
    {
        var current = issues.ToList();
        var placed = new List<(int X, int Z, List<ValidationIssue> Issues, List<FixedIssue> Fixed)>();
        var unplaced = new List<ValidationIssue>();
        int SpotNear(int x, int z) => placed.FindIndex(s => Math.Abs(s.X - x) <= SamePlace && Math.Abs(s.Z - z) <= SamePlace);
        foreach (var issue in current)
        {
            if (issue is not { X: { } x, Z: { } z })
            {
                unplaced.Add(issue);
                continue;
            }
            int i = SpotNear(x, z);
            if (i >= 0) placed[i].Issues.Add(issue);
            else placed.Add((x, z, [issue], []));
        }
        foreach (var done in fixedIssues ?? [])
        {
            if (done is not { X: { } x, Z: { } z } || !done.Holds(world, current)) continue;
            int i = SpotNear(x, z);
            if (i >= 0) placed[i].Fixed.Add(done);
            else placed.Add((x, z, [], [done]));
        }

        var spots = new List<TeleportSpot>();
        foreach (var spot in placed)
        {
            var standing = StandingCellFor(world, spot.X, spot.Z, prefabs);
            string? command = TeleportCommand.ForCell(world, standing.X, standing.Z, standing.Facing);
            spots.Add(new TeleportSpot(spot.X, spot.Z, command, spot.Issues) { StandingCell = standing, Fixed = spot.Fixed });
        }
        if (unplaced.Count > 0) spots.Add(new TeleportSpot(null, null, null, unplaced));
        return spots;
    }

    /// <summary>The first clear cell to stand on, facing the issue, or the issue cell itself if no side is clear.</summary>
    private static (int X, int Z, Facing? Facing) StandingCellFor(World world, int x, int z, PrefabLibrary prefabs)
    {
        foreach (var (dx, dz, look) in Sides)
        {
            int sx = x + dx, sz = z + dz;
            if (IsClear(world, sx, sz, prefabs)) return (sx, sz, look);
        }
        return (Math.Clamp(x, 0, world.Size - 1), Math.Clamp(z, 0, world.Size - 1), null);
    }

    private static bool IsClear(World world, int x, int z, PrefabLibrary prefabs)
    {
        if (!world.Height.Contains(x, z)) return false;
        if (world.Water is { } water && water[x, z] > 0 && World.HeightToMetres(world.Height[x, z]) <= water[x, z] + 0.5f) return false;
        return WorldQuery.PrefabAt(world, world.GridToWorld(x), world.GridToWorld(z), prefabs) is null;
    }

    /// <summary>The list as Markdown: a table with one row per spot, then the issues with no place.</summary>
    public static string Markdown(World world, IReadOnlyList<TeleportSpot> spots)
    {
        var sb = new StringBuilder();
        sb.Append("# Teleport list: ").Append(world.Name).Append("\n\n");
        sb.Append(CultureInfo.InvariantCulture, $"Made {DateTime.Now:yyyy-MM-dd HH:mm} from Validate. ");
        sb.Append(CultureInfo.InvariantCulture,
            $"In the game, press F1 to open the console, paste one command and press Enter. You land on the ground about {Standoff} m from the issue, looking at it.\n\n");

        var placed = spots.Where(s => s.Command is not null).ToList();
        if (placed.Count > 0)
        {
            sb.Append("| # | Issue | Command |\n|---|---|---|\n");
            for (int i = 0; i < placed.Count; i++)
            {
                string issues = string.Join("<br>", placed[i].Issues.Select(Describe).Concat(placed[i].Fixed.Select(Describe)));
                sb.Append(CultureInfo.InvariantCulture, $"| {i + 1} | {issues} | `{placed[i].Command}` |\n");
            }
        }
        else sb.Append("No issue has a place to go to.\n");

        var unplaced = spots.Where(s => s.Command is null).SelectMany(s => s.Issues).ToList();
        if (unplaced.Count > 0)
        {
            sb.Append("\n## No place to go to\n\n");
            foreach (var issue in unplaced) sb.Append("- ").Append(Describe(issue)).Append('\n');
        }
        return sb.ToString();
    }

    private static string Describe(ValidationIssue issue) => issue.Severity + ": " + Cell(issue.Message);

    private static string Describe(FixedIssue done) => "Fixed: " + Cell(done.Issue.Message) + " " + Cell(done.Done);

    /// <summary>Text made safe for one table cell.</summary>
    private static string Cell(string text) => text.Replace("|", "\\|").Replace("\r", "").Replace("\n", " ");
}
