using System.Windows.Media;

namespace MapEditor.App;

public enum EditorTool
{
    Select,
    Raise,
    Lower,
    Flatten,
    Smooth,
    Noise,
    Stamp,
    Biome,
    Radiation,
    Water,
    Road,
    /// <summary>Draws a bridge: click the start, click the end; the pieces, footings and ramps are built as one edit.</summary>
    Bridge,
    Prefab,
    Trader,
    Spawn,
    Town,
    /// <summary>The Generate panel's tool: paints plains, hills or mountains onto 256 m tiles.</summary>
    TerrainTypes,
    /// <summary>The Generate panel's tool: drags the area a stage regenerates.</summary>
    GenerateArea,
    /// <summary>The Generate panel's tool: picks, moves and adds planned towns.</summary>
    TownSites,
    /// <summary>The Generate panel's tool: places lakes, rivers, canyons and craters by hand and picks them.</summary>
    Features,
    /// <summary>The Generate panel's tool: places wilderness POIs by hand and picks them.</summary>
    Wilderness,
}

/// <summary>
/// A tool as shown on the ribbon: its icon, keyboard shortcut and a one-line hint shown under the ribbon while it
/// is active.
/// </summary>
public sealed record ToolItem(EditorTool Tool, string Label, string Shortcut, Geometry Icon, string Hint)
{
    public string Display => $"{Label}  ({Shortcut})";

    public string ToolTip => $"{Label} ({Shortcut})\n{Hint}";
}
