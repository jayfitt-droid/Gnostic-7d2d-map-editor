using System.Text;
using MapEditor.Core.GameLink;

namespace MapEditor.Core.Tests;

public class GameLinkProtocolTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void StatusAcceptsEitherLineEnding(string ending)
    {
        Assert.Equal(GameLinkParseResult.Ok, Read("GNOSTICLINK 1 STATUS" + ending, out var request));
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
            Read($"GNOSTICLINK 1 TELEPORT -2147483648 +2147483647 {token}\n", out var request));
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
        Assert.Equal(GameLinkParseResult.Ok, Read($"GNOSTICLINK 1 WAYPOINT 70 -300 {name}\n", out var request));
        Assert.Equal(GameLinkRequestKind.Waypoint, request.Kind);
        Assert.Equal(70, request.X);
        Assert.Equal(-300, request.Z);
        Assert.Equal(name, request.PrefabName);
    }

    [Fact]
    public void WaypointNameLimitIs64Characters()
    {
        string name = new('a', 64);
        Assert.Equal(GameLinkParseResult.Ok, Read($"GNOSTICLINK 1 WAYPOINT 0 0 {name}\n", out _));
        Assert.Equal(GameLinkParseResult.BadRequest, Read($"GNOSTICLINK 1 WAYPOINT 0 0 {name}a\n", out _));
    }

    [Theory]
    [InlineData("GNOSTICLINK 2 STATUS\n")]
    [InlineData("GNOSTICLINK 100 TELEPORT 0 0 -\n")]
    public void OtherVersionsAreUnsupported(string line)
    {
        Assert.Equal(GameLinkParseResult.UnsupportedProtocol, Read(line, out _));
    }

    [Theory]
    [InlineData("GNOSTICLINK 1 STATUS")]
    [InlineData("GNOSTICLINK 1 STATUS\r")]
    [InlineData("GNOSTICLINK 1 STATUS \n")]
    [InlineData("GNOSTICLINK  1 STATUS\n")]
    [InlineData("GNOSTICLINK\t1 STATUS\n")]
    [InlineData("gnosticlink 1 STATUS\n")]
    [InlineData("GNOSTICLINK x STATUS\n")]
    [InlineData("GNOSTICLINK 1 EXEC teleport 1 2 n\n")]
    [InlineData("GNOSTICLINK 1 TELEPORT 1 2\n")]
    [InlineData("GNOSTICLINK 1 TELEPORT 1 2 n admin\n")]
    [InlineData("GNOSTICLINK 1 TELEPORT 1 2 n;admin\n")]
    [InlineData("GNOSTICLINK 1 TELEPORT 1 2 north\n")]
    [InlineData("GNOSTICLINK 1 TELEPORT 1 2 N\n")]
    [InlineData("GNOSTICLINK 1 TELEPORT 1.5 2 n\n")]
    [InlineData("GNOSTICLINK 1 TELEPORT 1\0 2 n\n")]
    [InlineData("GNOSTICLINK 1 TELEPORT 2147483648 2 n\n")]
    [InlineData("GNOSTICLINK 1 TELEPORT 1 -2147483649 n\n")]
    [InlineData("GNOSTICLINK 1 WAYPOINT 1 2 \n")]
    [InlineData("GNOSTICLINK 1 WAYPOINT 1 2 farm/16\n")]
    [InlineData("GNOSTICLINK 1 WAYPOINT 1 2 farm.16\n")]
    [InlineData("GNOSTICLINK 1 WAYPOINT 1 2 café\n")]
    [InlineData("\uFEFFGNOSTICLINK 1 STATUS\n")]
    public void RejectsNonProtocolRequests(string line)
    {
        Assert.Equal(GameLinkParseResult.BadRequest, Read(line, out _));
    }

    [Theory]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public void ReadsAtMost128BytesIncludingTerminator(int length, bool accepted)
    {
        string line = "GNOSTICLINK 1 TELEPORT " + new string('0', length - 28) + " 0 -\n";
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
            Read("GNOSTICLINK 1 STATUS\nGNOSTICLINK 1 TELEPORT 1 2 n\n", out var request));
        Assert.Equal(GameLinkRequestKind.Status, request.Kind);
    }

    private static GameLinkParseResult Read(string line, out GameLinkRequest request)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(line));
        return GameLinkProtocol.ReadRequest(stream, out request);
    }
}
