using System.IO.Compression;
using MapEditor.Core.IO;
using MapEditor.Core.Model;

namespace MapEditor.Core.Tests;

public sealed class ProjectHistoryFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mapeditor-history-file-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

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

        var loaded = ProjectStore.Load(path);
        Assert.Equal(500, loaded.Height[0, 0]);
        Assert.Contains(warning, loaded.Warnings);
    }
}
