using System.IO.Compression;
using System.Numerics;
using System.Text.Json.Nodes;
using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Generate;
using MapEditor.Core.IO;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;
using MapEditor.Core.Validate;

namespace MapEditor.Core.Tests;

/// <summary>The trader the game's opening quest sends new players to, the warning about it, and stage 6's option.</summary>
public sealed class StarterTraderTests : IDisposable
{
    private const int Size = 3000;
    private readonly string _dir = Directory.CreateTempSubdirectory("mapeditor-starter-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static PrefabInfo Poi(string name, int size, int tier, params string[] questTags) =>
        new(name, "POIs", new Int3(size, 20, size), -1, 0, tier, [], null) { QuestTags = questTags, DuplicateRepeatDistance = 0 };

    private static readonly PrefabLibrary Library = PrefabLibrary.Of(
    [
        Poi("trader_rekt", 60, 0, "trader", "traderRekt") with { TraderArea = true },
        Poi("house_t1", 20, 1, "clear"),
        Poi("factory_t3", 20, 3, "clear"),
        // Tier 1 but no quest tags: the game doesn't list it as a POI.
        Poi("shed_untagged", 20, 1),
    ]);

    /// <summary>Flat land, forest in the west two thirds, desert in the east third (x 2000 on).</summary>
    private static World Land()
    {
        var world = new World { Name = "T", Size = Size, Height = new Grid<ushort>(Size, Size), Water = new Grid<byte>(Size, Size) };
        Array.Fill(world.Height.Data, World.MetresToHeight(40));
        var biomes = new Grid<byte>(Size / 4, Size / 4);
        for (int z = 0; z < biomes.Height; z++)
            for (int x = biomes.Width * 2 / 3; x < biomes.Width; x++)
                biomes[x, z] = 1;
        world.Biomes = new IndexedLayer(biomes, [BiomeLayout.ForestColor, BiomeLayout.DesertColor]);
        return world;
    }

    private static PrefabInstance Put(World world, string name, int gx, int gz)
    {
        var p = new PrefabInstance { Name = name, Position = new Int3(world.GridToWorld(gx), 40, world.GridToWorld(gz)) };
        world.Prefabs.Add(p);
        return p;
    }

    private static void Spawn(World world, int gx, int gz) =>
        world.Spawns.Add(new SpawnPoint { X = world.GridToWorld(gx), Y = 41, Z = world.GridToWorld(gz) });

    /// <summary>
    /// Trader A by the south-west spawns, ringed by hard POIs and one tier-1 house; trader B far to the north with two
    /// tier-1 houses (and a third 600 m off, out of reach); trader C in the desert with three.
    /// </summary>
    private static (World World, PrefabInstance A, PrefabInstance B) TwoForestTraders()
    {
        var world = Land();
        var a = Put(world, "trader_rekt", 300, 300);
        Put(world, "house_t1", 500, 300);
        Put(world, "factory_t3", 300, 500);
        Put(world, "factory_t3", 450, 450);
        Put(world, "shed_untagged", 350, 400);
        Put(world, "shed_untagged", 400, 350);
        var b = Put(world, "trader_rekt", 1500, 2700);
        Put(world, "house_t1", 1700, 2700);
        Put(world, "house_t1", 1500, 2500);
        Put(world, "house_t1", 2100, 2700);
        var c = Put(world, "trader_rekt", 2600, 300);
        Put(world, "house_t1", 2700, 300);
        Put(world, "house_t1", 2600, 400);
        Put(world, "house_t1", 2500, 300);
        return (world, a, b);
    }

    [Fact]
    public void The_quest_picks_the_pine_forest_trader_with_most_tier1_pois_not_the_nearest()
    {
        var (world, a, b) = TwoForestTraders();
        var candidates = StarterTrader.Candidates(world, Library, BiomeTable.Vanilla);

        // The desert trader isn't one; untagged sheds and hard POIs don't count; 600 m is out of reach.
        Assert.Equal([a, b], candidates.Select(c => c.Trader));
        Assert.Equal(1, candidates[0].Tier1);
        Assert.Equal(2, candidates[1].Tier1);
        Assert.Same(b, StarterTrader.Pick(candidates)!.Trader);
    }

    [Fact]
    public void A_tie_goes_to_the_first_trader_and_none_with_tier1_pois_finds_none()
    {
        var world = Land();
        var first = Put(world, "trader_rekt", 300, 300);
        Put(world, "house_t1", 500, 300);
        Put(world, "trader_rekt", 1500, 1500);
        Put(world, "house_t1", 1500, 1700);
        Assert.Same(first, StarterTrader.Find(world, Library, BiomeTable.Vanilla)!.Trader);

        var bare = Land();
        Put(bare, "trader_rekt", 300, 300);
        Put(bare, "factory_t3", 500, 300);
        Assert.Null(StarterTrader.Find(bare, Library, BiomeTable.Vanilla));
    }

    [Fact]
    public void Validation_warns_when_a_spawn_is_far_from_the_quests_trader_and_names_the_nearer_one()
    {
        var (world, _, _) = TwoForestTraders();
        Spawn(world, 400, 200);
        var warning = Assert.Single(WorldValidator.Validate(world, Library), i => i.Message.Contains("opening quest"));
        Assert.Equal(IssueSeverity.Warning, warning.Severity);
        Assert.Contains("has 1", warning.Message);
        Assert.Equal((400, 200), (warning.X, warning.Z));

        // Spawns near the picked trader: no warning.
        world.Spawns.Clear();
        Spawn(world, 1500, 2600);
        Assert.DoesNotContain(WorldValidator.Validate(world, Library), i => i.Message.Contains("opening quest"));
    }

    [Fact]
    public void Validation_warns_when_the_quest_finds_no_trader()
    {
        var world = Land();
        Put(world, "trader_rekt", 300, 300);
        Spawn(world, 400, 200);
        Assert.Contains(WorldValidator.Validate(world, Library), i => i.Message.StartsWith("The opening quest finds no trader"));

        // A world with no traders at all (an imported or bare one) isn't warned about.
        var bare = Land();
        Spawn(bare, 400, 200);
        Assert.DoesNotContain(WorldValidator.Validate(bare, Library), i => i.Message.Contains("opening quest"));
    }

    private static SpawnResult RunSpawns(World world, SpawnOptions options) =>
        SpawnStage.Run(world, options, [], Library, BiomeTable.Vanilla, [], [], new EditSession("Spawns", world));

    [Fact]
    public void Stage_6_puts_spawns_by_the_quests_trader_when_ticked()
    {
        var (world, _, b) = TwoForestTraders();
        var bCentre = new Vector2(1530, 2730);
        var result = RunSpawns(world, new SpawnOptions { Min = 4 });
        Assert.Equal(4, result.TopUp);
        Assert.Contains(result.Notes, n => n.Contains("opening quest"));
        Assert.All(world.Spawns, s => Assert.True(
            Vector2.Distance(new(world.WorldToGrid((int)s.X), world.WorldToGrid((int)s.Z)), bCentre) < 400,
            $"spawn at ({s.X}, {s.Z}) is not by {b.Name} at the north"));

        // Unticked: the game's way, nearest any trader, so the south-west trader gets them.
        var (plain, _, _) = TwoForestTraders();
        RunSpawns(plain, new SpawnOptions { Min = 4, NearStarterTrader = false });
        Assert.Contains(plain.Spawns, s => Vector2.Distance(new(plain.WorldToGrid((int)s.X), plain.WorldToGrid((int)s.Z)), bCentre) > 1000);
    }

    [Fact]
    public void By_the_quests_trader_no_spawn_goes_as_far_as_the_warning()
    {
        var (world, _, _) = TwoForestTraders();
        var bCentre = new Vector2(1530, 2730);
        var result = RunSpawns(world, new SpawnOptions { Min = 500, TraderDistance = 5000 });

        Assert.NotEmpty(world.Spawns);
        Assert.All(world.Spawns, s => Assert.True(
            Vector2.Distance(new(world.WorldToGrid((int)s.X), world.WorldToGrid((int)s.Z)), bCentre) < StarterTrader.FarSpawn));
        Assert.Contains(result.Notes, n => n.Contains($"within {StarterTrader.FarSpawn:0} m of the opening quest's trader"));
        Assert.DoesNotContain(WorldValidator.Validate(world, Library), i => i.Message.Contains("opening quest"));
    }

    [Fact]
    public void Projects_from_before_format_4_load_with_the_option_off_and_new_ones_with_it_on()
    {
        var world = Land();
        string path = Path.Combine(_dir, "p" + ProjectStore.Extension);
        ProjectStore.Save(world, path, new ProjectStore.ProjectSettings { Generator = new GeneratorSettings() });
        ProjectStore.Load(path, out var fresh);
        Assert.True(fresh.Generator!.Spawns.NearStarterTrader);

        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry("project.json")!;
            JsonObject json;
            using (var s = entry.Open()) json = JsonNode.Parse(s)!.AsObject();
            json["Format"] = 3;
            json["Settings"]!["Generator"]!["Spawns"]!.AsObject().Remove("NearStarterTrader");
            entry.Delete();
            using var w = new StreamWriter(zip.CreateEntry("project.json").Open());
            w.Write(json.ToJsonString());
        }
        ProjectStore.Load(path, out var old);
        Assert.False(old.Generator!.Spawns.NearStarterTrader);
    }

    [Fact]
    public void A_prefab_sidecar_gives_its_quest_tags()
    {
        string xml = Path.Combine(_dir, "trader_test.xml");
        File.WriteAllText(xml, """
            <prefab>
              <property name="PrefabSize" value="40, 20, 40" />
              <property name="TraderArea" value="True" />
              <property name="QuestTags" value="trader, traderRekt" />
            </prefab>
            """);
        var info = PrefabLibrary.TryRead(xml)!;
        Assert.Equal(["trader", "traderRekt"], info.QuestTags);
        Assert.True(info.IsQuestPoi);
        Assert.True(info.HasQuestTag("TRADER"));
    }
}
