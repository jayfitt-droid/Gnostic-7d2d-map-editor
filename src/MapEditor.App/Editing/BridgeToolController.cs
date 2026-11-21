using System.Numerics;
using System.Windows;
using MapEditor.App.Map;
using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.App.Editing;

/// <summary>
/// The Bridge tool: click the start, click the end, and Enter builds the bridge with ramps as one undo step. Clicking one
/// of the project's bridges selects it: drag it along its line to move it, drag an end to lengthen or shorten it,
/// Delete takes it down. The logic is in <see cref="BridgeTool"/> (Core); this turns mouse input into calls to it,
/// each through <see cref="EditRunner"/>.
/// </summary>
public sealed class BridgeToolController
{
    private enum Drag
    {
        None,
        Move,
        Start,
        End,
    }

    private readonly MapView _map;
    private readonly MainViewModel _vm;
    private readonly EditRunner _edits;

    // A bridge being planned: the first click, then (once the second click is made) the snapped line and its height.
    private Vector2? _first;
    private BridgeLine? _plan;
    private int _planY;

    // The selected bridge and a drag of it.
    private PlacedBridge? _selected;
    private Drag _drag;
    private Vector2 _dragFrom;
    private BridgeLine? _dragLine;

    private bool _settingDeck;

    public BridgeToolController(MapView map, MainViewModel vm, EditRunner edits)
    {
        _map = map;
        _vm = vm;
        _edits = edits;
        _map.ToolInput += OnToolInput;
    }

    private World? _world;

    public World? World
    {
        get => _world;
        set
        {
            _world = value;
            Reset();
        }
    }

    /// <summary>Drops any plan, selection or drag (the tool was put down or the world changed).</summary>
    public void Reset()
    {
        _first = null;
        _plan = null;
        _selected = null;
        _drag = Drag.None;
        _dragLine = null;
        Refresh();
    }

    /// <summary>The project's list of the user's bridges.</summary>
    public Func<List<PlacedBridge>>? Bridges { get; set; }

    /// <summary>True while a bridge is being dragged (so undo and save wait).</summary>
    public bool IsDragging => _drag != Drag.None;

    private List<PlacedBridge> List => Bridges?.Invoke() ?? [];

    private int Size => World?.Size ?? 0;

    private static Vector2 ToGrid(World world, Point map) => new((float)map.X, (float)(world.Size - map.Y));

    private void OnToolInput(object? sender, ToolInputEventArgs e)
    {
        if (World is not { } world || _vm.SelectedTool != EditorTool.Bridge) return;
        var grid = ToGrid(world, e.MapPoint);
        switch (e.Phase)
        {
            case ToolPhase.Down when !_vm.IsBusy:
                Press(world, grid);
                break;
            case ToolPhase.Move when _drag != Drag.None && e.DragThresholdPassed:
                _dragLine = DraggedLine(world, grid);
                _map.InvalidateVisual();
                break;
            case ToolPhase.Up when _drag != Drag.None:
                Release(world, grid, e.DragThresholdPassed);
                break;
        }
    }

    private void Press(World world, Vector2 grid)
    {
        // The second click of a plan sets its end.
        if (_first is { } first && _plan is null)
        {
            if (BridgeTool.Snap(first, grid, world.Size) is not { } line)
            {
                _vm.Status = "There isn't room for a bridge of two pieces (18 m) there.";
                _first = null;
            }
            else
            {
                _plan = line;
                _planY = BridgeTool.AutoY(world, line);
            }
            Refresh();
            return;
        }

        // Otherwise a click on one of the project's bridges selects it, and a press on one that is selected grabs it.
        if (BridgeTool.Find(List, grid) is { } hit)
        {
            _first = null;
            _plan = null;
            _selected = hit;
            _drag = ZoneAt(hit, grid);
            _dragFrom = grid;
            Refresh();
            return;
        }

        // Empty ground starts a new plan.
        _selected = null;
        _plan = null;
        _first = grid;
        Refresh();
    }

