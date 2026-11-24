using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MapEditor.Core.GameLink;

/// <summary>A playtest note at the player's game position and heading.</summary>
public sealed class GameLinkPin(Guid id, float x, float y, float z, float yaw, string world, string save,
    DateTimeOffset time, string note)
{
    public Guid Id { get; } = id;
    public float X { get; } = x;
    public float Y { get; } = y;
    public float Z { get; } = z;
    public float Yaw { get; } = yaw;
    public string World { get; } = world;
    public string Save { get; } = save;
    public DateTimeOffset Time { get; } = time;
    public string Note { get; } = note;
}

/// <summary>The mod's append-only queue format, with one tab-separated pin per line.</summary>
internal static class GameLinkPinFile
{
    internal const int MaxNoteLength = 200;
    internal const int MaxNameLength = 255;
    internal const int MaxBatchSize = 8;
    internal const int MaxReplyBytes = 4096;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static string CleanNote(string note)
    {
        var text = new StringBuilder(MaxNoteLength);
        foreach (char c in note)
        {
            if (char.IsControl(c) || c is '\u2028' or '\u2029') continue;
            if (text.Length == MaxNoteLength) break;
            text.Append(c);
        }
        if (text.Length > 0 && char.IsHighSurrogate(text[text.Length - 1])) text.Length--;
        return text.ToString();
    }

    internal static bool TryId(string value, out Guid id) =>
        Guid.TryParseExact(value, "N", out id) && id != Guid.Empty && id.ToString("N") == value;

    internal static string WorldKey(string world)
    {
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Utf8.GetBytes(world.ToUpperInvariant()));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    internal static bool IsWorldKey(string key) =>
        key.Length == 64 && key.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static bool IsValid(GameLinkPin pin) =>
        pin.Id != Guid.Empty && IsFinite(pin.X) && IsFinite(pin.Y) && IsFinite(pin.Z) && IsFinite(pin.Yaw) &&
        pin.World is { Length: > 0 } && IsName(pin.World) && IsName(pin.Save) &&
        pin.Note is { Length: <= MaxNoteLength } && pin.Note == CleanNote(pin.Note);

    internal static string WriteLine(GameLinkPin pin) => FormattableString.Invariant(
        $"{pin.Id:N}\t{pin.X:R}\t{pin.Y:R}\t{pin.Z:R}\t{pin.Yaw:R}\t{pin.Time:O}\t{Encode(pin.World)}\t{Encode(pin.Save)}\t{Encode(pin.Note)}");

    internal static IEnumerable<GameLinkPin> Read(TextReader reader)
    {
        var seen = new HashSet<Guid>();
        while (reader.ReadLine() is { } line)
            if (ParseLine(line) is { } pin && seen.Add(pin.Id))
                yield return pin;
    }

    /// <summary>Reads past a collected pin without acknowledging it. A missing cursor restarts at the oldest remaining pin.</summary>
    internal static List<GameLinkPin> ReadBatch(IEnumerable<GameLinkPin> pins, string? worldKey, Guid after)
    {
        var batch = new List<GameLinkPin>(MaxBatchSize);
        bool pastCursor = after == Guid.Empty;
        foreach (var pin in pins)
        {
            if (worldKey is not null && worldKey != WorldKey(pin.World)) continue;
            if (!pastCursor && pin.Id == after)
            {
                pastCursor = true;
                batch.Clear();
                continue;
            }
            if (batch.Count < MaxBatchSize) batch.Add(pin);
            if (pastCursor && batch.Count == MaxBatchSize) break;
        }
        return batch;
    }

    /// <summary>Removes the acknowledged prefix of that pin's world queue, leaving other worlds in place.</summary>
    internal static bool Acknowledge(List<GameLinkPin> pins, Guid id)
    {
        int index = pins.FindIndex(pin => pin.Id == id);
        if (index < 0) return false;
        string world = pins[index].World;
        for (int i = index; i >= 0; i--)
            if (string.Equals(pins[i].World, world, StringComparison.OrdinalIgnoreCase))
                pins.RemoveAt(i);
        return true;
    }

    private static GameLinkPin? ParseLine(string line)
    {
        string[] fields = line.Split('\t');
        if (fields.Length != 9 || !TryId(fields[0], out Guid id) ||
            !TryFloat(fields[1], out float x) || !TryFloat(fields[2], out float y) ||
            !TryFloat(fields[3], out float z) || !TryFloat(fields[4], out float yaw) ||
            !DateTimeOffset.TryParseExact(fields[5], "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            return null;

        try
        {
            var pin = new GameLinkPin(id, x, y, z, yaw, Decode(fields[6]), Decode(fields[7]), time, Decode(fields[8]));
            return IsValid(pin) ? pin : null;
        }
        catch (Exception ex) when (ex is FormatException or DecoderFallbackException)
        {
            return null;
        }
    }

    private static bool TryFloat(string field, out float value) =>
        float.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static bool IsName(string name) =>
        name is { Length: <= MaxNameLength } && !name.Any(c => char.IsControl(c) || c is '\u2028' or '\u2029');

    private static string Encode(string text) => Convert.ToBase64String(Utf8.GetBytes(text));
    private static string Decode(string text) => Utf8.GetString(Convert.FromBase64String(text));
}
