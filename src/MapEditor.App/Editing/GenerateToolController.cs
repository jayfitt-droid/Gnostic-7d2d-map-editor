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
    private bool _erase;

    public GenerateToolController(MapView map, MainViewModel vm)
    {
        _map = map;
        _vm = vm;
        _map.ToolInput += OnToolInput;
    }

    public World? World { get; set; }

    /// <summary>Raised when painted tiles change, so the project can be marked as changed.</summary>
    public event EventHandler? Painted;

    /// <summary>A planned town was dragged by whole cells (east, north).</summary>
    public event EventHandler<(TownSite Site, int Dx, int Dz)>? SiteMoved;

    /// <summary>A rectangle was dragged on empty ground to add a town on these cells (world 150 m grid).</summary>
    public event EventHandler<GridRect>? SiteAdded;

    /// <summary>The Place features tool was clicked at this world position (metres east, north).</summary>
    public event EventHandler<(float X, float Z)>? FeatureClicked;

    /// <summary>The Place wilderness POIs tool was clicked at this world position (grid metres east, north).</summary>
    public event EventHandler<(float X, float Z)>? WildernessClicked;

    private TownSite? _moving;
    private (int X, int Z) _moveFrom;

    public bool IsDragging => _dragStart is not null;

    private GenerateViewModel G => _vm.Generate;

    private void OnToolInput(object? sender, ToolInputEventArgs e)
    {
        if (World is not { } world || _vm.SelectedTool is not (EditorTool.TerrainTypes or EditorTool.GenerateArea or EditorTool.TownSites or EditorTool.Features or EditorTool.Wilderness)) return;
        if (G.IsRunning && e.Phase == ToolPhase.Down) return;

        if (_vm.SelectedTool == EditorTool.TerrainTypes)
        {
            if (e.Phase == ToolPhase.Down) _erase = e.Modifiers.HasFlag(ModifierKeys.Shift);
            if (e.Phase is ToolPhase.Down or ToolPhase.Move)
            {
                var (x, z) = Tile(world, e.MapPoint);
                if (G.PaintTile(x, z, _erase ? TerrainType.None : G.PaintType))
                {
                    Refresh();
                    Painted?.Invoke(this, EventArgs.Empty);
                }
            }
            return;
        }

        if (_vm.SelectedTool == EditorTool.Features)
        {
            if (e.Phase == ToolPhase.Down) FeatureClicked?.Invoke(this, ((float)e.MapPoint.X, (float)(world.Size - e.MapPoint.Y)));
            return;
        }

        if (_vm.SelectedTool == EditorTool.Wilderness)
        {
            if (e.Phase == ToolPhase.Down) WildernessClicked?.Invoke(this, ((float)e.MapPoint.X, (float)(world.Size - e.MapPoint.Y)));
            return;
        }

        if (_vm.SelectedTool == EditorTool.TownSites)
        {
            TownSitesInput(world, e);
            Refresh();
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

    /// <summary>
    /// The town sites review: a press on a planned town picks it and drags it by whole cells; a press on empty ground
    /// drags out a rectangle for a new town.
    /// </summary>
    private void TownSitesInput(World world, ToolInputEventArgs e)
    {
        var cell = CellAt(world, e.MapPoint);
        switch (e.Phase)
        {
            case ToolPhase.Down:
                _moving = G.Planned.FirstOrDefault(t => t.Plan.Tiles.Any(x => (x.Cx, x.Cz) == cell))?.Site;
                G.SelectedSite = _moving;
                _moveFrom = cell;
                _dragStart = e.MapPoint;
                _drag = _moving is null ? new Rect(e.MapPoint, e.MapPoint) : null;
                break;
            case ToolPhase.Move when _moving is { } site:
                int dx = cell.X - _moveFrom.X, dz = cell.Z - _moveFrom.Z;
                var box = site.CellBounds;
                var m = new GridRect(box.X0 + dx, box.Z0 + dz, box.X1 + dx, box.Z1 + dz).Scale(TownSiteStage.Cell);
                _drag = new Rect(m.X0, world.Size - m.Z1, m.X1 - m.X0, m.Z1 - m.Z0);
                break;
            case ToolPhase.Move when _dragStart is { } start:
                _drag = new Rect(start, e.MapPoint);
                break;
            case ToolPhase.Up when _moving is { } site:
                _moving = null;
                _dragStart = null;
                _drag = null;
                if (cell != _moveFrom) SiteMoved?.Invoke(this, (site, cell.X - _moveFrom.X, cell.Z - _moveFrom.Z));
                break;
            case ToolPhase.Up when _dragStart is { } start:
                _dragStart = null;
                _drag = null;
                var a = CellAt(world, start);
                var r = new GridRect(Math.Min(a.X, cell.X), Math.Min(a.Z, cell.Z), Math.Max(a.X, cell.X) + 1, Math.Max(a.Z, cell.Z) + 1);
                // A click, not a drag, on empty ground just clears the selection.
                if (Math.Abs(start.X - e.MapPoint.X) > 20 || Math.Abs(start.Y - e.MapPoint.Y) > 20) SiteAdded?.Invoke(this, r);
                break;
        }
    }

    /// <summary>The 150 m town cell under a map point.</summary>
    private static (int X, int Z) CellAt(World world, Point map) =>
        ((int)Math.Floor(map.X / TownSiteStage.Cell), (int)Math.Floor((world.Size - map.Y) / TownSiteStage.Cell));

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
    /// Shows the tiles while the Generate panel is open or one of its tools is in use: painted types, the last plan
    /// (if asked for), and the area when generating one.
    /// </summary>
    public void Refresh()
    {
        bool show = World is not null && (_vm.ShowSidePanel && _vm.SidePanelTab == MainViewModel.GenerateTab
                                          || _vm.SelectedTool is EditorTool.TerrainTypes or EditorTool.GenerateArea or EditorTool.TownSites or EditorTool.Features or EditorTool.Wilderness);
        if (!show || World is not { } world)
        {
            _map.TerrainTiles = null;
            _map.TownPreviews = [];
            return;
        }
        Rect? area = !G.WholeMap && G.Area is { } a ? MapRect(world, a) : null;
        _map.TerrainTiles = new TerrainTileOverlay(world.Size, G.Settings.PaintedTerrain, G.ShowPlan ? G.LastPlan : null, area, _drag);
        var previews = G.Planned.Select(t => Preview(world, t, t.Site == G.SelectedSite)).ToList();
        // The picked feature (a whole long river) gets a box round each of its stamps.
        if (G.SelectedFeature is { } picked)
            foreach (var f in G.Settings.PlacedFeatures.Where(f => f == picked || picked.Chain != 0 && f.Chain == picked.Chain))
            {
                var cells = f.Cells.Aggregate(GridRect.Empty, (r, c) => r.Union(new GridRect(c[0], c[1], c[0] + 1, c[1] + 1))).Scale(TownSiteStage.Cell);
                previews.Add(new TownPreview(new Rect(cells.X0, world.Size - cells.Z1, cells.X1 - cells.X0, cells.Z1 - cells.Z0), [], [], [], [], []));
            }
        // The picked wilderness POI gets a box round it and what its markers spawned.
        if (G.SelectedPoi is { Drawn: true } poi && _vm.Prefabs.Find(poi.Name) is { } info)
        {
            var (sx, sz) = info.Footprint(poi.Rotation);
            previews.Add(new TownPreview(_map.FootprintMapRect(poi.X, poi.Z, sx, sz), [], [], [], [], []));
        }
        _map.TownPreviews = previews;
    }

    /// <summary>A planned town as the map draws it: tiles tinted by district, POIs, traders, and a box when selected.</summary>
    private TownPreview Preview(World world, PlannedTown town, bool selected)
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
        var bounds = plan.Tiles.Count > 0 ? plan.Bounds : town.Site.Bounds;
        return new TownPreview(selected ? R(bounds) : Rect.Empty, tiles, pois, traders, [], []);
    }
}
