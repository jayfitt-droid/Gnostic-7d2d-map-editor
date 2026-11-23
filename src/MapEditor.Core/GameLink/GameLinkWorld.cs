using MapEditor.Core.GameData;
using MapEditor.Core.Model;

namespace MapEditor.Core.GameLink;

/// <summary>A placed POI and the centre of its rotated footprint in world coordinates.</summary>
public readonly record struct GameLinkWaypoint(string Name, int X, int Z);

/// <summary>The player in north-up map coordinates, with a clockwise heading from north.</summary>
public readonly record struct GameLinkPlayerMarker(double MapX, double MapY, float Yaw);

/// <summary>Relates Game Link's world and positions to the editor's map.</summary>
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

    /// <summary>The local player on this map, when the game has its exported world open.</summary>
    public static GameLinkPlayerMarker? PlayerMarker(string? exportFolder, int size, GameLinkStatus status)
    {
        if (status is not { State: GameLinkState.Ready, Hosting: true, Player: { } player } ||
            !Matches(exportFolder, status.World)) return null;

        int half = size / 2;
        double x = half + (double)player.X;
        double y = size - (half + (double)player.Z);
        return x >= 0 && x <= size && y >= 0 && y <= size && float.IsFinite(player.Yaw)
            ? new(x, y, player.Yaw)
            : null;
    }

    /// <summary>The POI at the clicked world coordinates, using the same footprint as the map.</summary>
    public static PrefabInstance? PoiAt(World world, int x, int z, PrefabLibrary prefabs) =>
        WorldQuery.PrefabAt(world, x, z, prefabs,
            info => info is { IsStreetTile: false, IsPart: false, IsInternal: false });

    /// <summary>The POI and the centre to send to the game for a temporary waypoint.</summary>
    public static GameLinkWaypoint? WaypointAt(World world, int x, int z, PrefabLibrary prefabs)
    {
        var prefab = PoiAt(world, x, z, prefabs);
        if (prefab is null || !GameLinkProtocol.IsPrefabName(prefab.Name) || prefabs.Find(prefab.Name) is not { } info) return null;
        var (width, depth) = info.Footprint(prefab.Rotation);
        return new(prefab.Name, prefab.Position.X + width / 2, prefab.Position.Z + depth / 2);
    }
}
