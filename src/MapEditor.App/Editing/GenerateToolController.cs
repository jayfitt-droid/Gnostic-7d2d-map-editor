using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MapEditor.App.Map;
using MapEditor.Core.Edit;
using MapEditor.Core.Generate;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.App.Editing;

/// <summary>
/// The Generate panel's map tools: painting plains, hills or mountains onto 256 m tiles, and dragging the area a
/// stage regenerates. Painting changes the generator's settings, not the world, so it isn't on the undo history.
/// </summary>
public sealed class GenerateToolController
{
    private readonly MapView _map;
    private readonly MainViewModel _vm;
    private Point? _dragStart;
    private Rect? _drag;

    public GenerateToolController(MapView map, MainViewModel vm)
    {
        _map = map;
        _vm = vm;
        _map.ToolInput += OnToolInput;
    }

    public World? World { get; set; }


    /// <summary>The Place features tool was clicked at this world position (metres east, north).</summary>
    public event EventHandler<(float X, float Z)>? FeatureClicked;

    public bool IsDragging => _dragStart is not null;

    private GenerateViewModel G => _vm.Generate;

    private void OnToolInput(object? sender, ToolInputEventArgs e)
    {
        if (World is not { } world || _vm.SelectedTool is not (EditorTool.GenerateArea or EditorTool.Features)) return;
        if (G.IsRunning && e.Phase == ToolPhase.Down) return;

        if (_vm.SelectedTool == EditorTool.Features)
        {
            if (e.Phase == ToolPhase.Down) FeatureClicked?.Invoke(this, ((float)e.MapPoint.X, (float)(world.Size - e.MapPoint.Y)));
            return;
        }

        switch (e.Phase)
        {
            case ToolPhase.Down:
                _dragStart = e.MapPoint;
                _drag = new Rect(e.MapPoint, e.MapPoint);
                break;
            case ToolPhase.Move when _dragStart is { } start:
                _drag = new Rect(start, e.MapPoint);
                break;
            case ToolPhase.Up when _dragStart is { } start:
                var tiles = TilesUnder(world, new Rect(start, e.MapPoint));
                _dragStart = null;
                _drag = null;
                if (!tiles.IsEmpty)
                {
                    G.Area = tiles;
                    G.WholeMap = false;
                    _vm.Status = $"Area set: {G.AreaText}. Press Generate in the Generate panel.";
                }
                break;
        }
        Refresh();
    }

    /// <summary>The tile under a map point (map y from the north edge; tile z from the south).</summary>
    private static (int X, int Z) Tile(World world, Point map) =>
        ((int)Math.Floor(map.X / TerrainStage.TileSize), (int)Math.Floor((world.Size - map.Y) / TerrainStage.TileSize));

    /// <summary>Every tile the rectangle touches.</summary>
    private static GridRect TilesUnder(World world, Rect map)
    {
        int tiles = world.Size / TerrainStage.TileSize;
        var (x0, z1) = Tile(world, map.TopLeft);
        var (x1, z0) = Tile(world, new Point(map.Right - 0.01, map.Bottom - 0.01));
        return new GridRect(x0, z0, x1 + 1, z1 + 1).Clamp(tiles, tiles);
    }

    /// <summary>The map rectangle of a tile area.</summary>
    public static Rect MapRect(World world, GridRect tiles) =>
        new(tiles.X0 * TerrainStage.TileSize, world.Size - tiles.Z1 * TerrainStage.TileSize,
            (tiles.X1 - tiles.X0) * TerrainStage.TileSize, (tiles.Z1 - tiles.Z0) * TerrainStage.TileSize);

    /// <summary>
    /// Shows the area to generate while the Generate panel is open or one of its tools is in use.
    /// </summary>
    public void Refresh()
    {
        bool show = World is not null && (_vm.ShowSidePanel && _vm.SidePanelTab == MainViewModel.GenerateTab
                                          || _vm.SelectedTool is EditorTool.GenerateArea or EditorTool.Features);
        if (!show || World is not { } world)
        {
            _map.TerrainTiles = null;
            _map.TownPreviews = [];
            return;
        }
        Rect? area = !G.WholeMap && G.Area is { } a ? MapRect(world, a) : null;
        _map.TerrainTiles = new TerrainTileOverlay(area, _drag);
        var previews = G.Planned.Select(t => Preview(world, t)).ToList();
        // The picked feature (a whole long river) gets a box round each of its stamps.
        if (G.SelectedFeature is { } picked)
            foreach (var f in G.Settings.PlacedFeatures.Where(f => f == picked || picked.Chain != 0 && f.Chain == picked.Chain))
            {
                var cells = f.Cells.Aggregate(GridRect.Empty, (r, c) => r.Union(new GridRect(c[0], c[1], c[0] + 1, c[1] + 1))).Scale(TownSiteStage.Cell);
                previews.Add(new TownPreview(new Rect(cells.X0, world.Size - cells.Z1, cells.X1 - cells.X0, cells.Z1 - cells.Z0), [], [], [], [], []));
            }
        _map.TownPreviews = previews;
    }

    /// <summary>A planned town as the map draws it: tiles tinted by district, POIs and traders.</summary>
    private TownPreview Preview(World world, PlannedTown town)
    {
        Rect R(GridRect r) => new(r.X0, world.Size - r.Z1, r.X1 - r.X0, r.Z1 - r.Z0);
        var plan = town.Plan;
        var tiles = plan.Tiles.Where(t => t.District is not null).Select(t =>
        {
            var (r, g, b) = t.District!.PreviewColour;
            return new TownTilePreview(R(t.Cells), Color.FromRgb(r, g, b), t.Exits);
        }).ToList();
        var pois = new List<Rect>();
        var traders = new List<Rect>();
        foreach (var p in plan.Prefabs)
        {
            if (_vm.Prefabs.Find(p.Name) is not { IsStreetTile: false, IsPart: false } info) continue;
            var (sx, sz) = info.Footprint(p.Rotation);
            (info.IsTrader ? traders : pois).Add(_map.FootprintMapRect(p.Position.X, p.Position.Z, sx, sz));
        }
        return new TownPreview(Rect.Empty, tiles, pois, traders, [], []);
    }
}
