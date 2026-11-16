using MapEditor.Core.Model;

namespace MapEditor.Core.Tests;

public sealed class TeleportCommandTests
{
    [Theory]
    [InlineData(-136, -1110, Facing.N, "teleport -136 -1110 n")]
    [InlineData(387, -1357, Facing.W, "teleport 387 -1357 w")]
    [InlineData(0, 0, Facing.E, "teleport 0 0 e")]
    [InlineData(1593, 1305, Facing.S, "teleport 1593 1305 s")]
    [InlineData(10, -20, Facing.NE, "teleport 10 -20 ne")]
    [InlineData(10, -20, Facing.SW, "teleport 10 -20 sw")]
    public void A_command_has_the_game_coordinates_and_the_facing(int x, int z, Facing facing, string expected) =>
        Assert.Equal(expected, TeleportCommand.Format(x, z, facing));

    [Fact]
    public void Without_a_facing_the_command_has_only_the_coordinates() =>
        Assert.Equal("teleport -5 7", TeleportCommand.Format(-5, 7, null));

    [Fact]
    public void A_grid_cell_is_given_in_the_same_game_coordinates_as_the_status_bar()
    {
        var world = new World { Name = "T", Size = 1024, Height = new Grid<ushort>(1024, 1024) };
        // Grid cell (0, 0) is the south-west corner; the game puts 0, 0 at the centre.
        Assert.Equal("teleport -512 -512 n", TeleportCommand.ForCell(world, 0, 0, Facing.N));
        Assert.Equal("teleport 0 0", TeleportCommand.ForCell(world, 512, 512, null));
        Assert.Equal("teleport 511 511 s", TeleportCommand.ForCell(world, 1023, 1023, Facing.S));
    }

    [Fact]
    public void A_cell_off_the_map_has_no_command()
    {
        var world = new World { Name = "T", Size = 1024, Height = new Grid<ushort>(1024, 1024) };
        Assert.Null(TeleportCommand.ForCell(world, -1, 5, Facing.N));
        Assert.Null(TeleportCommand.ForCell(world, 5, 1024, Facing.N));
    }
}
