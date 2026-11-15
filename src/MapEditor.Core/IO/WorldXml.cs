using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;

namespace MapEditor.Core.IO;

/// <summary>Readers and writers for prefabs.xml, spawnpoints.xml and map_info.xml.</summary>
public static class WorldXml
{
    private static readonly XmlWriterSettings WriterSettings = new()
    {
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        Indent = true,
        IndentChars = "  ",
    };

    /// <summary>
    /// Reads prefabs.xml. A decoration with no name, or a position or rotation that can't be read, is left out rather
    /// than stopping the whole world opening or landing at 0,0,0 (#20); each one is described in
    /// <paramref name="warnings"/>.
    /// </summary>
    public static List<PrefabInstance> ReadPrefabs(string path, List<string>? warnings = null)
    {
        var result = new List<PrefabInstance>();
        var skipped = new List<string>();
        foreach (var e in XDocument.Load(path, LoadOptions.SetLineInfo).Root?.Elements("decoration") ?? [])
        {
            var prefab = new PrefabInstance { Name = (string?)e.Attribute("name") ?? "" };
            string? problem = prefab.Name.Length == 0 ? "no name" : null;
            bool hasPosition = false;
            foreach (var attr in e.Attributes())
            {
                switch (attr.Name.LocalName)
                {
                    case "name": break;
                    case "type": prefab.Type = attr.Value; break;
                    case "position":
                        if (TryParseInt3(attr.Value, out var position)) { prefab.Position = position; hasPosition = true; }
                        else problem ??= $"position \"{attr.Value}\" isn't three numbers";
                        break;
                    case "rotation":
                        if (int.TryParse(attr.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rotation)) prefab.Rotation = rotation;
                        else problem ??= $"rotation \"{attr.Value}\" isn't a whole number";
                        break;
                    // Only "true" changes how the game reads Y; anything else is kept as it was.
                    case GroundLevel.Attribute when bool.TryParse(attr.Value, out bool ground) && ground: prefab.GroundLevel = true; break;
                    default: prefab.ExtraAttributes.Add(new(attr.Name.LocalName, attr.Value)); break;
                }
            }
            if (!hasPosition) problem ??= "no position";
            if (problem is null) result.Add(prefab);
            else skipped.Add($"{Where(e)}{(prefab.Name.Length > 0 ? " " + prefab.Name : "")}: {problem}");
        }
        Report(warnings, WorldFiles.Prefabs, "decoration", skipped);
        return result;
    }

    /// <summary>"line 12" for an element read with line info, or "" when there is none.</summary>
    private static string Where(XElement e) =>
        e is System.Xml.IXmlLineInfo info && info.HasLineInfo() ? $"line {info.LineNumber}" : "an entry";

    /// <summary>One warning for the entries of <paramref name="file"/> left out, naming the first few.</summary>
    private static void Report(List<string>? warnings, string file, string what, List<string> skipped)
    {
        if (warnings is null || skipped.Count == 0) return;
        const int Shown = 3;
        warnings.Add($"{file}: {skipped.Count} {what}{(skipped.Count == 1 ? "" : "s")} couldn't be read and {(skipped.Count == 1 ? "was" : "were")} left out "
            + $"({string.Join("; ", skipped.Take(Shown))}{(skipped.Count > Shown ? $"; and {skipped.Count - Shown} more" : "")}). "
            + "Exporting writes the file without them.");
    }

