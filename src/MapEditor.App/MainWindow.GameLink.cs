using System.Windows;
using MapEditor.Core.GameData;
using MapEditor.Core.GameLink;
using MapEditor.Core.Model;

namespace MapEditor.App;

public partial class MainWindow
{
    private readonly GameLinkClient _gameLink = new();
    private bool _gameLinkActionRunning;

    /// <summary>Checks the running game and sends a teleport or a temporary POI waypoint.</summary>
    private async Task RunGameLinkActionAsync(World world, int x, int z, Facing? facing = null, string? waypointPrefab = null)
    {
        if (!CanUseGameLink(world)) return;
        if (_gameLinkActionRunning)
        {
            _vm.Status = "A Game Link request is already in progress.";
            return;
        }

        _gameLinkActionRunning = true;
        _vm.Status = "Checking the game...";
        try
        {
            GameLinkReply status = await _gameLink.GetStatusAsync();
            if (!CanUseGameLink(world)) return;
            if (status.Status is not { } game)
            {
                ShowGameLinkError(status.Error);
                return;
            }

            if (game is not { State: GameLinkState.Ready, Hosting: true })
            {
                _vm.Status = GameLinkErrorText(game.State switch
                {
                    GameLinkState.Dead => GameLinkError.Dead,
                    GameLinkState.Remote or GameLinkState.Ready => GameLinkError.Remote,
                    _ => GameLinkError.NotReady,
                });
                return;
            }

            bool teleport = waypointPrefab is null;
            string? folder = GameLinkExportFolder;
            bool matches = GameLinkWorld.Matches(folder, game.World);
            if (!matches && !ConfirmGameLinkWorld(folder, game.World, teleport))
            {
                _vm.Status = teleport ? "Teleport cancelled." : "Waypoint cancelled.";
                return;
            }
            if (!CanUseGameLink(world) || folder != GameLinkExportFolder) return;

            _vm.Status = teleport ? "Teleporting..." : "Adding waypoint...";
            GameLinkReply reply = waypointPrefab is null
                ? await _gameLink.TeleportAsync(x, z, facing)
                : await _gameLink.AddWaypointAsync(x, z, waypointPrefab);
            if (!CanUseGameLink(world)) return;
            if (!reply.Ok)
            {
                ShowGameLinkError(reply.Error);
                return;
            }

            if (waypointPrefab is not null)
            {
                _vm.Status = $"Temporary waypoint added for {waypointPrefab}. The game does not save it.";
                return;
            }

            string message = $"Teleported to {x}, {z}.";
            if (GameLinkWorld.ExportName(folder) is { } exportName)
            {
                message += ExportHasChanges switch
                {
                    true => " The editor has changes since export.",
                    null => " The export may be older than this project.",
                    false => "",
                };
                if (matches && await Task.Run(() => HasSavedGameChunks(exportName)))
                    message += " Saves keep terrain in chunks they already generated.";
            }
            if (!CanUseGameLink(world)) return;
            _vm.Status = message;
            if (!GameWindow.TryActivate())
                _vm.Status += " Switch to the game to see the destination.";
        }
        finally
        {
            _gameLinkActionRunning = false;
        }
    }

    private bool CanUseGameLink(World world) => IsLoaded && !_vm.IsBusy && ReferenceEquals(_world, world);

    private void ShowGameLinkError(GameLinkError error)
    {
        string message = GameLinkErrorText(error);
        if (error == GameLinkError.Unavailable)
        {
            _vm.Status = string.Empty;
            ThemedMessageBox.Show(this, message, "Gnostic Game Link", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _vm.Status = message;
    }

    private bool ConfirmGameLinkWorld(string? exportFolder, string runningWorld, bool teleport)
    {
        string world = runningWorld.Length == 0 ? "an unnamed world" : $"'{runningWorld}'";
        string message = GameLinkWorld.ExportName(exportFolder) is { } exported
            ? $"The game has {world} open. This world was last exported to '{exported}'."
            : $"The game has {world} open. This project has no export folder yet.";
        string title = teleport ? "Teleport Here" : "Add Waypoint";
        string action = teleport ? "Teleport Anyway" : "Add Waypoint Anyway";
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
        GameLinkError.UnsupportedProtocol => "This editor and the Game Link mod use different protocol versions.",
        GameLinkError.NotReady => "The game is not ready. Enter a world and try again.",
        GameLinkError.Dead => "The player is dead. Respawn and try again.",
        GameLinkError.Remote => "Game Link works in single-player or when you host the game.",
        GameLinkError.OffMap => "The destination is outside the game world.",
        GameLinkError.Busy => "The game is busy. Wait for the player to finish teleporting and try again.",
        GameLinkError.PoiNotFound => "The game could not find that POI. Export the world again and load it in the game.",
        GameLinkError.AlreadySet => "This POI already has a temporary waypoint.",
        _ => "The game could not complete the Game Link request.",
    };
}
