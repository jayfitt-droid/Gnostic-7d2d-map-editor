using System.Text.Json;
using System.Xml.Linq;
using MapEditor.Core.GameData;
using MapEditor.Core.Generate;

namespace MapEditor.Core.Tests;

public sealed class TownPresetTests
{
    private const string MixerXml = """
        <rwgmixer>
          <world>
            <property name="world_size" value="4096,4096"/>
            <property class="town"><property name="tiles" value="2,4"/><property name="count" value="1,3,5"/></property>
          </world>
          <world>
            <property name="world_size" value="8192,8192"/>
            <property class="town"><property name="tiles" value="9,13"/><property name="count" value="5,9,12"/></property>
            <property class="city"><property name="tiles" value="4,8"/><property name="count" value="0,2,4"/></property>
          </world>
          <township name="town"/>
          <township name="city"/>
          <township name="roadside"/>
          <district name="downtown"><property name="spawn_weight" value="1"/><property name="required_township" value="town,city"/></district>
        </rwgmixer>
        """;

    // AppSettings stores the list with the standard System.Text.Json contract and indented output.
    private sealed class Settings
    {
        public List<TownPreset> TownPresets { get; set; } = [];
    }

    [Fact]
    public void Settings_json_round_trip_keeps_every_saved_field_and_leaves_the_seed_alone()
    {
        var source = new TownSiteOptions
        {
            Seed = 4823, EdgeMargin = 0.173f, Traders = TraderMode.OnePerTown, CrossBorders = true,
            Rows =
            [
                new() { Township = "city", Count = 5, MinTiles = 3, MaxTiles = 12, Distance = 7, Biomes = ["forest", "snow"] },
                new() { Township = "town", Count = 0, Distance = 0, Biomes = [] },
                new() { Township = "roadside" },
            ],
        };
        var settings = new Settings { TownPresets = [TownPreset.From(source, "Small map")] };
        string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        Assert.DoesNotContain("\"Seed\"", json, StringComparison.OrdinalIgnoreCase);
        var saved = Assert.Single(JsonSerializer.Deserialize<Settings>(json)!.TownPresets);
        var target = new TownSiteOptions { Seed = 9102 };

        saved.ApplyTo(target, RwgMixer.Empty);

        Assert.Equal("Small map", saved.Name);
        Assert.Equal(9102, target.Seed);
        Assert.Equal(source.EdgeMargin, target.EdgeMargin);
        Assert.Equal(source.Traders, target.Traders);
        Assert.Equal(source.CrossBorders, target.CrossBorders);
        Assert.Equal(JsonSerializer.Serialize(source.Rows), JsonSerializer.Serialize(target.Rows));
    }

    [Fact]
    public void Saving_and_applying_copy_the_rows_and_biome_lists()
    {
        var row = new TownRow { Township = "town", Count = 3, Biomes = ["forest"] };
        var source = new TownSiteOptions { Rows = [row] };
        var saved = TownPreset.From(source, "Mine");
        row.Count = 9;
        row.Biomes.Add("snow");
        source.Rows.Clear();

        var target = new TownSiteOptions();
        saved.ApplyTo(target);
        var applied = Assert.Single(target.Rows);
        Assert.Equal(3, applied.Count);
        Assert.Equal(["forest"], applied.Biomes);
        applied.Count = 12;
        applied.Biomes!.Add("desert");
        target.Rows.Clear();

        var savedRow = Assert.Single(saved.Rows);
        Assert.Equal(3, savedRow.Count);
        Assert.Equal(["forest"], savedRow.Biomes);
    }

    [Fact]
    public void Applying_at_a_different_size_keeps_saved_counts_tile_ranges_and_spacing()
    {
        var smallMixer = RwgMixer.Parse(XDocument.Parse(MixerXml), 4096);
        var largeMixer = RwgMixer.Parse(XDocument.Parse(MixerXml), 8192);
        var source = new TownSiteOptions { Rows = TownSiteStage.DefaultRows(smallMixer), EdgeMargin = 0.19f };
        source.Rows.Single(r => r.Township == "town").Distance = 7;
        var target = new TownSiteOptions { Rows = TownSiteStage.DefaultRows(largeMixer) };
        Assert.Equal(9, target.Rows.Single(r => r.Township == "town").Count);

        TownPreset.From(source, "4 km").ApplyTo(target, largeMixer);

        var town = target.Rows.Single(r => r.Township == "town");
        Assert.Equal((3, 2, 4, 7), (town.Count, town.MinTiles, town.MaxTiles, town.Distance));
        Assert.Equal(0.19f, target.EdgeMargin);
    }

    [Fact]
    public void Applying_skips_unknown_types_and_keeps_or_fills_missing_types()
    {
        var mixer = RwgMixer.Parse(XDocument.Parse(MixerXml), 8192);
        var saved = TownPreset.From(new TownSiteOptions
        {
            Rows = [new() { Township = "removed" }, new() { Township = "TOWN", Count = 5 }],
        }, "Other install");
        var roadside = new TownRow { Township = "ROADSIDE", Count = 11, Biomes = ["snow"] };
        var target = new TownSiteOptions { Rows = [roadside, new() { Township = "removed" }] };

        saved.ApplyTo(target, mixer);

        Assert.Equal(["TOWN", "city", "ROADSIDE"], target.Rows.Select(r => r.Township));
        Assert.Equal(5, target.Rows[0].Count);
        var city = target.Rows[1];
        Assert.Equal((2, 4, 8), (city.Count, city.MinTiles, city.MaxTiles));
        Assert.Equal(11, target.Rows[2].Count);
        Assert.NotSame(roadside, target.Rows[2]);
        Assert.Equal(["snow"], target.Rows[2].Biomes);
        Assert.NotSame(roadside.Biomes, target.Rows[2].Biomes);
    }

    [Theory]
    [InlineData("Small towns (4 km)", 2, 0)]
    [InlineData("Mixed towns (6 km)", 3, 1)]
    [InlineData("More cities (8 km)", 4, 3)]
    public void Starter_presets_set_fixed_counts_for_each_town_type(string name, int towns, int cities)
    {
        var preset = StarterTownPresets.Create().Single(p => p.Name == name);
        var mixer = RwgMixer.Parse(XDocument.Parse(MixerXml), 8192);
        var target = new TownSiteOptions();

        preset.ApplyTo(target, mixer);

        Assert.Equal(GameNames.TownshipNames.Keys.Order(), preset.Rows.Select(r => r.Township).Order());
        Assert.Equal(towns, target.Rows.Single(r => r.Township == "town").Count);
        Assert.Equal(cities, target.Rows.Single(r => r.Township == "city").Count);
    }
}