    /// <summary>The part of a bridge a press at <paramref name="grid"/> grabs: an end to resize it, else the middle to move it.</summary>
    private Drag ZoneAt(PlacedBridge bridge, Vector2 grid)
    {
        var line = bridge.Where;
        float along = line.AlongZ ? grid.Y : grid.X;
        float zone = Math.Clamp(12 / (float)_map.Scale, 3, line.Length / 3f);
        if (along - line.Start < zone) return Drag.Start;
        if (line.End - along < zone) return Drag.End;
        return Drag.Move;
    }

    private BridgeLine? DraggedLine(World world, Vector2 grid)
    {
        if (_selected is not { } bridge) return null;
        var line = bridge.Where;
        float now = line.AlongZ ? grid.Y : grid.X, then = line.AlongZ ? _dragFrom.Y : _dragFrom.X;
        return _drag switch
        {
            Drag.Move => BridgeTool.Move(line, (int)MathF.Round(now - then), world.Size),
            Drag.Start => BridgeTool.DragEnd(line, startEnd: true, now, world.Size),
            Drag.End => BridgeTool.DragEnd(line, startEnd: false, now, world.Size),
            _ => null,
        };
    }

    private void Release(World world, Vector2 grid, bool moved)
    {
        // Work out where it was dragged to before the drag is dropped: DraggedLine reads the drag's kind.
        var drag = _drag;
        var target = moved ? DraggedLine(world, grid) : null;
        _drag = Drag.None;
        _dragLine = null;
        if (_selected is { } bridge && target is { } line && line != bridge.Where)
            Rebuild(world, bridge, line, bridge.Y, drag == Drag.Move ? "Move bridge" : "Resize bridge");
        Refresh();
    }

    /// <summary>Takes the bridge down and builds it again at a new place, length or height, as one undo step.</summary>
    private void Rebuild(World world, PlacedBridge bridge, BridgeLine line, int y, string name)
    {
        if (Kit() is not { } kit) return;
        PlacedBridge? made = null;
        if (RunEdit(world, name, s => made = BridgeTool.Replace(world, kit, List, bridge, line, y, s, _vm.BridgeWrecks)))
        {
            _selected = made;
            _vm.Status = $"{name}: {BridgeTool.Describe(line, made!.DeckLevel)}.";
        }
    }

    /// <summary>
    /// Runs one bridge edit and redraws the map where it reached: the footings and ramps change the height and road
    /// layers, whose tiles are cached, so without this they keep their old picture until another edit redraws them.
    /// </summary>
    private bool RunEdit(World world, string name, Action<EditSession> change)
    {
        var reached = GridRect.Empty;
        bool done = _edits.Run(world, name, s =>
        {
            change(s);
            reached = s.Bounds;
        });
        _map.InvalidateRegion(reached);
        return done;
    }

    private BridgeKit? Kit()
    {
        if (BridgeKit.From(_vm.Prefabs) is { } kit) return kit;
        _vm.Status = $"The game's {BridgeOp.Tile} and {BridgeOp.End} prefabs weren't found. Check the game folder.";
        return null;
    }

    // ---- keys and buttons ----

    /// <summary>Builds the planned bridge. Returns whether there was one.</summary>
    public bool Build()
    {
        if (World is not { } world || _plan is not { } line) return false;
        if (Kit() is not { } kit) return true;
        PlacedBridge? made = null;
        if (!RunEdit(world, "Bridge", s => made = BridgeTool.Add(world, kit, List, line, _planY, s, _vm.BridgeWrecks))) return true;
        _first = null;
        _plan = null;
        _selected = made;
        _vm.Status = $"Built a bridge: {BridgeTool.Describe(line, made!.DeckLevel)}. Ctrl+Z takes it out.";
        Refresh();
        return true;
    }

    /// <summary>Selects a bridge of the project's (from a click on one of its pieces with another tool).</summary>
    public void Select(PlacedBridge bridge)
    {
        _first = null;
        _plan = null;
        _selected = bridge;
        Refresh();
    }

