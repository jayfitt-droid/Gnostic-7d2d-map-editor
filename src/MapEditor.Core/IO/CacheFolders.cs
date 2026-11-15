namespace MapEditor.Core.IO;

/// <summary>
/// Where the editor keeps files it can rebuild, under %LOCALAPPDATA%. They live apart from %LOCALAPPDATA%\7D2DMapEditor,
/// because Setup installs there and takes any folder of that name for an installed editor (issue #11).
/// </summary>
public static class CacheFolders
{
    /// <summary>The installer's folder, named after the Velopack pack id.</summary>
    public const string InstallFolder = "7D2DMapEditor";

    /// <summary>The cache folder, next to the install folder.</summary>
    public const string CacheFolder = "7D2DMapEditor-Cache";

    /// <summary>The folder for prefab pictures on the map.</summary>
    public static string PrefabDetail(string localAppData) => Path.Combine(localAppData, CacheFolder, "PrefabDetail");

    /// <summary>
    /// Deletes the prefab picture cache that versions before 2.0.0 kept in the install folder, and then the install
    /// folder if that leaves it empty (a portable copy made it, so Setup would refuse to install). An installed editor's
    /// own files are never touched. Best effort: a file in use stays until the next start.
    /// </summary>
    public static void RemoveOldCache(string localAppData)
    {
        string install = Path.Combine(localAppData, InstallFolder);
        try
        {
            string old = Path.Combine(install, "PrefabDetail");
            if (Directory.Exists(old)) Directory.Delete(old, recursive: true);
            if (Directory.Exists(install) && !Directory.EnumerateFileSystemEntries(install).Any()) Directory.Delete(install);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Try again next time.
        }
    }
}
