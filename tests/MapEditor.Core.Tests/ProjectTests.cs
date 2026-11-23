using System.IO.Compression;
using MapEditor.Core.GameData;
using MapEditor.Core.IO;
using MapEditor.Core.Model;
using MapEditor.Core.Validate;

namespace MapEditor.Core.Tests;

public sealed class ProjectTests : IDisposable
{
    private const int Size = 512;
    private readonly string _dir = Directory.CreateTempSubdirectory("mapeditor-project-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static World MakeWorld()
    {
        var world = new World { Name = "Proj", Size = Size, Height = new Grid<ushort>(Size, Size) };
        for (int i = 0; i < world.Height.Data.Length; i++) world.Height.Data[i] = (ushort)(i * 7);
        var biomes = new Grid<byte>(Size / 8, Size / 8);
        biomes[3, 4] = 1;
        world.Biomes = new IndexedLayer(biomes, [0x004000, 0xFFE477]);
        world.Roads = new Grid<byte>(Size, Size);
        world.Roads[5, 6] = (byte)RoadType.Gravel;
        world.Water = new Grid<byte>(Size, Size);
        world.Water[7, 8] = 30;
        world.Radiation = new Grid<byte>(Size / 32, Size / 32);
        world.Radiation[1, 1] = 255;
        var prefab = new PrefabInstance { Name = "house_01", Position = new Int3(-10, 37, 20), Rotation = 3, Type = "model" };
        prefab.ExtraAttributes.Add(new("help", "x"));
        world.Prefabs.Add(prefab);
        world.Spawns.Add(new SpawnPoint { X = 1.5f, Y = 40, Z = -7, RotationY = 90 });
        world.ExtraFiles[WorldFiles.Level] = [1, 2, 3];
        WorldXml.SetProperty(world.MapInfo, "Description", "test");
        return world;
    }

    [Fact]
    public void Project_round_trips_everything()
    {
        var world = MakeWorld();
        string path = Path.Combine(_dir, "a" + ProjectStore.Extension);
        ProjectStore.Save(world, path, new ProjectStore.ProjectSettings { ExportFolder = @"C:\out\Proj" });

        var copy = ProjectStore.Load(path, out var settings, out var missingWorldFilesWarning);
        Assert.Empty(copy.Warnings);
        Assert.Null(missingWorldFilesWarning);
        Assert.Empty(WorldComparer.Compare(world, copy));
        Assert.Equal("Proj", copy.Name);
        Assert.Equal(world.Height.Data, copy.Height.Data);
        Assert.Equal(@"C:\out\Proj", settings.ExportFolder);
        Assert.Equal("help", copy.Prefabs[0].ExtraAttributes[0].Key);
        Assert.Equal("test", WorldXml.GetProperty(copy.MapInfo, "Description"));
        Assert.Equal([1, 2, 3], copy.ExtraFiles[WorldFiles.Level]);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void A_missing_level_file_warns_and_leaves_the_world_readable()
    {
        var world = MakeWorld();
        string path = Path.Combine(_dir, "missing-level" + ProjectStore.Extension);
        ProjectStore.Save(world, path);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
            zip.GetEntry("extra/" + WorldFiles.Level)!.Delete();

        var loaded = ProjectStore.Load(path, out _, out var warning);

        Assert.Equal(world.Height.Data, loaded.Height.Data);
        Assert.Empty(loaded.ExtraFiles);
        Assert.Equal("The project lists the world file main.ttw, but the file is missing. The project may be damaged.",
            Assert.Single(loaded.Warnings));
        Assert.Equal("The project may be damaged. These world files are missing:\n\nmain.ttw\n\n"
            + "Saving the project will not bring these files back.\n\n"
            + "When you export the world, the editor makes a new main.ttw with a new world GUID. "
            + "Games already started on this world will not match the exported world.", warning);
    }

    [Fact]
    public void The_missing_files_dialog_lists_only_missing_world_files()
    {
        var world = MakeWorld();
        world.ExtraFiles["a.xml"] = [1];
        world.ExtraFiles["b.xml"] = [2];
        string path = Path.Combine(_dir, "missing-extra" + ProjectStore.Extension);
        ProjectStore.Save(world, path);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            zip.GetEntry("extra/a.xml")!.Delete();
            zip.GetEntry("extra/b.xml")!.Delete();
            using var history = zip.CreateEntry("history.jsonl").Open();
            history.Write("{truncated"u8);
        }

        var loaded = ProjectStore.Load(path, out _, out var warning);

        Assert.Contains("Some entries in the project history could not be read.", loaded.Warnings);
        Assert.Equal("The project may be damaged. These world files are missing:\n\na.xml\nb.xml\n\n"
            + "Saving the project will not bring these files back.", warning);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void ExportStateSurvivesCloningAndSaving(bool? hasChanges)
    {
        var settings = new ProjectStore.ProjectSettings { ExportFolder = "My Island", ExportHasChanges = hasChanges };
        string path = Path.Combine(_dir, "export" + ProjectStore.Extension);

        ProjectStore.Save(MakeWorld(), path, settings.Clone());
        ProjectStore.Load(path, out var loaded);

        Assert.Equal("My Island", loaded.ExportFolder);
        Assert.Equal(hasChanges, loaded.ExportHasChanges);
    }

    [Theory]
    [InlineData("Test2", true)]
    [InlineData("Test1", false)]
    [InlineData("test1", false)]
    public void SaveAsForgetsExportOnlyWhenTheProjectNameChanges(string newName, bool forgetsExport)
    {
        string exportFolder = Path.Combine(_dir, "Test1");
        var settings = new ProjectStore.ProjectSettings { ExportFolder = exportFolder, ExportHasChanges = true };
        string path = Path.Combine(_dir, "other", newName + ProjectStore.Extension);

        Assert.Equal(forgetsExport, settings.ForgetExportForSaveAs("Test1", path));
        Assert.Equal(forgetsExport ? null : exportFolder, settings.ExportFolder);
        Assert.Equal(forgetsExport ? (bool?)null : true, settings.ExportHasChanges);
    }

    [Fact]
    public void SaveAsWithANewNameStoresNoExportLink()
    {
        var world = MakeWorld();
        var settings = new ProjectStore.ProjectSettings { ExportFolder = "Exported World", ExportHasChanges = false };
        string path = Path.Combine(_dir, "New Project" + ProjectStore.Extension);
        settings.ForgetExportForSaveAs(world.Name, path);
        world.Name = Path.GetFileNameWithoutExtension(path);

        ProjectStore.Save(world, path, settings.Clone());
        var loadedWorld = ProjectStore.Load(path, out var loadedSettings);

        Assert.Equal("New Project", loadedWorld.Name);
        Assert.Null(loadedSettings.ExportFolder);
        Assert.Null(loadedSettings.ExportHasChanges);
    }

    [Fact]
    public void Passthrough_image_is_kept_until_its_layer_is_edited()
    {
        var world = MakeWorld();
        world.PassthroughImages[WorldFiles.Roads] = new RgbaImage(2, 1, [1, 2, 3, 4, 5, 6, 7, 8]);
        string path = Path.Combine(_dir, "b" + ProjectStore.Extension);

        ProjectStore.Save(world, path);
        Assert.True(ProjectStore.Load(path).PassthroughImages.ContainsKey(WorldFiles.Roads));

        world.Roads!.MarkChanged();
        ProjectStore.Save(world, path);
        Assert.False(ProjectStore.Load(path).PassthroughImages.ContainsKey(WorldFiles.Roads));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(255)]
    [InlineData(257)]
    public void A_declared_original_image_without_exactly_its_pixel_data_stops_the_load(int? bytes)
    {
        var world = MakeWorld();
        world.Biomes = null;
        world.PassthroughImages[WorldFiles.Biomes] = new RgbaImage(8, 8, new byte[8 * 8 * 4]);
        string path = Path.Combine(_dir, "original" + ProjectStore.Extension);
        ProjectStore.Save(world, path);

        const string entry = "passthrough/" + WorldFiles.Biomes;
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            zip.GetEntry(entry)!.Delete();
            if (bytes is { } n)
                using (var s = zip.CreateEntry(entry).Open())
                    s.Write(new byte[n]);
        }

        var ex = Assert.Throws<InvalidDataException>(() => ProjectStore.Load(path));
        Assert.Contains("original image", ex.Message);
        Assert.Contains(WorldFiles.Biomes, ex.Message);
    }

    [Fact]
    public void Clone_is_independent_and_keeps_edit_versions()
    {
        var world = MakeWorld();
        world.Roads!.MarkChanged();
        var copy = world.Clone();

        Assert.Empty(WorldComparer.Compare(world, copy));
        Assert.Equal(1, copy.Roads!.Version);
        Assert.Equal(0, copy.Water!.Version);

        world.Height[0, 0] = 1234;
        world.Prefabs[0].Position = new Int3(0, 0, 0);
        world.Spawns.Clear();
        Assert.NotEqual(1234, copy.Height[0, 0]);
        Assert.Equal(new Int3(-10, 37, 20), copy.Prefabs[0].Position);
        Assert.Single(copy.Spawns);
    }

    public static TheoryData<string> LayerEntries =>
        ["layers/height.u16", "layers/biomes.u8", "layers/roads.u8", "layers/water.u8", "layers/radiation.u8"];

    /// <summary>A saved project with <paramref name="entry"/> removed, or replaced with <paramref name="bytes"/> bytes.</summary>
    private string ProjectWithLayer(string entry, int? bytes)
    {
        string path = Path.Combine(_dir, "layer" + ProjectStore.Extension);
        ProjectStore.Save(MakeWorld(), path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        zip.GetEntry(entry)!.Delete();
        if (bytes is { } n)
            using (var s = zip.CreateEntry(entry).Open())
                s.Write(new byte[n]);
        return path;
    }

    [Theory]
    [MemberData(nameof(LayerEntries))]
    public void A_declared_layer_that_is_missing_stops_the_load(string entry)
    {
        var ex = Assert.Throws<InvalidDataException>(() => ProjectStore.Load(ProjectWithLayer(entry, null)));
        Assert.Contains(entry, ex.Message);
    }

    [Theory]
    [MemberData(nameof(LayerEntries))]
    public void A_declared_layer_of_the_wrong_size_stops_the_load(string entry)
    {
        string path = Path.Combine(_dir, "size" + ProjectStore.Extension);
        ProjectStore.Save(MakeWorld(), path);
        long length;
        using (var zip = ZipFile.OpenRead(path)) length = zip.GetEntry(entry)!.Length;

        foreach (long bytes in new[] { length - 1, length + 1 })
        {
            var ex = Assert.Throws<InvalidDataException>(() => ProjectStore.Load(ProjectWithLayer(entry, (int)bytes)));
            Assert.Contains(entry, ex.Message);
        }
    }

    [Fact]
    public void Layers_the_manifest_does_not_declare_are_not_needed()
    {
        var world = MakeWorld();
        world.Biomes = null;
        world.Roads = null;
        world.Water = null;
        world.Radiation = null;
        string path = Path.Combine(_dir, "bare" + ProjectStore.Extension);
        ProjectStore.Save(world, path);
        var copy = ProjectStore.Load(path);
        Assert.Null(copy.Roads);
        Assert.Null(copy.Biomes);
    }

    [Fact]
    public void Not_a_project_is_rejected()
    {
        string path = Path.Combine(_dir, "bad" + ProjectStore.Extension);
        File.WriteAllBytes(path, [1, 2, 3]);
        Assert.ThrowsAny<InvalidDataException>(() => ProjectStore.Load(path));
    }

    private static PrefabInfo Poi(string name, int s) => new(name, "POIs", new Int3(s, 10, s), -1, 0, 1, [], null);

    [Fact]
    public void Validator_flags_spawns_traders_overlaps_water_and_unknown_prefabs()
    {
        var world = new World { Name = "V", Size = Size, Height = new Grid<ushort>(Size, Size) };
        Array.Fill(world.Height.Data, World.MetresToHeight(20));
        world.Water = new Grid<byte>(Size, Size);
        for (int z = 300; z < 340; z++) for (int x = 300; x < 340; x++) world.Water[x, z] = 25;
        world.Biomes = new IndexedLayer(new Grid<byte>(Size / 8, Size / 8), [0x004000]);
        world.ExtraFiles[WorldFiles.Level] = [1];

        var library = PrefabLibrary.Of([Poi("a", 30), Poi("b", 30), Poi("wet", 30)]);
        world.Prefabs.Add(new PrefabInstance { Name = "a", Position = new Int3(-200, 20, -200) });
        world.Prefabs.Add(new PrefabInstance { Name = "b", Position = new Int3(-190, 20, -190) });
        world.Prefabs.Add(new PrefabInstance { Name = "wet", Position = new Int3(50, 20, 50) });
        world.Prefabs.Add(new PrefabInstance { Name = "missing", Position = new Int3(0, 20, 0) });
        world.Prefabs.Add(new PrefabInstance { Name = "a", Position = new Int3(400, 20, 0) });

        var issues = WorldValidator.Validate(world, library);
        string all = string.Join("\n", issues.Select(i => i.Message));

        Assert.Contains("no spawn points", all);
        Assert.Contains("no trader", all);
        Assert.Contains("a overlaps b", all);
        Assert.Contains("wet is 100% under water", all);
        Assert.Contains("missing (1×) is not in the game folder", all);
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Error && i.Message.StartsWith("a is outside"));
        var overlap = issues.Single(i => i.Message.Contains("overlaps"));
        Assert.Equal((76, 76), (overlap.X!.Value, overlap.Z!.Value));
    }

    [Fact]
    public void Validator_is_quiet_for_a_tidy_world()
    {
        var world = new World { Name = "V", Size = Size, Height = new Grid<ushort>(Size, Size) };
        world.Biomes = new IndexedLayer(new Grid<byte>(Size / 8, Size / 8), [0x004000]);
        world.ExtraFiles[WorldFiles.Level] = [1];
        world.Spawns.Add(new SpawnPoint { X = 0, Y = 5, Z = 0 });
        var trader = new PrefabInfo("trader_hugh", "POIs", new Int3(40, 10, 40), -1, 0, 1, [], null);
        world.Prefabs.Add(new PrefabInstance { Name = "trader_hugh", Position = new Int3(0, 5, 0) });

        Assert.Empty(WorldValidator.Validate(world, PrefabLibrary.Of([trader])));
    }
}
