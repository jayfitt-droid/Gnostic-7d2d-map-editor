using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using MapEditor.Core.Messages;

namespace MapEditor.App;

/// <summary>The Messages panel (#225): every status message of this session, under the map.</summary>
public partial class MainWindow
{
    public static readonly RoutedUICommand MessagesCommand = new("Messages", nameof(MessagesCommand), typeof(MainWindow));

    /// <summary>Whether the list keeps the newest line in view. It stops when the user scrolls up to read an older line.</summary>
    private bool _messagesFollow = true;

    private void Messages_Executed(object sender, ExecutedRoutedEventArgs e) => _vm.ShowMessages = !_vm.ShowMessages;

    private void CloseMessages_Click(object sender, RoutedEventArgs e)
    {
        _vm.ShowMessages = false;
        Map.Focus();
    }

    private void ClearMessages_Click(object sender, RoutedEventArgs e)
    {
        _vm.Messages.Clear();
        _messagesFollow = true;
    }

    private void CopyMessages_Click(object sender, RoutedEventArgs e) => CopyMessages();

    /// <summary>Copies the selected lines, or all lines when none is selected, as text.</summary>
    private void CopyMessages()
    {
        var lines = MessagesList.SelectedItems.Count > 0
            ? _vm.Messages.Lines.Where(MessagesList.SelectedItems.Contains).ToList()
            : _vm.Messages.Lines.ToList();
        if (lines.Count == 0) return;
        CopyToClipboard(MessageLog.CopyText(lines), lines.Count == 1 ? "Copied 1 message." : $"Copied {lines.Count} messages.");
    }

    private void MessagesList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            CopyMessages();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Keeps the newest line in view while the list is at its end. A scroll by the user (the extent stays the same)
    /// decides whether the list still follows; a new line (the extent grows) scrolls to it only if it does.
    /// </summary>
    private void MessagesList_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource is not ScrollViewer scroll) return;
        if (e.ExtentHeightChange == 0) _messagesFollow = scroll.VerticalOffset >= scroll.ScrollableHeight;
        else if (_messagesFollow) scroll.ScrollToEnd();
    }

    /// <summary>Dragging the panel's top edge: up makes it taller, but it leaves room for the map.</summary>
    private void MessagesEdge_DragDelta(object sender, DragDeltaEventArgs e)
    {
        double max = Math.Min(MainViewModel.MaxMessagesHeight, Math.Max(MainViewModel.MinMessagesHeight, ActualHeight - 400));
        _vm.MessagesHeight = Math.Clamp(_vm.MessagesHeight - e.VerticalChange, MainViewModel.MinMessagesHeight, max);
    }
}
