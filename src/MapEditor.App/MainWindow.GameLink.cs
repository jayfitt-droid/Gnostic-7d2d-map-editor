using System.Windows;
using MapEditor.Core.GameData;
using MapEditor.Core.GameLink;
using MapEditor.Core.Model;
using MapEditor.Core.Validate;

namespace MapEditor.App;

public partial class MainWindow
{
    private readonly GameLinkClient _gameLink = new();
    private bool _gameLinkActionRunning;

    private void ShowGameLinkStatus(GameLinkStatus? status)
    {
        _vm.GameLinkConnected = status is not null;
        _vm.GameLinkIndicator = GameLinkWorld.Indicator(GameLinkExportFolder, status);
    }

    /// <summary>Checks the running game and sends a teleport or a temporary POI waypoint.</summary>
    private Task RunGameLinkActionAsync(World world, int x, int z, Facing? facing = null,
        string? waypointPrefab = null, ValidationIssue? issue = null, Action? onTeleported = null)
    {
        string? folder = GameLinkExportFolder;
        bool SameExport() => ReferenceEquals(_world, world) && folder == GameLinkExportFolder;
        bool CanSend() => CanUseGameLink(world) && SameExport() && (issue is null || _issueStepper.StopFor(issue) is not null);
        bool teleport = waypointPrefab is null;
        return RunCheckedGameLinkActionAsync(folder, CanSend, teleport ? "Teleport Here" : "Add Waypoint",
            teleport ? "Teleport Anyway" : "Add Waypoint Anyway", teleport ? "Teleport cancelled." : "Waypoint cancelled.",
            async (_, matches) =>
        {
            _vm.Status = teleport ? "Teleporting..." : "Adding waypoint...";
            GameLinkReply reply = waypointPrefab is null
                ? await _gameLink.TeleportAsync(x, z, facing)
                : await _gameLink.AddWaypointAsync(x, z, waypointPrefab);
            if (!reply.Ok)
            {
                if (reply.Error is GameLinkError.Unavailable or GameLinkError.NoReply or GameLinkError.InvalidToken)
                    ShowGameLinkStatus(null);
                if (CanUseGameLink(world) && SameExport()) ShowGameLinkError(reply.Error);
                else _vm.Status = GameLinkErrorText(reply.Error);
                return;
            }

            if (waypointPrefab is not null)
            {
                _vm.Status = $"Temporary waypoint added for {waypointPrefab}. The game does not save it.";
                return;
            }

            if (ReferenceEquals(_world, world)) onTeleported?.Invoke();
            string message = $"Teleported to {x}, {z}.";
            string? exportName = SameExport() ? GameLinkWorld.ExportName(folder) : null;
            if (exportName is not null)
            {
                message += ExportHasChanges switch
                {
                    true => " The editor has changes since export.",
                    null => " The export may be older than this project.",
                    false => "",
                };
            }
            _vm.Status = message;
            if (matches && exportName is not null && await Task.Run(() => HasSavedGameChunks(exportName))
                && SameExport() && _vm.Status == message)
            {
                message += " Saves keep terrain in chunks they already generated.";
                _vm.Status = message;
            }
            if (CanUseGameLink(world) && SameExport() && _vm.Status == message && !GameWindow.TryActivate())
                _vm.Status += " Switch to the game to see the destination.";
        });
    }

    private async Task RunCheckedGameLinkActionAsync(string? folder, Func<bool> canSend, string title,
        string confirm, string cancelled, Func<GameLinkStatus, bool, Task> send)
    {
        if (!canSend()) return;
        if (_gameLinkActionRunning)
        {
            _vm.Status = "A Game Link request is already in progress.";
            return;
        }

        _gameLinkActionRunning = true;
        UpdateIssueStepControls();
        const string checking = "Checking the game...";
        _vm.Status = checking;
        try
        {
            GameLinkReply status = await _gameLink.GetStatusAsync();
            ShowGameLinkStatus(status.Status);
            if (!canSend()) return;
            if (status.Status is not { } game)
            {
                ShowGameLinkError(status.Error);
                return;
            }

            if (GameLinkReadinessError(game) is { } error)
            {
                _vm.Status = GameLinkErrorText(error);
                return;
            }

            bool matches = GameLinkWorld.Matches(folder, game.World);
            if (!matches && !ConfirmGameLinkWorld(folder, game.World, title, confirm))
            {
                _vm.Status = cancelled;
                return;
            }
            if (canSend()) await send(game, matches);
        }
        finally
        {
            if (_vm.Status == checking) _vm.Status = "Game Link action cancelled before it was sent.";
            _gameLinkActionRunning = false;
            UpdateIssueStepControls();
        }
    }

