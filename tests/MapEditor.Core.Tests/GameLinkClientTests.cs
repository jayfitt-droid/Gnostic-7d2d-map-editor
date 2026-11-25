using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using MapEditor.Core.GameLink;
using MapEditor.Core.Model;

namespace MapEditor.Core.Tests;

public class GameLinkClientTests : IDisposable
{
    private const string SessionToken = "0123456789abcdef0123456789abcdef";
    private const string Prefix = "GNOSTICLINK 2 " + SessionToken + " ";
    private readonly string _tokenPath = Path.Combine(AppContext.BaseDirectory, $"game-link-{Guid.NewGuid():N}.token");

    public GameLinkClientTests() => File.WriteAllText(_tokenPath, SessionToken);

    public void Dispose() => File.Delete(_tokenPath);

    [Fact]
    public async Task PinsReadsTheBatchAndAcknowledgesById()
    {
        const string id = "ae0123456789abcdef0123456789abcd";
        string key = GameLinkPins.WorldKey("Test World");
        var after = Guid.ParseExact("fe0123456789abcdef0123456789abcd", "N");
        var reply = await ExchangeAsync((client, cancel) => client.GetPinsAsync(key, after, cancel), Prefix + $"PINS {key} {after:N}",
            """
            {"ok":true,"pins":[{"id":"ae0123456789abcdef0123456789abcd","x":-4.5,"y":65,"z":18.25,"yaw":135,"world":"Test World","save":"Test Save","time":"2026-11-23T12:34:56+00:00","note":"Check \"caf\u00e9\""}]}
            """ + "\n");

        Assert.True(reply.Ok);
        var pin = Assert.Single(reply.Pins!);
        Assert.Equal((Guid.ParseExact(id, "N"), -4.5f, 65f, 18.25f, 135f), (pin.Id, pin.X, pin.Y, pin.Z, pin.Yaw));
        Assert.Equal(("Test World", "Test Save", new DateTimeOffset(2026, 11, 23, 12, 34, 56, TimeSpan.Zero), "Check \"caf\u00e9\""),
            (pin.World, pin.Save, pin.Time, pin.Note));

        var ack = await ExchangeAsync((client, cancel) => client.AckPinsAsync(pin.Id, cancel), Prefix + "PINSACK " + id,
            """{"ok":true}""" + "\n");
        Assert.True(ack.Ok);
    }

    [Theory]
    [InlineData("""{"ok":true,"pins":[]}""", GameLinkError.None)]
    [InlineData("""{"ok":false,"error":"bad_request"}""", GameLinkError.BadRequest)]
    [InlineData("""{"ok":true}""", GameLinkError.InvalidReply)]
    [InlineData("""{"ok":true,"pins":[{"id":"invalid"}]}""", GameLinkError.InvalidReply)]
    public async Task PinsDistinguishesAnEmptyBatchFromErrors(string json, GameLinkError error)
    {
        var reply = await ExchangeAsync((client, cancel) => client.GetPinsAsync(cancel: cancel), Prefix + "PINS", json + "\n");
        Assert.Equal(error, reply.Error);
        if (reply.Ok) Assert.Empty(reply.Pins!);
        else Assert.Null(reply.Pins);
    }

    [Fact]
    public async Task StatusReadsAReadyPlayerAndUnicodeWorld()
    {
        var reply = await ExchangeAsync((client, cancel) => client.GetStatusAsync(cancel), Prefix + "STATUS",
            """{"ok":true,"protocol":2,"mod":"1.1.0","state":"ready","world":"Île \u0022Test\u0022","hosting":true,"player":""",
            """{"x":-412.25,"z":1880,"yaw":135}}""" + "\r\n");

        Assert.True(reply.Ok);
        Assert.Equal(new GameLinkStatus("Île \"Test\"", true, GameLinkState.Ready, "1.1.0", new(-412.25f, 1880, 135)), reply.Status);
    }

    [Theory]
    [InlineData("menu", GameLinkState.Menu)]
    [InlineData("loading", GameLinkState.Loading)]
    [InlineData("dead", GameLinkState.Dead)]
    [InlineData("remote", GameLinkState.Remote)]
    public async Task StatusReadsOtherStatesWithoutAPlayer(string state, GameLinkState expected)
    {
        var reply = await ExchangeAsync((client, cancel) => client.GetStatusAsync(cancel), Prefix + "STATUS",
            $$"""{"ok":true,"protocol":2,"mod":"1.1.0","state":"{{state}}","world":"","hosting":false}""" + "\n");

        Assert.True(reply.Ok);
        Assert.NotNull(reply.Status);
        Assert.Equal(expected, reply.Status.State);
        Assert.False(reply.Status.Hosting);
        Assert.Null(reply.Status.Player);
        Assert.Empty(reply.Status.Save);
        Assert.Null(reply.Status.Operation);
        Assert.Null(reply.Status.Blocked);
    }

