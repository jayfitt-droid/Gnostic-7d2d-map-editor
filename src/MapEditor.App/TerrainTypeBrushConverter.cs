using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using MapEditor.App.Map;
using MapEditor.Core.Generate;

namespace MapEditor.App;

/// <summary>The map overlay's colour for a terrain type, for the Generate panel's swatches.</summary>
public sealed class TerrainTypeBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var colour = value is TerrainType t ? MapView.TerrainTypeColours[(int)t] : Colors.Transparent;
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
