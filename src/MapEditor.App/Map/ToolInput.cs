using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MapEditor.Core.GameData;

namespace MapEditor.App.Map;

public enum ToolPhase
{
    Down,
    Move,
    Up,
}

/// <summary>A tool press/drag/release at <see cref="MapPoint"/> (metres from the north-west corner).</summary>
public sealed class ToolInputEventArgs(ToolPhase phase, Point mapPoint, ModifierKeys modifiers, int clickCount = 1) : EventArgs
{
    public ToolPhase Phase { get; } = phase;
    public Point MapPoint { get; } = mapPoint;
    public ModifierKeys Modifiers { get; } = modifiers;

    /// <summary>2 for the second press of a double-click.</summary>
    public int ClickCount { get; } = clickCount;

    /// <summary>The drag ended because the map lost the mouse capture.</summary>
    public bool CaptureLost { get; init; }
}

/// <summary>The Multi-select tool's box and selected items, in map coordinates.</summary>
public sealed record MultiSelectionOverlay(Rect? Box,
    IReadOnlyList<(Rect Bounds, int Front)> Prefabs, IReadOnlyList<(Point Centre, float Rotation)> Spawns);

/// <summary>The drawn coast and box outlines in Land mode, in map coordinates.</summary>
public sealed record LandPreview(ImageSource? Coast, IReadOnlyList<(Rect Bounds, bool CutsWater)> Boxes, int? Selected)
{
    /// <summary>The corners and side midpoints used for both drawing and picking resize handles.</summary>
    internal static Point[] ResizeHandles(Rect box) =>
    [
        box.TopLeft, box.TopRight, box.BottomLeft, box.BottomRight,
        new(box.Left + box.Width / 2, box.Top), new(box.Left + box.Width / 2, box.Bottom),
        new(box.Left, box.Top + box.Height / 2), new(box.Right, box.Top + box.Height / 2),
    ];
}

/// <summary>A road being drawn: its points in map coordinates and its full width in metres.</summary>
public sealed record RoadPreview(IReadOnlyList<Point> MapPoints, double WidthMetres, bool Asphalt);

/// <summary>How a <see cref="BridgePreview"/> is drawn: being aimed, planned (Enter builds it), selected, or being dragged.</summary>
public enum BridgePreviewKind
{
    Aiming,
    Planned,
    Selected,
    Dragging,
}

/// <summary>
/// A bridge about to be built or being edited: its deck in map coordinates, its pieces, a label (length, deck height and
/// ramps) and the ramps it would get (#43).
/// </summary>
public sealed record BridgePreview(Rect MapRect, bool AlongZ, int Pieces, string Label, BridgePreviewKind Kind)
{
    public IReadOnlyList<RampPreview> Ramps { get; init; } = [];
}

/// <summary>A ramp in a bridge preview: the road it would pave, in map coordinates, and whether it needs a look (steep, or stopped short).</summary>
public sealed record RampPreview(Rect MapRect, bool Warning);

/// <summary>Where the road tool would put its next point for the mouse, and whether that is on a road's end.</summary>
public sealed record RoadSnap(Point MapPoint, bool OnEnd);

/// <summary>
/// Outline shown under the mouse: a circle (brushes) or a rotated square (stamps), with the stamp seen from above
/// inside it when <see cref="Picture"/> is set.
/// </summary>
public sealed record BrushCursor(double SizeMetres, bool Square = false, double RotationDegrees = 0, ImageSource? Picture = null);

/// <summary>
/// A prefab about to be placed or moved: its footprint in map coordinates, the way its front faces
/// (0 north, 1 west, 2 south, 3 east; -1 unknown), whether it has warnings, the lot it snaps into, and the prefab
/// and its turn, so the ghost can show the prefab from above. <see cref="Replaces"/> are the POIs it covers, which
/// placing it there replaces.
/// </summary>
public sealed record PrefabGhost(Rect MapRect, int Front, bool Warning, Rect? Lot = null, bool LotFits = true,
    PrefabInfo? Info = null, int Rotation = 0)
{
    public IReadOnlyList<Rect> Replaces { get; init; } = [];
}

/// <summary>A planned town tile: its cell in map coordinates, its district's preview colour and its street exits (bit 0 north, then clockwise).</summary>
public sealed record TownTilePreview(Rect MapRect, Color Colour, int Exits);

/// <summary>
/// The Town tool's selection and, once planned, the town it would build: tiles, POIs and traders, the prefabs it
/// replaces, and the link roads (all in map coordinates). While dragging, <see cref="Drag"/> is the rectangle
/// under the mouse and <see cref="Cells"/> the whole 150 m cells it snaps to; <see cref="TooSmall"/> when it
/// holds none. <see cref="OffRoads"/> are link roads the user turned off, and <see cref="Handles"/> shows the
/// box's resize handles.
/// </summary>
public sealed record TownPreview(
    Rect Selection,
    IReadOnlyList<TownTilePreview> Tiles,
    IReadOnlyList<Rect> Pois,
    IReadOnlyList<Rect> Traders,
    IReadOnlyList<Rect> Removed,
    IReadOnlyList<IReadOnlyList<Point>> Roads)
{
    public Rect? Drag { get; init; }
    public IReadOnlyList<Rect> Cells { get; init; } = [];
    public bool TooSmall { get; init; }
    public IReadOnlyList<IReadOnlyList<Point>> OffRoads { get; init; } = [];
    public bool Handles { get; init; }
}

/// <summary>
/// The Generate panel's view of the 256 m tiles: the types painted by hand (strong), the types the last run gave
/// (faint, when <see cref="Plan"/> is set), and the area to generate or the one being dragged, in map coordinates.
/// Arrays are x + z * tiles with z from the south.
/// </summary>
public sealed record TerrainTileOverlay(int WorldSize, MapEditor.Core.Generate.TerrainType[]? Painted,
    MapEditor.Core.Generate.TerrainType[]? Plan, Rect? Area, Rect? Drag);
