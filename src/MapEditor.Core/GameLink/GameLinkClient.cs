using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MapEditor.Core.Model;

namespace MapEditor.Core.GameLink;

public enum GameLinkState { Menu, Loading, Ready, Dead, Remote }

public enum GameLinkError
{
    None, Unavailable, NoReply, InvalidReply, BadRequest, UnsupportedProtocol, NotReady, Dead, Remote,
    OffMap, Busy, Failed, PoiNotFound, AlreadySet, PictureFailed, PictureRenderTimeout,
}

/// <summary>The local player's game coordinates and clockwise heading from north, in degrees.</summary>
public sealed record GameLinkPlayer(float X, float Z, float Yaw);

/// <summary>The running game's status. Player is present only when the game is ready.</summary>
public sealed record GameLinkStatus(string World, bool Hosting, GameLinkState State, string ModVersion, GameLinkPlayer? Player);

/// <summary>A Game Link response or a failed attempt to reach the mod.</summary>
public sealed record GameLinkReply(GameLinkError Error, string? Message = null, GameLinkStatus? Status = null, string? FileName = null)
{
    public bool Ok => Error == GameLinkError.None;
}

/// <summary>Sends one typed request per connection to Gnostic Game Link on IPv4 loopback.</summary>
public sealed class GameLinkClient
{
    private const int MaxReplyBytes = 4096;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PictureReplyTimeout = TimeSpan.FromSeconds(GameLinkProtocol.PictureReplyTimeoutSeconds);
    internal static readonly Regex PictureName = new(@"\Agnostic-[0-9]{8}-[0-9]{6}-[0-9]{3}\.png\z");
    private readonly int _port;
    private readonly TimeSpan _replyTimeout;

    public GameLinkClient() : this(GameLinkProtocol.Port, TimeSpan.FromSeconds(5)) { }

    internal GameLinkClient(int port, TimeSpan replyTimeout)
    {
        _port = port;
        _replyTimeout = replyTimeout;
    }

    public Task<GameLinkReply> GetStatusAsync(CancellationToken cancel = default) =>
        SendAsync("STATUS", GameLinkRequestKind.Status, cancel);

    public Task<GameLinkReply> TeleportAsync(int x, int z, Facing? facing, CancellationToken cancel = default)
    {
        if (facing is { } direction && !Enum.IsDefined(direction))
            return Task.FromResult(new GameLinkReply(GameLinkError.BadRequest));
        string token = facing?.ToString().ToLowerInvariant() ?? "-";
        return SendAsync(FormattableString.Invariant($"TELEPORT {x} {z} {token}"), GameLinkRequestKind.Teleport, cancel);
    }

    public Task<GameLinkReply> AddWaypointAsync(int x, int z, string prefabName, CancellationToken cancel = default) =>
        GameLinkProtocol.IsPrefabName(prefabName)
            ? SendAsync(FormattableString.Invariant($"WAYPOINT {x} {z} {prefabName}"), GameLinkRequestKind.Waypoint, cancel)
            : Task.FromResult(new GameLinkReply(GameLinkError.BadRequest));

    /// <summary>Captures a view and returns the validated name of the PNG written by the mod.</summary>
    public Task<GameLinkReply> TakePictureAsync(Int3 camera, Int3 look, CancellationToken cancel = default) =>
        SendAsync(FormattableString.Invariant($"PICTURE {camera.X} {camera.Y} {camera.Z} {look.X} {look.Y} {look.Z}"),
            GameLinkRequestKind.Picture, cancel);

