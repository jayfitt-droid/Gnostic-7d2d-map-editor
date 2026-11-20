using System.Text.Json.Serialization;

namespace MapEditor.Core.Edit;

/// <summary>One entry in a project's running history. Bounds are in world metres, when known.</summary>
public sealed record HistoryEntry(DateTimeOffset Time, string Kind, string Text, GridRect? Bounds = null)
{
    // The token has no reference to the undo data and only works with the stack that issued it.
    [JsonIgnore] internal object? EditPosition { get; init; }
    [JsonIgnore] public DateTime LocalTime => Time.LocalDateTime;

    public static HistoryEntry Event(string text) => new(DateTimeOffset.UtcNow, "Event", text);
}

/// <summary>The action available for an edit in the current session's undo stack.</summary>
public enum HistoryAction { None, Undo, Redo }

/// <summary>An append-only record, with undo and redo actions for edits still in the current undo stack.</summary>
public sealed class ProjectHistory(UndoStack undo, List<HistoryEntry> entries)
{
    /// <summary>The entries in the order they were recorded, oldest first.</summary>
    public IReadOnlyList<HistoryEntry> Entries => entries;

    /// <summary>Records an edit after <see cref="EditRunner.Committed"/>.</summary>
    public void RecordEdit(EditSession edit)
    {
        if (edit.CommittedPosition is not { } position) return;
        string text = edit.HistoryDetail is { } detail ? $"{edit.Name} ({detail})" : edit.Name;
        entries.Add(new HistoryEntry(DateTimeOffset.UtcNow, "Edit", text, Area(edit.Bounds)) { EditPosition = position });
    }

    /// <summary>Records a project event after it succeeds.</summary>
    public void RecordEvent(HistoryEntry entry) => entries.Add(entry);

    public HistoryAction ActionFor(HistoryEntry entry) =>
        entry.EditPosition is { } position ? undo.ActionFor(position) : HistoryAction.None;

    public UndoStep? Undo() => RecordMove(undo.Undo(), "Undo");
    public UndoStep? Redo() => RecordMove(undo.Redo(), "Redo");

    /// <summary>
    /// Moves to the state just after the target edit, recording each step. Returns the last step and the area
    /// of all steps taken, or null when no move was needed or the requested action is no longer available.
    /// </summary>
    public (UndoStep LastStep, GridRect Bounds)? MoveTo(HistoryEntry target, HistoryAction action)
    {
        if (action == HistoryAction.None || ActionFor(target) != action) return null;

        UndoStep? last = null;
        GridRect bounds = GridRect.Empty;
        while (!ReferenceEquals(undo.Position, target.EditPosition))
        {
            var step = action == HistoryAction.Undo ? Undo() : Redo();
            if (step is null) break;
            last = step;
            bounds = bounds.Union(step.Bounds);
        }
        return last is null ? null : (last, bounds);
    }

    private UndoStep? RecordMove(UndoStep? step, string kind)
    {
        if (step is not null)
            entries.Add(new HistoryEntry(DateTimeOffset.UtcNow, kind, $"{kind}: {step.Name}", Area(step.Bounds)));
        return step;
    }

    private static GridRect? Area(GridRect bounds) => bounds.IsEmpty ? null : bounds;
}
