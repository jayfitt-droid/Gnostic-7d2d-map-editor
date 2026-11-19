using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MapEditor.App;

/// <summary>Activates the local 7 Days to Die window after an editor request.</summary>
internal static class GameWindow
{
    private const int Restore = 9;

    public static bool TryActivate()
    {
        Process[] games = Process.GetProcessesByName("7DaysToDie");
        try
        {
            foreach (Process game in games)
            {
                IntPtr window;
                try
                {
                    window = game.MainWindowHandle;
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    continue;
                }
                if (window == IntPtr.Zero) continue;
                if (GetForegroundWindow() == window) return true;
                if (IsIconic(window)) ShowWindowAsync(window, Restore);
                if (SetForegroundWindow(window)) return true;
            }
            return false;
        }
        finally
        {
            foreach (Process game in games) game.Dispose();
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