    private async Task<GameLinkReply> SendAsync(string request, GameLinkRequestKind kind, CancellationToken cancel)
    {
        using var client = new TcpClient(AddressFamily.InterNetwork);
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel))
        {
            deadline.CancelAfter(ConnectTimeout);
            try
            {
                await client.ConnectAsync(IPAddress.Loopback, _port, deadline.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                cancel.ThrowIfCancellationRequested();
                return new(GameLinkError.Unavailable);
            }
        }

        using var replyDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        replyDeadline.CancelAfter(kind == GameLinkRequestKind.Picture ? PictureReplyTimeout : _replyTimeout);
        try
        {
            using NetworkStream stream = client.GetStream();
            byte[] line = Encoding.UTF8.GetBytes(GameLinkProtocol.RequestPrefix + request + "\n");
            await stream.WriteAsync(line, replyDeadline.Token).ConfigureAwait(false);
            return await ReadReplyAsync(stream, kind, replyDeadline.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException)
        {
            cancel.ThrowIfCancellationRequested();
            return new(GameLinkError.NoReply);
        }
    }

    private static async Task<GameLinkReply> ReadReplyAsync(Stream stream, GameLinkRequestKind kind, CancellationToken cancel)
    {
        byte[] buffer = new byte[MaxReplyBytes];
        int length = 0;
        while (length < buffer.Length)
        {
            int count = await stream.ReadAsync(buffer.AsMemory(length), cancel).ConfigureAwait(false);
            if (count == 0)
                return new(length == 0 ? GameLinkError.NoReply : GameLinkError.InvalidReply);
            int newline = Array.IndexOf(buffer, (byte)'\n', length, count);
            if (newline >= 0)
                return ParseReply(buffer.AsMemory(0, newline), kind);
            length += count;
        }
        return new(GameLinkError.InvalidReply);
    }

    private static GameLinkReply ParseReply(ReadOnlyMemory<byte> line, GameLinkRequestKind kind)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("ok", out var ok) || ok.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return new(GameLinkError.InvalidReply);

            string? message = StringProperty(root, "message");
            if (!ok.GetBoolean())
                return new(ParseError(StringProperty(root, "error")), message);
            if (kind == GameLinkRequestKind.Status)
                return ParseStatus(root, message);
            if (kind == GameLinkRequestKind.Picture)
                return StringProperty(root, "file") is { } file && PictureName.IsMatch(file)
                    ? new(GameLinkError.None, message, FileName: file)
                    : new(GameLinkError.InvalidReply);
            return new(GameLinkError.None, message);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return new(GameLinkError.InvalidReply);
        }
    }

    private static GameLinkReply ParseStatus(JsonElement root, string? message)
    {
        if (!root.TryGetProperty("protocol", out var version) ||
            version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int protocol))
            return new(GameLinkError.InvalidReply);
        if (protocol != GameLinkProtocol.Version)
            return new(GameLinkError.UnsupportedProtocol);

        GameLinkState? state = StringProperty(root, "state") switch
        {
            "menu" => GameLinkState.Menu,
            "loading" => GameLinkState.Loading,
            "ready" => GameLinkState.Ready,
            "dead" => GameLinkState.Dead,
            "remote" => GameLinkState.Remote,
            _ => null,
        };
        if (state is null || StringProperty(root, "world") is not { } world ||
            StringProperty(root, "mod") is not { Length: > 0 } mod ||
            !root.TryGetProperty("hosting", out var hosting) || hosting.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return new(GameLinkError.InvalidReply);

        GameLinkPlayer? player = null;
        if (state == GameLinkState.Ready)
        {
            if (!root.TryGetProperty("player", out var position) || position.ValueKind != JsonValueKind.Object ||
                !TryNumber(position, "x", out float x) || !TryNumber(position, "z", out float z) ||
                !TryNumber(position, "yaw", out float yaw))
                return new(GameLinkError.InvalidReply);
            player = new(x, z, yaw);
        }
        return new(GameLinkError.None, message, new(world, hosting.GetBoolean(), state.Value, mod, player));
    }

    private static string? StringProperty(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool TryNumber(JsonElement root, string name, out float number)
    {
        number = 0;
        return root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
            value.TryGetSingle(out number) && float.IsFinite(number);
    }

    private static GameLinkError ParseError(string? error) => error switch
    {
        "bad_request" => GameLinkError.BadRequest,
        "unsupported_protocol" => GameLinkError.UnsupportedProtocol,
        "not_ready" => GameLinkError.NotReady,
        "dead" => GameLinkError.Dead,
        "remote" => GameLinkError.Remote,
        "off_map" => GameLinkError.OffMap,
        "busy" => GameLinkError.Busy,
        "failed" => GameLinkError.Failed,
        "poi_not_found" => GameLinkError.PoiNotFound,
        "already_set" => GameLinkError.AlreadySet,
        "picture_failed" => GameLinkError.PictureFailed,
        "picture_render_timeout" => GameLinkError.PictureRenderTimeout,
        _ => GameLinkError.InvalidReply,
    };
}
