namespace MapEditor.Core.Validate;

/// <summary>Tracks the current teleport stop among the issues with a location.</summary>
public sealed class IssueStepper
{
    private TeleportSpot[] _spots = [];

    public int Count => _spots.Length;
    public int Position { get; private set; }
    public TeleportSpot? Current => Position > 0 ? _spots[Position - 1] : null;
    public TeleportSpot? Previous => Position > 1 ? _spots[Position - 2] : null;
    public TeleportSpot? Next => Position < Count ? _spots[Position] : null;

    /// <summary>Replaces the stops and keeps the closest issue location to the previous stop.</summary>
    public void Rebuild(IReadOnlyList<TeleportSpot> spots)
    {
        var current = Current;
        _spots = [.. spots.Where(s => s.Command is not null && s.StandingCell is not null && s.Issues.Count > 0)];
        Position = 0;
        if (current is not { X: { } x, Z: { } z } || Count == 0) return;

        double nearestDistance = double.PositiveInfinity;
        for (int i = 0; i < Count; i++)
        {
            double dx = (double)_spots[i].X!.Value - x;
            double dz = (double)_spots[i].Z!.Value - z;
            double distance = dx * dx + dz * dz;
            if (distance >= nearestDistance) continue;
            nearestDistance = distance;
            Position = i + 1;
        }
    }

    /// <summary>Records a completed step if the stop still belongs to the current list.</summary>
    public bool TryMoveTo(TeleportSpot spot)
    {
        int index = Array.FindIndex(_spots, candidate => ReferenceEquals(candidate, spot));
        if (index < 0) return false;
        Position = index + 1;
        return true;
    }
}
