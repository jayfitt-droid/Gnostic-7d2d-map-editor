using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;
using MapEditor.Core.Rendering;

namespace MapEditor.Core.Tests;

/// <summary>Edits as transactions: a failed edit leaves the world as it was, and undo covers added layers and colours.</summary>
public sealed class EditTransactionTests
{
    private const int Size = 512;

    private static World Flat()
    {
        var world = new World { Name = "T", Size = Size, Height = new Grid<ushort>(Size, Size) };
        Array.Fill(world.Height.Data, World.MetresToHeight(40));
        world.Prefabs.Add(new PrefabInstance { Name = "house_01", Position = new Int3(0, 40, 0) });
        return world;
    }

    private static readonly RoadPath Road = new([new(100, 200), new(400, 200)], RoadType.Gravel, 4.5f, Grade: true);

    private static (EditRunner Runner, UndoStack Undo, List<EditFailedEventArgs> Failures, List<EditSession> Commits) Runner()
    {
        var undo = new UndoStack();
        var runner = new EditRunner(undo);
        var failures = new List<EditFailedEventArgs>();
        var commits = new List<EditSession>();
        runner.Failed += (_, e) => failures.Add(e);
        runner.Committed += (_, s) => commits.Add(s);
        return (runner, undo, failures, commits);
    }

    /// <summary>State outside the world, as the generator's Done marks and status are (#15).</summary>
    private sealed class Bookkeeping
    {
        public string Status = "Not run yet.";

        public void Touch(EditSession session) => session.TouchState(this, () => Status, s => Status = (string)s!);
    }

    [Fact]
    public void Build_World_step_is_marked_when_undone_and_redone()
    {
        var world = Flat();
        var (runner, undo, _, _) = Runner();

        Assert.True(runner.Run(world, "Raise", s => { s.Touch(world.Height, new GridRect(0, 0, 10, 10)); world.Height[0, 0] = 1; }));
        Assert.True(runner.Run(world, "Build World", s =>
        {
            s.BuildsWorld = true;
            s.Touch(world.Height, new GridRect(0, 0, 10, 10));
            world.Height[1, 1] = 2;
        }));
        Assert.False(undo.RedoBuildsWorld);

        Assert.True(undo.Undo()!.BuildsWorld);
        Assert.True(undo.RedoBuildsWorld);
        Assert.Equal("Build World", undo.RedoName);

        Assert.True(undo.Redo()!.BuildsWorld);
        Assert.False(undo.RedoBuildsWorld);

        Assert.True(undo.Undo()!.BuildsWorld);
        Assert.False(undo.Undo()!.BuildsWorld);
        Assert.False(undo.RedoBuildsWorld);
    }

    [Fact]
    public async Task State_touched_before_a_stage_is_undone_and_redone_with_it()
    {
        var world = Flat();
        var state = new Bookkeeping();
        var (runner, undo, _, _) = Runner();

        // As a stage does: the state is saved on the calling thread, the ground changes in the background, and the
        // status is set after the run.
        await runner.RunInBackgroundAsync(world, "Stage", s =>
        {
            s.Touch(world.Height, new GridRect(0, 0, 10, 10));
            world.Height[0, 0] = 1;
        }, state.Touch);
        state.Status = "Done.";

        undo.Undo();
        Assert.Equal("Not run yet.", state.Status);
        Assert.Equal(World.MetresToHeight(40), world.Height[0, 0]);
        undo.Redo();
        Assert.Equal("Done.", state.Status);
        Assert.Equal(1, world.Height[0, 0]);
    }

    [Fact]
    public void State_alone_is_not_a_change_and_a_failed_edit_puts_it_back()
    {
        var world = Flat();
        var state = new Bookkeeping();
        var (runner, undo, failures, _) = Runner();

        // Nothing in the world changed: no undo step for the state alone.
        Assert.True(runner.Run(world, "Nothing", state.Touch));
        Assert.False(undo.CanUndo);

        Assert.False(runner.Run(world, "Fails", s =>
        {
            state.Touch(s);
            state.Status = "Half done.";
            throw new InvalidOperationException("boom");
        }));
        Assert.Equal("Not run yet.", state.Status);
        Assert.Single(failures);
    }

