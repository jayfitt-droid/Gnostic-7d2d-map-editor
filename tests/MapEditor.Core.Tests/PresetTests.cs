using System.Text.Json;
using System.Xml.Linq;
using MapEditor.Core.GameData;
using MapEditor.Core.Generate;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Tests;

public sealed class PresetTests
{
    private static readonly RwgMixer Mixer = RwgMixer.Parse(XDocument.Parse("""
        <rwgmixer>
          <world>
            <property name="world_size" value="0,99999"/>
            <property class="town"><property name="tiles" value="4,6"/><property name="count" value="1, 3, 5"/></property>
            <property class="roadside"><property name="tiles" value="1,1"/><property name="count" value="2, 4, 6"/></property>
            <property class="lakes"><property name="count" value="1, 2, 3"/></property>
            <property class="canyons"><property name="count" value="2, 4, 6"/></property>
          </world>
          <township name="town"/>
          <township name="roadside"/>
          <district name="downtown"><property name="spawn_weight" value="1"/><property name="required_township" value="town"/></district>
        </rwgmixer>
        """), 4096);

    private static GeneratorSettings Using(string name)
    {
        var s = GeneratorSettings.ForNewWorld(5, [EdgeStyle.Land, EdgeStyle.Land, EdgeStyle.Land, EdgeStyle.Land], 35);
        s.Apply(BuiltInPresets.All.Single(p => p.Name == name), Mixer);
        return s;
    }

    private static TownRow Row(GeneratorSettings s, string township) => s.Towns.Rows.Single(r => r.Township == township);
    private static int Count(GeneratorSettings s, FeatureKind kind) => s.Features.Rows.Single(r => r.Kind == kind).Count;

    [Fact]
    public void Built_in_presets_scale_the_games_numbers()
    {
        var game = Using("Game defaults");
        Assert.Equal((3, 6), (Row(game, "town").Count, Row(game, "town").MaxTiles));
        Assert.Equal(4, Row(game, "roadside").Count);
        Assert.Equal(2, Count(game, FeatureKind.Lake));

        var big = Using("Big cities");
        Assert.Equal((2, 12), (Row(big, "town").Count, Row(big, "town").MaxTiles));
        Assert.Equal((4, 1), (Row(big, "roadside").Count, Row(big, "roadside").MaxTiles));

        var frontier = Using("Frontier");
        Assert.Equal(6, Row(frontier, "roadside").Count);
        Assert.Equal((2, 1.5f, 0.5f), (frontier.Wilderness.Amount, frontier.Wilderness.CountScale, frontier.Wilderness.TierBias));

        Assert.Equal(4, Count(Using("Waterways"), FeatureKind.Lake));
        Assert.Equal(8, Count(Using("Rugged"), FeatureKind.Canyon));
        Assert.Equal(2, Count(Using("Flatlands"), FeatureKind.Canyon));
        Assert.Equal(new TerrainWeights(7, 3, 0), Using("Flatlands").Terrain.Weights);
        Assert.True(Using("Border towns").Towns.CrossBorders);
        Assert.Equal(5, Using("Easy start").Spawns.Biomes.Count);
    }

    [Fact]
    public void Using_a_preset_twice_does_not_compound_and_keeps_the_seeds()
    {
        var s = Using("Big cities");
        int seed = s.Towns.Seed;
        var big = BuiltInPresets.All.Single(p => p.Name == "Big cities");
        s.Apply(big, Mixer);
        Assert.Equal(2, Row(s, "town").Count);
        Assert.Equal(seed, s.Towns.Seed);
        // A preset saved from it is plain settings; the built-in parts aren't stored.
        var saved = JsonSerializer.Deserialize<GeneratorPreset>(JsonSerializer.Serialize(s.ToPreset("Mine")))!;
        Assert.False(saved.BuiltIn);
        Assert.Null(saved.Adjust);
        Assert.Equal(12, saved.Towns.Rows.Single(r => r.Township == "town").MaxTiles);
        Assert.True(BuiltInPresets.IsBuiltIn(" rugged "));
        Assert.False(BuiltInPresets.IsBuiltIn("Mine"));
    }
}
