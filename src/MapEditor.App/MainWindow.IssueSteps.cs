using System.Runtime.InteropServices;
using System.Windows;
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
        if (_issueStepper.Count == 0) StopIssueHotkeys();
        if (_issueStepper.Current is { } current) SelectSteppedIssue(current);
        UpdateIssueStepControls();
    }

    private void UpdateIssueStepControls()
    {
        bool canStep = !_vm.IsBusy && !_gameLinkActionRunning;
        PreviousIssueButton.IsEnabled = canStep && _issueStepper.Previous is not null;
        NextIssueButton.IsEnabled = canStep && _issueStepper.Next is not null;
        IssueHotkeysCheck.IsEnabled = _issueStepper.Count > 0;
        IssueStepPosition.Text = $"{_issueStepper.Position} of {_issueStepper.Count}";
    }

    private void SelectSteppedIssue(TeleportSpot spot)
    {
        var issue = spot.Issues[0];
        IssuesList.SelectedItem = issue;
        IssuesList.ScrollIntoView(issue);
    }

    private async void PreviousIssue_Click(object sender, RoutedEventArgs e) => await StepIssueAsync(next: false);
    private async void NextIssue_Click(object sender, RoutedEventArgs e) => await StepIssueAsync(next: true);

    private async Task StepIssueAsync(bool next)
    {
        if (_world is not { } world || !CanUseGameLink(world) || _gameLinkActionRunning) return;
        var spot = next ? _issueStepper.Next : _issueStepper.Previous;
        if (spot?.StandingCell is not { } standing ||
            TeleportCommand.CoordinatesForCell(world, standing.X, standing.Z) is not { } target) return;

        await RunGameLinkActionAsync(world, target.X, target.Z, standing.Facing, issueStop: spot);
    }

    private bool IsPendingIssueStep(TeleportSpot spot) =>
        ReferenceEquals(spot, _issueStepper.Previous) || ReferenceEquals(spot, _issueStepper.Next);

    private void CompleteIssueStep(TeleportSpot spot)
    {
        if (_issueStepper.TryMoveTo(spot)) SelectSteppedIssue(spot);
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
        _vm.Status = "Could not enable the in-game shortcuts. You can still use Previous issue and Next issue.";
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
