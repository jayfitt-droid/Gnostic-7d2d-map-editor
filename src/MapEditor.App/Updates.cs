using System.Net.Http;
using Velopack;
using Velopack.Sources;

namespace MapEditor.App;

/// <summary>
/// Update checks through Velopack. They only run for an installed copy (not a build run from source). Updates come
/// from this project's GitHub releases, unless <see cref="AppSettings.UpdateUrl"/> points at another feed (a URL or
/// folder of Velopack releases). A pre-release build (a version such as 1.1.0-beta) also takes later pre-releases;
/// a full release only takes full releases.
/// </summary>
public static class Updates
{
    /// <summary>The GitHub repository whose releases are the update feed.</summary>
    public const string Repository = "https://github.com/jayfitt-droid/Gnostic-7d2d-map-editor";

    /// <summary>
    /// The version as released, such as 2.0.0-beta.4 (set by the release build; local builds take Directory.Build.props').
    /// Anything after '+' is build metadata (the commit), not part of the version.
    /// </summary>
    public static string Version { get; } =
        typeof(Updates).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion?.Split('+')[0]
        ?? typeof(Updates).Assembly.GetName().Version?.ToString(3) ?? "";

    /// <summary>True for a pre-release build: its version has a suffix such as -beta.</summary>
    public static bool IsPrerelease => Version.Contains('-');

    private static UpdateManager Manager(AppSettings settings) =>
        string.IsNullOrWhiteSpace(settings.UpdateUrl)
            ? new UpdateManager(new GithubSource(Repository, null, IsPrerelease))
            : new UpdateManager(settings.UpdateUrl);

    public sealed record Result(string Message, UpdateManager? Manager = null, UpdateInfo? Update = null);

    public static async Task<Result> CheckAsync(AppSettings settings)
    {
        var manager = Manager(settings);
        if (!manager.IsInstalled)
            return new("Updates only work for the installed editor, not a copy run from a build folder.");
        try
        {
            var update = await manager.CheckForUpdatesAsync();
            return update is null
                ? new($"You have the latest version ({manager.CurrentVersion}).")
                : new($"Version {update.TargetFullRelease.Version} is available (you have {manager.CurrentVersion}).", manager, update);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.IO.IOException or TaskCanceledException)
        {
            return new("Could not check for updates: " + ex.Message);
        }
    }

    /// <summary>Downloads the update, then restarts into it. Unsaved work must be saved first.</summary>
    public static async Task ApplyAsync(Result result)
    {
        if (result is not { Manager: { } manager, Update: { } update }) return;
        await manager.DownloadUpdatesAsync(update);
        manager.ApplyUpdatesAndRestart(update.TargetFullRelease);
    }
}
