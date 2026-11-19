using System.IO.Compression;
using System.Text.Json;
using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Generate;
using MapEditor.Core.IO;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Tests;

public sealed class LandDrawingTests
{
    private static LandShape Shape(params LandBox[] boxes) => new()
        { Kind = LandShapeKind.Drawn, Drawing = new LandDrawing { Boxes = [.. boxes] } };

    private static LandField Build(params LandBox[] boxes) => LandShapes.Build(4096, Shape(boxes))!;

    [Fact]
    public void Land_is_inside_the_boxes_and_sea_is_outside()
    {
        var field = Build(new(512, 512, 1792, 1792), new(2304, 2304, 3584, 3584));
        Assert.True(field.IsLand(1152, 1152));
        Assert.True(field.IsLand(2944, 2944));
        Assert.False(field.IsLand(2048, 2048));
        Assert.False(field.IsLand(100, 100));
        Assert.Equal(2, field.Islands().Islands.Count);
        Assert.Equal(0, Build().LandShare());
        Assert.Equal(0, Build(new LandBox(512, 512, 768, 768)).LandShare());
    }

    [Fact]
    public void A_water_box_cuts_land_even_if_it_was_drawn_first_or_is_a_small_inland_sea()
    {
        var land = new LandBox(512, 512, 3584, 3584);
        var cut = new LandBox(1920, 1920, 2176, 2176, CutsWater: true);
        Assert.True(Build(land).IsLand(2048, 2048));
        var lake = Build(cut, land);
        Assert.False(lake.IsLand(2048, 2048));
        Assert.True(lake.IsLand(2400, 2048));
        Assert.Equal(Cells(lake), Cells(Build(land, cut)));
    }

    [Fact]
    public void Overlapping_coasts_merge_but_overlapping_empty_corners_do_not()
    {
        var first = new LandBox(384, 480, 1664, 1760);
        var second = new LandBox(1216, 480, 2496, 1760);
        var third = new LandBox(2432, 1696, 3712, 2976);
        Assert.Single(Build(first, second).Islands().Islands);
        Assert.Equal(2, Build(first, second, third).Islands().Islands.Count);
    }

    [Theory]
    [InlineData(0, 1024, 1280, 2304, 0, 1664)]
    [InlineData(2816, 1024, 4096, 2304, 4096, 1664)]
    [InlineData(1024, 0, 2304, 1280, 1664, 0)]
    [InlineData(1024, 2816, 2304, 4096, 1664, 4096)]
    public void A_box_on_an_edge_runs_off_the_map(int x0, int z0, int x1, int z1, int x, int z)
    {
        var field = Build(new LandBox(x0, z0, x1, z1));
        Assert.True(field.IsLand(x, z));
        Assert.False(field.IsLand(0, 0));
        Assert.False(field.IsLand(4096, 4096));
    }

    [Fact]
    public void Moving_an_interior_box_translates_its_land()
    {
        var box = new LandBox(512, 512, 1792, 1792, Seed: 38, Roughness: 100);
        var moved = box with { X0 = 1536, X1 = 2816, Z0 = 2048, Z1 = 3328 };
        var before = Build(box);
        var after = Build(moved);
        for (int z = box.Z0; z < box.Z1; z += LandField.Cell)
            for (int x = box.X0; x < box.X1; x += LandField.Cell)
                Assert.Equal(before[x / LandField.Cell, z / LandField.Cell],
                    after[(x + 1024) / LandField.Cell, (z + 1536) / LandField.Cell]);
    }

    [Fact]
    public void A_seed_repeats_its_coast_and_a_new_seed_changes_it()
    {
        var box = new LandBox(512, 512, 1792, 1792, Seed: 38, Roughness: 100);
        var original = Cells(Build(box));
        Assert.Equal(original, Cells(Build(box)));
        Assert.NotEqual(original, Cells(Build(box with { Seed = 42 })));
    }

