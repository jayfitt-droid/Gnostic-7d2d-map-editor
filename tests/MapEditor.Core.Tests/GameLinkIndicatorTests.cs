using MapEditor.Core.GameLink;

namespace MapEditor.Core.Tests;

public sealed class GameLinkIndicatorTests
{
    private static readonly string ExportFolder = Path.Combine("GeneratedWorlds", "My Island");
    private static readonly GameLinkStatus Ready = new("My Island", true, GameLinkState.Ready, "1.1.0", null);

    [Fact]
    public void Shows_disconnected_when_the_game_does_not_reply()
    {
        var indicator = GameLinkWorld.Indicator(ExportFolder, null);

        Assert.Equal(GameLinkIndicatorState.Disconnected, indicator.State);
        Assert.Equal("Game Link disconnected", indicator.Text);
    }

    [Theory]
    [InlineData(GameLinkError.Unavailable)]
    [InlineData(GameLinkError.NoReply)]
    [InlineData(GameLinkError.InvalidReply)]
    public void Stays_disconnected_for_errors_that_are_not_a_mod_mismatch(GameLinkError error)
    {
        var indicator = GameLinkWorld.Indicator(ExportFolder, null, error);

        Assert.Equal(GameLinkIndicatorState.Disconnected, indicator.State);
        Assert.Equal("Game Link disconnected", indicator.Text);
    }

    [Theory]
    [InlineData(GameLinkError.UnsupportedProtocol,
        "Game Link connected, but the mod is older than this editor needs: install Gnostic Game Link 1.1.0.")]
    [InlineData(GameLinkError.InvalidToken,
        "Game Link could not verify the session: restart the game or install the mod that matches this editor.")]
    public void Shows_incompatible_when_the_game_has_an_old_or_unverified_mod(GameLinkError error, string text)
    {
        var indicator = GameLinkWorld.Indicator(ExportFolder, null, error);

        Assert.Equal(GameLinkIndicatorState.Incompatible, indicator.State);
        Assert.Equal(text, indicator.Text);
        Assert.True(GameLinkWorld.IsModMismatch(error));
    }

    [Fact]
    public void A_status_wins_over_an_error()
    {
        var indicator = GameLinkWorld.Indicator(ExportFolder, Ready, GameLinkError.UnsupportedProtocol);

        Assert.Equal(GameLinkIndicatorState.ThisWorld, indicator.State);
    }

    [Theory]
    [InlineData(GameLinkState.Menu, false, "", "the game is at the main menu.")]
    [InlineData(GameLinkState.Loading, false, "", "the game is loading.")]
    [InlineData(GameLinkState.Ready, true, "Old Island", "the game has 'Old Island' loaded, not this world.")]
    [InlineData(GameLinkState.Remote, true, "My Island", "you joined another player's game.")]
    [InlineData(GameLinkState.Ready, false, "My Island", "you joined another player's game.")]
    public void Explains_why_the_game_is_not_playing_this_world(GameLinkState state, bool hosting, string world, string reason)
    {
        var indicator = GameLinkWorld.Indicator(ExportFolder, Ready with { State = state, Hosting = hosting, World = world });

        Assert.Equal(GameLinkIndicatorState.OtherWorld, indicator.State);
        Assert.Equal($"Game Link connected: {reason}", indicator.Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Prompts_for_an_export_when_there_is_no_export_folder(string? folder)
    {
        var indicator = GameLinkWorld.Indicator(folder, Ready);

        Assert.Equal(GameLinkIndicatorState.OtherWorld, indicator.State);
        Assert.Equal("Game Link connected: export this world to compare it with the game.", indicator.Text);
    }

    [Theory]
    [InlineData(GameLinkState.Ready, "My Island", "the game has this world loaded.")]
    [InlineData(GameLinkState.Dead, "My Island", "the player is dead in this world.")]
    [InlineData(GameLinkState.Ready, "my island", "the game has this world loaded.")]
    public void Recognizes_this_world_even_when_the_player_is_dead_or_the_name_case_differs(
        GameLinkState state, string world, string reason)
    {
        var indicator = GameLinkWorld.Indicator(ExportFolder, Ready with { State = state, World = world });

        Assert.Equal(GameLinkIndicatorState.ThisWorld, indicator.State);
        Assert.Equal($"Game Link connected: {reason}", indicator.Text);
    }
}