    [Fact]
    public async Task StatusReadsTheSaveAndMenuProgress()
    {
        var reply = await ExchangeAsync((client, cancel) => client.GetStatusAsync(cancel), Prefix + "STATUS",
            """{"ok":true,"protocol":2,"mod":"1.1.0","state":"menu","world":"Test","hosting":false,"save":"","op":""",
            """{"id":"0123456789abcdef","kind":"open","state":"blocked","reason":"older_version"},"blocked":""",
            """{"world":"Test","save":"Test Playtest","reason":"older_version"}}""" + "\n");

        Assert.True(reply.Ok);
        Assert.NotNull(reply.Status);
        Assert.Empty(reply.Status.Save);
        Assert.Equal(new("0123456789abcdef", "open", "blocked", "older_version"), reply.Status.Operation);
        Assert.Equal(new("Test", "Test Playtest", "older_version"), reply.Status.Blocked);
    }

    [Fact]
    public async Task StatusReadsTheCurrentSave()
    {
        var reply = await ExchangeAsync((client, cancel) => client.GetStatusAsync(cancel), Prefix + "STATUS",
            """{"ok":true,"protocol":2,"mod":"1.1.0","state":"ready","world":"Test","hosting":true,"save":"Test Playtest","player":{"x":0,"z":0,"yaw":0}}""" + "\n");

        Assert.Equal("Test Playtest", reply.Status?.Save);
    }

    [Fact]
    public async Task StatusReadsRebuildProgressAndOtherPlayers()
    {
        var reply = await ExchangeAsync((client, cancel) => client.GetStatusAsync(cancel), Prefix + "STATUS",
            """{"ok":true,"protocol":2,"mod":"1.1.0","state":"ready","world":"Test","hosting":true,"otherPlayers":true,"save":"Test 1","player":{"x":0,"z":0,"yaw":0},"op":""",
            """{"id":"0123456789abcdef","kind":"rebuild","state":"done","chunks":289}}""" + "\n");

        Assert.NotNull(reply.Status);
        Assert.True(reply.Status.OtherPlayers);
        Assert.Equal(new("0123456789abcdef", "rebuild", "done", "") { Chunks = 289 }, reply.Status.Operation);
    }

