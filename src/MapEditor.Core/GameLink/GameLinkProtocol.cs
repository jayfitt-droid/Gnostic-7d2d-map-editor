using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MapEditor.Core.GameLink;

internal enum GameLinkRequestKind { Status, Teleport, Waypoint, Picture }
internal enum GameLinkParseResult { Ok, BadRequest, UnsupportedProtocol, InvalidToken }

/// <summary>A validated request. A teleport's facing is selected from fixed protocol values.</summary>
internal readonly struct GameLinkRequest(
    GameLinkRequestKind kind, int x = 0, int z = 0, string? facing = null, string? prefabName = null,
    int y = 0, int lookX = 0, int lookY = 0, int lookZ = 0)
{
    public GameLinkRequestKind Kind { get; } = kind;
    public int X { get; } = x;
    public int Z { get; } = z;
    public int Y { get; } = y;
    public int LookX { get; } = lookX;
    public int LookY { get; } = lookY;
    public int LookZ { get; } = lookZ;
    public string? Facing { get; } = facing;
    public string? PrefabName { get; } = prefabName;
}

/// <summary>Game Link request framing and parsing, shared with the game mod.</summary>
internal static class GameLinkProtocol
{
    public const int Version = 2;
    public const int Port = 7790;
    public const int MaxRequestBytes = 160;
    public const int SessionTokenLength = 32;
    public const int ReadTimeoutMilliseconds = 2000;
    internal const float PictureVerticalFov = 50f;
    internal const float PictureMinimumAspect = 1.6f;
    internal const int PictureReplyTimeoutSeconds = 30;
    private const string RequestName = "GNOSTICLINK";
    private static readonly string VersionText = Version.ToString(CultureInfo.InvariantCulture);
    public static readonly string RequestPrefix = RequestName + " " + VersionText + " ";
    private static readonly string[] CompassFacings = new[] { "n", "ne", "e", "se", "s", "sw", "w", "nw" };

    public static string SessionTokenPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "7DaysToDie", "GnosticGameLink", "session-token");

    public static string CreateSessionToken()
    {
        byte[] bytes = new byte[SessionTokenLength / 2];
        using var random = RandomNumberGenerator.Create();
        random.GetBytes(bytes);
        return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }

    public static bool IsSessionToken(string token) =>
        token.Length == SessionTokenLength && token.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static bool IsPrefabName(string name) =>
        name.Length is >= 1 and <= 64 &&
        name.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-');

    /// <summary>Reads one line, including its terminator in the byte limit.</summary>
    public static GameLinkParseResult ReadRequest(Stream stream, string sessionToken, out GameLinkRequest request)
    {
        request = default;
        byte[] buffer = new byte[MaxRequestBytes];
        var elapsed = Stopwatch.StartNew();
        int length = 0;
        try
        {
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
                    return HasSessionToken(buffer, end, sessionToken)
                        ? ParseRequest(buffer, end, out request)
                        : GameLinkParseResult.InvalidToken;
                }

                length += count;
            }
        }
        catch (IOException)
        {
            // A timed-out or interrupted read may still contain the token field.
        }

        return HasSessionToken(buffer, length, sessionToken)
            ? GameLinkParseResult.BadRequest
            : GameLinkParseResult.InvalidToken;
    }

    private static bool HasSessionToken(byte[] buffer, int length, string sessionToken)
    {
        // Locate the third field without validating the request name, version or command.
        int firstSpace = Array.IndexOf(buffer, (byte)' ', 0, length);
        if (firstSpace < 0)
            return false;
        int secondSpace = Array.IndexOf(buffer, (byte)' ', firstSpace + 1, length - firstSpace - 1);
        if (secondSpace < 0)
            return false;
        int start = secondSpace + 1;
        int end = Array.IndexOf(buffer, (byte)' ', start, length - start);
        if ((end < 0 ? length : end) - start != SessionTokenLength)
            return false;

        // Compare every character without returning early on a mismatch (also on .NET Framework).
        int difference = 0;
        for (int i = 0; i < SessionTokenLength; i++)
            difference |= buffer[start + i] ^ sessionToken[i];
        return difference == 0;
    }

    private static GameLinkParseResult ParseRequest(byte[] buffer, int length, out GameLinkRequest request)
    {
        request = default;
        // The request grammar uses only printable ASCII, a subset of UTF-8.
        for (int i = 0; i < length; i++)
            if (buffer[i] is < 0x20 or > 0x7e)
                return GameLinkParseResult.BadRequest;

        string[] parts = Encoding.ASCII.GetString(buffer, 0, length).Split(' ');
        if (parts.Length < 4 || parts[0] != RequestName || parts[3].Length == 0)
            return GameLinkParseResult.BadRequest;
        if (parts[1] != VersionText)
        {
            return parts[1].Length > 0 && parts[1].All(c => c is >= '0' and <= '9')
                ? GameLinkParseResult.UnsupportedProtocol
                : GameLinkParseResult.BadRequest;
        }

        if (parts.Length == 4 && parts[3] == "STATUS")
        {
            request = new(GameLinkRequestKind.Status);
            return GameLinkParseResult.Ok;
        }

        if (parts[3] == "PICTURE" && parts.Length == 10 &&
            TryCoordinate(parts[4], out int camX) && TryCoordinate(parts[5], out int camY) &&
            TryCoordinate(parts[6], out int camZ) && TryCoordinate(parts[7], out int lookX) &&
            TryCoordinate(parts[8], out int lookY) && TryCoordinate(parts[9], out int lookZ))
        {
            request = new(GameLinkRequestKind.Picture, camX, camZ, y: camY, lookX: lookX, lookY: lookY, lookZ: lookZ);
            return GameLinkParseResult.Ok;
        }

        if (parts.Length != 7 || !TryCoordinate(parts[4], out int x) || !TryCoordinate(parts[5], out int z))
            return GameLinkParseResult.BadRequest;

        switch (parts[3])
        {
            case "TELEPORT":
                string facing = parts[6];
                if (facing != "-" && Array.IndexOf(CompassFacings, facing) < 0)
                    return GameLinkParseResult.BadRequest;
                request = new(GameLinkRequestKind.Teleport, x, z, facing == "-" ? null : facing);
                return GameLinkParseResult.Ok;
            case "WAYPOINT":
                string name = parts[6];
                if (!IsPrefabName(name))
                    return GameLinkParseResult.BadRequest;
                request = new(GameLinkRequestKind.Waypoint, x, z, prefabName: name);
                return GameLinkParseResult.Ok;
            default:
                return GameLinkParseResult.BadRequest;
        }
    }

    private static bool TryCoordinate(string token, out int coordinate) =>
        int.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out coordinate);
}