    [Fact]
    public async Task A_background_edit_commits_once_or_rolls_back_on_cancel()
    {
        var world = Flat();
        var height = (ushort[])world.Height.Data.Clone();
        var (runner, undo, failures, commits) = Runner();
        var all = new GridRect(0, 0, Size, Size);

        Assert.False(await runner.RunInBackgroundAsync(world, "Cancelled", s =>
        {
            s.Touch(world.Height, all);
            Array.Fill(world.Height.Data, World.MetresToHeight(90));
            throw new OperationCanceledException();
        }));
        Assert.Equal(height, world.Height.Data);
        Assert.IsType<OperationCanceledException>(Assert.Single(failures).Error);
        Assert.False(undo.CanUndo);

        Assert.True(await runner.RunInBackgroundAsync(world, "Raise all", s =>
        {
            s.Touch(world.Height, all);
            Array.Fill(world.Height.Data, World.MetresToHeight(90));
        }));
        Assert.Equal("Raise all", Assert.Single(commits).Name);
        undo.Undo();
        Assert.Equal(height, world.Height.Data);
    }

    [Fact]
    public async Task The_map_reads_the_prefabs_from_before_while_a_background_edit_changes_them()
    {
        var world = Flat();
        world.Prefabs.Add(new PrefabInstance { Name = "a" });
        int before = world.Prefabs.Count;
        var (runner, _, _, _) = Runner();
        int shownDuring = -1;
        Assert.True(await runner.RunInBackgroundAsync(world, "Add", s =>
        {
            s.TouchList(world.Prefabs, p => p.Clone(), GridRect.Empty);
            world.Prefabs.Add(new PrefabInstance { Name = "b" });
            shownDuring = world.ShownPrefabs.Count;
        }));
        Assert.Equal(before, shownDuring);
        Assert.Equal(before + 1, world.ShownPrefabs.Count);
    }

    [Fact]
    public void A_failed_edit_is_rolled_back_whole()
    {
        var world = Flat();
        var height = (ushort[])world.Height.Data.Clone();
        var (runner, undo, failures, commits) = Runner();

        bool ok = runner.Run(world, "Broken", session =>
        {
            RoadOp.Draw(world, Road, session); // adds the roads layer and grades the ground
            session.TouchList(world.Prefabs, p => p.Clone(), GridRect.Empty);
            world.Prefabs.Clear();
            throw new InvalidOperationException("boom");
        });

        Assert.False(ok);
        Assert.Null(world.Roads);
        Assert.Equal(height, world.Height.Data);
        Assert.Single(world.Prefabs);
        Assert.False(undo.CanUndo);
        Assert.Empty(commits);
        Assert.Equal("boom", Assert.Single(failures).Error.Message);
    }

    [Fact]
    public void A_stroke_failing_partway_rolls_back_its_earlier_dabs()
    {
        var world = Flat();
        var height = (ushort[])world.Height.Data.Clone();
        var (runner, undo, failures, _) = Runner();
        var settings = new BrushSettings(BrushMode.Raise, 30, 1, 0);

        var stroke = runner.Begin(world, "Raise");
        Assert.True(runner.Step(stroke, s => TerrainBrush.Dab(world.Height, 100, 100, settings, s, null)));
        Assert.True(runner.Step(stroke, s => TerrainBrush.Dab(world.Height, 200, 100, settings, s, null)));
        Assert.NotEqual(height, world.Height.Data);
        Assert.False(runner.Step(stroke, s =>
        {
            TerrainBrush.Dab(world.Height, 300, 100, settings, s, null);
            throw new InvalidOperationException("boom");
        }));

        Assert.Equal(height, world.Height.Data);
        Assert.False(undo.CanUndo);
        Assert.Single(failures);
    }

    [Fact]
    public void Undoing_the_first_road_removes_the_roads_layer_and_redo_brings_it_back()
    {
        var world = Flat();
        var (runner, undo, _, commits) = Runner();

        Assert.True(runner.Run(world, "Road", s => RoadOp.Draw(world, Road, s)));
        var roads = world.Roads;
        Assert.NotNull(roads);
        Assert.Single(commits);

        undo.Undo();
        Assert.Null(world.Roads);

        undo.Redo();
        Assert.Same(roads, world.Roads);
        Assert.Equal((byte)RoadType.Gravel, world.Roads![250, 200]);
    }

    [Fact]
    public void Undoing_a_new_biome_colour_takes_it_off_the_palette()
    {
        var world = Flat();
        world.Biomes = LayerPaint.NewBiomeLayer(Size);
        var (runner, undo, _, _) = Runner();

        runner.Run(world, "Desert", s => LayerPaint.PaintBiome(world, 256, 256, new BrushTip(40), 0xFFE477, s));
        Assert.Equal([LayerPaint.DefaultBiome, 0xFFE477u], world.Biomes.Palette);

        undo.Undo();
        Assert.Equal([LayerPaint.DefaultBiome], world.Biomes.Palette);
        Assert.All(world.Biomes.Indices.Data, i => Assert.Equal(0, i));

        undo.Redo();
        Assert.Equal([LayerPaint.DefaultBiome, 0xFFE477u], world.Biomes.Palette);
        Assert.Equal(1, world.Biomes.Indices[256 / 8, 256 / 8]);
    }

