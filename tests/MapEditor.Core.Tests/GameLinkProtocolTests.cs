using System.Text;
using MapEditor.Core.GameLink;

namespace MapEditor.Core.Tests;

public class GameLinkProtocolTests
{
    private const string SessionToken = "0123456789abcdef0123456789abcdef";
    private const string Prefix = "GNOSTICLINK 2 " + SessionToken + " ";

    [Theory]
    [InlineData(Prefix + "STATUS\n", "Ok")]
    [InlineData("GNOSTICLINK 2 STATUS\n", "InvalidToken")]
    [InlineData("GNOSTICLINK 2 1123456789abcdef0123456789abcdef STATUS\n", "InvalidToken")]
    [InlineData("GNOSTICLINK 3 STATUS\n", "InvalidToken")]
    [InlineData("WRONG 2 missing \tINVALID\n", "InvalidToken")]
    [InlineData("GNOSTICLINK 2  " + SessionToken + " STATUS\n", "InvalidToken")]
    [InlineData("GNOSTICLINK 2 STATUS", "InvalidToken")]
    [InlineData("GNOSTICLINK 3 " + SessionToken + " STATUS\n", "UnsupportedProtocol")]
    public void ChecksTheSessionTokenBeforeTheRestOfTheRequest(string line, string expected) =>
        Assert.Equal(expected, Read(line, out _).ToString());

    [Theory]
    [InlineData(Prefix + "LEAVE\n", "Ok")]
    [InlineData(Prefix + "OPEN 0123456789abcdef\n", "Ok")]
    [InlineData(Prefix + "OPEN ../0123456789abc\n", "BadRequest")]
    [InlineData(Prefix + "OPEN 0123456789abcdeg\n", "BadRequest")]
    [InlineData("GNOSTICLINK 2 1123456789abcdef0123456789abcdef OPEN invalid\n", "InvalidToken")]
    public void ParsesMenuRequests(string line, string expected) =>
        Assert.Equal(expected, Read(line, out _).ToString());

    [Fact]
    public void OpenRejectsAnOverlongRequest()
    {
        string line = Prefix + "OPEN " + new string('0', GameLinkProtocol.MaxRequestBytes) + "\n";
        Assert.Equal(GameLinkParseResult.BadRequest, Read(line, out _));
    }

