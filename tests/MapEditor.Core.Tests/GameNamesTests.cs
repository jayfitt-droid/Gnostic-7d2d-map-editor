using MapEditor.Core.GameData;

namespace MapEditor.Core.Tests;

public class GameNamesTests
{
    private const string Csv =
        "Key,File,Type,UsedInMainMenu,NoTranslate,KeepLoaded,english,Context / Alternate Text,german\r\n" +
        "biome_pine_forest,biomes,biome,,,x,Pine Forest,,Kiefernwald\r\n" +
        "biome_burnt_forest,biomes,biome,,,x,Burnt Forest,,\r\n" +
        "trader_hugh,POI,POIName,,x,x,Trader Hugh,\"Refers to the building, not the person\",\r\n" +
        "oldwest_business_01,POI,POIName,,x,x,\"Biskitz, \"\"the\"\" store\",,\r\n" +
        "someitem,items,Item,,,,Not a POI,,\r\n";

    [Fact]
    public void ParseCsv_handles_quoted_commas_and_doubled_quotes()
    {
        var rows = GameNames.ParseCsv("a,\"b,c\",\"say \"\"hi\"\"\"\n1,2,3\n");
        Assert.Equal(2, rows.Count);
        Assert.Equal(["a", "b,c", "say \"hi\""], rows[0]);
        Assert.Equal(["1", "2", "3"], rows[1]);
    }

    [Fact]
    public void ParseCsv_keeps_line_breaks_inside_quotes()
    {
        var rows = GameNames.ParseCsv("a,\"x\r\ny\",c\r\nd,e,f");
        Assert.Equal(2, rows.Count);
        Assert.Equal("x\r\ny", rows[0][1]);
    }

    [Fact]
    public void Parse_finds_english_by_header_and_keys_by_type()
    {
        var names = GameNames.Parse(Csv);
        Assert.Equal("Pine Forest", names.Biome("pine_forest"));
        Assert.Equal("Burnt Forest", names.Biome("burnt_forest"));
        Assert.Equal("Trader Hugh", names.PoiGameName("trader_hugh"));
        Assert.Equal("Biskitz, \"the\" store", names.PoiGameName("oldwest_business_01"));
        Assert.Null(names.PoiGameName("someitem"));
    }

    [Fact]
    public void Parse_finds_english_when_columns_are_reordered()
    {
        var names = GameNames.Parse("english,Type,Key\nTrader Joel,POIName,trader_joel\n");
        Assert.Equal("Trader Joel", names.Poi("trader_joel"));
    }

    [Fact]
    public void Missing_names_are_tidied()
    {
        var names = GameNames.Parse(Csv);
        Assert.Equal("Wasteland", names.Biome("wasteland"));
        Assert.Equal("Rural farm 03", names.Poi("rural_farm_03"));
        Assert.Equal("Underwater", GameNames.Empty.Biome("underwater"));
        Assert.Null(GameNames.Empty.PoiGameName("trader_hugh"));
        Assert.Equal("", GameNames.Tidy(""));
    }

    [Fact]
    public void Rwg_biome_tags_use_the_game_names()
    {
        var names = GameNames.Parse(Csv);
        Assert.Equal("Pine Forest", names.Biome("forest"));
        Assert.Equal("Burnt Forest", names.Biome("burntforest"));
    }

    [Fact]
    public void Missing_file_gives_empty_names()
    {
        Assert.Same(GameNames.Empty, GameNames.Parse(""));
        Assert.Same(GameNames.Empty, GameNames.Parse("Key,File\nx,y\n"));
    }

    [Fact]
    public void Search_matches_file_name_or_friendly_name()
    {
        var names = GameNames.Parse(Csv);
        Assert.True(names.PoiMatches("oldwest_business_01", "biskitz"));
        Assert.True(names.PoiMatches("oldwest_business_01", "business"));
        Assert.False(names.PoiMatches("oldwest_business_01", "hugh"));
    }

    [Theory]
    [InlineData("oldwest", "Old West")]
    [InlineData("citybig", "Big city")]
    [InlineData("roadside", "Roadside stop")]
    [InlineData("some_mod_town", "Some mod town")]
    public void Township_names(string type, string expected) => Assert.Equal(expected, GameNames.Township(type));

    [Fact]
    public void Every_game_township_has_a_name()
    {
        foreach (var t in new[] { "oldwest", "forest_countrytown", "bforest_countrytown", "bforest_town", "countrytown", "town", "city", "wasteland_city", "citybig", "roadside" })
            Assert.True(GameNames.TownshipNames.ContainsKey(t), t);
    }
}
