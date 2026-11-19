namespace MapEditor.Core.Validate;

/// <summary>Tracks the current teleport stop among the issues with a location.</summary>
public sealed class IssueStepper
{
    private TeleportSpot[] _spots = [];
    private readonly Dictionary<ValidationIssue, TeleportSpot> _stopsByIssue = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<(int? X, int? Z)> _visited = [];
    private int? _resumeIndex;

    public int Count => _spots.Length;
    public int Position { get; private set; }
    public TeleportSpot? Current => Position > 0 ? _spots[Position - 1] : null;
    public TeleportSpot? Previous
    {
        get
        {
            int index = (_resumeIndex ?? (Position - 1)) - 1;
            return index >= 0 ? _spots[index] : null;
        }
    }

    public TeleportSpot? Next
    {
        get
        {
            int index = _resumeIndex ?? Position;
            return index < Count ? _spots[index] : null;
        }
    }

    /// <summary>Keeps the current location if it survives. Otherwise resumes at the first unvisited location.</summary>
    public void Rebuild(IReadOnlyList<TeleportSpot> spots)
    {
        var current = Current;
        _spots = [.. spots.Where(s => s.Command is not null && s.StandingCell is not null && s.Issues.Count > 0)];
        _stopsByIssue.Clear();
        foreach (var spot in _spots)
            foreach (var issue in spot.Issues)
                _stopsByIssue[issue] = spot;
        Position = current is null ? 0 : IndexOf(current) + 1;
        if (Count == 0)
        {
            _visited.Clear();
            _resumeIndex = null;
        }
        else if (Position == 0 && (current is not null || _resumeIndex is not null))
            ResumeAtUnvisited();
    }

    /// <summary>Gets the teleport stop for a row in the current validation list.</summary>
    public TeleportSpot? StopFor(ValidationIssue issue) => _stopsByIssue.GetValueOrDefault(issue);

    /// <summary>Finds the next or previous row with a different teleport stop in the displayed order.</summary>
    public ValidationIssue? AdjacentIssue(IReadOnlyList<ValidationIssue> issues, int selectedIndex, bool next)
    {
        var selectedStop = StopFor(issues[selectedIndex]);
        int direction = next ? 1 : -1;
        for (int i = selectedIndex + direction; i >= 0 && i < issues.Count; i += direction)
        {
            var issue = issues[i];
            if (StopFor(issue) is { } stop && !ReferenceEquals(stop, selectedStop)) return issue;
        }
        return null;
    }

    /// <summary>Starts stepping from a selected row. Selecting a row does not visit it in game.</summary>
    public void SelectIssue(ValidationIssue issue)
    {
        if (StopFor(issue) is not { } stop) return;
        Position = IndexOf(stop) + 1;
        _resumeIndex = null;
    }

    /// <summary>Records a completed visit while keeping a newer selection.</summary>
    public void RecordVisit(TeleportSpot spot)
    {
        _visited.Add((spot.X, spot.Z));
        if (_resumeIndex is not null) ResumeAtUnvisited();
    }

    /// <summary>Records a completed teleport. Returns true if the location is still in the list.</summary>
    public bool TryMoveTo(TeleportSpot spot)
    {
        _visited.Add((spot.X, spot.Z));
        Position = IndexOf(spot) + 1;
        if (Position == 0) ResumeAtUnvisited();
        else _resumeIndex = null;
        return Position > 0;
    }

    private int IndexOf(TeleportSpot spot) => Array.FindIndex(_spots, candidate => candidate.X == spot.X && candidate.Z == spot.Z);

    private void ResumeAtUnvisited()
    {
        int first = Array.FindIndex(_spots, spot => !_visited.Contains((spot.X, spot.Z)));
        _resumeIndex = first < 0 ? Count : first;
    }
}
