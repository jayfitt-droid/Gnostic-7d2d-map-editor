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
    /// <summary>The Generate panel's tool: drags the area a stage regenerates.</summary>
    GenerateArea,
    /// <summary>The Generate panel's tool: places lakes, rivers, canyons and craters by hand and picks them.</summary>
    Features,
    /// <summary>Selects and edits a group of items on the map.</summary>
    MultiSelect,
    /// <summary>Measures distance and height on the map.</summary>
    Ruler,
}

/// <summary>
/// A tool as shown on the ribbon: its icon, keyboard shortcut and a one-line hint shown under the ribbon while it
/// is active.
/// </summary>
public sealed record ToolItem(EditorTool Tool, string Label, string Shortcut, Geometry Icon, string Hint)
{
    /// <summary>The name under the tool's icon in the Modern theme's toolbar.</summary>
    public string ShortLabel => Tool switch
    {
        EditorTool.Biome => "Biome",
        EditorTool.Radiation => "Radiation",
        EditorTool.Water => "Water",
        EditorTool.Prefab => "Prefabs",
        EditorTool.Spawn => "Spawns",
        EditorTool.MultiSelect => "Multi",
        _ => Label,
    };

    public string Display => $"{Label}  ({Shortcut})";

    public string ToolTip => $"{Label} ({Shortcut})\n{Hint}";
}
