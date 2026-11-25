using System.Windows;
using MapEditor.Core.GameData;
using MapEditor.Core.GameLink;

namespace MapEditor.App;

public partial class MainWindow
{
    private bool _rebuildWaitActive;

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

    private async Task<bool?> PrepareRebuildExportAsync(string folder, GameInstall install, GameLinkStatus initial, int radius)
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
            string message = $"The game will save '{initial.Save}', go to the main menu, and load the same save again. " +
                $"The game will rebuild the area within {radius} m of the player from the exported world files. " +
                "This includes whole chunks and any points of interest that cross the edge. " +
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
            if (!GameLinkRebuild.IsSameSave(initial, confirmed!))
            {
                _vm.Status = "Export cancelled. The game changed saves.";
                return null;
            }
            return await LeaveForExportAsync(initial.Save);
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
                _vm.Status = "World exported. Could not start the rebuild. " + GameLinkErrorText(reply.Error);
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
            _rebuildWaitActive = false;
            _openInGameWait = null;
            wait.Dispose();
            EndPlaytestAction();
        }
    }
}
