using System.Xml.Linq;
using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Generate;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Tests;

public sealed class GeneratorRunnerTests : IDisposable
{
    private const int Size = 2048;
    private readonly string _dir = Directory.CreateTempSubdirectory("mapeditor-runner-").FullName;
    private readonly GeneratorData _data;

    public GeneratorRunnerTests()
    {
        // Game-like terrain stamps: a constant height in a round alpha falloff.
        foreach (var name in new[] { "plains_01", "hills_01", "mountains_01" })
        {
            const int s = 128;
            var bytes = new byte[s * s * 6];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = MathF.Sqrt(MathF.Pow(x - s / 2f, 2) + MathF.Pow(y - s / 2f, 2)) / (s / 2f);
                    int o = (x + y * s) * 6;
                    BitConverter.TryWriteBytes(bytes.AsSpan(o), (ushort)65535);
                    BitConverter.TryWriteBytes(bytes.AsSpan(o + 2), (ushort)(Math.Clamp(1 - d, 0, 1) * 0.2f * 65535));
                }
            File.WriteAllBytes(Path.Combine(_dir, name + ".raw"), bytes);
        }
        var mixer = RwgMixer.Parse(XDocument.Parse("""
            <rwgmixer><world name="any"><property name="world_size" value="0,99999"/>
              <property class="plains"><property name="clusters" value="2, .5, .5"/></property>
              <property class="hills"><property name="clusters" value="2, .3, 1"/></property>
              <property class="mountains"><property name="clusters" value="3, .5, .76"/></property>
            </world></rwgmixer>
            """), Size);
        _data = new GeneratorData(StampLibrary.Load(_dir), mixer, PrefabLibrary.Empty, BiomeTable.Vanilla);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static World Forest()
    {
        var world = new World { Name = "R", Size = Size, Height = new Grid<ushort>(Size, Size) };
        Array.Fill(world.Height.Data, TerrainBrush.ToRaw(35));
        world.Biomes = new IndexedLayer(new Grid<byte>(Size / 8, Size / 8), [BiomeLayout.ForestColor]);
        world.Roads = new Grid<byte>(Size, Size);
        world.Water = new Grid<byte>(Size, Size);
        return world;
    }

    private static GeneratorSettings NewSettings() =>
        GeneratorSettings.ForNewWorld(4, [EdgeStyle.Land, EdgeStyle.Land, EdgeStyle.Land, EdgeStyle.Land], 35);

    [Fact]
    public void Stages_run_in_order_whatever_order_they_are_asked_in()
    {
        var world = Forest();
        var settings = NewSettings();
        var reports = GeneratorRunner.Run(world, settings, _data, [GeneratorStage.Features, GeneratorStage.Terrain],
            cancel: TestContext.Current.CancellationToken);

        Assert.Equal([GeneratorStage.Terrain, GeneratorStage.Features], reports.Select(r => r.Stage));
        Assert.Equal([GeneratorStage.Terrain, GeneratorStage.Features], settings.Done.Order());
        Assert.All(reports, r => Assert.False(string.IsNullOrWhiteSpace(r.Summary)));
    }

    [Fact]
    public void Terrain_raises_the_ground_and_keeps_its_plan_for_later_stages()
    {
        var world = Forest();
        var flat = world.Height.Data.ToArray();
        var settings = NewSettings();
        GeneratorRunner.Run(world, settings, _data, [GeneratorStage.Terrain], cancel: TestContext.Current.CancellationToken);

        Assert.NotEqual(flat, world.Height.Data);
        int tiles = Size / TerrainStage.TileSize;
        Assert.Equal(tiles * tiles, settings.TerrainPlan!.Length);
        Assert.Contains(GeneratorStage.Terrain, settings.Done);
    }

    [Fact]
    public void A_stage_that_has_run_or_one_before_it_is_refused()
    {
        var world = Forest();
        var settings = NewSettings();
        var cancel = TestContext.Current.CancellationToken;
        GeneratorRunner.Run(world, settings, _data, [GeneratorStage.Terrain, GeneratorStage.TownSites], cancel: cancel);

        var again = Assert.Throws<InvalidOperationException>(() =>
            GeneratorRunner.Run(world, settings, _data, [GeneratorStage.TownSites], cancel: cancel));
        Assert.Contains("towns", again.Message);
        Assert.Throws<InvalidOperationException>(() => GeneratorRunner.Run(world, settings, _data, [GeneratorStage.Terrain], cancel: cancel));

        // The next stage on carries on from there.
        var next = GeneratorRunner.Run(world, settings, _data, [GeneratorStage.Features], cancel: cancel);
        Assert.Equal(GeneratorStage.Features, Assert.Single(next).Stage);
    }

    [Fact]
    public void Each_stage_reports_its_start_its_end_and_its_result()
    {
        var world = Forest();
        var seen = new List<(GeneratorStage Stage, double Done)>();
        var finished = new List<GeneratorStage>();
        var progress = new Recorder(seen);
        GeneratorRunner.Run(world, NewSettings(), _data, [GeneratorStage.Terrain, GeneratorStage.TownSites], progress,
            r => finished.Add(r.Stage), TestContext.Current.CancellationToken);

        Assert.Equal([GeneratorStage.Terrain, GeneratorStage.TownSites], finished);
        foreach (var stage in finished)
        {
            var mine = seen.Where(p => p.Stage == stage).Select(p => p.Done).ToList();
            Assert.Equal(0, mine[0]);
            Assert.Equal(1, mine[^1]);
        }
        // Terrain's reports all come before the towns' first.
        Assert.True(seen.FindLastIndex(p => p.Stage == GeneratorStage.Terrain) < seen.FindIndex(p => p.Stage == GeneratorStage.TownSites));
    }

