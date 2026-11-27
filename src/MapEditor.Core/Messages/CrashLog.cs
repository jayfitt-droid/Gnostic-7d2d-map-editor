using System.Globalization;

namespace MapEditor.Core.Messages;

/// <summary>The text of the editor's crash log.</summary>
public static class CrashLog
{
    /// <summary>
    /// The time stamp that starts each crash log entry: the local time with its UTC offset, for example
    /// "2026-11-25 14:03:12 -05:00", so it can be matched with the game's Player.log (#230). The digits, separators and
    /// offset sign do not change with the region format.
    /// </summary>
    public static string FormatTime(DateTimeOffset time) =>
        time.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
}
