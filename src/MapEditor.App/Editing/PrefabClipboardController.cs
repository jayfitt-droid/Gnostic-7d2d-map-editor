using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using MapEditor.App.Map;
using MapEditor.Core.Edit;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.App.Editing;

/// <summary>Copies map selections to the Windows clipboard and owns the pending paste gesture.</summary>
internal sealed class PrefabClipboardController
{
    private readonly MapView _map;
    private readonly MainViewModel _vm;
    private readonly EditRunner _edits;
    private readonly PrefabToolController _select;
    private readonly MultiSelectToolController _multiSelect;
    private PrefabGroup? _paste;
    private int _skipped;
    private int _turns;

    public PrefabClipboardController(MapView map, MainViewModel vm, EditRunner edits, PrefabToolController select,
        MultiSelectToolController multiSelect)
    {
        _map = map;
        _vm = vm;
        _edits = edits;
        _select = select;
        _multiSelect = multiSelect;
        map.PreviewToolInput = OnToolInput;
        map.CursorMoved += (_, at) => Preview(at);
        map.ViewChanged += (_, _) => UpdatePreview();
    }

    public bool IsPasting => _paste is not null;

    public bool HandleShortcut(Key key)
    {
        if (_vm.IsLandMode || !(_vm.IsSelectTool || _vm.IsMultiSelectTool) || key is not (Key.C or Key.V)) return false;
        if (_vm.IsBusy) _vm.Status = "Wait for the current operation to finish.";
        else if (_select.World is { } world && !_select.IsDragging && !_multiSelect.IsDragging)
        {
            if (key == Key.C) Copy(world);
            else BeginPaste(world);
        }
        return true;
    }

    private bool IsBridgePiece(PrefabInstance prefab) =>
        _select.BridgeOf?.Invoke(prefab) is not null || _select.IsStageBridge?.Invoke(prefab) == true;

    private void Copy(World world)
    {
        IReadOnlyList<PrefabInstance> prefabs = _vm.IsMultiSelectTool ? _multiSelect.SelectedPrefabs
            : _select.SelectedPrefab is { } p ? [p] : [];
        IReadOnlyList<SpawnPoint> spawns = _vm.IsMultiSelectTool ? _multiSelect.SelectedSpawns
            : _select.SelectedSpawn is { } s ? [s] : [];
        if (prefabs.Count + spawns.Count == 0) return;
        var copy = PrefabClipboard.Capture(world, _vm.Prefabs, prefabs, spawns, IsBridgePiece, out int skipped);
        string skippedNote = skipped == 0 ? "" : $"; skipped {skipped} bridge {(skipped == 1 ? "piece" : "pieces")}";
        if (copy.Count == 0)
        {
            _vm.Status = "No items copied" + skippedNote;
            return;
        }
        if (copy.Serialize() is not { } text)
        {
            _vm.Status = "This selection is too large or contains data that cannot be copied.";
            return;
        }
        try
        {
            Clipboard.SetText(text);
            _vm.Status = "Copied " + ItemCount(copy.Count) + skippedNote;
        }
        catch (ExternalException)
        {
            _vm.Status = "Couldn't copy: another program is using the clipboard. Try again.";
        }
    }

    private void BeginPaste(World world)
    {
        PrefabClipboard? copy;
        try
        {
            copy = PrefabClipboard.Parse(Clipboard.GetText());
        }
        catch (ExternalException)
        {
            _vm.Status = "Couldn't paste: another program is using the clipboard. Try again.";
            return;
        }
        if (copy is null) return;
        var group = copy.PreparePaste(world, _vm.Prefabs, IsBridgePiece, out int skipped);
        Cancel();
        _skipped = skipped;
        if (copy.Count == skipped)
        {
            _vm.Status = "No items to paste; " + SkippedPrefabs();
            return;
        }
        _paste = group;
        _turns = 0;
        _vm.Status = "Click to paste. Q/E turn the copy. Esc cancels.";
        if (skipped > 0) _vm.Status += $" {skipped} {(skipped == 1 ? "prefab is" : "prefabs are")} missing from the prefab library.";
        UpdatePreview();
    }

    public bool Cancel()
    {
        if (!IsPasting) return false;
        _paste = null;
        _multiSelect.Refresh();
        return true;
    }

    public bool Rotate(int step)
    {
        if (!IsPasting) return false;
        _turns = (_turns + step) & 7;
        UpdatePreview();
        return true;
    }

    public void UpdatePreview() =>
        Preview(_map.IsMouseOver ? _map.ScreenToMap(Mouse.GetPosition(_map)) : null);

    private void Preview(Point? at)
    {
        if (_paste is not { } group || _select.World is not { } world) return;
        if (at is not { } point)
        {
            _multiSelect.HidePastePreview();
            return;
        }
        var (dx, dz) = DeltaTo(group, world, point);
        _multiSelect.ShowPastePreview(world, group.Layout(dx, dz, _turns, MultiSelectToolController.SnapDistance(_map)));
    }

    private bool OnToolInput(ToolInputEventArgs e)
    {
        if (_paste is not { } group || _select.World is not { } world) return false;
        if (e.Phase != ToolPhase.Up || e.CaptureLost || e.DragThresholdPassed) return true;
        if (_vm.IsBusy)
        {
            _vm.Status = "Wait for the current operation to finish.";
            return true;
        }
        var (dx, dz) = DeltaTo(group, world, e.MapPoint);
        GroupPasteResult pasted = null!;
        if (!_edits.Run(world, "Paste selection", session =>
            {
                pasted = group.Paste(dx, dz, _turns, _vm.PrefabFlatten, session, MultiSelectToolController.SnapDistance(_map));
                if (session.HasChanges) _map.InvalidateRegion(session.Bounds);
            })) return true;
        if (pasted.Problem is { } problem)
        {
            _vm.Status = problem;
            return true;
        }

        Cancel();
        int count = pasted.Prefabs.Count + pasted.Spawns.Count;
        _vm.SelectedTool = count == 1 ? EditorTool.Select : EditorTool.MultiSelect;
        if (count == 1) _select.SelectPasted(pasted.Prefabs.SingleOrDefault(), pasted.Spawns.SingleOrDefault());
        else _multiSelect.SelectPasted(pasted);
        _vm.Status = "Pasted " + ItemCount(count) + (_skipped == 0 ? "" : "; " + SkippedPrefabs());
        return true;
    }

    private static (int X, int Z) DeltaTo(PrefabGroup group, World world, Point at) =>
        group.DeltaTo(at.X - world.Size / 2, world.Size - at.Y - world.Size / 2);

    private static string ItemCount(int count) => $"{count} {(count == 1 ? "item" : "items")}";

    private string SkippedPrefabs() =>
        $"skipped {_skipped} {(_skipped == 1 ? "prefab" : "prefabs")} this world can't use";
}
