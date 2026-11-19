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
    public void Rebuild_keeps_a_surviving_location_and_records_an_in_flight_step()
    {
        var first = new ValidationIssue(IssueSeverity.Warning, "First", 80, 80);
        var second = new ValidationIssue(IssueSeverity.Warning, "Second", 110, 110);
        var stepper = new IssueStepper();
        stepper.Rebuild(Build(first, second));
        Assert.True(stepper.TryMoveTo(stepper.Next!));
        var pending = stepper.Next!;

        stepper.Rebuild(Build(new(IssueSeverity.Warning, "Earlier", 24, 25), first, second));

        Assert.Equal(2, stepper.Position);
        Assert.Same(first, Assert.Single(stepper.Current!.Issues));
        Assert.True(stepper.TryMoveTo(pending));
        Assert.Equal(3, stepper.Position);
        Assert.Same(second, Assert.Single(stepper.Current!.Issues));

        stepper.Rebuild(Build(new ValidationIssue(IssueSeverity.Error, "No trader")));
        Assert.Equal(0, stepper.Count);
        Assert.Equal(0, stepper.Position);
        Assert.Null(stepper.Current);
        Assert.Null(stepper.Next);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Removing_the_current_stop_leaves_the_first_unvisited_stop_for_next(bool visitFirst)
    {
        var a = new ValidationIssue(IssueSeverity.Warning, "A", 20, 20);
        var b = new ValidationIssue(IssueSeverity.Warning, "B", 60, 60);
        var c = new ValidationIssue(IssueSeverity.Warning, "C", 90, 90);
        var stepper = new IssueStepper();
        stepper.Rebuild(visitFirst ? Build(a, b, c) : Build(b, c));
        if (visitFirst) Assert.True(stepper.TryMoveTo(stepper.Next!));
        Assert.True(stepper.TryMoveTo(stepper.Next!));

        var remaining = visitFirst ? Build(a, c) : Build(c);
        stepper.Rebuild(remaining);
        stepper.Rebuild(remaining);

        Assert.Same(c, Assert.Single(Assert.IsType<TeleportSpot>(stepper.Next).Issues));
        Assert.Null(stepper.Current);
        Assert.Equal(0, stepper.Position);
        if (visitFirst) Assert.Same(a, Assert.Single(stepper.Previous!.Issues));
        else Assert.Null(stepper.Previous);
    }

    [Fact]
    public void Shortcuts_follow_the_selected_row_even_when_a_stop_is_grouped_with_an_earlier_row()
    {
        var a = new ValidationIssue(IssueSeverity.Warning, "A", 20, 20);
        var b = new ValidationIssue(IssueSeverity.Warning, "B", 60, 60);
        var grouped = new ValidationIssue(IssueSeverity.Warning, "A2", 22, 22);
        var unmapped = new ValidationIssue(IssueSeverity.Warning, "No trader");
        var c = new ValidationIssue(IssueSeverity.Warning, "C", 90, 90);
        ValidationIssue[] rows = [a, b, grouped, unmapped, c];
        var stepper = new IssueStepper();
        stepper.Rebuild(Build(rows));
        Assert.True(stepper.TryMoveTo(stepper.StopFor(c)!));
        stepper.SelectIssue(grouped);

        Assert.Same(stepper.StopFor(a), stepper.Current);
        Assert.Same(c, stepper.AdjacentIssue(rows, 2, next: true));
        Assert.Same(b, stepper.AdjacentIssue(rows, 2, next: false));
        Assert.Same(c, stepper.AdjacentIssue(rows, 3, next: true));
        Assert.Same(grouped, stepper.AdjacentIssue(rows, 3, next: false));

        stepper.RecordVisit(stepper.StopFor(b)!);
        Assert.Same(stepper.StopFor(a), stepper.Current);
        stepper.Rebuild(Build(b, c, new ValidationIssue(IssueSeverity.Warning, "D", 100, 100)));
        Assert.Equal("D", Assert.Single(Assert.IsType<TeleportSpot>(stepper.Next).Issues).Message);
    }

    private static IReadOnlyList<TeleportSpot> Build(params ValidationIssue[] issues)
    {
        var world = new World { Name = "Test World", Size = 128, Height = new Grid<ushort>(128, 128) };
        return TeleportList.Build(world, issues, PrefabLibrary.Empty);
    }
}