    [Fact]
    public void ANewSessionRejectsTheLastSessionsToken()
    {
        string previous = GameLinkProtocol.CreateSessionToken();
        string current = GameLinkProtocol.CreateSessionToken();
        Assert.True(GameLinkProtocol.IsSessionToken(previous));
        Assert.True(GameLinkProtocol.IsSessionToken(current));
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes($"GNOSTICLINK 2 {previous} STATUS\n"));
        Assert.Equal(GameLinkParseResult.InvalidToken, GameLinkProtocol.ReadRequest(stream, current, out _));
    }

    [Fact]
    public void PictureFitsTheRequestLimitWithSixMaximumLengthCoordinates()
    {
        const string line = Prefix + "PICTURE -2147483648 -2147483648 -2147483648 -2147483648 -2147483648 -2147483648\r\n";
        Assert.True(line.Length <= GameLinkProtocol.MaxRequestBytes);
        Assert.Equal(GameLinkParseResult.Ok, Read(line, out _));
    }

    [Fact]
    public void PictureParsesCameraAndLookInXyzOrder()
    {
        Assert.Equal(GameLinkParseResult.Ok, Read(Prefix + "PICTURE -10 +90 -20 30 40 50\n", out var request));
        Assert.Equal(GameLinkRequestKind.Picture, request.Kind);
        Assert.Equal((-10, 90, -20, 30, 40, 50), (request.X, request.Y, request.Z, request.LookX, request.LookY, request.LookZ));
    }

    [Theory]
    [InlineData("1 2 3 4 5")]
    [InlineData("1 2 3 4 5 6 ")]
    [InlineData("1 2 3 4 5 6 extra")]
    [InlineData("1 2 3 4  5 6")]
    [InlineData("1 2 3 4 5.1 6")]
    [InlineData("1 2 3 4 5 2147483648")]
    [InlineData("1 2 3 4 5 picture.png")]
    public void PictureRejectsAnythingExceptSixIntegers(string arguments) =>
        Assert.Equal(GameLinkParseResult.BadRequest, Read(Prefix + $"PICTURE {arguments}\n", out _));

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void StatusAcceptsEitherLineEnding(string ending)
    {
        Assert.Equal(GameLinkParseResult.Ok, Read(Prefix + "STATUS" + ending, out var request));
        Assert.Equal(GameLinkRequestKind.Status, request.Kind);
    }

    [Theory]
    [InlineData("n", "n")]
    [InlineData("ne", "ne")]
    [InlineData("e", "e")]
    [InlineData("se", "se")]
    [InlineData("s", "s")]
    [InlineData("sw", "sw")]
    [InlineData("w", "w")]
    [InlineData("nw", "nw")]
    [InlineData("-", null)]
    public void TeleportParsesIntegerCoordinatesAndAllowlistedFacing(string token, string? facing)
    {
        Assert.Equal(GameLinkParseResult.Ok,
            Read(Prefix + $"TELEPORT -2147483648 +2147483647 {token}\n", out var request));
        Assert.Equal(GameLinkRequestKind.Teleport, request.Kind);
        Assert.Equal(int.MinValue, request.X);
        Assert.Equal(int.MaxValue, request.Z);
        Assert.Equal(facing, request.Facing);
        Assert.Null(request.PrefabName);
    }

    [Theory]
    [InlineData("Farm_16-west")]
    [InlineData("a")]
    public void WaypointKeepsTheExactPrefabName(string name)
    {
        Assert.Equal(GameLinkParseResult.Ok, Read(Prefix + $"WAYPOINT 70 -300 {name}\n", out var request));
        Assert.Equal(GameLinkRequestKind.Waypoint, request.Kind);
        Assert.Equal(70, request.X);
        Assert.Equal(-300, request.Z);
        Assert.Equal(name, request.PrefabName);
    }

    [Fact]
    public void WaypointNameLimitIs64Characters()
    {
        string name = new('a', 64);
        Assert.Equal(GameLinkParseResult.Ok, Read(Prefix + $"WAYPOINT 0 0 {name}\n", out _));
        Assert.Equal(GameLinkParseResult.BadRequest, Read(Prefix + $"WAYPOINT 0 0 {name}a\n", out _));
    }

    [Theory]
    [InlineData("GNOSTICLINK 1 " + SessionToken + " STATUS\n")]
    [InlineData("GNOSTICLINK 200 " + SessionToken + " TELEPORT 0 0 -\n")]
    public void OtherVersionsAreUnsupported(string line)
    {
        Assert.Equal(GameLinkParseResult.UnsupportedProtocol, Read(line, out _));
    }

    [Theory]
    [InlineData(Prefix + "STATUS")]
    [InlineData(Prefix + "STATUS\r")]
    [InlineData(Prefix + "STATUS \n")]
    [InlineData("GNOSTICLINK  " + SessionToken + " STATUS\n")]
    [InlineData("GNOSTICLINK\t2  " + SessionToken + " STATUS\n")]
    [InlineData("gnosticlink 2 " + SessionToken + " STATUS\n")]
    [InlineData("GNOSTICLINK x " + SessionToken + " STATUS\n")]
    [InlineData(Prefix + "EXEC teleport 1 2 n\n")]
    [InlineData(Prefix + "TELEPORT 1 2\n")]
    [InlineData(Prefix + "TELEPORT 1 2 n admin\n")]
    [InlineData(Prefix + "TELEPORT 1 2 n;admin\n")]
    [InlineData(Prefix + "TELEPORT 1 2 north\n")]
    [InlineData(Prefix + "TELEPORT 1 2 N\n")]
    [InlineData(Prefix + "TELEPORT 1.5 2 n\n")]
    [InlineData(Prefix + "TELEPORT 1\0 2 n\n")]
    [InlineData(Prefix + "TELEPORT 2147483648 2 n\n")]
    [InlineData(Prefix + "TELEPORT 1 -2147483649 n\n")]
    [InlineData(Prefix + "WAYPOINT 1 2 \n")]
    [InlineData(Prefix + "WAYPOINT 1 2 farm/16\n")]
    [InlineData(Prefix + "WAYPOINT 1 2 farm.16\n")]
    [InlineData(Prefix + "WAYPOINT 1 2 café\n")]
    [InlineData("\uFEFF" + Prefix + "STATUS\n")]
    public void RejectsNonProtocolRequests(string line)
    {
        Assert.Equal(GameLinkParseResult.BadRequest, Read(line, out _));
    }

    [Theory]
    [InlineData(160, true)]
    [InlineData(161, false)]
    public void ReadsAtMost160BytesIncludingTerminator(int length, bool accepted)
    {
        const string start = Prefix + "TELEPORT ";
        const string end = " 0 -\n";
        string line = start + new string('0', length - start.Length - end.Length) + end;
        Assert.Equal(length, line.Length);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(line));
        Assert.Equal(accepted ? GameLinkParseResult.Ok : GameLinkParseResult.BadRequest,
            GameLinkProtocol.ReadRequest(stream, SessionToken, out _));
        Assert.Equal(160, stream.Position);
    }

    [Fact]
    public void ReadsOneRequestEvenIfMoreLinesArriveTogether()
    {
        Assert.Equal(GameLinkParseResult.Ok,
            Read(Prefix + "STATUS\n" + Prefix + "TELEPORT 1 2 n\n", out var request));
        Assert.Equal(GameLinkRequestKind.Status, request.Kind);
    }

    private static GameLinkParseResult Read(string line, out GameLinkRequest request)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(line));
        return GameLinkProtocol.ReadRequest(stream, SessionToken, out request);
    }
}
