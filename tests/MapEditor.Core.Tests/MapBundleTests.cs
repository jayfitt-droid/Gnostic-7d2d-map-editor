using System.IO.Compression;
using System.Xml.Linq;
using MapEditor.Core.GameData;
using MapEditor.Core.IO;

namespace MapEditor.Core.Tests;

public sealed class MapBundleTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mapeditor-bundle-").FullName;
    private readonly string _vanilla, _modPrefabs, _world;
    private readonly PrefabLibrary _library;

    public MapBundleTests()
    {
        _vanilla = Path.Combine(_dir, "game", "Data", "Prefabs");
        _modPrefabs = Path.Combine(_dir, "game", "Mods", "SomeMod", "Prefabs");
        Prefab(_vanilla, "vanilla_house", null);
        Prefab(_modPrefabs, "mod_tower", "mod_part");
        Prefab(_modPrefabs, "mod_part", null);
        // Extra files that share a base name, and one that doesn't.
        foreach (string ext in new[] { ".ins", ".mesh", ".blocks.nim", ".jpg" })
            File.WriteAllText(Path.Combine(_modPrefabs, "mod_tower" + ext), "x");
        File.WriteAllText(Path.Combine(_modPrefabs, "mod_towerX.tts"), "other");
        File.WriteAllText(Path.Combine(_modPrefabs, "mod_part.ins"), "x");
        _library = PrefabLibrary.Load([_vanilla, _modPrefabs]);

        _world = Path.Combine(_dir, "GeneratedWorlds", "Alpha");
        Directory.CreateDirectory(_world);
        File.WriteAllText(Path.Combine(_world, "prefabs.xml"), """
            <prefabs>
              <decoration type="model" name="vanilla_house" position="0,1,0" rotation="0" />
              <decoration type="model" name="mod_tower" position="10,1,0" rotation="1" />
              <decoration type="model" name="gone_prefab" position="20,1,0" rotation="0" />
            </prefabs>
            """);
        File.WriteAllText(Path.Combine(_world, "map_info.xml"), "<xml />");
        File.WriteAllBytes(Path.Combine(_world, "dtm.raw"), new byte[5000]);
        Directory.CreateDirectory(Path.Combine(_world, "sub"));
        File.WriteAllText(Path.Combine(_world, "sub", "extra.txt"), "e");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static void Prefab(string folder, string name, string? part)
    {
        Directory.CreateDirectory(folder);
        string markers = part is null ? "" : $"""
            <property name="POIMarkerStart" value="0,0,0" />
            <property name="POIMarkerSize" value="4,4,4" />
            <property name="POIMarkerType" value="PartSpawn" />
            <property name="POIMarkerPartToSpawn" value="{part}" />
            """;
        File.WriteAllText(Path.Combine(folder, name + ".xml"),
            $"""<prefab><property name="PrefabSize" value="10, 10, 10" />{markers}</prefab>""");
        File.WriteAllBytes(Path.Combine(folder, name + ".tts"), [1, 2, 3]);
    }

    private static BundleInfo Info(string name = "My-Bundle", string author = "A & B \"C\"") =>
        new(name, "My <Bundle>", "Adds maps", author, "1.0.0", "https://example.com/?a=1&b=2", true);

    [Fact]
    public void Plan_skips_vanilla_follows_parts_and_reports_missing()
    {
        var plan = MapBundle.Plan([_world], _library, _vanilla);
        Assert.Equal(["mod_part", "mod_tower"], plan.Prefabs.Select(p => p.Name));
        Assert.Equal(["gone_prefab"], plan.Missing);
        Assert.Equal(1, plan.VanillaCount);
        Assert.True(plan.Prefabs.Single(p => p.Name == "mod_part").IsPart);
        var tower = plan.Prefabs.Single(p => p.Name == "mod_tower");
        Assert.Equal(["mod_tower.blocks.nim", "mod_tower.ins", "mod_tower.jpg", "mod_tower.mesh", "mod_tower.tts", "mod_tower.xml"],
            tower.Files.Select(Path.GetFileName));
    }

    [Fact]
    public void Write_makes_the_bundle_layout()
    {
        var plan = MapBundle.Plan([_world], _library, _vanilla);
        string zip = Path.Combine(_dir, "out", "bundle.zip");
        BundleProgress last = default;
        MapBundle.Write(zip, Info(), [_world], plan, null, p => last = p, CancellationToken.None);

        using var archive = ZipFile.OpenRead(zip);
        var names = archive.Entries.Select(e => e.FullName).ToHashSet();
        string root = "My-Bundle/";
        Assert.All(names, n => Assert.StartsWith(root, n));
        Assert.Contains(root + "ModInfo.xml", names);
        foreach (string f in new[] { "prefabs.xml", "map_info.xml", "dtm.raw", "sub/extra.txt" })
            Assert.Contains(root + "Worlds/Alpha/" + f, names);
        foreach (string f in new[] { "mod_tower.tts", "mod_tower.xml", "mod_tower.ins", "mod_tower.mesh", "mod_tower.blocks.nim", "mod_tower.jpg",
                     "mod_part.tts", "mod_part.xml", "mod_part.ins" })
            Assert.Contains(root + "Prefabs/POIs/" + f, names);
        Assert.DoesNotContain(names, n => n.Contains("vanilla_house"));
        Assert.DoesNotContain(names, n => n.Contains("mod_towerX"));
        Assert.DoesNotContain(names, n => n.Contains('\\'));
        Assert.DoesNotContain(names, n => n.Contains("Config"));
        Assert.Equal(last.BytesTotal, last.BytesDone);
        Assert.False(File.Exists(zip + ".partial"));
    }

    [Fact]
    public void The_games_own_cache_files_stay_out_of_the_bundle()
    {
        foreach (string f in new[] { "checksums.txt", "dtm_processed.raw", "splat3_processed.png", "splat3_half.png", "splat4_half.png" })
            File.WriteAllBytes(Path.Combine(_world, f), new byte[100_000]);
        File.WriteAllBytes(Path.Combine(_world, "splat3.png"), new byte[10]);
        var plan = MapBundle.Plan([_world], _library, _vanilla);
        string zip = Path.Combine(_dir, "out", "bundle.zip");
        MapBundle.Write(zip, Info(), [_world], plan, null, null, CancellationToken.None);

        using var archive = ZipFile.OpenRead(zip);
        var names = archive.Entries.Select(e => e.FullName).Where(n => n.Contains("/Worlds/Alpha/")).Select(n => n[(n.LastIndexOf('/') + 1)..]).ToList();
        Assert.Contains("splat3.png", names);
        Assert.Contains("dtm.raw", names);
        Assert.DoesNotContain(names, n => n is "checksums.txt" || n.Contains("_processed") || n.Contains("_half"));
        // The size the list shows is what the bundle gets.
        Assert.True(MapBundle.ListWorlds(Path.GetDirectoryName(_world)!).Single().Bytes < 100_000);
    }

    [Fact]
    public void Files_an_interrupted_export_left_stay_out_of_the_bundle()
    {
        long before = MapBundle.ListWorlds(Path.GetDirectoryName(_world)!).Single().Bytes;
        foreach (string dir in new[] { WorldWriter.StagingDir, WorldWriter.BackupDir })
        {
            Directory.CreateDirectory(Path.Combine(_world, dir, "nested"));
            File.WriteAllBytes(Path.Combine(_world, dir, "dtm.raw"), new byte[100_000]);
            File.WriteAllBytes(Path.Combine(_world, dir, "nested", "biomes.png"), new byte[100_000]);
        }
        var plan = MapBundle.Plan([_world], _library, _vanilla);
        string zip = Path.Combine(_dir, "out", "bundle.zip");
        MapBundle.Write(zip, Info(), [_world], plan, null, null, CancellationToken.None);

        using var archive = ZipFile.OpenRead(zip);
        var names = archive.Entries.Select(e => e.FullName).ToList();
        Assert.DoesNotContain(names, n => n.Contains(".mapeditor-"));
        // An ordinary sub-folder still goes in.
        Assert.Contains("My-Bundle/Worlds/Alpha/sub/extra.txt", names);
        Assert.Equal(1, names.Count(n => n.EndsWith("/dtm.raw")));
        // The size the list shows doesn't count them either.
        Assert.Equal(before, MapBundle.ListWorlds(Path.GetDirectoryName(_world)!).Single().Bytes);
    }

    [Fact]
    public void Config_folder_goes_into_Config()
    {
        string config = Path.Combine(_dir, "cfg");
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config, "quests.xml"), "<q />");
        string zip = Path.Combine(_dir, "c.zip");
        MapBundle.Write(zip, Info(), [_world], new BundlePlan([], [], 0), config, null, CancellationToken.None);
        using var archive = ZipFile.OpenRead(zip);
        Assert.NotNull(archive.GetEntry("My-Bundle/Config/quests.xml"));
    }

    [Fact]
    public void ModInfo_has_the_expected_shape_and_escapes_values()
    {
        string text = MapBundle.ModInfoXml(Info());
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\" ?>", text);
        Assert.Contains("\n\t<Name value=\"My-Bundle\" />", text);
        var root = XDocument.Parse(text).Root!;
        string Value(string e) => (string)root.Element(e)!.Attribute("value")!;
        Assert.Equal("xml", root.Name.LocalName);
        Assert.Equal("My <Bundle>", Value("DisplayName"));
        Assert.Equal("A & B \"C\"", Value("Author"));
        Assert.Equal("https://example.com/?a=1&b=2", Value("Website"));
        Assert.Equal("1.0.0", Value("Version"));
        Assert.Equal("true", Value("SkipWithAntiCheat"));
        Assert.Equal(["Name", "DisplayName", "Description", "Author", "Version", "Website", "SkipWithAntiCheat"],
            root.Elements().Select(e => e.Name.LocalName));

        var bare = XDocument.Parse(MapBundle.ModInfoXml(Info() with { Website = "", SkipWithAntiCheat = false })).Root!;
        Assert.Null(bare.Element("Website"));
        Assert.Null(bare.Element("SkipWithAntiCheat"));
    }

    [Fact]
    public void Cancel_leaves_no_zip_and_keeps_an_old_one()
    {
        var plan = MapBundle.Plan([_world], _library, _vanilla);
        string zip = Path.Combine(_dir, "cancel.zip");
        using var cts = new CancellationTokenSource();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            MapBundle.Write(zip, Info(), [_world], plan, null, p => { if (p.BytesDone > 0) cts.Cancel(); }, cts.Token));
        Assert.False(File.Exists(zip));
        Assert.False(File.Exists(zip + ".partial"));

        File.WriteAllText(zip, "old");
        using var again = new CancellationTokenSource();
        again.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => MapBundle.Write(zip, Info(), [_world], plan, null, null, again.Token));
        Assert.Equal("old", File.ReadAllText(zip));
        Assert.False(File.Exists(zip + ".partial"));
    }

    [Fact]
    public void Names_are_checked_and_defaulted()
    {
        Assert.Equal("New-York-Undead-III", MapBundle.DefaultName("New York Undead III"));
        Assert.Equal("ab", MapBundle.DefaultName("a/b:"));
        Assert.Null(MapBundle.NameError("Good-Name"));
        Assert.NotNull(MapBundle.NameError(""));
        Assert.NotNull(MapBundle.NameError("bad/name"));
        Assert.NotNull(MapBundle.NameError("dots."));
        Assert.NotNull(MapBundle.NameWarning("has space"));
        Assert.Null(MapBundle.NameWarning("no-space"));
        Assert.Throws<ArgumentException>(() => MapBundle.Write(Path.Combine(_dir, "x.zip"), Info("bad/name"), [], new BundlePlan([], [], 0), null, null, CancellationToken.None));
    }

    [Fact]
    public void ListWorlds_reads_world_folders()
    {
        var worlds = MapBundle.ListWorlds(Path.Combine(_dir, "GeneratedWorlds"));
        var world = Assert.Single(worlds);
        Assert.Equal("Alpha", world.Name);
        Assert.True(world.Bytes >= 5000);
    }
}
