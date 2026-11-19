using System.Net;
using System.Net.Sockets;
using System.Text;
using MapEditor.Core.GameLink;
using MapEditor.Core.Model;

namespace MapEditor.Core.Tests;

public class GameLinkClientTests
{
    [Fact]
    public async Task StatusReadsAReadyPlayerAndUnicodeWorld()
    {
        var reply = await ExchangeAsync((client, cancel) => client.GetStatusAsync(cancel), "GNOSTICLINK 1 STATUS",
            """{"ok":true,"protocol":1,"mod":"1.0.0","state":"ready","world":"Île \u0022Test\u0022","hosting":true,"player":""",
            """{"x":-412.25,"z":1880,"yaw":135}}""" + "\r\n");

        Assert.True(reply.Ok);
        Assert.Equal(new GameLinkStatus("Île \"Test\"", true, GameLinkState.Ready, "1.0.0", new(-412.25f, 1880, 135)), reply.Status);
    }

    [Theory]
    [InlineData("menu", GameLinkState.Menu)]
    [InlineData("loading", GameLinkState.Loading)]
    [InlineData("dead", GameLinkState.Dead)]
    [InlineData("remote", GameLinkState.Remote)]
    public async Task StatusReadsOtherStatesWithoutAPlayer(string state, GameLinkState expected)
    {
        var reply = await ExchangeAsync((client, cancel) => client.GetStatusAsync(cancel), "GNOSTICLINK 1 STATUS",
            $$"""{"ok":true,"protocol":1,"mod":"1.0.0","state":"{{state}}","world":"","hosting":false}""" + "\n");

        Assert.True(reply.Ok);
        Assert.NotNull(reply.Status);
        Assert.Equal(expected, reply.Status.State);
        Assert.False(reply.Status.Hosting);
        Assert.Null(reply.Status.Player);
    }

    [Theory]
    [InlineData(null, "-")]
    [InlineData(Facing.SE, "se")]
    public async Task TeleportSendsCoordinatesAndFacing(Facing? facing, string token)
    {
        var reply = await ExchangeAsync((client, cancel) => client.TeleportAsync(-412, 1880, facing, cancel),
            "GNOSTICLINK 1 TELEPORT -412 1880 " + token, """{"ok":true}""" + "\n");
        Assert.True(reply.Ok);
    }

    [Fact]
    public async Task WaypointPreservesNameAndReadsTheTemporaryMessage()
    {
        var reply = await ExchangeAsync((client, cancel) => client.AddWaypointAsync(-412, 1880, "Farm_16-west", cancel),
            "GNOSTICLINK 1 WAYPOINT -412 1880 Farm_16-west",
            """{"ok":true,"message":"Temporary waypoint added. The game does not save it."}""" + "\n");
        Assert.True(reply.Ok);
        Assert.Equal("Temporary waypoint added. The game does not save it.", reply.Message);
    }

    [Theory]
    [InlineData("bad_request", GameLinkError.BadRequest)]
    [InlineData("unsupported_protocol", GameLinkError.UnsupportedProtocol)]
    [InlineData("not_ready", GameLinkError.NotReady)]
    [InlineData("dead", GameLinkError.Dead)]
    [InlineData("remote", GameLinkError.Remote)]
    [InlineData("off_map", GameLinkError.OffMap)]
    [InlineData("busy", GameLinkError.Busy)]
    [InlineData("failed", GameLinkError.Failed)]
    [InlineData("poi_not_found", GameLinkError.PoiNotFound)]
    [InlineData("already_set", GameLinkError.AlreadySet)]
    public async Task ReadsTheModsErrors(string code, GameLinkError expected)
    {
        var reply = await ExchangeAsync((client, cancel) => client.AddWaypointAsync(0, 0, "farm_16", cancel),
            "GNOSTICLINK 1 WAYPOINT 0 0 farm_16",
            $$"""{"ok":false,"error":"{{code}}","message":"Test error."}""" + "\n");
        Assert.Equal(expected, reply.Error);
        Assert.Equal("Test error.", reply.Message);
    }

    [Theory]
    [InlineData("bad json\n", GameLinkError.InvalidReply)]
    [InlineData("[]\n", GameLinkError.InvalidReply)]
    [InlineData("{\"ok\":true}", GameLinkError.InvalidReply)]
    [InlineData("{\"ok\":true}\n", GameLinkError.InvalidReply)]
    [InlineData("{\"ok\":true,\"protocol\":2}\n", GameLinkError.UnsupportedProtocol)]
    [InlineData("{\"ok\":true,\"protocol\":1,\"mod\":\"1.0.0\",\"state\":\"ready\",\"world\":\"Test\",\"hosting\":true}\n",
        GameLinkError.InvalidReply)]
    public async Task RejectsInvalidStatusReplies(string line, GameLinkError error)
    {
        var reply = await ExchangeAsync((client, cancel) => client.GetStatusAsync(cancel), "GNOSTICLINK 1 STATUS", line);
        Assert.Equal(error, reply.Error);
    }

    [Fact]
    public async Task RejectsAReplyAboveTheByteLimit()
    {
        var reply = await ExchangeAsync((client, cancel) => client.GetStatusAsync(cancel), "GNOSTICLINK 1 STATUS",
            new string(' ', 4096) + "{}\n");
        Assert.Equal(GameLinkError.InvalidReply, reply.Error);
    }

    [Fact]
    public async Task InvalidPrefabNamesAreRejectedBeforeConnecting()
    {
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var client = ClientFor(server);
        var reply = await client.AddWaypointAsync(0, 0, "farm_16\nGNOSTICLINK 1 STATUS", TestContext.Current.CancellationToken);
        Assert.Equal(GameLinkError.BadRequest, reply.Error);
        Assert.False(server.Pending());
    }

    [Fact]
    public async Task ReportsWhenTheModIsNotListening()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var client = new GameLinkClient(((IPEndPoint)socket.LocalEndPoint!).Port, TimeSpan.FromSeconds(3));
        Assert.Equal(GameLinkError.Unavailable, (await client.GetStatusAsync(TestContext.Current.CancellationToken)).Error);
    }

    [Fact]
    public async Task TimesOutWhenTheModAcceptsButDoesNotReply()
    {
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var cancel = TestContext.Current.CancellationToken;
        var client = new GameLinkClient(((IPEndPoint)server.LocalEndpoint).Port, TimeSpan.FromMilliseconds(500));
        Task<GameLinkReply> response = client.GetStatusAsync(cancel);
        using var accepted = await server.AcceptTcpClientAsync(cancel);
        Assert.Equal(GameLinkError.NoReply, (await response).Error);
    }

    private static async Task<GameLinkReply> ExchangeAsync(
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

    private static GameLinkClient ClientFor(TcpListener server) =>
        new(((IPEndPoint)server.LocalEndpoint).Port, TimeSpan.FromSeconds(3));
}
