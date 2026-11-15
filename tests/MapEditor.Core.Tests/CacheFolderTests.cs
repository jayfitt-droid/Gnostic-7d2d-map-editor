using MapEditor.Core.IO;

namespace MapEditor.Core.Tests;

/// <summary>The cache stays out of the install folder, so Setup doesn't think a portable user installed the editor (#11).</summary>
public sealed class CacheFolderTests : IDisposable
{
    private readonly string _local = Directory.CreateTempSubdirectory("mapeditor-cache-").FullName;

    public void Dispose() => Directory.Delete(_local, recursive: true);

    private string Install => Path.Combine(_local, CacheFolders.InstallFolder);

    private void WriteOldCache()
    {
        string old = Path.Combine(Install, "PrefabDetail");
        Directory.CreateDirectory(old);
        File.WriteAllBytes(Path.Combine(old, "barn_01-8ddf2c64463e27a.png"), [1, 2, 3]);
    }

    [Fact]
    public void The_cache_is_not_in_the_install_folder()
    {
        string cache = CacheFolders.PrefabDetail(_local);
        Assert.False(cache.StartsWith(Install + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        Assert.StartsWith(_local, cache);
    }

    [Fact]
    public void A_portable_copy_leaves_no_install_folder_behind()
    {
        WriteOldCache();
        CacheFolders.RemoveOldCache(_local);
        Assert.False(Directory.Exists(Install));
    }

    [Fact]
    public void An_installed_editor_keeps_its_files()
    {
        WriteOldCache();
        File.WriteAllBytes(Path.Combine(Install, "Update.exe"), [1]);
        Directory.CreateDirectory(Path.Combine(Install, "current"));

        CacheFolders.RemoveOldCache(_local);

        Assert.False(Directory.Exists(Path.Combine(Install, "PrefabDetail")));
        Assert.True(File.Exists(Path.Combine(Install, "Update.exe")));
        Assert.True(Directory.Exists(Path.Combine(Install, "current")));
    }

    [Fact]
    public void Nothing_to_remove_is_fine()
    {
        CacheFolders.RemoveOldCache(_local);
        Assert.False(Directory.Exists(Install));
    }
}
