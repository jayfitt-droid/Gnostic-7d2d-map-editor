using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace MapEditor.Core.IO;

/// <summary>
/// Reads JSON into a model one value at a time, so a hand edit with one wrong value doesn't lose all the others (#246).
/// A property whose value can't be read (wrong type, a fraction for an integer, an enum number with no member) uses
/// its default; a list item that can't be read is left out. Values that read are read as
/// <see cref="JsonSerializer"/> reads them, so a file that reads in one step gives the same result here.
/// </summary>
public static class TolerantJson
{
    /// <summary>The longest bad value written in full in a note.</summary>
    private const int MaxShownLength = 60;

    /// <summary>
    /// Reads <paramref name="json"/> as <typeparamref name="T"/> and adds one line to <paramref name="resets"/> for each
    /// value it couldn't read. Throws <see cref="JsonException"/> when the text isn't JSON or its top level can't be
    /// read as <typeparamref name="T"/> at all.
    /// </summary>
    public static T? Read<T>(string json, ICollection<string> resets, JsonSerializerOptions? options = null)
    {
        options ??= JsonSerializerOptions.Default;
        var info = options.GetTypeInfo(typeof(T));
        var root = ToNode(JsonSerializer.Deserialize<JsonElement>(json));
        if (root is null) return default;
        if (!Repair(root, info, "", resets))
            throw new JsonException($"The top level can't be read as {typeof(T).Name}.");
        return (T?)root.Deserialize(info);
    }

    /// <summary>
    /// Reads a JSON file as <typeparamref name="T"/>; a missing file, or one that holds only null, gives a new
    /// <typeparamref name="T"/>. Adds a note to <paramref name="notes"/> for each value that was reset. When the file
    /// isn't JSON, or a value was reset, it also keeps a copy of the file beside it (see <see cref="CopyPath"/>), as the
    /// next save writes over the file. Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>
    /// when the file can't be read.
    /// </summary>
    public static T ReadFile<T>(string path, ICollection<string> notes, JsonSerializerOptions? options = null)
        where T : class, new()
    {
        if (!File.Exists(path)) return new T();
        string text = File.ReadAllText(path);
        string name = Path.GetFileName(path);
        var resets = new List<string>();
        T? value;
        try
        {
            value = Read<T>(text, resets, options);
        }
        catch (JsonException ex)
        {
            notes.Add($"{name} can't be read, so all its values use their defaults: {ex.Message}{KeepCopy(path)}");
            return new T();
        }

        if (resets.Count > 0)
        {
            foreach (string reset in resets) notes.Add($"{name}: {reset}");
            notes.Add($"{name}: {resets.Count} value(s) were reset.{KeepCopy(path)}");
        }
        return value ?? new T();
    }

    /// <summary>Where <see cref="ReadFile"/> keeps a copy of a file it couldn't read in full, taken at <paramref name="time"/>.</summary>
    public static string CopyPath(string path, DateTime time) => $"{path}.{time:yyyyMMdd-HHmmss}.bad";

    /// <summary>Copies the file beside itself; returns the sentence for the note.</summary>
    private static string KeepCopy(string path)
    {
        string copy = CopyPath(path, DateTime.Now);
        try
        {
            File.Copy(path, copy, overwrite: true);
            return $" A copy of the file as it was is in {copy}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $" A copy of the file couldn't be kept: {ex.Message}";
        }
    }

