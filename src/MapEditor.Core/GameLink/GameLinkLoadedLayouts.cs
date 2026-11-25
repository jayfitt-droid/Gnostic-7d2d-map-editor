using System.Security.Cryptography;
using System.Text;
using MapEditor.Core.IO;
using MapEditor.Core.Model;

namespace MapEditor.Core.GameLink;

/// <summary>
/// Keeps, per export folder, the POI layout that an export replaced while the game still had the world open. The game
/// keeps using that layout until it loads the folder again, so the rebuild warning counts it too (#245). One small
/// prefabs file per folder, named from a hash of the folder's full path.
/// </summary>
public sealed class GameLinkLoadedLayouts(string folder)
{
    /// <summary>Adds <paramref name="replaced"/> to the layout kept for <paramref name="exportFolder"/>.</summary>
    public void Keep(string exportFolder, IReadOnlyList<PrefabInstance> replaced)
    {
        var all = Read(exportFolder);
        var seen = new HashSet<(string, Int3, int)>(all.Select(Key), new KeyComparer());
        foreach (var prefab in replaced)
            if (seen.Add(Key(prefab))) all.Add(prefab);

        string path = PathFor(exportFolder);
        Directory.CreateDirectory(folder);
        string staged = path + $".{Guid.NewGuid():N}.pending";
        try
        {
            WorldXml.WritePrefabs(staged, all);
            File.Move(staged, path, overwrite: true);
        }
        finally
        {
            try { File.Delete(staged); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Keeps the prefabs now in the export folder's prefabs.xml, before an export replaces them.</summary>
    public void KeepCurrent(string exportFolder)
    {
        try { Keep(exportFolder, WorldXml.ReadPrefabs(Path.Combine(exportFolder, WorldFiles.Prefabs))); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { }
    }

    /// <summary>The kept prefabs, or none if nothing is kept or the file can't be read.</summary>
    public List<PrefabInstance> Read(string exportFolder)
    {
        try { return WorldXml.ReadPrefabs(PathFor(exportFolder)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException
            or InvalidOperationException) { return []; }
    }

    /// <summary>Drops the layout kept for <paramref name="exportFolder"/>.</summary>
    public void Forget(string exportFolder)
    {
        try { File.Delete(PathFor(exportFolder)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Whether the game has dropped the layout it loaded from <paramref name="exportFolder"/>: it is at the main menu
    /// or in another world, so its next load reads the current files. An unknown status keeps the layout.
    /// </summary>
    public static bool ShouldForget(GameLinkStatus? status, string exportFolder) =>
        status is not null && (status.State == GameLinkState.Menu || !SameWorld(status, exportFolder));

    /// <summary>Whether the game has a save of the world in <paramref name="exportFolder"/> open.</summary>
    public static bool HasWorldOpen(GameLinkStatus? status, string exportFolder) =>
        status is { State: not GameLinkState.Menu } && SameWorld(status, exportFolder);

    private static bool SameWorld(GameLinkStatus status, string exportFolder) =>
        string.Equals(status.World, Path.GetFileName(Path.TrimEndingDirectorySeparator(exportFolder)),
            StringComparison.OrdinalIgnoreCase);

    private string PathFor(string exportFolder)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(exportFolder)).ToUpperInvariant();
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..32];
        return Path.Combine(folder, hash + ".xml");
    }

    private static (string, Int3, int) Key(PrefabInstance p) => (p.Name, p.Position, p.Rotation);

    private sealed class KeyComparer : IEqualityComparer<(string, Int3, int)>
    {
        public bool Equals((string, Int3, int) a, (string, Int3, int) b) =>
            string.Equals(a.Item1, b.Item1, StringComparison.OrdinalIgnoreCase) && a.Item2 == b.Item2 && a.Item3 == b.Item3;

        public int GetHashCode((string, Int3, int) k) =>
            HashCode.Combine(k.Item1.ToUpperInvariant(), k.Item2, k.Item3);
    }
}
