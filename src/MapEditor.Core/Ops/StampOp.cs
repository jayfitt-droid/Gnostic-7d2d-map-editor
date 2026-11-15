using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;

namespace MapEditor.Core.Ops;

/// <param name="X">Centre, grid column (metres from the west edge).</param>
/// <param name="Z">Centre, grid row (metres from the south edge).</param>
/// <param name="SizeMetres">Width of the stamp on the ground.</param>
/// <param name="RotationDegrees">Clockwise rotation as seen on the north-up map.</param>
/// <param name="Strength">Multiplies the stamp's alpha (0..1); the game uses 1 for the first stamp of a cluster.</param>
/// <param name="Additive">Add the stamp's height instead of blending toward it.</param>
/// <param name="HeightScale">Multiplies the stamp's height values.</param>
/// <param name="HeightOffset">Added to the stamp's height values (metres).</param>
/// <param name="ApplyWater">
/// Also fill water: the stamp's water mask if it has one (lakes), otherwise its alpha (as the game does for
/// water_border). Water only stays where the terrain ends up at or below the surface.
/// </param>
public sealed record StampPlacement(
    float X,
    float Z,
    float SizeMetres,
    float RotationDegrees,
    float Strength = 1,
    bool Additive = false,
    float HeightScale = 1,
    float HeightOffset = 0,
    bool ApplyWater = false,
    byte WaterLevel = StampOp.DefaultWaterLevel);

/// <summary>What one stamp did to the world.</summary>
/// <param name="Rect">The cells it could reach.</param>
/// <param name="Lowered">The most it took off any cell, in metres.</param>
/// <param name="Raised">The most it added to any cell, in metres.</param>
/// <param name="WaterCells">Cells that are under water now and were dry before.</param>
public sealed record StampResult(GridRect Rect, float Lowered, float Raised, long WaterCells);

/// <summary>Draws a game stamp into the heightmap the way RWG does (StampManager.DrawStamp).</summary>
public static class StampOp
{
    /// <summary>RWG's water surface height (WorldBuilder.WaterHeight).</summary>
    public const byte DefaultWaterLevel = 30;

    /// <summary>
    /// Lake, river and ocean-border stamps. They are made to hold water: without it a lake is only a shallow bowl
    /// that is easy to miss, so the Stamp tool always fills water for them (#24).
    /// </summary>
    public static bool IsWaterStamp(string? name) =>
        name is not null && (name.StartsWith("lake", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("river", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("water", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// <see cref="Apply"/>, and measures what the stamp did: how far it lowered and raised the ground and how much
    /// new water it left, for the Stamp tool's status line.
    /// </summary>
    public static StampResult ApplyAndMeasure(World world, StampImage stamp, StampPlacement p, EditSession session)
    {
        var reach = Reach(p, world.Height.Width, world.Height.Height);
        if (reach.IsEmpty) return new StampResult(GridRect.Empty, 0, 0, 0);
        int w = reach.X1 - reach.X0, h = reach.Z1 - reach.Z0;
        var height = new ushort[w * h];
        var wet = new bool[w * h];
        for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                height[x + z * w] = world.Height[reach.X0 + x, reach.Z0 + z];
                wet[x + z * w] = IsWet(world, reach.X0 + x, reach.Z0 + z);
            }

        var rect = Apply(world, stamp, p, session);
        float lowered = 0, raised = 0;
        long water = 0;
        for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                float change = (world.Height[reach.X0 + x, reach.Z0 + z] - height[x + z * w]) / 257f;
                if (change < -lowered) lowered = -change;
                if (change > raised) raised = change;
                if (!wet[x + z * w] && IsWet(world, reach.X0 + x, reach.Z0 + z)) water++;
            }
        return new StampResult(rect, lowered, raised, water);
    }

    private static bool IsWet(World world, int x, int z) =>
        world.Water is { } water && water[x, z] > 0 && world.Height[x, z] / 257f - 0.5f <= water[x, z];

    /// <summary>The cells a stamp can reach: the rotated square fits inside a circle of radius half*sqrt(2).</summary>
    public static GridRect Reach(StampPlacement p, int width, int height) =>
        GridRect.AroundCircle(p.X, p.Z, p.SizeMetres / 2 * 1.4143f).Clamp(width, height);

    public static GridRect Apply(World world, StampImage stamp, StampPlacement p, EditSession session)
    {
        var rect = Reach(p, world.Height.Width, world.Height.Height);
        if (rect.IsEmpty) return GridRect.Empty;
        Grid<byte>? waterGrid = null;
        if (p.ApplyWater)
        {
            waterGrid = world.Water ??= new Grid<byte>(world.Size, world.Size);
            session.Touch(waterGrid, rect);
        }
        session.Touch(world.Height, rect);
        return Draw(world.Height, waterGrid, stamp, p);
    }

    /// <summary>
    /// Draws a stamp straight into <paramref name="height"/> (and <paramref name="waterGrid"/> when the placement fills
    /// water), with no undo: the caller has already touched what it reaches.
    /// </summary>
    public static GridRect Draw(Grid<ushort> height, Grid<byte>? waterGrid, StampImage stamp, StampPlacement p)
    {
        float scale = p.SizeMetres / stamp.Size;
        var rect = Reach(p, height.Width, height.Height);
        if (rect.IsEmpty) return GridRect.Empty;
        bool water = p.ApplyWater && waterGrid is not null;

        // Map rotation is clockwise on a north-up view; grid z points north, so that is a negative angle in grid space.
        float angle = -p.RotationDegrees * MathF.PI / 180f;
        float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
        float centre = stamp.Size / 2f;

        // Rows are independent, so a large stamp (RWG's plains reach 2 km) is drawn on every core.
        void Row(int z)
        {
            var row = height.Row(z);
            float dz = z - p.Z;
            for (int x = rect.X0; x < rect.X1; x++)
            {
                float dx = x - p.X;
                // Inverse-rotate the destination cell into stamp pixel space.
                float sx = (dx * cos + dz * sin) / scale + centre;
                float sy = (-dx * sin + dz * cos) / scale + centre;
                float alpha = stamp.SampleAlpha(sx, sy) * p.Strength;
                if (alpha >= 1e-5f)
                {
                    float target = stamp.SampleHeight(sx, sy) * p.HeightScale + p.HeightOffset;
                    float h = row[x] / 257f;
                    h = p.Additive ? h + target * alpha : h + (target - h) * Math.Min(alpha, 1f);
                    row[x] = TerrainBrush.ToRaw(h);
                }
                if (water && waterGrid![x, z] == 0 && (stamp.HasWater ? stamp.SampleWater(sx, sy) : stamp.SampleAlpha(sx, sy)) >= 1e-5f)
                    waterGrid[x, z] = p.WaterLevel;
            }
        }
        if ((long)(rect.X1 - rect.X0) * (rect.Z1 - rect.Z0) < 1 << 16)
            for (int z = rect.Z0; z < rect.Z1; z++) Row(z);
        else
            Parallel.For(rect.Z0, rect.Z1, Row);

        if (water) FinalizeWater(height, waterGrid!, rect);
        return rect;
    }

    /// <summary>Keeps water only where the terrain is at or below its surface (WorldBuilder.FinalizeWater).</summary>
    public static void FinalizeWater(Grid<ushort> height, Grid<byte> water, GridRect rect)
    {
        for (int z = rect.Z0; z < rect.Z1; z++)
        {
            var h = height.Row(z);
            var w = water.Row(z);
            for (int x = rect.X0; x < rect.X1; x++)
                if (w[x] > 0 && h[x] / 257f - 0.5f > w[x]) w[x] = 0;
        }
    }
}
