using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MapEditor.Core.GameData;
using MapEditor.Core.IO;
using Microsoft.Win32;

namespace MapEditor.App;

/// <summary>Picks maps from GeneratedWorlds and writes them, with their custom prefabs, as a mod zip.</summary>
public sealed class PackageDialog : Window
{
    private readonly GameInstall _install;
    private readonly PrefabLibrary _library;
    private readonly PackageSettings _settings = PackageSettings.Load();
    private readonly List<(CheckBox Box, WorldFolder World)> _worlds = [];
    private readonly TextBox _name = new(), _display = new(), _description = new(), _author = new(), _version = new() { Text = "1.0.0" },
        _website = new(), _config = new();
    private readonly CheckBox _antiCheat = new() { Content = "_Skip with anti-cheat", IsChecked = true };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _nameNote = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _bar = new() { Height = 8, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly Button _save = new() { Content = "_Save as...", MinWidth = 110, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _cancelWrite = new() { Content = "Cancel", MinWidth = 110, Margin = new Thickness(0, 0, 8, 0), Visibility = Visibility.Collapsed };
    private readonly Button _close = new() { Content = "Close", MinWidth = 90, IsCancel = true };
    private readonly Panel _fields = new StackPanel();
    private CancellationTokenSource? _cancel;
    private bool _syncingName;
    private BundlePlan _plan = new([], [], 0);

    /// <summary>Raised with the zip name after each successful package.</summary>
    public event EventHandler<string>? Packaged;

    public PackageDialog(GameInstall install, PrefabLibrary library)
    {
        _install = install;
        _library = library;
        Title = "Package maps";
        Width = 640;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _author.Text = _settings.Author;
        _website.Text = _settings.Website;

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(Secondary("Zips exported maps and the custom prefabs they use as a mod. The game's own prefabs are never included."));

        root.Children.Add(Header("Maps in GeneratedWorlds"));
        var list = new StackPanel();
        foreach (var world in MapBundle.ListWorlds(GameInstall.UserGeneratedWorldsDir))
        {
            var box = new CheckBox
            {
                Content = $"{world.Name}    {FormatSize(world.Bytes)}    {world.Modified:yyyy-MM-dd HH:mm}",
                Margin = new Thickness(0, 3, 0, 3),
            };
            box.Checked += (_, _) => Refresh();
            box.Unchecked += (_, _) => Refresh();
            _worlds.Add((box, world));
            list.Children.Add(box);
        }
        if (_worlds.Count == 0) list.Children.Add(Secondary("No maps found in " + GameInstall.UserGeneratedWorldsDir));
        root.Children.Add(new ScrollViewer
        {
            Content = list, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderThickness = new Thickness(1), BorderBrush = (Brush)FindResource("DividerStrokeColorDefaultBrush"),
            Padding = new Thickness(8, 4, 8, 4),
        });

        root.Children.Add(Header("Mod"));
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        AddRow(grid, "_Display name", _display);
        AddRow(grid, "_Name", _name);
        AddRow(grid, "D_escription (optional)", _description);
        AddRow(grid, "A_uthor (optional)", _author);
        AddRow(grid, "_Version", _version);
        AddRow(grid, "_Website (optional)", _website);
        var browse = new Button { Content = "_Browse...", Margin = new Thickness(8, 4, 0, 4) };
        browse.Click += (_, _) => BrowseConfig();
        AddRow(grid, "_Config folder (optional)", _config, browse);
        _antiCheat.Margin = new Thickness(0, 6, 0, 0);
        Grid.SetColumn(_antiCheat, 1);
        Grid.SetRow(_antiCheat, grid.RowDefinitions.Count);
        grid.RowDefinitions.Add(new RowDefinition());
        grid.Children.Add(_antiCheat);
        _fields.Children.Add(grid);
        _fields.Children.Add(_nameNote);
        root.Children.Add(_fields);

        root.Children.Add(Header("Prefabs"));
        root.Children.Add(new ScrollViewer { Content = _summary, MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        root.Children.Add(_bar);
        root.Children.Add(_status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        _save.Style = (Style)FindResource("AccentButtonStyle");
        _save.Click += async (_, _) => await SaveAsync();
        _cancelWrite.Click += (_, _) => _cancel?.Cancel();
        buttons.Children.Add(_save);
        buttons.Children.Add(_cancelWrite);
        buttons.Children.Add(_close);
        root.Children.Add(buttons);
        Content = root;

        // Name follows Display name (spaces to hyphens) until it is typed in by hand.
        _display.TextChanged += (_, _) =>
        {
            if (_name.IsKeyboardFocusWithin) return;
            _syncingName = true;
            _name.Text = MapBundle.DefaultName(_display.Text);
            _syncingName = false;
            // The name just changed, so Save as may now be ready (Refresh skips changes made while syncing).
            Refresh();
        };
        _name.TextChanged += (_, _) => Refresh();
        _version.TextChanged += (_, _) => Refresh();
        Closing += (_, e) => { if (_cancel is not null) e.Cancel = true; };
        Loaded += (_, _) => _display.Focus();
        Refresh();
    }

    private static string FormatSize(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB" : $"{bytes / (double)(1L << 20):0} MB";

    private TextBlock Secondary(string text) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
        Foreground = (Brush)FindResource("TextFillColorSecondaryBrush"),
    };

    private static TextBlock Header(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 6) };

    private static void AddRow(Grid grid, string label, TextBox box, Button? extra = null)
    {
        int row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition());
        var l = new Label { Content = label, Target = box, VerticalAlignment = VerticalAlignment.Center };
        box.Margin = new Thickness(0, 4, 0, 4);
        Grid.SetRow(l, row);
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
        grid.Children.Add(l);
        grid.Children.Add(box);
        if (extra is not null)
        {
            Grid.SetRow(extra, row);
            Grid.SetColumn(extra, 2);
            grid.Children.Add(extra);
        }
        else Grid.SetColumnSpan(box, 2);
    }

    private void BrowseConfig()
    {
        var dialog = new OpenFolderDialog { Title = "Config folder to include" };
        if (dialog.ShowDialog(this) == true) _config.Text = dialog.FolderName;
    }

    private List<string> SelectedWorlds() => [.. _worlds.Where(w => w.Box.IsChecked == true).Select(w => w.World.Path)];

    // The game wants a DisplayName; with none typed, the Name stands in.
    private BundleInfo Info() => new(_name.Text, _display.Text.Trim() is { Length: > 0 } d ? d : _name.Text, _description.Text.Trim(), _author.Text.Trim(),
        _version.Text.Trim(), _website.Text.Trim(), _antiCheat.IsChecked == true);

    private void Refresh()
    {
        if (_syncingName) return;
        var worlds = SelectedWorlds();
        _plan = worlds.Count == 0 ? new BundlePlan([], [], 0) : MapBundle.Plan(worlds, _library, _install.PrefabsDir);
        var text = new StringBuilder();
        if (worlds.Count == 0) text.Append("Pick one or more maps.");
        else
        {
            text.Append($"{_plan.Prefabs.Count} custom prefab{(_plan.Prefabs.Count == 1 ? "" : "s")} will be included ({_plan.VanillaCount} of the game's own are left out).");
            if (_plan.Prefabs.Count > 0) text.Append('\n').Append(string.Join(", ", _plan.Prefabs.Select(p => p.Name)));
            if (_plan.Missing.Count > 0)
                text.Append($"\n\nNot found anywhere, so not included ({_plan.Missing.Count}): ").Append(string.Join(", ", _plan.Missing));
        }
        _summary.Text = text.ToString();

        string? error = MapBundle.NameError(_name.Text);
        _nameNote.Text = error ?? MapBundle.NameWarning(_name.Text) ?? "";
        _save.IsEnabled = worlds.Count > 0 && error is null && _version.Text.Trim().Length > 0 && _cancel is null;
    }

    private async Task SaveAsync()
    {
        var worlds = SelectedWorlds();
        var info = Info();
        if (_plan.Missing.Count > 0 && ThemedMessageBox.Show(this,
                $"{_plan.Missing.Count} prefab{(_plan.Missing.Count == 1 ? " is" : "s are")} used by the maps but found nowhere:\n\n"
                + string.Join(", ", _plan.Missing) + "\n\nPackage the maps anyway?",
                "Package maps", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        string? config = _config.Text.Trim() is { Length: > 0 } c ? c : null;
        if (config is not null && !Directory.Exists(config))
        {
            ThemedMessageBox.Show(this, "The Config folder doesn't exist.", "Package maps", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var picker = new SaveFileDialog
        {
            Filter = "Zip file (*.zip)|*.zip",
            FileName = info.Name + ".zip",
            InitialDirectory = _settings.LastFolder is { } d && Directory.Exists(d) ? d : "",
        };
        if (picker.ShowDialog(this) != true) return;
        string zip = picker.FileName;

        _settings.Author = info.Author;
        _settings.Website = info.Website;
        _settings.LastFolder = Path.GetDirectoryName(zip);
        _settings.Save();

        _cancel = new CancellationTokenSource();
        var token = _cancel.Token;
        SetBusy(true);
        var plan = _plan;
        try
        {
            await Task.Run(() => MapBundle.Write(zip, info, worlds, plan, config, p => Dispatcher.BeginInvoke(() =>
            {
                _bar.Maximum = Math.Max(1, p.BytesTotal);
                _bar.Value = p.BytesDone;
                _status.Text = p.File;
            }), token));
            _bar.Value = _bar.Maximum;
            _status.Text = $"Wrote {zip} ({FormatSize(new FileInfo(zip).Length)}). Unzip it into a Mods folder.";
            Packaged?.Invoke(this, Path.GetFileName(zip));
        }
        catch (OperationCanceledException)
        {
            _bar.Visibility = Visibility.Collapsed;
            _status.Text = "Cancelled. Nothing was written.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _bar.Visibility = Visibility.Collapsed;
            _status.Text = "Failed: " + e.Message;
        }
        finally
        {
            _cancel.Dispose();
            _cancel = null;
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _fields.IsEnabled = !busy;
        foreach (var (box, _) in _worlds) box.IsEnabled = !busy;
        _cancelWrite.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        _close.IsEnabled = !busy;
        if (busy)
        {
            _bar.Visibility = _status.Visibility = Visibility.Visible;
            _bar.Value = 0;
            _status.Text = "Writing...";
        }
        Refresh();
    }
}