    /// <summary>
    /// Writes prefabs.xml, with every prefab <paramref name="library"/> knows at ground level and flagged (see
    /// <see cref="GroundLevel.ForFile"/>).
    /// </summary>
    public static void WritePrefabs(string path, IEnumerable<PrefabInstance> prefabs, PrefabLibrary? library = null)
    {
        using var w = XmlWriter.Create(path, WriterSettings);
        w.WriteStartDocument();
        w.WriteStartElement("prefabs");
        foreach (var p in prefabs)
        {
            w.WriteStartElement("decoration");
            w.WriteAttributeString("type", p.Type);
            w.WriteAttributeString("name", p.Name);
            var (y, atGroundLevel) = GroundLevel.ForFile(p, library);
            w.WriteAttributeString("position", (p.Position with { Y = y }).ToString());
            w.WriteAttributeString("rotation", p.Rotation.ToString(CultureInfo.InvariantCulture));
            if (atGroundLevel) w.WriteAttributeString(GroundLevel.Attribute, "true");
            foreach (var (k, v) in p.ExtraAttributes)
            {
                // A y_is_groundlevel="false" read in is replaced by the flag written above.
                if (atGroundLevel && k == GroundLevel.Attribute) continue;
                w.WriteAttributeString(k, v);
            }
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }

    /// <summary>
    /// Reads spawnpoints.xml. A spawn point whose position is missing or can't be read is left out (and described in
    /// <paramref name="warnings"/>) rather than moved to 0,0,0 (#20). A missing or unreadable rotation faces north.
    /// </summary>
    public static List<SpawnPoint> ReadSpawns(string path, List<string>? warnings = null)
    {
        var result = new List<SpawnPoint>();
        var skipped = new List<string>();
        foreach (var e in XDocument.Load(path, LoadOptions.SetLineInfo).Root?.Elements("spawnpoint") ?? [])
        {
            string? text = (string?)e.Attribute("position");
            if (!TryParseFloats(text, 3, out var pos))
            {
                skipped.Add($"{Where(e)}: {(text is null ? "no position" : $"position \"{text}\" isn't three numbers")}");
                continue;
            }
            float rotationY = TryParseFloats((string?)e.Attribute("rotation"), 3, out var rot) ? rot[1] : 0;
            result.Add(new SpawnPoint { X = pos[0], Y = pos[1], Z = pos[2], RotationY = rotationY });
        }
        Report(warnings, WorldFiles.Spawns, "spawn point", skipped);
        return result;
    }

    public static void WriteSpawns(string path, IEnumerable<SpawnPoint> spawns)
    {
        using var w = XmlWriter.Create(path, WriterSettings);
        w.WriteStartDocument();
        w.WriteStartElement("spawnpoints");
        foreach (var s in spawns)
        {
            w.WriteStartElement("spawnpoint");
            w.WriteAttributeString("position", $"{F(s.X)},{F(s.Y)},{F(s.Z)}");
            w.WriteAttributeString("rotation", $"0,{F(s.RotationY)},0");
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }

    public static XDocument ReadMapInfo(string path) => XDocument.Load(path);

    public static void WriteMapInfo(string path, XDocument mapInfo, int worldSize)
    {
        var doc = new XDocument(mapInfo);
        if (doc.Root is null) doc.Add(new XElement("MapInfo"));
        SetProperty(doc, "HeightMapSize", $"{worldSize},{worldSize}");
        if (GetProperty(doc, "Scale") is null) SetProperty(doc, "Scale", "1");
        using var w = XmlWriter.Create(path, WriterSettings);
        doc.Save(w);
    }

    public static string? GetProperty(XDocument doc, string name) =>
        doc.Root?.Elements("property").FirstOrDefault(p => (string?)p.Attribute("name") == name)?.Attribute("value")?.Value;

    public static void SetProperty(XDocument doc, string name, string value)
    {
        var root = doc.Root ?? throw new InvalidOperationException("map_info has no root element.");
        var existing = root.Elements("property").FirstOrDefault(p => (string?)p.Attribute("name") == name);
        if (existing is not null) existing.SetAttributeValue("value", value);
        else root.Add(new XElement("property", new XAttribute("name", name), new XAttribute("value", value)));
    }

    private static bool TryParseInt3(string text, out Int3 value)
    {
        value = default;
        if (!TryParseFloats(text, 3, out var f)) return false;
        value = new Int3((int)MathF.Round(f[0]), (int)MathF.Round(f[1]), (int)MathF.Round(f[2]));
        return true;
    }

    /// <summary>
    /// Reads <paramref name="count"/> comma-separated numbers. False when there are fewer, or one isn't a number, or
    /// one is too large to be a place in a world (NaN, infinity, beyond a million metres).
    /// </summary>
    private static bool TryParseFloats(string? text, int count, out float[] values)
    {
        values = new float[count];
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split(',');
        if (parts.Length < count) return false;
        for (int i = 0; i < count; i++)
            if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])
                || !float.IsFinite(values[i]) || MathF.Abs(values[i]) > 1_000_000) return false;
        return true;
    }

    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
}
