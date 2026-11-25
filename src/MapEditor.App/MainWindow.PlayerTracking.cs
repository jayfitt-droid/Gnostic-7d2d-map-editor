using System.IO;
using System.Windows;
using System.Windows.Threading;
using MapEditor.Core.GameData;
using MapEditor.Core.GameLink;

namespace MapEditor.App;

public partial class MainWindow
{
    private readonly DispatcherTimer _gameLinkPollTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private CancellationTokenSource? _gameLinkPollRequest;
    private bool _gameLinkPollingStopped;
    private string? _playerTrackingStatus;
    private bool _centeringOnPlayer;

    private bool CanPollGameLink => !_gameLinkPollingStopped && !_vm.IsBusy && !_gameLinkActionRunning;

    private void UpdatePlayerPolling()
    {
        _gameLinkPollTimer.Interval = TimeSpan.FromSeconds(_vm.ShowPlayer ? 1 : 3);
        if (!_vm.ShowPlayer)
        {
            _vm.FollowPlayer = false;
            ClearPlayerMarker();
        }
        if (!_gameLinkPollingStopped && _gameLinkPollRequest is null) _gameLinkPollTimer.Start();
    }

    private void StopGameLinkPolling()
    {
        _gameLinkPollingStopped = true;
        _gameLinkPollTimer.Stop();
        _gameLinkPollRequest?.Cancel();
    }

    private void RefreshGameLinkInstallation()
    {
        string folder = Path.Combine(GameInstall.UserModsDir, "GnosticGameLink");
        bool installed = File.Exists(Path.Combine(folder, "ModInfo.xml")) && File.Exists(Path.Combine(folder, "GnosticGameLink.dll"));
        if (_vm.GameLinkInstalled == installed) return;
        _vm.GameLinkInstalled = installed;
        if (!installed) StopIssueHotkeys();
        UpdateIssueStepControls();
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

    /// <summary>One status request serves the connection indicator and, when enabled, the player marker.</summary>
    private async void PollGameLink_Tick(object? sender, EventArgs e)
    {
        _gameLinkPollTimer.Stop();
        try
        {
            if (!CanPollGameLink) return;
            RefreshGameLinkInstallation();
            var world = _world;
            string? folder = GameLinkExportFolder;
            bool showPlayer = _vm.ShowPlayer && world is not null;
            bool canShowPlayer = showPlayer && GameLinkWorld.ExportName(folder) is not null;
            if (showPlayer && !canShowPlayer) ReportPlayerTrackingStatus("Export this world to show the player.");
            if (!_vm.GameLinkInstalled && !canShowPlayer && GameLinkWorld.ExportName(folder) is null && _savedPinAcks.Count == 0)
            {
                ShowGameLinkStatus(null);
                _pinsUnsupported = false;
                return;
            }

            using var request = new CancellationTokenSource();
            _gameLinkPollRequest = request;
            try
            {
                var reply = await _gameLink.GetStatusAsync(request.Token);
                if (request.IsCancellationRequested || !CanPollGameLink) return;
                ShowGameLinkStatus(reply.Status);
                if (canShowPlayer && _vm.ShowPlayer && ReferenceEquals(world, _world) && folder == GameLinkExportFolder)
                    UpdatePlayerMarker(folder, world!.Size, reply);
                await PollPinsAsync(world, folder, reply.Status, request.Token);
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested)
            {
                // The editor closed.
            }
        }
        finally
        {
            _gameLinkPollRequest = null;
            if (!_gameLinkPollingStopped) _gameLinkPollTimer.Start();
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
        if (message == _playerTrackingStatus || ShowingRebuildResult) return;
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
        if (!centre && visible.Contains(position)) return;

        _centeringOnPlayer = true;
        try { Map.CenterOn(position); }
        finally { _centeringOnPlayer = false; }
    }
}
