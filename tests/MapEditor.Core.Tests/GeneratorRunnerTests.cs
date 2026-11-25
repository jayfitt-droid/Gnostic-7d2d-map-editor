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

    private static readonly PrefabLibrary StageLibrary = PrefabLibrary.Of(
    [
        new PrefabInfo(BridgeOp.Tile, "POIs", new Int3(14, 14, 9), -1, 0, 0, ["rwgonly"], null),
        new PrefabInfo(BridgeOp.End, "POIs", new Int3(14, 14, 9), -1, 0, 0, ["rwgonly"], null),
        new PrefabInfo("rwg_tile_downtown_cap", "RWGTiles", new Int3(150, 22, 150), -12, 0, 0, ["streettile"], null),
        new PrefabInfo("trader_rekt", "POIs", new Int3(60, 20, 60), -1, 0, 0, ["trader"], null),
    ]);
    private static readonly BridgeKit BridgeKitForStages = BridgeKit.From(StageLibrary)!;

    private static readonly BridgeLine StageBridge = new(false, 1000, 900, 12);

    /// <summary>
    /// Runs <paramref name="stage"/> as the app does with a tool bridge present: bridges down, the stage, bridges up, one
    /// edit. Checks the rebuilt record's ground is right (taking it down gives the world a bridge-less run gives) and that
    /// undo puts back the world and the list as they were.
    /// </summary>
    private void AssertStageKeepsToolBridges(Func<GeneratorStages, EditSession, StageChanges> stage, Action<World, GeneratorSettings>? setup = null)
    {
        var settings = NewSettings();
        var world = Forest();
        setup?.Invoke(world, settings);
        var plain = Forest();
        var plainSettings = NewSettings();
        setup?.Invoke(plain, plainSettings);
        var data = _data with { Prefabs = StageLibrary };
        var edits = new EditRunner(new UndoStack());
        Assert.True(edits.Run(plain, "Stage", session => stage(new GeneratorStages(plain, plainSettings, data), session)));

        var bridges = new List<PlacedBridge>();
        AddBridge(world, bridges, edits);
        var heightBefore = world.Height.Data.ToArray();
        var roadsBefore = world.Roads!.Data.ToArray();
        var original = bridges[0];
        var originalCopy = original.Clone();

        var undo = new UndoStack();
        var generator = new GeneratorStages(world, settings, data, bridges);
        Assert.True(new EditRunner(undo).Run(world, "Stage", session =>
        {
            var changes = stage(generator, session);
            Assert.Equal(1, changes.BridgesRebuilt);
            Assert.Equal(0, changes.BridgesDropped);
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
        AssertStageKeepsToolBridges((stages, session) => stages.Terrain(session, cancel: cancel).Changes);
    }

    [Fact]
    public void Running_the_features_stage_with_a_tool_bridge_takes_it_down_and_builds_it_again()
    {
        var cancel = TestContext.Current.CancellationToken;
        AssertStageKeepsToolBridges((stages, session) => stages.Features(session, cancel: cancel).Changes,
            (world, settings) => GeneratorRunner.Run(world, settings, _data, [GeneratorStage.Terrain], cancel: cancel));
    }

    [Theory]
    [InlineData(GeneratorStage.Terrain, 1, 1, 1, 1, 1, GeneratorStage.Terrain)]
    [InlineData(GeneratorStage.TownSites, 1, 0, 1, 1, 1, GeneratorStage.Terrain)]
    [InlineData(GeneratorStage.Features, 0, 1, 1, 1, 1, GeneratorStage.Features)]
    [InlineData(GeneratorStage.Highways, 0, 0, 1, 1, 1, GeneratorStage.Highways)]
    [InlineData(GeneratorStage.Wilderness, 0, 0, 0, 1, 1, GeneratorStage.Wilderness)]
    [InlineData(GeneratorStage.Spawns, 0, 0, 0, 0, 1, GeneratorStage.Spawns)]
    public async Task Rerunning_a_stage_takes_down_its_dependents_and_restores_tool_bridges(
        GeneratorStage stage, int towns, int features, int roads, int pois, int spawns, GeneratorStage lastDone)
    {
        var (world, settings, bridges, generator) = WithCompletedStages();
        var bridge = Assert.Single(bridges);
        var cancel = TestContext.Current.CancellationToken;
        var undo = new UndoStack();
        StageChanges? changes = null;
        int added = -1;
        IReadOnlyList<string>? notes = null;
        void Read<T>(StageRun<T> run) => (changes, added, notes) = (run.Changes, run.Added, run.Notes);

        Assert.True(await new EditRunner(undo).RunInBackgroundAsync(world, stage.ToString(), session =>
        {
            switch (stage)
            {
                case GeneratorStage.Terrain: Read(generator.Terrain(session, cancel: cancel)); break;
                case GeneratorStage.TownSites: Read(generator.ReplanTowns(session)); break;
                case GeneratorStage.Features: Read(generator.Features(session, cancel: cancel)); break;
                case GeneratorStage.Highways: Read(generator.Highways(session, cancel: cancel)); break;
                case GeneratorStage.Wilderness: Read(generator.Wilderness(session, cancel: cancel)); break;
                case GeneratorStage.Spawns: Read(generator.Spawns(session)); break;
            }
        }));

        int rebuilt = stage == GeneratorStage.Spawns ? 0 : 1;
        Assert.Equal(new StageChanges(towns, features, roads, pois, spawns, BridgesRebuilt: rebuilt), changes);
        var expectedDone = GeneratorRunner.Order.Where(s => s <= lastDone || stage == GeneratorStage.TownSites && s == GeneratorStage.Features);
        Assert.Equal(expectedDone, settings.Done.Order());
        var after = Assert.Single(bridges);
        Assert.Equal(bridge.Where, after.Where);
        if (rebuilt == 0) Assert.Same(bridge, after);
        else Assert.NotSame(bridge, after);
        Assert.All(after.Prefabs, p => Assert.Contains(world.Prefabs, p.Matches));
        Assert.Equal(pois == 0 ? 1 : 0, settings.PlacedWilderness.Count(p => p.Drawn));
        Assert.Equal(roads == 1 ? 0 : 1, settings.PlacedRoads.Count);
        Assert.DoesNotContain(world.Spawns, p => p.X == -700);
        Assert.Contains(world.Spawns, p => p.X == 0 && p.Z == 0);
        if (stage == GeneratorStage.Spawns)
        {
            Assert.Equal(2, added);
            Assert.Equal(added, settings.PlacedSpawns.Count);
        }
        if (stage == GeneratorStage.Wilderness) Assert.Contains("The game folder has no wilderness prefabs.", notes!);
    }

    [Fact]
    public void Building_planned_towns_takes_out_features_in_their_way_and_preserves_unrelated_features()
    {
        var (world, settings, bridges, _) = WithCompletedStages();
        var mixer = RwgMixer.Parse(XDocument.Parse("""
             <rwgmixer>
               <township name="town"><property name="spawn_gateway" value="false"/></township>
               <district name="downtown"><property name="spawn_weight" value="1"/><property name="required_township" value="town"/></district>
             </rwgmixer>
             """), Size);
        var generator = new GeneratorStages(world, settings, _data with { Mixer = mixer, Prefabs = StageLibrary }, bridges);
        settings.Sites.Add(new TownSite { Township = "town", Cells = [[4, 2]], Exact = true });
        var kept = settings.PlacedFeatures.Single();
        var inTheWay = kept.Clone();
        inTheWay.Cells = [[4, 2]];
        settings.PlacedFeatures.Add(inTheWay);
        var undo = new UndoStack();
        StageRun<TownBuildResult>? run = null;
        Assert.True(new EditRunner(undo).Run(world, "Build towns", session => run = generator.Towns(session)));

        Assert.NotNull(run);
        Assert.Equal(new StageChanges(Features: 1, Highways: 1, Wilderness: 1, Spawns: 1, BridgesRebuilt: 1), run.Changes);
        Assert.Equal(1, run.Added);
        Assert.True(settings.Sites[1].Built);
        Assert.Equal(1, run.Result.FeaturesInTheWay);
        Assert.Same(kept, Assert.Single(settings.PlacedFeatures));
        Assert.Equal([GeneratorStage.Terrain, GeneratorStage.TownSites, GeneratorStage.Features], settings.Done.Order());
        Assert.All(Assert.Single(bridges).Prefabs, p => Assert.Contains(world.Prefabs, p.Matches));
        Assert.NotNull(undo.Undo());
        Assert.False(settings.Sites[1].Built);
        Assert.Equal(2, settings.PlacedFeatures.Count);
        Assert.Equal(GeneratorRunner.Order, settings.Done.Order());
    }

    [Theory]
    [InlineData(GeneratorStage.Features, 1, 0, 0, 0)]
    [InlineData(GeneratorStage.Highways, 0, 1, 1, 1)]
    [InlineData(GeneratorStage.Wilderness, 0, 0, 1, 1)]
    [InlineData(GeneratorStage.Spawns, 0, 0, 0, 1)]
    public void Remove_takes_down_only_the_stages_the_panel_removed(
        GeneratorStage stage, int features, int roads, int pois, int spawns)
    {
        var (world, settings, bridges, generator) = WithCompletedStages();
        var bridge = Assert.Single(bridges);
        StageChanges? changes = null;
        Assert.True(new EditRunner(new UndoStack()).Run(world, "Remove", session => changes = generator.Remove(stage, session)));

        Assert.Equal(new StageChanges(Features: features, Highways: roads, Wilderness: pois, Spawns: spawns), changes);
        var expectedDone = GeneratorRunner.Order.Where(s => stage == GeneratorStage.Features ? s != stage : s < stage);
        Assert.Equal(expectedDone, settings.Done.Order());
        Assert.Same(bridge, Assert.Single(bridges));
        Assert.Equal(roads == 1 ? 0 : 1, settings.PlacedRoads.Count);
        Assert.Equal(features == 1 ? 0 : 1, settings.PlacedFeatures.Count);
        Assert.Equal(spawns == 1 ? 0 : 1, settings.PlacedSpawns.Count);
        Assert.Equal(pois == 0 ? 9 : roads == 0 ? 7 : 0, world.Roads![600, 600]);
        if (stage == GeneratorStage.Wilderness) Assert.Empty(settings.PlacedWilderness);
        if (stage == GeneratorStage.Highways) Assert.False(Assert.Single(settings.PlacedWilderness).Drawn);
    }

    [Fact]
    public async Task Undo_and_redo_of_a_terrain_rerun_restore_the_world_plan_seeds_and_generator_lists()
    {
        var (world, settings, bridges, generator) = WithCompletedStages();
        var undo = new UndoStack();
        var edits = new EditRunner(undo);
        var originalHeight = world.Height.Data.ToArray();
        var originalRoads = world.Roads!.Data.ToArray();
        var originalPrefabs = world.Prefabs.Select(p => (p.Name, p.Position, p.Rotation)).ToArray();
        int oldSeed = settings.Terrain.Seed;
        var oldPlan = settings.TerrainPlan!.ToArray();
        var cancel = TestContext.Current.CancellationToken;

        Assert.True(await edits.RunInBackgroundAsync(world, "Re-roll terrain", session => generator.Terrain(session, cancel: cancel),
            session =>
            {
                GeneratorEditState.Touch(session, settings);
                settings.Terrain.Seed = oldSeed + 1;
            }));

        var newPlan = settings.TerrainPlan!.ToArray();
        var newHeight = world.Height.Data.ToArray();
        Assert.Empty(settings.Sites);
        Assert.Empty(settings.PlacedFeatures);
        Assert.Empty(settings.PlacedRoads);
        Assert.False(Assert.Single(settings.PlacedWilderness).Drawn);
        Assert.Empty(settings.PlacedSpawns);
        Assert.NotNull(undo.Undo());
        Assert.False(undo.CanUndo);
        Assert.Equal(originalHeight, world.Height.Data);
        Assert.Equal(originalRoads, world.Roads.Data);
        Assert.Equal(originalPrefabs, world.Prefabs.Select(p => (p.Name, p.Position, p.Rotation)));
        Assert.Equal(oldSeed, settings.Terrain.Seed);
        Assert.Equal(oldPlan, settings.TerrainPlan);
        Assert.Equal(GeneratorRunner.Order, settings.Done.Order());
        Assert.True(Assert.Single(settings.Sites).Built);
        Assert.True(Assert.Single(settings.PlacedFeatures).Drawn);
        Assert.Single(settings.PlacedRoads);
        Assert.True(Assert.Single(settings.PlacedWilderness).Drawn);
        Assert.Contains(world.Spawns, Assert.Single(settings.PlacedSpawns).Matches);
        Assert.Equal(StageBridge, Assert.Single(bridges).Where);

        Assert.NotNull(undo.Redo());
        Assert.Equal(newHeight, world.Height.Data);
        Assert.Equal(oldSeed + 1, settings.Terrain.Seed);
        Assert.Equal(newPlan, settings.TerrainPlan);
        Assert.Equal([GeneratorStage.Terrain], settings.Done);
        Assert.Empty(settings.Sites);
        Assert.Empty(settings.PlacedSpawns);
    }

    [Fact]
    public void Planning_towns_records_one_edit_and_undo_restores_the_sites_and_seed()
    {
        var world = Forest();
        var settings = NewSettings();
        var generator = new GeneratorStages(world, settings, _data);
        var undo = new UndoStack();
        var edits = new EditRunner(undo);
        var sites = settings.Sites;
        sites.Add(new TownSite { Township = "town", Seed = 10, Cells = [[2, 2]] });
        var plan = new TownSiteResult([new TownSite { Township = "town", Seed = 20, Cells = [[4, 4]] }], []);
        int oldSeed = settings.Towns.Seed;
        int newSeed = oldSeed + 1;
        var height = world.Height.Data.ToArray();

        Assert.True(edits.Run(world, "Plan towns", session => generator.ApplyTownPlan(session, plan, newSeed)));
        Assert.Equal("Plan towns", undo.UndoName);
        Assert.Same(sites, settings.Sites);
        Assert.Equal((4, 4), Assert.Single(Assert.Single(settings.Sites).Core));
        Assert.Equal(newSeed, settings.Towns.Seed);
        Assert.Equal(height, world.Height.Data);

        Assert.NotNull(undo.Undo());
        Assert.False(undo.CanUndo);
        Assert.Equal((2, 2), Assert.Single(Assert.Single(settings.Sites).Core));
        Assert.Equal(oldSeed, settings.Towns.Seed);

        Assert.NotNull(undo.Redo());
        Assert.Equal((4, 4), Assert.Single(Assert.Single(settings.Sites).Core));
        Assert.Equal(newSeed, settings.Towns.Seed);
        Assert.Equal(height, world.Height.Data);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Undo_and_redo_restore_each_town_plan_with_its_terrain(bool buildWorld)
    {
        var world = Forest();
        var settings = NewSettings();
        settings.Sites.Add(new TownSite { Township = "town", Cells = [[2, 2]] });
        var generator = new GeneratorStages(world, settings, _data);
        var undo = new UndoStack();
        var edits = new EditRunner(undo);
        var originalHeight = world.Height.Data.ToArray();
        var cancel = TestContext.Current.CancellationToken;
        string terrainEdit = buildWorld ? "Build World" : "Generate terrain";
        Assert.True(edits.Run(world, terrainEdit, session => generator.Terrain(session, buildingWorld: buildWorld, cancel: cancel)));
        Assert.Empty(settings.Sites);
        var newHeight = world.Height.Data.ToArray();
        Assert.False(originalHeight.AsSpan().SequenceEqual(newHeight));

        var plan = new TownSiteResult([new TownSite { Township = "town", Cells = [[4, 4]] }], []);
        Assert.True(edits.Run(world, "Plan towns", session => generator.ApplyTownPlan(session, plan, settings.Towns.Seed)));

        Assert.Equal("Plan towns", undo.Undo()?.Name);
        Assert.Empty(settings.Sites);
        Assert.Equal(newHeight, world.Height.Data);
        Assert.Equal(terrainEdit, undo.Undo()?.Name);
        Assert.Equal((2, 2), Assert.Single(Assert.Single(settings.Sites).Core));
        Assert.Equal(originalHeight, world.Height.Data);

        Assert.Equal(terrainEdit, undo.Redo()?.Name);
        Assert.Empty(settings.Sites);
        Assert.Equal(newHeight, world.Height.Data);
        Assert.Equal("Plan towns", undo.Redo()?.Name);
        Assert.Equal((4, 4), Assert.Single(Assert.Single(settings.Sites).Core));
        Assert.Equal(newHeight, world.Height.Data);
    }

    [Fact]
    public void An_area_terrain_run_preserves_towns_features_and_plan_tiles_outside_the_area()
    {
        var (world, settings, _, generator) = WithCompletedStages();
        settings.TerrainPlan![0] = TerrainType.Mountains;
        settings.Terrain.Weights = new TerrainWeights(1, 0, 0);
        var outsideTown = new TownSite { Township = "town", Built = true, Cells = [[7, 7]] };
        settings.Sites.Add(outsideTown);
        var outsideFeature = settings.PlacedFeatures.Single().Clone();
        outsideFeature.X = outsideFeature.Z = 1125;
        outsideFeature.Cells = [[7, 7]];
        settings.PlacedFeatures.Add(outsideFeature);
        StageRun<TerrainRunResult>? run = null;
        var cancel = TestContext.Current.CancellationToken;
        Assert.True(new EditRunner(new UndoStack()).Run(world, "Generate area", session =>
            run = generator.Terrain(session, area: new GridRect(1, 1, 2, 2), cancel: cancel)));

        Assert.NotNull(run);
        Assert.Equal(new StageChanges(1, 1, 1, 1, 1, BridgesRebuilt: 1), run.Changes);
        Assert.True(outsideTown.Built);
        Assert.Contains(outsideTown, settings.Sites);
        Assert.Same(outsideFeature, Assert.Single(settings.PlacedFeatures));
        Assert.Equal(TerrainType.Mountains, settings.TerrainPlan[0]);
        Assert.Equal([GeneratorStage.Terrain, GeneratorStage.TownSites, GeneratorStage.Features], settings.Done.Order());
    }

    [Fact]
    public void Building_the_world_from_land_clears_even_done_marks_with_no_output()
    {
        var world = Forest();
        var settings = NewSettings();
        settings.Done.UnionWith(GeneratorRunner.Order);
        var generator = new GeneratorStages(world, settings, _data);
        var cancel = TestContext.Current.CancellationToken;
        Assert.True(new EditRunner(new UndoStack()).Run(world, "Build World", session =>
            generator.Terrain(session, buildingWorld: true, cancel: cancel)));

        Assert.Equal([GeneratorStage.Terrain], settings.Done);
    }

    [Fact]
    public void Replanning_keeps_built_pinned_towns_and_the_town_done_mark()
    {
        var (world, settings, _, generator) = WithCompletedStages();
        Assert.Single(settings.Sites).Pinned = true;
        StageRun<TownPlanResult>? run = null;
        Assert.True(new EditRunner(new UndoStack()).Run(world, "Replan towns", session => run = generator.ReplanTowns(session)));

        Assert.NotNull(run);
        Assert.Equal(new StageChanges(Highways: 1, Wilderness: 1, Spawns: 1, BridgesRebuilt: 1), run.Changes);
        Assert.True(Assert.Single(settings.Sites).Built);
        Assert.Contains(world.Prefabs, p => p.Name == "rwg_tile_downtown_cap");
        Assert.Equal([GeneratorStage.Terrain, GeneratorStage.TownSites, GeneratorStage.Features], settings.Done.Order());
    }

    [Fact]
    public async Task Cancelling_after_take_down_restores_the_generated_items_and_bridges()
    {
        var (world, settings, bridges, generator) = WithCompletedStages();
        var undo = new UndoStack();
        var edits = new EditRunner(undo);
        var originalHeight = world.Height.Data.ToArray();
        var originalRoads = world.Roads!.Data.ToArray();
        var originalPrefabs = world.Prefabs.Select(p => (p.Name, p.Position)).ToArray();
        edits.Failed += (_, failure) => Assert.IsAssignableFrom<OperationCanceledException>(failure.Error);

        Assert.False(await edits.RunInBackgroundAsync(world, "Cancel terrain", session =>
            generator.Terrain(session, cancel: new CancellationToken(canceled: true))));

        Assert.False(undo.CanUndo);
        Assert.Equal(originalHeight, world.Height.Data);
        Assert.Equal(originalRoads, world.Roads.Data);
        Assert.Equal(originalPrefabs, world.Prefabs.Select(p => (p.Name, p.Position)));
        Assert.Equal(GeneratorRunner.Order, settings.Done.Order());
        Assert.True(Assert.Single(settings.Sites).Built);
        Assert.True(Assert.Single(settings.PlacedFeatures).Drawn);
        Assert.Single(settings.PlacedRoads);
        Assert.True(Assert.Single(settings.PlacedWilderness).Drawn);
        Assert.Contains(world.Spawns, Assert.Single(settings.PlacedSpawns).Matches);
        Assert.All(Assert.Single(bridges).Prefabs, p => Assert.Contains(world.Prefabs, p.Matches));
    }

    [Fact]
    public void A_bridge_that_cannot_be_rebuilt_is_reported_and_undo_restores_it()
    {
        var (world, settings, bridges, _) = WithCompletedStages();
        var undo = new UndoStack();
        var generator = new GeneratorStages(world, settings, _data, bridges);
        StageRun<FeatureResult>? run = null;
        Assert.True(new EditRunner(undo).Run(world, "Features", session => run = generator.Features(session)));

        Assert.NotNull(run);
        Assert.Equal(0, run.Changes.BridgesRebuilt);
        Assert.Equal(1, run.Changes.BridgesDropped);
        Assert.Empty(bridges);
        Assert.DoesNotContain(world.Prefabs, p => p.Name == BridgeOp.Tile);
        Assert.NotNull(undo.Undo());
        Assert.All(Assert.Single(bridges).Prefabs, p => Assert.Contains(world.Prefabs, p.Matches));
    }

    private (World World, GeneratorSettings Settings, List<PlacedBridge> Bridges, GeneratorStages Generator) WithCompletedStages()
    {
        var world = Forest();
        var settings = NewSettings();
        settings.Done.UnionWith(GeneratorRunner.Order);
        settings.Spawns.Min = 3;
        int tiles = Size / TerrainStage.TileSize;
        settings.TerrainPlan = Enumerable.Repeat(TerrainType.Plains, tiles * tiles).ToArray();
        var bridges = new List<PlacedBridge>();
        var edits = new EditRunner(new UndoStack());
        Assert.True(edits.Run(world, "Place generated items", session =>
        {
            session.TouchList(world.Prefabs, p => p.Clone(), new GridRect(0, 0, Size, Size));
            session.TouchList(world.Spawns, p => p.Clone(), new GridRect(0, 0, Size, Size));
            world.Prefabs.Add(new PrefabInstance { Name = "rwg_tile_downtown_cap", Position = new Int3(-724, 35, -724) });
            settings.Sites.Add(new TownSite { Township = "town", Built = true, Cells = [[2, 2]] });
            world.Prefabs.Add(new PrefabInstance { Name = "trader_rekt", Position = new Int3(350, 35, 350) });
            settings.PlacedFeatures.Add(new Feature { Stamp = "plains_01", Drawn = true, X = 375, Z = 375, Size = 60, Cells = [[2, 2]] });
            // Stage 5 put pixels on top of stage 4's, so reversing the take-down order leaves the wrong road pixels.
            var overlap = new GridRect(600, 600, 601, 601);
            var roadGrid = world.Roads!;
            session.Touch(roadGrid, overlap);
            var highwayGround = HighwayStage.Capture(world, [overlap], () => roadGrid[600, 600] = 7);
            settings.PlacedRoads.Add(new HighwayRoad { Ground = highwayGround });
            var poiGround = HighwayStage.Capture(world, [overlap], () => roadGrid[600, 600] = 9);
            settings.PlacedWilderness.Add(new WildernessPoi { Name = "camp_01", Ground = poiGround, Pinned = true, Drawn = true });
            world.Spawns.Add(new SpawnPoint { X = -700, Y = 35, Z = -700 });
            world.Spawns.Add(new SpawnPoint { X = 0, Y = 35, Z = 0 });
            settings.PlacedSpawns.Add(new PlacedSpawn { X = -700, Y = 35, Z = -700 });
        }));
        AddBridge(world, bridges, edits);
        return (world, settings, bridges, new GeneratorStages(world, settings, _data with { Prefabs = StageLibrary }, bridges));
    }

    private static void AddBridge(World world, List<PlacedBridge> bridges, EditRunner edits) =>
        Assert.True(edits.Run(world, "Bridge", session =>
            BridgeTool.Add(world, BridgeKitForStages, bridges, StageBridge, BridgeTool.AutoY(world, StageBridge), session)));
}
