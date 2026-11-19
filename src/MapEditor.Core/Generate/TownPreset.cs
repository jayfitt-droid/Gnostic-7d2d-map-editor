using MapEditor.Core.GameData;

namespace MapEditor.Core.Generate;

/// <summary>Named settings for the town sites stage, kept in the app's settings.</summary>
public sealed class TownPreset
{
    public string Name { get; set; } = "";
    public List<TownRow> Rows { get; set; } = [];
    public float EdgeMargin { get; set; } = 0.1f;
    public TraderMode Traders { get; set; } = TraderMode.RwgCaps;
    public bool CrossBorders { get; set; }

    /// <summary>Copies the stage's settings apart from its seed.</summary>
    public static TownPreset From(TownSiteOptions options, string name) => new()
    {
        Name = name,
        Rows = [.. options.Rows.Select(r => r.Clone())],
        EdgeMargin = options.EdgeMargin,
        Traders = options.Traders,
        CrossBorders = options.CrossBorders,
    };

    /// <summary>
    /// Copies saved rows without scaling and keeps the world's seed. With township data, skips unknown types and
    /// adds missing buildable types from the world's current rows or the game's defaults.
    /// </summary>
    public void ApplyTo(TownSiteOptions options, RwgMixer? mixer = null)
    {
        List<TownRow> rows;
        if (mixer is { Townships.Count: > 0 })
        {
            var known = mixer.Townships.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            rows = [.. Rows.Where(r => known.Contains(r.Township)).Select(r => r.Clone())];
            var included = rows.Select(r => r.Township).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var defaults in TownSiteStage.DefaultRows(mixer))
            {
                if (!included.Add(defaults.Township)) continue;
                var current = options.Rows.FirstOrDefault(r =>
                    r.Township.Equals(defaults.Township, StringComparison.OrdinalIgnoreCase));
                rows.Add(current?.Clone() ?? defaults);
            }
        }
        else
        {
            rows = [.. Rows.Select(r => r.Clone())];
        }

        options.Rows = rows;
        options.EdgeMargin = EdgeMargin;
        options.Traders = Traders;
        options.CrossBorders = CrossBorders;
    }
}
