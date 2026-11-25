using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using MapEditor.App.Editing;
using MapEditor.Core.Ops;

namespace MapEditor.App;

public partial class MainWindow
{
    private readonly LandToolController _landTools;
    private bool _newLandWorld;

    private void EnterLandMode(bool newWorld)
    {
        if (_world is not { } world || _projectSettings.Generator?.Terrain.Shape is not { Kind: LandShapeKind.Drawn } shape) return;
        _tools.CancelRoad();
        _vm.SelectedTool = EditorTool.Select;
        _newLandWorld = newWorld;
        _vm.IsLandMode = true;
        UpdateLandOutline();
        _landTools.Begin(world.Size, shape.Drawing);
        UpdateToolState();
        Map.Focus();
        _vm.Status = "Draw the land, then click Build World.";
    }

    private void ExitLandMode()
    {
        _landTools.End();
        _vm.IsLandMode = false;
        _newLandWorld = false;
        UpdateLandOutline();
        UpdateUndoText();
        UpdateToolState();
        Map.Focus();
    }

    /// <summary>
    /// Shows the saved land boxes on the map outside Land mode while the ground isn't built from them, for example after
    /// Build World is undone. Once the terrain is built, the ground shows the land.
    /// </summary>
    private void UpdateLandOutline()
    {
        if (!_vm.IsLandMode && _world is { } world && !_vm.Generate.TerrainDone
            && _projectSettings.Generator?.Terrain.Shape is { Kind: LandShapeKind.Drawn, Drawing: { Boxes.Count: > 0 } drawing })
            Map.LandOutline = drawing.Boxes.Select(b => (new Rect(b.X0, world.Size - b.Z1, b.X1 - b.X0, b.Z1 - b.Z0), b.CutsWater)).ToArray();
        else
            Map.LandOutline = null;
    }

    private void EditLand_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.IsBusy) EnterLandMode(newWorld: false);
    }

    private async void BuildLand_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsBusy || _landTools.IsDragging || _landTools.Snapshot() is not { } draft) return;
        await GenerateTerrainAsync(draft);
    }

    private void CancelLand_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsBusy)
        {
            _generateCancel?.Cancel();
            return;
        }
        CancelLand();
    }

    private void CancelLand()
    {
        bool closeWorld = _newLandWorld;
        ExitLandMode();
        if (closeWorld) ClearWorld();
        else _vm.Status = "Land edits discarded.";
    }

    private bool ConfirmDiscardLand() =>
        _landTools.UndoName is null || ThemedMessageBox.Show(this,
                "Discard the changes in Land mode?", "Land mode", MessageBoxButton.OKCancel,
                MessageBoxImage.Question) == MessageBoxResult.OK;

    private void RerollLandBox_Click(object sender, RoutedEventArgs e) => _landTools.Reroll();

    private void DeleteLandBox_Click(object sender, RoutedEventArgs e)
    {
        _landTools.Delete();
        Map.Focus();
    }

    private void LandSlider_DragStarted(object sender, DragStartedEventArgs e) => _landTools.BeginSlider();

    private void LandSlider_DragCompleted(object sender, DragCompletedEventArgs e) =>
        _landTools.EndSlider(ReferenceEquals(sender, LandFill) ? "change fill" : "change roughness", e.Canceled);

    private bool CanUndoLand(bool redo) =>
        !_vm.IsBusy && !_landTools.IsDragging && (redo ? _landTools.RedoName : _landTools.UndoName) is not null;

    private void UndoLand(bool redo)
    {
        if (CanUndoLand(redo)) _landTools.UndoOrRedo(redo);
        else if (redo && CanRedoBuildWorld) Redo_Executed(this, null!);
    }

    private bool HandleLandKey(KeyEventArgs e)
    {
        if (e.Key == Key.F1) return false;
        if (e.Handled) return true;
        if (e.SystemKey is Key.LeftAlt or Key.RightAlt)
        {
            e.Handled = true;
            return true;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.Z or Key.Y ||
            Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Z)
        {
            UndoLand(redo: e.Key == Key.Y || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
        }
        else if (!_vm.IsBusy && Keyboard.Modifiers == ModifierKeys.None && ReferenceEquals(Keyboard.FocusedElement, Map))
        {
            if (e.Key == Key.Delete) _landTools.Delete();
            else if (e.Key == Key.Escape) _landTools.CancelGesture();
            else return true;
            e.Handled = true;
        }
        return true;
    }
}
