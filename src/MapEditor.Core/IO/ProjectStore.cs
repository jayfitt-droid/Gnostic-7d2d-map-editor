using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;
using MapEditor.Core.Edit;
using MapEditor.Core.Model;

namespace MapEditor.Core.IO;

/// <summary>
/// The editor's own project file (.7dmap): a zip holding project.json (name, size, prefabs, spawns, map_info and
/// settings) plus each layer as raw little-endian samples. Unlike a game world folder it keeps everything the
/// editor knows, including the export folder, and it saves faster because nothing is PNG-encoded.
/// </summary>
public static class ProjectStore
{
    public const string Extension = ".7dmap";
    /// <summary>
    /// 2 (v1.2.1): prefabs carry <see cref="PrefabInstance.GroundLevel"/>. Format 1 kept <c>y_is_groundlevel</c> as a
    /// plain attribute with Y unconverted, so it is read back as an unconverted ground-level prefab.
    /// 3 (v2.1.0): <see cref="ProjectSettings.Bridges"/>, the Bridge tool's list. Older projects have none, and load with it empty.
    /// 4 (v2.1.0): <see cref="Generate.SpawnOptions.NearStarterTrader"/>, on for new projects. Older projects load with it off, so
    /// their spawns stage keeps working as before.
    /// </summary>
    /// <remarks>
    /// Users keep projects from every release, and each release must open them all. project.json holds the records
    /// below and the whole <see cref="ProjectSettings"/> graph, including every Generate setting.
    /// <list type="bullet">
    /// <item>Safe with no format change: add a property whose default suits older files, or add an enum member at the
    /// end.</item>
    /// <item>Needs a migration: rename, retype or remove a saved property, or move, rename or remove an enum member
    /// (some enums are saved as numbers, some as names). Increase this number, convert the older format in
    /// <see cref="Load(string, out ProjectSettings)"/> (as format 1's ground level is), and note it above.</item>
    /// </list>
    /// SaveCompatibilityTests load a project saved by each release (tests/.../SaveSamples) and fail when a value is
    /// lost or changes meaning. For each release, add a sample with tools/save-samples/make-sample.ps1.
    /// </remarks>
    public const int FormatVersion = 4;

    private const string ManifestEntry = "project.json";
    private const string HeightEntry = "layers/height.u16";
    private const string BiomesEntry = "layers/biomes.u8";
    private const string RoadsEntry = "layers/roads.u8";
    private const string WaterEntry = "layers/water.u8";
    private const string RadiationEntry = "layers/radiation.u8";
    private const string PassthroughDir = "passthrough/";
    private const string ExtraDir = "extra/";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Largest world a project may hold, in metres: the game's own limit.</summary>
    public const int MaxWorldSize = 16384;

    /// <summary>Most bytes one extra file, and all of them together, may hold when a project is loaded.</summary>
    public const long MaxExtraFileBytes = 256L << 20, MaxExtraBytes = 1L << 30;

    /// <summary>Largest project.json read, in bytes.</summary>
    private const long MaxManifestBytes = 256L << 20;

    /// <summary>Editor state stored beside the world.</summary>
    public sealed class ProjectSettings
    {
        /// <summary>The game world folder this project was last exported to.</summary>
        public string? ExportFolder { get; set; }

        /// <summary>The Generate panel's seeds, settings, painted tiles and progress for this world.</summary>
        public Generate.GeneratorSettings? Generator { get; set; }

        /// <summary>
        /// The bridges the user built with the Bridge tool, each with the ground it replaced. The Highways stage keeps its
        /// own in <see cref="Generate.GeneratorSettings.PlacedRoads"/>. Filled in place, never replaced, so undo can restore it.
        /// </summary>
        public List<Ops.PlacedBridge> Bridges { get; set; } = [];

        /// <summary>
        /// A deep copy of everything saved, for a save or autosave that runs while editing carries on. Every saved field
        /// must be copied here, or saving drops it.
        /// </summary>
        public ProjectSettings Clone() => new()
        {
            ExportFolder = ExportFolder,
            Generator = Generator?.Clone(),
            Bridges = [.. Bridges.Select(b => b.Clone())],
        };
    }

