using MapEditor.Core.GameData;
using MapEditor.Core.GameLink;
using MapEditor.Core.Model;

namespace MapEditor.Core.Tests;

public sealed class GameLinkWorldTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("gnostic-link-poi-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void MatchesTheExportFolderNameIgnoringCase()
    {
        string folder = Path.Combine("GeneratedWorlds", "My Island") + Path.DirectorySeparatorChar;
        Assert.Equal("My Island", GameLinkWorld.ExportName(folder));
        Assert.True(GameLinkWorld.Matches(folder, "my island"));
        Assert.False(GameLinkWorld.Matches(folder, "Project Name"));
        Assert.False(GameLinkWorld.Matches(null, "My Island"));
        Assert.False(GameLinkWorld.Matches("", ""));
    }

    [Theory]
    [InlineData(0, -14, 14)]
    [InlineData(1, -16, 16)]
    [InlineData(2, -14, 14)]
    [InlineData(3, -16, 16)]
    public void WaypointIsAtTheCentreOfTheClickedPoi(int rotation, int x, int z)
    {
        var world = new World { Name = "P", Size = 128, Height = new Grid<ushort>(128, 128) };
        world.Prefabs.Add(new() { Name = "farm_16", Position = new(-20, 0, 10), Rotation = rotation });
        world.Prefabs.Add(new() { Name = "farm_16", Position = new(20, 0, 10) });
        world.Prefabs.Add(new() { Name = "part_light", Position = new(-20, 0, 10) });
        world.Prefabs.Add(new() { Name = "rwg_tile_test", Position = new(-30, 0, 0) });
        AddPrefab("farm_16", 12, 8, "rural");
        AddPrefab("part_light", 1, 1, "part");
        AddPrefab("rwg_tile_test", 50, 50, "streettile");
        var prefabs = PrefabLibrary.Load([_folder]);

        Assert.Equal(new GameLinkWaypoint("farm_16", x, z), GameLinkWorld.WaypointAt(world, -20, 10, prefabs));
        Assert.Null(GameLinkWorld.WaypointAt(world, -29, 5, prefabs));
        Assert.Null(GameLinkWorld.WaypointAt(world, 60, 60, prefabs));
    }

    [Theory]
    [InlineData("custom farm")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void WaypointIsHiddenForNamesTheProtocolCannotSend(string name)
    {
        var world = new World { Name = "P", Size = 128, Height = new Grid<ushort>(128, 128) };
        var prefab = new PrefabInstance { Name = name, Position = new(-20, 0, 10) };
        world.Prefabs.Add(prefab);
        AddPrefab(name, 12, 8, "rural");
        var prefabs = PrefabLibrary.Load([_folder]);

        Assert.Same(prefab, WorldQuery.PrefabAt(world, -20, 10, prefabs));
        Assert.Null(GameLinkWorld.WaypointAt(world, -20, 10, prefabs));
    }

    private void AddPrefab(string name, int x, int z, string tags)
    {
        File.WriteAllText(Path.Combine(_folder, name + ".xml"), $"""
            <prefab>
              <property name="PrefabSize" value="{x}, 8, {z}" />
              <property name="Tags" value="{tags}" />
            </prefab>
            """);
        File.WriteAllText(Path.Combine(_folder, name + ".tts"), "");
    }
}
