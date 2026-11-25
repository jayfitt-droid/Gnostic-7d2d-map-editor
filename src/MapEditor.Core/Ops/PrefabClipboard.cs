using System.Text.Json;
using System.Xml;
using MapEditor.Core.GameData;
using MapEditor.Core.IO;
using MapEditor.Core.Model;

namespace MapEditor.Core.Ops;

/// <summary>A portable map selection. Horizontal positions are relative to the copied group.</summary>
public sealed class PrefabClipboard
{
    private const string Marker = "Gnostic map selection\n";
    private const int Version = 1;
    /// <summary>The longest clipboard text that is read, in characters.</summary>
    public const int MaxTextLength = 2_000_000;
    private const int MaxItems = 4096;
    private const int MaxCoordinate = 1_000_000;
    private static readonly JsonSerializerOptions Json = new()
    {
        MaxDepth = 12, RespectRequiredConstructorParameters = true,
    };

    private sealed record Prefab(string Name, string Type, int X, int Z, int Rotation, int Y, bool AtGroundLevel,
        int SourceGround, int? Parent, KeyValuePair<string, string>[] Attributes);
    private sealed record Spawn(float X, float Z, float Rotation, float HeightOffset);
    private sealed record Payload(int Version, Prefab[] Prefabs, Spawn[] Spawns);

    private readonly Payload _data;

    private PrefabClipboard(Payload data) => _data = data;

    public int Count => _data.Prefabs.Length + _data.Spawns.Length;

    /// <summary>Captures the selection and the prefabs that move with it, apart from protected bridge pieces.</summary>
    public static PrefabClipboard Capture(World world, PrefabLibrary library, IEnumerable<PrefabInstance> prefabs,
        IEnumerable<SpawnPoint> spawns, Func<PrefabInstance, bool> isBridgePiece, out int skippedBridges)
    {
        var group = new PrefabGroup(world, library, prefabs, spawns);
        var (dx, dz) = group.DeltaTo(0, 0);
        var layout = group.Layout(dx, dz, 0);
        var allowed = layout.Prefabs.Where(t => !isBridgePiece(t.Prefab)).ToArray();
        skippedBridges = layout.Prefabs.Count - allowed.Length;
        var indices = allowed.Select((t, i) => (t.Prefab, Index: i)).ToDictionary(t => t.Prefab, t => t.Index);
        int half = world.Size / 2;
        return new PrefabClipboard(new Payload(Version, allowed.Select(t =>
        {
            var (p, spot) = t;
            var (y, atGroundLevel) = GroundLevel.ForFile(p, library);
            var cells = PrefabPlacer.Footprint(world, p, library);
            int ground = (library.Find(p.Name) is { IsPart: true } ? null : PrefabPlacer.RoadGround(world, cells))
                ?? PrefabPlacer.MedianGround(world, cells);
            int? parent = group.ParentOf(p) is { } owner && indices.TryGetValue(owner, out int index) ? index : null;
            return new Prefab(p.Name, p.Type, spot.X, spot.Z, p.Rotation & 3, y, atGroundLevel, ground, parent,
                p.ExtraAttributes.ToArray());
        }).ToArray(), layout.Spawns.Select(t =>
        {
            var s = t.Spawn;
            float ground = SpawnHelper.StandingHeight(world, (int)Math.Floor(s.X + half), (int)Math.Floor(s.Z + half));
            return new Spawn((float)t.X, (float)t.Z, t.Rotation, s.Y - ground);
        }).ToArray()));
    }