    /// <summary>Esc: drops the plan or drag, else the selection. Returns whether there was anything to drop.</summary>
    public bool Cancel()
    {
        if (_drag != Drag.None)
        {
            _drag = Drag.None;
            _dragLine = null;
        }
        else if (_first is not null || _plan is not null)
        {
            _first = null;
            _plan = null;
            _vm.Status = "Bridge cancelled.";
        }
        else if (_selected is not null)
            _selected = null;
        else
            return false;
        Refresh();
        return true;
    }

    /// <summary>
    /// Rebuild all: takes every one of the project's bridges down and builds it again in place with the current deck
    /// height and ramp rules, as one undo step.
    /// </summary>
    public void RebuildAll()
    {
        if (World is not { } world) return;
        if (List.Count == 0)
        {
            _vm.Status = "There are no bridges drawn with the Bridge tool to rebuild.";
            return;
        }
        if (Kit() is not { } kit) return;
        (int Rebuilt, int Dropped) done = default;
        if (RunEdit(world, "Rebuild bridges", s => done = BridgeTool.RebuildEvery(world, kit, List, s, _vm.BridgeWrecks)))
        {
            _selected = null;
            _vm.Status = $"Rebuilt {done.Rebuilt} bridge{(done.Rebuilt == 1 ? "" : "s")} with new ramps and deck heights"
                         + (done.Dropped > 0 ? $"; {done.Dropped} no longer fit and {(done.Dropped == 1 ? "was" : "were")} dropped." : ". Ctrl+Z puts them back as they were.");
        }
        Refresh();
    }

    /// <summary>Delete: takes the selected bridge down, with its footings and ramps, and puts back the ground they replaced.</summary>
    public bool DeleteSelected()
    {
        if (World is not { } world || _selected is not { } bridge) return false;
        if (RunEdit(world, "Delete bridge", s => BridgeTool.Remove(world, List, bridge, s)))
        {
            _selected = null;
            _vm.Status = "Took the bridge down and put back the ground under it.";
        }
        Refresh();
        return true;
    }

    /// <summary>The deck height box was changed: it sets the plan's height, or rebuilds the selected bridge at the new one.</summary>
    public void DeckEdited()
    {
        if (World is not { } world || _settingDeck) return;
        int y = Math.Clamp((int)Math.Round(_vm.BridgeDeck) - BridgeOp.Deck, 0, 255 - BridgeOp.Deck);
        if (_plan is not null)
        {
            _planY = y;
            Refresh();
        }
        else if (_selected is { } bridge && y != bridge.Y)
        {
            Rebuild(world, bridge, bridge.Where, y, "Bridge height");
            Refresh();
        }
    }

    // ---- display ----

    /// <summary>Brings the map, the deck box and the text up to date with the plan and selection.</summary>
    public void Refresh()
    {
        if (World is null || _vm.SelectedTool != EditorTool.Bridge)
        {
            _map.BridgePreviews = null;
            _vm.HasBridge = false;
            return;
        }
        if (_selected is not null && !List.Contains(_selected)) _selected = null;
        _map.BridgePreviews = Previews;

        int? level = _plan is not null ? _planY + BridgeOp.Deck : _selected?.DeckLevel;
        _vm.HasBridge = _plan is not null || _selected is not null;
        if (level is { } l)
        {
            _settingDeck = true;
            _vm.BridgeDeck = l;
            _settingDeck = false;
        }
        _vm.BridgeText = _plan is { } plan
            ? $"{BridgeTool.Describe(plan, _planY + BridgeOp.Deck)}. Change the deck height if you like, then Enter builds it with ramps; Esc cancels."
            : _first is not null
                ? "Click where the bridge ends. It locks to north-south or east-west and whole 9 m pieces."
                : _selected is { } sel
                    ? $"Selected: {BridgeTool.Describe(sel.Where, sel.DeckLevel)}. Drag to move it, drag an end to resize it, Del takes it down."
                    : "Click where the bridge starts, or click a bridge to select it.";
        _map.InvalidateVisual();
    }

