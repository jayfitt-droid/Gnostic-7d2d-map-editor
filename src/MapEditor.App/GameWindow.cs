using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MapEditor.App;

/// <summary>Checks and activates the local 7 Days to Die window.</summary>
internal static class GameWindow
{
    private const string GameProcessName = "7DaysToDie";
    private const int Restore = 9;

    public static bool IsRunning()
    {
        var games = Process.GetProcessesByName(GameProcessName);
        foreach (var game in games) game.Dispose();
        return games.Length != 0;
    }

    /// <summary>Checks whether the foreground window belongs to the game or this editor process.</summary>
    public static bool IsGameOrEditorInForeground()
    {
        IntPtr window = GetForegroundWindow();
        if (window == IntPtr.Zero || GetWindowThreadProcessId(window, out uint processId) == 0) return false;
        if (processId == Environment.ProcessId) return true;

        Process[] games = Process.GetProcessesByName(GameProcessName);
        try
        {
            return Array.Exists(games, game => game.Id == processId);
        }
        finally
        {
            foreach (Process game in games) game.Dispose();
        }
    }

    public static bool TryActivate()
    {
        Process[] games = Process.GetProcessesByName(GameProcessName);
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
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

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
