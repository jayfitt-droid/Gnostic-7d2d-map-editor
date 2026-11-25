using System.Globalization;
using System.Text;

namespace MapEditor.Core.GameLink;

/// <summary>The exact save and position captured for a single rebuild.</summary>
internal sealed class GameLinkRebuildRequest(string world, string save, float x, float z, int radius)
{
    private const int MaxBytes = 4096;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    public string World { get; } = world;
    public string Save { get; } = save;
    public float X { get; } = x;
    public float Z { get; } = z;
    public int Radius { get; } = radius;

    public string Write(string directory, DateTimeOffset now)
    {
        if (!GameLinkOpenRequest.IsFolderName(World) || !GameLinkOpenRequest.IsFolderName(Save) ||
            !IsValid(X, Z, Radius))
            throw new ArgumentException("Invalid rebuild target.");

        string id = GameLinkProtocol.CreateOperationId();
        Directory.CreateDirectory(directory);
        using var file = new FileStream(RequestPath(directory, id), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(file, Utf8);
        writer.WriteLine(now.Add(Lifetime).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        writer.WriteLine(World);
        writer.WriteLine(Save);
        writer.WriteLine(FormattableString.Invariant($"{X:R} {Z:R} {Radius}"));
        return id;
    }

    /// <summary>Claims and deletes the request file before returning its contents.</summary>
    public static string? Take(string directory, string id, DateTimeOffset now, out GameLinkRebuildRequest? request)
    {
        request = null;
        if (!GameLinkProtocol.IsOperationId(id)) return "bad_request";
        try
        {
            using var file = new FileStream(RequestPath(directory, id), FileMode.Open, FileAccess.Read, FileShare.None,
                bufferSize: MaxBytes, FileOptions.DeleteOnClose);
            if (file.Length > MaxBytes) return "bad_request";
            using var reader = new StreamReader(file, Utf8, detectEncodingFromByteOrderMarks: false);
            string? expires = reader.ReadLine();
            string? world = reader.ReadLine();
            string? save = reader.ReadLine();
            string[]? area = reader.ReadLine()?.Split(' ');
            var culture = CultureInfo.InvariantCulture;
            if (!long.TryParse(expires, NumberStyles.None, culture, out long expiry) ||
                world is null || save is null || !GameLinkOpenRequest.IsFolderName(world) ||
                !GameLinkOpenRequest.IsFolderName(save) || area?.Length != 3 ||
                !float.TryParse(area[0], NumberStyles.Float, culture, out float x) ||
                !float.TryParse(area[1], NumberStyles.Float, culture, out float z) ||
                !int.TryParse(area[2], NumberStyles.None, culture, out int radius) ||
                !IsValid(x, z, radius) || reader.ReadLine() is not null)
                return "bad_request";
            if (expiry <= now.ToUnixTimeSeconds() || expiry > now.Add(Lifetime).ToUnixTimeSeconds())
                return "expired";

            request = new(world, save, x, z, radius);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return "bad_request";
        }
    }

    private static bool IsValid(float x, float z, int radius) =>
        GameLinkRebuildArea.IsValidPosition(x) && GameLinkRebuildArea.IsValidPosition(z) &&
        radius == GameLinkRebuildArea.ClampRadius(radius);

    public static string RequestPath(string directory, string id) => Path.Combine(directory, "rebuild-" + id + ".txt");
}
