using MapEditor.Core.GameData;
using MapEditor.Core.GameLink;
using MapEditor.Core.IO;
using MapEditor.Core.Model;
using Footprint = MapEditor.Core.GameLink.GameLinkRebuildArea.Footprint;

namespace MapEditor.Core.Tests;

public class GameLinkRebuildTests
{
    private const string Id = "0123456789abcdef";
    private const string World = "Test World";
    private const string Save = "Test Save";
    private static readonly Footprint Bounds = new(-1024, -1024, 1024, 1024);
    private static readonly GameLinkStatus Ready = new(World, true, GameLinkState.Ready, "1.1.0", new(-0.25f, 21, 0))
    {
        Save = Save,
    };

    [Theory]
    [InlineData(16, 9)]
    [InlineData(128, 289)]
    [InlineData(512, 4225)]
    public void RoundsTheAreaOutToWholeChunks(int radius, int count)
    {
        var keys = GameLinkRebuildArea.Chunks(0.5f, 0.5f, radius, Bounds, []);
        Assert.Equal(count, keys.Count);
        Assert.Contains((-radius / 16, -radius / 16), keys);
        Assert.Contains((radius / 16, radius / 16), keys);
    }

    [Fact]
    public void AlignedEdgeDoesNotIncludeTheNextChunk()
    {
        var keys = GameLinkRebuildArea.Chunks(-16, -16, 16, Bounds, []);
        Assert.True(keys.SetEquals([(-2, -2), (-2, -1), (-1, -2), (-1, -1)]));
    }

    [Fact]
    public void ClipsToWorldEdgesAndRejectsAnOutsideCentre()
    {
        var keys = GameLinkRebuildArea.Chunks(-1023.75f, 1023.75f, 16, Bounds, [new(-1060, 1008, -1008, 1040)]);
        Assert.True(keys.SetEquals([(-64, 62), (-64, 63), (-63, 62), (-63, 63)]));
        Assert.Empty(GameLinkRebuildArea.Chunks(1024, 0, 16, Bounds, []));
    }

    [Fact]
    public void IncludesFullFootprintsAndAnyPoisSharingTheirChunks()
    {
        var keys = GameLinkRebuildArea.Chunks(0, 0, 16, Bounds,
        [
            new(12, 4, 32, 16),    // Crosses the initial edge.
            new(-10, -10, -1, -1), // Already inside.
            new(31, 4, 44, 15),    // Shares a chunk with the first POI.
            new(48, 4, 64, 16),    // Outside, with no shared chunks.
        ]);
        Assert.True(keys.SetEquals([(-1, -1), (-1, 0), (0, -1), (0, 0), (1, 0), (2, 0)]));
    }

    [Fact]
    public void SummaryMeasuresTheRoundedSquareAlongItsAxes()
    {
        var summary = GameLinkRebuildArea.Summarize(0.5f, 0.5f, 128, Bounds, []);

        Assert.Equal(289, summary.ChunkCount);
        Assert.Equal(289, summary.SquareChunkCount);
        Assert.Equal(144, summary.ReachMetres);
        Assert.False(summary.IsMuchLarger);
    }

    [Fact]
    public void SummaryIncludesOnePoiCrossingTheEdge()
    {
        var summary = GameLinkRebuildArea.Summarize(0, 0, 16, Bounds, [new(12, 4, 32, 16)]);

        Assert.Equal(5, summary.ChunkCount);
        Assert.Equal(4, summary.SquareChunkCount);
        Assert.Equal(32, summary.ReachMetres);
        Assert.False(summary.IsMuchLarger);
    }

    [Theory]
    [InlineData(79, 8, 80, false)]
    [InlineData(81, 9, 96, true)]
    public void SummaryFlagsAChainOnlyWhenItMoreThanDoublesTheSquare(int end, int count, int reach, bool muchLarger)
    {
        var summary = GameLinkRebuildArea.Summarize(0, 0, 16, Bounds,
            [new(12, 4, 32, 16), new(31, 4, 64, 16), new(63, 4, end, 16)]);

        Assert.Equal(count, summary.ChunkCount);
        Assert.Equal(4, summary.SquareChunkCount);
        Assert.Equal(reach, summary.ReachMetres);
        Assert.Equal(muchLarger, summary.IsMuchLarger);
    }

    [Fact]
    public void SummaryClipsTheSquareAndPoiToTheWorld()
    {
        var summary = GameLinkRebuildArea.Summarize(-1023.75f, 1023.75f, 16, Bounds,
            [new(-1060, 1008, -1008, 1040)]);

        Assert.Equal(4, summary.ChunkCount);
        Assert.Equal(4, summary.SquareChunkCount);
        Assert.Equal(32, summary.ReachMetres);
        Assert.False(summary.IsMuchLarger);
    }

