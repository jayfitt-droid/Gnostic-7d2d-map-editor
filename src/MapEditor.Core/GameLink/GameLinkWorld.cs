using MapEditor.Core.GameData;
using MapEditor.Core.Model;

namespace MapEditor.Core.GameLink;

/// <summary>A placed POI and the centre of its rotated footprint in world coordinates.</summary>
public readonly record struct GameLinkWaypoint(string Name, int X, int Z);

/// <summary>Looks up the editor's export and waypoint targets for Game Link.</summary>
public static class GameLinkWorld
{
    /// <summary>The world folder name the game reports, without the parent path.</summary>
    public static string? ExportName(string? exportFolder)
    {
        if (exportFolder is null) return null;
        string name = Path.GetFileName(exportFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return name is "" or "." or ".." ? null : name;
    }

    /// <summary>Checks the game world against the folder this world was written to.</summary>
    public static bool Matches(string? exportFolder, string gameWorld) =>
        ExportName(exportFolder) is { } name && string.Equals(name, gameWorld, StringComparison.OrdinalIgnoreCase);

    /// <summary>The POI at the clicked world coordinates, using the same footprint as the map.</summary>
    public static GameLinkWaypoint? WaypointAt(World world, int x, int z, PrefabLibrary prefabs)
    {
        var prefab = WorldQuery.PrefabAt(world, x, z, prefabs,
            info => info is { IsStreetTile: false, IsPart: false, IsInternal: false });
        if (prefab is null || prefabs.Find(prefab.Name) is not { } info) return null;
        var (width, depth) = info.Footprint(prefab.Rotation);
        return new(prefab.Name, prefab.Position.X + width / 2, prefab.Position.Z + depth / 2);
    }
}
