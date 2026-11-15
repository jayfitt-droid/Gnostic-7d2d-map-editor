using System.Globalization;
using System.Text;
using MapEditor.Core.Model;

namespace MapEditor.Core.Validate;

/// <summary>The text report the command line prints for a validated world.</summary>
public static class ValidationReport
{
    /// <summary>
    /// A summary line (bridges found, issues by severity and bridge-issue kind), a table of each bridge's measurements,
    /// then one line per issue. With <paramref name="bridgesOnly"/> only bridge issues are listed (and counted).
    /// </summary>
    public static string Format(World world, IReadOnlyList<ValidationIssue> issues, IReadOnlyList<BridgeReport> bridges, bool bridgesOnly)
    {
        var shown = bridgesOnly ? issues.Where(i => i.Kind is not null).ToList() : [.. issues];
        var sb = new StringBuilder();

        int errors = shown.Count(i => i.Severity == IssueSeverity.Error), warnings = shown.Count(i => i.Severity == IssueSeverity.Warning);
        var kinds = BridgeIssue.All
            .Select(k => (Kind: k, Count: shown.Count(i => i.Kind == k)))
            .Where(k => k.Count > 0)
            .Select(k => $"{k.Kind} {k.Count}");
        string byKind = string.Join(", ", kinds);
        sb.Append(CultureInfo.InvariantCulture, $"{bridges.Count} bridge(s) found. {Count(errors, "error")}, {Count(warnings, "warning")}");
        if (byKind.Length > 0) sb.Append(CultureInfo.InvariantCulture, $" (bridge issues: {byKind})");
        sb.AppendLine(".");

        if (bridges.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("  #  Centre (x, z)     Axis  Length  Deck   End step  Grade  Clearance  Issues");
            for (int i = 0; i < bridges.Count; i++)
            {
                var b = bridges[i];
                var centre = b.Bridge.Where.At(b.Bridge.Where.Start + b.Bridge.Length / 2f);
                string at = string.Create(CultureInfo.InvariantCulture, $"({centre.X - world.Size / 2:0}, {centre.Y - world.Size / 2:0})");
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"{i + 1,3}  {at,-16}  {(b.Bridge.AlongZ ? "N-S" : "E-W"),-4}  {b.Bridge.Length,5} m  {b.Bridge.DeckLevel,3} m  {Metres(b.WorstStep),8}  {Percent(b.SteepestGrade),5}  {Metres(b.MinClearance),9}  {b.Issues.Count,6}"));
            }
            sb.AppendLine("  End step = the larger gap between the deck and the ground at either end. Grade = steepest road grade within 150 m of an end.");
            sb.AppendLine("  Clearance = least room under the beams over water or a road.");
        }

        if (shown.Count > 0) sb.AppendLine();
        foreach (var i in shown)
            sb.AppendLine(Line(world, i));
        sb.AppendLine(shown.Count == 0 ? "No problems found." : $"{shown.Count} issue(s).");
        return sb.ToString();
    }

    /// <summary>One issue as a line: its severity, message and, when it has a place, its world coordinates.</summary>
    public static string Line(World world, ValidationIssue i) =>
        $"{i.Severity,-8} {i.Message}{(i.HasLocation ? $"  (at {world.GridToWorld(i.X!.Value)}, {world.GridToWorld(i.Z!.Value)})" : "")}";

    private static string Count(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";

    private static string Metres(float? v) => v is { } m ? m.ToString("0.0", CultureInfo.InvariantCulture) + " m" : "-";

    private static string Percent(float? v) => v is { } g ? (g * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%" : "-";
}
