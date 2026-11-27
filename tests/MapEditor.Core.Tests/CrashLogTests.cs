using System.Globalization;
using MapEditor.Core.Messages;

namespace MapEditor.Core.Tests;

public sealed class CrashLogTests
{
    [Theory]
    [InlineData(-5, 0, "2026-11-25 14:03:12 -05:00")]
    [InlineData(0, 0, "2026-11-25 14:03:12 +00:00")]
    [InlineData(5, 30, "2026-11-25 14:03:12 +05:30")]
    public void TimeIsLocalTimeWithItsOffset(int hours, int minutes, string expected)
    {
        var offset = new TimeSpan(hours, hours < 0 ? -minutes : minutes, 0);
        var time = new DateTimeOffset(2026, 11, 25, 14, 3, 12, offset);

        Assert.Equal(expected, CrashLog.FormatTime(time));
    }

    // Swedish writes a Unicode minus sign for negative numbers (#269); Thai counts years in the Buddhist calendar.
    [Theory]
    [InlineData("sv-SE")]
    [InlineData("th-TH")]
    public void TimeDoesNotChangeWithTheRegionFormat(string culture)
    {
        var time = new DateTimeOffset(2026, 11, 25, 14, 3, 12, TimeSpan.FromHours(-5));
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);

            string text = CrashLog.FormatTime(time);

            Assert.Equal("2026-11-25 14:03:12 -05:00", text);
            Assert.DoesNotContain("\u2212", text);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
