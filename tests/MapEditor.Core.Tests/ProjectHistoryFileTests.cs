using System.IO.Compression;
using MapEditor.Core.Edit;
using MapEditor.Core.IO;
using MapEditor.Core.Model;

namespace MapEditor.Core.Tests;

public sealed class ProjectHistoryFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mapeditor-history-file-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Save_keeps_the_newest_history_entries_that_fit()
    {
        const long historyLimit = 190;
        string path = Path.Combine(_dir, "World.7dmap");
        var world = new World { Name = "World", Size = 1, Height = new Grid<ushort>(1, 1) };
        var older = HistoryEntry.Event("Oldest");
        var previous = HistoryEntry.Event("Previous");
        var newest = HistoryEntry.Event("Newest é");
        var settings = new ProjectStore.ProjectSettings { History = [older, previous, newest] };

        ProjectStore.Save(world, path, settings, snapshot: null, maxHistoryBytes: historyLimit);

        var loaded = ProjectStore.Load(path, out var loadedSettings);
        Assert.Empty(loaded.Warnings);
        Assert.Equal([previous, newest], loadedSettings.History);
        using var zip = ZipFile.OpenRead(path);
        Assert.InRange(zip.GetEntry("history.jsonl")!.Length, 1, historyLimit);
    }

    [Theory]
    [InlineData(false, "Some entries in the project history could not be read.")]
    [InlineData(true, "The project history is too large to read.")]
    public void Bad_history_warns_and_leaves_the_world_readable(bool oversized, string warning)
    {
        string path = Path.Combine(_dir, "World.7dmap");
        var world = new World { Name = "World", Size = 1, Height = new Grid<ushort>(1, 1) };
        world.Height[0, 0] = 500;
        ProjectStore.Save(world, path);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        using (var stream = zip.CreateEntry("history.jsonl", CompressionLevel.Fastest).Open())
        {
            if (oversized)
            {
                var block = new byte[1 << 20];
                for (int i = 0; i < 65; i++) stream.Write(block);
            }
            else stream.Write("{truncated"u8);
        }

        var loaded = ProjectStore.Load(path, out _, out var missingWorldFilesWarning);
        Assert.Null(missingWorldFilesWarning);
        Assert.Equal(500, loaded.Height[0, 0]);
        Assert.Contains(warning, loaded.Warnings);
    }
}
