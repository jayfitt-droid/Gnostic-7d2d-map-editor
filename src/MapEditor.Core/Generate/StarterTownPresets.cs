namespace MapEditor.Core.Generate;

/// <summary>Fixed town settings for common map sizes, offered as editable presets on first use.</summary>
public static class StarterTownPresets
{
    /// <summary>Makes a fresh copy of each starter preset.</summary>
    public static IReadOnlyList<TownPreset> Create() =>
    [
        Make("Small towns (4 km)", 0.08f,
            ("oldwest", 1, 2, 4), ("forest_countrytown", 1, 2, 4),
            ("bforest_countrytown", 1, 2, 4), ("bforest_town", 0, 4, 6),
            ("countrytown", 1, 2, 4), ("town", 2, 4, 7),
            ("city", 0, 7, 10), ("wasteland_city", 0, 7, 10),
            ("citybig", 0, 10, 14), ("roadside", 3, 1, 1)),
        Make("Mixed towns (6 km)", 0.10f,
            ("oldwest", 1, 2, 4), ("forest_countrytown", 2, 2, 4),
            ("bforest_countrytown", 1, 2, 4), ("bforest_town", 1, 4, 7),
            ("countrytown", 2, 2, 5), ("town", 3, 4, 8),
            ("city", 1, 7, 11), ("wasteland_city", 1, 7, 11),
            ("citybig", 0, 10, 15), ("roadside", 5, 1, 1)),
        Make("More cities (8 km)", 0.10f,
            ("oldwest", 1, 2, 4), ("forest_countrytown", 2, 2, 5),
            ("bforest_countrytown", 2, 2, 5), ("bforest_town", 2, 4, 8),
            ("countrytown", 2, 2, 5), ("town", 4, 5, 9),
            ("city", 3, 8, 13), ("wasteland_city", 2, 8, 13),
            ("citybig", 1, 12, 18), ("roadside", 7, 1, 1)),
    ];

    private static TownPreset Make(string name, float edgeMargin,
        params (string Township, int Count, int Min, int Max)[] rows) => new()
    {
        Name = name,
        EdgeMargin = edgeMargin,
        Rows = [.. rows.Select(row => new TownRow
        {
            Township = row.Township,
            Count = row.Count,
            MinTiles = row.Min,
            MaxTiles = row.Max,
        })],
    };
}