    [Fact]
    public void A_world_without_biomes_is_refused()
    {
        var world = Forest();
        world.Biomes = null;
        Assert.Throws<InvalidOperationException>(() =>
            GeneratorRunner.Run(world, NewSettings(), _data, [GeneratorStage.Terrain], cancel: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Asking_for_no_stages_changes_nothing()
    {
        var world = Forest();
        var settings = NewSettings();
        Assert.Empty(GeneratorRunner.Run(world, settings, _data, [], cancel: TestContext.Current.CancellationToken));
        Assert.Empty(settings.Done);
        Assert.Null(settings.TerrainPlan);
    }

    /// <summary>Records reports as they come; a stage may report from several threads.</summary>
    private sealed class Recorder(List<(GeneratorStage, double)> seen) : IProgress<(GeneratorStage Stage, double Done)>
    {
        public void Report((GeneratorStage Stage, double Done) value)
        {
            lock (seen) seen.Add(value);
        }
    }

    // ---- the Bridge tool's bridges when a stage changes the ground ----

    private static readonly BridgeKit BridgeKitForStages = BridgeKit.From(PrefabLibrary.Of(
    [
        new PrefabInfo(BridgeOp.Tile, "POIs", new Int3(14, 14, 9), -1, 0, 0, ["rwgonly"], null),
        new PrefabInfo(BridgeOp.End, "POIs", new Int3(14, 14, 9), -1, 0, 0, ["rwgonly"], null),
    ]))!;

    private static readonly BridgeLine StageBridge = new(false, 1000, 900, 12);

    /// <summary>
    /// Runs <paramref name="stage"/> as the app does with a tool bridge present: bridges down, the stage, bridges up, one
    /// edit. Checks the rebuilt record's ground is right (taking it down gives the world a bridge-less run gives) and that
    /// undo puts back the world and the list as they were.
    /// </summary>
    private void AssertStageKeepsToolBridges(Action<World, GeneratorSettings, EditSession> stage, Action<World, GeneratorSettings>? setup = null)
    {
        var settings = NewSettings();
        var world = Forest();
        setup?.Invoke(world, settings);
        var plain = Forest();
        var plainSettings = NewSettings();
        setup?.Invoke(plain, plainSettings);
        stage(plain, plainSettings, new EditSession("Stage", plain));

        var bridges = new List<PlacedBridge>();
        BridgeTool.Add(world, BridgeKitForStages, bridges, StageBridge, BridgeTool.AutoY(world, StageBridge), new EditSession("Bridge", world));
        var heightBefore = world.Height.Data.ToArray();
        var roadsBefore = world.Roads!.Data.ToArray();
        var original = bridges[0];
        var originalCopy = original.Clone();

        var undo = new UndoStack();
        var edits = new EditRunner(undo);
        Assert.True(edits.Run(world, "Stage", session =>
        {
            var taken = BridgeTool.TakeDownAll(world, bridges, session);
            stage(world, settings, session);
            Assert.Equal(0, BridgeTool.RebuildAll(world, BridgeKitForStages, bridges, taken, session));
        }));

        var rebuilt = Assert.Single(bridges);
        Assert.NotSame(original, rebuilt);
        Assert.Equal(StageBridge, rebuilt.Where);
        Assert.Equal(rebuilt.Pieces, world.Prefabs.Count);
        Assert.Equal(BridgeTool.AutoY(plain, StageBridge), rebuilt.Y);

        // Take the rebuilt bridge down: the ground is what the stage alone gives.
        var undoCheck = new EditSession("Down", world);
        var afterStageWithBridge = world.Height.Data.ToArray();
        BridgeTool.Remove(world, bridges, rebuilt, undoCheck);
        Assert.Equal(plain.Height.Data, world.Height.Data);
        Assert.Equal(plain.Roads!.Data, world.Roads!.Data);
        Assert.Empty(world.Prefabs);
        undoCheck.Rollback();
        Assert.Equal(afterStageWithBridge, world.Height.Data);

        // Undoing the stage gives back the world and the list as they were.
        undo.Undo();
        Assert.Equal(heightBefore, world.Height.Data);
        Assert.Equal(roadsBefore, world.Roads!.Data);
        var back = Assert.Single(bridges);
        Assert.Equal((originalCopy.Where, originalCopy.Y), (back.Where, back.Y));
        Assert.Equal(originalCopy.Grounds, back.Grounds);
        Assert.Equal(back.Pieces, world.Prefabs.Count);
    }

    [Fact]
    public void Running_the_terrain_stage_with_a_tool_bridge_takes_it_down_and_builds_it_on_the_new_ground()
    {
        var cancel = TestContext.Current.CancellationToken;
        AssertStageKeepsToolBridges((world, settings, session) =>
        {
            var result = TerrainStage.Run(world, settings.Terrain.Clone(), _data.Stamps, _data.Mixer, settings.PaintedTerrain, area: null,
                previousMask: null, session, null, cancel);
            settings.TerrainPlan = result.Plan;
        });
    }

    [Fact]
    public void Running_the_features_stage_with_a_tool_bridge_takes_it_down_and_builds_it_again()
    {
        var cancel = TestContext.Current.CancellationToken;
        AssertStageKeepsToolBridges(
            (world, settings, session) => FeatureStage.Run(world, settings.Features.Clone(), settings.PlacedFeatures, _data.Stamps, _data.Mixer,
                _data.Prefabs, settings.Sites.ToList(), settings.TerrainPlan, settings.Terrain.BaseHeight, session, null, cancel),
            (world, settings) => GeneratorRunner.Run(world, settings, _data, [GeneratorStage.Terrain], cancel: cancel));
    }
}
