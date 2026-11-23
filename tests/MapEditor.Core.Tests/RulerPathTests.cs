using System.Numerics;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Tests;

public sealed class RulerPathTests
{
    private static World FlatWorld()
    {
        const int Size = 16;
        var height = new Grid<ushort>(Size, Size);
        Array.Fill<ushort>(height.Data, 40 * 257);
        return new World { Name = "Ruler test", Size = Size, Height = height };
    }

    [Fact]
    public void Flat_leg_uses_horizontal_distance()
    {
        var path = RulerPath.Measure(FlatWorld(), [new(1, 1), new(4, 5)]);

        var leg = Assert.Single(path.Legs);
        Assert.Equal(5, leg.LengthMetres);
        Assert.Equal(0, leg.HeightDifferenceMetres);
        Assert.Equal(0, leg.SlopePercent);
    }

    [Fact]
    public void Slopes_use_signed_height_change_over_horizontal_distance()
    {
        var world = FlatWorld();
        world.Height[5, 1] = 41 * 257;
        var path = RulerPath.Measure(world, [new(1.2f, 1.8f), new(5.2f, 1.8f), new(1.2f, 1.8f)]);

        Assert.Equal([40f, 41f, 40f], path.HeightsMetres);
        Assert.Equal(4, path.Legs[0].LengthMetres, precision: 5);
        Assert.Equal(1, path.Legs[0].HeightDifferenceMetres);
        Assert.Equal(25, path.Legs[0].SlopePercent, precision: 4);
        Assert.Equal(-1, path.Legs[1].HeightDifferenceMetres);
        Assert.Equal(-25, path.Legs[1].SlopePercent, precision: 4);
    }

    [Fact]
    public void Total_adds_all_legs_including_a_repeat_point()
    {
        var path = RulerPath.Measure(FlatWorld(), [new(1, 1), new(4, 5), new(4, 5), new(4, 8)]);

        Assert.Equal(8, path.TotalLengthMetres);
        Assert.Equal(0, path.Legs[1].LengthMetres);
        Assert.Equal(0, path.Legs[1].SlopePercent);
    }

    [Fact]
    public void Map_edge_uses_the_last_cell_without_shortening_the_leg()
    {
        var world = FlatWorld();
        world.Height[0, 15] = 35 * 257;
        world.Height[15, 15] = 43 * 257;
        var path = RulerPath.Measure(world, [new Vector2(0, 16), new Vector2(16, 16)]);

        Assert.Equal([35f, 43f], path.HeightsMetres);
        Assert.Equal(16, path.TotalLengthMetres);
        Assert.Equal(50, Assert.Single(path.Legs).SlopePercent);
    }
}
