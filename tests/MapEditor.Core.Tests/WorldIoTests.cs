using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.IO;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Tests;

public sealed class WorldIoTests : IDisposable
{
    private const int Size = 512;
    private readonly string _dir = Directory.CreateTempSubdirectory("mapeditor-world-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static World MakeWorld()
    {
        var world = new World { Name = "Test", Size = Size, Height = new Grid<ushort>(Size, Size) };
        for (int z = 0; z < Size; z++)
            for (int x = 0; x < Size; x++)
                world.Height[x, z] = World.MetresToHeight(40 + x * 0.1f + z * 0.05f);

        var biomes = new Grid<byte>(Size / 8, Size / 8);
        biomes[0, 0] = 1;
        world.Biomes = new IndexedLayer(biomes, [0x004000, 0xFFE477]);
        world.Roads = new Grid<byte>(Size, Size);
        world.Roads[10, 0] = (byte)RoadType.Asphalt;
        world.Roads[11, 0] = (byte)RoadType.Gravel;
        world.Water = new Grid<byte>(Size, Size);
        world.Water[0, 0] = 30;
        world.Radiation = new Grid<byte>(Size / 32, Size / 32);
        world.Radiation[0, 15] = 255;
        world.Prefabs.Add(new PrefabInstance { Name = "abandoned_house_01", Position = new Int3(-100, 37, 20), Rotation = 2 });
        world.Spawns.Add(new SpawnPoint { X = 12.5f, Y = 40, Z = -7, RotationY = 90 });
        world.ExtraFiles[WorldFiles.Level] = [1, 2, 3];
        return world;
    }

    [Fact]
    public void World_round_trips_through_game_files()
    {
        var world = MakeWorld();
        WorldWriter.Write(world, _dir);
        var copy = WorldReader.Read(_dir);

        Assert.Empty(WorldComparer.Compare(world, copy));
        Assert.Empty(copy.Warnings);
        Assert.Equal($"{Size},{Size}", WorldXml.GetProperty(copy.MapInfo, "HeightMapSize"));
    }

    /// <summary>Writes an RGBA image whose every pixel is the same colour.</summary>
    private static void WriteImage(string path, int width, int height, byte r, byte g, byte b) =>
        Png.WriteRows(path, width, height, PngPixelFormat.Rgba8, (_, row) =>
        {
            for (int x = 0; x < width; x++) { row[x * 4] = r; row[x * 4 + 1] = g; row[x * 4 + 2] = b; row[x * 4 + 3] = 255; }
        });

    [Fact]
    public void Biome_and_radiation_maps_that_dont_fit_the_world_are_kept_as_they_were()
    {
        WorldWriter.Write(MakeWorld(), _dir);
        // 512 m world: 100 doesn't divide it, and 30 x 20 isn't square.
        WriteImage(Path.Combine(_dir, WorldFiles.Biomes), 100, 100, 0x00, 0x40, 0x00);
        WriteImage(Path.Combine(_dir, WorldFiles.Radiation), 30, 20, 255, 0, 0);

        var world = WorldReader.Read(_dir);
        Assert.Null(world.Biomes);
        Assert.Null(world.Radiation);
        Assert.Equal(2, world.Warnings.Count(w => w.Contains("doesn't fit a 512 m world")));
        Assert.NotNull(LayerPaint.CantPaintBiome(world, 0x004000));
        Assert.NotNull(LayerPaint.CantPaintRadiation(world));
        Assert.Equal(GridRect.Empty, LayerPaint.PaintRadiation(world, 100, 100, new BrushTip(50), erase: false, new EditSession("r")));
        Assert.Null(world.Radiation);

        // Export writes both back exactly as they were.
        string export = Path.Combine(_dir, "export");
        WorldWriter.Write(world, export);
        foreach (string file in new[] { WorldFiles.Biomes, WorldFiles.Radiation })
        {
            var before = Png.ReadRgba(Path.Combine(_dir, file), out int w0, out int h0);
            var after = Png.ReadRgba(Path.Combine(export, file), out int w1, out int h1);
            Assert.Equal((w0, h0), (w1, h1));
            Assert.Equal(before, after);
        }

        // So does a project.
        string project = Path.Combine(_dir, "p" + ProjectStore.Extension);
        ProjectStore.Save(world, project);
        var loaded = ProjectStore.Load(project);
        Assert.Null(loaded.Radiation);
        Assert.Equal((30, 20), (loaded.PassthroughImages[WorldFiles.Radiation].Width, loaded.PassthroughImages[WorldFiles.Radiation].Height));
        Assert.Equal(100, loaded.PassthroughImages[WorldFiles.Biomes].Width);
    }

    [Fact]
    public void A_project_saved_with_layers_that_dont_fit_keeps_them_as_images()
    {
        // As a project from before #16 could hold them.
        var world = MakeWorld();
        world.Biomes = new IndexedLayer(new Grid<byte>(100, 100), [0x004000]);
        world.Radiation = new Grid<byte>(30, 20);
        world.Radiation[0, 19] = 255;
        string project = Path.Combine(_dir, "old" + ProjectStore.Extension);
        ProjectStore.Save(world, project);

        var loaded = ProjectStore.Load(project);
        Assert.Null(loaded.Biomes);
        Assert.Null(loaded.Radiation);
        Assert.Equal(2, loaded.Warnings.Count);
        // Row 19 (north) is the image's top row.
        var radiation = loaded.PassthroughImages[WorldFiles.Radiation];
        Assert.Equal(255, radiation.Pixels[0]);
        Assert.Equal(0x40, loaded.PassthroughImages[WorldFiles.Biomes].Pixels[1]);
    }

    [Fact]
    public void Bad_prefab_and_spawn_entries_are_left_out_with_a_warning_and_the_rest_load()
    {
        WorldWriter.Write(MakeWorld(), _dir);
        File.WriteAllText(Path.Combine(_dir, WorldFiles.Prefabs), """
            <prefabs>
              <decoration type="model" name="good_house" position="10,40,20" rotation="1" />
              <decoration type="model" name="bad_rotation" position="10,40,20" rotation="east" />
              <decoration type="model" name="no_position" rotation="0" />
              <decoration type="model" name="short_position" position="10,40" rotation="0" />
              <decoration type="model" position="1,2,3" rotation="0" />
              <decoration type="model" name="huge" position="1e30,40,20" rotation="0" />
            </prefabs>
            """);
        File.WriteAllText(Path.Combine(_dir, WorldFiles.Spawns), """
            <spawnpoints>
              <spawnpoint position="5,41,6" rotation="0,90,0" />
              <spawnpoint rotation="0,90,0" />
              <spawnpoint position="7,42,8" />
            </spawnpoints>
            """);

        var world = WorldReader.Read(_dir);

        var prefab = Assert.Single(world.Prefabs);
        Assert.Equal("good_house", prefab.Name);
        Assert.Equal((new Int3(10, 40, 20), 1), (prefab.Position, prefab.Rotation));
        Assert.Equal([(5f, 90f), (7f, 0f)], world.Spawns.Select(s => (s.X, s.RotationY)));

        string prefabWarning = Assert.Single(world.Warnings, w => w.StartsWith(WorldFiles.Prefabs));
        Assert.Contains("5 decorations", prefabWarning);
        Assert.Contains("line 3 bad_rotation", prefabWarning);
        Assert.Contains("and 2 more", prefabWarning);
        string spawnWarning = Assert.Single(world.Warnings, w => w.StartsWith(WorldFiles.Spawns));
        Assert.Contains("1 spawn point couldn't be read", spawnWarning);
        Assert.Contains("line 3: no position", spawnWarning);
    }

    [Fact]
    public void Pngs_are_written_north_up_and_raw_south_up()
    {
        var world = MakeWorld();
        WorldWriter.Write(world, _dir);

        // Grid row 0 (south) must be the LAST image row in the PNG.
        var water = Png.ReadRgba(Path.Combine(_dir, WorldFiles.Water), out _, out _);
        int lastRow = (Size - 1) * Size * 4;
        Assert.Equal(30, water[lastRow + 2]);
        Assert.Equal(0, water[2]);

        var roads = Png.ReadRgba(Path.Combine(_dir, WorldFiles.Roads), out _, out _);
        Assert.Equal([255, 0, 0, 255], roads.AsSpan(lastRow + 10 * 4, 4).ToArray());
        Assert.Equal([0, 255, 0, 255], roads.AsSpan(lastRow + 11 * 4, 4).ToArray());

        // radiation[0, 15] is the top (north) row of a 16x16 image.
        var rad = Png.ReadRgba(Path.Combine(_dir, WorldFiles.Radiation), out _, out _);
        Assert.Equal(255, rad[0]);

        // dtm.raw row 0 is south: its first sample is height at x=0, z=0.
        var raw = File.ReadAllBytes(Path.Combine(_dir, WorldFiles.Height));
        Assert.Equal(world.Height[0, 0], BitConverter.ToUInt16(raw, 0));
    }

    [Fact]
    public void Non_rwg_road_colours_survive_until_edited()
    {
        var world = MakeWorld();
        WorldWriter.Write(world, _dir);

        // Paint a colour RWG never writes into splat3.
        string roadsPath = Path.Combine(_dir, WorldFiles.Roads);
        var pixels = Png.ReadRgba(roadsPath, out int w, out int h);
        pixels[0] = 10; pixels[1] = 20; pixels[2] = 30; pixels[3] = 40;
        Png.WriteRgba(roadsPath, w, h, pixels);

        var loaded = WorldReader.Read(_dir);
        Assert.Contains(loaded.Warnings, m => m.Contains("splat3"));

        string outDir = Path.Combine(_dir, "out");
        WorldWriter.Write(loaded, outDir);
        Assert.Equal(pixels, Png.ReadRgba(Path.Combine(outDir, WorldFiles.Roads), out _, out _));
    }

    /// <summary>A world written once, with a backup folder holding an "original" heightmap, as an export cut off part way leaves it (#72).</summary>
    private (World World, string Backup, byte[] Original) WorldWithUnfinishedBackup()
    {
        var world = MakeWorld();
        WorldWriter.Write(world, _dir);
        string backup = Path.Combine(_dir, WorldWriter.BackupDir);
        Directory.CreateDirectory(backup);
        byte[] original = [7, 7, 7];
        File.WriteAllBytes(Path.Combine(backup, WorldFiles.Height), original);
        return (world, backup, original);
    }

    [Fact]
    public void An_unfinished_backup_stops_the_export_and_is_kept()
    {
        var (world, backup, original) = WorldWithUnfinishedBackup();
        byte[] height = File.ReadAllBytes(Path.Combine(_dir, WorldFiles.Height));

        var ex = Assert.Throws<IOException>(() => WorldWriter.Write(world, _dir, new WorldWriteOptions { Overwrite = true }));
        Assert.Contains(backup, ex.Message);
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(backup, WorldFiles.Height)));
        // Nothing in the world folder was touched.
        Assert.Equal(height, File.ReadAllBytes(Path.Combine(_dir, WorldFiles.Height)));
        Assert.False(Directory.Exists(Path.Combine(_dir, WorldWriter.StagingDir)));