    /// <summary>
    /// Removes the values in <paramref name="node"/> that can't be read as <paramref name="info"/>'s type, and returns
    /// whether what is left can be read.
    /// </summary>
    private static bool Repair(JsonNode? node, JsonTypeInfo info, string path, ICollection<string> resets)
    {
        switch (node)
        {
            case JsonObject obj when info.Kind == JsonTypeInfoKind.Object:
                foreach (var (name, value) in obj.ToList())
                {
                    if (FindProperty(info, name) is not { } property) continue;
                    string at = path.Length == 0 ? name : $"{path}.{name}";
                    // A property with its own converter reads its value as a whole.
                    bool ok = property.CustomConverter is null && value is JsonObject or JsonArray
                        ? Repair(value, info.Options.GetTypeInfo(property.PropertyType), at, resets)
                        : ReadsAsProperty(name, value, info, property);
                    if (ok) continue;
                    obj.Remove(name);
                    resets.Add($"{at} can't be read from {Show(value)}, so it uses its default.");
                }
                return CanRead(obj, info);

            case JsonArray array when info.Kind == JsonTypeInfoKind.Enumerable && info.ElementType is { } elementType:
                var elementInfo = info.Options.GetTypeInfo(elementType);
                var bad = new List<int>();
                for (int i = 0; i < array.Count; i++)
                {
                    if (Repair(array[i], elementInfo, $"{path}[{i}]", resets)) continue;
                    bad.Add(i);
                    resets.Add($"{path}[{i}] can't be read from {Show(array[i])}, so it is left out.");
                }
                for (int i = bad.Count - 1; i >= 0; i--) array.RemoveAt(bad[i]);
                return CanRead(array, info);

            case JsonObject map when info.Kind == JsonTypeInfoKind.Dictionary && info.ElementType is { } valueType:
                var valueInfo = info.Options.GetTypeInfo(valueType);
                foreach (var (key, value) in map.ToList())
                {
                    string at = path.Length == 0 ? key : $"{path}.{key}";
                    if (Repair(value, valueInfo, at, resets) && CanRead(new JsonObject { [key] = value?.DeepClone() }, info))
                        continue;
                    map.Remove(key);
                    resets.Add($"{at} can't be read from {Show(value)}, so it is left out.");
                }
                return CanRead(map, info);

            default:
                return CanRead(node, info);
        }
    }

    /// <summary>
    /// Builds a node tree. Unlike <c>JsonNode.Parse</c>, a name
    /// given twice in one object keeps its last value, as the serializer reads it, instead of throwing.
    /// </summary>
    private static JsonNode? ToNode(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var obj = new JsonObject();
                foreach (var property in element.EnumerateObject()) obj[property.Name] = ToNode(property.Value);
                return obj;
            case JsonValueKind.Array:
                var array = new JsonArray();
                foreach (var item in element.EnumerateArray()) array.Add(ToNode(item));
                return array;
            case JsonValueKind.Null:
                return null;
            default:
                return JsonValue.Create(element);
        }
    }

    /// <summary>The property that reads a JSON name, matched as the serializer matches it.</summary>
    private static JsonPropertyInfo? FindProperty(JsonTypeInfo info, string name)
    {
        var comparison = info.Options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return info.Properties.FirstOrDefault(p => string.Equals(p.Name, name, comparison));
    }

    /// <summary>
    /// Whether one property's value reads, tried in an object that holds only that property, so the property's own
    /// converter and attributes apply.
    /// </summary>
    private static bool ReadsAsProperty(string name, JsonNode? value, JsonTypeInfo owner, JsonPropertyInfo property)
    {
        if (owner.CreateObject is null || property.Get is null)
            return CanRead(value, owner.Options.GetTypeInfo(property.PropertyType));
        try
        {
            object? read = new JsonObject { [name] = value?.DeepClone() }.Deserialize(owner);
            return read is not null && !IsUnknownEnum(property.Get(read));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool CanRead(JsonNode? node, JsonTypeInfo info)
    {
        try
        {
            return !IsUnknownEnum(node.Deserialize(info));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether a value is an enum number with no member. The serializer reads any number into an enum; a later version
    /// may have added the member, but this version can't use it.
    /// </summary>
    private static bool IsUnknownEnum(object? value) =>
        value is Enum e && e.GetType().GetCustomAttribute<FlagsAttribute>() is null && !Enum.IsDefined(e.GetType(), e);

    private static string Show(JsonNode? value)
    {
        string text = value?.ToJsonString() ?? "null";
        return text.Length <= MaxShownLength ? text : text[..MaxShownLength] + "...";
    }
}
