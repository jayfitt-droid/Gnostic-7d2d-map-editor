using MapEditor.Core.GameData;
using MapEditor.Core.GameLink;
using MapEditor.Core.Model;

namespace MapEditor.Core.Tests;

public sealed class GameLinkLoadedLayoutsTests : IDisposable
{
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, Guid.NewGuid().ToString("N"));
    private string Export => Path.Combine(_root, "worlds", "Test World");
    private GameLinkLoadedLayouts Store => new(Path.Combine(_root, "loaded-layouts"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static PrefabInstance Poi(int x, int z, int rotation = 0) =>
        new() { Name = "poi", Position = new(x, 20, z), Rotation = rotation };

    private static GameLinkStatus Status(string world, GameLinkState state) =>
        new(world, true, state, "1.1.0", null) { Save = "Save" };

    [Fact]
    public void KeepReadAndForgetRoundTrip()
    {
        Assert.Empty(Store.Read(Export));

        Store.Keep(Export, [Poi(12, 4, 1), Poi(40, 8)]);
        var kept = Store.Read(Export);
        Assert.Equal(2, kept.Count);
        Assert.Contains(kept, p => p.Position == new Int3(12, 20, 4) && p.Rotation == 1);

        Store.Forget(Export);
        Assert.Empty(Store.Read(Export));
        Store.Forget(Export);
    }

    [Fact]
    public void KeepingAgainAddsToTheKeptLayout()
    {
        Store.Keep(Export, [Poi(12, 4)]);
        Store.Keep(Export, [Poi(12, 4), Poi(70, 4)]);

        Assert.Equal([new Int3(12, 20, 4), new Int3(70, 20, 4)], Store.Read(Export).Select(p => p.Position));
    }

    [Fact]
    public void FoldersAreKeptApartAndPathCasingDoesNotMatter()
    {
        Store.Keep(Export, [Poi(12, 4)]);

        Assert.Single(Store.Read(Export.ToUpperInvariant() + Path.DirectorySeparatorChar));
        Assert.Empty(Store.Read(Path.Combine(_root, "worlds", "Other World")));
    }

    [Fact]
    public void ACorruptFileReadsAsNothingKept()
    {
        Store.Keep(Export, [Poi(12, 4)]);
        foreach (string file in Directory.GetFiles(Path.Combine(_root, "loaded-layouts")))
            File.WriteAllText(file, "<prefabs><decoration");

        Assert.Empty(Store.Read(Export));
        Store.Keep(Export, [Poi(70, 4)]);
        Assert.Single(Store.Read(Export));
    }

    [Fact]
    public void KeepCurrentKeepsTheFoldersPrefabsAndIgnoresAMissingFile()
    {
        Store.KeepCurrent(Export);
        Assert.Empty(Store.Read(Export));

        Directory.CreateDirectory(Export);
        IO.WorldXml.WritePrefabs(Path.Combine(Export, IO.WorldFiles.Prefabs), [Poi(12, 4)]);
        Store.KeepCurrent(Export);
        Assert.Single(Store.Read(Export));
    }

    [Fact]
    public void SummaryCountsAKeptPoiThatIsInNeitherTheFolderNorTheWorld()
    {
        var world = new World { Name = "Test World", Size = 128, Height = new Grid<ushort>(128, 128) };
        world.Prefabs.Add(Poi(31, 4));
        var library = PrefabLibrary.Of([new("poi", "", new(20, 8, 10), 0, 0, 0, [], null)]);
        var player = new GameLinkPlayer(0, 0, 0);
        Directory.CreateDirectory(Export);

        Assert.Equal(4, GameLinkRebuild.SummarizeExport(Export, world, library, player, 16).ChunkCount);
        Assert.Equal(7, GameLinkRebuild.SummarizeExport(Export, world, library, player, 16, [Poi(12, 4)]).ChunkCount);
    }

    [Fact]
    public void ForgetsAtTheMenuOrInAnotherWorldButNotForAnUnknownStatus()
    {
        Assert.False(GameLinkLoadedLayouts.ShouldForget(null, Export));
        Assert.True(GameLinkLoadedLayouts.ShouldForget(Status("Test World", GameLinkState.Menu), Export));
        Assert.True(GameLinkLoadedLayouts.ShouldForget(Status("Other World", GameLinkState.Ready), Export));
        Assert.False(GameLinkLoadedLayouts.ShouldForget(Status("test world", GameLinkState.Ready), Export));
        Assert.False(GameLinkLoadedLayouts.ShouldForget(Status("Test World", GameLinkState.Loading), Export));
    }

    [Fact]
    public void OnlyASaveOfTheSameWorldCountsAsOpen()
    {
        Assert.True(GameLinkLoadedLayouts.HasWorldOpen(Status("test world", GameLinkState.Ready), Export));
        Assert.False(GameLinkLoadedLayouts.HasWorldOpen(Status("Test World", GameLinkState.Menu), Export));
        Assert.False(GameLinkLoadedLayouts.HasWorldOpen(Status("Other World", GameLinkState.Ready), Export));
        Assert.False(GameLinkLoadedLayouts.HasWorldOpen(null, Export));
    }
}