        // Once the user has dealt with it, the export works.
        Directory.Delete(backup, recursive: true);
        WorldWriter.Write(world, _dir, new WorldWriteOptions { Overwrite = true });
    }

    [Fact]
    public void A_finished_or_empty_backup_is_cleared()
    {
        var (world, backup, _) = WorldWithUnfinishedBackup();
        File.WriteAllBytes(Path.Combine(backup, WorldWriter.BackupDoneMarker), []);
        WorldWriter.Write(world, _dir, new WorldWriteOptions { Overwrite = true });
        Assert.False(Directory.Exists(backup));

        Directory.CreateDirectory(backup);
        WorldWriter.Write(world, _dir, new WorldWriteOptions { Overwrite = true });
        Assert.False(Directory.Exists(backup));
    }

    [Fact]
    public void A_failed_export_puts_the_old_files_back()
    {
        var world = MakeWorld();
        WorldWriter.Write(world, _dir);
        byte[] height = File.ReadAllBytes(Path.Combine(_dir, WorldFiles.Height));
        string target = Path.Combine(_dir, WorldFiles.Prefabs);
        world.Height[0, 0] += 100;

        // Placing the new prefabs.xml fails, as a locked file would; putting the old files back works.
        bool placing = true;
        WorldWriter.FailMovesTo.Value = to => placing && to == target && !(placing = false);
        try
        {
            Assert.Throws<IOException>(() => WorldWriter.Write(world, _dir, new WorldWriteOptions { Overwrite = true }));
        }
        finally
        {
            WorldWriter.FailMovesTo.Value = null;
        }
        Assert.Equal(height, File.ReadAllBytes(Path.Combine(_dir, WorldFiles.Height)));
        Assert.True(File.Exists(target));
        Assert.False(Directory.Exists(Path.Combine(_dir, WorldWriter.BackupDir)));
    }

    [Fact]
    public void A_failed_restore_keeps_the_backup_and_names_it()
    {
        var world = MakeWorld();
        WorldWriter.Write(world, _dir);
        byte[] prefabs = File.ReadAllBytes(Path.Combine(_dir, WorldFiles.Prefabs));
        string target = Path.Combine(_dir, WorldFiles.Prefabs);
        string backup = Path.Combine(_dir, WorldWriter.BackupDir);

        // Every move into the world folder's prefabs.xml fails: placing the new one, then putting the old one back.
        WorldWriter.FailMovesTo.Value = to => to == target;
        try
        {
            var ex = Assert.Throws<IOException>(() => WorldWriter.Write(world, _dir, new WorldWriteOptions { Overwrite = true }));
            Assert.Contains(backup, ex.Message);
        }
        finally
        {
            WorldWriter.FailMovesTo.Value = null;
        }
        Assert.Equal(prefabs, File.ReadAllBytes(Path.Combine(backup, WorldFiles.Prefabs)));
        Assert.False(File.Exists(Path.Combine(backup, WorldWriter.BackupDoneMarker)));

        // The next export doesn't delete it.
        Assert.Throws<IOException>(() => WorldWriter.Write(world, _dir, new WorldWriteOptions { Overwrite = true }));
        Assert.Equal(prefabs, File.ReadAllBytes(Path.Combine(backup, WorldFiles.Prefabs)));
    }

    [Fact]
    public void Derived_caches_are_removed_on_overwrite()
    {
        var world = MakeWorld();
        WorldWriter.Write(world, _dir);
        File.WriteAllText(Path.Combine(_dir, "checksums.txt"), "stale");
        File.WriteAllText(Path.Combine(_dir, "splat3_processed.png"), "stale");

        Assert.Throws<IOException>(() => WorldWriter.Write(world, _dir));
        WorldWriter.Write(world, _dir, new WorldWriteOptions { Overwrite = true });

        Assert.False(File.Exists(Path.Combine(_dir, "checksums.txt")));
        Assert.False(File.Exists(Path.Combine(_dir, "splat3_processed.png")));
    }

    [Fact]
    public void An_oversized_extra_file_is_left_out_so_the_project_still_opens()
    {
        WorldWriter.Write(MakeWorld(), _dir);
        using (var big = File.Create(Path.Combine(_dir, "huge.bin")))
            big.SetLength(ProjectStore.MaxExtraFileBytes + 1);
        File.WriteAllBytes(Path.Combine(_dir, "notes.txt"), [7]);

        var world = WorldReader.Read(_dir);
        Assert.False(world.ExtraFiles.ContainsKey("huge.bin"));
        Assert.Equal([7], world.ExtraFiles["notes.txt"]);
        Assert.Contains(world.Warnings, w => w.Contains("huge.bin"));

        string project = Path.Combine(_dir, "p" + ProjectStore.Extension);
        ProjectStore.Save(world, project);
        Assert.Equal([7], ProjectStore.Load(project).ExtraFiles["notes.txt"]);
    }

    [Fact]
    public void Missing_level_file_needs_a_template()
    {
        var world = MakeWorld();
        world.ExtraFiles.Remove(WorldFiles.Level);
        Assert.Throws<InvalidOperationException>(() => WorldWriter.Write(world, _dir));

        string outDir = Path.Combine(_dir, "templated");
        WorldWriter.Write(world, outDir, new WorldWriteOptions { LevelTemplate = [9, 9] });
        Assert.Equal([9, 9], File.ReadAllBytes(Path.Combine(outDir, WorldFiles.Level)));
    }

    [Fact]
    public void New_level_guid_replaces_only_the_guid()
    {
        var world = MakeWorld();
        byte[] ttw = [.. "ttw\0"u8, 1, 2, 3, 32, .. "A6BA93844F7C91B4E9667E128C5C0BAC"u8];
        world.ExtraFiles[WorldFiles.Level] = ttw;

        WorldWriter.Write(world, _dir, new WorldWriteOptions { NewLevelGuid = true });

        var written = File.ReadAllBytes(Path.Combine(_dir, WorldFiles.Level));
        Assert.Equal(ttw.Length, written.Length);
        Assert.Equal(ttw[..^32], written[..^32]);
        Assert.NotEqual(ttw[^32..], written[^32..]);
        Assert.True(LevelFile.EndsWithGuid(written));
        Assert.Equal(written, world.ExtraFiles[WorldFiles.Level]);
    }

    /// <summary>Set MAPEDITOR_TEST_WORLD to a real world folder to run this.</summary>
    [Fact]
    public void Real_world_round_trips_when_configured()
    {
        string? folder = Environment.GetEnvironmentVariable("MAPEDITOR_TEST_WORLD");
        if (string.IsNullOrEmpty(folder)) return;

        var world = WorldReader.Read(folder);
        string outDir = Path.Combine(_dir, "real");
        WorldWriter.Write(world, outDir);
        Assert.Empty(WorldComparer.Compare(world, WorldReader.Read(outDir)));
        Assert.Equal(File.ReadAllBytes(Path.Combine(folder, WorldFiles.Height)), File.ReadAllBytes(Path.Combine(outDir, WorldFiles.Height)));
    }

    [Fact]
    public void Saved_games_are_found_by_world_folder_name()
    {
        string saves = Directory.CreateTempSubdirectory("mapeditor-saves-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(saves, "My World", "Game B", "Region"));
            Directory.CreateDirectory(Path.Combine(saves, "My World", "Game A", "Region"));
            Directory.CreateDirectory(Path.Combine(saves, "My World", "NotAGame"));
            Assert.Equal(["Game A", "Game B"], GameInstall.SavedGamesOn("My World", saves));
            Assert.Empty(GameInstall.SavedGamesOn("Other World", saves));
        }
        finally
        {
            Directory.Delete(saves, recursive: true);
        }
    }
}
