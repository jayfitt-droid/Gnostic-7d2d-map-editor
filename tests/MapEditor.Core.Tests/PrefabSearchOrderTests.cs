using MapEditor.Core.GameData;

namespace MapEditor.Core.Tests;

/// <summary>
/// Where prefabs load from, and which copy wins when two folders hold the same name. The game searches LocalPrefabs
/// first, then mods, then Data/Prefabs, so the library loads them the other way round and the last copy wins.
/// </summary>
public sealed class PrefabSearchOrderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mapeditor-prefab-roots-").FullName;
    private readonly GameInstall _install;
    private readonly string _userMods, _localPrefabs;

    public PrefabSearchOrderTests()
    {
        string game = Path.Combine(_dir, "game");
        Directory.CreateDirectory(Path.Combine(game, "Data", "Config"));
        File.WriteAllText(Path.Combine(game, "Data", "Config", "biomes.xml"), "<biomes />");
        File.WriteAllText(Path.Combine(game, "Data", "Config", "rwgmixer.xml"), "<rwgmixer />");
        _install = GameInstall.Open(game);
        _userMods = Path.Combine(_dir, "appdata", "Mods");
        _localPrefabs = Path.Combine(_dir, "appdata", "LocalPrefabs");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>Writes a prefab whose X size tells which copy it is.</summary>
    private static void Prefab(string folder, string name, int marker)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name + ".xml"),
            $"""<prefab><property name="PrefabSize" value="{marker}, 10, 10" /></prefab>""");
        File.WriteAllBytes(Path.Combine(folder, name + ".tts"), []);
    }

    private PrefabLibrary Load() => PrefabLibrary.Load(PrefabLibrary.SearchRoots(_install, _userMods, _localPrefabs));

    [Fact]
    public void Folders_load_vanilla_then_game_mods_then_user_mods_then_local_prefabs()
    {
        string vanilla = _install.PrefabsDir;
        string gameModA = Path.Combine(_install.ModsDir, "A_mod", "Prefabs");
        string gameModB = Path.Combine(_install.ModsDir, "b_mod", "Prefabs");
        string userMod = Path.Combine(_userMods, "my_mod", "Prefabs");
        foreach (string folder in new[] { vanilla, gameModB, gameModA, userMod, _localPrefabs }) Directory.CreateDirectory(folder);
        // A mod with no Prefabs folder adds nothing.
        Directory.CreateDirectory(Path.Combine(_install.ModsDir, "c_mod_without_prefabs"));

        Assert.Equal([vanilla, gameModA, gameModB, userMod, _localPrefabs], PrefabLibrary.SearchRoots(_install, _userMods, _localPrefabs));
    }

    [Fact]
    public void Missing_mod_and_local_folders_leave_only_vanilla()
    {
        Assert.Equal([_install.PrefabsDir], PrefabLibrary.SearchRoots(_install, _userMods, _localPrefabs));
    }

    [Fact]
    public void Local_prefab_wins_over_mods_and_vanilla()
    {
        Prefab(_install.PrefabsDir, "house_01", 10);
        Prefab(Path.Combine(_install.ModsDir, "a_mod", "Prefabs"), "house_01", 20);
        Prefab(Path.Combine(_userMods, "my_mod", "Prefabs"), "house_01", 30);
        Prefab(_localPrefabs, "House_01", 40); // names match whatever their case, as in the game

        var library = Load();
        Assert.Equal(1, library.Count);
        Assert.Equal(40, library.Find("house_01")!.Size.X);
        Assert.Equal(_localPrefabs, library.Find("house_01")!.Folder);
    }

    [Fact]
    public void Later_mod_wins_and_unshared_prefabs_all_load()
    {
        Prefab(_install.PrefabsDir, "house_01", 10);
        Prefab(_install.PrefabsDir, "store_01", 11);
        Prefab(Path.Combine(_install.ModsDir, "A_mod", "Prefabs"), "house_01", 20);
        Prefab(Path.Combine(_install.ModsDir, "b_mod", "Prefabs"), "house_01", 21);
        Prefab(Path.Combine(_userMods, "my_mod", "Prefabs"), "store_01", 30);
        Prefab(_localPrefabs, "my_own_poi", 40);

        var library = Load();
        Assert.Equal(3, library.Count);
        Assert.Equal(21, library.Find("house_01")!.Size.X); // b_mod loads after A_mod
        Assert.Equal(30, library.Find("store_01")!.Size.X); // a user mod beats vanilla
        Assert.Equal(40, library.Find("my_own_poi")!.Size.X);
    }
}
