using MapEditor.Core.GameData;

namespace MapEditor.Core.Generate;

/// <summary>
/// The presets that come with the editor. Each starts from the game's settings and scales the game's town and feature
/// numbers for the world's size (see <see cref="GeneratorPreset.Adjust"/>), so it suits any world size. Read-only.
/// </summary>
public static class BuiltInPresets
{
    public static IReadOnlyList<GeneratorPreset> All { get; } =
    [
        GeneratorPreset.GameDefaults,
        Make("Big cities", "Towns at 60% of the game's count, each up to twice the street tiles.",
            (s, m) => ScaleTowns(s, m, towns: 0.6f, roadside: 1, maxTiles: 2)),
        Make("Frontier", "Half the towns, 150% roadside stops, many harder wilderness POIs, gravel roads up to 1.5 km.",
            (s, m) =>
            {
                ScaleTowns(s, m, towns: 0.5f, roadside: 1.5f, maxTiles: 1);
                s.Wilderness.Amount = 2;
                s.Wilderness.CountScale = 1.5f;
                s.Wilderness.TierBias = 0.5f;
                s.Wilderness.MaxRoad = 1500;
            }),
        Make("Border towns", "The game's counts; towns may cross biome borders, and every town gets a trader.",
            (s, _) =>
            {
                s.Towns.CrossBorders = true;
                s.Towns.Traders = TraderMode.OnePerTown;
            }),
        Make("Easy start", "Easier wilderness POIs; at least 20 spawns, in every biome, by roads within 1.2 km of a trader.",
            (s, _) =>
            {
                s.Wilderness.TierBias = -0.6f;
                s.Spawns.Biomes = [.. TownSiteStage.BiomeTags];
                s.Spawns.Min = 20;
                s.Spawns.TraderDistance = 1200;
            }),
        Make("Rugged", "Plains 2, hills 4, mountains 5; lakes on mountains too; twice the canyons.",
            (s, _) =>
            {
                s.Terrain.Weights = new TerrainWeights(2, 4, 5);
                s.Features.MountainLakes = true;
                ScaleFeature(s, FeatureKind.Canyon, 2);
            }),
        Make("Flatlands", "Plains 7, hills 3, no mountains; half the canyons.",
            (s, _) =>
            {
                s.Terrain.Weights = new TerrainWeights(7, 3, 0);
                ScaleFeature(s, FeatureKind.Canyon, 0.5f);
            }),
        Make("Waterways", "Long rivers of up to 12 stamps, twice the lakes and rivers, causeways up to 100 m.",
            (s, _) =>
            {
                s.Features.LongRivers = true;
                s.Features.RiverSegments = 12;
                ScaleFeature(s, FeatureKind.Lake, 2);
                ScaleFeature(s, FeatureKind.River, 2);
                s.Highways.CausewayMax = 100;
            }),
        Make("Wasteland heavy", "Harder wilderness POIs and twice the craters; spawns in the forest, as the game does.",
            (s, _) =>
            {
                s.Wilderness.TierBias = 0.6f;
                ScaleFeature(s, FeatureKind.Crater, 2);
            }),
    ];

    /// <summary>Whether a name belongs to a built-in preset (saving under it is refused).</summary>
    public static bool IsBuiltIn(string name) => All.Any(p => p.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    private static GeneratorPreset Make(string name, string summary, Action<GeneratorSettings, RwgMixer> adjust) =>
        new() { Name = name, Summary = summary, BuiltIn = true, Adjust = adjust };

    /// <summary>Multiplies the town rows' counts (towns and roadside stops apart) and their most street tiles.</summary>
    internal static void ScaleTowns(GeneratorSettings s, RwgMixer mixer, float towns, float roadside, float maxTiles)
    {
        foreach (var row in s.Towns.Rows)
        {
            bool isRoadside = mixer.Townships.FirstOrDefault(t => t.Name.Equals(row.Township, StringComparison.OrdinalIgnoreCase)) is { } type
                ? type.IsRoadside : row.Township.EndsWith("roadside", StringComparison.OrdinalIgnoreCase);
            float f = isRoadside ? roadside : towns;
            if (row.Count > 0) row.Count = Math.Max(1, (int)MathF.Round(row.Count * f));
            if (!isRoadside) row.MaxTiles = Math.Max(row.MinTiles, (int)MathF.Round(row.MaxTiles * maxTiles));
        }
    }

    internal static void ScaleFeature(GeneratorSettings s, FeatureKind kind, float f)
    {
        foreach (var row in s.Features.Rows.Where(r => r.Kind == kind))
            row.Count = Math.Max(0, (int)MathF.Round(row.Count * f));
    }
}
