using System.Text;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;

namespace MapEditor.Core.IO;

/// <summary>Human-readable world statistics, shared by the CLI and the app.</summary>
public static class WorldSummaryFormatter
{
    public static string Format(World world) =>
        Format(WorldStatistics.Measure(world, BiomeTable.Vanilla, PrefabLibrary.Empty));

    public static string Format(WorldStatistics stats)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"World      {stats.Name}");
        sb.AppendLine($"Size       {stats.Size} x {stats.Size} m");
        sb.AppendLine($"Height     {stats.MinHeightMetres:0.0} .. {stats.MaxHeightMetres:0.0} m");
        sb.AppendLine($"Land       {stats.LandPercent:0.0}%");
        sb.AppendLine($"Water      {stats.WaterPercent:0.0}%");
        sb.AppendLine("Biomes (% of land)");
        foreach (var (name, percent) in stats.BiomeLandPercent.OrderBy(b => b.Key, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine($"  {name}  {percent:0.0}%");
        if (stats.BiomeLandPercent.Count == 0)
            sb.AppendLine(stats.LandPercent == 0 ? "  No land" : "  Unavailable");

        sb.AppendLine("Road length (estimate)");
        sb.AppendLine($"  Asphalt  about {stats.AsphaltKm:0.###} km");
        sb.AppendLine($"  Gravel   about {stats.GravelKm:0.###} km");
        sb.AppendLine($"  Uses {WorldStatistics.AsphaltWidth} m asphalt and {WorldStatistics.GravelWidth} m gravel widths");
        sb.AppendLine($"Prefabs    {stats.Prefabs:N0}");
        sb.AppendLine($"POIs       {stats.Pois:N0}");
        if (stats.Pois > 0)
        {
            sb.AppendLine("  By tier");
            foreach (var (tier, count) in stats.PoisByTier.OrderBy(t => t.Key))
                sb.AppendLine($"    Tier {tier}  {count:N0}");
            if (stats.PoisWithUnknownTier > 0) sb.AppendLine($"    Unknown  {stats.PoisWithUnknownTier:N0}");
            sb.AppendLine("  By biome");
            foreach (var (name, count) in stats.PoisByBiome.OrderBy(b => b.Key, StringComparer.OrdinalIgnoreCase))
                sb.AppendLine($"    {name}  {count:N0}");
        }
        sb.AppendLine($"Street tiles  {stats.StreetTiles:N0}");
        sb.AppendLine($"Towns         {stats.Towns:N0}");
        sb.AppendLine($"Traders       {stats.Traders:N0}");
        sb.AppendLine($"Spawn points  {stats.SpawnPoints:N0}");

        if (stats.Radiation is { } rad)
            sb.AppendLine($"Radiation  {rad.Width} x {rad.Height}, {rad.Marked:N0} px");
        sb.AppendLine($"Extra      {string.Join(", ", stats.ExtraFiles)}");
        foreach (string warning in stats.Warnings) sb.AppendLine($"Warning    {warning}");
        return sb.ToString();
    }
}
