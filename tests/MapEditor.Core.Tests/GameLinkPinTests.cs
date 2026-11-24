using MapEditor.Core.GameLink;

namespace MapEditor.Core.Tests;

public sealed class GameLinkPinTests
{
    private static GameLinkPin Pin(string world, string note = "") =>
        new(Guid.NewGuid(), -16.5f, 70f, 81.25f, 135f, world, "Test Save",
            new DateTimeOffset(2026, 11, 23, 12, 34, 56, TimeSpan.Zero), note);

    [Fact]
    public void FileRoundTripsPinsAndSkipsDamagedLines()
    {
        string note = GameLinkPinFile.CleanNote("Check \"caf\u00e9\"\r\n near the \u6e56");
        Assert.Equal("Check \"caf\u00e9\" near the \u6e56", note);
        var first = Pin("\u00cele du Lac", note);
        var second = Pin("Another World");
        string line = GameLinkPinFile.WriteLine(first);
        string damagedBase64 = line[..(line.LastIndexOf('\t') + 1)] + "!";
        using var input = new StringReader($"{damagedBase64}\n{line}\ntruncated\n{line}\n{GameLinkPinFile.WriteLine(second)}\n");
        var read = GameLinkPinFile.Read(input).ToArray();

        Assert.Equal(2, read.Length);
        Assert.Equal((first.Id, first.World, first.Save, first.Time, first.Note),
            (read[0].Id, read[0].World, read[0].Save, read[0].Time, read[0].Note));
        Assert.Equal((first.X, first.Y, first.Z, first.Yaw), (read[0].X, read[0].Y, read[0].Z, read[0].Yaw));
        Assert.Equal(second.Id, read[1].Id);
        Assert.Empty(read[1].Note);
    }

    [Fact]
    public void FileReadsAnEmptyQueue()
    {
        using var input = new StringReader(string.Empty);
        Assert.Empty(GameLinkPinFile.Read(input));
    }

    [Fact]
    public void NotesAreBoundedWithoutSplittingASurrogatePair()
    {
        string prefix = new('a', GameLinkPinFile.MaxNoteLength - 1);
        Assert.Equal(prefix, GameLinkPinFile.CleanNote(prefix + "\U0001F3AE"));
        Assert.Equal(GameLinkPinFile.MaxNoteLength, GameLinkPinFile.CleanNote(prefix + "bc").Length);
    }

    [Fact]
    public void MergeKeepsOnlyNewPinsForTheProjectsExportWorld()
    {
        var known = Pin("Test World");
        var matching = Pin("TEST WORLD");
        var other = Pin("Another World");
        List<GameLinkPin> pins = [known];

        Assert.Equal(0, GameLinkPins.Merge(pins, [matching], null));
        Assert.Equal(1, GameLinkPins.Merge(pins, [other, known, matching, matching], Path.Combine("exports", "Test World")));
        Assert.Equal([known, matching], pins);
        Assert.Equal(GameLinkPins.WorldKey("Test World"), GameLinkPins.WorldKey("TEST WORLD"));
        Assert.NotEqual(GameLinkPins.WorldKey(matching.World), GameLinkPins.WorldKey(other.World));
    }

    [Fact]
    public void AckRemovesOnlyThePrefixForTheAcknowledgedWorld()
    {
        var other = Pin("Another World");
        var first = Pin("Test World");
        var last = Pin("test world");
        var after = Pin("Test World");
        List<GameLinkPin> queue = [other, first, last, after];

        Assert.True(GameLinkPinFile.Acknowledge(queue, last.Id));
        Assert.False(GameLinkPinFile.Acknowledge(queue, last.Id));
        Assert.Equal([other, after], queue);
    }

    [Fact]
    public void PollingCollectsPinsAddedSinceTheLastPollBeforeAcknowledging()
    {
        var first = Pin("Test World");
        var other = Pin("Another World");
        var second = Pin("test world");
        string key = GameLinkPins.WorldKey(first.World);
        List<GameLinkPin> queue = [first];
        List<GameLinkPin> collected = [];
        string folder = Path.Combine("exports", first.World);

        var batch = GameLinkPinFile.ReadBatch(queue, key, Guid.Empty);
        Assert.Equal(1, GameLinkPins.Merge(collected, batch, folder));
        queue.AddRange([other, second]);

        var next = GameLinkPinFile.ReadBatch(queue, key, batch[^1].Id);
        Assert.Equal([second], next);
        Assert.Equal(1, GameLinkPins.Merge(collected, next, folder));
        Assert.Equal([first, second], collected);
        Assert.Equal([first, other, second], queue);
        Assert.Equal(next, GameLinkPinFile.ReadBatch(queue, key, first.Id));
        Assert.Empty(GameLinkPinFile.ReadBatch(queue, key, second.Id));

        Assert.True(GameLinkPinFile.Acknowledge(queue, second.Id));
        Assert.Equal([other], queue);
    }

    [Fact]
    public void CursorPagesAcrossBoundedBatchesAndCanRecoverIfAlreadyAcknowledged()
    {
        const string world = "Test World";
        string key = GameLinkPins.WorldKey(world);
        var queue = Enumerable.Range(0, GameLinkPinFile.MaxBatchSize + 1).Select(_ => Pin(world)).ToList();
        var first = GameLinkPinFile.ReadBatch(queue, key, Guid.Empty);
        var last = queue[^1];
        Assert.Equal(queue.Take(GameLinkPinFile.MaxBatchSize), first);
        Assert.Equal([last], GameLinkPinFile.ReadBatch(queue, key, first[^1].Id));

        Assert.True(GameLinkPinFile.Acknowledge(queue, first[^1].Id));
        Assert.Equal([last], GameLinkPinFile.ReadBatch(queue, key, first[^1].Id));
    }
}