    [Fact]
    public void High_fill_is_capped_and_leaves_a_rounded_shore()
    {
        var box = new LandBox(512, 512, 3584, 3584, Roughness: 100, Fill: LandBox.MaxFill);
        var high = Build(box);
        Assert.Equal(Cells(high), Cells(Build(box with { Fill = 1000 })));
        Assert.True(high.LandShare() > Build(box with { Fill = 30 }).LandShare());
        Assert.True(high.IsLand(2048, 2048));
        for (int cell = 16; cell < 112; cell++)
        {
            Assert.False(high.IsLandCell(16, cell));
            Assert.False(high.IsLandCell(111, cell));
            Assert.False(high.IsLandCell(cell, 16));
            Assert.False(high.IsLandCell(cell, 111));
        }
        Assert.False(high.IsLand(800, 800)); // A corner is still water well inside the box.
    }

    [Fact]
    public void Same_land_compares_drawings_and_ignores_inactive_shape_settings()
    {
        var land = new LandBox(512, 512, 2048, 2048);
        var water = new LandBox(768, 768, 1280, 1280, CutsWater: true);
        var shape = Shape(land, water);
        Assert.True(Shape().SameLand(new LandShape { Kind = LandShapeKind.Drawn }));
        Assert.False(shape.SameLand(new LandShape { Kind = LandShapeKind.Island }));
        var same = Shape(water, land);
        same.Seed++;
        same.LandPercent++;
        Assert.True(shape.SameLand(same));
        Assert.False(shape.SameLand(Shape(land)));
        Assert.False(shape.SameLand(Shape(land, water with { Fill = 25 })));
        Assert.False(shape.SameLand(Shape(land with { Seed = 10 }, water)));

        var copy = shape.Clone();
        copy.Drawing!.Boxes.Clear();
        Assert.Equal(2, shape.Drawing!.Boxes.Count);
        Assert.False(shape.SameLand(copy));
    }

    [Fact]
    public void Draft_undo_and_redo_restore_add_move_resize_seed_and_delete_steps()
    {
        var land = new LandBox(512, 512, 1792, 1792);
        var saved = Shape(land).Drawing!;
        var water = land with { CutsWater = true };
        var moved = water with { X0 = 1024, X1 = 2304 };
        var resized = moved with { X1 = 2048 };
        var rerolled = resized with { Seed = 100 };
        var draft = new LandDrawingHistory(saved);
        draft.Add(water);
        draft.Replace(1, moved, "move box");
        draft.Replace(1, resized, "resize box");
        draft.Replace(1, rerolled, "re-roll coast");
        draft.Delete(1);
        Assert.Equal("delete box", draft.UndoName);
        Assert.Single(draft.Boxes);
        foreach (var restored in new[] { rerolled, resized, moved, water })
        {
            Assert.True(draft.Undo());
            Assert.Equal(restored, draft.Boxes[1]);
        }
        Assert.True(draft.Undo());
        Assert.False(draft.Undo());
        Assert.Single(draft.Boxes);
        Assert.True(draft.Redo());
        Assert.Equal(water, draft.Boxes[1]);
        draft.Replace(1, water, "no change");
        Assert.Equal("move box", draft.RedoName);
        draft.Delete(1);
        Assert.False(draft.Redo());
        Assert.Equal(land, Assert.Single(saved.Boxes));
        draft.Snapshot().Boxes.Clear();
        Assert.Single(draft.Boxes);
    }

