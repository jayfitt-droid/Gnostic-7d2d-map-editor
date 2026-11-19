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
    private int _issueSelectionRevision;
    private bool _settingIssueSelection;

    private void ResetIssueSteps() => RebuildIssueSteps([]);

    private void RebuildIssueSteps(IReadOnlyList<TeleportSpot> spots, ValidationIssue? selected = null)
    {
        _issueStepper.Rebuild(spots);
        if (_issueStepper.Count == 0) StopIssueHotkeys();
        if (_issueStepper.Current is { } current) SelectSteppedIssue(current, selected);
        UpdateIssueStepControls();
    }

    private void UpdateIssueStepControls()
    {
        _vm.CanTeleportIssues = _vm.GameLinkInstalled && _vm.GameLinkConnected && !_vm.IsBusy && !_gameLinkActionRunning;
        IssueHotkeysCheck.IsEnabled = _vm.GameLinkInstalled && _issueStepper.Count > 0;
    }

    private void SelectSteppedIssue(TeleportSpot spot, ValidationIssue? preferred = null)
    {
        var issue = spot.Issues.FirstOrDefault(candidate => candidate == preferred) ?? spot.Issues[0];
        _settingIssueSelection = true;
        try
        {
            IssuesList.SelectedItem = issue;
            IssuesList.ScrollIntoView(issue);
        }
        finally { _settingIssueSelection = false; }
    }

    private void Issues_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settingIssueSelection || IssuesList.SelectedItem is not ValidationIssue issue) return;
        _issueSelectionRevision++;
        _issueStepper.SelectIssue(issue);
    }

    private async void TeleportIssue_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ValidationIssue issue } && _issueStepper.StopFor(issue) is { } stop)
            await TeleportToIssueAsync(stop, issue);
    }

    private async Task StepIssueAsync(bool next)
    {
        int index = IssuesList.SelectedIndex;
        if (index >= 0)
        {
            if (_issueStepper.AdjacentIssue(_vm.Issues, index, next) is { } row && _issueStepper.StopFor(row) is { } target)
                await TeleportToIssueAsync(target, row);
            return;
        }

        if ((next ? _issueStepper.Next : _issueStepper.Previous) is { } spot)
            await TeleportToIssueAsync(spot);
    }

    private async Task TeleportToIssueAsync(TeleportSpot spot, ValidationIssue? preferred = null)
    {
        if (!_vm.CanTeleportIssues || _world is not { } world || !CanUseGameLink(world) ||
            spot.StandingCell is not { } standing ||
            TeleportCommand.CoordinatesForCell(world, standing.X, standing.Z) is not { } target) return;

        await RunGameLinkActionAsync(world, target.X, target.Z, standing.Facing, issueStop: spot, issueRow: preferred);
    }

    private bool IsPendingIssueStep(TeleportSpot spot) =>
        ReferenceEquals(spot, _issueStepper.StopFor(spot.Issues[0]));

    private void CompleteIssueStep(TeleportSpot spot, ValidationIssue? issue, int selectionRevision)
    {
        if (selectionRevision != _issueSelectionRevision)
        {
            _issueStepper.RecordVisit(spot);
            return;
        }
        _issueStepper.TryMoveTo(spot);
        if (_issueStepper.Current is { } current) SelectSteppedIssue(current, issue);
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
            if (_issueHotkeySource is { IsDisposed: false } source && IsWindowEnabled(source.Handle))
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
