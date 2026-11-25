using System.Windows;
using MapEditor.Core.GameData;
using MapEditor.Core.GameLink;
using MapEditor.Core.Model;

namespace MapEditor.App;

public partial class MainWindow
{
    private bool _rebuildWaitActive;
    private string? _rebuildResultStatus;
    private bool ShowingRebuildResult => _rebuildResultStatus is not null && _vm.Status == _rebuildResultStatus;

    private async Task<GameLinkStatus?> ReadExportGameStatusAsync()
    {
        if (_gameLinkActionRunning) return null;

        BeginPlaytestAction();
        _vm.IsBusy = true;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(1.25));
        try
        {
            return (await _gameLink.GetStatusAsync(deadline.Token)).Status;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            _vm.IsBusy = false;
            EndPlaytestAction();
            CloseIfWaiting();
        }
    }

    private async Task<GameLinkStatus?> PrepareRebuildExportAsync(string folder, World world, GameInstall install,
        GameLinkStatus initial, int radius)
    {
        if (_gameLinkActionRunning)
        {
            _vm.Status = "A Game Link request is already in progress.";
            return null;
        }

        BeginPlaytestAction();
        _vm.IsBusy = true;
        try
        {
            _vm.Status = "Checking the game...";
            var current = (await _gameLink.GetStatusAsync()).Status;
            _vm.GameLinkConnected = current is not null;
            if (_closeWhenIdle) return null;
            if (GameLinkRebuild.UnavailableReason(folder, install, current, GameLinkExportFolder, ExportHasChanges) is { } unavailable)
            {
                _vm.Status = "Export cancelled. " + unavailable;
                return null;
            }
            if (current is not { Player: { } player } || !GameLinkRebuild.IsSameSave(initial, current))
            {
                _vm.Status = "Export cancelled. The game changed saves.";
                return null;
            }

            _vm.Status = "Calculating the rebuild area...";
            var library = _vm.Prefabs;
            GameLinkRebuildArea.Summary summary;
            try
            {
                summary = await Task.Run(() => GameLinkRebuild.SummarizeExport(folder, world, library, player, radius));
            }
            catch (Exception ex) when (IsBadInput(ex))
            {
                _vm.Status = "Export cancelled. Could not calculate the rebuild area.";
                if (!_closeWhenIdle)
                    ThemedMessageBox.Show(this, ex.Message, "Rebuild the area around the player",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
            if (_closeWhenIdle) return null;

            string larger = summary.IsMuchLarger
                ? $"Points of interest at the edge make this area much larger than the {2 * radius} m square, as can happen in a town. "
                : "";
            string message = $"The game will save '{current.Save}', go to the main menu, and load the same save again. " +
                $"The game will rebuild {summary.ChunkCount:N0} chunks from the exported world files. " +
                $"The area reaches up to {summary.ReachMetres:N0} m from the player along a map axis. " +
                "This includes whole chunks and any points of interest that cross the edge. " + larger +
                "Player buildings, loot, bedrolls and land claims in the rebuilt area are lost. " +
                "The game can also remove vehicles and dropped backpacks there.";
            if (ThemedMessageBox.Show(this, message, "Rebuild the area around the player",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            {
                _vm.Status = "Export cancelled.";
                return null;
            }
            _vm.Status = "Checking the game...";
            var confirmed = (await _gameLink.GetStatusAsync()).Status;
            _vm.GameLinkConnected = confirmed is not null;
            if (_closeWhenIdle) return null;
            if (GameLinkRebuild.UnavailableReason(folder, install, confirmed) is { } reason)
            {
                _vm.Status = "Export cancelled. " + reason;
                return null;
            }
            if (!GameLinkRebuild.IsSameSave(current, confirmed!))
            {
                _vm.Status = "Export cancelled. The game changed saves.";
                return null;
            }
            return await LeaveForExportAsync(current.Save) == true ? current : null;
        }
        finally
        {
            _vm.IsBusy = false;
            EndPlaytestAction();
            CloseIfWaiting();
        }
    }

    private void StartRebuild(GameLinkStatus target, int radius) =>
        StartExportGameWait(wait => RebuildAreaAsync(target, radius, wait));

    private async Task RebuildAreaAsync(GameLinkStatus target, int radius, CancellationTokenSource wait)
    {
        string rebuilding = $"Rebuilding around the player in '{target.Save}'... Press Shift-Esc to stop waiting.";
        _vm.Status = rebuilding;
        _rebuildWaitActive = true;
        try
        {
            var reply = await _gameLink.RebuildAsync(target.World, target.Save, target.Player!, radius, wait.Token);
            if (reply.OperationId is not { } id)
            {
                _vm.Status = "World exported. Could not start the rebuild. " + (reply.Message ?? GameLinkErrorText(reply.Error));
                return;
            }

            var (result, _) = await PollPlaytestAsync(
                (status, expired) => GameLinkRebuild.Wait(status, target.World, target.Save, id, expired), wait.Token);
            wait.Token.ThrowIfCancellationRequested();
            _vm.Status = result switch
            {
                { State: GameLinkWaitState.Rebuilt, Chunks: > 0 } =>
                    $"Rebuilt {result.Chunks:N0} chunks around the player in '{target.Save}'.",
                { State: GameLinkWaitState.Rebuilt } => $"Rebuilt the area around the player in '{target.Save}'.",
                { State: GameLinkWaitState.Blocked } =>
                    $"The game could not continue '{target.Save}'. Check the game's Continue screen.",
                { State: GameLinkWaitState.Failed, Reason: "reset_failed" } =>
                    $"The game could not complete the chunk reset in '{target.Save}'. See the game log.",
                { State: GameLinkWaitState.Failed, Reason: not "timeout" } => PlaytestFailure(result.Reason),
                _ => $"The game did not report the rebuild in '{target.Save}' complete in time.",
            };
            if (result.State == GameLinkWaitState.Rebuilt) GameWindow.TryActivate();
        }
        catch (OperationCanceledException) when (wait.IsCancellationRequested)
        {
            if (_vm.Status == rebuilding) _vm.Status = "Stopped waiting for the game.";
        }
        catch (Exception ex) when (IsBadInput(ex) || ex is InvalidOperationException)
        {
            _vm.Status = $"Could not rebuild the area in '{target.Save}'. {ex.Message}";
        }
        finally
        {
            _rebuildResultStatus = _vm.Status;
            _rebuildWaitActive = false;
            _openInGameWait = null;
            wait.Dispose();
            EndPlaytestAction();
        }
    }
}
