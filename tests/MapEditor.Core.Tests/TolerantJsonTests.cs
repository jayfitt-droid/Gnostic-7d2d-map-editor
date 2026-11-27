using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MapEditor.Core.GameLink;
using MapEditor.Core.Generate;
using MapEditor.Core.IO;

namespace MapEditor.Core.Tests;

/// <summary>A hand edit with one wrong value in settings.json must not reset all the settings (#246).</summary>
public sealed class TolerantJsonTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mapeditor-json-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    // The same kinds of values as AppSettings: text, numbers, flags, a nullable enum stored as a number, an enum stored
    // by name, a nested object, lists of text and of presets.
    private sealed class Model
    {
        public string? GameFolder { get; set; }
        public int RebuildRadius { get; set; } = 64;
        public double SidePanelWidth { get; set; }
        public bool Autosave { get; set; } = true;
        public AfterExportAction? AfterExport { get; set; }

        [JsonConverter(typeof(JsonStringEnumConverter<TraderMode>))]
        public TraderMode ByName { get; set; } = TraderMode.OnePerTown;

        public Window Window { get; set; } = new();
        public List<string> RecentFiles { get; set; } = [];
        public List<TownPreset> TownPresets { get; set; } = [];
        public Dictionary<string, int> Counts { get; set; } = [];
    }

    private sealed class Window
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public bool Maximized { get; set; }
    }

    private static Model Full() => new()
    {
        GameFolder = @"D:\Games\7 Days To Die",
        RebuildRadius = 200,
        SidePanelWidth = 312.5,
        Autosave = false,
        AfterExport = AfterExportAction.Rebuild,
        ByName = TraderMode.RwgCaps,
        Window = new Window { Left = -1200, Top = 40, Maximized = true },
        RecentFiles = [@"C:\Maps\One.7dmap", @"C:\Maps\Two.7dmap"],
        TownPresets =
        [
            new TownPreset { Name = "First", EdgeMargin = 0.2f, Traders = TraderMode.OnePerTown, Rows = [new TownRow { Township = "town", Count = 3 }] },
            new TownPreset { Name = "Second", CrossBorders = true },
            new TownPreset { Name = "Third", EdgeMargin = 0.05f },
        ],
        Counts = new() { ["a"] = 1, ["b"] = 2 },
    };

    private static JsonObject FullJson() => JsonSerializer.SerializeToNode(Full())!.AsObject();

    private static (Model Model, List<string> Resets) Read(JsonNode json)
    {
        var resets = new List<string>();
        var model = TolerantJson.Read<Model>(json.ToJsonString(), resets)!;
        return (model, resets);
    }

    private static string Text(object value) => JsonSerializer.Serialize(value);

    [Fact]
    public void A_file_with_no_bad_values_reads_as_the_serializer_reads_it()
    {
        var (model, resets) = Read(FullJson());
        Assert.Empty(resets);
        Assert.Equal(Text(Full()), Text(model));
    }

    [Theory]
    [InlineData("\"128\"")]
    [InlineData("128.5")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("99999999999")]
    public void A_bad_number_uses_its_default_and_keeps_every_other_value(string bad)
    {
        var json = FullJson();
        json["RebuildRadius"] = JsonNode.Parse(bad);
        var (model, resets) = Read(json);

        Assert.Equal(new Model().RebuildRadius, model.RebuildRadius);
        var expected = Full();
        expected.RebuildRadius = new Model().RebuildRadius;
        Assert.Equal(Text(expected), Text(model));
        Assert.Contains("RebuildRadius", Assert.Single(resets));
    }

    [Fact]
    public void Text_for_an_enum_stored_as_a_number_uses_its_default()
    {
        var json = FullJson();
        json["AfterExport"] = "Rebuild";
        var (model, resets) = Read(json);

        Assert.Null(model.AfterExport);
        Assert.Equal(200, model.RebuildRadius);
        Assert.Equal(3, model.TownPresets.Count);
        Assert.Contains("AfterExport", Assert.Single(resets));
    }

    [Theory]
    [InlineData("AfterExport", "7")]
    [InlineData("ByName", "\"Nope\"")]
    [InlineData("ByName", "9")]
    public void An_enum_value_with_no_member_uses_its_default(string property, string bad)
    {
        var json = FullJson();
        json[property] = JsonNode.Parse(bad);
        var (model, resets) = Read(json);

        var expected = Full();
        if (property == "AfterExport") expected.AfterExport = null;
        else expected.ByName = new Model().ByName;
        Assert.Equal(Text(expected), Text(model));
        Assert.Contains(property, Assert.Single(resets));
    }

    [Fact]
    public void A_bad_value_in_a_nested_object_keeps_the_object_and_its_other_values()
    {
        var json = FullJson();
        json["Window"]!["Left"] = "far left";
        var (model, resets) = Read(json);

        Assert.Equal(0, model.Window.Left);
        Assert.Equal(40, model.Window.Top);
        Assert.True(model.Window.Maximized);
        Assert.Equal(200, model.RebuildRadius);
        Assert.Contains("Window.Left", Assert.Single(resets));
    }

    [Fact]
    public void A_nested_object_of_the_wrong_kind_uses_its_default()
    {
        var json = FullJson();
        json["Window"] = 5;
        var (model, resets) = Read(json);

        Assert.Equal(Text(new Window()), Text(model.Window));
        Assert.Equal(Full().RecentFiles, model.RecentFiles);
        Assert.Single(resets);
    }

    [Fact]
    public void A_list_item_that_cant_be_read_is_left_out_and_the_others_are_kept()
    {
        var json = FullJson();
        json["TownPresets"]![1] = "not a preset";
        json["RecentFiles"]!.AsArray().Insert(1, 42);
        var (model, resets) = Read(json);

        Assert.Equal(["First", "Third"], model.TownPresets.Select(p => p.Name));
        Assert.Equal(Full().RecentFiles, model.RecentFiles);
        Assert.Equal(2, resets.Count);
        Assert.Contains(resets, r => r.StartsWith("TownPresets[1] ", StringComparison.Ordinal));
        Assert.Contains(resets, r => r.StartsWith("RecentFiles[1] ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_bad_value_in_a_list_item_keeps_the_item_with_that_value_reset()
    {
        var json = FullJson();
        json["TownPresets"]![0]!["EdgeMargin"] = "wide";
        json["TownPresets"]![0]!["Traders"] = 5;
        json["TownPresets"]![0]!["Rows"]![0]!["Count"] = 2.5;
        var (model, resets) = Read(json);

        Assert.Equal(["First", "Second", "Third"], model.TownPresets.Select(p => p.Name));
        var first = model.TownPresets[0];
        Assert.Equal(new TownPreset().EdgeMargin, first.EdgeMargin);
        Assert.Equal(new TownPreset().Traders, first.Traders);
        Assert.Equal("town", Assert.Single(first.Rows).Township);
        Assert.Equal(0, first.Rows[0].Count);
        Assert.Equal(3, resets.Count);
        Assert.Contains(resets, r => r.StartsWith("TownPresets[0].Rows[0].Count ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_bad_dictionary_value_is_left_out()
    {
        var json = FullJson();
        json["Counts"]!["a"] = "one";
        var (model, resets) = Read(json);

        Assert.Equal(new Dictionary<string, int> { ["b"] = 2 }, model.Counts);
        Assert.Single(resets);
    }

    [Fact]
    public void A_name_given_twice_keeps_its_last_value_as_the_serializer_does()
    {
        const string json = """{ "RebuildRadius": 100, "Autosave": false, "RebuildRadius": 150 }""";
        var resets = new List<string>();
        var model = TolerantJson.Read<Model>(json, resets)!;

        Assert.Empty(resets);
        Assert.Equal(Text(JsonSerializer.Deserialize<Model>(json)!), Text(model));
        Assert.Equal(150, model.RebuildRadius);
    }

    [Theory]
    [MemberData(nameof(SaveCompatibilityTests.PresetSamples), MemberType = typeof(SaveCompatibilityTests))]
    public void Old_generate_presets_read_with_nothing_reset(string release)
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SaveSamples", release + "-preset.json"));
        var resets = new List<string>();
        var preset = TolerantJson.Read<GeneratorPreset>(json, resets)!;

        Assert.Empty(resets);
        Assert.Equal(Text(JsonSerializer.Deserialize<GeneratorPreset>(json)!), Text(preset));
    }

    [Theory]
    [InlineData("{ \"RebuildRadius\": 12, ")]
    [InlineData("not JSON at all")]
    [InlineData("")]
    [InlineData("[1, 2]")]
    public void A_file_that_cant_be_read_gives_defaults_and_keeps_a_copy(string text)
    {
        string path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, text);
        var notes = new List<string>();
        var model = TolerantJson.ReadFile<Model>(path, notes);

        Assert.Equal(Text(new Model()), Text(model));
        Assert.Equal(text, File.ReadAllText(path));
        string copy = Assert.Single(Directory.GetFiles(_dir, "settings.json.*.bad"));
        Assert.Equal(text, File.ReadAllText(copy));
        Assert.Contains(copy, Assert.Single(notes));
    }

    [Fact]
    public void A_file_with_a_bad_value_keeps_a_copy_and_notes_the_reset()
    {
        string path = Path.Combine(_dir, "settings.json");
        var json = FullJson();
        json["RebuildRadius"] = 128.5;
        File.WriteAllText(path, json.ToJsonString());
        var notes = new List<string>();
        var model = TolerantJson.ReadFile<Model>(path, notes);

        Assert.Equal(new Model().RebuildRadius, model.RebuildRadius);
        Assert.Equal(Full().RecentFiles, model.RecentFiles);
        Assert.Equal(2, notes.Count);
        Assert.Contains("RebuildRadius", notes[0]);
        string copy = Assert.Single(Directory.GetFiles(_dir, "settings.json.*.bad"));
        Assert.Equal(json.ToJsonString(), File.ReadAllText(copy));
    }

    [Fact]
    public void A_good_or_missing_file_keeps_no_copy_and_notes_nothing()
    {
        string path = Path.Combine(_dir, "settings.json");
        var notes = new List<string>();
        Assert.Equal(Text(new Model()), Text(TolerantJson.ReadFile<Model>(path, notes)));

        File.WriteAllText(path, FullJson().ToJsonString());
        Assert.Equal(Text(Full()), Text(TolerantJson.ReadFile<Model>(path, notes)));
        File.WriteAllText(path, "null");
        Assert.Equal(Text(new Model()), Text(TolerantJson.ReadFile<Model>(path, notes)));

        Assert.Empty(notes);
        Assert.Empty(Directory.GetFiles(_dir, "*.bad"));
    }
}
