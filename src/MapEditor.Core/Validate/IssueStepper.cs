namespace MapEditor.Core.Validate;

/// <summary>Finds teleport stops for the rows in the validation list.</summary>
public sealed class IssueStepper
{
    private readonly Dictionary<ValidationIssue, TeleportSpot> _stopsByIssue = new(ReferenceEqualityComparer.Instance);

    public bool HasStops => _stopsByIssue.Count > 0;

    /// <summary>Replaces the row-to-stop map with the current validation results.</summary>
    public void Rebuild(IReadOnlyList<TeleportSpot> spots)
    {
        _stopsByIssue.Clear();
        foreach (var spot in spots)
        {
            if (spot.Command is null || spot.StandingCell is null) continue;
            foreach (var issue in spot.Issues)
                _stopsByIssue[issue] = spot;
        }
    }

    /// <summary>Gets the teleport stop for a row in the current validation list.</summary>
    public TeleportSpot? StopFor(ValidationIssue issue) => _stopsByIssue.GetValueOrDefault(issue);

    /// <summary>Finds the next or previous row with a different stop. With no selection (-1), only Next has a row.</summary>
    public ValidationIssue? AdjacentIssue(IReadOnlyList<ValidationIssue> issues, int selectedIndex, bool next)
    {
        var selectedStop = selectedIndex >= 0 ? StopFor(issues[selectedIndex]) : null;
        int direction = next ? 1 : -1;
        for (int i = selectedIndex + direction; i >= 0 && i < issues.Count; i += direction)
        {
            var issue = issues[i];
            if (StopFor(issue) is { } stop && !ReferenceEquals(stop, selectedStop)) return issue;
        }
        return null;
    }
}
