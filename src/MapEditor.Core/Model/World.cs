using System.Globalization;
using System.Xml.Linq;

namespace MapEditor.Core.Model;

/// <summary>Road surface codes stored in <see cref="World.Roads"/>; mapped to splat3 colours on export.</summary>
public enum RoadType : byte
{
    None = 0,
    Gravel = 1,
    Asphalt = 2,
    /// <summary>RWG's river-path id; written as pure blue in splat3.</summary>
    Water = 3,
}

/// <summary>Classifies paint stored in the road layer.</summary>
public static class RoadPaint
{
    /// <summary>Returns true for asphalt or gravel road paint.</summary>
    public static bool IsRoad(byte paint) => paint is (byte)RoadType.Asphalt or (byte)RoadType.Gravel;
}

public readonly record struct Int3(int X, int Y, int Z)
{
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{X},{Y},{Z}");
}

/// <summary>One &lt;decoration&gt; from prefabs.xml. Position is the bounding-box min corner in world coords.</summary>
public sealed class PrefabInstance
{
    public required string Name { get; set; }
    public Int3 Position { get; set; }
    public int Rotation { get; set; }
    public string Type { get; set; } = "model";
    /// <summary>Attributes we do not interpret, kept so they survive a save.</summary>
    public List<KeyValuePair<string, string>> ExtraAttributes { get; } = [];

    /// <summary>
    /// The file gave this prefab's Y at ground level (<c>y_is_groundlevel="true"</c>, as the game's own editor
    /// saves), so it is written back that way. See <see cref="IO.GroundLevel"/>.
    /// </summary>
    public bool GroundLevel { get; set; }

    /// <summary>
    /// The YOffset added to the file's ground-level Y to get the box Y in <see cref="Position"/>. Null while a
    /// <see cref="GroundLevel"/> prefab hasn't been converted (it wasn't in the prefab library), so its
    /// Position.Y is still the file's ground-level value.
    /// </summary>
    public int? GroundLevelOffset { get; set; }

    /// <summary>A ground-level prefab whose Y hasn't been converted to box Y yet.</summary>
    public bool NeedsGroundLevelOffset => GroundLevel && GroundLevelOffset is null;

    public PrefabInstance Clone()
    {
        var copy = new PrefabInstance
        {
            Name = Name, Position = Position, Rotation = Rotation, Type = Type,
            GroundLevel = GroundLevel, GroundLevelOffset = GroundLevelOffset,
        };
        copy.ExtraAttributes.AddRange(ExtraAttributes);
        return copy;
    }
}

/// <summary>One &lt;spawnpoint&gt; from spawnpoints.xml, in world coords.</summary>
public sealed class SpawnPoint
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float RotationY { get; set; }

    public SpawnPoint Clone() => (SpawnPoint)MemberwiseClone();
}

/// <summary>Top-down RGBA8 pixels, exactly as decoded from a PNG.</summary>
public sealed record RgbaImage(int Width, int Height, byte[] Pixels);

/// <summary>
/// An indexed colour raster (biomes). Indices point into <see cref="Palette"/>, whose entries are
/// packed 0xRRGGBB colours exactly as they appear in biomes.png.
/// </summary>
public sealed class IndexedLayer(Grid<byte> indices, List<uint> palette)
{
    public Grid<byte> Indices { get; } = indices;
    public List<uint> Palette { get; } = palette;
}

/// <summary>An in-memory 7 Days to Die world folder.</summary>
public sealed class World
{
    public required string Name { get; set; }

    /// <summary>World edge length in metres (= heightmap resolution, since Scale is 1).</summary>
    public required int Size { get; init; }

    /// <summary>dtm.raw: height * 257 as uint16.</summary>
    public required Grid<ushort> Height { get; init; }

    /// <summary>biomes.png, usually Size/8 square.</summary>
    public IndexedLayer? Biomes { get; set; }

    /// <summary>splat3.png roads, Size square.</summary>
    public Grid<byte>? Roads { get; set; }

    /// <summary>splat4.png blue channel: water surface height (0 = dry), Size square.</summary>
    public Grid<byte>? Water { get; set; }

    /// <summary>radiation.png red channel, usually Size/32 square.</summary>
    public Grid<byte>? Radiation { get; set; }

    public List<PrefabInstance> Prefabs { get; } = [];

    private volatile PrefabInstance[]? _shownPrefabs;

    /// <summary>
    /// The prefabs to draw and pick from. While a background edit runs it may change <see cref="Prefabs"/> on another
    /// thread, so the map shows a copy from before the edit until it finishes.
    /// </summary>
    public IReadOnlyList<PrefabInstance> ShownPrefabs => _shownPrefabs ?? (IReadOnlyList<PrefabInstance>)Prefabs;

    /// <summary>Shows a copy of the prefabs as they are now, until <see cref="ShowLivePrefabs"/>.</summary>
    public void HoldShownPrefabs()
    {
        _shownPrefabs = [.. Prefabs];
        _shownSpawns = [.. Spawns];
    }

    public void ShowLivePrefabs()
    {
        _shownPrefabs = null;
        _shownSpawns = null;
    }

    public List<SpawnPoint> Spawns { get; } = [];

    private volatile SpawnPoint[]? _shownSpawns;

    /// <summary>The spawns to draw: like <see cref="ShownPrefabs"/>, a copy from before a background edit while it runs.</summary>
    public IReadOnlyList<SpawnPoint> ShownSpawns => _shownSpawns ?? (IReadOnlyList<SpawnPoint>)Spawns;

    /// <summary>map_info.xml, kept as XML so unknown properties survive.</summary>
    public XDocument MapInfo { get; set; } = new(new XElement("MapInfo"));

    /// <summary>
    /// Original RGBA (top-down) for images whose pixels do not fit our layer encoding. Written back
    /// verbatim while the matching layer is unchanged, so nothing is lost on a round trip.
    /// Key is the file name, e.g. "splat3.png".
    /// </summary>
    public Dictionary<string, RgbaImage> PassthroughImages { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Files we do not model (main.ttw, water_info.xml, splat1.png...), copied as-is.</summary>
    public Dictionary<string, byte[]> ExtraFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Warnings { get; } = [];

    /// <summary>
    /// A deep copy, for writing a snapshot on another thread while editing carries on (autosave).
    /// Passthrough images and extra files are immutable once loaded, so they are shared.
    /// </summary>
    public World Clone()
    {
        var copy = new World
        {
            Name = Name,
            Size = Size,
            Height = Height.Clone(),
            Biomes = Biomes is { } b ? new IndexedLayer(b.Indices.Clone(), [.. b.Palette]) : null,
            Roads = Roads?.Clone(),
            Water = Water?.Clone(),
            Radiation = Radiation?.Clone(),
            MapInfo = new XDocument(MapInfo),
        };
        copy.Prefabs.AddRange(Prefabs.Select(p => p.Clone()));
        copy.Spawns.AddRange(Spawns.Select(s => s.Clone()));
        foreach (var (k, v) in PassthroughImages) copy.PassthroughImages[k] = v;
        foreach (var (k, v) in ExtraFiles) copy.ExtraFiles[k] = v;
        return copy;
    }

    public static float HeightToMetres(ushort raw) => raw / 257f;

    public static ushort MetresToHeight(float metres) => (ushort)Math.Clamp((int)(metres * 257f), 0, ushort.MaxValue);

    /// <summary>Converts a grid column/row to game world X/Z (the world centre is 0,0).</summary>
    public int GridToWorld(int gridCoord) => gridCoord - Size / 2;

    public int WorldToGrid(int worldCoord) => worldCoord + Size / 2;
}
