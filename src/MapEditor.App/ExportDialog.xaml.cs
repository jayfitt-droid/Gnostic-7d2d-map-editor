using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MapEditor.Core.GameData;
using MapEditor.Core.GameLink;
using MapEditor.Core.IO;
using MapEditor.Core.Validate;
using Microsoft.Win32;

namespace MapEditor.App;

/// <summary>
/// Picks a world name and parent folder for an export, and shows the validation results first so problems are
/// seen before the world reaches the game. The world is written to Location\Name.
/// </summary>
public partial class ExportDialog : Window
{
    private readonly bool _hasErrors;
    private readonly GameInstall? _install;
    private readonly GameLinkStatus? _gameStatus;
    private readonly string? _previousExportFolder;
    private readonly bool? _exportHasChanges;

    /// <param name="prefabNote">Shown above the issues, e.g. which prefabs keep RWG's heights; null for none.</param>
    public ExportDialog(string name, string location, IReadOnlyList<ValidationIssue> issues, string? prefabNote,
        GameInstall? install, AfterExportAction choice, int radius, GameLinkStatus? gameStatus,
        string? previousExportFolder, bool? exportHasChanges)
    {
        _install = install;
        _gameStatus = gameStatus;
        _previousExportFolder = previousExportFolder;
        _exportHasChanges = exportHasChanges;
        ActionPreference = choice;
        Radius = radius;
        InitializeComponent();
        if (prefabNote is not null)
        {
            PrefabNote.Text = prefabNote;
            PrefabNote.Visibility = Visibility.Visible;
        }
        NameBox.Text = name;
        LocationBox.Text = location;
        RadiusBox.Text = radius.ToString(System.Globalization.CultureInfo.InvariantCulture);
        IssuesList.ItemsSource = issues;

        int errors = issues.Count(i => i.Severity == IssueSeverity.Error);
        int warnings = issues.Count(i => i.Severity == IssueSeverity.Warning);
        _hasErrors = errors > 0;
        IssuesHeader.Text = issues.Count == 0
            ? "✓ No problems found."
            : $"{Plural(errors, "error")}, {Plural(warnings, "warning")}. Double-click an issue to close this and find it on the map; it stays in the Issues panel.";
        IssuesList.Visibility = issues.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (_hasErrors) ExportButton.Content = "Export anyway";

        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";

    /// <summary>
    /// The issue double-clicked (or Enter) to find on the map: the dialog closes without exporting, and the window
    /// shows it (#27). Null otherwise.
    /// </summary>
    public ValidationIssue? GoTo { get; private set; }

    private void Issue_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Only a double-click on a row: not on the scroll bar or the empty space under the last row.
        if (ItemsControl.ContainerFromElement(IssuesList, (DependencyObject)e.OriginalSource) is not ListBoxItem { DataContext: ValidationIssue issue }) return;
        Find(issue);
    }

    private void Issue_KeyDown(object sender, KeyEventArgs e)
    {
        // Enter on an issue finds it, rather than pressing the default Export button.
        if (e.Key != Key.Enter || IssuesList.SelectedItem is not ValidationIssue issue) return;
        Find(issue);
        e.Handled = true;
    }

    private void Find(ValidationIssue issue)
    {
        if (!issue.HasLocation) return;
        GoTo = issue;
        DialogResult = false;
    }

    /// <summary>The chosen world folder, set when the dialog returns true.</summary>
    public string? Folder { get; private set; }

    /// <summary>The action to run for this export.</summary>
    public AfterExportAction Action => RebuildRadio.IsChecked == true ? AfterExportAction.Rebuild :
        PlaytestRadio.IsChecked == true ? AfterExportAction.Playtest : AfterExportAction.None;

    /// <summary>The last choice stays remembered when the current export cannot use it.</summary>
    public AfterExportAction ActionPreference { get; private set; }

    public int Radius { get; private set; }

    private string? CurrentFolder()
    {
        string name = NameBox.Text.Trim();
        string location = LocationBox.Text.Trim();
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || location.Length == 0) return null;
        try
        {
            return Path.GetFullPath(Path.Combine(location, name));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private void Input_Changed(object sender, TextChangedEventArgs e)
    {
        if (RadiusBox is null || RebuildNote is null || ExportButton is null) return;
        var folder = CurrentFolder();
        UpdateActions(folder);
        if (folder is null)
            InfoText.Text = "Enter a name that can be used as a folder name, and a location.";
        else if (File.Exists(Path.Combine(folder, WorldFiles.Height)))
            InfoText.Text = $"A world already exists at {folder}. Exporting replaces it.";
        else if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
            InfoText.Text = $"{folder} already has files in it; the world files will be added alongside them.";
        else
            InfoText.Text = $"Exports to {folder}";
    }

    private void Action_Click(object sender, RoutedEventArgs e)
    {
        ActionPreference = Action;
        UpdateActions(CurrentFolder());
    }

    private void UpdateActions(string? folder)
    {
        string? note = null;
        if (folder is null || !GameLinkPlaytest.CanOpen(folder, _install))
            note = "Export to the game's GeneratedWorlds folder to open it in the game.";
        else if (!GameLinkPlaytest.CanEnterSaveName(GameLinkPlaytest.SaveName(Path.GetFileName(folder))))
            note = "Use a world name that the game can use for the playtest save. The save name can have up to 50 characters.";
        PlaytestRadio.IsEnabled = note is null;
        OpenInGameNote.Text = note;
        OpenInGameNote.Visibility = note is null ? Visibility.Collapsed : Visibility.Visible;

        string? rebuildReason = GameLinkRebuild.UnavailableReason(folder, _install, _gameStatus,
            _previousExportFolder, _exportHasChanges);
        RebuildRadio.IsEnabled = rebuildReason is null;
        if (RebuildRadio.IsEnabled && ActionPreference == AfterExportAction.Rebuild)
            RebuildRadio.IsChecked = true;
        else if (PlaytestRadio.IsEnabled && ActionPreference == AfterExportAction.Playtest)
            PlaytestRadio.IsChecked = true;
        else
            DoNothingRadio.IsChecked = true;

        RadiusPanel.IsEnabled = Action == AfterExportAction.Rebuild;
        bool validRadius = int.TryParse(RadiusBox.Text, out int radius) && radius == GameLinkRebuildArea.ClampRadius(radius);
        if (validRadius) Radius = radius;
        RebuildNote.Text = rebuildReason ?? (RadiusPanel.IsEnabled && !validRadius
            ? "Enter a whole number from 16 to 512."
            : $"Rebuilds around the player in '{_gameStatus!.Save}'.");
        ExportButton.IsEnabled = folder is not null && (!RadiusPanel.IsEnabled || validRadius);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose where to export the world", InitialDirectory = LocationBox.Text };
        if (dialog.ShowDialog(this) == true) LocationBox.Text = dialog.FolderName;
    }

    private void GameFolder_Click(object sender, RoutedEventArgs e) => LocationBox.Text = GameInstall.UserGeneratedWorldsDir;

    internal Action? ExportClicked { get; init; }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentFolder() is not { } folder) return;
        ExportClicked?.Invoke();
        if (_hasErrors &&
            ThemedMessageBox.Show(this, "The world has errors, so the game may not load it properly. Export it anyway?", "Export World",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;
        if (File.Exists(Path.Combine(folder, WorldFiles.Height)) &&
            ThemedMessageBox.Show(this, $"Replace the world in '{Path.GetFileName(folder)}'?", "Export World",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        Folder = folder;
        DialogResult = true;
    }
}
