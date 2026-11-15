using System.Globalization;
using System.Windows.Data;
using MapEditor.Core.GameData;

namespace MapEditor.App;

/// <summary>What kind of name a <see cref="FriendlyNameConverter"/> looks up.</summary>
public enum NameKind { Biome, Township, Poi }

/// <summary>
/// Turns a game name (a string, or a biome, township type or prefab) into the friendly name for display.
/// Bindings keep the game's own names; only the text shown changes.
/// </summary>
public sealed class FriendlyNameConverter(NameKind kind) : IValueConverter
{
    public static readonly FriendlyNameConverter Biome = new(NameKind.Biome);
    public static readonly FriendlyNameConverter Township = new(NameKind.Township);
    public static readonly FriendlyNameConverter Poi = new(NameKind.Poi);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string name = value switch
        {
            string s => s,
            BiomeDefinition b => b.Name,
            TownshipType t => t.Name,
            PrefabInfo p => p.Name,
            _ => "",
        };
        return kind switch
        {
            NameKind.Biome => GameNames.Current.Biome(name),
            NameKind.Township => GameNames.Township(name),
            NameKind.Poi => GameNames.Current.Poi(name),
            _ => name,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
