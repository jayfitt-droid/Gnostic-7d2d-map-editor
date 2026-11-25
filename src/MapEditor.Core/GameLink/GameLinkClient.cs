using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;

namespace MapEditor.Core.GameLink;

public enum GameLinkState { Menu, Loading, Ready, Dead, Remote }

public enum GameLinkError
{
    None, Unavailable, NoReply, InvalidReply, BadRequest, UnsupportedProtocol, NotReady, Dead, Remote,
    OffMap, Busy, Failed, PoiNotFound, AlreadySet, PictureFailed, PictureRenderTimeout,
    InvalidToken, Players, WorldNotFound, SaveNotFound, SaveUnplayable, Expired,
}

/// <summary>The local player's game coordinates and clockwise heading from north, in degrees.</summary>
public sealed record GameLinkPlayer(float X, float Z, float Yaw);

/// <summary>The running game's status. Player is present only when the game is ready.</summary>
public sealed record GameLinkStatus(string World, bool Hosting, GameLinkState State, string ModVersion, GameLinkPlayer? Player)
{
    public string Save { get; init; } = "";
    public bool OtherPlayers { get; init; }
    public GameLinkOperation? Operation { get; init; }
    public GameLinkBlockedSave? Blocked { get; init; }
}

/// <summary>Progress of the latest menu operation, including its failure reason when available.</summary>
public sealed record GameLinkOperation(string Id, string Kind, string State, string Reason)
{
    public int Chunks { get; init; }
}

/// <summary>The save selected on the game's Continue page when Start is disabled.</summary>
public sealed record GameLinkBlockedSave(string World, string Save, string Reason);

/// <summary>A Game Link response or a failed attempt to reach the mod.</summary>
public sealed record GameLinkReply(GameLinkError Error, string? Message = null, GameLinkStatus? Status = null,
    string? FileName = null, string? OperationId = null)
{
    public bool Ok => Error == GameLinkError.None;
    public IReadOnlyList<GameLinkPin>? Pins { get; init; }
}

/// <summary>Sends one typed request per connection to Gnostic Game Link on IPv4 loopback.</summary>
public sealed class GameLinkClient
{
    private const int MaxReplyBytes = GameLinkPinFile.MaxReplyBytes;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PictureReplyTimeout = TimeSpan.FromSeconds(GameLinkProtocol.PictureReplyTimeoutSeconds);
    internal static readonly Regex PictureName = new(@"\Agnostic-[0-9]{8}-[0-9]{6}-[0-9]{3}\.png\z");
    private readonly int _port;
    private readonly TimeSpan _replyTimeout;
    private readonly string _sessionTokenPath;

    public GameLinkClient() : this(GameLinkProtocol.Port, TimeSpan.FromSeconds(5), GameInstall.GameLinkSessionTokenPath) { }

    internal GameLinkClient(int port, TimeSpan replyTimeout, string sessionTokenPath)
    {
        _port = port;
        _replyTimeout = replyTimeout;
        _sessionTokenPath = sessionTokenPath;
    }

    public Task<GameLinkReply> GetStatusAsync(CancellationToken cancel = default) =>
        SendAsync("STATUS", GameLinkRequestKind.Status, cancel);

    public Task<GameLinkReply> LeaveAsync(CancellationToken cancel = default) =>
        SendAsync("LEAVE", GameLinkRequestKind.Leave, cancel);

