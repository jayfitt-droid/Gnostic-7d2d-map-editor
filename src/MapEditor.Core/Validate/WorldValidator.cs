using MapEditor.Core.Edit;
using MapEditor.Core.Generate;
using MapEditor.Core.GameData;
using MapEditor.Core.IO;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Validate;

public enum IssueSeverity
{
    /// <summary>The game cannot load the world, or loads it broken.</summary>
    Error,
    /// <summary>The world loads, but something is probably not what the author wants.</summary>
    Warning,
    Info,
}

/// <summary>One finding. <see cref="X"/>/<see cref="Z"/> are grid cells to jump to, when the issue has a place.</summary>
/// <param name="Kind">Which check raised it, when a report counts them (see <see cref="BridgeIssue"/>); null for the rest.</param>
/// <param name="Fix">The fix the Issues tab offers for it (<see cref="IssueFixer"/>), when there is a safe one (#40).</param>
public sealed record ValidationIssue(IssueSeverity Severity, string Message, int? X = null, int? Z = null, string? Kind = null, IssueFix? Fix = null)
{
    public bool HasLocation => X is not null && Z is not null;
}

/// <summary>Checks a world for problems before it is exported: spawns, traders, prefab overlaps, water and bounds.</summary>
public static class WorldValidator
{
    /// <summary>The reason export stops when a world needs a spawn point but has no dry ground.</summary>
    public const string NoDrySpawnMessage = "There are no spawn points and no dry ground for one. Make dry ground before you export.";

    /// <summary>
    /// The opening quest sends every new player to one trader (<see cref="StarterTrader"/>): warns when it finds none,
    /// or when a spawn is <see cref="StarterTrader.FarSpawn"/> or more from it, naming a nearer pine-forest trader that
    /// lost. Only for worlds with traders and spawns.
    /// </summary>
    private static void CheckStarterTrader(World world, PrefabLibrary library, BiomeTable biomes, List<ValidationIssue> issues)
    {
        // Only worlds with a trader the game takes for quests (a trader area with the "trader" quest tag).
        if (world.Spawns.Count == 0 || !world.Prefabs.Any(p => library.Find(p.Name) is { TraderArea: true } t && t.HasQuestTag("trader"))) return;
        var candidates = StarterTrader.Candidates(world, library, biomes);
        if (candidates.Count == 0)
        {
            issues.Add(new(IssueSeverity.Warning, "The opening quest finds no trader: it only looks in pine forest, and no trader stands there."));
            return;
        }
        string At(System.Numerics.Vector2 grid) => $"({world.GridToWorld((int)grid.X)}, {world.GridToWorld((int)grid.Y)})";
        if (StarterTrader.Pick(candidates) is not { } picked)
        {
            var first = candidates[0];
            issues.Add(new(IssueSeverity.Warning,
                $"The opening quest finds no trader: none of the {candidates.Count} pine-forest traders has a tier-{StarterTrader.Tier} POI within {StarterTrader.Reach:0} m.",
                (int)first.Centre.X, (int)first.Centre.Y));
            return;
        }

        // The spawn farthest from the trader the quest picks.
        (System.Numerics.Vector2 At, float D) far = (default, -1);
        foreach (var s in world.Spawns)
        {
            var at = new System.Numerics.Vector2(world.WorldToGrid((int)MathF.Floor(s.X)), world.WorldToGrid((int)MathF.Floor(s.Z)));
            float d = System.Numerics.Vector2.Distance(at, picked.Centre);
            if (d > far.D) far = (at, d);
        }
        if (far.D < StarterTrader.FarSpawn) return;
        var nearest = candidates.MinBy(c => System.Numerics.Vector2.Distance(c.Centre, far.At))!;
        string text = $"The opening quest sends new players to {picked.Trader.Name} at {At(picked.Centre)}, the pine-forest trader with the most " +
                      $"tier-{StarterTrader.Tier} POIs within {StarterTrader.Reach:0} m ({picked.Tier1}). The spawn at {At(far.At)} is {far.D / 1000:0.0} km from it.";
        if (nearest != picked)
            text += $" {nearest.Trader.Name} at {At(nearest.Centre)} is {System.Numerics.Vector2.Distance(nearest.Centre, far.At) / 1000:0.0} km from that spawn " +
                    $"but has {nearest.Tier1}. Move the spawn, or add tier-{StarterTrader.Tier} POIs near the nearer trader.";
        issues.Add(new(IssueSeverity.Warning, text, (int)far.At.X, (int)far.At.Y));
    }