    private sealed record Size2(int Width, int Height);

    private sealed record PrefabDto(string Name, string Type, int X, int Y, int Z, int Rotation, List<string[]>? Extra,
        bool GroundLevel = false, int? GroundLevelOffset = null);

    private sealed record SpawnDto(float X, float Y, float Z, float RotationY);

    private sealed record PassthroughDto(string File, int Width, int Height);

    private sealed class Manifest
    {
        public int Format { get; set; }
        public string Name { get; set; } = "";
        public int Size { get; set; }
        public string MapInfo { get; set; } = "<MapInfo />";
        public List<uint>? BiomePalette { get; set; }
        public Size2? Biomes { get; set; }
        public bool Roads { get; set; }
        public bool Water { get; set; }
        public Size2? Radiation { get; set; }
        public List<PrefabDto> Prefabs { get; set; } = [];
        public List<SpawnDto> Spawns { get; set; } = [];
        public List<PassthroughDto> Passthrough { get; set; } = [];
        public List<string> ExtraFiles { get; set; } = [];
        public ProjectSettings Settings { get; set; } = new();
    }

    /// <summary>
    /// Writes the project. The file is written beside the target first and then moved over it, so a failed
    /// save never leaves a half-written project behind.
    /// </summary>
    public static void Save(World world, string path, ProjectSettings? settings = null) => Save(world, path, settings, null);

    /// <summary>Writes a snapshot as it was when taken, while editing carries on (autosave).</summary>
    public static void Save(WorldSnapshot snapshot, string path, ProjectSettings? settings = null) =>
        Save(snapshot.World, path, settings, snapshot);

