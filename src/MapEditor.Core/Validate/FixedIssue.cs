using MapEditor.Core.Model;

namespace MapEditor.Core.Validate;

/// <summary>
/// A Validate issue that a Fix button fixed (#59), kept so the teleport list still sends you there to check the fix in the
/// game. <see cref="X"/>/<see cref="Z"/> are the grid cell to check: where a moved spawn is now, else where the issue was.
/// <see cref="Done"/> is the fix's status line.
/// </summary>
public sealed record FixedIssue(ValidationIssue Issue, string Done, int? X, int? Z)
{
    /// <summary>The record for <paramref name="issue"/>, fixed by its <see cref="ValidationIssue.Fix"/> with the status line <paramref name="done"/>.</summary>
    public static FixedIssue Of(World world, ValidationIssue issue, string done)
    {
        if (issue.Fix is { Spawn: { } spawn })
            return new FixedIssue(issue, done, world.WorldToGrid((int)MathF.Floor(spawn.X)), world.WorldToGrid((int)MathF.Floor(spawn.Z)));
        return new FixedIssue(issue, done, issue.X, issue.Z);
    }

    /// <summary>Whether this record and <paramref name="other"/> are the same fix on the same prefab or spawn.</summary>
    public bool SameFixAs(IssueFix? other) =>
        Issue.Fix is { } fix && other is not null && fix.Kind == other.Kind
        && ReferenceEquals(fix.Prefab, other.Prefab) && ReferenceEquals(fix.Spawn, other.Spawn);

    /// <summary>
    /// Whether the fix still holds: what it fixed is still in <paramref name="world"/>, and Validate's
    /// <paramref name="current"/> issues don't offer the same fix again (it was undone).
    /// </summary>
    public bool Holds(World world, IEnumerable<ValidationIssue> current)
    {
        if (Issue.Fix is not { } fix) return false;
        if (fix.Prefab is { } p && !world.Prefabs.Contains(p)) return false;
        if (fix.Spawn is { } s && !world.Spawns.Contains(s)) return false;
        return !current.Any(i => SameFixAs(i.Fix));
    }
}
