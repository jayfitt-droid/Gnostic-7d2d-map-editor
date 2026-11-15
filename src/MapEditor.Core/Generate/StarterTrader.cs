using System.Numerics;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Generate;

/// <summary>A pine-forest trader the opening quest may send new players to, and how many tier-1 POIs it has in reach.</summary>
/// <param name="Centre">The trader's middle, in grid metres.</param>
/// <param name="Tier1">Tier-1 quest POIs whose corner is within <see cref="StarterTrader.Reach"/> of the trader's corner.</param>
public sealed record StarterCandidate(PrefabInstance Trader, Vector2 Centre, int Tier1);

/// <summary>
/// The trader the game's opening quest (quest_whiteRiverCitizen1, "go to a trader") sends every new player to. Its
/// Goto objective only takes traders in pine forest, and as a "traderquest" it doesn't take the closest: it takes the
/// one with the most tier-1 POIs within 500 m, at any distance from the player, the first in prefabs.xml on a tie, and
/// none when no trader has any (DynamicPrefabDecorator.chooseBestTrader, QuestEventManager.SetupTraderPrefabList).
/// So a trader beside the spawns that is ringed by harder POIs can lose to one across the map.
/// </summary>
public static class StarterTrader
{
    /// <summary>The only biome the opening quest looks for traders in.</summary>
    public const string Biome = "pine_forest";

    /// <summary>The quest tier whose POIs are counted.</summary>
    public const int Tier = 1;

    /// <summary>How far from the trader its POIs are counted, in metres (corner to corner, as the game does).</summary>
    public const float Reach = 500;

    /// <summary>
    /// A spawn this far or more from the starter trader, in metres, gets a validation warning: about the walk a new
    /// player should make on day one (the user's choice). Stage 6 keeps its spawns inside it when placing them by the
    /// starter trader.
    /// </summary>
    public const float FarSpawn = 750;

    /// <summary>The pine-forest traders, in prefab order, with their tier-1 counts.</summary>
    public static List<StarterCandidate> Candidates(World world, PrefabLibrary library, BiomeTable biomes)
    {
        var tier1 = new List<PrefabInstance>();
        var traders = new List<PrefabInstance>();
        foreach (var p in world.Prefabs)
        {
            if (library.Find(p.Name) is not { IsQuestPoi: true } info) continue;
            if (info.DifficultyTier == Tier) tier1.Add(p);
            if (info.TraderArea && info.HasQuestTag("trader") && !p.Name.Contains("rwg_tile", StringComparison.OrdinalIgnoreCase))
                traders.Add(p);
        }

        var list = new List<StarterCandidate>();
        foreach (var t in traders)
        {
            var f = PrefabPlacer.Footprint(world, t, library);
            var centre = new Vector2((f.X0 + f.X1) / 2f, (f.Z0 + f.Z1) / 2f);
            if (BiomeName(world, biomes, (int)centre.X, (int)centre.Y) != Biome) continue;
            int count = 0;
            foreach (var p in tier1)
            {
                float dx = p.Position.X - t.Position.X, dy = p.Position.Y - t.Position.Y, dz = p.Position.Z - t.Position.Z;
                if (dx * dx + dy * dy + dz * dz <= Reach * Reach) count++;
            }
            list.Add(new StarterCandidate(t, centre, count));
        }
        return list;
    }

    /// <summary>The trader the quest picks: the first with the most tier-1 POIs, or null when none has any.</summary>
    public static StarterCandidate? Pick(IReadOnlyList<StarterCandidate> candidates)
    {
        StarterCandidate? best = null;
        foreach (var c in candidates)
            if (c.Tier1 > (best?.Tier1 ?? 0)) best = c;
        return best;
    }

    public static StarterCandidate? Find(World world, PrefabLibrary library, BiomeTable biomes) => Pick(Candidates(world, library, biomes));

    private static string? BiomeName(World world, BiomeTable biomes, int x, int z)
    {
        if (world.Biomes is not { } b) return null;
        int s = Math.Max(1, world.Size / b.Indices.Width);
        x = Math.Clamp(x, 0, world.Size - 1);
        z = Math.Clamp(z, 0, world.Size - 1);
        return biomes.FindByColor(b.Palette[b.Indices[x / s, z / s]])?.Name;
    }
}
