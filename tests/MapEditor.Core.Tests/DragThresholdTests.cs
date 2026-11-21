using MapEditor.Core.Edit;

namespace MapEditor.Core.Tests;

public sealed class DragThresholdTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(3.9, 5.9)]
    [InlineData(-3.9, -5.9)]
    public void BothAxesBelowMinimumDoNotStartDrag(double dx, double dy)
    {
        var threshold = new DragThreshold(10, 20, 4, 6);

        Assert.False(threshold.Update(10 + dx, 20 + dy));
        Assert.False(threshold.Passed);
    }

    [Theory]
    [InlineData(4, 0)]
    [InlineData(5, 0)]
    [InlineData(0, 6)]
    [InlineData(0, 7)]
    [InlineData(-4, 0)]
    [InlineData(0, -6)]
    public void EitherAxisAtOrAboveMinimumStartsDrag(double dx, double dy)
    {
        var threshold = new DragThreshold(10, 20, 4, 6);

        Assert.True(threshold.Update(10 + dx, 20 + dy));
        Assert.True(threshold.Passed);
    }

    [Fact]
    public void DragStaysStartedWhenPointerReturnsToPress()
    {
        var threshold = new DragThreshold(10, 20, 4, 6);

        Assert.True(threshold.Update(14, 20));
        Assert.True(threshold.Update(10, 20));
    }
}
