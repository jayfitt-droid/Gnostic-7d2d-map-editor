using System.Globalization;
using System.Text;

namespace MapEditor.Core.GameLink;

/// <summary>A short-lived menu request stored locally, so names can contain spaces and Unicode.</summary>
internal sealed class GameLinkOpenRequest(string world, string save, bool create)
{
    private const int MaxBytes = 4096;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    public string World { get; } = world;
    public string Save { get; } = save;
    public bool Create { get; } = create;

    public static bool IsFolderName(string name) =>
        name.Length is > 0 and <= 255 && name != "." && name != ".." &&
        !name.EndsWith(".", StringComparison.Ordinal) && !name.EndsWith(" ", StringComparison.Ordinal) &&
        name.All(IsFolderNameCharacter);

    internal static bool IsFolderNameCharacter(char c) => c >= ' ' && "<>:\"/\\|?*".IndexOf(c) < 0;

    public string Write(string directory, DateTimeOffset now)
    {
        if (!IsFolderName(World) || !IsFolderName(Save))
            throw new ArgumentException("Invalid world or save name.");
        string id = GameLinkProtocol.CreateOperationId();
        Directory.CreateDirectory(directory);
        using var file = new FileStream(RequestPath(directory, id), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(file, Utf8);
        writer.WriteLine(now.Add(Lifetime).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        writer.WriteLine(Create ? "create" : "continue");
        writer.WriteLine(World);
        writer.WriteLine(Save);
        return id;
    }

    /// <summary>Claims the file once and deletes it before returning a target to the game.</summary>
    public static string? Take(string directory, string id, DateTimeOffset now, out GameLinkOpenRequest? request)
    {
        request = null;
        if (!GameLinkProtocol.IsOperationId(id)) return "bad_request";
        string path = RequestPath(directory, id);
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None,
                bufferSize: 4096, FileOptions.DeleteOnClose);
            if (file.Length > MaxBytes) return "bad_request";
            using var reader = new StreamReader(file, Utf8, detectEncodingFromByteOrderMarks: false);
            string? expires = reader.ReadLine();
            string? mode = reader.ReadLine();
            string? world = reader.ReadLine();
            string? save = reader.ReadLine();
            if (!long.TryParse(expires, NumberStyles.None, CultureInfo.InvariantCulture, out long expiry) ||
                mode is not ("create" or "continue") || world is null || save is null ||
                !IsFolderName(world) || !IsFolderName(save) || reader.ReadLine() is not null)
                return "bad_request";
            if (expiry <= now.ToUnixTimeSeconds() || expiry > now.Add(Lifetime).ToUnixTimeSeconds())
                return "expired";

            request = new(world, save, mode == "create");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return "bad_request";
        }
    }

    public static string RequestPath(string directory, string id) => Path.Combine(directory, "open-" + id + ".txt");
}
