using MapEditor.Core.IO;

namespace MapEditor.Core.Tests;

public sealed class WorldFolderTests
{
    [Theory]
    [InlineData("generatedworlds/island", true)]
    [InlineData("generatedworlds/./other/../island/", true)]
    [InlineData("GeneratedWorlds/IslandCopy", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void Same_folder_uses_full_paths_and_ignores_case(string? otherFolder, bool expected)
    {
        string folder = Path.GetFullPath("GeneratedWorlds/Island");
        Assert.Equal(expected, WorldFiles.SameFolder(folder, otherFolder));
    }
}
