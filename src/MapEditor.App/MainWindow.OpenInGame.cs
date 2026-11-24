using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using MapEditor.Core.GameData;
using MapEditor.Core.GameLink;
using MapEditor.Core.Model;

namespace MapEditor.App;

public partial class MainWindow
{
    private static readonly TimeSpan PlaytestTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PlaytestPollInterval = TimeSpan.FromSeconds(1.5);
    private CancellationTokenSource? _openInGameWait;
    private Task _openInGameTask = Task.CompletedTask;

    private (int X, int Z)? PlaytestDestination(World world)
    {
        var map = Map.ScreenToMap(new Point(Map.ActualWidth / 2, Map.ActualHeight / 2));
        int x = (int)Math.Floor(map.X);
        int z = world.Size - 1 - (int)Math.Floor(map.Y);
        return TeleportCommand.CoordinatesForCell(world, x, z);
    }

    /// <summary>Returns true after leaving a save, false if there was no save to leave, or null to cancel export.</summary>
    private async Task<bool?> PreparePlaytestExportAsync(string save)
    {
        if (_gameLinkActionRunning)
        {
            _vm.Status = "A Game Link request is already in progress.";
            return null;
        }

        BeginPlaytestAction();
        _vm.IsBusy = true;
        _vm.Status = "Checking the game...";
        try
        {
            var reply = await _gameLink.GetStatusAsync();
            _vm.GameLinkConnected = reply.Status is not null;
            if (_closeWhenIdle) return null;
            if (reply.Status is not { } status) return false;
            if (status.Operation?.State is "leaving" or "opening" || status is { State: GameLinkState.Menu, Hosting: true })
            {
                _vm.Status = "The game is busy. Wait for it to finish and try the export again.";
                return null;
            }
            if (status.State == GameLinkState.Menu) return false;

            if (GameLinkReadinessError(status) is { } error)
            {
                _vm.Status = GameLinkErrorText(error);
                return null;
            }
            if (status.Save.Length == 0)
            {
                _vm.Status = "Game Link could not identify the current save. Install the mod that matches this editor.";
                return null;
            }

            string message = $"The game will save '{status.Save}' and go to the main menu, then open '{save}'.";
            if (ThemedMessageBox.Show(this, message, "Open in the game after export",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            {
                _vm.Status = "Export cancelled.";
                return null;
            }

            var confirmed = (await _gameLink.GetStatusAsync()).Status;
            if (_closeWhenIdle) return null;
            if (confirmed is null)
            {
                _vm.Status = "Export cancelled. Game Link could not verify the current save.";
                return null;
            }
            if (confirmed.State != GameLinkState.Ready ||
                confirmed.World != status.World || confirmed.Save != status.Save)
            {
                _vm.Status = "Export cancelled. The game changed saves.";
                return null;
            }
            _vm.Status = $"Waiting for the game to save '{status.Save}' and return to the main menu...";
            var leave = await _gameLink.LeaveAsync();
            if (leave.OperationId is not { } id)
            {
                _vm.Status = GameLinkErrorText(leave.Error);
                return null;
            }

            var (result, _) = await PollPlaytestAsync((game, expired) => GameLinkPlaytest.WaitForMenu(game, id, expired));
            if (result.State == GameLinkWaitState.AtMenu) return true;
            _vm.Status = result.State == GameLinkWaitState.TimedOut || result.Reason == "timeout"
                ? "Export cancelled. The game did not return to the main menu in time."
                : PlaytestFailure(result.Reason);
            return null;
        }
        finally
        {
            _vm.IsBusy = false;
            EndPlaytestAction();
            CloseIfWaiting();
        }
    }

    private void StartOpenInGame(GameInstall install, string world, string save, (int X, int Z)? destination)
    {
        var wait = new CancellationTokenSource();
        _openInGameWait = wait;
        BeginPlaytestAction();
        _openInGameTask = OpenInGameAsync(install, world, save, destination, wait);
    }

    private async Task OpenInGameAsync(GameInstall install, string world, string save, (int X, int Z)? destination,
        CancellationTokenSource wait)
    {
        string opening = $"Opening '{save}' in the game... Esc cancels";
        _vm.Status = opening;
        try
        {
            string? operationId = null;
            bool launched = !GameWindow.IsRunning();
            if (launched)
            {
                var start = new ProcessStartInfo(Path.Combine(install.Root, "7DaysToDie.exe"))
                {
                    WorkingDirectory = install.Root,
                    UseShellExecute = false,
                };
                start.ArgumentList.Add("-LoadSaveGame=true");
                start.ArgumentList.Add("-world=" + world);
                start.ArgumentList.Add("-name=" + save);
                using var game = Process.Start(start);
            }
            else
            {
                var status = await _gameLink.GetStatusAsync(wait.Token);
                _vm.GameLinkConnected = status.Status is not null;
                if (status.Status is null)
                {
                    _vm.Status = $"The game is running without Gnostic Game Link. Close the game, or open '{world}' from the game's menu.";
                    return;
                }
                if (status.Status.State != GameLinkState.Menu)
                {
                    _vm.Status = "The world was exported. Go to the game's main menu and export again to open the playtest save.";
                    return;
                }

                bool create = !Directory.Exists(Path.Combine(GameInstall.UserSavesDir, world, save));
                var open = await _gameLink.OpenAsync(world, save, create, wait.Token);
                operationId = open.OperationId;
                if (operationId is null)
                {
                    _vm.Status = PlaytestError(open.Error);
                    return;
                }
            }

            var (result, linked) = await PollPlaytestAsync(
                (game, expired) => GameLinkPlaytest.WaitForSave(game, world, save, operationId, expired), wait.Token);
            wait.Token.ThrowIfCancellationRequested();
            switch (result.State)
            {
                case GameLinkWaitState.Teleport when destination is { } point:
                    var reply = await _gameLink.TeleportAsync(point.X, point.Z, null, wait.Token);
                    wait.Token.ThrowIfCancellationRequested();
                    _vm.Status = reply.Ok ? $"Opened '{save}' at {point.X}, {point.Z}." : GameLinkErrorText(reply.Error);
                    if (reply.Ok && !GameWindow.TryActivate()) _vm.Status += " Switch to the game to see the destination.";
                    break;
                case GameLinkWaitState.Teleport:
                    _vm.Status = $"Opened '{save}'. The centre of the map view was outside the world.";
                    GameWindow.TryActivate();
                    break;
                case GameLinkWaitState.Blocked:
                    _vm.Status = BlockedPlaytestMessage(save, result.Reason);
                    break;
                case GameLinkWaitState.Failed when result.Reason != "timeout":
                    _vm.Status = PlaytestFailure(result.Reason);
                    break;
                default:
                    _vm.Status = launched && !linked && GameWindow.IsRunning()
                        ? $"Started the game to open '{save}' without Gnostic Game Link." +
                            (destination is { } coords ? $" Teleport to {coords.X}, {coords.Z} in the game." : "")
                        : $"The game did not report '{save}' ready in time.";
                    break;
            }
        }
        catch (OperationCanceledException) when (wait.IsCancellationRequested)
        {
            if (_vm.Status == opening) _vm.Status = "Stopped waiting for the game.";
        }
        catch (Exception ex) when (IsBadInput(ex) || ex is Win32Exception or InvalidOperationException)
        {
            _vm.Status = $"Could not open '{save}' in the game. {ex.Message}";
        }
        finally
        {
            _openInGameWait = null;
            wait.Dispose();
            EndPlaytestAction();
        }
    }

    private async Task<(GameLinkWaitResult Result, bool Linked)> PollPlaytestAsync(
        Func<GameLinkStatus?, bool, GameLinkWaitResult> decide, CancellationToken cancel = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        deadline.CancelAfter(PlaytestTimeout);
        bool linked = false;
        try
        {
            while (!_closeWhenIdle)
            {
                var reply = await _gameLink.GetStatusAsync(deadline.Token);
                _vm.GameLinkConnected = reply.Status is not null;
                linked |= reply.Status is not null;
                var result = decide(reply.Status, false);
                if (result.State != GameLinkWaitState.Waiting) return (result, linked);
                await Task.Delay(PlaytestPollInterval, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested && deadline.IsCancellationRequested) { }
        return (decide(null, true), linked);
    }

    private void BeginPlaytestAction()
    {
        _gameLinkActionRunning = true;
        _gameLinkPollRequest?.Cancel();
        UpdateIssueStepControls();
    }

    private void EndPlaytestAction()
    {
        _gameLinkActionRunning = false;
        UpdateIssueStepControls();
    }

    private bool CancelOpenInGameWait()
    {
        if (_openInGameWait is not { IsCancellationRequested: false } wait) return false;
        wait.Cancel();
        return true;
    }

    private static string PlaytestFailure(string reason) => PlaytestError(reason switch
    {
        "players" => GameLinkError.Players,
        "busy" => GameLinkError.Busy,
        "not_ready" => GameLinkError.NotReady,
        "world_not_found" => GameLinkError.WorldNotFound,
        "save_not_found" => GameLinkError.SaveNotFound,
        "save_unplayable" => GameLinkError.SaveUnplayable,
        _ => GameLinkError.Failed,
    });

    private static string PlaytestError(GameLinkError error) => error == GameLinkError.NotReady
        ? "The game is not ready to open the playtest save. Go to the game's main menu and try again."
        : GameLinkErrorText(error);

    private static string BlockedPlaytestMessage(string save, string reason) => reason switch
    {
        "older_version" => $"The game can't continue '{save}' (made with an older game version). Delete it in the game's Continue page, then try again.",
        "newer_version" => $"The game can't continue '{save}' (made with a newer game version). Use that game version or delete the save in the game's Continue page.",
        "world_not_found" => $"The game can't continue '{save}' because its world is missing.",
        "start_disabled" => $"The game can't continue '{save}'. See the Start button on the game's Continue page for the reason.",
        _ => $"The game can't continue '{save}'. Delete it in the game's Continue page, then try again.",
    };
}