    [Fact]
    public async Task LeaveReadsTheOperationId()
    {
        var reply = await ExchangeAsync((client, cancel) => client.LeaveAsync(cancel), Prefix + "LEAVE",
            """{"ok":true,"id":"0123456789abcdef"}""" + "\n");

        Assert.Equal("0123456789abcdef", reply.OperationId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveOperationWritesTheTargetBeforeSendingOnlyItsId(bool rebuild)
    {
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        const string world = "\u0141u\u017Cyce World";
        const string save = world + " Playtest";
        var client = ClientFor(server);
        var response = rebuild
            ? client.RebuildAsync(world, save, new(-12.25f, 32.5f, 0), 128, deadline.Token)
            : client.OpenAsync(world, save, create: true, deadline.Token);
        using var accepted = await server.AcceptTcpClientAsync(deadline.Token);
        using var reader = new StreamReader(accepted.GetStream(), leaveOpen: true);
        string? line = await reader.ReadLineAsync(deadline.Token);
        string start = Prefix + (rebuild ? "REBUILD " : "OPEN ");
        Assert.NotNull(line);
        Assert.StartsWith(start, line);
        string id = line[start.Length..];
        Assert.True(GameLinkProtocol.IsOperationId(id));
        string directory = Path.GetDirectoryName(_tokenPath)!;
        if (rebuild)
        {
            Assert.Null(GameLinkRebuildRequest.Take(directory, id, DateTimeOffset.UtcNow, out var request));
            Assert.NotNull(request);
            Assert.Equal((world, save, -12.25f, 32.5f, 128), (request.World, request.Save, request.X, request.Z, request.Radius));
        }
        else
        {
            Assert.Null(GameLinkOpenRequest.Take(directory, id, DateTimeOffset.UtcNow, out var request));
            Assert.NotNull(request);
            Assert.Equal((world, save, true), (request.World, request.Save, request.Create));
        }

        var acknowledgment = JsonSerializer.Serialize(new { ok = true, id }) + "\n";
        await accepted.GetStream().WriteAsync(Encoding.UTF8.GetBytes(acknowledgment), deadline.Token);
        Assert.Equal(id, (await response).OperationId);
    }

    [Theory]
    [InlineData(null, "-")]
    [InlineData(Facing.SE, "se")]
    public async Task TeleportSendsCoordinatesAndFacing(Facing? facing, string token)
    {
        var reply = await ExchangeAsync((client, cancel) => client.TeleportAsync(-412, 1880, facing, cancel),
            Prefix + "TELEPORT -412 1880 " + token, """{"ok":true}""" + "\n");
        Assert.True(reply.Ok);
    }

    [Fact]
    public async Task WaypointPreservesNameAndReadsTheTemporaryMessage()
    {
        var reply = await ExchangeAsync((client, cancel) => client.AddWaypointAsync(-412, 1880, "Farm_16-west", cancel),
            Prefix + "WAYPOINT -412 1880 Farm_16-west",
            """{"ok":true,"message":"Temporary waypoint added. The game does not save it."}""" + "\n");
        Assert.True(reply.Ok);
        Assert.Equal("Temporary waypoint added. The game does not save it.", reply.Message);
    }

    [Theory]
    [InlineData("bad_request", GameLinkError.BadRequest)]
    [InlineData("invalid_token", GameLinkError.InvalidToken)]
    [InlineData("unsupported_protocol", GameLinkError.UnsupportedProtocol)]
    [InlineData("not_ready", GameLinkError.NotReady)]
    [InlineData("dead", GameLinkError.Dead)]
    [InlineData("remote", GameLinkError.Remote)]
    [InlineData("off_map", GameLinkError.OffMap)]
    [InlineData("busy", GameLinkError.Busy)]
    [InlineData("failed", GameLinkError.Failed)]
    [InlineData("poi_not_found", GameLinkError.PoiNotFound)]
    [InlineData("already_set", GameLinkError.AlreadySet)]
    [InlineData("picture_failed", GameLinkError.PictureFailed)]
    [InlineData("picture_render_timeout", GameLinkError.PictureRenderTimeout)]
    [InlineData("players", GameLinkError.Players)]
    [InlineData("world_not_found", GameLinkError.WorldNotFound)]
    [InlineData("save_not_found", GameLinkError.SaveNotFound)]
    [InlineData("save_unplayable", GameLinkError.SaveUnplayable)]
    [InlineData("expired", GameLinkError.Expired)]
    public async Task ReadsTheModsErrors(string code, GameLinkError expected)
    {
        var reply = await ExchangeAsync((client, cancel) => client.AddWaypointAsync(0, 0, "farm_16", cancel),
            Prefix + "WAYPOINT 0 0 farm_16",
            $$"""{"ok":false,"error":"{{code}}","message":"Test error."}""" + "\n");
        Assert.Equal(expected, reply.Error);
        Assert.Equal("Test error.", reply.Message);
    }

    [Theory]
    [InlineData("gnostic-20261123-123456-789.png", true)]
    [InlineData(null, false)]
    [InlineData("..\\gnostic-20261123-123456-789.png", false)]
    [InlineData("C:/gnostic-20261123-123456-789.png", false)]
    [InlineData("gnostic-20261123-123456-789.PNG", false)]
    [InlineData("gnostic-20261123-123456-789.png\n", false)]
    public async Task PictureSendsCoordinatesAndAcceptsOnlyGeneratedNames(string? file, bool valid)
    {
        var reply = await ExchangeAsync((client, cancel) => client.TakePictureAsync(new(-10, 90, -20), new(30, 40, 50), cancel),
            Prefix + "PICTURE -10 90 -20 30 40 50", JsonSerializer.Serialize(new { ok = true, file }) + "\n");
        Assert.Equal(valid ? GameLinkError.None : GameLinkError.InvalidReply, reply.Error);
        Assert.Equal(valid ? file : null, reply.FileName);
    }

    [Fact]
    public async Task PictureCanWaitPastTheNormalReplyTimeout()
    {
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var cancel = TestContext.Current.CancellationToken;
        var client = new GameLinkClient(((IPEndPoint)server.LocalEndpoint).Port, TimeSpan.FromMilliseconds(100), _tokenPath);
        var response = client.TakePictureAsync(new(0, 80, -30), new(0, 50, 0), cancel);
        using var accepted = await server.AcceptTcpClientAsync(cancel);
        await Task.Delay(250, cancel);
        await accepted.GetStream().WriteAsync("""{"ok":true,"file":"gnostic-20261123-123456-789.png"}"""u8.ToArray(), cancel);
        await accepted.GetStream().WriteAsync("\n"u8.ToArray(), cancel);

        Assert.True((await response).Ok);
    }

    [Theory]
    [InlineData("bad json\n", GameLinkError.InvalidReply)]
    [InlineData("[]\n", GameLinkError.InvalidReply)]
    [InlineData("{\"ok\":true}", GameLinkError.InvalidReply)]
    [InlineData("{\"ok\":true}\n", GameLinkError.InvalidReply)]
    [InlineData("{\"ok\":true,\"protocol\":1}\n", GameLinkError.UnsupportedProtocol)]
    [InlineData("{\"ok\":true,\"protocol\":2,\"mod\":\"1.1.0\",\"state\":\"ready\",\"world\":\"Test\",\"hosting\":true}\n",
        GameLinkError.InvalidReply)]
    public async Task RejectsInvalidStatusReplies(string line, GameLinkError error)
    {
        var reply = await ExchangeAsync((client, cancel) => client.GetStatusAsync(cancel), Prefix + "STATUS", line);
        Assert.Equal(error, reply.Error);
    }

    [Fact]
    public async Task RejectsAReplyAboveTheByteLimit()
    {
        var reply = await ExchangeAsync((client, cancel) => client.GetStatusAsync(cancel), Prefix + "STATUS",
            new string(' ', 4096) + "{}\n");
        Assert.Equal(GameLinkError.InvalidReply, reply.Error);
    }

    [Fact]
    public async Task InvalidPrefabNamesAreRejectedBeforeConnecting()
    {
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var client = ClientFor(server);
        var reply = await client.AddWaypointAsync(0, 0, "farm_16\nGNOSTICLINK 2 STATUS", TestContext.Current.CancellationToken);
        Assert.Equal(GameLinkError.BadRequest, reply.Error);
        Assert.False(server.Pending());
    }

    [Fact]
    public async Task ReportsWhenTheModIsNotListening()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var client = new GameLinkClient(((IPEndPoint)socket.LocalEndPoint!).Port, TimeSpan.FromSeconds(3), _tokenPath);
        Assert.Equal(GameLinkError.Unavailable, (await client.GetStatusAsync(TestContext.Current.CancellationToken)).Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(SessionToken + "\n")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF")]
    public async Task SendsNoRequestWhenTheTokenFileIsMissingOrInvalid(string? contents)
    {
        if (contents is null)
            File.Delete(_tokenPath);
        else
            File.WriteAllText(_tokenPath, contents);

        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        var response = ClientFor(server).GetStatusAsync(deadline.Token);
        using var accepted = await server.AcceptTcpClientAsync(deadline.Token);
        Assert.Equal(GameLinkError.InvalidToken, (await response).Error);
        Assert.Equal(0, await accepted.GetStream().ReadAsync(new byte[1], deadline.Token));
    }

    [Fact]
    public async Task ReadsTheNewTokenOnTheNextRequest()
    {
        const string nextToken = "abcdef0123456789abcdef0123456789";
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        var client = ClientFor(server);

        await CheckRequestAsync(Prefix + "STATUS");
        File.WriteAllText(_tokenPath, nextToken);
        await CheckRequestAsync("GNOSTICLINK 2 " + nextToken + " STATUS");

        async Task CheckRequestAsync(string expected)
        {
            var response = client.GetStatusAsync(deadline.Token);
            using var accepted = await server.AcceptTcpClientAsync(deadline.Token);
            using var reader = new StreamReader(accepted.GetStream());
            Assert.Equal(expected, await reader.ReadLineAsync(deadline.Token));
            accepted.Close();
            Assert.Equal(GameLinkError.NoReply, (await response).Error);
        }
    }

    [Fact]
    public async Task TimesOutWhenTheModAcceptsButDoesNotReply()
    {
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var cancel = TestContext.Current.CancellationToken;
        var client = new GameLinkClient(((IPEndPoint)server.LocalEndpoint).Port, TimeSpan.FromMilliseconds(500), _tokenPath);
        Task<GameLinkReply> response = client.GetStatusAsync(cancel);
        using var accepted = await server.AcceptTcpClientAsync(cancel);
        Assert.Equal(GameLinkError.NoReply, (await response).Error);
    }

    private async Task<GameLinkReply> ExchangeAsync(
        Func<GameLinkClient, CancellationToken, Task<GameLinkReply>> request, string expectedRequest, params string[] replyParts)
    {
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        var response = request(ClientFor(server), deadline.Token);
        using (var accepted = await server.AcceptTcpClientAsync(deadline.Token))
        using (NetworkStream stream = accepted.GetStream())
        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false))
        {
            Assert.Equal(expectedRequest, await reader.ReadLineAsync(deadline.Token));
            foreach (string part in replyParts)
                await stream.WriteAsync(Encoding.UTF8.GetBytes(part), deadline.Token);
        }
        return await response;
    }

    private GameLinkClient ClientFor(TcpListener server) =>
        new(((IPEndPoint)server.LocalEndpoint).Port, TimeSpan.FromSeconds(3), _tokenPath);
}
