using MapEditor.Core.Edit;

namespace MapEditor.Core.IO;

/// <summary>Stores recovery copies for the current document in a folder reserved for autosaves.</summary>
public sealed class AutosaveStore(string folder)
{
    private const string AutosaveName = "autosave";
    private const string AutosaveFileName = AutosaveName + ProjectStore.Extension;
    private readonly Lock _gate = new();
    private readonly string _autosavePath = Path.Combine(folder, AutosaveFileName);
    private string? _recoveredPath;
    private long _generation;

    /// <summary>Writes unsaved work on a worker. Returns true if the recovery copy is still current when written.</summary>
    public Task<bool> SaveAsync(WorldSnapshot snapshot, ProjectStore.ProjectSettings settings) =>
        SaveAsync(path => Task.Run(() => ProjectStore.Save(snapshot, path, settings)));

    internal async Task<bool> SaveAsync(Func<string, Task> write)
    {
        long generation;
        lock (_gate) generation = ++_generation;

        // Keep the write out of recovery until it is complete and still belongs to the current save point.
        string staged = _autosavePath + $".{Guid.NewGuid():N}.pending";
        try
        {
            await write(staged).ConfigureAwait(false);
            lock (_gate)
            {
                if (generation != _generation) return false;
                File.Move(staged, _recoveredPath ?? _autosavePath, overwrite: true);
                return true;
            }
        }
        finally
        {
            TryDelete(staged);
        }
    }

    /// <summary>
    /// Clears recovery copies and invalidates writes in progress after a save or a document change.
    /// When opening a recovery copy, keep that copy until the recovered work is saved or discarded.
    /// </summary>
    public void Discard(string? recoveredPath = null)
    {
        lock (_gate)
        {
            _generation++;
            _recoveredPath = null;
            foreach (var file in RecoveryFiles())
            {
                if (string.Equals(file.FullName, recoveredPath, StringComparison.OrdinalIgnoreCase))
                    _recoveredPath = file.FullName;
                else
                    TryDelete(file.FullName);
            }
        }
    }

    /// <summary>Finds the newest completed recovery copy, including the original fixed-name autosave.</summary>
    public FileInfo? FindRecovery()
    {
        lock (_gate) return RecoveryFiles().MaxBy(file => file.LastWriteTimeUtc);
    }

    private IEnumerable<FileInfo> RecoveryFiles() => GetFiles("*" + ProjectStore.Extension).Where(file => IsRecoveryFile(file.Name));

    private static bool IsRecoveryFile(string name)
    {
        if (name.Equals(AutosaveFileName, StringComparison.OrdinalIgnoreCase)) return true;

        // Also accept numbered recovery copies such as autosave-2.7dmap.
        const string prefix = AutosaveName + "-";
        int digits = name.Length - prefix.Length - ProjectStore.Extension.Length;
        return digits > 0 && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && name.EndsWith(ProjectStore.Extension, StringComparison.OrdinalIgnoreCase)
            && name.AsSpan(prefix.Length, digits).IndexOfAnyExceptInRange('0', '9') < 0;
    }

    private FileInfo[] GetFiles(string pattern)
    {
        try { return new DirectoryInfo(folder).GetFiles(pattern); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort if the folder or a file is in use.
        }
    }
}