    /// <summary>Asks the running game to create or continue the named save from its menu.</summary>
    public async Task<GameLinkReply> OpenAsync(string world, string save, bool create, CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();
        string directory = Path.GetDirectoryName(_sessionTokenPath)!;
        string id = new GameLinkOpenRequest(world, save, create).Write(directory, DateTimeOffset.UtcNow);
        try
        {
            return await SendAsync("OPEN " + id, GameLinkRequestKind.Open, cancel).ConfigureAwait(false);
        }
        finally
        {
            try { File.Delete(GameLinkOpenRequest.RequestPath(directory, id)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Continues the same save from the menu and rebuilds around the captured position.</summary>
    public async Task<GameLinkReply> RebuildAsync(string world, string save, GameLinkPlayer centre, int radius,
        CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();
        string directory = Path.GetDirectoryName(_sessionTokenPath)!;
        string id = new GameLinkRebuildRequest(world, save, centre.X, centre.Z, radius).Write(directory, DateTimeOffset.UtcNow);
        try
        {
            return await SendAsync("REBUILD " + id, GameLinkRequestKind.Rebuild, cancel).ConfigureAwait(false);
        }
        finally
        {
            try { File.Delete(GameLinkRebuildRequest.RequestPath(directory, id)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Returns a batch of queued pins, optionally starting after a pin already received for this world.</summary>
    public Task<GameLinkReply> GetPinsAsync(string? worldKey = null, Guid after = default, CancellationToken cancel = default)
    {
        if (worldKey is null ? after != Guid.Empty : !GameLinkPinFile.IsWorldKey(worldKey))
            return Task.FromResult(new GameLinkReply(GameLinkError.BadRequest));

        string request = worldKey is null ? "PINS" : $"PINS {worldKey}";
        if (after != Guid.Empty) request += $" {after:N}";
        return SendAsync(request, GameLinkRequestKind.Pins, cancel);
    }

    /// <summary>Acknowledges the saved prefix of the queue for the pin's world.</summary>
    public Task<GameLinkReply> AckPinsAsync(Guid id, CancellationToken cancel = default) =>
        id == Guid.Empty
            ? Task.FromResult(new GameLinkReply(GameLinkError.BadRequest))
            : SendAsync($"PINSACK {id:N}", GameLinkRequestKind.PinsAck, cancel);

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
            string? sessionToken = await ReadSessionTokenAsync(replyDeadline.Token).ConfigureAwait(false);
            if (sessionToken is null)
                return new(GameLinkError.InvalidToken);

            using NetworkStream stream = client.GetStream();
            byte[] line = Encoding.UTF8.GetBytes(GameLinkProtocol.RequestPrefix + sessionToken + " " + request + "\n");
            await stream.WriteAsync(line, replyDeadline.Token).ConfigureAwait(false);
            return await ReadReplyAsync(stream, kind, replyDeadline.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException)
        {
            cancel.ThrowIfCancellationRequested();
            return new(GameLinkError.NoReply);
        }
    }

    private async Task<string?> ReadSessionTokenAsync(CancellationToken cancel)
    {
        try
        {
            using var file = new FileStream(_sessionTokenPath, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, bufferSize: 4096, useAsync: true);
            byte[] bytes = new byte[GameLinkProtocol.SessionTokenLength + 1];
            int length = await file.ReadAtLeastAsync(bytes, bytes.Length, false, cancel).ConfigureAwait(false);
            string token = Encoding.ASCII.GetString(bytes, 0, length);
            return GameLinkProtocol.IsSessionToken(token) ? token : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
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
            if (kind is GameLinkRequestKind.Leave or GameLinkRequestKind.Open or GameLinkRequestKind.Rebuild)
                return StringProperty(root, "id") is { } id && GameLinkProtocol.IsOperationId(id)
                    ? new(GameLinkError.None, message, OperationId: id)
                    : new(GameLinkError.InvalidReply);
            if (kind == GameLinkRequestKind.Pins)
                return ParsePins(root, message);
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

    private static GameLinkReply ParsePins(JsonElement root, string? message)
    {
        if (!root.TryGetProperty("pins", out var list) || list.ValueKind != JsonValueKind.Array ||
            list.GetArrayLength() > GameLinkPinFile.MaxBatchSize)
            return new(GameLinkError.InvalidReply);

        var pins = new List<GameLinkPin>();
        var ids = new HashSet<Guid>();
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                StringProperty(item, "id") is not { } textId || !GameLinkPinFile.TryId(textId, out var id) || !ids.Add(id) ||
                !TryNumber(item, "x", out float x) || !TryNumber(item, "y", out float y) ||
                !TryNumber(item, "z", out float z) || !TryNumber(item, "yaw", out float yaw) ||
                StringProperty(item, "world") is not { } world || StringProperty(item, "save") is not { } save ||
                !item.TryGetProperty("time", out var time) || time.ValueKind != JsonValueKind.String ||
                !time.TryGetDateTimeOffset(out var timestamp) || StringProperty(item, "note") is not { } note)
                return new(GameLinkError.InvalidReply);

            var pin = new GameLinkPin(id, x, y, z, yaw, world, save, timestamp, note);
            if (!GameLinkPinFile.IsValid(pin)) return new(GameLinkError.InvalidReply);
            pins.Add(pin);
        }
        return new(GameLinkError.None, message) { Pins = pins };
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
        GameLinkOperation? operation = null;
        if (root.TryGetProperty("op", out var op) && op.ValueKind == JsonValueKind.Object &&
            StringProperty(op, "id") is { } id && GameLinkProtocol.IsOperationId(id) &&
            StringProperty(op, "kind") is { } kind && StringProperty(op, "state") is { } phase)
            operation = new(id, kind, phase, StringProperty(op, "reason") ?? "")
            {
                Chunks = op.TryGetProperty("chunks", out var chunks) && chunks.ValueKind == JsonValueKind.Number &&
                    chunks.TryGetInt32(out int count) && count > 0 ? count : 0,
            };

        GameLinkBlockedSave? blockedSave = null;
        if (root.TryGetProperty("blocked", out var blocked) && blocked.ValueKind == JsonValueKind.Object &&
            StringProperty(blocked, "world") is { } blockedWorld && StringProperty(blocked, "save") is { } blockedName)
            blockedSave = new(blockedWorld, blockedName, StringProperty(blocked, "reason") ?? "");

        return new(GameLinkError.None, message, new GameLinkStatus(world, hosting.GetBoolean(), state.Value, mod, player)
        {
            Save = StringProperty(root, "save") ?? "",
            OtherPlayers = root.TryGetProperty("otherPlayers", out var otherPlayers) &&
                otherPlayers.ValueKind == JsonValueKind.True,
            Operation = operation,
            Blocked = blockedSave,
        });
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
        "invalid_token" => GameLinkError.InvalidToken,
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
        "players" => GameLinkError.Players,
        "world_not_found" => GameLinkError.WorldNotFound,
        "save_not_found" => GameLinkError.SaveNotFound,
        "save_unplayable" => GameLinkError.SaveUnplayable,
        "expired" => GameLinkError.Expired,
        _ => GameLinkError.InvalidReply,
    };
}
