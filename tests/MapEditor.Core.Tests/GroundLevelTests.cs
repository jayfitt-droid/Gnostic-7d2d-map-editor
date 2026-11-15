using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.IO;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;
using MapEditor.Core.Validate;

namespace MapEditor.Core.Tests;

/// <summary>
/// The game's own editor saves every prefab's Y at ground level with y_is_groundlevel="true" (reported 2026-10-31:
/// after a save in the game's editor, Review showed over 1,000 POI warnings on an unchanged map).
/// </summary>
public sealed class GroundLevelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mapeditor-groundlevel-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly PrefabInfo Poi = new("trader_joel", "POIs", new Int3(40, 20, 40), -2, 0, 0, ["trader"], null);
    private static readonly PrefabLibrary Library = PrefabLibrary.Of([Poi]);

    /// <summary>A flat 40 m world with an east-west road at 43 m along z = 300, and the POI placed against it.</summary>
    private static (World World, PrefabInstance Placed) WorldWithPoiByRoad()
    {
        var world = new World { Name = "T", Size = 600, Height = new Grid<ushort>(600, 600) };
        Array.Fill(world.Height.Data, World.MetresToHeight(40));
        world.Roads = new Grid<byte>(600, 600);
        for (int z = 295; z < 305; z++)
            for (int x = 0; x < 600; x++)
            {
                world.Roads[x, z] = (byte)RoadType.Asphalt;
                world.Height[x, z] = World.MetresToHeight(43);
            }
        var spot = PrefabPlacer.SpotAt(world, Library, Poi, 220, 326, 0, out _);
        var placed = PrefabPlacer.Place(world, Poi, spot, flatten: true, new EditSession("Place"));
        return (world, placed);
    }

    /// <summary>prefabs.xml as the game's editor writes it (DynamicPrefabDecorator.Save).</summary>
    private static string GameSaved(params (string Name, Int3 Box, int YOffset)[] prefabs) =>
        new XElement("prefabs", prefabs.Select(p => new XElement("decoration",
            new XAttribute("type", "model"), new XAttribute("name", p.Name),
            new XAttribute("position", (p.Box with { Y = p.Box.Y - p.YOffset }).ToString()),
            new XAttribute("rotation", "0"), new XAttribute(GroundLevel.Attribute, "true")))).ToString();

    private string WriteFile(string xml)
    {
        string path = Path.Combine(_dir, $"in-{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, xml);
        return path;
    }

    private static int RoadWarnings(World world) =>
        WorldValidator.Validate(world, Library).Count(i => i.Message.Contains("doesn't meet the road"));

    [Fact]
    public void A_game_saved_world_reviews_the_same_as_the_original()
    {
        var (world, placed) = WorldWithPoiByRoad();
        Assert.Equal(0, RoadWarnings(world));

        world.Prefabs.Clear();
        world.Prefabs.AddRange(WorldXml.ReadPrefabs(WriteFile(GameSaved((Poi.Name, placed.Position, Poi.YOffset)))));
        Assert.True(world.Prefabs[0].GroundLevel);
        Assert.Equal(1, RoadWarnings(world)); // the bug: ground-level Y read as box Y puts the POI 2 m high

        Assert.Equal(1, GroundLevel.Resolve(world, Library));
        Assert.Equal(placed.Position, world.Prefabs[0].Position);
        Assert.Equal(0, RoadWarnings(world));
        Assert.Equal(0, GroundLevel.Resolve(world, Library)); // safe to run again
        Assert.Equal(placed.Position, world.Prefabs[0].Position);
    }

    [Fact]
    public void An_unedited_game_saved_file_is_written_back_unchanged()
    {
        var box = new Int3(-80, 41, 26);
        string xml = GameSaved((Poi.Name, box, Poi.YOffset), ("not_in_the_library", new Int3(10, 50, 10), -5));
        var world = new World { Name = "T", Size = 600, Height = new Grid<ushort>(600, 600) };
        world.Prefabs.AddRange(WorldXml.ReadPrefabs(WriteFile(xml)));
        GroundLevel.Resolve(world, Library);
        Assert.Equal(box, world.Prefabs[0].Position);
        Assert.True(world.Prefabs[1].NeedsGroundLevelOffset);

        string output = Path.Combine(_dir, "out.xml");
        WorldXml.WritePrefabs(output, world.Prefabs, Library);
        var expected = XElement.Parse(xml).Elements().Select(e => e.ToString()).ToList();
        var actual = XDocument.Load(output).Root!.Elements().Select(e => e.ToString()).ToList();
        Assert.Equal(expected, actual);

        // Without the library (CLI export of a project) the YOffset it was converted with is used.
        WorldXml.WritePrefabs(output, world.Prefabs);
        Assert.Equal(expected, XDocument.Load(output).Root!.Elements().Select(e => e.ToString()).ToList());
    }

    [Fact]
    public void A_moved_ground_level_poi_is_written_at_its_new_height()
    {
        var (world, placed) = WorldWithPoiByRoad();
        world.Prefabs.Clear();
        world.Prefabs.AddRange(WorldXml.ReadPrefabs(WriteFile(GameSaved((Poi.Name, placed.Position, Poi.YOffset)))));
        GroundLevel.Resolve(world, Library);

        // Moved away from the road, onto the 40 m ground.
        var p = world.Prefabs[0];
        var spot = PrefabPlacer.SpotAt(world, Library, Poi, 220, 450, 0, out _);
        PrefabPlacer.Move(world, Library, p, spot, flatten: true, new EditSession("Move"));
        Assert.Equal(PrefabPlacer.PrefabY(Poi, 40), p.Position.Y);

        string output = Path.Combine(_dir, "out.xml");
        WorldXml.WritePrefabs(output, world.Prefabs, Library);
        var e = XDocument.Load(output).Root!.Element("decoration")!;
        Assert.Equal("true", (string?)e.Attribute(GroundLevel.Attribute));
        // The game adds the YOffset back, which lands the box where the editor put it.
        int fileY = int.Parse(((string)e.Attribute("position")!).Split(',')[1]);
        Assert.Equal(p.Position.Y, fileY + Poi.YOffset);
    }

    [Fact]
    public void Prefabs_placed_in_the_editor_are_written_at_ground_level()
    {
        var (world, placed) = WorldWithPoiByRoad();
        string output = Path.Combine(_dir, "out.xml");
        WorldXml.WritePrefabs(output, world.Prefabs, Library);
        var e = XDocument.Load(output).Root!.Element("decoration")!;
        Assert.Equal("true", (string?)e.Attribute(GroundLevel.Attribute));
        Assert.Equal((placed.Position with { Y = placed.Position.Y - Poi.YOffset }).ToString(), (string?)e.Attribute("position"));

        // Read back, it lands where it was placed.
        var copy = new World { Name = "T", Size = 600, Height = new Grid<ushort>(600, 600) };
        copy.Prefabs.AddRange(WorldXml.ReadPrefabs(output));
        GroundLevel.Resolve(copy, Library);
        Assert.Equal(placed.Position, copy.Prefabs[0].Position);
    }

    [Fact]
    public void An_rwg_prefab_the_library_knows_is_written_at_ground_level_and_one_it_doesnt_keeps_rwgs_form()
    {
        string xml = """
            <prefabs>
              <decoration type="model" name="trader_joel" position="5,42,6" rotation="1" />
              <decoration type="model" name="modded_poi" position="7,50,8" rotation="0" />
            </prefabs>
            """;
        var prefabs = WorldXml.ReadPrefabs(WriteFile(xml));
        Assert.Equal(["modded_poi"], GroundLevel.KeptInBoxForm(prefabs, Library).Select(p => p.Name));

        string output = Path.Combine(_dir, "out.xml");
        WorldXml.WritePrefabs(output, prefabs, Library);
        var e = XDocument.Load(output).Root!.Elements().ToList();
        Assert.Equal($"5,{42 - Poi.YOffset},6", (string?)e[0].Attribute("position"));
        Assert.Equal("true", (string?)e[0].Attribute(GroundLevel.Attribute));
        Assert.Equal("7,50,8", (string?)e[1].Attribute("position"));
        Assert.Null(e[1].Attribute(GroundLevel.Attribute));

        // Without a game folder nothing can be converted, so every RWG prefab keeps its form.
        Assert.Equal(2, GroundLevel.KeptInBoxForm(prefabs, null).Count);
    }

    [Fact]
    public void A_false_flag_on_a_known_prefab_becomes_one_true_flag()
    {
        string xml = """<prefabs><decoration type="model" name="trader_joel" position="1,40,2" rotation="0" y_is_groundlevel="false" help="x" /></prefabs>""";
        var prefabs = WorldXml.ReadPrefabs(WriteFile(xml));
        string output = Path.Combine(_dir, "out.xml");
        WorldXml.WritePrefabs(output, prefabs, Library);
        var e = XDocument.Load(output).Root!.Element("decoration")!;
        Assert.Equal("true", (string?)e.Attribute(GroundLevel.Attribute));
        Assert.Equal($"1,{40 - Poi.YOffset},2", (string?)e.Attribute("position"));
        Assert.Equal("x", (string?)e.Attribute("help"));
    }

    [Fact]
    public void A_false_flag_is_kept_as_it_was()
    {
        string xml = """<prefabs><decoration type="model" name="trader_joel" position="1,40,2" rotation="0" y_is_groundlevel="false" /></prefabs>""";
        var p = WorldXml.ReadPrefabs(WriteFile(xml)).Single();
        Assert.False(p.GroundLevel);
        Assert.Equal(40, p.Position.Y);
        Assert.Contains(p.ExtraAttributes, a => a.Key == GroundLevel.Attribute && a.Value == "false");
    }

    [Fact]
    public void Projects_keep_ground_level_prefabs()
    {
        var world = new World { Name = "P", Size = 512, Height = new Grid<ushort>(512, 512) };
        world.Prefabs.Add(new PrefabInstance { Name = "a", Position = new Int3(1, 39, 2), GroundLevel = true, GroundLevelOffset = -2 });
        world.Prefabs.Add(new PrefabInstance { Name = "b", Position = new Int3(3, 45, 4), GroundLevel = true });
        world.Prefabs.Add(new PrefabInstance { Name = "c", Position = new Int3(5, 41, 6) });
        string path = Path.Combine(_dir, "p" + ProjectStore.Extension);
        ProjectStore.Save(world, path);

        var loaded = ProjectStore.Load(path).Prefabs;
        Assert.Equal([(true, -2), (true, null), (false, null)],
            loaded.Select(p => (p.GroundLevel, p.GroundLevelOffset)).ToList<(bool, int?)>());
        Assert.Equal(world.Prefabs.Select(p => p.Position), loaded.Select(p => p.Position));
    }

    [Fact]
    public void A_format_1_project_reads_the_flag_as_unconverted()
    {
        // v1.2.0 kept the flag as a plain attribute and Y as the file had it.
        var world = new World { Name = "P", Size = 512, Height = new Grid<ushort>(512, 512) };
        var old = new PrefabInstance { Name = "trader_joel", Position = new Int3(1, 43, 2) };
        old.ExtraAttributes.Add(new(GroundLevel.Attribute, "true"));
        old.ExtraAttributes.Add(new("help", "x"));
        world.Prefabs.Add(old);
        string path = Path.Combine(_dir, "old" + ProjectStore.Extension);
        ProjectStore.Save(world, path);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry("project.json")!;
            JsonNode manifest;
            using (var s = entry.Open()) manifest = JsonNode.Parse(s)!;
            manifest["Format"] = 1;
            entry.Delete();
            using var w = new StreamWriter(zip.CreateEntry("project.json").Open());
            w.Write(manifest.ToJsonString());
        }

        var p = ProjectStore.Load(path).Prefabs.Single();
        Assert.True(p.NeedsGroundLevelOffset);
        Assert.Equal(43, p.Position.Y);
        Assert.Equal([new("help", "x")], p.ExtraAttributes);
    }
}
