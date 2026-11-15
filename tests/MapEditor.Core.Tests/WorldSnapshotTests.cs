using MapEditor.Core.Edit;
using MapEditor.Core.IO;
using MapEditor.Core.Model;
using MapEditor.Core.Validate;

namespace MapEditor.Core.Tests;

/// <summary>Copy-on-write snapshots for autosave: what is written is the world as it was when the snapshot was taken.</summary>
public sealed class WorldSnapshotTests : IDisposable
{
    private const int Size = 512;
    private readonly string _dir = Directory.CreateTempSubdirectory("mapeditor-snapshot-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static World MakeWorld()
    {
        var world = new World { Name = "Snap", Size = Size, Height = new Grid<ushort>(Size, Size) };
        for (int i = 0; i < world.Height.Data.Length; i++) world.Height.Data[i] = (ushort)(i * 7);
        world.Biomes = new IndexedLayer(new Grid<byte>(Size / 8, Size / 8), [0x004000]);
        world.Roads = new Grid<byte>(Size, Size);
        world.Roads[5, 6] = (byte)RoadType.Gravel;
        world.Prefabs.Add(new PrefabInstance { Name = "house_01", Position = new Int3(-10, 37, 20) });
        world.Spawns.Add(new SpawnPoint { X = 1.5f, Y = 40, Z = -7 });
        world.ExtraFiles[WorldFiles.Level] = [1, 2, 3];
        return world;
    }

    /// <summary>An edit the way the tools make one: touch first, then change cells.</summary>
    private static void Paint<T>(Grid<T> grid, GridRect rect, T value, UndoStack? stack = null) where T : unmanaged
    {
        var session = new EditSession("Paint");
        session.Touch(grid, rect);
        for (int z = rect.Z0; z < rect.Z1; z++)
            for (int x = rect.X0; x < rect.X1; x++) grid[x, z] = value;
        stack?.Push(session);
    }

    private string PathFor(string name) => Path.Combine(_dir, name + ProjectStore.Extension);

    [Fact]
    public void Edits_after_the_snapshot_is_taken_are_not_saved()
    {
        var world = MakeWorld();
        var before = world.Clone();
        using var snapshot = WorldSnapshot.Take(world);

        Paint(world.Height, new GridRect(100, 100, 300, 300), (ushort)1234);
        Paint(world.Roads!, new GridRect(0, 0, 50, 50), (byte)RoadType.Asphalt);
        world.Biomes!.Palette.Add(0xFFE477);
        Paint(world.Biomes.Indices, new GridRect(0, 0, 4, 4), (byte)1);
        world.Water = new Grid<byte>(Size, Size);
        world.Prefabs[0].Position = new Int3(0, 0, 0);
        world.Prefabs.Add(new PrefabInstance { Name = "store_01" });
        world.Spawns.Clear();
        world.Name = "Renamed";

        ProjectStore.Save(snapshot, PathFor("a"));
        var saved = ProjectStore.Load(PathFor("a"));
        Assert.Empty(WorldComparer.Compare(before, saved));
        Assert.Equal(before.Height.Data, saved.Height.Data);
        Assert.Equal(before.Roads!.Data, saved.Roads!.Data);
        Assert.Equal(before.Biomes!.Palette, saved.Biomes!.Palette);
        Assert.Null(saved.Water);
        Assert.Equal("Snap", saved.Name);
        Assert.True(snapshot.KeptBytes > 0);
    }

    [Fact]
    public void Edits_during_the_write_keep_old_cells_only_where_not_yet_written()
    {
        var world = MakeWorld();
        var original = (ushort[])world.Height.Data.Clone();
        using var snapshot = WorldSnapshot.Take(world);
        var frozen = snapshot.Find(world.Height)!;

        var written = new ushort[original.Length];
        int band = FrozenGrid<ushort>.Band;
        frozen.ReadBands((z0, cells) =>
        {
            cells.CopyTo(written.AsSpan(z0 * Size));
            if (z0 != 2 * band) return;
            // One tile already written (band 0) and one still to come (band 5).
            Paint(world.Height, new GridRect(0, 0, band, band), (ushort)1);
            Paint(world.Height, new GridRect(0, 5 * band, band, 6 * band), (ushort)2);
        }, final: true);

        Assert.Equal(original, written);
        Assert.Equal(band * band * sizeof(ushort), snapshot.KeptBytes);
        Assert.Equal(2, world.Height[0, 5 * band]);
    }

    [Fact]
    public void Undo_during_the_write_is_not_saved()
    {
        var world = MakeWorld();
        var stack = new UndoStack();
        Paint(world.Height, new GridRect(10, 10, 200, 200), (ushort)999, stack);
        var edited = (ushort[])world.Height.Data.Clone();

        using var snapshot = WorldSnapshot.Take(world);
        stack.Undo();
        Assert.NotEqual(edited, world.Height.Data);

        ProjectStore.Save(snapshot, PathFor("b"));
        Assert.Equal(edited, ProjectStore.Load(PathFor("b")).Height.Data);
    }

    [Fact]
    public void Disposed_snapshot_stops_keeping_tiles()
    {
        var world = MakeWorld();
        var snapshot = WorldSnapshot.Take(world);
        snapshot.Dispose();
        Paint(world.Height, new GridRect(0, 0, 10, 10), (ushort)5);
        Assert.Equal(0, snapshot.KeptBytes);
    }

    [Fact]
    public void Edited_then_undone_road_image_is_kept_while_live_edits_carry_on()
    {
        // A splat3 with a colour RWG never writes, so it is kept as a passthrough image.
        string folder = Path.Combine(_dir, "world");
        WorldWriter.Write(MakeWorld(), folder);
        var rgba = new byte[Size * Size * 4];
        rgba[0] = 12; rgba[1] = 34; rgba[2] = 56; rgba[3] = 255;
        Png.WriteRgba(Path.Combine(folder, WorldFiles.Roads), Size, Size, rgba);
        var world = WorldReader.Read(folder);
        Assert.True(world.PassthroughImages.ContainsKey(WorldFiles.Roads));

        var stack = new UndoStack();
        Paint(world.Roads!, new GridRect(0, 0, 8, 8), (byte)RoadType.Asphalt, stack);
        stack.Undo();

        using var snapshot = WorldSnapshot.Take(world);
        Paint(world.Roads!, new GridRect(100, 100, 120, 120), (byte)RoadType.Asphalt);

        ProjectStore.Save(snapshot, PathFor("c"));
        Assert.True(ProjectStore.Load(PathFor("c")).PassthroughImages.ContainsKey(WorldFiles.Roads));
        // The live world has moved on, so a normal save drops it.
        ProjectStore.Save(world, PathFor("d"));
        Assert.False(ProjectStore.Load(PathFor("d")).PassthroughImages.ContainsKey(WorldFiles.Roads));
    }
}
