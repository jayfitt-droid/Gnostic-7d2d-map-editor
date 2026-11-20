using System.Globalization;
using System.Windows.Data;
using MapEditor.Core.Edit;

namespace MapEditor.App;

/// <summary>Looks up the row action again as the undo stack changes or a list row is recycled.</summary>
public sealed class HistoryActionLabelConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values is [HistoryEntry entry, ProjectHistory history, int]
            ? Label(history.ActionFor(entry)) : null;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();

    private static string? Label(HistoryAction action) => action switch
    {
        HistoryAction.Undo => "Undo to here",
        HistoryAction.Redo => "Redo to here",
        _ => null,
    };
}
