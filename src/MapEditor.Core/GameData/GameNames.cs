using System.Text;

namespace MapEditor.Core.GameData;

/// <summary>
/// Friendly names for the editor's lists: "Pine Forest" for pine_forest. Biome and prefab names come from the
/// game's Data/Config/Localization.csv (English column), read from the install at runtime. Display text only;
/// saved projects, world files and the CLI keep the game's own names.
/// </summary>
public sealed class GameNames
{
    /// <summary>No names loaded: everything is tidied.</summary>
    public static readonly GameNames Empty = new(new Dictionary<string, string>(), new Dictionary<string, string>());

    /// <summary>The names the editor shows. Set when the game data loads.</summary>
    public static GameNames Current { get; set; } = Empty;

    /// <summary>The editor's own names for the township types (the game has none).</summary>
    public static IReadOnlyDictionary<string, string> TownshipNames { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["oldwest"] = "Old West",
        ["forest_countrytown"] = "Forest village",
        ["bforest_countrytown"] = "Burnt forest village",
        ["bforest_town"] = "Burnt forest town",
        ["countrytown"] = "Village",
        ["town"] = "Town",
        ["city"] = "City",
        ["wasteland_city"] = "Wasteland city",
        ["citybig"] = "Big city",
        ["roadside"] = "Roadside stop",
    };

    private readonly IReadOnlyDictionary<string, string> _biomes;
    private readonly IReadOnlyDictionary<string, string> _pois;

    private GameNames(IReadOnlyDictionary<string, string> biomes, IReadOnlyDictionary<string, string> pois)
    {
        _biomes = biomes;
        _pois = pois;
    }

    /// <summary>Reads Localization.csv from the install; a missing or unreadable file gives <see cref="Empty"/>.</summary>
    public static GameNames Load(GameInstall install)
    {
        try
        {
            string path = Path.Combine(install.ConfigDir, "Localization.csv");
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty;
        }
        catch (IOException) { return Empty; }
        catch (UnauthorizedAccessException) { return Empty; }
    }

    /// <summary>
    /// Reads Localization.csv text. The columns are found by header name: Key, Type and english. Rows of type
    /// "POIName" and keys starting biome_ are kept.
    /// </summary>
    public static GameNames Parse(string csv)
    {
        var biomes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pois = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rows = ParseCsv(csv);
        if (rows.Count == 0) return Empty;
        var header = rows[0];
        int key = header.FindIndex(h => h.Trim().Equals("Key", StringComparison.OrdinalIgnoreCase));
        int type = header.FindIndex(h => h.Trim().Equals("Type", StringComparison.OrdinalIgnoreCase));
        int english = header.FindIndex(h => h.Trim().Equals("english", StringComparison.OrdinalIgnoreCase));
        if (key < 0 || english < 0) return Empty;
        for (int i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Count <= Math.Max(key, english)) continue;
            string k = row[key].Trim(), text = row[english].Trim();
            if (k.Length == 0 || text.Length == 0) continue;
            string kind = type >= 0 && type < row.Count ? row[type].Trim() : "";
            if (kind.Equals("POIName", StringComparison.OrdinalIgnoreCase)) pois[k] = text;
            else if (k.StartsWith("biome_", StringComparison.OrdinalIgnoreCase)) biomes[k["biome_".Length..]] = text;
        }
        return new GameNames(biomes, pois);
    }

    /// <summary>Splits CSV text into rows of fields. Quoted fields may hold commas, line breaks and doubled quotes.</summary>
    public static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false, any = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else quoted = false;
                }
                else field.Append(c);
                continue;
            }
            switch (c)
            {
                case '"': quoted = true; any = true; break;
                case ',': row.Add(field.ToString()); field.Clear(); any = true; break;
                case '\r': break;
                case '\n':
                    if (any || field.Length > 0) { row.Add(field.ToString()); rows.Add(row); }
                    row = [];
                    field.Clear();
                    any = false;
                    break;
                default: field.Append(c); any = true; break;
            }
        }
        if (any || field.Length > 0) { row.Add(field.ToString()); rows.Add(row); }
        return rows;
    }

    /// <summary>Underscores to spaces, first letter capital: the name used when the game has none.</summary>
    public static string Tidy(string name)
    {
        string s = name.Replace('_', ' ').Trim();
        return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    }

    /// <summary>A biome's friendly name, e.g. "Pine Forest" for pine_forest. RWG's own tags (forest, burntforest) work too.</summary>
    public string Biome(string name)
    {
        if (_biomes.TryGetValue(name, out var n)) return n;
        string alias = name.ToLowerInvariant() switch { "forest" => "pine_forest", "burntforest" => "burnt_forest", _ => "" };
        return alias.Length > 0 && _biomes.TryGetValue(alias, out n) ? n : Tidy(name);
    }

    /// <summary>A township type's friendly name: the editor's own list, else tidied.</summary>
    public static string Township(string name) => TownshipNames.TryGetValue(name, out var n) ? n : Tidy(name);

    /// <summary>The game's name for a prefab (a POIName row), or null when it has none.</summary>
    public string? PoiGameName(string prefab) => _pois.GetValueOrDefault(prefab);

    /// <summary>A prefab's friendly name: the game's, else tidied.</summary>
    public string Poi(string prefab) => PoiGameName(prefab) ?? Tidy(prefab);

    /// <summary>True if the word is in the prefab's file name or its friendly name.</summary>
    public bool PoiMatches(string prefab, string word) =>
        prefab.Contains(word, StringComparison.OrdinalIgnoreCase) || Poi(prefab).Contains(word, StringComparison.OrdinalIgnoreCase);
}