    private IReadOnlyList<BridgePreview> Previews(Point mouse)
    {
        if (World is not { } world) return [];
        var list = new List<BridgePreview>();
        if (_selected is { } sel)
            list.Add(Preview(world, sel.Where, BridgePreviewKind.Selected, ""));
        if (_drag != Drag.None && _dragLine is { } ghost && _selected is { } moving)
            list.Add(WithRamps(world, ghost, BridgePreviewKind.Dragging, moving.Y));
        else if (_plan is { } plan)
            list.Add(WithRamps(world, plan, BridgePreviewKind.Planned, _planY));
        else if (_first is { } first && BridgeTool.Snap(first, ToGrid(world, mouse), world.Size) is { } aim)
            list.Add(WithRamps(world, aim, BridgePreviewKind.Aiming, BridgeTool.AutoY(world, aim)));
        return list;
    }

    private static string Label(BridgeLine line, int deck) => $"{line.Pieces} pieces, {line.Length} m, deck {deck} m";

    /// <summary>
    /// A bridge's preview with the ramps it would get, worked out as building them would (<see cref="BridgeTool.PlanRamp"/>),
    /// and their length and grade on a second line of the label (#43).
    /// </summary>
    private BridgePreview WithRamps(World world, BridgeLine line, BridgePreviewKind kind, int y)
    {
        string label = Label(line, y + BridgeOp.Deck);
        if (BridgeKit.From(_vm.Prefabs) is not { } kit) return Preview(world, line, kind, label);
        var plan = new BridgePlan(kit, line.AlongZ, line.Line, line.Start, line.Pieces, y);
        var ramps = new List<RampPreview>();
        var parts = new List<string>();
        foreach (bool atStart in new[] { true, false })
        {
            string side = line.AlongZ ? (atStart ? "S" : "N") : (atStart ? "W" : "E");
            if (BridgeTool.PlanRamp(world, plan, atStart) is not { } ramp)
            {
                parts.Add($"{side}: none (map edge)");
                continue;
            }
            bool warn = ramp.Steep || ramp.Ends == RampEnd.Longest;
            ramps.Add(new RampPreview(RampRect(world, ramp), warn));
            string text = $"{side} {ramp.Length:0} m {ramp.Grade * 100:0}%";
            if (ramp.Ends == RampEnd.Prefab) text += $" to {GameNames.Current.Poi(ramp.Prefab!)}";
            else if (ramp.Ends == RampEnd.Longest) text += " (doesn't reach the ground)";
            parts.Add(text);
        }
        return Preview(world, line, kind, label + Environment.NewLine + "Ramps: " + string.Join(" / ", parts)) with { Ramps = ramps };
    }

    /// <summary>The road a ramp would pave, in map coordinates (y down), as wide as the deck.</summary>
    private static Rect RampRect(World world, RampPlan ramp)
    {
        float half = BridgeOp.Width / 2f;
        double x0 = Math.Min(ramp.From.X, ramp.To.X), x1 = Math.Max(ramp.From.X, ramp.To.X);
        double z0 = Math.Min(ramp.From.Y, ramp.To.Y), z1 = Math.Max(ramp.From.Y, ramp.To.Y);
        if (x1 - x0 < z1 - z0) { x0 -= half; x1 += half; }
        else { z0 -= half; z1 += half; }
        return new Rect(x0, world.Size - z1, x1 - x0, z1 - z0);
    }

    private static BridgePreview Preview(World world, BridgeLine line, BridgePreviewKind kind, string label)
    {
        var r = line.Rect;
        return new BridgePreview(new Rect(r.X0, world.Size - r.Z1, r.X1 - r.X0, r.Z1 - r.Z0), line.AlongZ, line.Pieces, label, kind);
    }

    /// <summary>The bridge ends roads can snap to: this project's bridges and the Highways stage's.</summary>
    public IReadOnlyList<BridgeEnd> Ends(IEnumerable<MapEditor.Core.Generate.HighwayRoad>? stage) => BridgeTool.Ends(List, stage);
}
