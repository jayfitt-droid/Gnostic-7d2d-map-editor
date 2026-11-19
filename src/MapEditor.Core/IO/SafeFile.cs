namespace MapEditor.Core.IO;

/// <summary>Writes a file beside its destination, then moves it into place after the stream closes.</summary>
internal static class SafeFile
{
    public static void Write(string path, Action<Stream> write, int bufferSize = 4096)
    {
        string full = Path.GetFullPath(path);
        string temp = Path.Combine(Path.GetDirectoryName(full)!, $".mapeditor-{Guid.NewGuid():N}.tmp");
        var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize);
        try
        {
            using (file) write(file);
            File.Move(temp, full, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }
}
