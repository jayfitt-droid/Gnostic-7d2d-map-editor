using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace MapEditor.App;

public partial class App : Application
{
    /// <summary>The name shown in the title bar, About and message boxes. Install folders and file names keep 7D2DMapEditor.</summary>
    public const string ProductName = "Gnostic - 7D2D Map Editor";

    public static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "7D2DMapEditor", "crash.log");

    private bool _reportedError;

    /// <summary>How many times one error (same type, message and place) is written per run before its repeats are only counted.</summary>
    private const int RepeatsLogged = 3;

    /// <summary>Past this size the log is moved to crash.old.log and started again.</summary>
    private const long MaxLogBytes = 1 << 20;

    private static readonly Dictionary<string, int> Seen = [];
    private static readonly Lock LogLock = new();

    /// <summary>
    /// Appends an exception to the crash log; best effort. A render or tile error can repeat every frame, so each
    /// distinct error is written only <see cref="RepeatsLogged"/> times a run, and the log is kept to about 1 MiB (#22).
    /// </summary>
    public static void LogError(Exception error)
    {
        string key = $"{error.GetType().FullName}|{error.Message}|{error.StackTrace?.Split('\n').FirstOrDefault()}";
        lock (LogLock)
        {
            int count = Seen[key] = Seen.GetValueOrDefault(key) + 1;
            if (count > RepeatsLogged) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
                if (File.Exists(CrashLogPath) && new FileInfo(CrashLogPath).Length > MaxLogBytes)
                    File.Move(CrashLogPath, Path.ChangeExtension(CrashLogPath, ".old.log"), overwrite: true);
                string note = count == RepeatsLogged ? $"(Seen {RepeatsLogged} times; further repeats this run aren't logged.){Environment.NewLine}" : "";
                File.AppendAllText(CrashLogPath, $"[{DateTime.Now:u}] {error}{Environment.NewLine}{note}{Environment.NewLine}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging is best effort.
            }
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        // Older versions kept the prefab picture cache in the install folder, which stops Setup installing (#11).
        Task.Run(() => MapEditor.Core.IO.CacheFolders.RemoveOldCache(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
    }

    /// <summary>Logs UI-thread exceptions and keeps the editor open, so a bug does not lose the user's work.</summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogError(e.Exception);
        e.Handled = true;
        // A failing render repeats every frame; tell the user once rather than flooding them with dialogs.
        if (_reportedError) return;
        _reportedError = true;
        ThemedMessageBox.Show(MainWindow, $"Something went wrong:\n\n{e.Exception.Message}\n\nDetails were written to {CrashLogPath}.",
            ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
