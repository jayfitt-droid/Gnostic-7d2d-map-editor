using MapEditor.Core.Model;

namespace MapEditor.Core.Edit;

/// <summary>
/// The one way edits reach a world. Each edit is a transaction: it commits as one undo step, or, if it throws partway,
/// it is rolled back so the world is exactly as it was before.
/// </summary>
public sealed class EditRunner(UndoStack undo)
{
    /// <summary>Raised after an edit with changes has been committed to the undo history.</summary>
    public event EventHandler<EditSession>? Committed;

    /// <summary>Raised after an edit failed and was rolled back.</summary>
    public event EventHandler<EditFailedEventArgs>? Failed;

    /// <summary>
    /// Opens an edit that spans several calls, such as a brush stroke: make its changes with <see cref="Step"/>,
    /// then <see cref="Commit"/> it.
    /// </summary>
    public EditSession Begin(World world, string name) => new(name, world);

    /// <summary>
    /// Makes part of an open edit. If <paramref name="change"/> throws, the whole edit so far is rolled back,
    /// <see cref="Failed"/> is raised, and false is returned; the session is then finished.
    /// </summary>
    public bool Step(EditSession session, Action<EditSession> change)
    {
        try
        {
            change(session);
            return true;
        }
        catch (Exception ex)
        {
            session.Rollback();
            Failed?.Invoke(this, new EditFailedEventArgs(session, ex));
            return false;
        }
    }

    /// <summary>Pushes the edit onto the undo history if it changed anything. Returns whether it did.</summary>
    public bool Commit(EditSession session)
    {
        if (undo.Push(session) is not { } step) return false;
        session.CommittedPosition = step.Position;
        Committed?.Invoke(this, session);
        return true;
    }

    /// <summary>
    /// Runs a long edit (a generator stage) on a background thread as one transaction. The caller keeps every other
    /// edit and save off the world until it finishes. <see cref="Committed"/> or <see cref="Failed"/> is raised back on
    /// the calling thread; a cancelled edit (OperationCanceledException) is rolled back and reported as failed too.
    /// <paramref name="prepare"/> runs first on the calling thread, for what must be read there, such as
    /// <see cref="EditSession.TouchState"/>.
    /// </summary>
    public async Task<bool> RunInBackgroundAsync(World world, string name, Action<EditSession> change, Action<EditSession>? prepare = null)
    {
        var session = Begin(world, name);
        prepare?.Invoke(session);
        // The edit may add or remove prefabs off this thread; the map keeps drawing the ones from before meanwhile.
        world.HoldShownPrefabs();
        try
        {
            await Task.Run(() => change(session));
        }
        catch (Exception ex)
        {
            session.Rollback();
            world.ShowLivePrefabs();
            Failed?.Invoke(this, new EditFailedEventArgs(session, ex));
            return false;
        }
        world.ShowLivePrefabs();
        Commit(session);
        return true;
    }

    /// <summary>Runs a whole edit as one transaction. Returns false if it failed (and was rolled back).</summary>
    public bool Run(World world, string name, Action<EditSession> change)
    {
        var session = Begin(world, name);
        if (!Step(session, change)) return false;
        Commit(session);
        return true;
    }
}

public sealed class EditFailedEventArgs(EditSession session, Exception error) : EventArgs
{
    public EditSession Session { get; } = session;
    public Exception Error { get; } = error;
}
