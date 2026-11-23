using MapEditor.Core.GameData;
using MapEditor.Core.IO;
using MapEditor.Core.Rendering;

namespace MapEditor.App;

/// <summary>Display values for the editor's World tab. The statistics themselves come from Core.</summary>
public sealed class WorldStatsViewModel
{
    public WorldStatsViewModel(WorldStatistics stats, GameNames names, BiomeTable biomes)
    {
        Name = stats.Name;
        Size = $"{stats.Size:N0} \u00D7 {stats.Size:N0} m";
        Height = $"{stats.MinHeightMetres:0.0} to {stats.MaxHeightMetres:0.0} m";
        LandPercent = stats.LandPercent;
        WaterPercent = stats.WaterPercent;
        Biomes = stats.BiomeLandPercent
            .OrderByDescending(b => b.Value).ThenBy(b => b.Key, StringComparer.OrdinalIgnoreCase)
            .Select(b =>
            {
                var biome = biomes.Biomes.FirstOrDefault(def => def.Name.Equals(b.Key, StringComparison.OrdinalIgnoreCase));
                uint color = biome is null ? 0x808080u : MapRenderer.BiomeDisplayColor(biome);
                return new ShareRow(names.Biome(b.Key), b.Value, color);
            }).ToArray();
        BiomeStatus = Biomes.Count > 0 ? "" : stats.LandPercent == 0 ? "No land" : "Biome data unavailable";
        Roads = [new("Asphalt", $"About {stats.AsphaltKm:0.###} km"), new("Gravel", $"About {stats.GravelKm:0.###} km")];

        Placements =
        [
            new("POIs", $"{stats.Pois:N0}"),
            new("Street tiles", $"{stats.StreetTiles:N0}"),
            new("Towns", $"{stats.Towns:N0}"),
            new("Traders", $"{stats.Traders:N0}"),
            new("Spawn points", $"{stats.SpawnPoints:N0}"),
        ];
        var tiers = stats.PoisByTier.OrderBy(t => t.Key)
            .Select(t => new ValueRow($"Tier {t.Key}", $"{t.Value:N0}")).ToList();
        if (stats.PoisWithUnknownTier > 0) tiers.Add(new("Unknown", $"{stats.PoisWithUnknownTier:N0}"));
        PoisByTier = tiers;
        PoisByBiome = stats.PoisByBiome
            .Select(b => new ValueRow(names.Biome(b.Key), $"{b.Value:N0}"))
            .OrderBy(b => b.Label, StringComparer.CurrentCultureIgnoreCase).ToArray();

        var files = new List<ValueRow> { new("Prefabs", $"{stats.Prefabs:N0}") };
        if (stats.Radiation is { } rad)
            files.Add(new("Radiation", $"{rad.Width:N0} \u00D7 {rad.Height:N0}, {rad.Marked:N0} px"));
        Files = files;
        ExtraFiles = stats.ExtraFiles.Count > 0 ? "Extra files\n" + string.Join(", ", stats.ExtraFiles) : "";
        Warnings = stats.Warnings;
    }

    public string Name { get; }
    public string Size { get; }
    public string Height { get; }
    public double LandPercent { get; }
    public double WaterPercent { get; }
    public double LandFraction => LandPercent / 100;
    public IReadOnlyList<ShareRow> Biomes { get; }
    public string BiomeStatus { get; }
    public IReadOnlyList<ValueRow> Roads { get; }
    public IReadOnlyList<ValueRow> Placements { get; }
    public IReadOnlyList<ValueRow> PoisByTier { get; }
    public IReadOnlyList<ValueRow> PoisByBiome { get; }
    public IReadOnlyList<ValueRow> Files { get; }
    public string ExtraFiles { get; }
    public IReadOnlyList<string> Warnings { get; }

    public sealed record ShareRow(string Label, double Percent, uint Color)
    {
        public double Fraction => Percent / 100;
    }
    public sealed record ValueRow(string Label, string Value);
}
