using System.Globalization;
using System.Windows.Data;

namespace MapEditor.App;

/// <summary>
/// Opens a Generate stage card when <see cref="GenerateViewModel.OpenStage"/> is its number (the parameter).
/// Opening a card makes it the open stage; folding it leaves none open.
/// </summary>
public sealed class StageOpenConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int open && open == Stage(parameter);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Stage(parameter) : 0;

    private static int Stage(object parameter) => int.Parse((string)parameter, CultureInfo.InvariantCulture);
}
