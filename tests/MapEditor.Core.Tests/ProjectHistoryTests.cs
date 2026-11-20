using System.IO.Compression;
using MapEditor.Core.Edit;
using MapEditor.Core.IO;
using MapEditor.Core.Model;

namespace MapEditor.Core.Tests;

public sealed class ProjectHistoryTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mapeditor-history-").FullName;
    private static readonly GridRect Area = new(1, 2, 2, 3);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static World MakeWorld() => new() { Name = "History", Size = 4, Height = new Grid<ushort>(4, 4) };

    private static void Raise(World world, EditSession edit)
    {
        edit.Touch(world.Height, Area);
        world.Height[1, 2]++;
    }

    private static EditRunner Track(UndoStack undo, ProjectHistory history)
    {
        var runner = new EditRunner(undo);
        runner.Committed += (_, edit) => history.RecordEdit(edit);
        return runner;
    }

    [Fact]
    public async Task Records_only_committed_edits_with_their_area_and_detail()
    {
        var world = MakeWorld();
        var undo = new UndoStack();
        var history = new ProjectHistory(undo, []);
        var runner = Track(undo, history);

        Assert.True(runner.Run(world, "No change", s => s.Touch(world.Height, Area)));
        Assert.False(runner.Run(world, "Failed", s =>
        {
            Raise(world, s);
            throw new InvalidOperationException();
        }));
        Assert.False(await runner.RunInBackgroundAsync(world, "Cancelled", s =>
        {
            Raise(world, s);
            throw new OperationCanceledException();
        }));
        Assert.Empty(history.Entries);

        Assert.True(await runner.RunInBackgroundAsync(world, "Generate terrain", s => Raise(world, s),
            s => s.HistoryDetail = "seed 123"));
        var entry = Assert.Single(history.Entries);
        Assert.Equal(("Edit", "Generate terrain (seed 123)", Area, TimeSpan.Zero),
            (entry.Kind, entry.Text, entry.Bounds, entry.Time.Offset));

        Assert.NotNull(history.Undo());
        Assert.NotNull(history.Redo());
        Assert.Equal(new[] { "Edit", "Undo", "Redo" }, history.Entries.Select(e => e.Kind));
        Assert.Equal(new[] { "Generate terrain (seed 123)", "Undo: Generate terrain", "Redo: Generate terrain" },
            history.Entries.Select(e => e.Text));
        Assert.All(history.Entries, e => Assert.Equal(Area, e.Bounds));
        Assert.Equal(HistoryAction.None, history.ActionFor(history.Entries[1]));
    }

    [Fact]
    public void Move_to_keeps_the_target_applied_and_records_each_step()
    {
        var world = MakeWorld();
        var undo = new UndoStack();
        var history = new ProjectHistory(undo, []);
        var runner = Track(undo, history);
        foreach (string name in new[] { "First", "Second", "Third" }) runner.Run(world, name, s => Raise(world, s));
        var (first, second, third) = (history.Entries[0], history.Entries[1], history.Entries[2]);

        Assert.Equal(HistoryAction.Undo, history.ActionFor(first));
        Assert.Null(history.MoveTo(third, HistoryAction.Undo));
        Assert.Equal("Second", history.MoveTo(first, HistoryAction.Undo)?.LastStep.Name);
        Assert.Equal(1, world.Height[1, 2]);
        Assert.Equal(HistoryAction.Undo, history.ActionFor(first));
        Assert.Equal(HistoryAction.Redo, history.ActionFor(second));
        Assert.Equal(HistoryAction.Redo, history.ActionFor(third));
        Assert.Null(history.MoveTo(second, HistoryAction.Undo));

        Assert.Equal("Third", history.MoveTo(third, HistoryAction.Redo)?.LastStep.Name);
        Assert.Equal(3, world.Height[1, 2]);
        Assert.Equal(new[] { "First", "Second", "Third", "Undo: Third", "Undo: Second", "Redo: Second", "Redo: Third" },
            history.Entries.Select(e => e.Text));
    }

    [Fact]
    public void Evicted_and_replaced_steps_remain_as_records_only()
    {
        var world = MakeWorld();
        var undo = new UndoStack(maxBytes: 64); // Two 4 x 4 height tiles.
        var history = new ProjectHistory(undo, []);
        var runner = Track(undo, history);
        foreach (string name in new[] { "First", "Second", "Third" }) runner.Run(world, name, s => Raise(world, s));
        var (first, second, third) = (history.Entries[0], history.Entries[1], history.Entries[2]);
        Assert.Equal(HistoryAction.None, history.ActionFor(first));
        Assert.Null(history.MoveTo(first, HistoryAction.Undo));
        Assert.Equal(HistoryAction.Undo, history.ActionFor(second));
        history.Undo();
        Assert.Equal(HistoryAction.Redo, history.ActionFor(third));
        runner.Run(world, "Fourth", s => Raise(world, s));
        Assert.Equal(HistoryAction.None, history.ActionFor(third));
        Assert.Equal(HistoryAction.Undo, history.ActionFor(second));
        undo.Clear();
        Assert.All(history.Entries, entry => Assert.Equal(HistoryAction.None, history.ActionFor(entry)));
        Assert.Equal(5, history.Entries.Count);
    }

    [Fact]
    public async Task Project_and_autosave_keep_history_but_load_it_as_records_only()
    {
        var world = MakeWorld();
        var settings = new ProjectStore.ProjectSettings();
        var undo = new UndoStack();
        var history = new ProjectHistory(undo, settings.History);
        var runner = Track(undo, history);
        runner.Run(world, "Raise terrain", s => Raise(world, s));
        history.RecordEvent(HistoryEntry.Event("Save As: History.7dmap"));
        var frozen = settings.Clone();
        history.RecordEvent(HistoryEntry.Event("Export Map Image: History.png"));

        string path = Path.Combine(_dir, "History.7dmap");
        ProjectStore.Save(world, path, settings);
        ProjectStore.Load(path, out var loaded);
        AssertSaved(settings.History, loaded.History, undo);

        var autosave = new AutosaveStore(Path.Combine(_dir, "autosave"));
        using var snapshot = WorldSnapshot.Take(world);
        Assert.True(await autosave.SaveAsync(snapshot, frozen));
        ProjectStore.Load(autosave.FindRecovery()!.FullName, out var recovered);
        Assert.Equal(2, recovered.History.Count);
        AssertSaved(frozen.History, recovered.History, undo);
    }

    private static void AssertSaved(List<HistoryEntry> expected, List<HistoryEntry> actual, UndoStack undo)
    {
        static object Saved(HistoryEntry e) => (e.Time, e.Kind, e.Text, e.Bounds);
        Assert.Equal(expected.Select(Saved), actual.Select(Saved));
        var loadedHistory = new ProjectHistory(undo, actual);
        Assert.All(actual, entry => Assert.Equal(HistoryAction.None, loadedHistory.ActionFor(entry)));
    }

    [Fact]
    public void Format_five_without_history_loads_with_an_empty_history()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "SaveSamples", "v2.2.0.7dmap");
        ProjectStore.Load(path, out var settings);
        Assert.Empty(settings.History);
    }

    [Fact]
    public void Malformed_history_rows_leave_the_world_and_valid_rows_readable()
    {
        string path = Path.Combine(_dir, "Broken.7dmap");
        ProjectStore.Save(MakeWorld(), path);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        using (var writer = new StreamWriter(zip.CreateEntry("history.jsonl").Open()))
        {
            writer.WriteLine("{truncated");
            writer.WriteLine("null");
            writer.WriteLine("{\"Kind\":\"Edit\",\"Text\":12}");
            writer.WriteLine("{\"Kind\":\"Edit\",\"Text\":\"No date\"}");
            writer.WriteLine("{\"Time\":\"2026-07-01T10:00:00+02:00\",\"Kind\":\"Event\",\"Text\":\"Open world\"}");
        }

        Assert.Equal("History", ProjectStore.Load(path, out var loaded).Name);
        var entry = Assert.Single(loaded.History);
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero), entry.Time);
        Assert.Equal(TimeSpan.Zero, entry.Time.Offset);
        Assert.Equal("Open world", entry.Text);
    }
}
