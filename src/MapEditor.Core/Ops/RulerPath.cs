using System.Numerics;
using MapEditor.Core.Model;

namespace MapEditor.Core.Ops;

/// <summary>A leg's horizontal length, signed height change and signed slope.</summary>
public readonly record struct RulerLeg(double LengthMetres, double HeightDifferenceMetres, double SlopePercent);

/// <summary>Heights in point order, the legs between them, and the sum of their horizontal lengths.</summary>
public sealed record RulerMeasurement(IReadOnlyList<float> HeightsMetres, IReadOnlyList<RulerLeg> Legs, double TotalLengthMetres);

/// <summary>Measures a path over the terrain without changing the world.</summary>
public static class RulerPath
{
    /// <summary>
    /// Points are in grid metres from the south-west corner. Each height comes from the nearest cell centre, with
    /// points on the outer edge using the last cell. Distances use the original points. A zero-length leg has zero slope.
    /// </summary>
    public static RulerMeasurement Measure(World world, IReadOnlyList<Vector2> points)
    {
        var heights = new float[points.Count];
        var legs = new RulerLeg[Math.Max(0, points.Count - 1)];
        double total = 0;
        var height = world.Height;
        for (int i = 0; i < points.Count; i++)
        {
            var point = points[i];
            int x = (int)Math.Clamp(point.X, 0, height.Width - 1);
            int z = (int)Math.Clamp(point.Y, 0, height.Height - 1);
            heights[i] = World.HeightToMetres(height[x, z]);
            if (i == 0) continue;

            var previous = points[i - 1];
            double length = double.Hypot((double)point.X - previous.X, (double)point.Y - previous.Y);
            double rise = (double)heights[i] - heights[i - 1];
            legs[i - 1] = new RulerLeg(length, rise, length == 0 ? 0 : rise / length * 100);
            total += length;
        }
        return new RulerMeasurement(heights, legs, total);
    }
}
