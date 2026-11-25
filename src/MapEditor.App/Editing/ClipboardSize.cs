using System.Runtime.InteropServices;

namespace MapEditor.App.Editing;

/// <summary>Reads the size of the clipboard text without copying it. WPF's <c>Clipboard.GetText</c> reads all of it.</summary>
internal static class ClipboardSize
{
    private const uint UnicodeText = 13;

    /// <summary>The clipboard text length in UTF-16 characters (with the end mark), or null when it cannot be read.</summary>
    public static long? TextLength()
    {
        if (!OpenClipboard(IntPtr.Zero)) return null;
        try
        {
            nint handle = GetClipboardData(UnicodeText);
            return handle == 0 ? null : (long)GlobalSize(handle) / sizeof(char);
        }
        finally
        {
            CloseClipboard();
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(nint owner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetClipboardData(uint format);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nuint GlobalSize(nint memory);
}
