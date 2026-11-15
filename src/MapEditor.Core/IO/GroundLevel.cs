using MapEditor.Core.GameData;
using MapEditor.Core.Model;

namespace MapEditor.Core.IO;

/// <summary>
/// prefabs.xml gives a prefab's Y in one of two ways. RWG writes the bottom of its box, with the YOffset already
/// applied. The game's own editor writes Y at ground level (box Y minus YOffset) with <c>y_is_groundlevel="true"</c>,
/// and the game adds the YOffset back when it loads the world (DynamicPrefabDecorator). Shipped worlds such as
/// Navezgane use the second form.
/// <para>
/// The editor works in box Y throughout, so ground-level prefabs are converted once the prefab library is known.
/// Export writes every prefab at ground level with the flag (v2.0.0), so a POI stays on the ground it was placed on
/// even if a game update or a mod changes its YOffset. A prefab the library doesn't know can't be converted, so it
/// is written in the form it was read.
/// </para>
/// </summary>
public static class GroundLevel
{
    public const string Attribute = "y_is_groundlevel";

    /// <summary>
    /// Converts every ground-level prefab the library knows to box Y. Safe to run again: converted prefabs are
    /// skipped, so call it whenever the world or the library changes, and after undo or redo. Returns how many
    /// were converted.
    /// </summary>
    public static int Resolve(World world, PrefabLibrary library)
    {
        int count = 0;
        foreach (var p in world.Prefabs)
        {
            if (!p.NeedsGroundLevelOffset || library.Find(p.Name) is not { } info) continue;
            p.Position = p.Position with { Y = p.Position.Y + info.YOffset };
            p.GroundLevelOffset = info.YOffset;
            count++;
        }
        return count;
    }

    /// <summary>
    /// The Y to write to prefabs.xml, and whether it is at ground level (written with the flag). That is box Y minus
    /// the YOffset the game will add back: the library's when it knows the prefab, else the one a ground-level prefab
    /// was converted with. A prefab neither gives a YOffset for stays as it was read.
    /// </summary>
    public static (int Y, bool AtGroundLevel) ForFile(PrefabInstance p, PrefabLibrary? library)
    {
        // Still at ground level as read: already what the game wants.
        if (p.NeedsGroundLevelOffset) return (p.Position.Y, true);
        if (library?.Find(p.Name) is { } info) return (p.Position.Y - info.YOffset, true);
        if (p.GroundLevel && p.GroundLevelOffset is { } converted) return (p.Position.Y - converted, true);
        return (p.Position.Y, false);
    }

    /// <summary>The prefabs export can't write at ground level, because <paramref name="library"/> doesn't know them.</summary>
    public static List<PrefabInstance> KeptInBoxForm(IEnumerable<PrefabInstance> prefabs, PrefabLibrary? library) =>
        prefabs.Where(p => !ForFile(p, library).AtGroundLevel).ToList();
}