    private static GameLinkError? GameLinkReadinessError(GameLinkStatus game) => game.State switch
    {
        GameLinkState.Ready when game.Hosting => null,
        GameLinkState.Dead => GameLinkError.Dead,
        GameLinkState.Remote or GameLinkState.Ready => GameLinkError.Remote,
        _ => GameLinkError.NotReady,
    };

    private bool CanUseGameLink(World world) => IsLoaded && !_vm.IsBusy && ReferenceEquals(_world, world);

    private void ShowGameLinkError(GameLinkError error)
    {
        string message = GameLinkErrorText(error);
        if (error == GameLinkError.Unavailable)
        {
            _vm.Status = string.Empty;
            Activate();
            ThemedMessageBox.Show(this, message, "Gnostic Game Link", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _vm.Status = message;
    }

    private bool ConfirmGameLinkWorld(string? exportFolder, string runningWorld, string title, string action)
    {
        string world = runningWorld.Length == 0 ? "an unnamed world" : $"'{runningWorld}'";
        string message = GameLinkWorld.ExportName(exportFolder) is { } exported
            ? $"The game has {world} open. This world was last exported to '{exported}'."
            : $"The game has {world} open. This project has no export folder yet.";
        Activate();
        return ThemedMessageBox.Show(this, message, title, MessageBoxButton.OKCancel,
            MessageBoxImage.Warning, [action, "Cancel"]) == MessageBoxResult.OK;
    }

    private static bool HasSavedGameChunks(string worldName)
    {
        try
        {
            return GameInstall.SavedGamesOn(worldName).Count > 0;
        }
        catch (Exception ex) when (IsBadInput(ex))
        {
            return false;
        }
    }

    private static string GameLinkErrorText(GameLinkError error) => error switch
    {
        GameLinkError.Unavailable => "The game is not running with Gnostic Game Link.",
        GameLinkError.NoReply => "The game did not answer Game Link. Try again.",
        GameLinkError.InvalidReply => "Game Link sent an invalid reply.",
        GameLinkError.BadRequest => "Game Link could not use that target.",
        GameLinkError.UnsupportedProtocol => "Install Gnostic Game Link 1.1.0 to use Game Link with this editor.",
        GameLinkError.InvalidToken => "Game Link could not verify the session. Restart the game or install the mod that matches this editor.",
        GameLinkError.NotReady => "The game is not ready. Enter a world and try again.",
        GameLinkError.Dead => "The player is dead. Respawn and try again.",
        GameLinkError.Remote => "Game Link works in single-player or when you host the game.",
        GameLinkError.OffMap => "The destination is outside the game world.",
        GameLinkError.Busy => "The game is busy. Wait for the current action to finish and try again.",
        GameLinkError.Players => "The game cannot leave the save while other players are connected.",
        GameLinkError.WorldNotFound => "The game could not find that world.",
        GameLinkError.SaveNotFound => "The game could not find that save.",
        GameLinkError.SaveUnplayable => "The game cannot continue that save.",
        GameLinkError.Expired => "The request to open the save expired. Try the export again.",
        GameLinkError.PoiNotFound => "The game could not find that POI. Export the world again and load it in the game.",
        GameLinkError.AlreadySet => "This POI already has a temporary waypoint.",
        GameLinkError.PictureFailed => "The game could not take the picture. See the game log for details.",
        GameLinkError.PictureRenderTimeout => "The area did not finish rendering in time. Try taking the picture again.",
        _ => "The game could not complete the Game Link request.",
    };
}
