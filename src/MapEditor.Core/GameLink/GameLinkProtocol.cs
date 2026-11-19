using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace MapEditor.Core.GameLink;

internal enum GameLinkRequestKind { Status, Teleport, Waypoint }
internal enum GameLinkParseResult { Ok, BadRequest, UnsupportedProtocol }

/// <summary>A validated request. A teleport's facing is selected from fixed protocol values.</summary>
internal readonly struct GameLinkRequest(
    GameLinkRequestKind kind, int x = 0, int z = 0, string? facing = null, string? prefabName = null)
{
    public GameLinkRequestKind Kind { get; } = kind;
    public int X { get; } = x;
    public int Z { get; } = z;
    public string? Facing { get; } = facing;
    public string? PrefabName { get; } = prefabName;
}

/// <summary>Game Link v1 request framing and parsing, shared with the game mod.</summary>
internal static class GameLinkProtocol
{
    public const int Version = 1;
    public const int Port = 7790;
    public const int MaxRequestBytes = 128;
    public const int ReadTimeoutMilliseconds = 2000;
    private const string RequestName = "GNOSTICLINK";
    private static readonly string VersionText = Version.ToString(CultureInfo.InvariantCulture);
    public static readonly string RequestPrefix = RequestName + " " + VersionText + " ";

    public static bool IsPrefabName(string name) =>
        name.Length is >= 1 and <= 64 &&
        name.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-');

    /// <summary>Reads one line, including its terminator in the byte limit.</summary>
    public static GameLinkParseResult ReadRequest(Stream stream, out GameLinkRequest request)
    {
        request = default;
        byte[] buffer = new byte[MaxRequestBytes];
        var elapsed = Stopwatch.StartNew();
        int length = 0;
        while (length < buffer.Length)
        {
            int remaining = ReadTimeoutMilliseconds - (int)elapsed.ElapsedMilliseconds;
            if (remaining <= 0)
                break;
            if (stream.CanTimeout)
                stream.ReadTimeout = remaining;

            int count = stream.Read(buffer, length, buffer.Length - length);
            if (count == 0)
                break;

            int newline = Array.IndexOf(buffer, (byte)'\n', length, count);
            if (newline >= 0)
            {
                int end = newline > 0 && buffer[newline - 1] == '\r' ? newline - 1 : newline;
                return ParseRequest(buffer, end, out request);
            }

            length += count;
        }

        return GameLinkParseResult.BadRequest;
    }

    private static GameLinkParseResult ParseRequest(byte[] buffer, int length, out GameLinkRequest request)
    {
        request = default;
        // The request grammar uses only printable ASCII, a subset of UTF-8.
        for (int i = 0; i < length; i++)
            if (buffer[i] is < 0x20 or > 0x7e)
                return GameLinkParseResult.BadRequest;

        string[] parts = Encoding.ASCII.GetString(buffer, 0, length).Split(' ');
        if (parts.Length < 3 || parts[0] != RequestName || parts[2].Length == 0)
            return GameLinkParseResult.BadRequest;
        if (parts[1] != VersionText)
        {
            return parts[1].Length > 0 && parts[1].All(c => c is >= '0' and <= '9')
                ? GameLinkParseResult.UnsupportedProtocol
                : GameLinkParseResult.BadRequest;
        }

        if (parts.Length == 3 && parts[2] == "STATUS")
        {
            request = new(GameLinkRequestKind.Status);
            return GameLinkParseResult.Ok;
        }

        if (parts.Length != 6 ||
            !int.TryParse(parts[3], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int x) ||
            !int.TryParse(parts[4], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int z))
            return GameLinkParseResult.BadRequest;

        switch (parts[2])
        {
            case "TELEPORT":
                string? facing = parts[5] switch
                {
                    "n" => "n",
                    "ne" => "ne",
                    "e" => "e",
                    "se" => "se",
                    "s" => "s",
                    "sw" => "sw",
                    "w" => "w",
                    "nw" => "nw",
                    "-" => "-",
                    _ => null
                };
                if (facing == null)
                    return GameLinkParseResult.BadRequest;
                request = new(GameLinkRequestKind.Teleport, x, z, facing == "-" ? null : facing);
                return GameLinkParseResult.Ok;
            case "WAYPOINT":
                string name = parts[5];
                if (!IsPrefabName(name))
                    return GameLinkParseResult.BadRequest;
                request = new(GameLinkRequestKind.Waypoint, x, z, prefabName: name);
                return GameLinkParseResult.Ok;
            default:
                return GameLinkParseResult.BadRequest;
        }
    }
}
