using System.Windows.Threading;
using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.IO;

namespace MapEditor.App;

public partial class MainWindow
{
    private readonly DispatcherTimer _worldStatsTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(250)
    };
    private bool _worldStatsPending;
    private CancellationTokenSource? _worldStatsCancel;
    private WorldSnapshot? _worldStatsSnapshot;

    private void QueueWorldStats()
    {
        _worldStatsPending = _world is not null;
        CancelWorldStatsRead();
        _worldStatsTimer.Stop();
        if (_worldStatsPending && !_vm.IsBusy) _worldStatsTimer.Start();
    }

    private void UpdateWorldStatsWhileBusy()
    {
        if (_vm.IsBusy)
        {
            _worldStatsTimer.Stop();
            _worldStatsPending |= _worldStatsSnapshot is not null;
            CancelWorldStatsRead();
        }
        else if (_worldStatsPending)
        {
            _worldStatsTimer.Start();
        }
    }

    private void ClearWorldStatsRequest()
    {
        _worldStatsPending = false;
        _worldStatsTimer.Stop();
        CancelWorldStatsRead();
    }

    private void CancelWorldStatsRead()
    {
        _worldStatsCancel?.Cancel();
        // A stage may replace or repaint entire layers. Drop the watchers before it touches them.
        // The worker can finish its current band, but the cancelled result will be discarded.
        _worldStatsSnapshot?.Dispose();
        _worldStatsSnapshot = null;
    }

    private async Task RefreshWorldStatsAsync()
    {
        if (!_worldStatsPending || _world is not { } world)
        {
            _worldStatsTimer.Stop();
            return;
        }
        // The timer retries while a stroke or drag still has an open edit.
        if (!CanChangeUndoHistory || _worldStatsCancel is not null) return;

        _worldStatsTimer.Stop();
        _worldStatsPending = false;
        using var cancel = new CancellationTokenSource();
        _worldStatsCancel = cancel;
        try
        {
            var snapshot = WorldSnapshot.Take(world);
            _worldStatsSnapshot = snapshot;
            var biomes = _vm.Biomes;
            var prefabs = _vm.Prefabs;
            var names = GameNames.Current;
            var display = await Task.Run(() =>
                new WorldStatsViewModel(WorldStatistics.Measure(snapshot, biomes, prefabs, cancel.Token), names, biomes), cancel.Token);

            if (!cancel.IsCancellationRequested)
            {
                _vm.WorldStats = display;
                _vm.WorldStatsStatus = "";
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            App.LogError(ex);
            if (!cancel.IsCancellationRequested) _vm.WorldStatsStatus = "World statistics are unavailable.";
        }
        finally
        {
            _worldStatsSnapshot?.Dispose();
            _worldStatsSnapshot = null;
            _worldStatsCancel = null;
        }
    }
}
