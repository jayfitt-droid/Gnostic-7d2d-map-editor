using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Validate;

/// <summary>Which known fix a Validate issue offers (#40).</summary>
public enum FixKind
{
    /// <summary>Level the road and ground beside <see cref="IssueFix.Prefab"/> to its ground, easing back into the road.</summary>
    LevelRoad,
    /// <summary>Move <see cref="IssueFix.Spawn"/> to the nearest dry ground inside the world.</summary>
    MoveSpawn,
}

/// <summary>A fix a Validate issue offers, with what it applies to.</summary>
public sealed record IssueFix(FixKind Kind, PrefabInstance? Prefab = null, SpawnPoint? Spawn = null)
{
    /// <summary>The button's text.</summary>
    public string Label => Kind switch
    {
        FixKind.LevelRoad => "Level road",
        _ => "Move spawn",
    };
}

/// <summary>
/// Applies the fixes Validate offers (#40), each in the caller's <see cref="EditSession"/> so it's one undo step. Only
/// fixes with one safe answer are offered: the rest need the author's judgement.
/// </summary>
public static class IssueFixer
{
    /// <summary>How far past the road's touch zone the levelled ground eases back to the road as it was, in metres.</summary>
    public const int RoadBlend = 20;

    /// <summary>How close to the world's edge a moved spawn may stand, in metres.</summary>
    public const int EdgeMargin = 8;

    /// <summary>How far to look for dry ground for a spawn, in metres.</summary>
    public const int SpawnSearch = 400;

    /// <summary>Applies <paramref name="fix"/>. Returns a status line for what it did, or null if it found nothing it could do.</summary>
    public static string? Apply(World world, PrefabLibrary library, IssueFix fix, EditSession session) => fix switch
    {
        { Kind: FixKind.LevelRoad, Prefab: { } prefab } => LevelRoad(world, library, prefab, session),
        { Kind: FixKind.MoveSpawn, Spawn: { } spawn } => MoveSpawn(world, library, spawn, session),
        _ => null,
    };

    /// <summary>
    /// Sets the ground, road included, within <see cref="PrefabPlacer.RoadTouch"/> m of the prefab to its ground, and
    /// blends it back into the land and the road over <see cref="RoadBlend"/> m, so the road climbs or drops to meet it.
    /// </summary>
    private static string? LevelRoad(World world, PrefabLibrary library, PrefabInstance prefab, EditSession session)
    {
        if (library.Find(prefab.Name) is not { } info || !world.Prefabs.Contains(prefab)) return null;
        var cells = PrefabPlacer.Footprint(world, prefab, library);
        int ground = info.RoadLevelAbove(prefab.Position.Y);
        int touch = PrefabPlacer.RoadTouch + 1;
        var band = new GridRect(cells.X0 - touch, cells.Z0 - touch, cells.X1 + touch, cells.Z1 + touch).Clamp(world.Size, world.Size);
        PrefabPlacer.Flatten(world, band, ground, RoadBlend, session, spareRoads: false, library: library, ownTile: prefab);
        return $"Levelled the road and ground beside {prefab.Name} to {ground} m.";
    }

    /// <summary>Moves a spawn to the nearest dry cell inside the world that no prefab stands on, keeping its facing.</summary>
    private static string? MoveSpawn(World world, PrefabLibrary library, SpawnPoint spawn, EditSession session)
    {
        if (!world.Spawns.Contains(spawn)) return null;
        int lo = EdgeMargin, hi = world.Size - 1 - EdgeMargin;
        int sx = Math.Clamp(world.WorldToGrid((int)MathF.Floor(spawn.X)), lo, hi);
        int sz = Math.Clamp(world.WorldToGrid((int)MathF.Floor(spawn.Z)), lo, hi);

        // Rings outward, nearest first within each ring.
        for (int r = 0; r <= SpawnSearch; r++)
        {
            (int X, int Z)? best = null;
            int bestD = int.MaxValue;
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                    int x = sx + dx, z = sz + dz;
                    if (x < lo || z < lo || x > hi || z > hi || dx * dx + dz * dz >= bestD) continue;
                    if (!IsDry(world, x, z) || WorldQuery.PrefabAt(world, world.GridToWorld(x), world.GridToWorld(z), library) is not null) continue;
                    best = (x, z);
                    bestD = dx * dx + dz * dz;
                }
            if (best is { } at)
            {
                SpawnHelper.Move(world, spawn, at.X, at.Z, session);
                return $"Moved the spawn to {spawn.X:0}, {spawn.Z:0}.";
            }
        }
        return null;
    }

    private static bool IsDry(World world, int x, int z) =>
        world.Water?[x, z] is not (> 0 and var w) || World.HeightToMetres(world.Height[x, z]) > w + 0.5f;
}