    /// <summary>Stop listing per-prefab issues after this many, so a broken import stays readable.</summary>
    public const int MaxIssues = 500;

    /// <summary>A POI this much under water is flagged.</summary>
    public const int WetPercent = 25;

    /// <param name="biomes">The game's biomes, to name the biome under a trader; the vanilla ones when null.</param>
    public static List<ValidationIssue> Validate(World world, PrefabLibrary library, bool hasLevelTemplate = true, BiomeTable? biomes = null)
    {
        var issues = new List<ValidationIssue>();
        CheckStarterTrader(world, library, biomes ?? BiomeTable.Vanilla, issues);

        if (!world.ExtraFiles.ContainsKey(WorldFiles.Level) && !hasLevelTemplate)
            issues.Add(new(IssueSeverity.Error, "No main.ttw, and none was found to copy. Generate any world in the game once."));
        if (world.Size % 512 != 0)
            issues.Add(new(IssueSeverity.Warning, $"The world is {world.Size} m; the game expects a multiple of 512."));
        if (world.Biomes is null && !world.PassthroughImages.ContainsKey(WorldFiles.Biomes))
            issues.Add(new(IssueSeverity.Warning, "The world has no biomes, so the game will use one biome everywhere."));

        if (world.Spawns.Count == 0)
            issues.Add(SpawnHelper.FindDryGround(world) is null
                ? new(IssueSeverity.Error, NoDrySpawnMessage)
                : new(IssueSeverity.Warning, "There are no spawn points. Export will add one on the dry ground nearest the world centre."));
        foreach (var s in world.Spawns)
        {
            int gx = world.WorldToGrid((int)MathF.Floor(s.X)), gz = world.WorldToGrid((int)MathF.Floor(s.Z));
            if (!world.Height.Contains(gx, gz))
            {
                issues.Add(new(IssueSeverity.Error, $"Spawn point at {s.X:0}, {s.Z:0} is outside the world.", Fix: new IssueFix(FixKind.MoveSpawn, Spawn: s)));
                continue;
            }
            float ground = World.HeightToMetres(world.Height[gx, gz]);
            if (world.Water?[gx, gz] is > 0 and var w && ground <= w + 0.5f)
                issues.Add(new(IssueSeverity.Warning, $"Spawn point at {s.X:0}, {s.Z:0} is under water.", gx, gz, Fix: new IssueFix(FixKind.MoveSpawn, Spawn: s)));
        }

        if (library.Count == 0)
        {
            issues.Add(new(IssueSeverity.Info, "The game folder is not set, so prefabs were not checked."));
            return issues;
        }

        bool trader = false;
        var unknown = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var pois = new List<(PrefabInstance Prefab, GridRect Cells)>();
        int listed = 0;
        void Add(ValidationIssue issue)
        {
            if (listed++ < MaxIssues) issues.Add(issue);
        }

        // Bridge decks, for the prefabs standing on them (deck wrecks): the game's own bridges and the modular ones.
        var bridgeCells = new List<GridRect>();
        foreach (var p in world.Prefabs)
            if (library.Find(p.Name) is { IsBridge: true })
                bridgeCells.Add(PrefabPlacer.Footprint(world, p, library));
        var infos = new Dictionary<PrefabInstance, PrefabInfo>(ReferenceEqualityComparer.Instance);

        foreach (var p in world.Prefabs)
        {
            if (library.Find(p.Name) is not { } info)
            {
                unknown[p.Name] = unknown.GetValueOrDefault(p.Name) + 1;
                continue;
            }
            trader |= info.IsTrader;
            var cells = PrefabPlacer.Footprint(world, p, library);
            int mx = (cells.X0 + cells.X1) / 2, mz = (cells.Z0 + cells.Z1) / 2;
            if (cells.X0 < 0 || cells.Z0 < 0 || cells.X1 > world.Size || cells.Z1 > world.Size)
            {
                Add(new(IssueSeverity.Error, $"{p.Name} is outside the world.", Math.Clamp(mx, 0, world.Size - 1), Math.Clamp(mz, 0, world.Size - 1)));
                continue;
            }
            if (info.IsPart) continue;
            if (PrefabPlacer.RoadMismatch(world, cells, info.RoadLevelAbove(p.Position.Y)) is var step and > PrefabPlacer.RoadStep)
                Add(new(IssueSeverity.Warning, $"{p.Name} doesn't meet the road beside it level: the road is up to {step:0.0} m off its ground. Flatten the road and the ground on both sides, then place it again.", mx, mz,
                    Fix: new IssueFix(FixKind.LevelRoad, p)));
            if (info.IsStreetTile) continue;
            pois.Add((p, cells));
            infos[p] = info;

            // Bridges stand over water by design, and so does anything the game only places on one (a deck wreck).
            if (!info.IsBridge && !(info.HasTag("navonly") && BridgeChecks.OnBridge(cells, bridgeCells)) && world.Water is { } water && WetShare(world, water, cells) is var wet and >= WetPercent)
                Add(new(IssueSeverity.Warning, $"{p.Name} is {wet}% under water.", mx, mz));
        }

        foreach (var (a, b) in Overlaps(pois))
        {
            // Bridge pieces lie end to end, Navezgane's bridges carry their own signs, and a deck wreck stands on the deck.
            PrefabInfo ia = infos[a.Prefab], ib = infos[b.Prefab];
            if (ia.IsBridge && (ib.IsBridge || ib.HasTag("navonly") && BridgeChecks.OnBridge(b.Cells, bridgeCells))) continue;
            if (ib.IsBridge && ia.HasTag("navonly") && BridgeChecks.OnBridge(a.Cells, bridgeCells)) continue;
            Add(new(IssueSeverity.Warning, $"{a.Prefab.Name} overlaps {b.Prefab.Name}.",
                (Math.Max(a.Cells.X0, b.Cells.X0) + Math.Min(a.Cells.X1, b.Cells.X1)) / 2,
                (Math.Max(a.Cells.Z0, b.Cells.Z0) + Math.Min(a.Cells.Z1, b.Cells.Z1)) / 2));
        }

        if (listed > MaxIssues)
            issues.Add(new(IssueSeverity.Info, $"… and {listed - MaxIssues:N0} more prefab issues."));
        foreach (var (name, count) in unknown.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            issues.Add(new(IssueSeverity.Warning, $"{name} ({count}×) is not in the game folder; the game will skip it."));
        issues.AddRange(BridgeChecks.Analyze(world, library).Issues);
        if (!trader)
            issues.Add(new(IssueSeverity.Warning, "There is no trader, so the game's trader quests will have nowhere to go."));
        return issues;
    }

    /// <summary>The share of <paramref name="cells"/> under water, in percent.</summary>
    private static int WetShare(World world, Grid<byte> water, GridRect cells)
    {
        long wet = 0;
        for (int z = cells.Z0; z < cells.Z1; z++)
            for (int x = cells.X0; x < cells.X1; x++)
                if (water[x, z] is > 0 and var w && World.HeightToMetres(world.Height[x, z]) <= w + 0.5f) wet++;
        long area = (long)(cells.X1 - cells.X0) * (cells.Z1 - cells.Z0);
        return area == 0 ? 0 : (int)(wet * 100 / area);
    }

    /// <summary>Pairs of overlapping footprints, found through a coarse bucket grid so large worlds stay fast.</summary>
    private static IEnumerable<((PrefabInstance Prefab, GridRect Cells) A, (PrefabInstance Prefab, GridRect Cells) B)> Overlaps(
        List<(PrefabInstance Prefab, GridRect Cells)> items)
    {
        const int bucket = 64;
        var buckets = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < items.Count; i++)
        {
            var c = items[i].Cells;
            for (int bz = c.Z0 / bucket; bz <= (c.Z1 - 1) / bucket; bz++)
                for (int bx = c.X0 / bucket; bx <= (c.X1 - 1) / bucket; bx++)
                {
                    if (!buckets.TryGetValue((bx, bz), out var list)) buckets[(bx, bz)] = list = [];
                    list.Add(i);
                }
        }

        var seen = new HashSet<(int, int)>();
        foreach (var list in buckets.Values)
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                {
                    var (a, b) = (items[list[i]].Cells, items[list[j]].Cells);
                    if (a.X0 < b.X1 && a.X1 > b.X0 && a.Z0 < b.Z1 && a.Z1 > b.Z0 && seen.Add((list[i], list[j])))
                        yield return (items[list[i]], items[list[j]]);
                }
    }
}
