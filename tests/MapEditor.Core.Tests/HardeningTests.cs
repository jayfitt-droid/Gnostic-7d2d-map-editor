using System.IO.Compression;
using System.Text.Json.Nodes;
using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Generate;
using MapEditor.Core.IO;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Tests;

/// <summary>The small fixes from the v2.0.0 code review's hardening list (#22): damaged files and unusual cases.</summary>
public sealed class HardeningTests : IDisposable
{
    private const int Size = 64;
    private readonly string _dir = Directory.CreateTempSubdirectory("mapeditor-hardening-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static World MakeWorld(float metres = 40)
    {
        var world = new World { Name = "H", Size = Size, Height = new Grid<ushort>(Size, Size) };
        Array.Fill(world.Height.Data, World.MetresToHeight(metres));
        world.ExtraFiles[WorldFiles.Level] = [1, 2, 3];
        return world;
    }

    /// <summary>A saved project whose project.json is then edited by hand.</summary>
    private string CraftedProject(Action<JsonObject> edit)
    {
        string path = Path.Combine(_dir, "p" + ProjectStore.Extension);
        ProjectStore.Save(MakeWorld(), path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        var entry = zip.GetEntry("project.json")!;
        JsonObject json;
        using (var s = entry.Open()) json = JsonNode.Parse(s)!.AsObject();
        edit(json);
        entry.Delete();
        using (var w = new StreamWriter(zip.CreateEntry("project.json").Open())) w.Write(json.ToJsonString());
        return path;
    }

    [Fact]
    public void A_project_json_with_missing_settings_sections_still_opens()
    {
        string path = CraftedProject(j => j["Settings"] = new JsonObject
        {
            ["Generator"] = new JsonObject
            {
                ["Towns"] = null,
                ["Sites"] = null,
                ["PlacedRoads"] = new JsonArray(null, null),
                ["Terrain"] = new JsonObject { ["Shape"] = null, ["Edges"] = null },
            },
            ["Bridges"] = null,
        });
        ProjectStore.Load(path, out var settings);
        var g = settings.Generator!;
        Assert.NotNull(g.Towns.Rows);
        Assert.Empty(g.Sites);
        Assert.Empty(g.PlacedRoads);
        Assert.NotNull(g.Terrain.Shape);
        Assert.Equal(4, g.Terrain.Edges.Length);
        Assert.Empty(settings.Bridges);
    }

    [Fact]
    public void A_project_prefab_without_a_name_is_refused_and_a_missing_project_name_is_filled()
    {
        string noName = CraftedProject(j => j["Prefabs"] = new JsonArray(new JsonObject { ["Name"] = null, ["X"] = 1, ["Y"] = 2, ["Z"] = 3 }));
        Assert.Throws<InvalidDataException>(() => ProjectStore.Load(noName));
        Assert.Equal("p", ProjectStore.Load(CraftedProject(j => j["Name"] = null)).Name);
    }

    [Fact]
    public void Saving_a_project_refuses_an_extra_file_it_could_not_open_again()
    {
        var world = MakeWorld();
        world.ExtraFiles["../escaped.txt"] = [1];
        string path = Path.Combine(_dir, "bad" + ProjectStore.Extension);
        Assert.Throws<InvalidOperationException>(() => ProjectStore.Save(world, path));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Radiation_painted_just_off_the_map_paints_nothing()
    {
        var world = MakeWorld();
        // A small brush 10 m west of the map: the cell under it is outside, not cell 0.
        var rect = LayerPaint.PaintRadiation(world, -10, 40, new BrushTip(1), erase: false, new EditSession("r"));
        Assert.Equal(GridRect.Empty, rect);
        Assert.All(world.Radiation!.Data, v => Assert.Equal(0, v));
    }

    [Fact]
    public void A_road_change_deeper_than_127_m_is_undone_exactly()
    {
        var world = MakeWorld(metres: 200);
        var before = (ushort[])world.Height.Data.Clone();
        byte[] ground = HighwayStage.Capture(world, [new GridRect(10, 10, 20, 20)], () =>
        {
            for (int z = 10; z < 20; z++)
                for (int x = 10; x < 20; x++) world.Height[x, z] = World.MetresToHeight(x < 15 ? 5 : 199);
        });
        HighwayStage.Restore(world, ground, new EditSession("take down"));
        Assert.Equal(before, world.Height.Data);
    }

    [Fact]
    public void A_dab_that_changes_nothing_leaves_no_undo_step()
    {
        var world = MakeWorld();
        var undo = new UndoStack();
        var runner = new EditRunner(undo);
        // Flattening flat ground at its own height touches the cells but changes none.
        runner.Run(world, "Flatten", s => TerrainBrush.Dab(world.Height, 30, 30, new BrushSettings(BrushMode.Flatten, 10, 1, TargetHeight: 40), s));
        Assert.False(undo.CanUndo);
        runner.Run(world, "Raise", s => TerrainBrush.Dab(world.Height, 30, 30, new BrushSettings(BrushMode.Raise, 10, 1), s));
        Assert.True(undo.CanUndo);
    }

    [Fact]
    public void A_tts_claiming_billions_of_blocks_is_not_drawn()
    {
        string tts = Path.Combine(_dir, "huge.tts");
        using (var w = new BinaryWriter(File.Create(tts)))
        {
            w.Write(0x00737474u);
            w.Write(18u);
            w.Write((short)30000);
            w.Write((short)30000);
            w.Write((short)30000);
        }
        using (var w = new BinaryWriter(File.Create(Path.Combine(_dir, "huge.blocks.nim"))))
        {
            w.Write(1);
            w.Write(-5);
        }
        Assert.Null(PrefabTopViewReader.Read(tts, 0));
    }

    [Fact]
    public void A_town_without_tiles_has_no_centre_and_says_so()
    {
        var e = Assert.Throws<ArgumentException>(() => TownGenerator.CentreTile([]));
        Assert.Contains("at least one street tile", e.Message);
    }
}
