using MapEditor.Core.Theming;
using static MapEditor.Core.Theming.WindowPlacement;

namespace MapEditor.Core.Tests;

public sealed class WindowPlacementTests
{
    private static readonly WindowPlacement.Monitor Primary = new(new Bounds(0, 0, 1920, 1040), 1);
    private static readonly WindowPlacement.Monitor Secondary = new(new Bounds(-1600, 40, 1600, 860), 1);
    private static readonly WindowPlacement.Monitor HighDpi = new(new Bounds(1920, 0, 2560, 1400), 1.5);

    [Fact]
    public void RestoresWindowOnPrimaryMonitor()
    {
        Assert.Equal(new Bounds(120, 70, 1200, 700), Restore(120, 70, 1200, 700, [Primary, Secondary]));
    }

    [Fact]
    public void RestoresWindowAtNegativeDesktopPosition()
    {
        Assert.Equal(new Bounds(-1480, 90, 1200, 700), Restore(-1480, 90, 1200, 700, [Primary, Secondary]));
    }

    [Fact]
    public void UsesDefaultWhenMonitorIsGone()
    {
        Assert.Null(Restore(-1480, 90, 1200, 700, [Primary]));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0.0, null)]
    [InlineData(null, 0.0)]
    public void UsesDefaultWhenNoPositionWasSaved(double? left, double? top)
    {
        Assert.Null(Restore(left, top, 1200, 700, [Primary, Secondary]));
    }

    [Fact]
    public void ShiftsPartlyOffScreenWindowIntoWorkArea()
    {
        Assert.Equal(new Bounds(-1600, 40, 1200, 700), Restore(-1700, 30, 1200, 700, [Primary, Secondary]));
    }

    [Fact]
    public void UsesDefaultWhenTooLittleOfTitleBarIsVisible()
    {
        Assert.Null(Restore(1870, 60, 1200, 700, [Primary]));
        Assert.Null(Restore(100, -24, 1200, 700, [Primary]));
    }

    [Fact]
    public void ShrinksOversizedWindowToWorkArea()
    {
        Assert.Equal(Secondary.WorkArea, Restore(-1480, 60, 2100, 1200, [Primary, Secondary]));
    }

    [Fact]
    public void ScalesSizeAtTargetDpiWithoutScalingDesktopOrigin()
    {
        Assert.Equal(new Bounds(2100, 100, 1800, 1050), Restore(2100, 100, 1200, 700, [Primary, HighDpi]));
    }

    [Fact]
    public void FitsScaledWindowToHighDpiMonitor()
    {
        Assert.Equal(new Bounds(2680, 350, 1800, 1050), Restore(3500, 500, 1200, 700, [Primary, HighDpi]));
    }

    [Fact]
    public void UsesMonitorWithMoreOfTitleBarAcrossMixedDpiBoundary()
    {
        Assert.Equal(new Bounds(1920, 100, 1800, 1050), Restore(1750, 100, 1200, 700, [Primary, HighDpi]));
    }
}
