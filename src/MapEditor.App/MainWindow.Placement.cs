using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MapEditor.Core.Theming;

namespace MapEditor.App;

public partial class MainWindow
{
    /// <summary>Restores the normal bounds before Windows shows or maximizes the window.</summary>
    private void RestoreWindowPlacement()
    {
        bool hasSize = _settings.WindowWidth >= MinWidth && _settings.WindowHeight >= MinHeight;
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(hasSize ? _settings.WindowWidth : Width, workArea.Width);
        Height = Math.Min(hasSize ? _settings.WindowHeight : Height, workArea.Height);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (_settings.WindowLeft is null || _settings.WindowTop is null)
        {
            if (_settings.WindowMaximized) WindowState = WindowState.Maximized;
            return;
        }

        SourceInitialized += (_, _) =>
        {
            RestoreSavedBounds();
            if (_settings.WindowMaximized) WindowState = WindowState.Maximized;
        };
    }

    private void RestoreSavedBounds()
    {
        nint window = new WindowInteropHelper(this).Handle;
        if (HwndSource.FromHwnd(window)?.CompositionTarget is not { } target)
            return;

        var transform = target.TransformToDevice;
        // The position is usable even if the saved size is below the current window minimum.
        double width = _settings.WindowWidth > 0 ? Math.Max(_settings.WindowWidth, MinWidth) : Width;
        double height = _settings.WindowHeight > 0 ? Math.Max(_settings.WindowHeight, MinHeight) : Height;
        var bounds = WindowPlacement.Restore(_settings.WindowLeft, _settings.WindowTop,
            width, height, GetMonitorWorkAreas(transform.M11));
        if (bounds is not { } restored)
            return;

        // Move the hidden HWND in desktop coordinates. WPF's Left and Top use the HWND's current DPI.
        WindowStartupLocation = WindowStartupLocation.Manual;
        if (MoveWindow(window, restored, transform.M11, transform.M22))
        {
            // WPF can apply a suggested rectangle when the move changes the HWND's DPI.
            var targetTransform = target.TransformToDevice;
            if (targetTransform != transform)
            {
                MoveWindow(window, restored, targetTransform.M11, targetTransform.M22);
            }
        }
    }

    /// <summary>Saves the normal bounds even when the window is maximized or minimized.</summary>
    private void SaveWindowPlacement()
    {
        var bounds = RestoreBounds;
        _settings.WindowMaximized = WindowState == WindowState.Maximized;
        nint window = new WindowInteropHelper(this).Handle;
        if (bounds.IsEmpty || HwndSource.FromHwnd(window)?.CompositionTarget is not { } target)
            return;

        // Undo WPF's HWND-to-DIP conversion so that monitor origins stay in one desktop coordinate system.
        var position = target.TransformToDevice.Transform(bounds.TopLeft);
        _settings.WindowLeft = Math.Round(position.X);
        _settings.WindowTop = Math.Round(position.Y);
        _settings.WindowWidth = bounds.Width;
        _settings.WindowHeight = bounds.Height;
    }

    private bool MoveWindow(nint window, WindowPlacement.Bounds bounds, double scaleX, double scaleY)
    {
        const uint noZOrder = 0x0004, noActivate = 0x0010;
        return SetWindowPos(window, 0, (int)Math.Round(bounds.Left), (int)Math.Round(bounds.Top),
            (int)Math.Round(Math.Max(bounds.Width, MinWidth * scaleX)),
            (int)Math.Round(Math.Max(bounds.Height, MinHeight * scaleY)), noZOrder | noActivate);
    }

    private static List<WindowPlacement.Monitor> GetMonitorWorkAreas(double windowScale)
    {
        var monitors = new List<WindowPlacement.Monitor>();
        EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                var area = info.WorkArea;
                // Windows reports the effective DPI for this process, including system DPI in system-aware WPF.
                double scale = GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0 ? dpi / 96.0 : windowScale;
                monitors.Add(new(new(area.Left, area.Top, area.Right - area.Left, area.Bottom - area.Top), scale));
            }
            return true;
        }, 0);
        return monitors;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public MonitorRect MonitorArea, WorkArea;
        public uint Flags;
    }

    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool MonitorCallback(nint monitor, nint context, nint bounds, nint data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint context, nint clip, MonitorCallback callback, nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo monitorInfo);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}
