using System.Text;
using MapEditor.Core.GameLink;

namespace MapEditor.Core.Tests;

public class GameLinkProtocolTests
{
    [Fact]
    public void PictureFitsTheRequestLimitWithSixMaximumLengthCoordinates()
    {
        const string line = "GNOSTICLINK 2 PICTURE -2147483648 -2147483648 -2147483648 -2147483648 -2147483648 -2147483648\r\n";
        Assert.True(line.Length <= GameLinkProtocol.MaxRequestBytes);
        Assert.Equal(GameLinkParseResult.Ok, Read(line, out _));
    }

    [Fact]
    public void PictureParsesCameraAndLookInXyzOrder()
    {
        Assert.Equal(GameLinkParseResult.Ok, Read("GNOSTICLINK 2 PICTURE -10 +90 -20 30 40 50\n", out var request));
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
        Assert.Equal(GameLinkParseResult.BadRequest, Read($"GNOSTICLINK 2 PICTURE {arguments}\n", out _));

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void StatusAcceptsEitherLineEnding(string ending)
    {
        Assert.Equal(GameLinkParseResult.Ok, Read("GNOSTICLINK 2 STATUS" + ending, out var request));
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
            Read($"GNOSTICLINK 2 TELEPORT -2147483648 +2147483647 {token}\n", out var request));
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
        Assert.Equal(GameLinkParseResult.Ok, Read($"GNOSTICLINK 2 WAYPOINT 70 -300 {name}\n", out var request));
        Assert.Equal(GameLinkRequestKind.Waypoint, request.Kind);
        Assert.Equal(70, request.X);
        Assert.Equal(-300, request.Z);
        Assert.Equal(name, request.PrefabName);
    }

    [Fact]
    public void WaypointNameLimitIs64Characters()
    {
        string name = new('a', 64);
        Assert.Equal(GameLinkParseResult.Ok, Read($"GNOSTICLINK 2 WAYPOINT 0 0 {name}\n", out _));
        Assert.Equal(GameLinkParseResult.BadRequest, Read($"GNOSTICLINK 2 WAYPOINT 0 0 {name}a\n", out _));
    }

    [Theory]
    [InlineData("GNOSTICLINK 1 STATUS\n")]
    [InlineData("GNOSTICLINK 200 TELEPORT 0 0 -\n")]
    public void OtherVersionsAreUnsupported(string line)
    {
        Assert.Equal(GameLinkParseResult.UnsupportedProtocol, Read(line, out _));
    }

    [Theory]
    [InlineData("GNOSTICLINK 2 STATUS")]
    [InlineData("GNOSTICLINK 2 STATUS\r")]
    [InlineData("GNOSTICLINK 2 STATUS \n")]
    [InlineData("GNOSTICLINK  2 STATUS\n")]
    [InlineData("GNOSTICLINK\t2 STATUS\n")]
    [InlineData("gnosticlink 2 STATUS\n")]
    [InlineData("GNOSTICLINK x STATUS\n")]
    [InlineData("GNOSTICLINK 2 EXEC teleport 1 2 n\n")]
    [InlineData("GNOSTICLINK 2 TELEPORT 1 2\n")]
    [InlineData("GNOSTICLINK 2 TELEPORT 1 2 n admin\n")]
    [InlineData("GNOSTICLINK 2 TELEPORT 1 2 n;admin\n")]
    [InlineData("GNOSTICLINK 2 TELEPORT 1 2 north\n")]
    [InlineData("GNOSTICLINK 2 TELEPORT 1 2 N\n")]
    [InlineData("GNOSTICLINK 2 TELEPORT 1.5 2 n\n")]
    [InlineData("GNOSTICLINK 2 TELEPORT 1\0 2 n\n")]
    [InlineData("GNOSTICLINK 2 TELEPORT 2147483648 2 n\n")]
    [InlineData("GNOSTICLINK 2 TELEPORT 1 -2147483649 n\n")]
    [InlineData("GNOSTICLINK 2 WAYPOINT 1 2 \n")]
    [InlineData("GNOSTICLINK 2 WAYPOINT 1 2 farm/16\n")]
    [InlineData("GNOSTICLINK 2 WAYPOINT 1 2 farm.16\n")]
    [InlineData("GNOSTICLINK 2 WAYPOINT 1 2 café\n")]
    [InlineData("\uFEFFGNOSTICLINK 2 STATUS\n")]
    public void RejectsNonProtocolRequests(string line)
    {
        Assert.Equal(GameLinkParseResult.BadRequest, Read(line, out _));
    }

    [Theory]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public void ReadsAtMost128BytesIncludingTerminator(int length, bool accepted)
    {
        string line = "GNOSTICLINK 2 TELEPORT " + new string('0', length - 28) + " 0 -\n";
        Assert.Equal(length, line.Length);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(line));
        Assert.Equal(accepted ? GameLinkParseResult.Ok : GameLinkParseResult.BadRequest,
            GameLinkProtocol.ReadRequest(stream, out _));
        Assert.Equal(128, stream.Position);
    }

    [Fact]
    public void ReadsOneRequestEvenIfMoreLinesArriveTogether()
    {
        Assert.Equal(GameLinkParseResult.Ok,
            Read("GNOSTICLINK 2 STATUS\nGNOSTICLINK 2 TELEPORT 1 2 n\n", out var request));
        Assert.Equal(GameLinkRequestKind.Status, request.Kind);
    }

    private static GameLinkParseResult Read(string line, out GameLinkRequest request)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(line));
        return GameLinkProtocol.ReadRequest(stream, out request);
    }
}
