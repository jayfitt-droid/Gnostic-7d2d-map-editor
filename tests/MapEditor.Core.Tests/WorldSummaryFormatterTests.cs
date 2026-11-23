using MapEditor.Core.IO;
using MapEditor.Core.Model;

namespace MapEditor.Core.Tests;

public sealed class WorldSummaryFormatterTests
{
    [Fact]
    public void Summary_shows_named_land_biomes_and_road_lengths()
    {
        var world = new World
        {
            Name = "Test world", Size = 4, Height = new Grid<ushort>(4, 4),
            Biomes = new IndexedLayer(new Grid<byte>(1, 1), [0x004000]),
            Roads = new Grid<byte>(4, 4),
        };

        string summary = WorldSummaryFormatter.Format(world);
        Assert.Contains("pine_forest", summary);
        Assert.Contains("Biomes (% of land)", summary);
        Assert.Contains("Asphalt  about 0 km", summary);
        Assert.Contains("Spawn points", summary);
    }
}