    [Fact]
    public async Task A_terrain_build_uses_the_drawing_and_undo_restores_the_ocean_and_drawing()
    {
        var shape = Shape();
        var shares = BiomeLayout.DefaultShares(BiomeTable.Vanilla);
        var plan = new BiomePlan(BiomeLayoutKind.Line, shares);
        var world = NewWorldGenerator.Create(new NewWorldOptions("Drawn", 2048, NoiseAmplitude: 0, Shape: shape, BiomePlan: plan),
            StampLibrary.Empty, null);
        var settings = GeneratorSettings.ForNewWorld(1, [], 35, shape, BiomeLayoutSettings.From(plan, 1, shape));
        var undo = new UndoStack();
        var edits = new EditRunner(undo);
        var options = settings.Terrain.Clone();
        var drawing = new LandDrawing { Boxes = [new(0, 256, 1536, 1792)] };
        options.Shape.Drawing = drawing;
        Assert.Equal(StampOp.DefaultWaterLevel, world.Water![640, 1024]);
        Assert.True(await edits.RunInBackgroundAsync(world, "Build World", session =>
        {
            var result = TerrainStage.Run(world, options, StampLibrary.Empty, RwgMixer.Empty, null, null, null, session,
                cancel: TestContext.Current.CancellationToken, replanBiomes: settings.Biomes);
            Assert.True(result.BiomesPlanned);
        }, session =>
        {
            session.TouchState(settings, () => settings.Terrain.Shape.Drawing,
                value => settings.Terrain.Shape.Drawing = (LandDrawing?)value, ReferenceEquals);
            settings.Terrain.Shape.Drawing = drawing;
        }));
        Assert.Equal(0, world.Water[640, 1024]);
        Assert.Equal(StampOp.DefaultWaterLevel, world.Water[1900, 1024]);
        Assert.True(settings.BiomesOutOfDate);
        Assert.NotNull(undo.Undo());
        Assert.False(undo.CanUndo);
        Assert.Empty(settings.Terrain.Shape.Drawing!.Boxes);
        Assert.Equal(StampOp.DefaultWaterLevel, world.Water[640, 1024]);
        Assert.NotNull(undo.Redo());
        Assert.Equal(drawing.Boxes, settings.Terrain.Shape.Drawing!.Boxes);
        Assert.Equal(0, world.Water[640, 1024]);
    }

    [Fact]
    public void Land_and_water_boxes_round_trip_in_a_project()
    {
        var shape = Shape(new(0, 64, 800, 992, Seed: 104, Fill: 66, Roughness: 75),
            new(320, 352, 608, 640, CutsWater: true, Seed: 227, Fill: 55, Roughness: 30));
        var world = NewWorldGenerator.Create(new NewWorldOptions("Drawn", 1024, NoiseAmplitude: 0, Shape: shape), StampLibrary.Empty, null);
        var plan = new BiomePlan(BiomeLayoutKind.Line, BiomeLayout.DefaultShares(BiomeTable.Vanilla));
        var generator = GeneratorSettings.ForNewWorld(3, [], 35, shape, BiomeLayoutSettings.From(plan, 3, shape));
        var directory = Directory.CreateTempSubdirectory("mapeditor-drawing-");
        try
        {
            string path = Path.Combine(directory.FullName, "drawing" + ProjectStore.Extension);
            ProjectStore.Save(world, path, new ProjectStore.ProjectSettings { Generator = generator }.Clone());
            using (var zip = ZipFile.OpenRead(path))
            using (var manifest = JsonDocument.Parse(zip.GetEntry("project.json")!.Open()))
            {
                var root = manifest.RootElement;
                Assert.Equal(5, root.GetProperty("Format").GetInt32());
                // A format 4 reader sees no new enum in Settings before it can reject the format.
                Assert.Equal(JsonValueKind.Null, root.GetProperty("Settings").ValueKind);
                Assert.Equal("Drawn", root.GetProperty("DrawnWorldSettings").GetProperty("Generator")
                    .GetProperty("Terrain").GetProperty("Shape").GetProperty("Kind").GetString());
            }
            var reopened = ProjectStore.Load(path, out var settings);
            Assert.Equal(world.Water!.Data, reopened.Water!.Data);
            var loaded = Assert.IsType<GeneratorSettings>(settings.Generator);
            Assert.Equal(shape.Drawing!.Boxes, loaded.Terrain.Shape.Drawing!.Boxes);
            Assert.True(shape.SameLand(loaded.Terrain.Shape));
            Assert.False(loaded.BiomesOutOfDate);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static float[] Cells(LandField field) =>
        [.. Enumerable.Range(0, field.Width * field.Width).Select(i => field[i % field.Width, i / field.Width])];
}
