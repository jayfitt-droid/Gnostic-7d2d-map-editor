using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using MapEditor.Core.Model;
using MapEditor.Core.Validate;

namespace MapEditor.App;

public partial class MainWindow
{
    private const int HotkeyMessage = 0x0312;
    private const int PreviousIssueHotkey = 1, NextIssueHotkey = 2;
    private const uint IssueHotkeyModifiers = 0x4000 | 0x0002 | 0x0001; // MOD_NOREPEAT | MOD_CONTROL | MOD_ALT

    private readonly IssueStepper _issueStepper = new();
    private HwndSource? _issueHotkeySource;

    private void ResetIssueSteps() => RebuildIssueSteps([]);

    private void RebuildIssueSteps(IReadOnlyList<TeleportSpot> spots)
    {
        _issueStepper.Rebuild(spots);
        if (!_issueStepper.HasStops) StopIssueHotkeys();
        UpdateIssueStepControls();
    }

    private void UpdateIssueStepControls()
    {
        _vm.CanTeleportIssues = _vm.GameLinkInstalled && _vm.GameLinkConnected && !_vm.IsBusy && !_gameLinkActionRunning;
        IssueHotkeysCheck.IsEnabled = _vm.GameLinkInstalled && _issueStepper.HasStops;
    }

    private void SelectIssueRow(ValidationIssue issue)
    {
        if (_vm.Issues.FirstOrDefault(row => row == issue) is not { } current) return;
        IssuesList.SelectedItem = current;
        IssuesList.ScrollIntoView(current);
    }

    private async void TeleportIssue_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ValidationIssue issue })
            await TeleportToIssueAsync(issue);
    }

    private Task StepIssueAsync(bool next) =>
        _issueStepper.AdjacentIssue(_vm.Issues, IssuesList.SelectedIndex, next) is { } row
            ? TeleportToIssueAsync(row) : Task.CompletedTask;

    private async Task TeleportToIssueAsync(ValidationIssue issue)
    {
        if (!_vm.CanTeleportIssues || _world is not { } world || !CanUseGameLink(world) ||
            _issueStepper.StopFor(issue)?.StandingCell is not { } standing ||
            TeleportCommand.CoordinatesForCell(world, standing.X, standing.Z) is not { } target) return;

        var selected = IssuesList.SelectedItem as ValidationIssue;
        bool selectionChanged = false;
        void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Validate can replace the selected row with an equal result.
            if (e.AddedItems.Count > 0 && !Equals(e.AddedItems[0], selected)) selectionChanged = true;
        }
        IssuesList.SelectionChanged += OnSelectionChanged;
        try
        {
            await RunGameLinkActionAsync(world, target.X, target.Z, standing.Facing, issue: issue, onTeleported: () =>
            {
                if (!selectionChanged) SelectIssueRow(issue);
            });
        }
        finally { IssuesList.SelectionChanged -= OnSelectionChanged; }
    }

    private void IssueHotkeys_Click(object sender, RoutedEventArgs e)
    {
        if (IssueHotkeysCheck.IsChecked != true)
        {
            StopIssueHotkeys();
            return;
        }

        if (PresentationSource.FromVisual(this) is HwndSource source &&
            RegisterHotKey(source.Handle, PreviousIssueHotkey, IssueHotkeyModifiers, (uint)KeyInterop.VirtualKeyFromKey(Key.Left)))
        {
            if (RegisterHotKey(source.Handle, NextIssueHotkey, IssueHotkeyModifiers, (uint)KeyInterop.VirtualKeyFromKey(Key.Right)))
            {
                source.AddHook(IssueHotkeyHook);
                _issueHotkeySource = source;
                return;
            }
            UnregisterHotKey(source.Handle, PreviousIssueHotkey);
        }

        IssueHotkeysCheck.IsChecked = false;
        _vm.Status = "Could not enable the in-game shortcuts. You can still use the Teleport buttons.";
    }

    private void StopIssueHotkeys()
    {
        if (_issueHotkeySource is { IsDisposed: false } source)
        {
            source.RemoveHook(IssueHotkeyHook);
            UnregisterHotKey(source.Handle, PreviousIssueHotkey);
            UnregisterHotKey(source.Handle, NextIssueHotkey);
        }
        _issueHotkeySource = null;
        IssueHotkeysCheck.IsChecked = false;
    }

    private IntPtr IssueHotkeyHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != HotkeyMessage) return IntPtr.Zero;
        int id = wParam.ToInt32();
        if (id is not (PreviousIssueHotkey or NextIssueHotkey)) return IntPtr.Zero;
        handled = true;
        // Run outside the native hook, since a step can show a modal dialog.
        _ = Dispatcher.InvokeAsync(new Action(async () =>
        {
            if (_issueHotkeySource is { IsDisposed: false } source && IsWindowEnabled(source.Handle) &&
                GameWindow.IsGameOrEditorInForeground())
                await StepIssueAsync(next: id == NextIssueHotkey);
        }));
        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr window);
}
