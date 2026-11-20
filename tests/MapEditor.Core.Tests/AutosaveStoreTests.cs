using MapEditor.Core.Edit;
using MapEditor.Core.IO;
using MapEditor.Core.Model;

namespace MapEditor.Core.Tests;

public sealed class AutosaveStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mapeditor-autosave-").FullName;
    private string RecoveryFolder => Path.Combine(_dir, "autosave");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static World MakeWorld(string name) => new() { Name = name, Size = 512, Height = new Grid<ushort>(512, 512) };

    [Fact]
    public async Task Autosave_finishing_after_a_save_cannot_leave_a_recovery_copy()
    {
        var store = new AutosaveStore(RecoveryFolder);
        var world = MakeWorld("Saved");
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var autosave = store.SaveAsync(async path =>
        {
            await finish.Task;
            ProjectStore.Save(world, path);
        });

        string savedPath = Path.Combine(_dir, "Saved" + ProjectStore.Extension);
        ProjectStore.Save(world, savedPath);
        store.Discard();
        finish.SetResult();

        Assert.False(await autosave);
        Assert.Null(store.FindRecovery());
        Assert.Empty(Directory.GetFiles(RecoveryFolder));
        Assert.Equal("Saved", ProjectStore.Load(savedPath).Name);
    }

    [Fact]
    public async Task Autosave_finishing_after_close_cannot_leave_a_recovery_copy()
    {
        var store = new AutosaveStore(RecoveryFolder);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var autosave = store.SaveAsync(async path =>
        {
            ProjectStore.Save(MakeWorld("Closing"), path);
            await finish.Task;
        });

        Assert.Null(store.FindRecovery());
        store.Discard();
        finish.SetResult();

        Assert.False(await autosave);
        Assert.Null(store.FindRecovery());
        Assert.Empty(Directory.GetFiles(RecoveryFolder));
    }

    [Fact]
    public async Task Replacing_a_world_clears_its_recovery_and_rejects_its_pending_write()
    {
        var store = new AutosaveStore(RecoveryFolder);
        var oldWorld = MakeWorld("Old");
        using var oldSnapshot = WorldSnapshot.Take(oldWorld);
        Assert.True(await store.SaveAsync(oldSnapshot, new()));

        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldWrite = store.SaveAsync(async path =>
        {
            await finish.Task;
            ProjectStore.Save(oldWorld, path);
        });

        store.Discard();
        Assert.Null(store.FindRecovery());

        using var newSnapshot = WorldSnapshot.Take(MakeWorld("New"));
        Assert.True(await store.SaveAsync(newSnapshot, new()));
        finish.SetResult();

        Assert.False(await oldWrite);
        var recovery = Assert.IsType<FileInfo>(store.FindRecovery());
        Assert.Equal(recovery.FullName, Assert.Single(Directory.GetFiles(RecoveryFolder)));
        Assert.Equal("New", ProjectStore.Load(recovery.FullName).Name);
    }

    [Fact]
    public async Task Autosaving_unsaved_work_leaves_one_recoverable_copy()
    {
        var store = new AutosaveStore(RecoveryFolder);
        var world = MakeWorld("Unsaved");
        using (var first = WorldSnapshot.Take(world))
            Assert.True(await store.SaveAsync(first, new()));

        world.Height[0, 0] = 1234;
        using var second = WorldSnapshot.Take(world);
        Assert.True(await store.SaveAsync(second, new()));

        var recovery = Assert.IsType<FileInfo>(new AutosaveStore(RecoveryFolder).FindRecovery());
        Assert.Equal(recovery.FullName, Assert.Single(Directory.GetFiles(RecoveryFolder)));
        Assert.Equal(1234, ProjectStore.Load(recovery.FullName).Height[0, 0]);
    }

    [Fact]
    public void Recovery_finds_the_newest_copy_including_the_legacy_name()
    {
        var store = new AutosaveStore(RecoveryFolder);
        string legacy = Path.Combine(RecoveryFolder, "autosave" + ProjectStore.Extension);
        string other = Path.Combine(RecoveryFolder, "autosave-2" + ProjectStore.Extension);
        ProjectStore.Save(MakeWorld("Legacy"), legacy);
        ProjectStore.Save(MakeWorld("Other"), other);
        var now = DateTime.UtcNow;
        File.SetLastWriteTimeUtc(legacy, now.AddMinutes(-2));
        File.SetLastWriteTimeUtc(other, now.AddMinutes(-1));

        Assert.Equal(other, store.FindRecovery()?.FullName);
        File.SetLastWriteTimeUtc(legacy, now);
        Assert.Equal(legacy, store.FindRecovery()?.FullName);

        store.Discard();

        Assert.Null(new AutosaveStore(RecoveryFolder).FindRecovery());
        Assert.Empty(Directory.GetFiles(RecoveryFolder));
    }

    [Fact]
    public void Discard_keeps_a_saved_project_in_the_recovery_folder()
    {
        var store = new AutosaveStore(RecoveryFolder);
        string saved = Path.Combine(RecoveryFolder, "autosave-notes" + ProjectStore.Extension);
        string recovery = Path.Combine(RecoveryFolder, "autosave" + ProjectStore.Extension);
        ProjectStore.Save(MakeWorld("Saved"), saved);
        ProjectStore.Save(MakeWorld("Recovery"), recovery);

        store.Discard();

        Assert.Equal(saved, Assert.Single(Directory.GetFiles(RecoveryFolder)));
        Assert.Equal("Saved", ProjectStore.Load(saved).Name);
    }

    [Fact]
    public void FindRecovery_ignores_a_newer_saved_project_in_the_recovery_folder()
    {
        var store = new AutosaveStore(RecoveryFolder);
        string saved = Path.Combine(RecoveryFolder, "autosave-notes" + ProjectStore.Extension);
        string recovery = Path.Combine(RecoveryFolder, "autosave-2" + ProjectStore.Extension);
        ProjectStore.Save(MakeWorld("Saved"), saved);
        ProjectStore.Save(MakeWorld("Recovery"), recovery);
        var now = DateTime.UtcNow;
        File.SetLastWriteTimeUtc(saved, now);
        File.SetLastWriteTimeUtc(recovery, now.AddMinutes(-1));

        Assert.Equal(recovery, store.FindRecovery()?.FullName);
        File.Delete(recovery);
        Assert.Null(store.FindRecovery());
    }

    [Fact]
    public void FindRecovery_deletes_only_old_autosave_staging_files()
    {
        var store = new AutosaveStore(RecoveryFolder);
        Directory.CreateDirectory(RecoveryFolder);
        var now = DateTime.UtcNow;
        string prefix = "autosave" + ProjectStore.Extension;
        string oldPending = $"{prefix}.{Guid.NewGuid():N}.pending";
        string newPending = $"{prefix}.{Guid.NewGuid():N}.pending";
        string[] abandoned = [oldPending, oldPending + ".tmp", prefix + ".tmp"];
        string[] unrelated = [prefix + ".notes.pending", $"project.7dmap.{Guid.NewGuid():N}.pending.tmp"];
        string[] recent = [newPending, newPending + ".tmp"];
        foreach (string name in abandoned.Concat(unrelated).Concat(recent))
        {
            string path = Path.Combine(RecoveryFolder, name);
            File.WriteAllText(path, "staged");
            File.SetLastWriteTimeUtc(path, recent.Contains(name) ? now : now.AddHours(-2));
        }

        Assert.Null(store.FindRecovery());

        Assert.Equal(unrelated.Concat(recent).Order(), Directory.GetFiles(RecoveryFolder).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task Opening_a_recovery_copy_keeps_it_until_the_work_is_saved_or_discarded()
    {
        var store = new AutosaveStore(RecoveryFolder);
        string legacy = Path.Combine(RecoveryFolder, "autosave" + ProjectStore.Extension);
        string recovered = Path.Combine(RecoveryFolder, "autosave-2" + ProjectStore.Extension);
        ProjectStore.Save(MakeWorld("Legacy"), legacy);
        ProjectStore.Save(MakeWorld("Recovered"), recovered);

        store.Discard(recovered);
        Assert.Equal(recovered, Assert.Single(Directory.GetFiles(RecoveryFolder)));

        using var snapshot = WorldSnapshot.Take(MakeWorld("Edited"));
        Assert.True(await store.SaveAsync(snapshot, new()));
        Assert.Equal(recovered, Assert.Single(Directory.GetFiles(RecoveryFolder)));
        Assert.Equal("Edited", ProjectStore.Load(recovered).Name);

        store.Discard();
        Assert.Null(new AutosaveStore(RecoveryFolder).FindRecovery());
    }
}
