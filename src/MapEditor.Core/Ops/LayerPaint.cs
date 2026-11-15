using MapEditor.Core.Edit;
using MapEditor.Core.Model;

namespace MapEditor.Core.Ops;

/// <summary>Round brushes for the coarse painted layers: biomes (W/8) and radiation (W/32).</summary>
public static class LayerPaint
{
    /// <summary>The value RWG writes into radiation.png's red channel for a radiation zone.</summary>
    public const byte RadiationOn = 255;

    /// <summary>Default biome when a world has no biome layer yet (pine forest).</summary>
    public const uint DefaultBiome = 0x004000;

    /// <summary>
    /// Paints biome colour <paramref name="rgb"/> (a biomes.xml biomemapcolor) over the cells whose centres lie
    /// under <paramref name="tip"/> centred on (cx, cz). Returns the changed area in metres.
    /// </summary>
    public static GridRect PaintBiome(World world, float cx, float cz, BrushTip tip, uint rgb, EditSession session)
    {
        if (CantPaintBiome(world, rgb) is not null) return GridRect.Empty;
        world.Biomes ??= NewBiomeLayer(world.Size);
        var layer = world.Biomes;
        int index = layer.Palette.IndexOf(rgb);
        if (index < 0)
        {
            layer.Palette.Add(rgb);
            index = layer.Palette.Count - 1;
        }
        int scale = Math.Max(1, world.Size / layer.Indices.Width);
        return PaintTip(layer.Indices, scale, cx, cz, tip, (byte)index, session);
    }

    /// <summary>
    /// Why biome colour <paramref name="rgb"/> can't be painted, or null if it can. A biome map with more than 255
    /// colours is kept only as its original image: painting would replace the whole map, so it is refused.
    /// </summary>
    public static string? CantPaintBiome(World world, uint rgb)
    {
        if (world.Biomes is null && world.PassthroughImages.ContainsKey(IO.WorldFiles.Biomes))
            return "This world's biome map has more than 255 colours or a size that doesn't fit the world, so it is kept as it was and can't be painted.";
        if (world.Biomes is { } layer && layer.Palette.Count >= 256 && !layer.Palette.Contains(rgb))
            return "The biome map already uses 256 colours; paint with one of the biomes already on it.";
        return null;
    }

    /// <summary>Adds (or with <paramref name="erase"/>, removes) radiation under the brush tip.</summary>
    public static GridRect PaintRadiation(World world, float cx, float cz, BrushTip tip, bool erase, EditSession session)
    {
        if (CantPaintRadiation(world) is not null) return GridRect.Empty;
        world.Radiation ??= new Grid<byte>(Math.Max(1, world.Size / 32), Math.Max(1, world.Size / 32));
        int scale = Math.Max(1, world.Size / world.Radiation.Width);
        return PaintTip(world.Radiation, scale, cx, cz, tip, erase ? (byte)0 : RadiationOn, session);
    }

    /// <summary>
    /// Why radiation can't be painted, or null if it can. A radiation map whose size doesn't fit the world is kept only
    /// as its original image: painting would start a new map and drop it (#16).
    /// </summary>
    public static string? CantPaintRadiation(World world) =>
        world.Radiation is null && world.PassthroughImages.ContainsKey(IO.WorldFiles.Radiation)
            ? "This world's radiation map has a size that doesn't fit the world, so it is kept as it was and can't be painted."
            : null;

    public static IndexedLayer NewBiomeLayer(int worldSize) =>
        new(new Grid<byte>(Math.Max(1, worldSize / 8), Math.Max(1, worldSize / 8)), [DefaultBiome]);

    private static GridRect PaintTip(Grid<byte> grid, int scale, float cx, float cz, BrushTip tip, byte value, EditSession session)
    {
        // Work in cells; a cell is painted when its centre falls inside the tip. Brushes smaller
        // than a cell still paint the cell under the cursor. Falloff does not apply to these layers.
        float gx = cx / scale, gz = cz / scale;
        var cellTip = tip with { Radius = Math.Max(tip.Radius / scale, 0.5f) };
        var rect = cellTip.Bounds(gx, gz).Clamp(grid.Width, grid.Height);
        if (rect.IsEmpty) return GridRect.Empty;

        bool touched = false;
        for (int z = rect.Z0; z < rect.Z1; z++)
        {
            float dz = z + 0.5f - gz;
            var row = grid.Row(z);
            for (int x = rect.X0; x < rect.X1; x++)
            {
                if (cellTip.Distance(x + 0.5f - gx, dz) > 1 || row[x] == value) continue;
                if (!touched)
                {
                    session.Touch(grid, rect, scale);
                    touched = true;
                }
                row[x] = value;
            }
        }
        // Floor, not a cast: a cursor just west or south of the map is outside it, not in cell 0 (#22).
        int cxCell = (int)MathF.Floor(gx), czCell = (int)MathF.Floor(gz);
        if (!touched && grid.Contains(cxCell, czCell) && grid[cxCell, czCell] != value)
        {
            session.Touch(grid, rect, scale);
            grid[cxCell, czCell] = value;
            touched = true;
        }
        return touched ? rect.Scale(scale) : GridRect.Empty;
    }
}
