using System.Windows;
using System.Windows.Threading;
using MapEditor.Core.GameLink;

namespace MapEditor.App;

public partial class MainWindow
{
    private readonly DispatcherTimer _playerPollTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private CancellationTokenSource? _playerPollRequest;
    private string? _playerTrackingStatus;

    private bool CanPollPlayer => _vm.ShowPlayer && !_vm.IsBusy && !_gameLinkActionRunning;

    private void UpdatePlayerPolling()
    {
        if (_vm.ShowPlayer)
        {
            if (_playerPollRequest is null) _playerPollTimer.Start();
            return;
        }

        _playerPollTimer.Stop();
        _playerPollRequest?.Cancel();
        _vm.FollowPlayer = false;
        ClearPlayerMarker();
    }

    private void ResetPlayerTracking()
    {
        _vm.ShowPlayer = false;
        ShowPlayerCheck.IsEnabled = _world is not null;
    }

    private void ClearPlayerMarker()
    {
        PlayerOverlay.SetMarker(null);
        ReportPlayerTrackingStatus(null);
    }

    /// <summary>Schedules the next poll after the current one finishes, including after a rapid off/on toggle.</summary>
    private async void PollPlayer_Tick(object? sender, EventArgs e)
    {
        _playerPollTimer.Stop();
        try
        {
            if (!CanPollPlayer || _world is not { } world) return;
            string? folder = GameLinkExportFolder;
            if (GameLinkWorld.ExportName(folder) is null)
            {
                ReportPlayerTrackingStatus("Export this world to show the player.");
                return;
            }

            using var request = new CancellationTokenSource();
            _playerPollRequest = request;
            try
            {
                var reply = await _gameLink.GetStatusAsync(request.Token);
                if (request.IsCancellationRequested || !CanPollPlayer || !ReferenceEquals(world, _world) || folder != GameLinkExportFolder)
                    return;
                UpdatePlayerMarker(folder, world.Size, reply);
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested)
            {
                // Show player was turned off or the editor closed.
            }
        }
        finally
        {
            _playerPollRequest = null;
            if (_vm.ShowPlayer && _world is not null) _playerPollTimer.Start();
        }
    }

    private void UpdatePlayerMarker(string? folder, int size, GameLinkReply reply)
    {
        if (reply.Status is not { } status)
        {
            PlayerOverlay.FadeMarker();
            ReportPlayerTrackingStatus(reply.Error switch
            {
                GameLinkError.Unavailable or GameLinkError.NoReply or GameLinkError.NotReady => "Game Link is not getting a reply from the game.",
                _ => GameLinkErrorText(reply.Error),
            });
            return;
        }

        var marker = GameLinkWorld.PlayerMarker(folder, size, status);
        PlayerOverlay.SetMarker(marker);
        ReportPlayerTrackingStatus(marker is not null ? null : status.State switch
        {
            GameLinkState.Menu or GameLinkState.Loading => "Game Link is waiting for the player to enter the world.",
            GameLinkState.Dead => GameLinkErrorText(GameLinkError.Dead),
            GameLinkState.Remote => GameLinkErrorText(GameLinkError.Remote),
            _ when !status.Hosting => GameLinkErrorText(GameLinkError.Remote),
            _ when !GameLinkWorld.Matches(folder, status.World) => $"Game Link is running '{status.World}', a different world.",
            _ => "The player is outside the editor map.",
        });
        KeepPlayerInView();
    }

    private void ReportPlayerTrackingStatus(string? message)
    {
        if (message == _playerTrackingStatus) return;
        if (message is not null) _vm.Status = message;
        else if (_vm.Status == _playerTrackingStatus) _vm.Status = string.Empty;
        _playerTrackingStatus = message;
    }

    private void KeepPlayerInView(bool centre = false)
    {
        if (!_vm.FollowPlayer || _vm.IsLandMode || Map.IsMouseCaptured || PlayerOverlay.LiveMarker is not { } marker) return;
        var position = new Point(marker.MapX, marker.MapY);
        var visible = Map.VisibleMapRect;
        visible.Inflate(-visible.Width * 0.15, -visible.Height * 0.15);
        if (centre || !visible.Contains(position)) Map.CenterOn(position);
    }
}
