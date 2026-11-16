using System.Globalization;

namespace MapEditor.Core.Model;

/// <summary>The way a player looks after a teleport. N is +z, as in the game.</summary>
public enum Facing { N, NE, E, SE, S, SW, W, NW }

/// <summary>
/// Builds the game console's <c>teleport x z [facing]</c> command. With only x and z the game puts the player on the
/// ground at that spot; the facing turns them that way.
/// </summary>
public static class TeleportCommand
{
    /// <summary>The command for game coordinates <paramref name="worldX"/>, <paramref name="worldZ"/> (0, 0 at the map's centre).</summary>
    public static string Format(int worldX, int worldZ, Facing? facing)
    {
        string command = string.Create(CultureInfo.InvariantCulture, $"teleport {worldX} {worldZ}");
        return facing is { } f ? command + " " + f.ToString().ToLowerInvariant() : command;
    }

    /// <summary>
    /// The command for grid cell (<paramref name="x"/>, <paramref name="z"/>), in the same game coordinates as the
    /// status bar, or null if the cell is off the map.
    /// </summary>
    public static string? ForCell(World world, int x, int z, Facing? facing) =>
        world.Height.Contains(x, z) ? Format(world.GridToWorld(x), world.GridToWorld(z), facing) : null;
}