    [Fact]
    public void ExportSummaryJoinsTheOnDiskAndEditedPrefabLists()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, WorldFiles.Prefabs);
        try
        {
            var world = new World { Name = World, Size = 128, Height = new Grid<ushort>(128, 128) };
            world.Prefabs.Add(new() { Name = "poi", Position = new(31, 20, 4) });
            var library = PrefabLibrary.Of([new("poi", "", new(20, 8, 10), 0, 0, 0, [], null)]);
            var player = new GameLinkPlayer(0, 0, 0);

            var firstExport = GameLinkRebuild.SummarizeExport(folder, world, library, player, 16);
            Assert.Equal(4, firstExport.ChunkCount);

            WorldXml.WritePrefabs(file, [new() { Name = "poi", Position = new(12, 20, 4) }]);
            var reexport = GameLinkRebuild.SummarizeExport(folder, world, library, player, 16);
            Assert.Equal(7, reexport.ChunkCount);
            Assert.Equal(4, reexport.SquareChunkCount);
            Assert.Equal(64, reexport.ReachMetres);
        }
        finally
        {
            File.Delete(file);
            Directory.Delete(folder);
        }
    }

    [Theory]
    [InlineData(0, -7, 49)]
    [InlineData(1, 2, 40)]
    [InlineData(2, -7, 49)]
    [InlineData(3, 2, 40)]
    public void PrefabFootprintsUseTheCornerAndRotatedSize(int rotation, int maxX, int maxZ)
    {
        var prefab = new PrefabInstance { Name = "poi", Position = new(-20, 6, 27), Rotation = rotation };
        var info = new PrefabInfo("poi", "", new(13, 4, 22), 0, 0, 0, [], null);
        var footprint = GameLinkRebuild.PrefabFootprint(prefab, info);

        Assert.Equal((-20, 27, maxX, maxZ), (footprint.MinX, footprint.MinZ, footprint.MaxX, footprint.MaxZ));
    }

    [Fact]
    public void MissingPrefabUsesOneBlockAtItsCorner()
    {
        var prefab = new PrefabInstance { Name = "unknown", Position = new(-1, 6, 31), Rotation = 3 };
        var footprint = GameLinkRebuild.PrefabFootprint(prefab, PrefabLibrary.Empty.Find(prefab.Name));

        Assert.Equal((-1, 31, 0, 32), (footprint.MinX, footprint.MinZ, footprint.MaxX, footprint.MaxZ));
    }

    [Theory]
    [InlineData(int.MinValue, 16)]
    [InlineData(16, 16)]
    [InlineData(128, 128)]
    [InlineData(512, 512)]
    [InlineData(int.MaxValue, 512)]
    public void RadiusStaysWithinLimits(int radius, int expected) =>
        Assert.Equal(expected, GameLinkRebuildArea.ClampRadius(radius));

    [Theory]
    [InlineData(119, null)]
    [InlineData(120, "expired")]
    public void TheRequestKeepsTheExactSaveAndPositionAndIsUsedOnce(int age, string? error)
    {
        string folder = AppContext.BaseDirectory;
        string world = new string('\u754C', 140) + " Test World";
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        string id = new GameLinkRebuildRequest(world, Save, -8.875f, 16.25f, 128).Write(folder, now);
        string path = GameLinkRebuildRequest.RequestPath(folder, id);
        try
        {
            Assert.Equal(error, GameLinkRebuildRequest.Take(folder, id, now.AddSeconds(age), out var request));
            if (error is null)
            {
                Assert.NotNull(request);
                Assert.Equal((world, Save, -8.875f, 16.25f, 128), (request.World, request.Save, request.X, request.Z, request.Radius));
            }
            else
                Assert.Null(request);
            Assert.False(File.Exists(path));
            Assert.Equal("bad_request", GameLinkRebuildRequest.Take(folder, id, now, out _));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("NaN 1 128")]
    [InlineData("1 Infinity 128")]
    [InlineData("1 1 15")]
    [InlineData("1 1 513")]
    [InlineData("1 1 128 extra")]
    public void InvalidRequestFileIsRejectedAndConsumed(string area)
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        string id = GameLinkProtocol.CreateOperationId();
        string directory = AppContext.BaseDirectory;
        string path = GameLinkRebuildRequest.RequestPath(directory, id);
        File.WriteAllLines(path, ["1800000120", World, Save, area]);
        try
        {
            Assert.Equal("bad_request", GameLinkRebuildRequest.Take(directory, id, now, out var request));
            Assert.Null(request);
            Assert.False(File.Exists(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RebuildRequiresAGameFolderAndReadyHostInTheExportedWorld()
    {
        string root = Path.Combine(AppContext.BaseDirectory, Guid.NewGuid().ToString("N"));
        string config = Path.Combine(root, "Data", "Config");
        Directory.CreateDirectory(config);
        try
        {
            File.WriteAllText(Path.Combine(config, "biomes.xml"), "");
            File.WriteAllText(Path.Combine(config, "rwgmixer.xml"), "");
            var install = GameInstall.Open(root);
            string folder = Path.Combine(GameInstall.UserGeneratedWorldsDir, World);
            Assert.StartsWith("Set the game folder", GameLinkRebuild.UnavailableReason(folder, null, Ready));
            Assert.StartsWith("Export to a world folder", GameLinkRebuild.UnavailableReason(Path.Combine(root, World), install, Ready));
            Assert.StartsWith("Start the game with", GameLinkRebuild.UnavailableReason(folder, install, null));
            Assert.StartsWith("Start the game with",
                GameLinkRebuild.UnavailableReason(folder, install, null, statusError: GameLinkError.Unavailable));
            Assert.Contains("install Gnostic Game Link 1.1.0",
                GameLinkRebuild.UnavailableReason(folder, install, null, statusError: GameLinkError.UnsupportedProtocol));
            Assert.Contains("restart the game",
                GameLinkRebuild.UnavailableReason(folder, install, null, statusError: GameLinkError.InvalidToken));
            Assert.StartsWith("Enter a save", Reason(Ready with { State = GameLinkState.Menu, Player = null }));
            Assert.StartsWith("Enter a save", Reason(Ready with { Save = "" }));
            Assert.StartsWith("The current save uses a different world", Reason(Ready with { World = "Other World" }));
            Assert.StartsWith("Host the save", Reason(Ready with { Hosting = false }));
            Assert.StartsWith("Host the save", Reason(Ready with { State = GameLinkState.Remote, Hosting = false }));
            Assert.StartsWith("All other players", Reason(Ready with { OtherPlayers = true }));
            Assert.StartsWith("Wait for the current Game Link", Reason(Ready with { Operation = new(Id, "open", "opening", "") }));
            Assert.Null(Reason(Ready));
            Assert.StartsWith("There are no map changes",
                GameLinkRebuild.UnavailableReason(folder, install, Ready, folder, exportHasChanges: false));
            Assert.Null(GameLinkRebuild.UnavailableReason(folder, install, Ready, folder, exportHasChanges: true));
            Assert.Null(GameLinkRebuild.UnavailableReason(folder, install, Ready, folder, exportHasChanges: null));
            Assert.Null(GameLinkRebuild.UnavailableReason(folder, install, Ready,
                Path.Combine(install.WorldsDir, World), exportHasChanges: false));

            string? Reason(GameLinkStatus status) => GameLinkRebuild.UnavailableReason(folder, install, status);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(null, true, AfterExportAction.Playtest)]
    [InlineData(null, false, AfterExportAction.None)]
    [InlineData(null, null, AfterExportAction.None)]
    [InlineData(AfterExportAction.None, true, AfterExportAction.None)]
    [InlineData(AfterExportAction.Rebuild, false, AfterExportAction.Rebuild)]
    public void MigratesTheOldExportPreference(AfterExportAction? choice, bool? legacy, AfterExportAction expected) =>
        Assert.Equal(expected, GameLinkRebuild.ExportPreference(choice, legacy));

    [Fact]
    public void WaitsForTheExactRebuildToFinishInTheSameSave()
    {
        var leaving = Ready with { Operation = new(Id, "leave", "leaving", "") };
        Assert.Equal(GameLinkWaitState.Waiting, Wait(leaving).State);
        var rebuilding = Ready with { Operation = new(Id, "rebuild", "rebuilding", "") };
        Assert.Equal(GameLinkWaitState.Waiting, Wait(rebuilding).State);
        var complete = Ready with { Operation = new(Id, "rebuild", "done", "") { Chunks = 289 } };
        Assert.Equal(GameLinkWaitState.Waiting, Wait(complete with { Save = "Other Save" }).State);
        Assert.Equal(GameLinkWaitState.Waiting, Wait(complete with { State = GameLinkState.Loading }).State);
        Assert.Equal(GameLinkWaitState.Rebuilt, Wait(complete).State);
        Assert.Equal(289, Wait(complete).Chunks);
        Assert.Equal(new(GameLinkWaitState.Failed, "reset_failed"),
            Wait(Ready with { Operation = new(Id, "rebuild", "failed", "reset_failed") }));
        Assert.Equal(new(GameLinkWaitState.Blocked, "older_version"),
            Wait(Ready with { Operation = new(Id, "rebuild", "blocked", "older_version") }));
        Assert.Equal(GameLinkWaitState.TimedOut, GameLinkRebuild.Wait(null, World, Save, Id, true).State);

        static GameLinkWaitResult Wait(GameLinkStatus status) => GameLinkRebuild.Wait(status, World, Save, Id, false);
    }
}