    [Fact]
    public void An_edit_that_changes_nothing_is_not_an_undo_step()
    {
        var world = Flat();
        var (runner, undo, failures, commits) = Runner();
        Assert.True(runner.Run(world, "Nothing", _ => { }));
        Assert.False(undo.CanUndo);
        Assert.Empty(commits);
        Assert.Empty(failures);
    }

    [Fact]
    public void Map_shows_the_right_biome_after_undoing_a_layer_and_painting_another_colour()
    {
        var world = Flat();
        var (runner, undo, _, _) = Runner();
        var renderer = new MapRenderer(world, BiomeTable.Vanilla);
        var tile = new uint[MapRenderer.TileSize * MapRenderer.TileSize];

        runner.Run(world, "Desert", s => LayerPaint.PaintBiome(world, 256, 256, new BrushTip(40), 0xFFE477, s));
        renderer.RenderTile(1, 0, 0, MapLayers.Biomes, tile);
        uint desert = tile[128 + 128 * MapRenderer.TileSize];

        undo.Undo();
        Assert.Null(world.Biomes);
        runner.Run(world, "Snow", s => LayerPaint.PaintBiome(world, 256, 256, new BrushTip(40), 0xFFFFFF, s));
        renderer.RenderTile(1, 0, 0, MapLayers.Biomes, tile);

        Assert.NotEqual(desert, tile[128 + 128 * MapRenderer.TileSize]);
    }

    private static void Dab(EditRunner runner, World world, ushort value) =>
        runner.Run(world, "Dab", s =>
        {
            s.Touch(world.Height, new GridRect(0, 0, 10, 10));
            world.Height[0, 0] = value;
        });

    [Fact]
    public void Undo_back_to_the_save_point_is_not_a_change()
    {
        var world = Flat();
        var (runner, undo, _, _) = Runner();
        Assert.True(undo.AtSavePoint);

        Dab(runner, world, 1);
        Dab(runner, world, 2);
        Assert.False(undo.AtSavePoint);

        undo.Undo();
        undo.Undo();
        Assert.True(undo.AtSavePoint);

        undo.Redo();
        Assert.False(undo.AtSavePoint);
    }

    [Fact]
    public void A_save_moves_the_save_point_and_a_new_edit_after_undo_leaves_it_behind()
    {
        var world = Flat();
        var (runner, undo, _, _) = Runner();
        Dab(runner, world, 1);
        undo.MarkSaved(undo.Position);
        Assert.True(undo.AtSavePoint);

        // Undo past the save, then redo back to it.
        undo.Undo();
        Assert.False(undo.AtSavePoint);
        undo.Redo();
        Assert.True(undo.AtSavePoint);

        // Undo past the save and make a new edit: the saved step is gone from redo, so nothing gets back there.
        undo.Undo();
        Dab(runner, world, 3);
        Assert.False(undo.AtSavePoint);
        undo.Undo();
        Assert.False(undo.AtSavePoint);

        undo.Clear();
        Assert.True(undo.AtSavePoint);
    }

    [Fact]
    public void Dropping_old_steps_keeps_the_save_point_right()
    {
        var world = Flat();
        // Room for about one step, so each new one drops the oldest.
        var undo = new UndoStack(maxBytes: 1);
        var runner = new EditRunner(undo);

        // Saved with nothing to undo; once the first step is dropped, undo can't get back there.
        Dab(runner, world, 1);
        Dab(runner, world, 2);
        undo.Undo();
        Assert.False(undo.AtSavePoint);

        // Saved after a step that is then dropped: the bottom of the history is now that save.
        undo.Clear();
        Dab(runner, world, 3);
        undo.MarkSaved(undo.Position);
        Dab(runner, world, 4);
        Assert.False(undo.AtSavePoint);
        undo.Undo();
        Assert.True(undo.AtSavePoint);
    }

    [Fact]
    public void AnExportPositionStillIdentifiesTheWorldAfterDroppingUndoSteps()
    {
        var world = Flat();
        var undo = new UndoStack(maxBytes: 1);
        var runner = new EditRunner(undo);

        object original = undo.Position;
        Dab(runner, world, 1);
        object exported = undo.Position;
        Dab(runner, world, 2);
        undo.Undo();

        Assert.Same(exported, undo.Position);
        Assert.NotSame(original, undo.Position);
        undo.Redo();
        Assert.NotSame(exported, undo.Position);
    }
}