    /// <summary>Returns clipboard text, or null when the selection cannot fit the clipboard format.</summary>
    public string? Serialize()
    {
        try
        {
            if (!IsValid(_data)) return null;
            string json = JsonSerializer.Serialize(_data, Json);
            return json.Length <= MaxTextLength - Marker.Length ? Marker + json : null;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>Reads a supported Gnostic selection. Rejects foreign, oversized and malformed text.</summary>
    public static PrefabClipboard? Parse(string? text)
    {
        if (text is null || text.Length > MaxTextLength || !text.StartsWith(Marker, StringComparison.Ordinal)) return null;
        try
        {
            var data = JsonSerializer.Deserialize<Payload>(text.AsSpan(Marker.Length), Json);
            return data is not null && IsValid(data) ? new PrefabClipboard(data) : null;
        }
        catch (Exception ex) when (ex is JsonException or XmlException)
        {
            return null;
        }
    }

    /// <summary>Builds a preview and drop group for the target world. Each missing prefab is skipped.</summary>
    public PrefabGroup PreparePaste(World world, PrefabLibrary library, Func<PrefabInstance, bool> isBridgePiece,
        out int skippedPrefabs)
    {
        var prefabs = new PrefabInstance?[_data.Prefabs.Length];
        var prefabOffsets = new Dictionary<PrefabInstance, int>();
        for (int i = 0; i < prefabs.Length; i++)
        {
            var copy = _data.Prefabs[i];
            if (library.Find(copy.Name) is not { } info) continue;
            int y = copy.AtGroundLevel ? copy.Y + info.YOffset : copy.Y;
            var prefab = new PrefabInstance
            {
                Name = copy.Name, Type = copy.Type, Position = new Int3(copy.X, y, copy.Z), Rotation = copy.Rotation,
                GroundLevel = true, GroundLevelOffset = info.YOffset,
            };
            prefab.ExtraAttributes.AddRange(copy.Attributes);
            prefabs[i] = prefab;
            prefabOffsets.Add(prefab, PrefabPlacer.GroundUnder(info, y) - copy.SourceGround);
        }
        skippedPrefabs = prefabs.Length - prefabOffsets.Count;
        var riders = new Dictionary<PrefabInstance, PrefabInstance>();
        for (int i = 0; i < prefabs.Length; i++)
            if (prefabs[i] is { } rider && _data.Prefabs[i].Parent is { } parent && prefabs[parent] is { } root)
                riders.Add(rider, root);
        var spawnOffsets = _data.Spawns.ToDictionary(s => new SpawnPoint { X = s.X, Z = s.Z, RotationY = s.Rotation },
            s => s.HeightOffset);
        return new PrefabGroup(world, library, prefabOffsets.Keys, spawnOffsets.Keys, isBridgePiece, riders, prefabOffsets, spawnOffsets);
    }

    private static bool IsValid(Payload data)
    {
        if (data.Version != Version || data.Prefabs is null || data.Spawns is null ||
            data.Prefabs.Length + data.Spawns.Length is 0 or > MaxItems) return false;
        for (int i = 0; i < data.Prefabs.Length; i++)
        {
            var p = data.Prefabs[i];
            if (p is null || string.IsNullOrWhiteSpace(p.Name) || p.Type is null ||
                !IsCoordinate(p.X) || !IsCoordinate(p.Z) || !IsCoordinate(p.Y) ||
                p.Rotation is < 0 or > 3 || p.SourceGround is < 0 or > 255 ||
                p.Attributes is null || !ValidAttributes(p.Attributes)) return false;
            XmlConvert.VerifyXmlChars(p.Name);
            XmlConvert.VerifyXmlChars(p.Type);
            if (p.Parent is { } parent && ((uint)parent >= data.Prefabs.Length || parent == i ||
                                          data.Prefabs[parent] is not { Parent: null })) return false;
        }
        return data.Spawns.All(s => s is not null && IsCoordinate(s.X) && IsCoordinate(s.Z) &&
            IsCoordinate(s.HeightOffset) && float.IsFinite(s.Rotation));
    }

    private static bool ValidAttributes(IEnumerable<KeyValuePair<string, string>> attributes)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, value) in attributes)
        {
            if (string.IsNullOrEmpty(key) || value is null || key is "type" or "name" or "position" or "rotation" or "xmlns" ||
                !names.Add(key)) return false;
            XmlConvert.VerifyNCName(key);
            XmlConvert.VerifyXmlChars(value);
        }
        return true;
    }

    private static bool IsCoordinate(double value) => value is >= -MaxCoordinate and <= MaxCoordinate;
}
