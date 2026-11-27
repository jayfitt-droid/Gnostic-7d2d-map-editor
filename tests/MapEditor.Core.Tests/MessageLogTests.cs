using System.Globalization;
using MapEditor.Core.Messages;

namespace MapEditor.Core.Tests;

public sealed class MessageLogTests
{
    private DateTime _now = new(2026, 11, 27, 14, 5, 0);

    private MessageLog NewLog() => new(() => _now);

    [Fact]
    public void AddsLinesOldestFirstWithTimeAndKind()
    {
        var log = NewLog();
        log.Add("Opened the world.");
        _now = _now.AddMinutes(1);
        log.Add("Autosave failed: disk full", MessageKind.Error);

        Assert.Collection(log.Lines,
            first =>
            {
                Assert.Equal("Opened the world.", first.Text);
                Assert.Equal(MessageKind.Normal, first.Kind);
                Assert.Equal(new DateTime(2026, 11, 27, 14, 5, 0), first.Time);
            },
            second =>
            {
                Assert.Equal("Autosave failed: disk full", second.Text);
                Assert.Equal(MessageKind.Error, second.Kind);
                Assert.Equal("14:06", second.FormatTime(CultureInfo.InvariantCulture));
            });
    }

    [Fact]
    public void EmptyTextAddsNothing()
    {
        var log = NewLog();
        Assert.Null(log.Add(""));
        Assert.Null(log.Add("   ", MessageKind.Error));
        Assert.Empty(log.Lines);
    }

    [Fact]
    public void ProgressFromTheSameSourceUpdatesOneLine()
    {
        var log = NewLog();
        log.Add("Drawing the map image...", MessageKind.Progress, "map image");
        _now = _now.AddSeconds(30);
        log.Add("Drawing the map image... 40%", MessageKind.Progress, "map image");
        log.Add("Drawing the map image... 100%", MessageKind.Progress, "map image");

        var line = Assert.Single(log.Lines);
        Assert.Equal("Drawing the map image... 100%", line.Text);
        Assert.Equal(_now, line.Time);
    }

    [Fact]
    public void ProgressFromAnotherSourceAddsALine()
    {
        var log = NewLog();
        log.Add("Picture 1 of 3...", MessageKind.Progress, "pictures");
        log.Add("Drawing the map image... 10%", MessageKind.Progress, "map image");
        log.Add("Picture 2 of 3...", MessageKind.Progress, "pictures");

        Assert.Equal(["Picture 2 of 3...", "Drawing the map image... 10%"], log.Lines.Select(l => l.Text));
    }

    [Fact]
    public void RoutineLinesDoNotEndAProgressLine()
    {
        var log = NewLog();
        log.Add("Picture 1 of 3...", MessageKind.Progress, "pictures");
        log.Add("Autosaved at 14:05.", MessageKind.Routine);
        log.Add("Picture 2 of 3...", MessageKind.Progress, "pictures");

        Assert.Equal(["Picture 2 of 3...", "Autosaved at 14:05."], log.Lines.Select(l => l.Text));
    }

    [Fact]
    public void AnOrdinaryLineEndsAProgressLine()
    {
        var log = NewLog();
        log.Add("Drawing the map image... 100%", MessageKind.Progress, "map image");
        log.Add("Map image saved.");
        log.Add("Drawing the map image... 5%", MessageKind.Progress, "map image");

        Assert.Equal(["Drawing the map image... 100%", "Map image saved.", "Drawing the map image... 5%"],
            log.Lines.Select(l => l.Text));
    }

    [Fact]
    public void KeepsOnlyTheLastThousandLines()
    {
        var log = NewLog();
        for (int i = 1; i <= MessageLog.MaxLines + 5; i++) log.Add($"Message {i}");

        Assert.Equal(1000, MessageLog.MaxLines);
        Assert.Equal(MessageLog.MaxLines, log.Lines.Count);
        Assert.Equal("Message 6", log.Lines[0].Text);
        Assert.Equal("Message 1005", log.Lines[^1].Text);
    }

    [Fact]
    public void ClearEmptiesTheLog()
    {
        var log = NewLog();
        log.Add("One");
        log.Add("Two", MessageKind.Warning);
        log.Clear();
        Assert.Empty(log.Lines);
    }

    [Fact]
    public void CopyTextHasTheTimeAndMarksErrorsAndWarnings()
    {
        var log = NewLog();
        log.Add("Saved the project.");
        log.Add("Couldn't save the tour setting.", MessageKind.Warning);
        log.Add("Autosave failed: disk full", MessageKind.Error);
        log.Add("Autosaved at 14:05.", MessageKind.Routine);

        string nl = Environment.NewLine;
        Assert.Equal(
            $"14:05  Saved the project.{nl}14:05  Warning: Couldn't save the tour setting.{nl}"
            + $"14:05  Error: Autosave failed: disk full{nl}14:05  Autosaved at 14:05.{nl}",
            MessageLog.CopyText(log.Lines, CultureInfo.InvariantCulture));
        Assert.Equal($"14:05  Error: Autosave failed: disk full{nl}",
            MessageLog.CopyText([log.Lines[2]], CultureInfo.InvariantCulture));
    }
}
