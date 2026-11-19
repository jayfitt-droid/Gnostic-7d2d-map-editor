using MapEditor.Core.GameData;
using MapEditor.Core.Model;
using MapEditor.Core.Validate;

namespace MapEditor.Core.Tests;

public sealed class IssueStepperTests
{
    [Fact]
    public void Steps_in_list_order_and_skips_issues_without_a_location_and_fixed_only_stops()
    {
        var first = new ValidationIssue(IssueSeverity.Warning, "A", 35, 35);
        var nearby = new ValidationIssue(IssueSeverity.Warning, "B", 37, 36);
        var last = new ValidationIssue(IssueSeverity.Warning, "C", 95, 95);
        var noLocation = new ValidationIssue(IssueSeverity.Error, "No trader");
        var fixedOnly = new TeleportSpot(60, 60, "teleport -4 -19 n", []) { StandingCell = (60, 45, Facing.N) };
        var stepper = new IssueStepper();
        stepper.Rebuild([fixedOnly, .. Build(first, noLocation, nearby, last)]);

        Assert.Equal(2, stepper.Count);
        Assert.Equal(0, stepper.Position);
        Assert.Null(stepper.Previous);

        var firstStop = Assert.IsType<TeleportSpot>(stepper.Next);
        Assert.Equal([first, nearby], firstStop.Issues);
        Assert.True(stepper.TryMoveTo(firstStop));
        Assert.Equal(1, stepper.Position);
        Assert.Same(firstStop, stepper.Current);
        Assert.Null(stepper.Previous);

        var lastStop = Assert.IsType<TeleportSpot>(stepper.Next);
        Assert.Same(last, Assert.Single(lastStop.Issues));
        Assert.True(stepper.TryMoveTo(lastStop));
        Assert.Equal(2, stepper.Position);
        Assert.Null(stepper.Next);
        Assert.Same(firstStop, stepper.Previous);
        Assert.True(stepper.TryMoveTo(firstStop));
        Assert.Equal(1, stepper.Position);
    }

    [Fact]
    public void Rebuild_keeps_the_closest_issue_and_rejects_targets_from_the_old_list()
    {
        var stepper = new IssueStepper();
        stepper.Rebuild(Build(new(IssueSeverity.Warning, "Old", 80, 80), new(IssueSeverity.Warning, "Old next", 110, 110)));
        Assert.True(stepper.TryMoveTo(stepper.Next!));
        var oldNext = stepper.Next!;

        var nearer = new ValidationIssue(IssueSeverity.Warning, "Nearer", 75, 62);
        stepper.Rebuild(Build(new(IssueSeverity.Warning, "Far", 24, 25), nearer, new(IssueSeverity.Warning, "Near", 101, 81)));

        Assert.Equal(2, stepper.Position);
        Assert.Same(nearer, Assert.Single(stepper.Current!.Issues));
        Assert.False(stepper.TryMoveTo(oldNext));
        Assert.Equal(2, stepper.Position);

        stepper.Rebuild(Build(new ValidationIssue(IssueSeverity.Error, "No trader")));
        Assert.Equal(0, stepper.Count);
        Assert.Equal(0, stepper.Position);
        Assert.Null(stepper.Current);
        Assert.Null(stepper.Next);
    }

    private static IReadOnlyList<TeleportSpot> Build(params ValidationIssue[] issues)
    {
        var world = new World { Name = "Test World", Size = 128, Height = new Grid<ushort>(128, 128) };
        return TeleportList.Build(world, issues, PrefabLibrary.Empty);
    }
}
