using MapEditor.Core.GameLink;

namespace MapEditor.Core.Tests;

public sealed class GameLinkPlayerMarkerTests
{
    private static readonly string ExportFolder = Path.Combine("GeneratedWorlds", "My Island");

    [Theory]
    [InlineData(-12.5f, 50.25f, 0f, 51.5, 13.75)]
    [InlineData(12.5f, -50.25f, 90f, 76.5, 114.25)]
    [InlineData(-64f, -64f, 270f, 0.0, 128.0)]
    public void Projects_the_player_to_the_north_up_map_and_preserves_heading(float x, float z, float yaw, double mapX, double mapY)
    {
        var status = new GameLinkStatus("my island", true, GameLinkState.Ready, "1.0.0", new GameLinkPlayer(x, z, yaw));

        Assert.Equal(new GameLinkPlayerMarker(mapX, mapY, yaw), GameLinkWorld.PlayerMarker(ExportFolder, 128, status));
    }

    [Fact]
    public void Hides_the_player_when_the_world_or_player_is_not_available()
    {
        var ready = new GameLinkStatus("My Island", true, GameLinkState.Ready, "1.0.0", new GameLinkPlayer(0, 0, 0));
        Assert.NotNull(GameLinkWorld.PlayerMarker(ExportFolder, 128, ready));

        Assert.Null(GameLinkWorld.PlayerMarker(null, 128, ready));
        Assert.Null(GameLinkWorld.PlayerMarker(ExportFolder, 128, ready with { World = "Another Island" }));
        Assert.Null(GameLinkWorld.PlayerMarker(ExportFolder, 128, ready with { Hosting = false }));
        Assert.Null(GameLinkWorld.PlayerMarker(ExportFolder, 128, ready with { State = GameLinkState.Loading }));
        Assert.Null(GameLinkWorld.PlayerMarker(ExportFolder, 128, ready with { Player = null }));
        Assert.Null(GameLinkWorld.PlayerMarker(ExportFolder, 128, ready with { Player = new GameLinkPlayer(65, 0, 0) }));
        Assert.Null(GameLinkWorld.PlayerMarker(ExportFolder, 128, ready with { Player = new GameLinkPlayer(0, 0, float.NaN) }));
    }
}
