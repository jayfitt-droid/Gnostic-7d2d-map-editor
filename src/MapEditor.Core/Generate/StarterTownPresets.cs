namespace MapEditor.Core.Generate;

/// <summary>Fixed town settings for common map sizes and styles, offered as editable presets.</summary>
public static class StarterTownPresets
{
    /// <summary>The town types in the order of each starter's counts and tile ranges.</summary>
    private static readonly string[] Townships =
    [
        "oldwest", "forest_countrytown", "bforest_countrytown", "bforest_town", "countrytown",
        "town", "city", "wasteland_city", "citybig", "roadside",
    ];

    /// <summary>The game's fewest street tiles between a township and any other: villages 4, the rest 5.</summary>
    private static int Distance(string township) =>
        township is "forest_countrytown" or "bforest_countrytown" or "countrytown" ? 4 : 5;

    /// <summary>Makes a fresh copy of each starter preset.</summary>
    public static IReadOnlyList<TownPreset> Create() =>
    [
        //                             oldwest   forest vil  burnt vil  burnt town  village   town      city        wasteland   big city    roadside
        Make("6K Many towns",        (2,1,2),  (6,4,4),    (4,4,5),   (2,6,7),    (3,5,6),  (3,7,8),  (3,10,15),  (3,7,14),   (1,28,40),  (12,1,1)),
        Make("6K Countryside",         (1,1,2),  (6,3,5),    (4,3,5),   (1,5,7),    (4,4,6),  (2,6,8),  (1,10,12),  (1,7,10),   (0,28,40),  (10,1,1)),
        Make("6K Cities",              (1,1,2),  (2,4,4),    (1,4,5),   (1,6,7),    (1,5,6),  (2,7,9),  (3,12,18),  (2,10,16),  (1,30,40),  (8,1,1)),
        Make("8K Many towns",        (2,2,2),  (6,4,5),    (5,5,6),   (2,6,7),    (5,5,6),  (4,7,8),  (4,10,15),  (4,7,14),   (2,28,40),  (17,1,1)),
        Make("8K Countryside",         (2,2,2),  (8,3,5),    (5,4,6),   (2,5,7),    (6,4,6),  (3,6,8),  (1,10,13),  (2,7,10),   (0,28,40),  (14,1,1)),
        Make("8K Cities",              (1,2,2),  (3,4,5),    (2,5,6),   (1,6,7),    (2,5,6),  (3,7,9),  (4,12,18),  (3,10,16),  (2,30,40),  (12,1,1)),
        Make("10K Many towns",       (3,2,3),  (7,4,5),    (6,5,6),   (2,7,8),    (6,5,6),  (5,7,8),  (4,10,15),  (4,7,14),   (3,28,40),  (19,1,1)),
        Make("10K Countryside",        (3,2,3),  (10,3,5),   (7,4,6),   (2,6,8),    (8,4,6),  (5,6,8),  (2,10,13),  (2,7,10),   (0,28,40),  (18,1,1)),
        Make("10K Cities",             (2,2,3),  (4,4,5),    (3,5,6),   (2,7,8),    (3,5,6),  (4,7,9),  (5,12,18),  (4,10,16),  (3,30,40),  (15,1,1)),
        Make("12K Large",              (4,2,3),  (9,4,5),    (7,5,6),   (3,7,8),    (8,5,6),  (6,7,8),  (5,10,15),  (5,7,14),   (3,28,40),  (24,1,1)),
        Make("16K Huge",               (6,2,3),  (14,4,5),   (11,5,6),  (4,7,8),    (12,5,6), (10,7,8), (8,10,15),  (8,7,14),   (5,28,40),  (36,1,1)),
        Make("Sparse (any size)",      (1,1,2),  (2,4,4),    (1,4,5),   (0,6,7),    (1,5,6),  (1,7,8),  (1,10,15),  (1,7,14),   (0,28,40),  (4,1,1)),
    ];

    /// <summary>
    /// Adds each starter whose name isn't in <paramref name="offered"/> yet, unless a preset already has its name, and
    /// records it as offered. A starter the user deleted isn't offered again; a starter added in a later version is.
    /// </summary>
    /// <returns>Whether anything changed.</returns>
    public static bool AddNew(List<TownPreset> presets, ICollection<string> offered)
    {
        bool changed = false;
        var names = presets.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var starter in Create())
        {
            if (offered.Contains(starter.Name, StringComparer.OrdinalIgnoreCase)) continue;
            offered.Add(starter.Name);
            if (names.Add(starter.Name)) presets.Add(starter);
            changed = true;
        }
        return changed;
    }

    private static TownPreset Make(string name, params (int Count, int Min, int Max)[] rows) => new()
    {
        Name = name,
        Rows = [.. rows.Select((row, i) => new TownRow
        {
            Township = Townships[i],
            Count = row.Count,
            MinTiles = row.Min,
            MaxTiles = row.Max,
            Distance = Distance(Townships[i]),
        })],
    };
}
