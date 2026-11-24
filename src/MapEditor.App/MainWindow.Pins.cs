using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MapEditor.Core.GameLink;
using MapEditor.Core.Model;

namespace MapEditor.App;

public partial class MainWindow
{
    private sealed class PinRow(GameLinkPin pin)
    {
        public GameLinkPin Pin { get; } = pin;
        public string Note => Pin.Note.Length == 0 ? "Pin" : Pin.Note;
        public string Coordinates => $"X {Pin.X:0.##}, Y {Pin.Y:0.##}, Z {Pin.Z:0.##}";
        public string Time => Pin.Time.ToLocalTime().ToString("g");
    }

    private readonly ObservableCollection<PinRow> _pinRows = [];
    private readonly Dictionary<string, Guid> _savedPinAcks = [];
    private long _nextPinPoll;
    private bool _pinsUnsupported;

    private void ResetPins()
    {
        _savedPinAcks.Clear();
        _nextPinPoll = 0;
        RefreshPins();
    }

    private void RefreshPins()
    {
        _pinRows.Clear();
        foreach (var pin in _projectSettings.Pins)
            _pinRows.Add(new PinRow(pin));
        PinOverlay.SetPins(_projectSettings.Pins, _world?.Size ?? 0);
    }

    private void SetSavedPinAcks(IReadOnlyDictionary<string, Guid> saved)
    {
        _savedPinAcks.Clear();
        foreach (var (key, id) in saved)
            _savedPinAcks.Add(key, id);
        _nextPinPoll = 0;
    }

    private async Task PollPinsAsync(World? world, string? folder, GameLinkStatus? status, CancellationToken cancel)
    {
        if (status is null)
        {
            _pinsUnsupported = false;
            return;
        }
        if (_pinsUnsupported || world is null || Environment.TickCount64 < _nextPinPoll) return;

        var project = _projectSettings;
        bool SameProject() => !cancel.IsCancellationRequested && CanPollGameLink && ReferenceEquals(world, _world) &&
            ReferenceEquals(project, _projectSettings) && folder == GameLinkExportFolder;
        if (!SameProject()) return;
        _nextPinPoll = Environment.TickCount64 + 3000;

        if (_savedPinAcks.Count > 0)
        {
            var (key, id) = _savedPinAcks.First();
            var ack = await _gameLink.AckPinsAsync(id, cancel);
            if (!SameProject()) return;
            if (ack.Error == GameLinkError.BadRequest) _pinsUnsupported = true;
            if (!ack.Ok) return;
            if (_savedPinAcks.GetValueOrDefault(key) == id) _savedPinAcks.Remove(key);
            if (project.PendingPinAcks.GetValueOrDefault(key) == id) project.PendingPinAcks.Remove(key);
        }

        if (GameLinkWorld.ExportName(folder) is not { } name) return;
        string worldKey = GameLinkPins.WorldKey(name);
        Guid after = project.PendingPinAcks.GetValueOrDefault(worldKey);
        var reply = await _gameLink.GetPinsAsync(worldKey, after, cancel);
        if (!SameProject()) return;
        if (reply.Error == GameLinkError.BadRequest) _pinsUnsupported = true;
        if (reply.Pins is not { Count: > 0 } pins || pins.Any(pin => !GameLinkWorld.Matches(folder, pin.World))) return;

        int first = project.Pins.Count;
        int added = GameLinkPins.Merge(project.Pins, pins, folder);
        project.PendingPinAcks[worldKey] = pins[^1].Id;
        MarkSettingsChanged();
        for (int i = first; i < project.Pins.Count; i++)
            _pinRows.Add(new PinRow(project.Pins[i]));
        PinOverlay.SetPins(project.Pins, world.Size);
        if (added > 0) _vm.Status = added == 1 ? "1 new pin from the game." : $"{added} new pins from the game.";
    }

    private async void TeleportPin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PinRow row } || !_vm.CanTeleportIssues ||
            _world is not { } world || PinMapPoint(row.Pin, world.Size) is null) return;

        int x = Math.Clamp((int)Math.Floor(row.Pin.X), world.GridToWorld(0), world.GridToWorld(world.Size - 1));
        int z = Math.Clamp((int)Math.Floor(row.Pin.Z), world.GridToWorld(0), world.GridToWorld(world.Size - 1));
        var facing = (Facing)((int)Math.Round(row.Pin.Yaw / 45) & 7);
        await RunGameLinkActionAsync(world, x, z, facing);
    }

    private void DeletePin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PinRow row } || _vm.IsBusy ||
            !_projectSettings.Pins.Remove(row.Pin)) return;

        _pinRows.Remove(row);
        PinOverlay.SetPins(_projectSettings.Pins, _world!.Size);
        MarkSettingsChanged();
    }

    private void Pin_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(PinsList, source) is ListBoxItem)
            JumpToPin();
    }

    private void Pin_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.OriginalSource is Button) return;
        JumpToPin();
        e.Handled = true;
    }

    private void JumpToPin()
    {
        if (PinsList.SelectedItem is not PinRow row || _world is not { } world ||
            PinMapPoint(row.Pin, world.Size) is not { } point) return;
        double target = Math.Min(Map.ActualWidth, Map.ActualHeight) / 300;
        if (Map.Scale < target) Map.ZoomTo(target);
        Map.CenterOn(point);
    }

    internal static Point? PinMapPoint(GameLinkPin pin, int size)
    {
        int half = size / 2;
        double x = half + (double)pin.X, y = size - (half + (double)pin.Z);
        return x >= 0 && x <= size && y >= 0 && y <= size ? new Point(x, y) : null;
    }
}
