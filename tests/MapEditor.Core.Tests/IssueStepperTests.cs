using MapEditor.Core.GameData;
using MapEditor.Core.Model;
using MapEditor.Core.Validate;

namespace MapEditor.Core.Tests;

public sealed class IssueStepperTests
{
    [Fact]
    public void Next_and_previous_follow_the_displayed_order_from_the_selected_row()
    {
        var first = Issue("A", 20);
        var before = Issue("B", 60);
        var selected = Issue("A2", 22);
        var after = Issue("C", 90);
        ValidationIssue[] rows = [first, before, selected, after];
        var stepper = StepperFor(rows);

        Assert.Same(stepper.StopFor(first), stepper.StopFor(selected));
        Assert.Same(after, stepper.AdjacentIssue(rows, 2, next: true));
        Assert.Same(before, stepper.AdjacentIssue(rows, 2, next: false));
    }

    [Fact]
    public void Stepping_skips_unmapped_rows_and_rows_that_share_the_selected_stop()
    {
        var before = Issue("Before", 20);
        var after = Issue("After", 90);
        var unmapped = new ValidationIssue(IssueSeverity.Warning, "No trader");
        ValidationIssue[] rows = [before, Issue("Shared 1", 59), Issue("Selected", 60), unmapped, Issue("Shared 2", 61), after];
        var stepper = StepperFor(rows);

        Assert.Null(stepper.StopFor(unmapped));
        Assert.Same(after, stepper.AdjacentIssue(rows, 2, next: true));
        Assert.Same(before, stepper.AdjacentIssue(rows, 2, next: false));
        Assert.Same(rows[4], stepper.AdjacentIssue(rows, 3, next: true));
    }

    [Fact]
    public void Without_a_selection_only_next_finds_the_first_mapped_row()
    {
        var first = Issue("First mapped", 20);
        ValidationIssue[] rows = [new(IssueSeverity.Warning, "No trader"), first, Issue("Second mapped", 70)];
        var stepper = StepperFor(rows);

        Assert.Same(first, stepper.AdjacentIssue(rows, -1, next: true));
        Assert.Null(stepper.AdjacentIssue(rows, -1, next: false));
        Assert.Null(stepper.AdjacentIssue([], -1, next: true));
    }

    [Fact]
    public void Stepping_does_not_wrap_at_either_end()
    {
        ValidationIssue[] rows = [Issue("First", 20), Issue("Last", 70)];
        var stepper = StepperFor(rows);

        Assert.Null(stepper.AdjacentIssue(rows, 0, next: false));
        Assert.Null(stepper.AdjacentIssue(rows, 1, next: true));
    }

    [Fact]
    public void Rebuild_only_maps_the_rows_from_the_new_results()
    {
        var removed = Issue("Removed", 20);
        var kept = Issue("Kept", 70);
        var stepper = StepperFor([removed, kept]);
        Assert.NotNull(stepper.StopFor(removed));
        var refreshed = kept with { };

        stepper.Rebuild(Build([refreshed]));

        Assert.Null(stepper.StopFor(removed));
        Assert.Null(stepper.StopFor(kept));
        Assert.NotNull(stepper.StopFor(refreshed));
    }

    private static ValidationIssue Issue(string message, int coord) => new(IssueSeverity.Warning, message, coord, coord);

    private static IssueStepper StepperFor(ValidationIssue[] issues)
    {
        var stepper = new IssueStepper();
        stepper.Rebuild(Build(issues));
        return stepper;
    }

    private static IReadOnlyList<TeleportSpot> Build(ValidationIssue[] issues)
    {
        var world = new World { Name = "Test World", Size = 128, Height = new Grid<ushort>(128, 128) };
        return TeleportList.Build(world, issues, PrefabLibrary.Empty);
    }
}