    private static void Save(World world, string path, ProjectSettings? settings, WorldSnapshot? snapshot)
    {
        // Load refuses these, so a project saved with one could never be opened again (#22). WorldWriter checks the same.
        foreach (var name in world.ExtraFiles.Keys)
            if (!WorldFiles.IsSafeExtraName(name))
                throw new InvalidOperationException($"The world has an extra file named '{name}', which can't be kept in a project.");
        var manifest = new Manifest
        {
            Format = FormatVersion,
            Name = world.Name,
            Size = world.Size,
            MapInfo = world.MapInfo.ToString(SaveOptions.DisableFormatting),
            BiomePalette = world.Biomes?.Palette,
            Biomes = world.Biomes is { } b ? new Size2(b.Indices.Width, b.Indices.Height) : null,
            Roads = world.Roads is not null,
            Water = world.Water is not null,
            Radiation = world.Radiation is { } r ? new Size2(r.Width, r.Height) : null,
            Prefabs = world.Prefabs.Select(p => new PrefabDto(p.Name, p.Type, p.Position.X, p.Position.Y, p.Position.Z, p.Rotation,
                p.ExtraAttributes.Count == 0 ? null : p.ExtraAttributes.Select(a => new[] { a.Key, a.Value }).ToList(),
                p.GroundLevel, p.GroundLevelOffset)).ToList(),
            Spawns = world.Spawns.Select(s => new SpawnDto(s.X, s.Y, s.Z, s.RotationY)).ToList(),
            ExtraFiles = world.ExtraFiles.Keys.ToList(),
            Settings = settings ?? new ProjectSettings(),
        };

        // A passthrough image is only used while its layer still matches it (see WorldWriter), so an edited layer's
        // original is dropped rather than carried along.
        var passthrough = world.PassthroughImages.Where(kv => Passthrough.Applies(world, kv.Key, snapshot)).ToList();
        manifest.Passthrough = passthrough.Select(kv => new PassthroughDto(kv.Key, kv.Value.Width, kv.Value.Height)).ToList();

        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        string temp = full + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                using (var entry = zip.CreateEntry(ManifestEntry, CompressionLevel.Optimal).Open())
                    JsonSerializer.Serialize(entry, manifest, Json);
                WriteGrid(zip, HeightEntry, world.Height, snapshot);
                if (world.Biomes is { } biomes) WriteGrid(zip, BiomesEntry, biomes.Indices, snapshot);
                if (world.Roads is { } roads) WriteGrid(zip, RoadsEntry, roads, snapshot);
                if (world.Water is { } water) WriteGrid(zip, WaterEntry, water, snapshot);
                if (world.Radiation is { } radiation) WriteGrid(zip, RadiationEntry, radiation, snapshot);
                foreach (var (file, image) in passthrough) WriteBytes(zip, PassthroughDir + file, image.Pixels);
                foreach (var (file, bytes) in world.ExtraFiles) WriteBytes(zip, ExtraDir + file, bytes);
            }
            File.Move(temp, full, overwrite: true);
        }
        catch
        {
            // Don't leave a half-written project beside the real one.
            try { File.Delete(temp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    public static World Load(string path) => Load(path, out _);

    public static World Load(string path, out ProjectSettings settings)
    {
        using var zip = ZipFile.OpenRead(path);
        var manifestEntry = zip.GetEntry(ManifestEntry) ?? throw new InvalidDataException("The file is not a map editor project (no project.json).");
        if (manifestEntry.Length > MaxManifestBytes) throw new InvalidDataException("project.json is too large.");
        Manifest manifest;
        using (var s = manifestEntry.Open())
            manifest = JsonSerializer.Deserialize<Manifest>(s, Json) ?? throw new InvalidDataException("project.json is empty.");
        if (manifest.Format > FormatVersion)
            throw new InvalidDataException($"The project was saved by a newer version of the editor (format {manifest.Format}).");
        CheckManifest(manifest);

        int size = manifest.Size;
        var world = new World
        {
            // A hand-edited project.json can drop these (#22).
            Name = string.IsNullOrWhiteSpace(manifest.Name) ? Path.GetFileNameWithoutExtension(path) : manifest.Name,
            Size = size,
            Height = ReadGrid<ushort>(zip, HeightEntry, size, size) ?? throw new InvalidDataException("The project has no heightmap."),
            MapInfo = XDocument.Parse(manifest.MapInfo ?? throw new InvalidDataException("project.json has no MapInfo.")),
        };
        if (manifest.Biomes is { } bs && ReadGrid<byte>(zip, BiomesEntry, bs.Width, bs.Height) is { } biomes)
        {
            var palette = manifest.BiomePalette ?? [];
            foreach (byte index in biomes.Data)
                if (index >= palette.Count) throw new InvalidDataException("The biome layer uses a colour its palette doesn't have.");
            world.Biomes = new IndexedLayer(biomes, palette);
        }
        if (manifest.Roads) world.Roads = ReadGrid<byte>(zip, RoadsEntry, size, size);
        if (manifest.Water) world.Water = ReadGrid<byte>(zip, WaterEntry, size, size);
        if (manifest.Radiation is { } rs) world.Radiation = ReadGrid<byte>(zip, RadiationEntry, rs.Width, rs.Height);
        KeepUnfittingLayers(world);

        foreach (var p in manifest.Prefabs ?? [])
        {
            if (p is null) continue;
            if (string.IsNullOrEmpty(p.Name)) throw new InvalidDataException("A prefab in project.json has no name.");
            var prefab = new PrefabInstance
            {
                Name = p.Name, Type = p.Type ?? "model", Position = new Int3(p.X, p.Y, p.Z), Rotation = p.Rotation,
                GroundLevel = p.GroundLevel, GroundLevelOffset = p.GroundLevel ? p.GroundLevelOffset : null,
            };
            foreach (var pair in p.Extra ?? [])
            {
                if (pair is not [not null, not null]) continue;
                if (manifest.Format < 2 && pair[0] == GroundLevel.Attribute && bool.TryParse(pair[1], out bool ground) && ground)
                    prefab.GroundLevel = true;
                else
                    prefab.ExtraAttributes.Add(new(pair[0], pair[1]));
            }
            world.Prefabs.Add(prefab);
        }
        foreach (var s in (manifest.Spawns ?? []).Where(s => s is not null))
            world.Spawns.Add(new SpawnPoint { X = s.X, Y = s.Y, Z = s.Z, RotationY = s.RotationY });
        foreach (var p in (manifest.Passthrough ?? []).Where(p => p is not null))
        {
            long expected = (long)p.Width * p.Height * 4;
            if (ReadBytes(zip, PassthroughDir + p.File, expected) is { } pixels && pixels.Length == expected)
                world.PassthroughImages[p.File] = new RgbaImage(p.Width, p.Height, pixels);
        }
        long extraBytes = 0;
        foreach (var file in manifest.ExtraFiles ?? [])
        {
            if (ReadBytes(zip, ExtraDir + file, MaxExtraFileBytes) is not { } bytes) continue;
            if ((extraBytes += bytes.Length) > MaxExtraBytes) throw new InvalidDataException("The project's extra files are too large.");
            world.ExtraFiles[file] = bytes;
        }

        settings = manifest.Settings ?? new ProjectSettings();
        // Format 3 added the bridge list; a project from before it has none.
        if (manifest.Format < 3 || settings.Bridges is null) settings.Bridges = [];
        FillMissing(settings);
        // Format 4 added the starter-trader spawns option, on by default; older projects keep the old way.
        if (manifest.Format < 4 && settings.Generator is { } gen) gen.Spawns.NearStarterTrader = false;
        return world;
    }

    /// <summary>
    /// project.json is plain text that people and tools can edit, so a section may be missing or null ("Towns": null).
    /// Each missing part gets its defaults, and null entries are dropped from the lists, so the editor opens the
    /// project instead of failing on it later (#22).
    /// </summary>
    private static void FillMissing(ProjectSettings settings)
    {
        settings.Bridges.RemoveAll(b => b is null);
        if (settings.Generator is not { } g) return;
        g.Terrain ??= new();
        g.Terrain.BiomeWeights ??= [];
        g.Terrain.Edges ??= [Ops.EdgeStyle.Land, Ops.EdgeStyle.Land, Ops.EdgeStyle.Land, Ops.EdgeStyle.Land];
        g.Terrain.Shape ??= new();
        g.Towns ??= new();
        g.Towns.Rows ??= [];
        g.Towns.Rows.RemoveAll(r => r is null);
        g.Features ??= new();
        g.Features.Rows ??= [];
        g.Features.Rows.RemoveAll(r => r is null);
        g.Highways ??= new();
        g.Wilderness ??= new();
        g.Spawns ??= new();
        g.Spawns.Biomes ??= [];
        g.Spawns.Biomes.RemoveAll(b => b is null);
        g.Sites ??= [];
        g.Sites.RemoveAll(s => s is null);
        g.PlacedFeatures ??= [];
        g.PlacedFeatures.RemoveAll(f => f is null);
        g.PlacedRoads ??= [];
        g.PlacedRoads.RemoveAll(r => r is null);
        g.PlacedWilderness ??= [];
        g.PlacedWilderness.RemoveAll(p => p is null);
        g.PlacedSpawns ??= [];
        g.PlacedSpawns.RemoveAll(s => s is null);
        g.Done ??= [];
        if (g.Biomes is { } b)
        {
            b.Shares ??= [];
            b.PlannedFor ??= new();
        }
    }

    private static void WriteGrid<T>(ZipArchive zip, string name, Grid<T> grid, WorldSnapshot? snapshot) where T : unmanaged
    {
        // Fastest still shrinks typical layers 5-50x; Optimal costs seconds on an 8k heightmap.
        using var entry = zip.CreateEntry(name, CompressionLevel.Fastest).Open();
        if (snapshot?.Find(grid) is { } frozen) frozen.WriteTo(entry);
        else entry.Write(MemoryMarshal.AsBytes(grid.Data.AsSpan()));
    }

    private static void WriteBytes(ZipArchive zip, string name, byte[] bytes)
    {
        using var entry = zip.CreateEntry(name, CompressionLevel.Fastest).Open();
        entry.Write(bytes);
    }

    /// <summary>
    /// Checks everything in the manifest that decides how much is allocated or where files go, before anything is read:
    /// a project may come from someone else.
    /// </summary>
    private static void CheckManifest(Manifest m)
    {
        if (m.Size <= 0 || m.Size > MaxWorldSize) throw new InvalidDataException($"project.json has a world size of {m.Size} m; it must be 1 to {MaxWorldSize}.");
        void Layer(string what, Size2? size)
        {
            if (size is { } s && (s.Width <= 0 || s.Height <= 0 || s.Width > m.Size || s.Height > m.Size))
                throw new InvalidDataException($"The project's {what} layer is {s.Width}×{s.Height}, which doesn't fit a {m.Size} m world.");
        }
        Layer("biome", m.Biomes);
        Layer("radiation", m.Radiation);
        if (m.BiomePalette is { Count: > 256 }) throw new InvalidDataException("The project's biome palette has more than 256 colours.");
        foreach (var p in (m.Passthrough ?? []).Where(p => p is not null))
        {
            if (p.File is not (WorldFiles.Biomes or WorldFiles.Roads or WorldFiles.Water or WorldFiles.Radiation))
                throw new InvalidDataException($"The project keeps an original image for '{p.File}', which isn't a layer image.");
            Layer(p.File, new Size2(p.Width, p.Height));
        }
        foreach (var file in m.ExtraFiles ?? [])
            if (file is null || !WorldFiles.IsSafeExtraName(file))
                throw new InvalidDataException($"The project lists an extra file named '{file}', which isn't a plain file name for a world folder.");
    }

    /// <summary>
    /// A project saved before #16 can hold a biome or radiation layer whose size doesn't fit the world. Those are turned
    /// back into their images and kept as they were, as <see cref="WorldReader"/> now does for a world folder.
    /// </summary>
    private static void KeepUnfittingLayers(World world)
    {
        if (world.Biomes is { } b && !WorldFiles.CoarseLayerFits(world.Size, b.Indices.Width, b.Indices.Height))
        {
            world.PassthroughImages[WorldFiles.Biomes] = ToImage(b.Indices.Width, b.Indices.Height, (y, row) => LayerImages.EncodeBiomesRow(b, y, row));
            world.Warnings.Add(WorldFiles.CoarseLayerKept(WorldFiles.Biomes, world.Size, b.Indices.Width, b.Indices.Height));
            world.Biomes = null;
        }
        if (world.Radiation is { } r && !WorldFiles.CoarseLayerFits(world.Size, r.Width, r.Height))
        {
            world.PassthroughImages[WorldFiles.Radiation] = ToImage(r.Width, r.Height, (y, row) => LayerImages.EncodeRadiationRow(r, y, row));
            world.Warnings.Add(WorldFiles.CoarseLayerKept(WorldFiles.Radiation, world.Size, r.Width, r.Height));
            world.Radiation = null;
        }
    }

    private delegate void RowEncoder(int y, Span<byte> row);

    private static RgbaImage ToImage(int width, int height, RowEncoder encode)
    {
        var pixels = new byte[(long)width * height * 4];
        for (int y = 0; y < height; y++) encode(y, pixels.AsSpan(y * width * 4, width * 4));
        return new RgbaImage(width, height, pixels);
    }

    private static Grid<T>? ReadGrid<T>(ZipArchive zip, string name, int width, int height) where T : unmanaged
    {
        if (zip.GetEntry(name) is not { } entry) return null;
        var grid = new Grid<T>(width, height);
        var bytes = MemoryMarshal.AsBytes(grid.Data.AsSpan());
        using var s = entry.Open();
        s.ReadExactly(bytes);
        return grid;
    }

    /// <summary>Reads an entry of at most <paramref name="limit"/> bytes; a larger one is refused.</summary>
    private static byte[]? ReadBytes(ZipArchive zip, string name, long limit)
    {
        if (zip.GetEntry(name) is not { } entry) return null;
        if (entry.Length > limit) throw new InvalidDataException($"{name} in the project is too large.");
        var bytes = new byte[entry.Length];
        using var s = entry.Open();
        s.ReadExactly(bytes);
        return bytes;
    }
}
