using System.Diagnostics;
using System.IO;
using System.Windows;
using MapEditor.Core.GameData;
using MapEditor.Core.GameLink;
using MapEditor.Core.Messages;
using MapEditor.Core.Model;
using MapEditor.Core.Validate;

namespace MapEditor.App;

public partial class MainWindow
{
    private sealed record PictureTarget(string Name, GameLinkPicture? Frame);

    private CancellationTokenSource? _pictureBatch;

    private PictureTarget PictureOfPrefab(World world, PrefabInstance prefab) =>
        new(prefab.Name, GameLinkPicture.ForPrefab(world, prefab, _vm.Prefabs.Find(prefab.Name)));

    private async void TakeIssuePictures_Click(object sender, RoutedEventArgs e)
    {
        if (_world is not { } world || _vm.Issues.Count == 0) return;
        var targets = _vm.Issues.Select((issue, index) => new PictureTarget($"issue-{index + 1}",
            issue is { X: int x, Z: int z } && TeleportCommand.CoordinatesForCell(world, x, z) is { } cell
                ? GameLinkPicture.ForSpot(world, cell.X, cell.Z)
                : null)).ToArray();
        await TakeGameLinkPicturesAsync(world, targets, batch: true);
    }

    private async Task TakeGameLinkPicturesAsync(World world, IReadOnlyList<PictureTarget> targets, bool batch)
    {
        if (!CanUseGameLink(world) || !_vm.GameLinkInstalled) return;
        if (_projectPath is not { } project)
        {
            ThemedMessageBox.Show(this, "Save the project before you take a picture.", "Take Picture",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string? exportFolder = GameLinkExportFolder;
        string pictures = Path.Combine(Path.GetDirectoryName(project)!, Path.GetFileNameWithoutExtension(project) + " pictures");
        bool SameProject() => CanUseGameLink(world) && project == _projectPath && exportFolder == GameLinkExportFolder;
        await RunCheckedGameLinkActionAsync(exportFolder, SameProject, "Take Picture", "Take Picture Anyway",
            "Picture capture cancelled.", async (game, _) =>
        {
            using var cancellation = new CancellationTokenSource();
            if (batch) _pictureBatch = cancellation;
            var token = cancellation.Token;
            int saved = 0, failed = 0;
            string? lastFailure = null;
            try
            {
                for (int i = 0; i < targets.Count && SameProject() && !token.IsCancellationRequested; i++)
                {
                    _vm.Report(batch
                        ? $"Picture {i + 1} of {targets.Count}... Press Esc to cancel. {lastFailure}".TrimEnd()
                        : "Taking picture...", MessageKind.Progress);

                    var (path, error) = await CapturePictureAsync(targets[i], pictures, game.World, SameProject, token);
                    if (path is not null) saved++;
                    if (error is not null)
                    {
                        failed++;
                        lastFailure = $"Picture {i + 1} ({targets[i].Name}) failed. {error}";
                    }
                    if (!batch)
                        _vm.Report(path is not null ? $"Picture saved to {path}" : error ?? "Picture capture cancelled.",
                            error is not null ? MessageKind.Error : MessageKind.Normal);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // An in-flight capture finishes and saves before the batch stops.
            }
            finally
            {
                _pictureBatch = null;
            }

            if (batch)
            {
                string result = token.IsCancellationRequested || !SameProject() ? "Pictures cancelled." : "Pictures complete.";
                string location = saved > 0 ? $" Folder: {pictures}." : "";
                _vm.Report($"{result} {saved} saved, {failed} failed.{location} {lastFailure}".TrimEnd(),
                    failed > 0 ? MessageKind.Warning : MessageKind.Normal);
            }
        });
    }

    private async Task<(string? Path, string? Error)> CapturePictureAsync(PictureTarget target, string folder,
        string gameWorld, Func<bool> sameProject, CancellationToken cancel)
    {
        if (target.Frame is not { } frame)
            return (null, "This issue has no place on the map.");

        async Task<(GameLinkStatus? Game, string? Error)> CheckGameAsync()
        {
            var status = await _gameLink.GetStatusAsync(cancel);
            ShowGameLinkStatus(status.Status, status.Error);
            if (status.Status is not { } game) return (null, GameLinkErrorText(status.Error));
            return string.Equals(game.World, gameWorld, StringComparison.OrdinalIgnoreCase)
                ? (game, null) : (null, "The game changed worlds.");
        }

        var (before, checkError) = await CheckGameAsync();
        if (before is null) return (null, checkError);
        if (GameLinkReadinessError(before) is { } beforeError) return (null, GameLinkErrorText(beforeError));
        cancel.ThrowIfCancellationRequested();
        if (!sameProject()) return (null, null);

        var standing = frame.StandingPoint;
        var teleport = await _gameLink.TeleportAsync(standing.X, standing.Z, frame.StandingFacing);
        if (!teleport.Ok) return (null, GameLinkErrorText(teleport.Error));

        var timeout = Stopwatch.StartNew();
        bool chunksWaited = false;
        while (sameProject() && timeout.Elapsed < TimeSpan.FromSeconds(15))
        {
            cancel.ThrowIfCancellationRequested();
            await Task.Delay(chunksWaited ? TimeSpan.FromSeconds(3) : TimeSpan.FromSeconds(1), cancel);
            if (!sameProject()) return (null, null);
            var (game, errorText) = await CheckGameAsync();
            if (game is null) return (null, errorText);
            if (GameLinkReadinessError(game) is { } error)
            {
                if (error != GameLinkError.NotReady) return (null, GameLinkErrorText(error));
                chunksWaited = false;
                continue;
            }
            if (chunksWaited)
            {
                cancel.ThrowIfCancellationRequested();
                if (!sameProject()) return (null, null);
                // Let the mod restore the view and the editor save this PNG if Esc is pressed during capture.
                var picture = await _gameLink.TakePictureAsync(frame.Camera, frame.Look);
                if (picture.FileName is not { } file) return (null, GameLinkErrorText(picture.Error));
                return await Task.Run(() => CopyGameLinkPicture(target.Name, frame.Look, folder, file));
            }
            chunksWaited = true;
        }

        return sameProject() ? (null, GameLinkErrorText(GameLinkError.NotReady)) : (null, null);
    }

    private static (string? Path, string? Error) CopyGameLinkPicture(string name, Int3 look, string folder, string file)
    {
        try
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            string safeName = string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c).Take(150));
            string path = Path.Combine(folder, FormattableString.Invariant($"{safeName}_{look.X}_{look.Z}_{DateTime.Now:yyyyMMdd-HHmmss}.png"));
            Directory.CreateDirectory(folder);
            GameLinkPictureFile.CopyToProject(GameInstall.GameLinkPicturesDir, file, path);
            return (path, null);
        }
        catch (Exception ex) when (IsWriteError(ex))
        {
            return (null, $"Could not save the picture. {ex.Message}");
        }
    }
}
