using System.Windows;
using System.Windows.Input;
using MapEditor.App.Map;
using MapEditor.Core.Edit;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.App.Editing;

/// <summary>Picks and edits a group on the map. A drag previews one transform and applies it on release.</summary>
public sealed class MultiSelectToolController(MapView map, MainViewModel vm, EditRunner edits,
    Func<PrefabInstance, bool> isManualBridgePiece, Func<PrefabInstance, bool> isStageBridgePiece)
{
    private sealed record Gesture(Point Start, bool Shift, PrefabInstance? Prefab, SpawnPoint? Spawn, bool Moving);

    private readonly HashSet<PrefabInstance> _prefabs = [];
    private readonly HashSet<SpawnPoint> _spawns = [];
    private Gesture? _gesture;
    private PrefabGroup? _moving;
    private EditSession? _session;
    private bool _boxing;
    private Point _dragAt;
    private int _turns;

    public bool IsDragging => _gesture is not null;

    public World? World { get; private set; }

    public void SetWorld(World? world)
    {
        World = world;
        ClearSelection();
        Refresh();
    }

    /// <summary>Clears the selection when leaving the tool and drops stale references after world edits.</summary>
    public void Refresh()
    {
        EndGesture();
        if (!vm.IsMultiSelectTool || World is not { } world)
        {
            ClearSelection();
            map.MultiSelection = null;
            return;
        }
        _prefabs.IntersectWith(world.ShownPrefabs);
        _spawns.IntersectWith(world.ShownSpawns);
        ShowSelection(world);
    }

    public void OnToolInput(object? sender, ToolInputEventArgs e)
    {
        if (!vm.IsMultiSelectTool || World is not { } world) return;
        if (e.CaptureLost || vm.IsBusy)
        {
            if (IsDragging) Refresh();
            return;
        }

        if (e.Phase == ToolPhase.Down)
        {
            var at = e.MapPoint;
            var spawn = SpawnHelper.Near(world, (float)at.X, (float)(world.Size - at.Y), (float)Math.Max(4, 8 / map.Scale));
            var prefab = spawn is null ? WorldQuery.PrefabAt(world, world.GridToWorld((int)Math.Floor(at.X)),
                world.GridToWorld((int)Math.Floor(world.Size - at.Y)), vm.Prefabs, info => info is not { IsPart: true }) : null;
            if (prefab is not null && isManualBridgePiece(prefab)) prefab = null;
            bool shift = e.Modifiers.HasFlag(ModifierKeys.Shift);
            bool moving = !shift && (spawn is not null && _spawns.Contains(spawn) || prefab is not null && _prefabs.Contains(prefab));
            _gesture = new Gesture(at, shift, prefab, spawn, moving);
            return;
        }

        if (_gesture is not { } gesture) return;
        var delta = e.MapPoint - gesture.Start;
        if (gesture.Moving)
        {
            if (_moving is null && e.DragThresholdPassed)
            {
                _moving = Capture(world);
                _session = edits.Begin(world, "Move selection");
            }
            _dragAt = e.MapPoint;
            if (_moving is not null)
            {
                if (e.Phase == ToolPhase.Up) Drop(world);
                else PreviewMove(world);
                return;
            }
        }
        else if (_boxing || Math.Abs(delta.X) * map.Scale >= SystemParameters.MinimumHorizontalDragDistance ||
                 Math.Abs(delta.Y) * map.Scale >= SystemParameters.MinimumVerticalDragDistance)
        {
            _boxing = true;
            var box = new Rect(gesture.Start, e.MapPoint);
            if (e.Phase == ToolPhase.Move)
            {
                ShowSelection(world, box);
                return;
            }
            if (!gesture.Shift) ClearSelection();
            var hits = PrefabGroup.InBox(world, vm.Prefabs, box.Left, world.Size - box.Bottom,
                box.Right, world.Size - box.Top, isManualBridgePiece);
            _prefabs.UnionWith(hits.Prefabs);
            _spawns.UnionWith(hits.Spawns);
            EndGesture();
            ShowSelection(world);
            return;
        }

        if (e.Phase != ToolPhase.Up) return;
        if (!gesture.Moving)
        {
            if (!gesture.Shift) ClearSelection();
            if (gesture.Prefab is { } p && !_prefabs.Add(p)) _prefabs.Remove(p);
            if (gesture.Spawn is { } s && !_spawns.Add(s)) _spawns.Remove(s);
        }
        EndGesture();
        ShowSelection(world);
    }

    private PrefabGroup Capture(World world) =>
        new(world, vm.Prefabs, _prefabs, _spawns, p => isManualBridgePiece(p) || isStageBridgePiece(p));

    private (int X, int Z) DragDelta()
    {
        var delta = _dragAt - _gesture!.Start;
        return ((int)Math.Round(delta.X), (int)Math.Round(-delta.Y));
    }

    private void PreviewMove(World world)
    {
        var (dx, dz) = DragDelta();
        ShowSelection(world, layout: _moving!.Layout(dx, dz, _turns));
    }

    private void Drop(World world)
    {
        var group = _moving!;
        var session = _session!;
        var (dx, dz) = DragDelta();
        int turns = _turns;
        EndGesture();
        string? problem = null;
        if ((dx != 0 || dz != 0 || turns != 0) &&
            edits.Step(session, s => problem = group.Apply(dx, dz, turns, vm.PrefabFlatten, replaceCovered: true, s)))
        {
            if (problem is not null) vm.Status = problem;
            else if (edits.Commit(session)) Redraw(session);
        }
        ShowSelection(world);
    }

    /// <summary>Turns the group, or turns its preview while the group is being dragged.</summary>
    public void Rotate(int step)
    {
        if (_moving is not null && World is { } world)
        {
            _turns = (_turns + step) & (_prefabs.Count > 0 ? 3 : 7);
            PreviewMove(world);
            return;
        }
        Run("Rotate selection", (group, session) => group.Apply(0, 0, step, vm.PrefabFlatten, replaceCovered: false, session));
    }

    /// <summary>Raises or lowers the selected prefabs and their riders.</summary>
    public void NudgeHeight(int blocks) =>
        Run(blocks > 0 ? "Raise selection" : "Lower selection", (group, session) => group.NudgeHeight(blocks, session));

    /// <summary>Deletes the selected group as one edit.</summary>
    public void Delete() => Run("Delete selection", (group, session) => group.Delete(session));

    private void Run(string name, Func<PrefabGroup, EditSession, string?> change)
    {
        if (IsDragging || World is not { } world || _prefabs.Count + _spawns.Count == 0) return;
        var group = Capture(world);
        string? status = null;
        if (!edits.Run(world, name, session =>
            {
                status = change(group, session);
                if (session.HasChanges) Redraw(session);
            })) return;
        if (status is not null) vm.Status = status;
    }

    private void Redraw(EditSession session)
    {
        map.InvalidateRegion(session.Bounds);
        map.InvalidateVisual();
    }

    /// <summary>Cancels the current gesture or clears the selection.</summary>
    public bool Cancel()
    {
        if (IsDragging)
        {
            Refresh();
            return true;
        }
        if (_prefabs.Count + _spawns.Count == 0) return false;
        ClearSelection();
        map.MultiSelection = null;
        return true;
    }

    private void EndGesture()
    {
        _gesture = null;
        _moving = null;
        _session = null;
        _boxing = false;
        _turns = 0;
    }

    private void ClearSelection()
    {
        _prefabs.Clear();
        _spawns.Clear();
    }

    private void ShowSelection(World world, Rect? box = null, GroupLayout? layout = null)
    {
        if (box is null && _prefabs.Count + _spawns.Count == 0)
        {
            map.MultiSelection = null;
            return;
        }

        var prefabs = layout is null
            ? _prefabs.Select(p => (Prefab: p, Spot: new PrefabSpot(p.Position.X, p.Position.Y, p.Position.Z, p.Rotation)))
            : layout.Prefabs.Where(t => _prefabs.Contains(t.Prefab));
        var spawns = layout is null
            ? _spawns.Select(s => (X: (double)s.X, Z: (double)s.Z, Rotation: s.RotationY))
            : layout.Spawns.Select(s => (s.X, s.Z, s.Rotation));
        int half = world.Size / 2;
        map.MultiSelection = new MultiSelectionOverlay(box, prefabs
            .OrderByDescending(t => vm.Prefabs.Find(t.Prefab.Name)?.IsStreetTile == true)
            .Select(t =>
            {
                var info = vm.Prefabs.Find(t.Prefab.Name);
                var (sx, sz) = info?.Footprint(t.Spot.Rotation) ?? (1, 1);
                return (map.FootprintMapRect(t.Spot.X, t.Spot.Z, sx, sz), info?.FrontDirection(t.Spot.Rotation) ?? -1);
            }).ToArray(),
            spawns.Select(s => (new Point(s.X + half, world.Size - (s.Z + half)), s.Rotation)).ToArray());
    }
}
