using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MapEditor.App.Editing;
using MapEditor.App.Map;
using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.GameLink;
using MapEditor.Core.Generate;
using MapEditor.Core.IO;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;
using MapEditor.Core.Rendering;
using MapEditor.Core.Validate;
using Microsoft.Win32;

namespace MapEditor.App;

public partial class MainWindow : Window
{
    public static readonly RoutedUICommand NewWorldCommand = new("New World", nameof(NewWorldCommand), typeof(MainWindow));
    public static readonly RoutedUICommand OpenCommand = new("Open", nameof(OpenCommand), typeof(MainWindow));
    public static readonly RoutedUICommand OpenWorldCommand = new("Open World Folder", nameof(OpenWorldCommand), typeof(MainWindow));
    public static readonly RoutedUICommand SaveCommand = new("Save", nameof(SaveCommand), typeof(MainWindow));
    public static readonly RoutedUICommand SaveAsCommand = new("Save Project As", nameof(SaveAsCommand), typeof(MainWindow));
    public static readonly RoutedUICommand ExportCommand = new("Export World", nameof(ExportCommand), typeof(MainWindow));
    public static readonly RoutedUICommand CloseWorldCommand = new("Close", nameof(CloseWorldCommand), typeof(MainWindow));
    public static readonly RoutedUICommand ValidateCommand = new("Validate", nameof(ValidateCommand), typeof(MainWindow));
    public static readonly RoutedUICommand GenerateCommand = new("Generate", nameof(GenerateCommand), typeof(MainWindow));
    public static readonly RoutedUICommand PackageCommand = new("Package Maps", nameof(PackageCommand), typeof(MainWindow));
    public static readonly RoutedUICommand FindCommand = new("Find", nameof(FindCommand), typeof(MainWindow));
    public static readonly RoutedUICommand UndoCommand = new("Undo", nameof(UndoCommand), typeof(MainWindow));
    public static readonly RoutedUICommand RedoCommand = new("Redo", nameof(RedoCommand), typeof(MainWindow));
    public static readonly RoutedUICommand HistoryMoveCommand = new("Move to history entry", nameof(HistoryMoveCommand), typeof(MainWindow));

    private readonly MainViewModel _vm = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly UndoStack _undo = new();
    /// <summary>Every edit goes through this: it commits as one undo step or rolls back if it fails.</summary>
    private readonly EditRunner _edits;
    private readonly TerrainToolController _tools;
    private readonly PrefabToolController _prefabTools;
    private readonly MultiSelectToolController _multiSelect;
    private readonly TownToolController _townTools;
    private readonly GenerateToolController _genTools;
    private readonly BridgeToolController _bridgeTools;
    /// <summary>Cancels the generator stage that is running, if one is.</summary>
    private CancellationTokenSource? _generateCancel;
    private Task _gameDataLoad = Task.CompletedTask;
    private GameInstall? _install;

    private World? _world;
    private string? _sourcePath;  // world folder the world was opened from
    private string? _savePath;    // world folder Ctrl+S writes to (an opened world folder, or the last export)
    private string? _projectPath; // .7dmap the world was opened from or saved to; Ctrl+S prefers it
    private ProjectStore.ProjectSettings _projectSettings = new();
    private object? _exportPosition;
    private string? GameLinkExportFolder => _savePath ?? _projectSettings.ExportFolder;
    private bool? ExportHasChanges => !ReferenceEquals(_exportPosition, _undo.Position)
        ? true : _projectSettings.ExportHasChanges;
    /// <summary>
    /// Unsaved changes that are not undo steps: Generate panel settings, plans, a changed export folder, a recovered autosave.
    /// Edits are tracked by the undo history's save point instead, so undoing back to it is not a change (see <see cref="Dirty"/>).
    /// </summary>
    private bool _dirty;
    /// <summary>
    /// Counts the changes behind <see cref="_dirty"/>. A save notes it before writing and clears <see cref="_dirty"/>
    /// only if nothing changed while it ran, so a later change is never marked as saved.
    /// </summary>
    private long _revision;

    /// <summary>The world differs from what was last saved: a setting changed, or the history is not at the save point.</summary>
    private bool Dirty => _world is not null && (_dirty || !_undo.AtSavePoint);
    /// <summary>The window was asked to close while a save was running; it closes once the save is done.</summary>
    private bool _closeWhenIdle;
    private static readonly string AutosaveFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "7D2DMapEditor", "autosave");
    private readonly AutosaveStore _autosaves = new(AutosaveFolder);
    private readonly System.Windows.Threading.DispatcherTimer _autosaveTimer = new() { Interval = TimeSpan.FromMinutes(5) };
    private bool _autosaving;

    public MainWindow()
    {
        if (_settings.UsesModernTheme) ModernTheme.Start();
        InitializeComponent();
        DataContext = _vm;
        Overview.Target = Map;
        PlayerOverlay.Target = Map;
        _gameLinkPollTimer.Tick += PollGameLink_Tick;
        RefreshGameLinkInstallation();
        _gameLinkPollTimer.Start();
        Map.CursorMoved += Map_CursorMoved;
        Map.MenuRequested += (_, p) => ShowMapMenu(p);
        Map.ViewChanged += (_, _) =>
        {
            if (!_centeringOnPlayer) _vm.FollowPlayer = false;
            UpdateZoomText();
        };
        _autosaveTimer.Tick += async (_, _) => await AutosaveAsync();
        _autosaveTimer.Start();
        _vm.PropertyChanged += Vm_PropertyChanged;
        // The automatic ribbon goes compact below this height (DIPs): 1366×768 at 100%, or 1080p at 150%.
        SizeChanged += (_, _) =>
        {
            _vm.WindowIsShort = ActualHeight < 800;
            _vm.WindowIsNarrow = ActualWidth < MainViewModel.ToolNamesMinWidth;
            _vm.WindowIsTight = ActualWidth < MainViewModel.ActionTextMinWidth;
            _vm.InlineHintMaxWidth = Math.Max(200, ActualWidth - 240);
        };

        _edits = new EditRunner(_undo);
        _edits.Committed += (_, edit) =>
        {
            _vm.History?.RecordEdit(edit);
            _vm.RefreshHistory();
            OnWorldEdited();
        };
        _edits.Failed += (_, e) => OnEditFailed(e);
        _landTools = new LandToolController(Map, _vm);
        LandTools.DataContext = LandPanel.DataContext = _landTools;
        Map.LandToolInput += _landTools.OnToolInput;
        _landTools.HistoryChanged += (_, _) => UpdateUndoText();
        _tools = new TerrainToolController(Map, _vm, _edits);
        _prefabTools = new PrefabToolController(Map, _vm, _edits);
        _townTools = new TownToolController(Map, _vm, _edits);
        _genTools = new GenerateToolController(Map, _vm);
        _bridgeTools = new BridgeToolController(Map, _vm, _edits) { Bridges = () => _projectSettings.Bridges };
        // Roads snap to the ends of the Bridge tool's bridges and the Highways stage's, at deck height.
        _tools.BridgeEnds = () => _bridgeTools.Ends(_projectSettings.Generator?.PlacedRoads);
        // A click on a piece of one of the tool's bridges picks the whole bridge, in the Bridge tool.
        _prefabTools.BridgeOf = p => BridgeTool.Owner(_projectSettings.Bridges, p);
        // The Highways stage's bridge pieces stay as it built them: they can be selected, not moved or deleted.
        _prefabTools.IsStageBridge = p => BridgeTool.StageOwner(_projectSettings.Generator?.PlacedRoads, p) is not null;
        _multiSelect = new MultiSelectToolController(Map, _vm, _edits,
            p => _prefabTools.BridgeOf?.Invoke(p) is not null, p => _prefabTools.IsStageBridge?.Invoke(p) == true);
        Map.ToolInput += _multiSelect.OnToolInput;
        _prefabTools.BridgePicked += (_, bridge) =>
        {
            _vm.SelectedTool = EditorTool.Bridge;
            _bridgeTools.Select(bridge);
        };
        // Undo and redo can take a selected bridge away.
        _undo.Changed += (_, _) => _bridgeTools.Refresh();
        // The panel's seeds, weights, rows and pins are generator settings, which the project saves (#14).
        _vm.Generate.SettingsChanged += (_, _) => MarkSettingsChanged();
        _vm.Generate.PropertyChanged += (_, _) => _genTools.Refresh();
        // A stage's map tool (Place features, Edit on the map, ...) is put down when another stage card opens, so a
        // click made while working on stage 4 can't still place a stage 3 lake.
        _vm.Generate.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(GenerateViewModel.OpenStage)) return;
            int? owner = _vm.SelectedTool switch
            {
                EditorTool.GenerateArea => 1,
                EditorTool.Features => 3,
                EditorTool.Spawn => 6,
                _ => null,
            };
            if (owner is { } stage && stage != _vm.Generate.OpenStage) _vm.SelectedTool = EditorTool.Select;
        };
        _genTools.FeatureClicked += (_, p) => FeatureClicked(p.X, p.Z);
        _undo.Changed += (_, _) => UpdateUndoText();

        _chosenTheme = _settings.UsesModernTheme ? AppSettings.ModernTheme : AppSettings.FlatTheme;
        TickChosenTheme();
        if (_settings.UsesModernTheme) UseModernLayout();

        Loaded += async (_, _) =>
        {
            _gameDataLoad = LoadGameDataAsync();
            _vm.Autosave = _settings.Autosave;
            _vm.RibbonMode = _settings.RibbonMode;
            _vm.ShowMiniMap = _settings.ShowMiniMap;
            _vm.ShowSidePanel = _settings.ShowSidePanel;
            _vm.SideBarOnRight = _settings.SideBarOnRight;
            if (_settings.SidePanelWidth > 0)
                _vm.SidePanelWidth = Math.Clamp(_settings.SidePanelWidth, MainViewModel.MinSidePanelWidth, MainViewModel.MaxSidePanelWidth);
            _vm.Favourites = new HashSet<string>(_settings.FavouritePrefabs, StringComparer.OrdinalIgnoreCase);
            LoadPresets();
            LoadTownPresets();
            // Gnostic.exe "<world folder or .7dmap>" opens it straight away.
            var args = Environment.GetCommandLineArgs();
            if (args.Length > 1 && Directory.Exists(args[1])) await OpenWorldAsync(args[1]);
            else if (args.Length > 1 && File.Exists(args[1])) await OpenProjectAsync(args[1]);
            else await OfferRecoveryAsync();
            await CheckUpdatesQuietlyAsync();
        };
        Closing += MainWindow_Closing;

        // Open at the size it was closed at; fit a small screen.
        if (_settings.WindowWidth >= MinWidth && _settings.WindowHeight >= MinHeight)
        {
            Width = Math.Min(_settings.WindowWidth, SystemParameters.WorkArea.Width);
            Height = Math.Min(_settings.WindowHeight, SystemParameters.WorkArea.Height);
        }
        else
        {
            Width = Math.Min(Width, SystemParameters.WorkArea.Width);
            Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        }
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (_settings.WindowMaximized) WindowState = WindowState.Maximized;
    }

    // ---- game data ---------------------------------------------------------

    /// <summary>
    /// Reads biomes, prefabs, stamps and names from the game folder. It never fails: <see cref="_gameDataLoad"/> is
    /// awaited before every open and generate, so a failed task would fail all of those too (#17). If the folder
    /// can't be read, the editor carries on without game data and says so.
    /// </summary>
    private async Task LoadGameDataAsync()
    {
        _install = null;
        if (_settings.GameFolder is not { } folder || !GameInstall.IsValid(folder))
        {
            UseNoGameData("Not set. Use Settings > Game Folder so biomes, prefabs and stamps are available.");
            return;
        }

        _vm.GameFolderText = folder + "\n(loading prefabs...)";
        var sw = Stopwatch.StartNew();
        GameInstall install;
        BiomeTable biomes;
        PrefabLibrary prefabs;
        StampLibrary stamps;
        GameNames names;
        try
        {
            (install, biomes, prefabs, stamps, names) = await Task.Run(() =>
            {
                var gi = GameInstall.Open(folder);
                return (gi, BiomeTable.Load(gi), PrefabLibrary.Load(gi), StampLibrary.Load(gi), GameNames.Load(gi));
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            App.LogError(ex);
            UseNoGameData($"{folder}\nCouldn't be read, so biomes, prefabs and stamps are unavailable: {ex.Message}");
            _vm.Status = "Couldn't read the game folder: " + ex.Message;
            return;
        }
        _install = install;
        GameNames.Current = names; // before the lists below, which show these names
        _vm.Biomes = biomes;
        _vm.Prefabs = prefabs;
        _vm.Stamps = stamps;
        _vm.StampNames.Clear();
        foreach (var name in stamps.Names) _vm.StampNames.Add(name);
        // Keep the stamp picked before only if this game folder has it too (#19).
        if (_vm.SelectedStamp is not { } kept || !_vm.StampNames.Contains(kept))
            _vm.SelectedStamp = _vm.StampNames.FirstOrDefault(n => n.StartsWith("mountains")) ?? _vm.StampNames.FirstOrDefault();
        Map.Prefabs = prefabs;
        LoadMixer();
        Map.InvalidateVisual();
        _vm.GameFolderText = $"{folder}\n{prefabs.Count:N0} prefabs, {stamps.Names.Count} stamps, {biomes.Biomes.Count} biomes ({sw.Elapsed.TotalSeconds:0.0} s)"
            + (prefabs.Skipped == 0 ? "" : $"\n{prefabs.Skipped:N0} prefab file{(prefabs.Skipped == 1 ? "" : "s")} or folder{(prefabs.Skipped == 1 ? "" : "s")} couldn't be read and were left out.");
    }

    /// <summary>Carries on without game data: the game's vanilla biome colours, no prefabs or stamps.</summary>
    private void UseNoGameData(string folderText)
    {
        _install = null;
        _vm.GameFolderText = folderText;
        GameNames.Current = GameNames.Empty;
        _vm.Biomes = BiomeTable.Vanilla;
        _vm.Prefabs = PrefabLibrary.Empty;
        _vm.Stamps = StampLibrary.Empty;
        _vm.StampNames.Clear();
        _vm.SelectedStamp = null;
        Map.Prefabs = PrefabLibrary.Empty;
        LoadMixer();
        Map.InvalidateVisual();
    }

    /// <summary>Reads rwgmixer.xml for the open world's size, which sets the township tile ranges.</summary>
    private void LoadMixer()
    {
        try
        {
            _vm.Mixer = _install is null ? RwgMixer.Empty : RwgMixer.Load(_install, _world?.Size ?? 8192);
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException or UnauthorizedAccessException)
        {
            _vm.Mixer = RwgMixer.Empty;
            _vm.Status = "Could not read rwgmixer.xml, so the Town tool is unavailable: " + ex.Message;
        }
        // The wilderness card shows the counts for this world size.
        _vm.Generate.Mixer = _vm.Mixer;
        _vm.Generate.RefreshWilderness();
    }

    private async void GameFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the 7 Days to Die game folder (the one containing Data)",
            InitialDirectory = _settings.GameFolder ?? "",
        };
        if (dialog.ShowDialog(this) != true) return;
        if (!GameInstall.IsValid(dialog.FolderName))
        {
            ThemedMessageBox.Show(this, "That folder has no Data\\Config\\biomes.xml, so it doesn't look like a 7 Days to Die install.",
                "Game Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _settings.GameFolder = dialog.FolderName;
        _settings.Save();
        await (_gameDataLoad = LoadGameDataAsync());
        if (_world is not null)
        {
            // Prefabs the old folder didn't have can be converted now.
            GroundLevel.Resolve(_world, _vm.Prefabs);
            _prefabTools.Refresh();
            _townTools.Refresh();
            ShowWorld(_world, keepView: true);
        }
    }

    // ---- new / open --------------------------------------------------------

    private bool _opening; // New, Open or Open World Folder is under way

    private void CanOpen_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = !_opening && !_vm.IsBusy && !_vm.IsLandMode;

    /// <summary>
    /// Runs New, Open or Open World Folder once at a time: each waits (for the game data, or the unsaved-changes prompt)
    /// before its dialog shows, and a second click in that time would open a second dialog.
    /// </summary>
    private async Task OnceAtATime(Func<Task> open)
    {
        if (_opening || _vm.IsBusy) return;
        _opening = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            await open();
        }
        finally
        {
            _opening = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async void NewWorld_Executed(object sender, ExecutedRoutedEventArgs e) => await OnceAtATime(NewWorldAsync);

    private async Task NewWorldAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        await _gameDataLoad;
        var dialog = new NewWorldDialog(_vm.Biomes) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Options is not { } options) return;

        _vm.IsBusy = true;
        _vm.Status = "Creating " + options.Name + "...";
        try
        {
            var template = LevelFile.FindTemplate(_install);
            var stamps = _vm.Stamps;
            var world = await Task.Run(() => NewWorldGenerator.Create(options, stamps, template is null ? null : File.ReadAllBytes(template.Path)));
            SetWorld(world, sourcePath: null, dirty: true);
            BeginHistory($"New world: {world.Name}");
            // The Generate panel takes over from here, starting with the terrain.
            _projectSettings.Generator = GeneratorSettings.ForNewWorld(options.Seed,
                options.Edges ?? [EdgeStyle.Land, EdgeStyle.Land, EdgeStyle.Land, EdgeStyle.Land], options.BaseHeight, options.Shape,
                options.BiomePlan is { } biomePlan ? BiomeLayoutSettings.From(biomePlan, options.Seed, options.Shape) : null);
            LoadGeneratorSettings();
            _vm.SidePanelTab = MainViewModel.GenerateTab;
            _vm.ShowSidePanel = true;
            _vm.Status = template is null
                ? "Created. Note: no main.ttw template was found; generate any world in the game once before saving."
                : $"Created {options.Name} ({options.Size} m). Save it with Ctrl+S.";
            if (options.Shape?.Kind == LandShapeKind.Drawn) EnterLandMode(newWorld: true);
        }
        finally
        {
            _vm.IsBusy = false;
        }
    }

    private async void Open_Executed(object sender, ExecutedRoutedEventArgs e) => await OnceAtATime(OpenAsync);

    private async Task OpenAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        var dialog = new OpenFileDialog
        {
            Title = "Open a project or world",
            Filter = "Projects and worlds (*.7dmap, dtm.raw, map_info.xml)|*.7dmap;dtm.raw;map_info.xml|Projects (*.7dmap)|*.7dmap|All files (*.*)|*.*",
            InitialDirectory = _settings.LastWorldFolder is { } last && Directory.Exists(Path.GetDirectoryName(last))
                ? Path.GetDirectoryName(last)!
                : GameInstall.UserGeneratedWorldsDir,
        };
        if (dialog.ShowDialog(this) != true) return;
        if (dialog.FileName.EndsWith(ProjectStore.Extension, StringComparison.OrdinalIgnoreCase)) await OpenProjectAsync(dialog.FileName);
        else await OpenWorldAsync(Path.GetDirectoryName(dialog.FileName)!);
    }

    private async Task OpenProjectAsync(string path, bool recovered = false)
    {
        _vm.IsBusy = true;
        _vm.Status = "Loading " + Path.GetFileName(path) + "...";
        try
        {
            var sw = Stopwatch.StartNew();
            ProjectStore.ProjectSettings settings = new();
            var world = await Task.Run(() => ProjectStore.Load(path, out settings));
            await _gameDataLoad;
            SetWorld(world, sourcePath: null, dirty: recovered, recoveryPath: recovered ? path : null);
            _projectPath = recovered ? null : path;
            _projectSettings = settings;
            BeginHistory(recovered ? $"Recover autosave: {world.Name}" : $"Open project: {Path.GetFileName(path)}");
            _bridgeTools.Refresh();
            LoadGeneratorSettings();
            _savePath = recovered ? null : settings.ExportFolder;
            UpdateTitle();
            _vm.Status = recovered
                ? $"Recovered {world.Name} from the autosave. Save it with Ctrl+S."
                : $"Opened {Path.GetFileName(path)} in {sw.Elapsed.TotalSeconds:0.0} s";
            if (!recovered)
            {
                _settings.LastWorldFolder = path;
                _settings.AddRecent(path);
                _settings.Save();
            }
        }
        catch (Exception ex) when (IsBadInput(ex))
        {
            _vm.Status = "Failed to open the project";
            ThemedMessageBox.Show(this, ex.Message, "Open", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _vm.IsBusy = false;
        }
    }

    /// <summary>Errors a damaged or unexpected file can cause while opening it: shown as a message, not a crash.</summary>
    private static bool IsBadInput(Exception ex) =>
        ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException
            or System.Text.Json.JsonException or System.Xml.XmlException or FormatException or OverflowException
            or ArgumentException or IndexOutOfRangeException;

    /// <summary>Errors writing a file can cause (disk, permissions, a bad path): shown as a message, not a crash.</summary>
    private static bool IsWriteError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException
            or NotSupportedException or System.Security.SecurityException;

    private async void OpenWorld_Executed(object sender, ExecutedRoutedEventArgs e) => await OnceAtATime(OpenWorldFolderAsync);

    private async Task OpenWorldFolderAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        var dialog = new OpenFolderDialog
        {
            Title = "Select a 7 Days to Die world folder",
            InitialDirectory = Directory.Exists(GameInstall.UserGeneratedWorldsDir) || _install is null
                ? GameInstall.UserGeneratedWorldsDir
                : _install.WorldsDir,
        };
        if (dialog.ShowDialog(this) != true) return;
        await OpenWorldAsync(dialog.FolderName);
    }

    private async Task OpenWorldAsync(string folder)
    {
        _vm.IsBusy = true;
        _vm.Status = "Loading " + Path.GetFileName(folder) + "...";
        try
        {
            var sw = Stopwatch.StartNew();
            var world = await Task.Run(() => WorldReader.Read(folder));
            await _gameDataLoad;
            SetWorld(world, folder, dirty: false);
            BeginHistory($"Open world: {world.Name}");
            _vm.Status = $"Loaded {world.Name} in {sw.Elapsed.TotalSeconds:0.0} s";
            _settings.LastWorldFolder = folder;
            _settings.AddRecent(folder);
            _settings.Save();
        }
        catch (Exception ex) when (IsBadInput(ex))
        {
            _vm.Status = "Failed to load world";
            ThemedMessageBox.Show(this, ex.Message, "Open World", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _vm.IsBusy = false;
        }
    }

    private void SetWorld(World world, string? sourcePath, bool dirty, string? recoveryPath = null)
    {
        // Prefabs saved at ground level by the game's editor get box Y, which everything here works in.
        GroundLevel.Resolve(world, _vm.Prefabs);
        if (_world is not null || recoveryPath is not null) _autosaves.Discard(recoveryPath);
        _world = world;
        ResetPlayerTracking();
        _sourcePath = sourcePath;
        _savePath = sourcePath;
        _projectPath = null;
        _projectSettings = new ProjectStore.ProjectSettings();
        _vm.Issues.Clear();
        ResetIssueSteps();
        _fixedIssues.Clear();
        _vm.HasFixedIssues = false;
        _vm.IssuesText = "Press Validate (F7) to check the world.";
        _undo.Clear();
        _exportPosition = _undo.Position;
        _projectSettings.ExportHasChanges = sourcePath is null ? null : false;
        _tools.CancelRoad();
        _tools.World = world;
        _prefabTools.World = world;
        _prefabTools.Refresh();
        _multiSelect.SetWorld(world);
        _vm.PlacedWorld = world;
        _townTools.World = world;
        _genTools.World = world;
        _bridgeTools.World = world;
        _vm.SelectedTool = EditorTool.Select;
        _townTools.Refresh();
        LoadMixer();
        LoadGeneratorSettings();
        _dirty = dirty;
        ShowWorld(world, keepView: false);
    }

    private void ShowWorld(World world, bool keepView)
    {
        var renderer = new MapRenderer(world, _vm.Biomes);
        Map.Prefabs = _vm.Prefabs;
        Map.Renderer = renderer;
        _multiSelect.Refresh();
        Overview.SetRenderer(renderer, _vm.Layers);
        _vm.WorldSummary = WorldSummaryFormatter.Format(world);
        _vm.WorldSizeText = $"{world.Size} × {world.Size} m";
        StartPanel.Visibility = Visibility.Collapsed;
        UpdateTitle();
        UpdateZoomText();
        UpdateToolState();
        Map.Focus();
    }

    // ---- save --------------------------------------------------------------

    private void HasWorld_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = _world is not null && !_vm.IsBusy && !_vm.IsLandMode && !_tools.IsStroking && !_prefabTools.IsDragging && !_multiSelect.IsDragging && !_townTools.IsDragging;

    /// <summary>
    /// True (and says so) while a save, export or load is running. Saves write the live world on another thread,
    /// so nothing may edit it until they finish.
    /// </summary>
    private bool RefuseWhileBusy()
    {
        if (!_vm.IsBusy) return false;
        _vm.Status = "Wait for the current save or load to finish.";
        return true;
    }

    private async void Save_Executed(object sender, ExecutedRoutedEventArgs e) => await SaveAsync(saveAs: false);

    private async void SaveAs_Executed(object sender, ExecutedRoutedEventArgs e) => await SaveAsync(saveAs: true);

    private async void Export_Executed(object sender, ExecutedRoutedEventArgs e) => await ExportAsync();

    /// <summary>
    /// Ctrl+F: puts the cursor in the search box of the Placed tab when that tab is open, otherwise of the Prefabs tab.
    /// Shows the side panel if it's hidden. The tab's content may not exist until it's shown, so the focus waits for layout.
    /// </summary>
    private void Find_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_vm.IsLandMode) return;
        bool placed = _vm.ShowSidePanel && _vm.SidePanelTab == MainViewModel.PlacedTab;
        _vm.ShowSidePanel = true;
        _vm.SidePanelTab = placed ? MainViewModel.PlacedTab : MainViewModel.PrefabsTab;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            var box = placed ? PlacedSearchBox : PrefabSearchBox;
            box.Focus();
            Keyboard.Focus(box);
            box.SelectAll();
        });
    }

    /// <summary>Package maps: zip worlds from GeneratedWorlds and their custom prefabs as a mod. Needs no open world.</summary>
    private void Package_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_install is null)
        {
            ThemedMessageBox.Show(this, "Set the game folder first (Settings > Game Folder), so the game's own prefabs can be told from custom ones.",
                "Package maps", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new PackageDialog(_install, _vm.Prefabs) { Owner = this };
        dialog.Packaged += (_, file) => RecordHistoryEvent($"Package: {file}");
        dialog.ShowDialog();
    }

    /// <summary>
    /// Ctrl+S: a project saves to its .7dmap, a world opened from a folder saves back to that folder, and anything
    /// else asks where to save a project. Returns false if the user cancelled or the save failed.
    /// </summary>
    private async Task<bool> SaveAsync(bool saveAs)
    {
        if (_world is null) return false;
        if (!saveAs && _projectPath is null && _sourcePath is not null && _savePath is not null)
        {
            string folder = _savePath;
            var (ok, _) = await WriteWorldAsync(folder);
            if (ok) RecordHistoryEvent($"Save: {Path.GetFileName(folder)}");
            return ok;
        }

        string? path = saveAs ? null : _projectPath;
        while (path is null)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Save project",
                Filter = "Map editor project (*.7dmap)|*.7dmap",
                FileName = _world.Name + ProjectStore.Extension,
                InitialDirectory = Path.GetDirectoryName(_projectPath ?? "") is { Length: > 0 } dir
                    && Directory.Exists(dir) && !IsGameWorldsPath(dir) && !IsRecoveryPath(dir)
                    ? dir
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            };
            if (dialog.ShowDialog(this) != true) return false;
            if (IsGameWorldsPath(dialog.FileName))
            {
                ThemedMessageBox.Show(this, "Projects can't be saved in the game's world folders. Choose another folder, such as Documents.",
                    "Save project", MessageBoxButton.OK, MessageBoxImage.Warning);
                continue;
            }
            if (IsRecoveryPath(dialog.FileName))
            {
                ThemedMessageBox.Show(this, "Choose a folder outside the editor's recovery folder, such as Documents.",
                    "Save project", MessageBoxButton.OK, MessageBoxImage.Warning);
                continue;
            }
            path = dialog.FileName;
        }
        return await SaveProjectAsync(path, saveAs);
    }

    /// <summary>True if the path is inside GeneratedWorlds or the game's own Worlds folder, where projects must not go.</summary>
    private bool IsGameWorldsPath(string path)
    {
        string full = Path.GetFullPath(path);
        return new[] { GameInstall.UserGeneratedWorldsDir, _install?.WorldsDir }.Any(root => root is not null
            && full.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True if the path is the recovery folder or is inside it.</summary>
    private static bool IsRecoveryPath(string path)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return full.Equals(AutosaveFolder, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(AutosaveFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> SaveProjectAsync(string path, bool saveAs)
    {
        if (_world is not { } world) return false;
        _vm.IsBusy = true;
        _vm.Status = "Saving " + Path.GetFileName(path) + "...";
        string oldName = world.Name;
        try
        {
            var sw = Stopwatch.StartNew();
            world.Name = Path.GetFileNameWithoutExtension(path);
            // A copy: the Generate panel can change the settings while the save runs.
            var settings = CopyProjectSettings();
            var entry = HistoryEntry.Event($"{(saveAs ? "Save As" : "Save")}: {Path.GetFileName(path)}");
            settings.History.Add(entry);
            long revision = _revision;
            var position = _undo.Position;
            await Task.Run(() => ProjectStore.Save(world, path, settings));
            RecordHistoryEvent(entry);
            _projectPath = path;
            _dirty = _revision != revision;
            _undo.MarkSaved(position);
            _autosaves.Discard();
            _settings.LastWorldFolder = path;
            _settings.AddRecent(path);
            _settings.Save();
            UpdateTitle();
            _vm.Status = $"Saved {Path.GetFileName(path)} in {sw.Elapsed.TotalSeconds:0.0} s. Use Export (Ctrl+E) to make a world the game can load.";
            return true;
        }
        catch (Exception ex) when (IsWriteError(ex))
        {
            world.Name = oldName;
            _vm.Status = "Save failed";
            ThemedMessageBox.Show(this, ex.Message, "Save", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        finally
        {
            _vm.IsBusy = false;
        }
    }

    /// <summary>Validates, then asks where to export the world folder.</summary>
    private async Task<bool> ExportAsync()
    {
        if (_world is not { } world) return false;
        var issues = await ValidateAsync(showPanel: false);
        string location = Path.GetDirectoryName(_savePath ?? _sourcePath ?? "") is { Length: > 0 } dir ? dir : GameInstall.UserGeneratedWorldsDir;
        string name = _savePath is { } saved ? Path.GetFileName(saved) : world.Name;
        var dialog = new ExportDialog(name, location, issues, BoxFormNote(world)) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Folder is null)
        {
            // An issue double-clicked in the dialog: show it in the Issues panel (the export filled it) and on the map (#27).
            if (dialog.GoTo is { } issue)
            {
                _vm.SidePanelTab = MainViewModel.IssuesTab;
                _vm.ShowSidePanel = true;
                IssuesList.SelectedItem = issue;
                IssuesList.ScrollIntoView(issue);
                // After the panel has opened, so the zoom fits the map's new size.
                _ = Dispatcher.BeginInvoke(JumpToIssue, System.Windows.Threading.DispatcherPriority.Loaded);
            }
            return false;
        }
        bool exportStatusChanged = ExportHasChanges != false;
        var (ok, savedChanged) = await WriteWorldAsync(dialog.Folder);
        if (ok) RecordHistoryEvent($"Export: {Path.GetFileName(dialog.Folder)}");
        if (ok && _projectPath is not null)
        {
            // Remember the export in the project, so Export offers the same place next time. Only a new folder, or an
            // export that changed the world's saved data or export status, makes the project changed.
            string? recorded = _projectSettings.ExportFolder;
            bool newFolder = recorded is not { Length: > 0 }
                || !string.Equals(Path.GetFullPath(dialog.Folder), Path.GetFullPath(recorded), StringComparison.OrdinalIgnoreCase);
            _projectSettings.ExportFolder = dialog.Folder;
            if (newFolder || savedChanged || exportStatusChanged)
            {
                _dirty = true;
                _revision++;
            }
            UpdateTitle();
        }
        return ok;
    }

    /// <summary>
    /// Why some prefabs will keep RWG's heights in an export instead of being written at ground level, or null if
    /// none will.
    /// </summary>
    private string? BoxFormNote(World world)
    {
        var kept = GroundLevel.KeptInBoxForm(world.Prefabs, _vm.Prefabs);
        if (kept.Count == 0) return null;
        var names = kept.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        string list = string.Join(", ", names.Take(3)) + (names.Count > 3 ? $" and {names.Count - 3} more" : "");
        string why = _vm.Prefabs.Count == 0 ? "no game folder is set" : "the game folder's prefabs don't include them";
        return $"{(kept.Count == 1 ? "1 prefab is" : $"{kept.Count:N0} prefabs are")} written with RWG's heights instead of at "
               + $"ground level, because {why} ({list}). They still load at the right height.";
    }

    /// <summary>
    /// Writes the world as a game world folder. <c>SavedChanged</c> is true when the write also changed data the
    /// project saves: a new level GUID in main.ttw, a main.ttw the world did not have, or an added spawn point.
    /// </summary>
    private async Task<(bool Ok, bool SavedChanged)> WriteWorldAsync(string folder)
    {
        if (_world is not { } world) return (false, false);
        string projectName = world.Name;
        SpawnPoint? addedSpawn = null;
        _vm.IsBusy = true;
        _vm.Status = "Writing " + folder + "...";
        try
        {
            bool newGuid = !string.Equals(Path.GetFullPath(folder), _sourcePath is null ? null : Path.GetFullPath(_sourcePath), StringComparison.OrdinalIgnoreCase)
                           && folder != _savePath;
            byte[]? template = null;
            if (!world.ExtraFiles.ContainsKey(WorldFiles.Level))
            {
                var found = LevelFile.FindTemplate(_install);
                if (found is null)
                {
                    _vm.Status = "Export cancelled";
                    ThemedMessageBox.Show(this, "The world needs a main.ttw and none could be found to copy. Generate any world in the game once, then export again.",
                        "Export World", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return (false, false);
                }
                template = File.ReadAllBytes(found.Path);
            }

            addedSpawn = SpawnHelper.EnsureOne(world);
            if (world.Spawns.Count == 0)
            {
                _vm.Status = "Export cancelled";
                ThemedMessageBox.Show(this, WorldValidator.NoDrySpawnMessage, "Export World", MessageBoxButton.OK, MessageBoxImage.Warning);
                return (false, false);
            }
            if (addedSpawn is not null) Map.InvalidateVisual();

            var sw = Stopwatch.StartNew();
            world.Name = Path.GetFileName(folder);
            var options = new WorldWriteOptions { Overwrite = true, LevelTemplate = template, NewLevelGuid = newGuid, Prefabs = _vm.Prefabs };
            int keptBoxForm = GroundLevel.KeptInBoxForm(world.Prefabs, _vm.Prefabs).Count;
            // A copy: the writer keeps a new level GUID in the world, and the old bytes say whether it changed.
            byte[]? levelBefore = world.ExtraFiles.TryGetValue(WorldFiles.Level, out var level) ? (byte[])level.Clone() : null;
            long revision = _revision;
            var position = _undo.Position;
            await Task.Run(() => WorldWriter.Write(world, folder, options));
            // Both a new level GUID kept in the world and an added spawn point are part of what the project saves.
            bool savedChanged = addedSpawn is not null
                || (world.ExtraFiles.TryGetValue(WorldFiles.Level, out var levelAfter)
                    && (levelBefore is null || !levelAfter.AsSpan().SequenceEqual(levelBefore)));
            if (_projectPath is not null) world.Name = projectName;
            if (!string.Equals(GameLinkWorld.ExportName(GameLinkExportFolder), GameLinkWorld.ExportName(folder), StringComparison.OrdinalIgnoreCase))
                ClearPlayerMarker();
            _savePath = folder;
            _exportPosition = position;
            _projectSettings.ExportHasChanges = false;
            if (_projectPath is null)
            {
                // A world edited as a folder is now saved there.
                _sourcePath ??= folder;
                _dirty = _revision != revision;
                _undo.MarkSaved(position);
                _autosaves.Discard();
            }
            _settings.LastWorldFolder = folder;
            _settings.Save();
            _vm.WorldSummary = WorldSummaryFormatter.Format(world);
            UpdateTitle();
            _vm.Status = $"Wrote {Path.GetFileName(folder)} in {sw.Elapsed.TotalSeconds:0.0} s. The game rebuilds its caches on first load."
                + (addedSpawn is null ? "" : $" Added a spawn point at {addedSpawn.X:0}, {addedSpawn.Z:0} because the world had none.")
                + (keptBoxForm == 0 ? "" : $" {keptBoxForm:N0} prefab{(keptBoxForm == 1 ? "" : "s")} not in the game folder kept RWG's heights.");
            WarnAboutExistingGames(Path.GetFileName(folder));
            return (true, savedChanged);
        }
        catch (Exception ex) when (IsWriteError(ex))
        {
            // Leave the world as it was before the export was tried.
            world.Name = projectName;
            if (addedSpawn is not null)
            {
                world.Spawns.Remove(addedSpawn);
                Map.InvalidateVisual();
            }
            _vm.Status = "Export failed";
            ThemedMessageBox.Show(this, ex.Message, "Export World", MessageBoxButton.OK, MessageBoxImage.Error);
            return (false, false);
        }
        finally
        {
            _vm.IsBusy = false;
        }
    }

    // ---- validation --------------------------------------------------------

    private async void Validate_Executed(object sender, ExecutedRoutedEventArgs e) => await ValidateAsync(showPanel: true);

    /// <summary>
    /// Issues fixed with a Fix button while this world is open, for the teleport list (#59). A fix that is undone drops out
    /// there, because Validate offers it again.
    /// </summary>
    private readonly List<FixedIssue> _fixedIssues = [];

    /// <summary>An issue's Fix button (#40): applies the fix as one undo step, then validates again so the list is current.</summary>
    private async void IssueFix_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ValidationIssue { Fix: { } fix } issue } || _world is not { } world
            || RefuseWhileBusy()) return;
        string? done = null;
        var reached = GridRect.Empty;
        if (!_edits.Run(world, fix.Label, session =>
            {
                done = IssueFixer.Apply(world, _vm.Prefabs, fix, session);
                reached = session.Bounds;
            }))
            return;
        if (done is not null)
        {
            // Fixed again after an undo: the newest record replaces the old one.
            _fixedIssues.RemoveAll(f => f.SameFixAs(fix));
            _fixedIssues.Add(FixedIssue.Of(world, issue, done));
            _vm.HasFixedIssues = true;
        }
        Map.InvalidateRegion(reached);
        Map.InvalidateVisual();
        await ValidateAsync(showPanel: false);
        _vm.Status = done ?? "Nothing to fix there any more: it may have changed since Validate ran.";
    }

    /// <summary>
    /// Saves a Markdown teleport list of Validate's issues and the issues fixed since the world was opened (#59). It
    /// validates again first, so the list matches the world as it is.
    /// </summary>
    private async void ExportTeleportList_Click(object sender, RoutedEventArgs e)
    {
        if (_world is not { } world || RefuseWhileBusy()) return;
        var issues = await ValidateAsync(showPanel: false);
        var spots = TeleportList.Build(world, issues, _vm.Prefabs, _fixedIssues);
        if (spots.Count == 0)
        {
            _vm.Status = "No issues, so there is no teleport list to save.";
            return;
        }
        var save = new SaveFileDialog
        {
            Title = "Save teleport list",
            Filter = "Markdown (*.md)|*.md|Text (*.txt)|*.txt",
            FileName = world.Name + "_teleports",
            DefaultExt = ".md",
        };
        if (save.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(save.FileName, TeleportList.Markdown(world, spots));
            int count = spots.Count(s => s.Command is not null);
            _vm.Status = $"Saved {count} teleport{(count == 1 ? "" : "s")} to {Path.GetFileName(save.FileName)}";
        }
        catch (Exception ex) when (IsWriteError(ex))
        {
            _vm.Status = "Teleport list not saved";
            ThemedMessageBox.Show(this, ex.Message, "Save Teleport List", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task<IReadOnlyList<ValidationIssue>> ValidateAsync(bool showPanel)
    {
        if (_world is not { } world) return [];
        await _gameDataLoad;
        var library = _vm.Prefabs;
        bool template = world.ExtraFiles.ContainsKey(WorldFiles.Level) || LevelFile.FindTemplate(_install) is not null;
        _vm.IsBusy = true;
        _vm.Status = "Checking the world...";
        List<ValidationIssue> issues;
        IReadOnlyList<TeleportSpot> issueStops;
        try
        {
            // Edits can carry on while the checks run, so they get their own copy of the lists (the layers are only read).
            var view = new World
            {
                Name = world.Name, Size = world.Size, Height = world.Height, Biomes = world.Biomes, Roads = world.Roads,
                Water = world.Water, Radiation = world.Radiation,
            };
            view.Prefabs.AddRange(world.Prefabs.Select(p => p.Clone()));
            view.Spawns.AddRange(world.Spawns.Select(sp => sp.Clone()));
            foreach (var (k, v) in world.ExtraFiles) view.ExtraFiles[k] = v;
            foreach (var (k, v) in world.PassthroughImages) view.PassthroughImages[k] = v;
            // Each copy, with the prefab or spawn it came from, so a fix (#40) acts on the world, not on the copy.
            var prefabOf = new Dictionary<PrefabInstance, PrefabInstance>(ReferenceEqualityComparer.Instance);
            for (int i = 0; i < view.Prefabs.Count; i++) prefabOf[view.Prefabs[i]] = world.Prefabs[i];
            var spawnOf = new Dictionary<SpawnPoint, SpawnPoint>(ReferenceEqualityComparer.Instance);
            for (int i = 0; i < view.Spawns.Count; i++) spawnOf[view.Spawns[i]] = world.Spawns[i];
            var biomes = _vm.Biomes;
            (issues, issueStops) = await Task.Run(() =>
            {
                var validated = WorldValidator.Validate(view, library, template, biomes).Select(i => i.Fix is not { } fix ? i : i with
                {
                    Fix = fix with
                    {
                        Prefab = fix.Prefab is { } p ? prefabOf.GetValueOrDefault(p) : null,
                        Spawn = fix.Spawn is { } s ? spawnOf.GetValueOrDefault(s) : null,
                    },
                }).ToList();
                return (validated, TeleportList.Build(view, validated, library));
            });
        }
        finally
        {
            _vm.IsBusy = false;
        }
        var selectedIssue = IssuesList.SelectedItem as ValidationIssue;
        _vm.Issues.Clear();
        foreach (var issue in issues) _vm.Issues.Add(issue);
        RebuildIssueSteps(issueStops);
        if (selectedIssue is not null) SelectIssueRow(selectedIssue);
        int errors = issues.Count(i => i.Severity == IssueSeverity.Error);
        int warnings = issues.Count(i => i.Severity == IssueSeverity.Warning);
        static string Count(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";
        _vm.IssuesText = issues.Count == 0 ? "No problems found." : $"{Count(errors, "error")}, {Count(warnings, "warning")}.";
        _vm.Status = "Validated: " + _vm.IssuesText;
        if (showPanel)
        {
            _vm.SidePanelTab = MainViewModel.IssuesTab;
            _vm.ShowSidePanel = true;
        }
        return issues;
    }

    private void Placed_DoubleClick(object sender, MouseButtonEventArgs e) => JumpToPlaced();

    private void Placed_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        JumpToPlaced();
        e.Handled = true;
    }

    /// <summary>A click on a row zooms the map to that prefab; it changes neither the tool nor the map selection.</summary>
    private void Placed_Click(object sender, MouseButtonEventArgs e)
    {
        // Only a click on a row: not on the scroll bar or the empty space under the last row.
        if (ItemsControl.ContainerFromElement(PlacedList, (DependencyObject)e.OriginalSource) is not ListBoxItem { DataContext: PlacedPrefab row }) return;
        if (_world is { } world) ZoomToPlaced(world, row);
    }

    /// <summary>Centres the map on the prefab picked in the Placed list and selects it, with the Select tool if need be.</summary>
    private void JumpToPlaced()
    {
        if (_vm.SelectedPlaced is not { } row || _world is not { } world || RefuseWhileBusy()) return;
        if (!world.Prefabs.Contains(row.Instance))
        {
            _vm.RefreshPlaced();
            return;
        }
        if (!_vm.IsPrefabTool && !_vm.IsSelectTool) _vm.SelectedTool = EditorTool.Select;
        ZoomToPlaced(world, row);
        _prefabTools.SelectPlaced(row.Instance);
    }

    /// <summary>
    /// Zooms in until the prefab is a good size on screen (never out) and centres the map on it. The view shows about
    /// eight times the prefab's size, at least 500 m, so the area around it shows too.
    /// </summary>
    private void ZoomToPlaced(World world, PlacedPrefab row)
    {
        var (sx, sz) = row.Info?.Footprint(row.Instance.Rotation) ?? (1, 1);
        double target = Math.Min(Map.ActualWidth, Map.ActualHeight) / Math.Clamp(Math.Max(sx, sz) * 8, 500, 2500);
        if (Map.Scale < target) Map.ZoomTo(target);
        Map.CenterOn(new Point(world.WorldToGrid(row.Instance.Position.X) + sx / 2.0, world.Size - world.WorldToGrid(row.Instance.Position.Z) - sz / 2.0));
    }

    private void Issue_DoubleClick(object sender, MouseButtonEventArgs e) => JumpToIssue();

    private void Issue_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.OriginalSource is Button) return;
        JumpToIssue();
        e.Handled = true;
    }

    /// <summary>Centres the map on the selected issue and zooms in to about a street tile across.</summary>
    private void JumpToIssue()
    {
        if (IssuesList.SelectedItem is not ValidationIssue { HasLocation: true } issue || _world is not { } world) return;
        double target = Math.Min(Map.ActualWidth, Map.ActualHeight) / 300;
        if (Map.Scale < target) Map.ZoomTo(target);
        Map.CenterOn(new Point(issue.X!.Value + 0.5, world.Size - issue.Z!.Value - 0.5));
    }

    // ---- autosave ----------------------------------------------------------

    /// <summary>
    /// Every few minutes, writes unsaved work to an autosave project. A copy-on-write snapshot is taken on the UI
    /// thread, so editing carries on while it is written without copying the whole world first. The autosave is
    /// discarded after a save, a world change or a normal exit.
    /// </summary>
    private async Task AutosaveAsync()
    {
        if (!_vm.Autosave || _autosaving || !Dirty || _world is not { } world || _vm.IsBusy || _newLandWorld
            || _tools.IsStroking || _prefabTools.IsDragging || _multiSelect.IsDragging || _townTools.IsDragging)
            return;
        _autosaving = true;
        try
        {
            using var snapshot = WorldSnapshot.Take(world);
            var settings = CopyProjectSettings();
            if (await _autosaves.SaveAsync(snapshot, settings))
                _vm.Status = $"Autosaved at {DateTime.Now:t}.";
        }
        catch (Exception ex)
        {
            // Autosave runs on a timer; whatever goes wrong, it must not take the editor and the work with it.
            App.LogError(ex);
            _vm.Status = "Autosave failed: " + ex.Message;
        }
        finally
        {
            _autosaving = false;
            CloseIfWaiting();
        }
    }

    /// <summary>An autosave left at start-up means the editor did not close normally; offer to bring the work back.</summary>
    private async Task OfferRecoveryAsync()
    {
        if (_autosaves.FindRecovery() is not { } recovery) return;
        // Let the window draw first, so the question doesn't sit on a blank window.
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ContextIdle);
        var when = recovery.LastWriteTime;
        var answer = ThemedMessageBox.Show(this,
            $"The editor did not close normally last time. Recover the work autosaved at {when:g}?",
            "Recover unsaved work", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes) await OpenProjectAsync(recovery.FullName, recovered: true);
        else _autosaves.Discard();
    }

    private readonly HashSet<string> _warnedAboutGames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Saved games keep the chunks they have already generated, so edits only show up in places a game has
    /// not visited yet. Says so once per world per session.
    /// </summary>
    private void WarnAboutExistingGames(string worldName)
    {
        var games = GameInstall.SavedGamesOn(worldName);
        if (games.Count == 0 || !_warnedAboutGames.Add(worldName)) return;
        string list = string.Join("\n", games.Take(8).Select(g => "  • " + g)) + (games.Count > 8 ? $"\n  … and {games.Count - 8} more" : "");
        ThemedMessageBox.Show(this,
            $"The game already has saved games on {worldName}:\n\n{list}\n\n" +
            "Those games keep the terrain, plants and water they have already generated, so your changes only appear " +
            "where they have not been yet. Start a new game on this world to see the changes everywhere.\n\n" +
            $"Saved games are in {Path.Combine(GameInstall.UserSavesDir, worldName)}.",
            "Existing games on this world", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>Asks about unsaved changes. Returns false if the user wants to stay.</summary>
    private async Task<bool> ConfirmDiscardAsync()
    {
        if (_world is null || !Dirty) return true;
        var answer = ThemedMessageBox.Show(this, $"Save changes to {_world.Name}?", App.ProductName, MessageBoxButton.YesNoCancel, MessageBoxImage.Question,
            ["Save", "Don't save", "Cancel"]);
        return answer switch
        {
            MessageBoxResult.Yes => await SaveAsync(saveAs: false),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    private async void CloseWorld_Executed(object sender, ExecutedRoutedEventArgs e) => await CloseWorldAsync();

    /// <summary>File > Close: asks about unsaved changes, then goes back to the start screen with no world open.</summary>
    private async Task CloseWorldAsync()
    {
        if (_world is null || _vm.IsBusy || !await ConfirmDiscardAsync()) return;
        ClearWorld();
    }

    private void ClearWorld()
    {
        _autosaves.Discard();
        _tools.CancelRoad();
        _world = null;
        ResetPlayerTracking();
        _sourcePath = _savePath = _projectPath = null;
        _projectSettings = new ProjectStore.ProjectSettings();
        _exportPosition = null;
        _dirty = false;
        _undo.Clear();
        _vm.History = null;
        _tools.World = null;
        _prefabTools.World = null;
        _prefabTools.Refresh();
        _multiSelect.SetWorld(null);
        _vm.PlacedWorld = null;
        _townTools.World = null;
        _townTools.Refresh();
        _genTools.World = null;
        _bridgeTools.World = null;
        _vm.Generate.HasWorld = false;
        _genTools.Refresh();
        _vm.Issues.Clear();
        ResetIssueSteps();
        _fixedIssues.Clear();
        _vm.HasFixedIssues = false;
        _vm.IssuesText = "Press Validate (F7) to check the world.";
        _vm.WorldSummary = "";
        _vm.WorldSizeText = "";
        _vm.CursorText = "";
        Map.Renderer = null;
        Overview.SetRenderer(null, _vm.Layers);
        StartPanel.Visibility = Visibility.Visible;
        UpdateTitle();
        UpdateToolState();
        CommandManager.InvalidateRequerySuggested();
        _vm.Status = "Closed. Open a world with File > Open (Ctrl+O), or start one with File > New World (Ctrl+N).";
    }

    private bool _closeConfirmed;

    /// <summary>Set by Settings > Theme: the editor starts again once this window has closed.</summary>
    private bool _restartWhenClosed;

    /// <summary>The theme ticked under Settings > Theme. It starts when the editor restarts.</summary>
    private string _chosenTheme = AppSettings.FlatTheme;

    /// <summary>A click toggles a menu item's tick, so the ticks are set again after every click.</summary>
    private void TickChosenTheme()
    {
        ThemeFlatItem.IsChecked = _chosenTheme == AppSettings.FlatTheme;
        ThemeModernItem.IsChecked = _chosenTheme == AppSettings.ModernTheme;
    }

    /// <summary>Saves the chosen theme and offers to restart, because a theme only starts with the editor.</summary>
    private async void Theme_Click(object sender, RoutedEventArgs e)
    {
        string theme = (string)((MenuItem)sender).Tag;
        bool changed = theme != _chosenTheme;
        _chosenTheme = theme;
        TickChosenTheme();
        if (!changed) return;
        _settings.Theme = theme;
        _settings.Save();

        // Back to the theme that is running: nothing to restart for.
        if (theme == AppSettings.ModernTheme == _vm.IsModernTheme) return;
        if (ThemedMessageBox.Show(this, $"The {theme} theme starts when the editor restarts. Restart now?", "Theme",
                MessageBoxButton.YesNo, MessageBoxImage.Question, ["Restart now", "Later"]) != MessageBoxResult.Yes)
            return;
        if (_vm.IsBusy || _autosaving)
        {
            _vm.Status = $"The {theme} theme starts the next time you open the editor.";
            return;
        }
        if (!await ConfirmDiscardAsync()) return;
        _closeConfirmed = true;
        _restartWhenClosed = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        StopGameLinkPolling();
        _vm.ShowPlayer = false;
        StopIssueHotkeys();
        base.OnClosed(e);
        if (!_restartWhenClosed || Environment.ProcessPath is not { } editor) return;
        var start = new ProcessStartInfo(editor) { UseShellExecute = false };
        // A test override must not hide the theme that was just chosen.
        start.Environment.Remove("GNOSTIC_THEME");
        if ((_projectPath ?? _sourcePath) is { } open) start.ArgumentList.Add(open);
        Process.Start(start);
    }

    /// <summary>Closes the window if it was asked to close while a save was running and nothing is running now.</summary>
    private void CloseIfWaiting()
    {
        if (_closeWhenIdle && !_vm.IsBusy && !_autosaving)
        {
            _closeWhenIdle = false;
            _ = Dispatcher.BeginInvoke(Close);
        }
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        // Closing would cut a running save off half-written; wait for it, then close.
        if (_vm.IsBusy || _autosaving)
        {
            e.Cancel = true;
            _closeWhenIdle = true;
            if (_generateCancel is { } generating)
            {
                generating.Cancel();
                _vm.Status = "Closing once the generator stops...";
            }
            else _vm.Status = "Closing once the save finishes...";
            return;
        }
        if (_vm.IsLandMode && !ConfirmCloseLand())
        {
            e.Cancel = true;
            return;
        }
        if (_closeConfirmed || !Dirty)
        {
            _autosaves.Discard();
            _settings.Autosave = _vm.Autosave;
            _settings.RibbonMode = _vm.RibbonMode;
            _settings.ShowMiniMap = _vm.ShowMiniMap;
            _settings.SidePanelWidth = _vm.SidePanelWidth;
            _settings.ShowSidePanel = _vm.ShowSidePanel;
            _settings.SideBarOnRight = _vm.SideBarOnRight;
            _settings.WindowMaximized = WindowState == WindowState.Maximized;
            _settings.WindowWidth = RestoreBounds.Width;
            _settings.WindowHeight = RestoreBounds.Height;
            _settings.Save();
            return;
        }
        e.Cancel = true;
        if (await ConfirmDiscardAsync())
        {
            _closeConfirmed = true;
            // "Don't save" completes synchronously, still inside this Closing event, where Close() throws;
            // close once the event has returned instead.
            _ = Dispatcher.BeginInvoke(Close);
        }
    }

    // ---- generator ---------------------------------------------------------

    /// <summary>
    /// Shows the open world's generator settings in the Generate panel, making them first for a world that has none
    /// (an opened RWG world, or a project from before the generator).
    /// </summary>
    private void LoadGeneratorSettings()
    {
        if (_world is not { } world) return;
        var settings = _projectSettings.Generator ??= GeneratorSettings.ForNewWorld(Random.Shared.Next(1, 1_000_000),
            [EdgeStyle.Land, EdgeStyle.Land, EdgeStyle.Land, EdgeStyle.Land], 35);
        if (settings.Towns.Rows.Count == 0) settings.Towns.Rows = TownSiteStage.DefaultRows(_vm.Mixer);
        _townCells = null;
        var g = _vm.Generate;
        g.Load(settings, world.Size);
        g.HasWorld = true;
        g.TerrainStatus = settings.Done.Contains(GeneratorStage.Terrain)
            ? $"Generated before, with seed {settings.Terrain.Seed}."
            : "Not run yet. Generate replaces the whole map's ground, or an area's.";
        TownSiteStage.MarkBuilt(world, _vm.Prefabs, settings.Sites);
        int built = settings.Sites.Count(s => s.Built);
        g.TownStatus = settings.Sites.Count == 0 ? "Not run yet. Plan towns picks the sites; nothing is built until Build."
            : built > 0 ? $"{built} town{(built == 1 ? "" : "s")} built." : "Towns planned, not built yet.";
        g.RefreshFeatureRows(_vm.Mixer);
        int features = settings.PlacedFeatures.Count(f => f.Drawn);
        g.FeatureStatus = features > 0 ? $"{features} feature stamp{(features == 1 ? "" : "s")} in the world."
            : "Not run yet. Generate presses lakes, rivers, canyons and craters into the finished ground.";
        g.HighwayStatus = settings.PlacedRoads.Count > 0
            ? $"{settings.PlacedRoads.Count} road{(settings.PlacedRoads.Count == 1 ? "" : "s")} laid, {settings.PlacedRoads.Sum(r => r.Length) / 1000:0} km."
            : "Not run yet. Generate joins the built towns with highways.";
        g.RefreshHighways();
        g.Mixer = _vm.Mixer;
        int wild = settings.PlacedWilderness.Count(p => p.Drawn);
        g.WildernessStatus = wild > 0
            ? $"{wild} POI{(wild == 1 ? "" : "s")} placed, {settings.PlacedWilderness.Count(p => p.Road is not null)} with gravel roads."
            : "Not run yet. Generate places wilderness POIs away from the towns, each with a gravel road to the nearest road.";
        g.RefreshWilderness();
        int spawns = settings.PlacedSpawns.Count;
        g.SpawnStatus = spawns > 0
            ? $"{spawns} spawn{(spawns == 1 ? "" : "s")} placed; {world.Spawns.Count} in the world."
            : "Not run yet. Generate places spawns by the gravel roads near traders, then tops up to the minimum.";
        g.RefreshSpawns();
        _genTools.Refresh();
        if (settings.Sites.Any(s => !s.Built)) _ = ReplanTownsAsync();
    }

    // ---- town sites ----------------------------------------------------------

    /// <summary>Which 150 m cells can take a town; worked out again after every edit.</summary>
    private TownCells? _townCells;

    private TownCells TownCellsNow(World world)
    {
        bool cross = _projectSettings.Generator?.Towns.CrossBorders ?? false;
        // Ticking "Let towns cross biome borders" changes which cells are usable.
        if (_townCells is null || _townCellsCross != cross)
        {
            _townCells = TownCells.Build(world, _vm.Biomes, _vm.Prefabs, _projectSettings.Generator?.TerrainPlan, cross);
            _townCellsCross = cross;
        }
        return _townCells;
    }

    private bool _townCellsCross;

    private bool TownsReady()
    {
        if (_vm.Mixer.Townships.Count == 0)
        {
            ThemedMessageBox.Show(this, "Town sites need the game's rwgmixer.xml and prefabs. Set the game folder under Settings > Game Folder.",
                "Town sites", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        return true;
    }

    /// <summary>Plans the towns on every unbuilt site again (after an edit, undo, or a change to one of them) and shows them.</summary>
    private async Task ReplanTownsAsync()
    {
        if (_world is not { } world || _projectSettings.Generator is not { } gen || _vm.IsBusy) return;
        var g = _vm.Generate;
        var sites = gen.Sites.Where(s => !s.Built).ToList();
        if (sites.Count == 0)
        {
            g.Planned = [];
            _genTools.Refresh();
            return;
        }
        _vm.IsBusy = true;
        g.IsRunning = true;
        try
        {
            var (library, mixer, biomes) = (_vm.Prefabs, _vm.Mixer, _vm.Biomes);
            var cells = TownCellsNow(world);
            g.Planned = await Task.Run(() => TownSiteStage.PlanTowns(world, library, mixer, biomes, cells, sites, default, gen.Towns.Traders));
        }
        catch (Exception ex)
        {
            // Often run without waiting for it, so a failure must be shown here or nobody sees it.
            App.LogError(ex);
            g.Planned = [];
            g.TownStatusShort = "Planning failed";
            _vm.Status = $"Couldn't plan the towns: {ex.Message}. The world hasn't changed.";
            _genTools.Refresh();
            g.IsRunning = false;
            _vm.IsBusy = false;
            CloseIfWaiting();
            return;
        }
        finally
        {
            g.IsRunning = false;
            _vm.IsBusy = false;
        }
        _genTools.Refresh();
        CloseIfWaiting();
    }

    private async void TownsPlan_Click(object sender, RoutedEventArgs e) => await PlanTownSitesAsync();

    private async void TownsReroll_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsBusy) return;
        await PlanTownSitesAsync(newSeed: Random.Shared.Next(1, 1_000_000));
    }

    /// <summary>
    /// Picks the sites (keeping built and pinned towns) and plans every town on them. The world doesn't change, unless
    /// unpinned towns are already built: built towns count toward each township's total, so planning would find nothing
    /// left to do. Those are taken down first, after asking (<see cref="TakeDownAndReplanAsync"/>).
    /// <paramref name="newSeed"/> is Re-roll's new town seed.
    /// </summary>
    private async Task PlanTownSitesAsync(int? newSeed = null)
    {
        if (_world is not { } world || RefuseWhileBusy() || _projectSettings.Generator is not { } gen) return;
        await _gameDataLoad;
        if (!TownsReady()) return;
        if (gen.Sites.Any(s => s.Built && !s.Pinned))
        {
            await TakeDownAndReplanAsync(world, gen, newSeed);
            return;
        }
        var g = _vm.Generate;
        if (newSeed is { } seed) g.TownSeed = seed;
        var options = gen.Towns.Clone();
        var keep = gen.Sites.Where(s => s.Built || s.Pinned).ToList();
        var (library, mixer, biomes) = (_vm.Prefabs, _vm.Mixer, _vm.Biomes);
        _vm.IsBusy = true;
        g.IsRunning = true;
        _vm.Status = "Planning the towns...";
        var sw = Stopwatch.StartNew();
        TownSiteResult result;
        List<PlannedTown> towns;
        try
        {
            var cells = TownCellsNow(world);
            (result, towns) = await Task.Run(() =>
            {
                var r = TownSiteStage.Plan(cells, mixer, options, keep);
                return (r, TownSiteStage.PlanTowns(world, library, mixer, biomes, cells, r.Sites, default, options.Traders));
            });
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            g.TownStatusShort = "Planning failed";
            g.TownStatus = $"Planning failed: {ex.Message}";
            _vm.Status = $"Couldn't plan the towns: {ex.Message}. The world and the sites list haven't changed.";
            g.IsRunning = false;
            _vm.IsBusy = false;
            CloseIfWaiting();
            return;
        }
        finally
        {
            g.IsRunning = false;
            _vm.IsBusy = false;
        }
        // The sites list is replaced; built and pinned towns came through as copies.
        gen.Sites = result.Sites;
        TownSiteStage.MarkBuilt(world, library, gen.Sites);
        g.Planned = towns;
        MarkSettingsChanged();
        RecordHistoryEvent($"Plan towns (seed {options.Seed})");
        int planned = towns.Count;
        g.TownStatus = $"Planned {planned} town{(planned == 1 ? "" : "s")} with seed {options.Seed} in {sw.Elapsed.TotalSeconds:0.0} s."
                       + (result.Shortfalls.Count > 0 ? " No room for all of: " + string.Join("; ", result.Shortfalls) + "." : "");
        g.TownStatusShort = $"Planned {planned} town{(planned == 1 ? "" : "s")}, not built yet" + (result.Shortfalls.Count > 0 ? "; some didn't fit" : "");
        _vm.Status = "Towns planned. Check them on the map, then Build. Nothing has changed in the world yet.";
        _genTools.Refresh();
        CloseIfWaiting();
    }

    /// <summary>
    /// Plan towns or Re-roll after towns were built: takes the unpinned built towns down (and the highways and stage 5,
    /// which join them) and plans new sites, as one undo step. Pinned towns stay. Undo puts the towns, the sites list,
    /// the seed and the statuses back.
    /// </summary>
    private async Task TakeDownAndReplanAsync(World world, GeneratorSettings gen, int? newSeed)
    {
        var down = gen.Sites.Where(s => s.Built && !s.Pinned).ToList();
        // The highways join the towns, and stage 5 hangs off the highways, so they come out with them.
        bool laterStages = gen.PlacedRoads.Count > 0 || gen.PlacedWilderness.Any(p => p.Drawn);
        string later = laterStages ? " Every highway, and the wilderness POIs and spawns that follow them, will be taken out too." : "";
        if (ThemedMessageBox.Show(this,
                $"{down.Count} built town{(down.Count == 1 ? "" : "s")} will be taken down so new ones can be planned (pinned towns stay).{later}\n\n"
                + $"{(newSeed is null ? "Plan the towns again?" : "Re-roll the towns?")} Undo (Ctrl+Z) puts them back.",
                "Town sites", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;

        var g = _vm.Generate;
        var (library, mixer, biomes) = (_vm.Prefabs, _vm.Mixer, _vm.Biomes);
        TownSiteResult? result = null;
        List<PlannedTown>? towns = null;
        int takenDown = 0, roadsOut = 0;
        EditSession? ran = null;
        TownSiteOptions? options = null;
        _vm.IsBusy = true;
        g.IsRunning = true;
        _vm.Status = "Taking the built towns down and planning new ones...";
        var sw = Stopwatch.StartNew();
        bool ok;
        try
        {
            ok = await _edits.RunInBackgroundAsync(world, newSeed is null ? "Plan towns again" : "Re-roll towns", session =>
            {
                ran = session;
                var bridges = TakeDownBridges(world, session);
                roadsOut = TakeDownHighways(world, gen, session);
                takenDown = TownSiteStage.TakeDown(world, library, down, null, session);
                RebuildBridges(world, bridges, library, session);
                // Plan on the ground as it is now, without the towns that came down.
                var cells = TownCells.Build(world, biomes, library, gen.TerrainPlan, options!.CrossBorders);
                var keep = gen.Sites.Where(s => s.Pinned).ToList();
                result = TownSiteStage.Plan(cells, mixer, options, keep);
                towns = TownSiteStage.PlanTowns(world, library, mixer, biomes, cells, result.Sites, default, options.Traders);
                // Replaced in place, so undo brings the old sites (and their built towns) back into the same list.
                session.TouchList(gen.Sites, s => s.Clone(), new GridRect(0, 0, world.Size, world.Size));
                gen.Sites.Clear();
                gen.Sites.AddRange(result.Sites);
            }, session =>
            {
                // The seed changes inside the edit, so undo puts the old one back with the towns.
                NoteGeneratorState(session);
                if (newSeed is { } seed) g.TownSeed = seed;
                options = gen.Towns.Clone();
                session.HistoryDetail = $"seed {options.Seed}";
            });
        }
        finally
        {
            g.IsRunning = false;
            _vm.IsBusy = false;
        }
        _townCells = null;
        if (!ok || result is null || towns is null || ran is null || options is null)
        {
            TownSiteStage.MarkBuilt(world, library, gen.Sites);
            CloseIfWaiting();
            return;
        }
        TownSiteStage.MarkBuilt(world, library, gen.Sites);
        if (!gen.Sites.Any(s => s.Built)) gen.Done.Remove(GeneratorStage.TownSites);
        g.Planned = towns;
        NoteHighwaysTakenDown(roadsOut);
        Map.InvalidateRegion(ran.Bounds);
        Map.InvalidateVisual();
        _prefabTools.Refresh();
        _townTools.Refresh();
        _genTools.Refresh();
        int planned = towns.Count;
        g.TownStatus = $"Took {takenDown} built town{(takenDown == 1 ? "" : "s")} down and planned {planned} with seed {options.Seed} in {sw.Elapsed.TotalSeconds:0.0} s."
                       + (result.Shortfalls.Count > 0 ? " No room for all of: " + string.Join("; ", result.Shortfalls) + "." : "");
        g.TownStatusShort = $"Planned {planned} town{(planned == 1 ? "" : "s")}, not built yet" + (result.Shortfalls.Count > 0 ? "; some didn't fit" : "");
        g.RefreshDone();
        _vm.Status = "Towns planned. Check them on the map, then Build. Undo (Ctrl+Z) puts the built towns back." + TakeBridgeNote();
        CloseIfWaiting();
    }

    private void TownsDefaults_Click(object sender, RoutedEventArgs e)
    {
        if (_projectSettings.Generator is not { } gen) return;
        gen.Towns.Rows = TownSiteStage.DefaultRows(_vm.Mixer);
        _vm.Generate.RefreshTownRows();
        MarkSettingsChanged();
    }

    private void LoadTownPresets()
    {
        var presets = _vm.Generate.TownPresets;
        presets.Clear();
        foreach (var preset in _settings.TownPresets) presets.Add(preset);
        TownPresetBox.SelectedIndex = presets.Count > 0 ? 0 : -1;
    }

    private void TownPresetUse_Click(object sender, RoutedEventArgs e)
    {
        if (TownPresetBox.SelectedItem is not TownPreset preset || _projectSettings.Generator is not { } gen) return;
        preset.ApplyTo(gen.Towns, _vm.Mixer);
        _vm.Generate.RefreshTownSettings();
        MarkSettingsChanged();
        _vm.Status = $"Using the {preset.Name} town settings. Click Plan towns to plan with them.";
    }

    private void TownPresetSave_Click(object sender, RoutedEventArgs e)
    {
        if (_projectSettings.Generator is not { } gen) return;
        string name = _vm.Generate.TownPresetName.Trim();
        if (name.Length == 0)
        {
            _vm.Status = "Type a name for the town preset first.";
            return;
        }
        var preset = TownPreset.From(gen.Towns, name);
        _settings.TownPresets.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        _settings.TownPresets.Add(preset);
        bool saved = _settings.Save();
        LoadTownPresets();
        TownPresetBox.SelectedItem = preset;
        _vm.Generate.TownPresetName = "";
        _vm.Status = saved ? $"Saved the town preset {name}."
            : $"Added the town preset {name}, but couldn't save the editor's settings, so it will be lost when the editor closes. Details are in crash.log.";
    }

    private void TownPresetDelete_Click(object sender, RoutedEventArgs e)
    {
        if (TownPresetBox.SelectedItem is not TownPreset preset) return;
        _settings.TownPresets.Remove(preset);
        bool saved = _settings.Save();
        LoadTownPresets();
        _vm.Status = saved ? $"Deleted the town preset {preset.Name}."
            : $"Removed the town preset {preset.Name}, but couldn't save the editor's settings. Details are in crash.log.";
    }

    private void TownsClear_Click(object sender, RoutedEventArgs e)
    {
        if (_projectSettings.Generator is not { } gen || _vm.IsBusy) return;
        gen.Sites.RemoveAll(s => !s.Built);
        _vm.Generate.Planned = [];
        _vm.Generate.TownStatus = "Plan cleared.";
        MarkSettingsChanged();
        _genTools.Refresh();
    }

    private async void TownsBuild_Click(object sender, RoutedEventArgs e) => await BuildTownsAsync();

    /// <summary>Builds every planned town as one undo step, planned afresh so it fits the world as it is now.</summary>
    private async Task BuildTownsAsync()
    {
        if (_world is not { } world || RefuseWhileBusy() || _projectSettings.Generator is not { } gen) return;
        var sites = gen.Sites.Where(s => !s.Built).ToList();
        if (sites.Count == 0) return;
        var g = _vm.Generate;
        var (library, mixer, biomes, stamps) = (_vm.Prefabs, _vm.Mixer, _vm.Biomes, _vm.Stamps);
        var cells = TownCellsNow(world);
        var traderMode = gen.Towns.Traders;
        int featuresOut = 0, roadsOut = 0;
        _vm.IsBusy = true;
        g.IsRunning = true;
        _vm.Status = "Building the towns...";
        var sw = Stopwatch.StartNew();
        List<PlannedTown>? towns = null;
        EditSession? ran = null;
        bool ok;
        try
        {
            ok = await _edits.RunInBackgroundAsync(world, "Build towns", session =>
            {
                ran = session;
                var bridges = TakeDownBridges(world, session);
                roadsOut = TakeDownHighways(world, gen, session);
                towns = TownSiteStage.PlanTowns(world, library, mixer, biomes, cells, sites, default, traderMode);
                // Features in the way come out first: the towns get flat ground, as RWG plans towns before features.
                var townCells = sites.SelectMany(s => s.AllCells).ToHashSet();
                var inTheWay = gen.PlacedFeatures.Where(f => f.Drawn && f.Cells.Any(c => townCells.Contains((c[0], c[1])))).ToList();
                featuresOut = inTheWay.Count;
                if (inTheWay.Count > 0) FeatureStage.Remove(world, gen.PlacedFeatures, inTheWay, stamps, session);
                TownSiteStage.Build(world, library, towns, session);
                RebuildBridges(world, bridges, library, session);
            }, session => NoteGeneratorState(session, $"seed {gen.Towns.Seed}"));
        }
        finally
        {
            g.IsRunning = false;
            _vm.IsBusy = false;
        }
        if (!ok || towns is null || ran is null)
        {
            TownSiteStage.MarkBuilt(world, library, gen.Sites);
            return;
        }
        gen.Done.Add(GeneratorStage.TownSites);
        g.Planned = [];
        Map.InvalidateRegion(ran.Bounds);
        Map.InvalidateVisual();
        _prefabTools.Refresh();
        _townTools.Refresh();
        _genTools.Refresh();
        int pois = towns.Sum(t => t.Plan.PoiCount), traders = towns.Sum(t => t.Plan.TraderCount);
        var byName = towns.SelectMany(t => t.Plan.Prefabs).Where(p => library.Find(p.Name) is { IsTrader: true, IsPart: false })
            .GroupBy(p => p.Name.StartsWith("trader_", StringComparison.OrdinalIgnoreCase) ? p.Name[7..] : p.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => $"{x.Key} {x.Count()}").ToList();
        g.TownStatus = $"Built {towns.Count} towns: {pois:N0} POIs and {traders} traders{(byName.Count > 0 ? $" ({string.Join(", ", byName)})" : "")}, in {sw.Elapsed.TotalSeconds:0.0} s."
                       + (featuresOut > 0 ? $" {featuresOut} feature stamp{(featuresOut == 1 ? " was" : "s were")} in the way and came out." : "");
        g.TownStatusShort = $"Built {towns.Count} towns, {pois:N0} POIs, {traders} traders";
        g.RefreshFeatures();
        NoteHighwaysTakenDown(roadsOut);
        _vm.Status = $"Built {towns.Count} towns in {sw.Elapsed.TotalSeconds:0.0} s. Undo (Ctrl+Z) takes them all down again." + TakeBridgeNote();
        _vm.SelectedTool = EditorTool.Select;
        CloseIfWaiting();
    }

    // ---- features --------------------------------------------------------------

    private bool FeaturesReady()
    {
        if (FeatureStage.Order.All(k => _vm.Stamps.WithPrefix(FeatureStage.StampPrefix(k)).Count == 0))
        {
            ThemedMessageBox.Show(this, "Features are made from the game's own lake, river, canyon and crater stamps, and none were found. Set the game folder under Settings > Game Folder.",
                "Features", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        return true;
    }

    private async void FeaturesGenerate_Click(object sender, RoutedEventArgs e) => await GenerateFeaturesAsync();

    private async void FeaturesReroll_Click(object sender, RoutedEventArgs e)
    {
        await RerollAsync(() => _vm.Generate.FeatureSeed = Random.Shared.Next(1, 1_000_000), GenerateFeaturesAsync);
    }

    /// <summary>
    /// Runs the features stage in the background as one edit: the last run's features come out (placed and pinned
    /// ones stay) and new ones are pressed in.
    /// </summary>
    private async Task GenerateFeaturesAsync()
    {
        if (_world is not { } world || RefuseWhileBusy() || _projectSettings.Generator is not { } gen) return;
        await _gameDataLoad;
        if (!FeaturesReady()) return;
        var g = _vm.Generate;
        g.RefreshFeatureRows(_vm.Mixer);
        var options = gen.Features.Clone();
        var (stamps, mixer, library) = (_vm.Stamps, _vm.Mixer, _vm.Prefabs);
        var sites = gen.Sites.ToList();
        var plan = gen.TerrainPlan;
        float baseHeight = gen.Terrain.BaseHeight;
        using var cancel = new CancellationTokenSource();
        _generateCancel = cancel;
        _vm.IsBusy = true;
        g.IsRunning = true;
        g.Progress = 0;
        _vm.Status = "Pressing in lakes, rivers, canyons and craters...";
        var progress = new Progress<double>(v => g.Progress = v);
        var sw = Stopwatch.StartNew();
        FeatureResult? result = null;
        EditSession? ran = null;
        int roadsOut = 0;
        bool ok;
        try
        {
            ok = await _edits.RunInBackgroundAsync(world, "Generate features", session =>
            {
                ran = session;
                var bridges = TakeDownBridges(world, session);
                roadsOut = TakeDownHighways(world, gen, session);
                result = FeatureStage.Run(world, options, gen.PlacedFeatures, stamps, mixer, library, sites, plan, baseHeight, session, progress, cancel.Token);
                RebuildBridges(world, bridges, library, session);
            }, session => NoteGeneratorState(session, $"seed {options.Seed}"));
        }
        finally
        {
            _generateCancel = null;
            g.IsRunning = false;
            _vm.IsBusy = false;
        }
        g.RefreshFeatures();
        if (!ok || result is null || ran is null) return;

        gen.Done.Add(GeneratorStage.Features);
        NoteHighwaysTakenDown(roadsOut);
        var counts = string.Join(", ", result.Added.Where(f => f.Chain == 0 || f.Kind == FeatureKind.River)
            .GroupBy(f => f.Chain != 0 ? -f.Chain : f.GetHashCode()).Select(c => c.First().Kind)
            .GroupBy(k => k).OrderBy(k => k.Key).Select(k => $"{k.Count()} {(k.Count() == 1 ? FeatureRowViewModel.NameOf(k.Key).ToLowerInvariant() : FeatureStage.Plural(k.Key))}"));
        g.FeatureStatus = (counts.Length == 0 ? "Nothing placed" : "Placed " + counts) + $" with seed {options.Seed} in {sw.Elapsed.TotalSeconds:0.0} s."
                          + (result.Shortfalls.Count > 0 ? " " + string.Join(" ", result.Shortfalls) : "");
        g.FeatureStatusShort = counts.Length == 0 ? "Nothing placed" : "Placed " + counts;
        Map.InvalidateRegion(ran.Bounds);
        Map.InvalidateVisual();
        _genTools.Refresh();
        _vm.Status = $"Features done in {sw.Elapsed.TotalSeconds:0.0} s. Undo (Ctrl+Z) takes them all out again." + TakeBridgeNote();
        CloseIfWaiting();
        if (gen.Sites.Any(s => !s.Built)) await ReplanTownsAsync();
    }

    private void FeaturesDefaults_Click(object sender, RoutedEventArgs e)
    {
        if (_projectSettings.Generator is not { } gen) return;
        gen.Features.Rows = FeatureStage.DefaultRows(_vm.Mixer);
        _vm.Generate.RefreshFeatureRows(_vm.Mixer);
        MarkSettingsChanged();
    }

    private void FeaturesRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_world is not { } world || RefuseWhileBusy() || _projectSettings.Generator is not { } gen || gen.PlacedFeatures.Count == 0) return;
        RemoveFeatures(world, gen, gen.PlacedFeatures.ToList(), "Remove features");
        gen.Done.Remove(GeneratorStage.Features);
        _vm.Generate.FeatureStatus = "Every feature taken out.";
    }

    private void FeaturesPlace_Click(object sender, RoutedEventArgs e)
    {
        _vm.SelectedTool = EditorTool.Features;
        Map.Focus();
    }

    private void FeatureDrop_Click(object sender, RoutedEventArgs e)
    {
        if (_world is not { } world || RefuseWhileBusy() || _projectSettings.Generator is not { } gen || _vm.Generate.SelectedFeature is not { } f) return;
        RemoveFeatures(world, gen, [f], "Drop feature");
    }

    private void RemoveFeatures(World world, GeneratorSettings gen, List<Feature> which, string name)
    {
        var stamps = _vm.Stamps;
        // What comes out, a long river whole, so the map redraws all of it.
        var chains = which.Where(f => f.Chain != 0).Select(f => f.Chain).ToHashSet();
        var reach = gen.PlacedFeatures.Where(f => which.Contains(f) || f.Chain != 0 && chains.Contains(f.Chain))
            .Aggregate(GridRect.Empty, (r, f) => r.Union(f.Reach(world.Size)));
        if (!_edits.Run(world, name, session =>
            {
                NoteGeneratorState(session);
                FeatureStage.Remove(world, gen.PlacedFeatures, which, stamps, session);
            })) return;
        _vm.Generate.RefreshFeatures();
        Map.InvalidateRegion(reach);
        Map.InvalidateVisual();
        _genTools.Refresh();
        _vm.Status = $"{name}: done. Undo (Ctrl+Z) puts it back.";
    }

    /// <summary>The Place features tool: a click on a feature picks it; anywhere else presses in a pinned one.</summary>
    private async void FeatureClicked(float x, float z)
    {
        if (_world is not { } world || _vm.IsBusy || _projectSettings.Generator is not { } gen) return;
        var g = _vm.Generate;
        (int cx, int cz) = ((int)(x / FeatureStage.Cell), (int)(z / FeatureStage.Cell));
        // The smallest feature under the click, so one inside a bigger one's box can still be picked.
        var hit = gen.PlacedFeatures.Where(f => f.Drawn && f.Cells.Any(c => c[0] == cx && c[1] == cz)).OrderBy(f => f.Cells.Count).FirstOrDefault();
        if (hit is not null)
        {
            g.SelectedFeature = hit;
            _genTools.Refresh();
            return;
        }
        await _gameDataLoad;
        if (!FeaturesReady()) return;
        g.RefreshFeatureRows(_vm.Mixer);
        var kind = g.PlaceKind;
        if (_vm.Stamps.WithPrefix(FeatureStage.StampPrefix(kind)).Count == 0)
        {
            _vm.Status = $"No {FeatureStage.StampPrefix(kind)} stamps in the game's Data/Stamps.";
            return;
        }
        List<Feature> made = [];
        var (stamps, mixer, library) = (_vm.Stamps, _vm.Mixer, _vm.Prefabs);
        if (!_edits.Run(world, "Place " + FeatureStage.StampPrefix(kind), session =>
            {
                NoteGeneratorState(session);
                made = FeatureStage.PlaceByHand(world, kind, x, z, gen.Features, gen.PlacedFeatures, stamps, mixer, library, gen.Sites,
                    gen.TerrainPlan, gen.Terrain.BaseHeight, Random.Shared.Next(1, 1_000_000), session);
            }))
            return;
        if (made.Count == 0)
        {
            _vm.Status = "No river fits there: it needs free ground running downhill.";
            return;
        }
        g.SelectedFeature = made[0];
        g.RefreshFeatures();
        Map.InvalidateRegion(made.Aggregate(GridRect.Empty, (r, f) => r.Union(f.Reach(world.Size))));
        Map.InvalidateVisual();
        _genTools.Refresh();
        _vm.Status = $"Placed a {FeatureStage.StampPrefix(kind)}{(made.Count > 1 ? $" of {made.Count} stamps" : "")}, pinned. Undo (Ctrl+Z) takes it out.";
    }

    // ---- highways --------------------------------------------------------------

    private async void HighwaysGenerate_Click(object sender, RoutedEventArgs e) => await GenerateHighwaysAsync();

    private async void HighwaysReroll_Click(object sender, RoutedEventArgs e)
    {
        await RerollAsync(() => _vm.Generate.HighwaySeed = Random.Shared.Next(1, 1_000_000), GenerateHighwaysAsync);
    }

    /// <summary>Runs the highways stage in the background as one edit: the last run's roads come out and new ones go in.</summary>
    private async Task GenerateHighwaysAsync()
    {
        if (_world is not { } world || RefuseWhileBusy() || _projectSettings.Generator is not { } gen) return;
        await _gameDataLoad;
        var g = _vm.Generate;
        var options = gen.Highways.Clone();
        var (library, mixer) = (_vm.Prefabs, _vm.Mixer);
        var sites = gen.Sites.ToList();
        using var cancel = new CancellationTokenSource();
        _generateCancel = cancel;
        _vm.IsBusy = true;
        g.IsRunning = true;
        g.Progress = 0;
        _vm.Status = "Laying the highways...";
        var progress = new Progress<double>(v => g.Progress = v);
        var sw = Stopwatch.StartNew();
        HighwayResult? result = null;
        EditSession? ran = null;
        int wildOut = 0;
        bool ok;
        try
        {
            ok = await _edits.RunInBackgroundAsync(world, "Generate highways", session =>
            {
                ran = session;
                var bridges = TakeDownBridges(world, session);
                wildOut = TakeDownWilderness(world, gen, session);
                // The Bridge tool's bridges are down while the stage runs; it must still keep off their decks and ramps.
                result = HighwayStage.Run(world, options, gen.PlacedRoads, library, mixer, sites, session, progress, cancel.Token,
                    [.. _projectSettings.Bridges.Select(b => BridgeTool.KeepClearArea(b, world.Size))]);
                RebuildBridges(world, bridges, library, session);
            }, session => NoteGeneratorState(session, $"seed {options.Seed}"));
        }
        finally
        {
            _generateCancel = null;
            g.IsRunning = false;
            _vm.IsBusy = false;
        }
        g.RefreshHighways();
        g.RefreshWilderness();
        if (!ok || result is null || ran is null) return;

        NoteWildernessTakenDown(wildOut);
        if (result.Added.Count > 0) gen.Done.Add(GeneratorStage.Highways);
        var parts = new List<string>();
        void Part(HighwayKind kind, string one, string many)
        {
            int n = result.Count(kind);
            if (n > 0) parts.Add($"{n} {(n == 1 ? one : many)}");
        }
        Part(HighwayKind.Highway, "highway", "highways");
        Part(HighwayKind.Ring, "ring road", "ring roads");
        Part(HighwayKind.Country, "country road", "country roads");
        Part(HighwayKind.JoinUp, "join-up", "join-ups");
        Part(HighwayKind.Bridge, "bridge", "bridges");
        g.HighwayStatus = (parts.Count == 0 ? "No roads laid" : $"Laid {string.Join(", ", parts)}, {result.Kilometres:0} km,")
                          + $" with seed {options.Seed} in {sw.Elapsed.TotalSeconds:0.0} s." + (result.Notes.Count > 0 ? " " + string.Join(" ", result.Notes) : "");
        g.HighwayStatusShort = parts.Count == 0 ? "No roads laid" : $"{string.Join(", ", parts)}, {result.Kilometres:0} km";
        Map.InvalidateRegion(new GridRect(0, 0, world.Size, world.Size));
        Map.InvalidateVisual();
        // Bridges are prefabs.
        _prefabTools.Refresh();
        _genTools.Refresh();
        _vm.Status = $"Highways done in {sw.Elapsed.TotalSeconds:0.0} s. Undo (Ctrl+Z) takes them all out again." + TakeBridgeNote();
        CloseIfWaiting();
    }

    private void HighwaysRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_world is not { } world || RefuseWhileBusy() || _projectSettings.Generator is not { } gen || gen.PlacedRoads.Count == 0) return;
        int wildOut = 0;
        if (!_edits.Run(world, "Remove highways", session =>
            {
                NoteGeneratorState(session);
                wildOut = TakeDownWilderness(world, gen, session);
                HighwayStage.TakeDown(world, gen.PlacedRoads, session);
            })) return;
        NoteWildernessTakenDown(wildOut);
        gen.Done.Remove(GeneratorStage.Highways);
        _vm.Generate.RefreshHighways();
        _vm.Generate.HighwayStatus = "Every road this stage laid was taken out.";
        Map.InvalidateRegion(new GridRect(0, 0, world.Size, world.Size));
        Map.InvalidateVisual();
        _prefabTools.Refresh();
        _vm.Status = "Highways taken out. Undo (Ctrl+Z) puts them back.";
    }

    /// <summary>
    /// An earlier stage is about to change the ground: the highways come out first, in the same edit, putting back the
    /// ground they graded. Returns how many came out.
    /// </summary>
    private int TakeDownHighways(World world, GeneratorSettings? gen, EditSession session)
    {
        // Stage 5 was drawn after the highways, so it comes out first.
        _wildernessTakenDown = TakeDownWilderness(world, gen, session);
        return gen is { PlacedRoads.Count: > 0 } ? HighwayStage.TakeDown(world, gen.PlacedRoads, session) : 0;
    }

    /// <summary>How many wilderness POIs the last <see cref="TakeDownHighways"/> took out, for <see cref="NoteHighwaysTakenDown"/>.</summary>
    private int _wildernessTakenDown;

    /// <summary>What the last stage run did to the Bridge tool's bridges, for its status text.</summary>
    private string _bridgeNote = "";

    /// <summary>A stage is about to change the ground: the Bridge tool's bridges come down first, in the same edit.</summary>
    private List<PlacedBridge> TakeDownBridges(World world, EditSession session) =>
        BridgeTool.TakeDownAll(world, _projectSettings.Bridges, session);

    /// <summary>The stage has changed the ground: each bridge goes up again where it was, its deck height worked out afresh.</summary>
    private void RebuildBridges(World world, List<PlacedBridge> taken, PrefabLibrary library, EditSession session)
    {
        if (taken.Count == 0) return;
        int dropped = BridgeTool.RebuildAll(world, BridgeKit.From(library), _projectSettings.Bridges, taken, session, _vm.BridgeWrecks);
        _bridgeNote = dropped > 0
            ? $" {dropped} bridge{(dropped == 1 ? " couldn't" : "s couldn't")} be built again and {(dropped == 1 ? "was" : "were")} dropped."
            : $" {taken.Count} bridge{(taken.Count == 1 ? " was" : "s were")} rebuilt on the new ground.";
    }

    private string TakeBridgeNote()
    {
        string note = _bridgeNote;
        _bridgeNote = "";
        return note;
    }

    /// <summary>After an earlier stage took the highways (and stage 5) out: says so and marks the stages as not run.</summary>
    private void NoteHighwaysTakenDown(int count)
    {
        NoteWildernessTakenDown(_wildernessTakenDown);
        _wildernessTakenDown = 0;
        if (count == 0 || _projectSettings.Generator is not { } gen) return;
        gen.Done.Remove(GeneratorStage.Highways);
        _vm.Generate.RefreshHighways();
        _vm.Generate.HighwayStatus = $"{count} road{(count == 1 ? " was" : "s were")} taken out as an earlier stage changed; Generate lays them again.";
    }

    // ---- wilderness POIs and gravel roads ----------------------------------------

    private bool WildernessReady()
    {
        if (!_vm.Prefabs.All.Any(p => p.HasTag("wilderness")))
        {
            ThemedMessageBox.Show(this, "Wilderness POIs come from the game's own prefabs, and none tagged wilderness were found. Set the game folder under Settings > Game Folder.",
                "Wilderness POIs", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        return true;
    }

    private async void WildernessGenerate_Click(object sender, RoutedEventArgs e) => await GenerateWildernessAsync();

    private async void WildernessReroll_Click(object sender, RoutedEventArgs e)
    {
        await RerollAsync(() => _vm.Generate.WildernessSeed = Random.Shared.Next(1, 1_000_000), GenerateWildernessAsync);
    }

    /// <summary>Runs the wilderness stage in the background as one edit: the last run's POIs and roads come out (pinned ones stay) and new ones go in.</summary>
    private async Task GenerateWildernessAsync()
    {
        if (_world is not { } world || RefuseWhileBusy() || _projectSettings.Generator is not { } gen) return;
        await _gameDataLoad;
        if (!WildernessReady()) return;
        var g = _vm.Generate;
        var options = gen.Wilderness.Clone();
        var (library, mixer, biomes) = (_vm.Prefabs, _vm.Mixer, _vm.Biomes);
        var sites = gen.Sites.ToList();
        using var cancel = new CancellationTokenSource();
        _generateCancel = cancel;
        _vm.IsBusy = true;
        g.IsRunning = true;
        g.Progress = 0;
        _vm.Status = "Placing wilderness POIs...";
        var progress = new Progress<double>(v => g.Progress = v);
        var sw = Stopwatch.StartNew();
        WildernessResult? result = null;
        EditSession? ran = null;
        bool ok;
        try
        {
            ok = await _edits.RunInBackgroundAsync(world, "Generate wilderness", session =>
            {
                ran = session;
                var bridges = TakeDownBridges(world, session);
                _spawnsTakenDown = TakeDownSpawns(world, gen, session);
                // The Bridge tool's bridges are down while the stage runs; its POIs and roads must still keep off their decks and ramps.
                result = WildernessStage.Run(world, options, gen.PlacedWilderness, library, mixer, biomes, sites, session, progress, cancel.Token,
                    [.. _projectSettings.Bridges.Select(b => BridgeTool.KeepClearArea(b, world.Size))]);
                RebuildBridges(world, bridges, library, session);
            }, session => NoteGeneratorState(session, $"seed {options.Seed}"));
        }
        finally
        {
            _generateCancel = null;
            g.IsRunning = false;
            _vm.IsBusy = false;
        }
        g.RefreshWilderness();
        if (!ok || result is null || ran is null) return;

        NoteSpawnsTakenDown();
        if (gen.PlacedWilderness.Count > 0) gen.Done.Add(GeneratorStage.Wilderness);
        int pinned = gen.PlacedWilderness.Count(p => p.Pinned);
        g.WildernessStatus = $"Placed {result.Added.Count} POIs ({result.Prefabs:N0} prefabs){(pinned > 0 ? $", kept {pinned} pinned" : "")}, "
                             + (options.Roads ? $"{result.Roads} gravel roads, {result.RoadKilometres:0} km," : "no roads,")
                             + $" with seed {options.Seed} in {sw.Elapsed.TotalSeconds:0.0} s." + (result.Notes.Count > 0 ? " " + string.Join(" ", result.Notes) : "");
        g.WildernessStatusShort = $"{result.Added.Count} POIs, " + (options.Roads ? $"{result.Roads} gravel roads, {result.RoadKilometres:0} km" : "no roads");
        Map.InvalidateRegion(new GridRect(0, 0, world.Size, world.Size));
        Map.InvalidateVisual();
        _prefabTools.Refresh();
        _genTools.Refresh();
        _vm.Status = $"Wilderness done in {sw.Elapsed.TotalSeconds:0.0} s. Undo (Ctrl+Z) takes it all out again." + TakeBridgeNote();
        CloseIfWaiting();
    }

    private void WildernessDefaults_Click(object sender, RoutedEventArgs e)
    {
        if (_projectSettings.Generator is not { } gen) return;
        int seed = gen.Wilderness.Seed;
        gen.Wilderness = new WildernessOptions { Seed = seed };
        _vm.Generate.Refresh();
        MarkSettingsChanged();
    }

    private void WildernessRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_world is not { } world || RefuseWhileBusy() || _projectSettings.Generator is not { } gen || gen.PlacedWilderness.Count == 0) return;
        RemoveWilderness(world, gen, gen.PlacedWilderness.ToList(), "Remove wilderness POIs");
        gen.Done.Remove(GeneratorStage.Wilderness);
        _vm.Generate.WildernessStatus = "Every POI and gravel road this stage placed was taken out.";
    }

    private void RemoveWilderness(World world, GeneratorSettings gen, List<WildernessPoi> which, string name)
    {
        var reach = PoiReach(world, which);
        if (!_edits.Run(world, name, session =>
            {
                NoteGeneratorState(session);
                _spawnsTakenDown = TakeDownSpawns(world, gen, session);
                WildernessStage.Remove(world, gen.PlacedWilderness, which, session);
            })) return;
        NoteSpawnsTakenDown();
        _vm.Generate.RefreshWilderness();
        Map.InvalidateRegion(reach);
        Map.InvalidateVisual();
        _prefabTools.Refresh();
        _genTools.Refresh();
        _vm.Status = $"{name}: done. Undo (Ctrl+Z) puts it back.";
    }

    /// <summary>What POIs and their roads can have changed on the map.</summary>
    private GridRect PoiReach(World world, IEnumerable<WildernessPoi> pois)
    {
        var r = GridRect.Empty;
        foreach (var p in pois)
        {
            if (_vm.Prefabs.Find(p.Name) is { } info)
            {
                var f = PrefabPlacer.Footprint(world, info, p.X, p.Z, p.Rotation);
                r = r.Union(new GridRect(f.X0 - 40, f.Z0 - 40, f.X1 + 40, f.Z1 + 40));
            }
            if (p.Road is { } road)
                foreach (var pt in road.Points) r = r.Union(new GridRect((int)pt[0] - 30, (int)pt[1] - 30, (int)pt[0] + 30, (int)pt[1] + 30));
        }
        return r.Clamp(world.Size, world.Size);
    }

    /// <summary>
    /// An earlier stage is about to change the ground: stage 5's POIs and gravel roads come out first, in the same edit,
    /// putting back the ground they changed. Pinned POIs wait to come back on the next run. Returns how many came out.
    /// </summary>
    private int TakeDownWilderness(World world, GeneratorSettings? gen, EditSession session)
    {
        // Stage 6 came after stage 5, so its spawns come out first.
        _spawnsTakenDown = TakeDownSpawns(world, gen, session);
        return gen is { PlacedWilderness.Count: > 0 } ? WildernessStage.TakeDown(world, gen.PlacedWilderness, session) : 0;
    }

    /// <summary>After an earlier stage took stage 5 out: says so and marks the stage as not run.</summary>
    private void NoteWildernessTakenDown(int count)
    {
        NoteSpawnsTakenDown();
        if (count == 0 || _projectSettings.Generator is not { } gen) return;
        gen.Done.Remove(GeneratorStage.Wilderness);
        _vm.Generate.RefreshWilderness();
        _prefabTools.Refresh();
        _vm.Generate.WildernessStatus = $"{count} wilderness POI{(count == 1 ? " was" : "s were")} taken out as an earlier stage changed; Generate places them again.";
    }

    // ---- stage 6: spawns ----------------------------------------------------------

    /// <summary>How many spawns the last <see cref="TakeDownWilderness"/> or stage 5 edit took out, for <see cref="NoteSpawnsTakenDown"/>.</summary>
    private int _spawnsTakenDown;

    /// <summary>An earlier stage is about to run: stage 6's spawns come out first, in the same edit. Hand-placed ones stay.</summary>
    private static int TakeDownSpawns(World world, GeneratorSettings? gen, EditSession session) =>
        gen is { PlacedSpawns.Count: > 0 } ? SpawnStage.TakeDown(world, gen.PlacedSpawns, session) : 0;

    /// <summary>After an earlier stage took stage 6's spawns out: says so and marks the stage as not run.</summary>
    private void NoteSpawnsTakenDown()
    {
        int count = _spawnsTakenDown;
        _spawnsTakenDown = 0;
        if (count == 0 || _projectSettings.Generator is not { } gen) return;
        gen.Done.Remove(GeneratorStage.Spawns);
        _vm.Generate.RefreshSpawns();
        _prefabTools.Refresh();
        _vm.Generate.SpawnStatus = $"{count} spawn{(count == 1 ? " was" : "s were")} taken out as an earlier stage changed; Generate places them again.";
    }

    private async void SpawnsGenerate_Click(object sender, RoutedEventArgs e) => await GenerateSpawnsAsync();

    private async void SpawnsReroll_Click(object sender, RoutedEventArgs e)
    {
        await RerollAsync(() => _vm.Generate.SpawnSeed = Random.Shared.Next(1, 1_000_000), GenerateSpawnsAsync);
    }

    /// <summary>Runs the spawns stage as one edit: the last run's spawns come out (hand-placed ones stay) and new ones go in.</summary>
    private async Task GenerateSpawnsAsync()
    {
        if (_world is not { } world || RefuseWhileBusy() || _projectSettings.Generator is not { } gen) return;
        await _gameDataLoad;
        var g = _vm.Generate;
        var options = gen.Spawns.Clone();
        var (library, biomes) = (_vm.Prefabs, _vm.Biomes);
        var sites = gen.Sites.ToList();
        var pois = gen.PlacedWilderness.ToList();
        _vm.IsBusy = true;
        g.IsRunning = true;
        _vm.Status = "Placing spawn points...";
        var sw = Stopwatch.StartNew();
        SpawnResult? result = null;
        bool ok;
        try
        {
            ok = await _edits.RunInBackgroundAsync(world, "Generate spawns", session =>
                result = SpawnStage.Run(world, options, gen.PlacedSpawns, library, biomes, sites, pois, session),
                session => NoteGeneratorState(session, $"seed {options.Seed}"));
        }
        finally
        {
            g.IsRunning = false;
            _vm.IsBusy = false;
        }
        g.RefreshSpawns();
        if (!ok || result is null) return;

        if (gen.PlacedSpawns.Count > 0) gen.Done.Add(GeneratorStage.Spawns);
        g.SpawnStatus = $"Placed {result.Added} spawns ({result.ByRoad} by gravel roads, {result.TopUp} topping up)"
                        + (result.Kept > 0 ? $", kept {result.Kept} already there" : "")
                        + $"; {world.Spawns.Count} in all, {result.Traders} traders, seed {options.Seed}."
                        + (result.Notes.Count > 0 ? " " + string.Join(" ", result.Notes) : "");
        g.SpawnStatusShort = $"{world.Spawns.Count} spawns ({result.ByRoad} by roads, {result.TopUp} top-up" + (result.Kept > 0 ? $", {result.Kept} kept" : "") + ")";
        Map.InvalidateVisual();
        _prefabTools.Refresh();
        _vm.Status = $"Spawns done in {sw.Elapsed.TotalSeconds:0.0} s. Undo (Ctrl+Z) takes them out again.";
        CloseIfWaiting();
    }

    private void SpawnsDefaults_Click(object sender, RoutedEventArgs e)
    {
        if (_projectSettings.Generator is not { } gen) return;
        int seed = gen.Spawns.Seed;
        // The starter-trader tick is the editor's own, not a game setting, so it stays.
        gen.Spawns = new SpawnOptions { Seed = seed, NearStarterTrader = gen.Spawns.NearStarterTrader };
        _vm.Generate.Refresh();
        MarkSettingsChanged();
    }

    private void SpawnsRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_world is not { } world || RefuseWhileBusy() || _projectSettings.Generator is not { } gen || gen.PlacedSpawns.Count == 0) return;
        int n = 0;
        if (!_edits.Run(world, "Remove spawns", session =>
            {
                NoteGeneratorState(session);
                n = SpawnStage.TakeDown(world, gen.PlacedSpawns, session);
            })) return;
        gen.Done.Remove(GeneratorStage.Spawns);
        _vm.Generate.RefreshSpawns();
        _vm.Generate.SpawnStatus = $"Took out the {n} spawn{(n == 1 ? "" : "s")} this stage placed; {world.Spawns.Count} left.";
        Map.InvalidateVisual();
        _prefabTools.Refresh();
        _vm.Status = "Spawns taken out. Undo (Ctrl+Z) puts them back.";
    }

    private void SpawnsEdit_Click(object sender, RoutedEventArgs e)
    {
        _vm.SelectedTool = EditorTool.Spawn;
        Map.Focus();
    }


    private ProjectStore.ProjectSettings CopyProjectSettings()
    {
        var settings = _projectSettings.Clone();
        settings.ExportHasChanges = settings.ExportFolder is null ? null : ExportHasChanges;
        return settings;
    }

    /// <summary>Generator settings changed (a preset, a seed): the project has something new to save.</summary>
    /// <summary>
    /// Saves the generator's Done marks, plans and status lines in a generator edit's undo step, so undo and redo
    /// bring them back with the ground (#15). Call it on the UI thread before the edit changes anything.
    /// </summary>
    private void NoteGeneratorState(EditSession session, string? historyDetail = null)
    {
        session.HistoryDetail = historyDetail;
        if (_projectSettings.Generator is { } gen) GeneratorUndoState.Touch(session, gen, _vm.Generate, _seedsBeforeReroll);
        _seedsBeforeReroll = null;
    }

    /// <summary>The seeds from before a Re-roll changed one, for its run's undo step; null outside a Re-roll.</summary>
    private int[]? _seedsBeforeReroll;

    /// <summary>
    /// A stage's Re-roll: a new seed, then the run. The seed changes before the run starts, so the run's undo step is
    /// given the seeds from before it, and Ctrl+Z puts the old seed back with the old result.
    /// </summary>
    private async Task RerollAsync(Action newSeed, Func<Task> run)
    {
        if (_vm.IsBusy) return;
        _seedsBeforeReroll = _projectSettings.Generator is { } gen ? GeneratorUndoState.SeedsOf(gen) : null;
        try
        {
            newSeed();
            await run();
        }
        finally
        {
            // A run that stopped before its edit (a refused or cancelled run) must not hand them to a later one.
            _seedsBeforeReroll = null;
        }
    }

    private void MarkSettingsChanged()
    {
        if (_world is null) return;
        _dirty = true;
        _revision++;
        UpdateTitle();
    }

    private void LoadPresets()
    {
        var presets = _vm.Generate.Presets;
        presets.Clear();
        foreach (var p in BuiltInPresets.All) presets.Add(p);
        foreach (var p in _settings.GeneratorPresets) presets.Add(p);
        PresetBox.SelectedIndex = 0;
    }

    private void Generate_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        _vm.SidePanelTab = MainViewModel.GenerateTab;
        _vm.ShowSidePanel = true;
    }

    private void GenerateNewWorldSeed_Click(object sender, RoutedEventArgs e) =>
        _vm.Generate.WorldSeed = Random.Shared.Next(1, 1_000_000);

    private void PresetUse_Click(object sender, RoutedEventArgs e)
    {
        if (PresetBox.SelectedItem is not GeneratorPreset preset) return;
        _vm.Generate.Settings.Apply(preset, _vm.Mixer);
        _vm.Generate.RefreshTownRows();
        _vm.Generate.RefreshFeatureRows(_vm.Mixer);
        _vm.Generate.Refresh();
        MarkSettingsChanged();
        _vm.Status = $"Using the {preset.Name} settings. Press Generate to make the terrain with them.";
    }

    private void PresetSave_Click(object sender, RoutedEventArgs e)
    {
        string name = _vm.Generate.PresetName.Trim();
        if (name.Length == 0)
        {
            _vm.Status = "Type a name for the preset first.";
            return;
        }
        if (BuiltInPresets.IsBuiltIn(name))
        {
            _vm.Status = "That name is a built-in preset's; pick another.";
            return;
        }
        var preset = _vm.Generate.Settings.ToPreset(name);
        _settings.GeneratorPresets.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        _settings.GeneratorPresets.Add(preset);
        bool saved = _settings.Save();
        LoadPresets();
        PresetBox.SelectedItem = _vm.Generate.Presets.FirstOrDefault(p => p.Name == name);
        _vm.Generate.PresetName = "";
        _vm.Status = saved ? $"Saved the preset {name}."
            : $"Added the preset {name}, but couldn't save the editor's settings, so it will be lost when the editor closes. Details are in crash.log.";
    }

    private void PresetDelete_Click(object sender, RoutedEventArgs e)
    {
        if (PresetBox.SelectedItem is not GeneratorPreset preset) return;
        if (preset.BuiltIn)
        {
            _vm.Status = $"{preset.Name} comes with the editor and can't be deleted.";
            return;
        }
        _settings.GeneratorPresets.RemoveAll(p => p.Name == preset.Name);
        _settings.Save();
        LoadPresets();
        _vm.Status = $"Deleted the preset {preset.Name}.";
    }

    private void GenerateSelectArea_Click(object sender, RoutedEventArgs e)
    {
        _vm.Generate.IsArea = true;
        _vm.SelectedTool = EditorTool.GenerateArea;
        Map.Focus();
    }

    private void GenerateCancel_Click(object sender, RoutedEventArgs e) => _generateCancel?.Cancel();

    private async void TerrainGenerate_Click(object sender, RoutedEventArgs e) => await GenerateTerrainAsync();

    private async void TerrainReroll_Click(object sender, RoutedEventArgs e)
    {
        await RerollAsync(() => _vm.Generate.TerrainSeed = Random.Shared.Next(1, 1_000_000), GenerateTerrainAsync);
    }

    /// <summary>
    /// A new biome layout on the ground as it is: no terrain run, so a terrain the user likes stays (#26). The new seed
    /// is set inside the edit, so undo puts the old one back with the old biomes.
    /// </summary>
    private async void BiomeReroll_Click(object sender, RoutedEventArgs e)
    {
        var g = _vm.Generate;
        if (_vm.IsBusy || _world is not { } world || _projectSettings.Generator is not { Biomes: { } layout } gen || !g.WholeMap) return;
        var stamps = _vm.Stamps;
        var shape = gen.Terrain.Shape.Clone();
        int seed = Random.Shared.Next(1, 1_000_000);
        _vm.IsBusy = true;
        g.IsRunning = true;
        _vm.Status = "Planning the biomes again...";
        bool ok;
        try
        {
            ok = await _edits.RunInBackgroundAsync(world, "Re-roll biomes", session =>
            {
                if (!TerrainStage.PlanBiomes(world, layout, shape, stamps, session))
                    throw new InvalidOperationException("The biome map isn't the size New World made it.");
            }, session =>
            {
                NoteGeneratorState(session, $"seed {seed}");
                layout.Seed = seed;
                layout.PlannedFor = shape.Clone();
                // The last run's mountain biome was painted over the old layout; the next terrain run must not put it back.
                g.LastMask = null;
            });
        }
        finally
        {
            g.IsRunning = false;
            _vm.IsBusy = false;
        }
        g.Refresh();
        if (!ok) return;
        Map.InvalidateRegion(new GridRect(0, 0, world.Size, world.Size));
        _vm.Status = $"Planned the biomes again with seed {seed}. The ground is unchanged.";
    }

    private async void CoastReroll_Click(object sender, RoutedEventArgs e)
    {
        await RerollAsync(() => _vm.Generate.CoastSeed = Random.Shared.Next(1, 1_000_000), GenerateTerrainAsync);
    }

    /// <summary>An area run plans only its own tiles: puts the last plan back everywhere else. Changes and returns <paramref name="plan"/>.</summary>
    private static TerrainType[] MergeTerrainPlan(TerrainType[] plan, GridRect? area, TerrainType[]? last, int tiles)
    {
        if (area is { } a && last is not null && last.Length == plan.Length)
            for (int i = 0; i < last.Length; i++)
                if (!a.Contains(i % tiles, i / tiles)) plan[i] = last[i];
        return plan;
    }

    /// <summary>
    /// Runs the terrain stage in the background as one edit: the whole map, or the area picked in the panel. Cancel
    /// rolls it back.
    /// </summary>
    private Task GenerateTerrainAsync() => GenerateTerrainAsync(null);

    private async Task GenerateTerrainAsync(LandDrawing? drawing)
    {
        if (_world is not { } world || RefuseWhileBusy()) return;
        await _gameDataLoad;
        var g = _vm.Generate;
        var stamps = _vm.Stamps;
        string action = drawing is null ? "Generate terrain" : "Build World";
        if (drawing is null && !g.CanGenerateLandShape)
        {
            _vm.Status = "Click Edit land to draw the land first.";
            return;
        }
        if (drawing is null && stamps.WithPrefix("plains").Count + stamps.WithPrefix("hills").Count + stamps.WithPrefix("mountains").Count == 0)
        {
            ThemedMessageBox.Show(this, "The terrain is made from the game's own stamps, and none were found. Set the game folder under Settings > Game Folder.",
                "Generate terrain", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (world.Biomes is null)
        {
            _vm.Status = "This world has no biome map, which the terrain follows. Paint biomes first.";
            return;
        }
        GridRect? area = drawing is not null || g.WholeMap ? null : g.Area;
        if (drawing is null && !g.WholeMap && area is null)
        {
            _vm.Status = "Select an area first: press Select area in the Generate panel, then drag on the map.";
            return;
        }

        bool hasBuilt = world.Prefabs.Count > 0 || world.Roads?.Data.AsSpan().ContainsAnyExcept((byte)0) == true;
        var options = g.Settings.Terrain.Clone();
        if (drawing is not null) options.Shape.Drawing = drawing;
        var mask = g.LastMask;
        var mixer = _vm.Mixer;
        var replan = area is null && (g.ReplanBiomes || drawing is not null && !options.Shape.SameLand(g.Settings.Biomes?.PlannedFor))
            ? g.Settings.Biomes?.Clone() : null;
        var library = _vm.Prefabs;
        // New ground clears the later stages: built towns come down in the same edit and their sites wait to be built again.
        var gen = _projectSettings.Generator;
        var builtSites = gen?.Sites.Where(s => s.Built).ToList() ?? [];
        GridRect? areaMetres = area is { } am ? am.Scale(TerrainStage.TileSize) : null;
        bool hasFeatures = gen?.PlacedFeatures.Any(f => f.Drawn) == true;
        bool hasRoads = gen?.PlacedRoads.Count > 0;
        bool hasWild = gen?.PlacedWilderness.Any(p => p.Drawn) == true;
        int downCount = areaMetres is { } ar
            ? builtSites.Count(s => s.Bounds.X0 < ar.X1 && s.Bounds.X1 > ar.X0 && s.Bounds.Z0 < ar.Z1 && s.Bounds.Z1 > ar.Z0)
            : builtSites.Count;
        bool plannedToClear = area is null && gen?.Sites.Any(s => !s.Built && !s.Pinned) == true;
        string? townsLater = area is null
            ? (builtSites.Count > 0 || plannedToClear
                ? (builtSites.Count > 0 ? $"{builtSites.Count} built town{(builtSites.Count == 1 ? "" : "s")} will be taken down and the town plan cleared" : "the town plan will be cleared")
                  + " (pinned towns stay where the land still fits)"
                : null)
            : (downCount > 0 ? $"{downCount} built town{(downCount == 1 ? "" : "s")} in the area will be taken down, and planned towns that no longer fit will be dropped"
                : gen?.Sites.Any(s => !s.Built) == true ? "planned towns that no longer fit will be dropped" : null);
        string later = string.Join(" and ", new[]
        {
            townsLater,
            hasFeatures ? "lakes, rivers, canyons and craters will be taken out (pinned ones come back on the next Features run)" : null,
            hasRoads ? "every highway will be taken out" : null,
            hasWild ? "every wilderness POI and gravel road will be taken out (pinned ones come back on the next Wilderness run)" : null,
        }.Where(t => t is not null));
        if (later.Length > 0 && ThemedMessageBox.Show(this,
                $"New ground clears the later stages: {(area is null ? "" : "in or near the area, ")}{later}.\n\n{action}?",
                action, MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        int takenDown = 0, featuresOut = 0, roadsOut = 0, sitesCleared = 0, sitesUnusable = 0;
        // Read on the UI thread; the run uses them in the background to check the town sites against the new ground.
        var (lastPlan, tiles, biomes) = (g.LastPlan, g.Tiles, _vm.Biomes);
        using var cancel = new CancellationTokenSource();
        _generateCancel = cancel;
        _vm.IsBusy = true;
        g.IsRunning = true;
        g.Progress = 0;
        _vm.Status = drawing is not null ? "Building the world..." : area is null ? "Generating the terrain..." : "Generating the terrain in the area...";
        var progress = new Progress<double>(v => g.Progress = v);
        var sw = Stopwatch.StartNew();
        TerrainResult? result = null;
        EditSession? ran = null;
        bool ok;
        try
        {
            ok = await _edits.RunInBackgroundAsync(world, drawing is null ? "Generate terrain" : "Build World", session =>
            {
                ran = session;
                // Later stages come out newest first, so each puts back the ground it found.
                var bridges = TakeDownBridges(world, session);
                roadsOut = TakeDownHighways(world, gen, session);
                if (hasFeatures) featuresOut = FeatureStage.TakeDown(world, gen!.PlacedFeatures, areaMetres, stamps, session);
                if (builtSites.Count > 0) takenDown = TownSiteStage.TakeDown(world, library, builtSites, areaMetres, session);
                result = TerrainStage.Run(world, options, stamps, mixer, painted: null, area, mask, session, progress, cancel.Token, replan);
                RebuildBridges(world, bridges, library, session);
                if (gen is not null && gen.Sites.Count > 0)
                {
                    // The new land can leave sites over water or mountains. The list is snapshotted before it changes, so undo
                    // puts them back (into the same list the settings hold).
                    var plan = MergeTerrainPlan(result.Plan, area, lastPlan, tiles);
                    var cells = TownCells.Build(world, biomes, library, plan, gen.Towns.CrossBorders);
                    var kept = gen.Sites.ToList();
                    (sitesCleared, sitesUnusable) = TownSiteStage.DropForNewGround(kept, cells, area is null);
                    if (kept.Count != gen.Sites.Count)
                    {
                        session.TouchList(gen.Sites, s => s.Clone(), new GridRect(0, 0, world.Size, world.Size));
                        gen.Sites.Clear();
                        gen.Sites.AddRange(kept);
                    }
                }
            }, session =>
            {
                if (drawing is not null)
                    session.TouchState(_landTools, () => g.Settings.Terrain.Shape.Drawing,
                        previous => g.Settings.Terrain.Shape.Drawing = (LandDrawing?)previous, ReferenceEquals);
                NoteGeneratorState(session, drawing is null ? $"seed {options.Seed}"
                    : $"{drawing.Boxes.Count} land {(drawing.Boxes.Count == 1 ? "box" : "boxes")}");
                if (drawing is not null) g.Settings.Terrain.Shape.Drawing = drawing;
            });
        }
        finally
        {
            _generateCancel = null;
            g.IsRunning = false;
            _vm.IsBusy = false;
        }
        if (!ok || result is null || ran is null)
        {
            // A cancelled or failed run rolled the towns back up.
            if (gen is not null) TownSiteStage.MarkBuilt(world, library, gen.Sites);
            g.RefreshFeatures();
            CloseIfWaiting();
            return;
        }
        g.RefreshFeatures();
        NoteHighwaysTakenDown(roadsOut);
        if (featuresOut > 0)
        {
            if (!g.Settings.PlacedFeatures.Any(f => f.Drawn)) g.Settings.Done.Remove(GeneratorStage.Features);
            g.FeatureStatus = $"{featuresOut} feature stamp{(featuresOut == 1 ? " was" : "s were")} taken out for the new ground; Generate presses them in again.";
        }

        // An area run plans only its own tiles; keep the last plan elsewhere.
        MergeTerrainPlan(result.Plan, area, g.LastPlan, g.Tiles);
        g.LastPlan = result.Plan;
        g.LastMask = result.Mask;
        g.Settings.TerrainPlan = result.Plan;
        g.Settings.Done.Add(GeneratorStage.Terrain);
        if (drawing is not null) g.Settings.Done.RemoveWhere(stage => stage != GeneratorStage.Terrain);
        if (result.BiomesPlanned && g.Settings.Biomes is { } planned) planned.PlannedFor = options.Shape.Clone();
        g.ShapeChanged();
        _townCells = null;
        if (takenDown > 0 || sitesCleared > 0 || sitesUnusable > 0)
        {
            if (gen is not null && !gen.Sites.Any(s => s.Built)) g.Settings.Done.Remove(GeneratorStage.TownSites);
            string dropped = sitesUnusable > 0
                ? $" {sitesUnusable} planned town{(sitesUnusable == 1 ? " was" : "s were")} dropped because {(sitesUnusable == 1 ? "its" : "their")} ground is now water, mountains or radiation."
                : "";
            string down = takenDown > 0 ? $"{takenDown} town{(takenDown == 1 ? " was" : "s were")} taken down for the new ground. " : "";
            g.TownStatus = area is null
                ? down + "Town plan cleared for the new ground; Plan towns picks new sites." + dropped
                : (down + (takenDown > 0 ? "Build puts them up again." : "") + dropped).Trim();
            if (gen?.Sites.Count == 0)
            {
                g.Planned = [];
            }
        }
        string where = area is null ? "the whole map" : "the area";
        string biomesNote = result.BiomesPlanned ? " The biomes were planned again for the land." : "";
        g.TerrainStatus = $"Generated {where} with seed {options.Seed}: {result.Stamps:N0} stamps in {sw.Elapsed.TotalSeconds:0.0} s.{biomesNote}";
        g.TerrainStatusShort = $"Generated {where}, {result.Stamps:N0} stamps";
        if (drawing is not null)
        {
            g.RefreshDone();
            g.Refresh();
            ExitLandMode();
        }
        Map.InvalidateRegion(ran.Bounds);
        Map.InvalidateVisual();
        _prefabTools.Refresh();
        _townTools.Refresh();
        _genTools.Refresh();
        _vm.Status = $"Generated the terrain for {where} in {sw.Elapsed.TotalSeconds:0.0} s.{biomesNote} Undo (Ctrl+Z) puts the old ground back."
                     + (hasBuilt && world.Prefabs.Count > 0 ? " Prefabs and roads stayed where they were; check they still sit right (Validate, F7)." : "")
                     + TakeBridgeNote();
        CloseIfWaiting();
        if (gen?.Sites.Any(s => !s.Built) == true) await ReplanTownsAsync();
    }

    // ---- heightmap import/export -------------------------------------------

    private void ImportHeightmap_Click(object sender, RoutedEventArgs e)
    {
        if (_world is not { } world || RefuseWhileBusy()) return;
        var dialog = new OpenFileDialog { Title = "Import heightmap", Filter = "Heightmaps (*.png;*.raw)|*.png;*.raw" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var imported = HeightmapFile.Import(dialog.FileName, world.Size);
            var all = new GridRect(0, 0, world.Size, world.Size);
            if (!_edits.Run(world, "Import Heightmap", session =>
                {
                    session.Touch(world.Height, all);
                    imported.Data.CopyTo(world.Height.Data, 0);
                }))
                return;
            Map.InvalidateRegion(all);
            _vm.Status = "Imported " + Path.GetFileName(dialog.FileName);
        }
        catch (Exception ex) when (IsBadInput(ex))
        {
            ThemedMessageBox.Show(this, ex.Message, "Import Heightmap", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportHeightmap_Click(object sender, RoutedEventArgs e)
    {
        if (_world is not { } world) return;
        var dialog = new SaveFileDialog
        {
            Title = "Export heightmap",
            Filter = "16-bit PNG (*.png)|*.png|Game RAW (*.raw)|*.raw",
            FileName = world.Name + "_height",
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            HeightmapFile.Export(world.Height, dialog.FileName);
            _vm.Status = "Exported " + Path.GetFileName(dialog.FileName);
        }
        catch (Exception ex) when (IsWriteError(ex))
        {
            _vm.Status = "Heightmap export failed";
            ThemedMessageBox.Show(this, ex.Message, "Export Heightmap", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Saves the whole world as a PNG. The starting choice uses the map view's layers, POIs and world grid.
    /// The picture is drawn off the UI thread while edits are held off.
    /// </summary>
    private async void ExportMapImage_Click(object sender, RoutedEventArgs e)
    {
        if (_world is not { } world || RefuseWhileBusy()) return;
        var dialog = new MapImageDialog(world.Size, _vm.Layers, _vm.ShowPrefabs, _vm.ShowWorldGrid) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Options is not { } options) return;
        var save = new SaveFileDialog
        {
            Title = "Export map image",
            Filter = "PNG image (*.png)|*.png",
            FileName = world.Name + "_map",
            DefaultExt = ".png",
        };
        if (save.ShowDialog(this) != true) return;

        string path = save.FileName;
        _vm.IsBusy = true;
        _vm.Status = "Drawing the map image...";
        try
        {
            var sw = Stopwatch.StartNew();
            var progress = new Progress<double>(f => _vm.Status = $"Drawing the map image... {f:P0}");
            var biomes = _vm.Biomes;
            var prefabs = _vm.Prefabs;
            await Task.Run(() => new MapImage(world, biomes, prefabs, options).WritePng(path, progress));
            RecordHistoryEvent($"Export Map Image: {Path.GetFileName(path)}");
            _vm.Status = $"Exported {Path.GetFileName(path)} in {sw.Elapsed.TotalSeconds:0.0} s";
        }
        catch (Exception ex) when (IsWriteError(ex) || ex is OutOfMemoryException)
        {
            _vm.Status = "Map image export failed";
            ThemedMessageBox.Show(this, ex.Message, "Export Map Image", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _vm.IsBusy = false;
        }
    }

    // ---- project history --------------------------------------------------

    private void BeginHistory(string text)
    {
        var history = new ProjectHistory(_undo, _projectSettings.History);
        history.RecordEvent(HistoryEntry.Event(text));
        _vm.History = history;
    }

    private void RecordHistoryEvent(string text) => RecordHistoryEvent(HistoryEntry.Event(text));

    private void RecordHistoryEvent(HistoryEntry entry)
    {
        _vm.History?.RecordEvent(entry);
        _vm.RefreshHistory();
    }

    private void History_Click(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(HistoryList, (DependencyObject)e.OriginalSource) is ListBoxItem { DataContext: HistoryEntry entry })
            JumpToHistory(entry);
    }

    private void History_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.OriginalSource is Button || HistoryList.SelectedItem is not HistoryEntry entry) return;
        JumpToHistory(entry);
        e.Handled = true;
    }

    private void JumpToHistory(HistoryEntry entry)
    {
        if (_world is not { } world || entry.Bounds is not { } area) return;
        area = area.Clamp(world.Size, world.Size);
        if (area.IsEmpty) return;
        double span = Math.Max(500, 1.15 * Math.Max(area.X1 - area.X0, area.Z1 - area.Z0));
        Map.ZoomTo(Math.Min(Map.ActualWidth, Map.ActualHeight) / span);
        Map.CenterOn(new Point((area.X0 + area.X1) / 2.0, world.Size - (area.Z0 + area.Z1) / 2.0));
    }

    private void HistoryMove_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = !_vm.IsLandMode && CanChangeUndoHistory && e.Parameter is HistoryEntry entry
            && _vm.History?.ActionFor(entry) is HistoryAction.Undo or HistoryAction.Redo;

    private void HistoryMove_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_vm.IsLandMode || !CanChangeUndoHistory || e.Parameter is not HistoryEntry entry || _vm.History is not { } history) return;
        var action = history.ActionFor(entry);
        if (history.MoveTo(entry, action) is { } move)
            AfterUndoRedo(move.Bounds, move.LastStep.Name, redo: action == HistoryAction.Redo);
    }

    // ---- undo --------------------------------------------------------------

    private bool CanChangeUndoHistory => !_vm.IsBusy && _tools is { IsStroking: false }
        && _prefabTools is { IsDragging: false } && _multiSelect is { IsDragging: false } && _townTools is { IsDragging: false };

    private void Undo_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = _vm.IsLandMode ? _landTools is not null && CanUndoLand(redo: false) : _undo.CanUndo && CanChangeUndoHistory;

    private void Redo_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = _vm.IsLandMode ? _landTools is not null && CanUndoLand(redo: true) : _undo.CanRedo && CanChangeUndoHistory;

    private void Undo_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_vm.IsLandMode) { UndoLand(redo: false); return; }
        if (_vm.History?.Undo() is { } step) AfterUndoRedo(step.Bounds, step.Name, redo: false);
    }

    private void Redo_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_vm.IsLandMode) { UndoLand(redo: true); return; }
        if (_vm.History?.Redo() is { } step) AfterUndoRedo(step.Bounds, step.Name, redo: true);
    }

    private void AfterUndoRedo(GridRect bounds, string editName, bool redo)
    {
        _vm.RefreshHistory();
        _townCells = null;
        _vm.Generate.RefreshFeatures();
        _vm.Generate.RefreshHighways();
        _vm.Generate.RefreshWilderness();
        if (_world is not null && _projectSettings.Generator is { Sites.Count: > 0 } gen)
        {
            TownSiteStage.MarkBuilt(_world, _vm.Prefabs, gen.Sites);
            _ = ReplanTownsAsync();
        }
        // Undo can bring back a copy taken before a game-folder change converted it.
        if (_world is not null) GroundLevel.Resolve(_world, _vm.Prefabs);
        Map.InvalidateRegion(bounds);
        _prefabTools.Refresh();
        _townTools.Refresh();
        OnWorldEdited();
        _vm.Status = $"{(redo ? "Redid" : "Undid")} {editName}";
    }

    private void UpdateUndoText()
    {
        _vm.UndoText = (_vm.IsLandMode ? _landTools.UndoName : _undo.UndoName) is { } u ? $"Undo {u}" : "Undo";
        _vm.RedoText = (_vm.IsLandMode ? _landTools.RedoName : _undo.RedoName) is { } r ? $"Redo {r}" : "Redo";
        CommandManager.InvalidateRequerySuggested();
    }

    private void OnWorldEdited()
    {
        _townCells = null;
        // Edits, undo and redo move the history; Dirty compares it with the save point.
        UpdateTitle();
        _vm.RefreshPlaced();
        _multiSelect.Refresh();
        if (Map.Renderer is { } renderer) Overview.SetRenderer(renderer, _vm.Layers);
    }

    /// <summary>An edit threw partway and was rolled back: redraw where it reached, and say so.</summary>
    private void OnEditFailed(EditFailedEventArgs e)
    {
        if (e.Error is OperationCanceledException)
        {
            Map.InvalidateRegion(e.Session.Bounds);
            _vm.Status = $"{e.Session.Name} cancelled; nothing was changed.";
            return;
        }
        App.LogError(e.Error);
        Map.InvalidateRegion(e.Session.Bounds);
        Map.InvalidateVisual();
        _prefabTools.Refresh();
        _townTools.Refresh();
        _multiSelect.Refresh();
        _vm.Status = $"{e.Session.Name} failed; nothing was changed.";
        // Shown after the input event that ran the edit, so a stroke's mouse handling isn't cut off by a dialog.
        string message = $"{e.Session.Name} failed, so it was undone and the world is as it was.\n\n{e.Error.Message}"
                         + $"\n\nDetails were written to {App.CrashLogPath}.";
        _ = Dispatcher.BeginInvoke(() =>
            ThemedMessageBox.Show(this, message, App.ProductName, MessageBoxButton.OK, MessageBoxImage.Error));
    }

    private void UpdateTitle() =>
        Title = _world is null ? App.ProductName : $"{(Dirty ? "*" : "")}{_world.Name} - {App.ProductName}";

    // ---- palette, panels and help --------------------------------------------

    /// <summary>A biome swatch picks that biome and the biome brush, as clicking a colour in Paint picks it.</summary>
    private void BiomeSwatch_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not BiomeDefinition biome) return;
        _vm.SelectedBiome = biome;
        _vm.SelectedTool = EditorTool.Biome;
    }

    private void RoadSwatch_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Swatch { Road: { } road }) return;
        _vm.RoadType = road;
        _vm.SelectedTool = EditorTool.Road;
    }

    private void LayersPopup_Closed(object? sender, EventArgs e) => LayersButton.IsChecked = false;

    private void ShowPrefabBrowser_Click(object sender, RoutedEventArgs e)
    {
        _vm.SidePanelTab = MainViewModel.PrefabsTab;
        _vm.ShowSidePanel = true;
    }

    /// <summary>Turns the side panel's tabs into the Modern theme's activity bar and side bar. Flat never comes here.</summary>
    private void UseModernLayout()
    {
        _vm.IsModernTheme = true;
        UseModernTitleBar();
        System.Windows.Data.BindingOperations.ClearBinding(SidePanel, VisibilityProperty);
        SidePanel.Visibility = Visibility.Visible;
        SidePanel.SetBinding(WidthProperty, nameof(MainViewModel.SideBarOuterWidth));
        SidePanelClose.Visibility = Visibility.Collapsed;
        SideTabs.Margin = new Thickness(0);
        SideTabs.Style = (Style)FindResource("ModernSideTabs");
        SidePanelEdge.Template = (System.Windows.Controls.ControlTemplate)FindResource("ModernSash");
        SidePanelEdge.Width = 9;

        // The ribbon becomes one row of named tools. Its palette and brush rail move to the tool options row.
        var toolGroup = (Style)FindResource("ModernToolGroup");
        foreach (var group in new[] { NavigateGroup, TerrainGroup, PaintGroup, BuildGroup }) group.Style = toolGroup;
        PaletteGroup.Visibility = Visibility.Collapsed;
        PaletteDivider.Visibility = Visibility.Collapsed;
        BuildDivider.Visibility = Visibility.Collapsed;
        LayersDivider.Visibility = Visibility.Collapsed;

        // Layers and the world's actions become bordered buttons at the right of the toolbar.
        RibbonTools.Children.Remove(LayersGroup);
        RibbonTools.Children.Remove(WorldGroup);
        var actions = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        System.Windows.Controls.DockPanel.SetDock(actions, System.Windows.Controls.Dock.Right);
        actions.Children.Add(LayersGroup);
        actions.Children.Add(WorldGroup);
        RibbonDock.Children.Insert(1, actions);
        // The options toggle lines up with the buttons, with a gap after Package.
        ToolOptionsToggle.VerticalAlignment = VerticalAlignment.Center;
        ToolOptionsToggle.Margin = new Thickness(8, 0, 0, 0);
        ToolOptionsToggle.Width = 30;
        ToolOptionsToggle.Height = 30;
        var action = (Style)FindResource("ModernAction");
        UseModernAction(LayersButton, action, Icons.Layers, "Layers", dropDown: true);
        UseModernAction(GenerateAction, action, Icons.Generate, "Generate");
        UseModernAction(ValidateAction, action, Icons.Validate, "Validate");
        UseModernAction(ExportAction, (Style)FindResource("ModernExportAction"), Icons.Export, "Export");
        UseModernAction(PackageAction, action, Icons.Package, "Package");

        LeftRail.Visibility = Visibility.Collapsed;
        ToolHintLines.Visibility = Visibility.Collapsed;
        System.Windows.Data.BindingOperations.ClearBinding(ToolHintLine, VisibilityProperty);
        ToolHintLine.Visibility = Visibility.Collapsed;

        System.Windows.Media.Geometry[] icons = [Icons.Prefab, Icons.ActivityPlaced, Icons.ActivityIssues, Icons.ActivityWorld, Icons.Generate, Icons.ActivityHistory];
        for (int i = 0; i < icons.Length; i++)
        {
            var item = (System.Windows.Controls.TabItem)SideTabs.Items[i];
            string name = ((string)item.Header).Replace("_", "");
            item.Tag = icons[i];
            if (i == MainViewModel.IssuesTab)
            {
                item.Uid = "IssuesView";
                item.SetBinding(System.Windows.Automation.AutomationProperties.HelpTextProperty, nameof(MainViewModel.IssuesText));
            }
            ReserveScrollBarRoom(item.Content);
            item.ToolTip = name;
            System.Windows.Automation.AutomationProperties.SetName(item, name);
        }

        UseModernStages(((System.Windows.Controls.TabItem)SideTabs.Items[MainViewModel.GenerateTab]).Content);
        _vm.Issues.CollectionChanged += (_, _) => _vm.RefreshIssueCounts();

        // Alt+G or the arrow keys pick a view while the side bar is closed: show it.
        SideTabs.SelectionChanged += (_, e) =>
        {
            if (IsLoaded && ReferenceEquals(e.OriginalSource, SideTabs)) _vm.ShowSidePanel = true;
        };
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.SideBarOnRight) or nameof(MainViewModel.SidePanelVisible)) PlaceSideBar();
        };
        PlaceSideBar();
    }

    /// <summary>Gives each Generate stage the Modern row: a numbered circle, its name and status, and Done or Not run.</summary>
    private void UseModernStages(object? node)
    {
        if (node is System.Windows.Controls.Expander { Header: System.Windows.Controls.StackPanel header } stage
            && header.Children.Count == 2 && header.Children[1] is System.Windows.Controls.TextBlock status
            && header.Children[0] is System.Windows.Controls.WrapPanel { Children.Count: 2 } row
            && row.Children[0] is System.Windows.Controls.TextBlock title && row.Children[1] is System.Windows.Controls.Border done)
        {
            string[] name = title.Text.Split("  ", 2);
            string donePath = System.Windows.Data.BindingOperations.GetBinding(done, VisibilityProperty).Path.Path;
            header.Children.Remove(status);
            row.Children.Remove(done);
            done.Margin = new Thickness(8, 0, 0, 0);
            done.VerticalAlignment = VerticalAlignment.Top;

            var number = new System.Windows.Controls.Border
            {
                Style = (Style)FindResource("ModernStageNumber"),
                Child = new System.Windows.Controls.TextBlock
                {
                    Text = name[0], FontSize = 12, FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            var notRun = new System.Windows.Controls.Border
            {
                Style = (Style)FindResource("ModernNotRunPill"),
                Child = new System.Windows.Controls.TextBlock { Text = "Not run" },
            };
            notRun.SetBinding(VisibilityProperty, new System.Windows.Data.Binding(donePath) { Converter = new InverseBoolToVisibilityConverter() });

            var words = new System.Windows.Controls.StackPanel();
            words.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = name[^1], FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
            });
            words.Children.Add(status);

            var line = new System.Windows.Controls.DockPanel();
            System.Windows.Controls.DockPanel.SetDock(number, System.Windows.Controls.Dock.Left);
            System.Windows.Controls.DockPanel.SetDock(done, System.Windows.Controls.Dock.Right);
            System.Windows.Controls.DockPanel.SetDock(notRun, System.Windows.Controls.Dock.Right);
            line.Children.Add(number);
            line.Children.Add(done);
            line.Children.Add(notRun);
            line.Children.Add(words);

            stage.Header = line;
            stage.Margin = new Thickness(0, name[0] == "1" ? 12 : 0, 0, 0);
            stage.Style = (Style)FindResource("ModernStageExpander");
            return;
        }
        if (node is DependencyObject parent)
            foreach (object child in LogicalTreeHelper.GetChildren(parent)) UseModernStages(child);
    }

    /// <summary>Scroll bars are drawn over a view's content, so keeps its buttons and text clear of them.</summary>
    private static void ReserveScrollBarRoom(object? node)
    {
        const double room = 14;
        if (node is System.Windows.Controls.ListBox list)
        {
            list.Padding = new Thickness(list.Padding.Left, list.Padding.Top, room, list.Padding.Bottom);
            return;
        }
        if (node is System.Windows.Controls.ScrollViewer { Content: FrameworkElement content })
            content.Margin = new Thickness(content.Margin.Left, content.Margin.Top, room, content.Margin.Bottom);
        if (node is DependencyObject parent)
            foreach (object child in LogicalTreeHelper.GetChildren(parent)) ReserveScrollBarRoom(child);
    }

    /// <summary>Draws the Modern title bar in the window's own area, with the menus in it.</summary>
    private void UseModernTitleBar()
    {
        var chrome = new System.Windows.Shell.WindowChrome
        {
            CaptionHeight = 36,
            ResizeBorderThickness = SystemParameters.WindowResizeBorderThickness,
            GlassFrameThickness = new Thickness(0, 0, 0, 1),
            UseAeroCaptionButtons = false,
        };
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, chrome);

        // A slim title row: the menus and quick-access buttons fit inside the window buttons' height.
        MenuRow.Margin = new Thickness(4, 0, 4, 0);
        // The theme's menu items are taller than their text needs: let them overlap the row's edges.
        MainMenu.Margin = new Thickness(0, -7, 0, -7);

        // The whole title row drags the window, also the part a maximized window pushes down.
        void FitCaption() => chrome.CaptionHeight = TitleRow.ActualHeight + RootDock.Margin.Top;
        TitleRow.SizeChanged += (_, _) => FitCaption();
        StateChanged += (_, _) => FitCaption();
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(MenuRow, true);
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(WindowControls, true);
        StateChanged += (_, _) => ShowWindowState();
        ShowWindowState();
        SourceInitialized += (_, _) =>
            System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle)?.AddHook(TitleBarHook);
    }

    /// <summary>A maximized window reaches past the screen's edges by its frame, so keeps the content on screen.</summary>
    private void ShowWindowState()
    {
        bool maximized = WindowState == WindowState.Maximized;
        RootDock.Margin = maximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
        MaximizeGlyph.Text = maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";
        System.Windows.Automation.AutomationProperties.SetName(MaximizeButton, maximized ? "Restore" : "Maximize");
    }

    /// <summary>Tells Windows where the maximize button is, so that hovering it shows the Windows 11 snap layouts.</summary>
    private nint TitleBarHook(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        const int hitTest = 0x84, buttonDown = 0xA1, buttonUp = 0xA2, mouseLeave = 0x2A2, maximizeArea = 9;
        switch (message)
        {
            case hitTest:
                var point = MaximizeButton.PointFromScreen(new Point((short)(lParam & 0xFFFF), (short)(lParam >> 16 & 0xFFFF)));
                bool over = MaximizeButton.IsVisible && point.X >= 0 && point.Y >= 0
                    && point.X < MaximizeButton.ActualWidth && point.Y < MaximizeButton.ActualHeight;
                MaximizeButton.Tag = over ? "Hover" : null;
                if (!over) break;
                handled = true;
                return maximizeArea;
            case buttonDown when wParam == maximizeArea:
                handled = true;
                break;
            case buttonUp when wParam == maximizeArea:
                handled = true;
                ToggleMaximized();
                break;
            case mouseLeave:
                MaximizeButton.Tag = null;
                break;
        }
        return 0;
    }

    private void ToggleMaximized()
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    private void WindowMinimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void WindowMaximize_Click(object sender, RoutedEventArgs e) => ToggleMaximized();

    private void WindowClose_Click(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    private void LayoutPopup_Closed(object? sender, EventArgs e) => LayoutButton.IsChecked = false;

    /// <summary>Gives a toolbar button the Modern look: a small icon with its word beside it.</summary>
    private void UseModernAction(System.Windows.Controls.Primitives.ButtonBase button, Style style, System.Windows.Media.Geometry icon, string text, bool dropDown = false)
    {
        var iconStyle = (Style)FindResource("IconPath");
        var content = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        content.Children.Add(new System.Windows.Shapes.Path { Data = icon, Style = iconStyle, Width = 16, Height = 16 });
        var word = new System.Windows.Controls.TextBlock { Text = text, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        word.SetBinding(VisibilityProperty, new System.Windows.Data.Binding(nameof(MainViewModel.ActionTextShown))
        {
            Converter = (System.Windows.Data.IValueConverter)FindResource("BoolToVisibility"),
        });
        content.Children.Add(word);
        if (dropDown)
            content.Children.Add(new System.Windows.Shapes.Path
            {
                Data = Icons.ChevronDown, Style = iconStyle, Width = 12, Height = 12, Margin = new Thickness(6, 0, 0, 0),
            });
        button.Style = style;
        button.Content = content;
    }

    /// <summary>Puts the Modern theme's side bar on its chosen side, outside the brush rail.</summary>
    private void PlaceSideBar()
    {
        bool right = _vm.SideBarOnRight;
        var side = right ? System.Windows.Controls.Dock.Right : System.Windows.Controls.Dock.Left;
        // Moving the panel reloads its contents, so only move it when the side changes.
        if (System.Windows.Controls.DockPanel.GetDock(SidePanel) != side)
        {
            RootDock.Children.Remove(SidePanel);
            int rail = RootDock.Children.IndexOf(LeftRail);
            RootDock.Children.Insert(right ? rail + 1 : rail, SidePanel);
            System.Windows.Controls.DockPanel.SetDock(SidePanel, side);
        }
        SideTabs.TabStripPlacement = side;
        SidePanelEdge.HorizontalAlignment = right ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        SidePanelEdge.Visibility = _vm.SidePanelVisible ? Visibility.Visible : Visibility.Collapsed;
        double line = _vm.SidePanelVisible ? 1 : 0;
        SidePanel.BorderThickness = right ? new Thickness(line, 0, 0, 0) : new Thickness(0, 0, line, 0);
    }

    /// <summary>The Modern status bar's error and warning counts open the Issues view.</summary>
    private void IssueCounter_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsLandMode) return;
        _vm.SidePanelTab = MainViewModel.IssuesTab;
        _vm.ShowSidePanel = true;
    }

    /// <summary>A click on the open view's icon closes the side bar; a click on another icon opens that view.</summary>
    private void ActivityItem_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // A click inside the open view also passes through its tab: only the icon itself counts.
        var item = (System.Windows.Controls.TabItem)sender;
        if (e.OriginalSource is not System.Windows.Media.Visual source || !item.IsAncestorOf(source)) return;

        if (item.IsSelected && _vm.ShowSidePanel)
        {
            _vm.ShowSidePanel = false;
            Map.Focus();
            e.Handled = true;
        }
        else _vm.ShowSidePanel = true;
    }

    /// <summary>Dragging the side panel's inner edge: towards the map widens it, leaving the map at least 300 wide.</summary>
    private void SidePanelEdge_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        double max = Math.Min(MainViewModel.MaxSidePanelWidth, Math.Max(MainViewModel.MinSidePanelWidth, ActualWidth - 300));
        double change = _vm.IsModernTheme && !_vm.SideBarOnRight ? e.HorizontalChange : -e.HorizontalChange;
        _vm.SidePanelWidth = Math.Clamp(_vm.SidePanelWidth + change, MainViewModel.MinSidePanelWidth, max);
    }

    private void CloseSidePanel_Click(object sender, RoutedEventArgs e)
    {
        _vm.ShowSidePanel = false;
        Map.Focus();
    }

    private void WorldInfo_Click(object sender, RoutedEventArgs e)
    {
        _vm.SidePanelTab = MainViewModel.WorldTab;
        _vm.ShowSidePanel = true;
    }

    private void Shortcuts_Click(object sender, RoutedEventArgs e) => ShowShortcuts();

    private ShortcutsWindow? _shortcuts;

    private void ShowShortcuts()
    {
        if (_shortcuts is { } open)
        {
            open.Activate();
            return;
        }
        _shortcuts = new ShortcutsWindow(_vm.Tools.Where(t => t.Shortcut.Length > 0)) { Owner = this };
        _shortcuts.Closed += (_, _) => _shortcuts = null;
        _shortcuts.Show();
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        var result = await Updates.CheckAsync(_settings);
        if (result.Update is null)
        {
            ThemedMessageBox.Show(this, result.Message, "Check for Updates", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (ThemedMessageBox.Show(this, result.Message + "\n\nInstall it now? The editor restarts.", "Check for Updates",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        if (!await ConfirmDiscardAsync()) return;
        _vm.Status = "Downloading the update...";
        _vm.IsBusy = true;
        try
        {
            _closeConfirmed = true;
            _autosaves.Discard();
            await Updates.ApplyAsync(result);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or IOException)
        {
            _closeConfirmed = false;
            _vm.Status = "Update failed: " + ex.Message;
        }
        finally
        {
            _vm.IsBusy = false;
        }
    }

    /// <summary>A quiet check at start-up; only says something when there is an update.</summary>
    private async Task CheckUpdatesQuietlyAsync()
    {
        var result = await Updates.CheckAsync(_settings);
        if (result.Update is not null) _vm.Status = result.Message + " Use Help > Check for Updates to install it.";
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        ThemedMessageBox.Show(this,
            $"{App.ProductName} {Updates.Version}\n\nMakes and edits 7 Days to Die worlds. Game assets are read from your own install " +
            "and never shipped with the editor.\n\nNot made by or affiliated with The Fun Pimps.",
            "About", MessageBoxButton.OK, MessageBoxImage.Information);
    }


    // ---- tools and view ----------------------------------------------------

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.IsBusy):
                // Commands that check IsBusy are not re-queried by WPF on their own.
                CommandManager.InvalidateRequerySuggested();
                UpdateIssueStepControls();
                CloseIfWaiting();
                break;
            case nameof(MainViewModel.GameLinkConnected):
                UpdateIssueStepControls();
                break;
            case nameof(MainViewModel.Layers) when Map.Renderer is { } renderer:
                Overview.SetRenderer(renderer, _vm.Layers);
                break;
            case nameof(MainViewModel.ShowPlayer):
                UpdatePlayerPolling();
                break;
            case nameof(MainViewModel.FollowPlayer):
                KeepPlayerInView(centre: true);
                break;
            case nameof(MainViewModel.SelectedTool):
                if (_vm.SelectedTool == EditorTool.Prefab)
                {
                    _vm.SidePanelTab = MainViewModel.PrefabsTab;
                    _vm.ShowSidePanel = true;
                }
                if (!_vm.IsRoadTool) _tools.CancelRoad();
                if (!_vm.IsBridgeTool) _bridgeTools.Reset();
                else _bridgeTools.Refresh();
                if (!_vm.IsSpawnTool) Map.SpawnGhost = null;
                // The trader starts facing north, as a new pick in the browser does.
                if (_vm.IsTraderTool && _vm.SelectedTrader is { } trader) _vm.PrefabRotation = trader.RotationToFaceNorth & 3;
                _prefabTools.Refresh();
                _multiSelect.Refresh();
                _townTools.Refresh();
                _genTools.Refresh();
                UpdateToolState();
                break;
            case nameof(MainViewModel.SidePanelTab) or nameof(MainViewModel.ShowSidePanel):
                _genTools.Refresh();
                break;
            case nameof(MainViewModel.Favourites):
                SaveFavourites();
                break;
            case nameof(MainViewModel.PlacedMatches):
                Map.SearchMatches = _vm.PlacedMatches;
                break;
            case nameof(MainViewModel.SelectedTrader):
            case nameof(MainViewModel.SelectedPrefabInfo):
            case nameof(MainViewModel.PrefabRotation):
                _prefabTools.UpdateGhost();
                UpdateToolState();
                break;
            case nameof(MainViewModel.BridgeDeck):
                _bridgeTools.DeckEdited();
                break;
            case nameof(MainViewModel.RoadType):
            case nameof(MainViewModel.RoadWidth):
                _tools.RefreshRoadPreview();
                UpdateToolState();
                break;
            case nameof(MainViewModel.BrushRadius):
            case nameof(MainViewModel.StampSize):
            case nameof(MainViewModel.StampRotation):
            case nameof(MainViewModel.SelectedStamp):
            case nameof(MainViewModel.BrushShape):
            case nameof(MainViewModel.BrushRotation):
                UpdateToolState();
                break;
        }
    }

    private void UpdateToolState()
    {
        _vm.SelectionSizeText = _vm.HasSize ? _vm.SizeText : "";
        Map.ToolActive = _vm.IsLandMode || _vm.SelectedTool != EditorTool.Select;
        // With Select, a press on something that can be picked grabs it; anywhere else it pans.
        Map.GrabTest = _vm.IsSelectTool ? _prefabTools.CanGrab : null;
        Map.ShowLots = _vm.IsPrefabTool;
        Map.RoadSnapper = _vm.IsRoadTool ? _tools.SnapCursor : null;
        if (!_vm.IsPrefabTool) Map.HoverPrefab = null;
        Map.BrushCursor = _vm.SelectedTool switch
        {
            EditorTool.Select or EditorTool.MultiSelect or EditorTool.Water or EditorTool.Prefab or EditorTool.Trader or EditorTool.Spawn or EditorTool.Town or EditorTool.Bridge
                or EditorTool.GenerateArea or EditorTool.Features => null,
            EditorTool.Road => new BrushCursor(_vm.RoadWidth),
            EditorTool.Stamp => new BrushCursor(_vm.StampSize, Square: true, _vm.StampRotation,
                _vm.SelectedStamp is { } stamp ? StampPicture.Get(_vm.Stamps, stamp, UpdateToolState) : null),
            _ => new BrushCursor(_vm.BrushRadius * 2, _vm.IsSquareBrush, _vm.BrushRotation),
        };
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (_vm.IsLandMode && HandleLandKey(e)) return;
        if (AltTurnsOffSnapping(e)) return;
        // Leave typing in text boxes alone.
        if (e.Handled || Keyboard.FocusedElement is TextBox || Keyboard.Modifiers != ModifierKeys.None) return;
        if (e.Key != Key.F1 && RefuseWhileBusy())
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            // An open list or menu closes itself with Esc.
            if (Keyboard.FocusedElement is ComboBoxItem or MenuItem or ComboBox { IsDropDownOpen: true }) return;
            Escape();
            e.Handled = true;
            return;
        }
        if (_vm.IsTownTool && e.Key == Key.Enter && _townTools.Accept()) { e.Handled = true; return; }
        if (_vm.IsBridgeTool)
        {
            if (e.Key == Key.Enter && _bridgeTools.Build()) { e.Handled = true; return; }
            if (e.Key == Key.Delete && _bridgeTools.DeleteSelected()) { e.Handled = true; return; }
        }
        if (_vm.IsMultiSelectTool && e.Key is Key.Delete or Key.PageUp or Key.PageDown)
        {
            if (e.Key == Key.Delete) _multiSelect.Delete();
            else _multiSelect.NudgeHeight(e.Key == Key.PageUp ? 1 : -1);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Delete && (_vm.IsPrefabTool || _vm.IsSpawnTool || _vm.IsSelectTool)) { _prefabTools.DeleteSelected(); e.Handled = true; return; }
        if (e.Key is Key.PageUp or Key.PageDown && (_vm.IsPrefabTool || _vm.IsSelectTool))
        {
            _prefabTools.NudgeHeight(e.Key == Key.PageUp ? 1 : -1);
            e.Handled = true;
            return;
        }

        if (_tools.IsDrawingRoad)
        {
            switch (e.Key)
            {
                case Key.Enter: _tools.FinishRoad(); e.Handled = true; return;
                case Key.Back: _tools.UndoRoadPoint(); e.Handled = true; return;
            }
        }

        var tool = _vm.Tools.FirstOrDefault(t => t.Shortcut == e.Key.ToString());
        if (tool is not null)
        {
            _vm.SelectedTool = tool.Tool;
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.OemOpenBrackets: Resize(1 / 1.2); break;
            case Key.OemCloseBrackets: Resize(1.2); break;
            case Key.Q: Rotate(-15); break;
            case Key.E: Rotate(15); break;
            case Key.F12: SaveSnapshot(); break;
            case Key.F1: ShowShortcuts(); break;
            default: return;
        }
        e.Handled = true;
    }

    /// <summary>
    /// Esc, as in CAD programs (#35), one step at a time. In any other tool a first Esc stops what that tool is doing (a
    /// road, a bridge or a town being drawn, a pick in the browser, its selection) and stays in the tool; an Esc with
    /// nothing to stop goes back to Select and clears the selection. In Select it steps back one thing: the browser's
    /// replacement pick first, then the selection.
    /// </summary>
    private void Escape()
    {
        if (_vm.IsSelectTool)
        {
            _prefabTools.Cancel();
            return;
        }
        // Only the active tool's own work counts, so something left selected in another tool doesn't take an extra Esc.
        bool stopped = _vm.IsRoadTool && _tools.CancelRoad()
                       || _vm.IsBridgeTool && _bridgeTools.Cancel()
                       || _vm.IsTownTool && _townTools.Cancel()
                       || _vm.IsMultiSelectTool && _multiSelect.Cancel()
                       || (_vm.IsPrefabTool || _vm.IsSpawnTool) && _prefabTools.Cancel();
        if (stopped) return;
        _vm.SelectedPrefabInfo = null;
        _vm.SelectedTool = EditorTool.Select;
        while (_prefabTools.Cancel()) { }
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        if (_vm.IsLandMode && e.SystemKey is Key.LeftAlt or Key.RightAlt)
        {
            e.Handled = true;
            return;
        }
        AltTurnsOffSnapping(e);
    }

    /// <summary>
    /// With the road, prefab, trader and town tools, Alt is held to place without snapping, so it redraws the preview and
    /// doesn't open the menu.
    /// </summary>
    private bool AltTurnsOffSnapping(KeyEventArgs e)
    {
        bool draggingPrefab = _prefabTools.IsDragging;
        if (!(_vm.IsRoadTool || _vm.IsPrefabTool || _vm.IsTownTool || draggingPrefab) || e.Key != Key.System
            || e.SystemKey is not (Key.LeftAlt or Key.RightAlt)) return false;
        if (_vm.IsPrefabTool || draggingPrefab) _prefabTools.SnappingChanged();
        Map.InvalidateVisual();
        e.Handled = true;
        return true;
    }

    private void Rotate(double degrees)
    {
        // Prefabs turn a quarter at a time; positive quarter turns are counter-clockwise, so Q (-) gives +1.
        if (_vm.IsPrefabTool) _prefabTools.Rotate(degrees < 0 ? 1 : -1);
        else if (_vm.IsSpawnTool) _prefabTools.RotateSpawn(degrees < 0 ? -45 : 45);
        else if (_vm.IsSelectTool && _prefabTools.HasSpawnSelected) _prefabTools.RotateSpawn(degrees < 0 ? -45 : 45);
        else if (_vm.IsSelectTool) _prefabTools.Rotate(degrees < 0 ? 1 : -1);
        else if (_vm.IsMultiSelectTool) _multiSelect.Rotate(degrees < 0 ? 1 : -1);
        else if (_vm.SelectedTool == EditorTool.Stamp) _vm.StampRotation = (_vm.StampRotation + degrees + 360) % 360;
        else if (_vm.HasRadius && _vm.IsSquareBrush) _vm.BrushRotation = (_vm.BrushRotation + degrees + 360) % 360;
    }

    private void Resize(double factor)
    {
        if (_vm.SelectedTool == EditorTool.Stamp) _vm.StampSize = Math.Clamp(_vm.StampSize * factor, 32, 4000);
        else _vm.BrushRadius = Math.Clamp(_vm.BrushRadius * factor, 2, 800);
    }

    /// <summary>
    /// The map's right-click menu at map point <paramref name="map"/>. Build every item here, so the menu stays one
    /// list. Right drags still pan; MapView only asks for the menu when the click didn't move.
    /// </summary>
    private void ShowMapMenu(Point map)
    {
        if (_world is not { } world || _vm.IsBusy) return;
        int x = (int)Math.Floor(map.X);
        int z = world.Size - 1 - (int)Math.Floor(map.Y);
        if (TeleportCommand.CoordinatesForCell(world, x, z) is not { } target) return;

        var menu = new ContextMenu { PlacementTarget = Map, Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };

        var teleportHere = new MenuItem { Header = "Teleport Here" };
        teleportHere.Click += async (_, _) => await RunGameLinkActionAsync(world, target.X, target.Z);
        menu.Items.Add(teleportHere);

        var teleport = new MenuItem { Header = "Copy _teleport command" };
        foreach (var (header, facing) in new (string, Facing?)[]
                 {
                     ("Facing _north", Facing.N), ("Facing _east", Facing.E), ("Facing _south", Facing.S), ("Facing _west", Facing.W),
                     ("No _facing", null),
                 })
        {
            string command = TeleportCommand.ForCell(world, x, z, facing)!;
            var item = new MenuItem { Header = header, InputGestureText = command };
            item.Click += (_, _) => CopyToClipboard(command, "Copied: " + command + ". Paste it into the game's console (F1).");
            teleport.Items.Add(item);
        }
        menu.Items.Add(teleport);

        if (GameLinkWorld.WaypointAt(world, target.X, target.Z, _vm.Prefabs) is { } waypoint)
        {
            var addWaypoint = new MenuItem { Header = new TextBlock { Text = $"Add Waypoint: {waypoint.Name}" } };
            addWaypoint.Click += async (_, _) =>
                await RunGameLinkActionAsync(world, waypoint.X, waypoint.Z, waypointPrefab: waypoint.Name);
            menu.Items.Add(addWaypoint);
        }

        menu.IsOpen = true;
    }

    private void CopyToClipboard(string text, string status)
    {
        try
        {
            Clipboard.SetText(text);
            _vm.Status = status;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            _vm.Status = "Couldn't copy: another program is using the clipboard. Try again.";
        }
    }

    private void Map_CursorMoved(object? sender, Point? map)
    {
        if (_vm.IsLandMode) _landTools.UpdateCursor(map);
        if (map is not { } m || _world is not { } world)
        {
            _vm.CursorText = "";
            Map.HoverPrefab = null;
            return;
        }
        int x = (int)Math.Floor(m.X);
        int z = world.Size - 1 - (int)Math.Floor(m.Y);
        if (_vm.IsLandMode)
        {
            _vm.CursorText = (uint)x < world.Size && (uint)z < world.Size ? $"X {x - world.Size / 2}, Z {z - world.Size / 2}" : "";
            return;
        }
        var info = WorldQuery.At(world, x, z, _vm.Biomes, _vm.Prefabs);
        _vm.CursorText = info is null ? "" : MainViewModel.FormatPoint(info);
        Map.HoverPrefab = _vm.ShowPrefabs && (_vm.SelectedTool == EditorTool.Select || _vm.IsPrefabTool && _vm.PlacingPrefab is null)
            ? info?.Prefab : null;
    }

    private bool _syncingZoom;

    /// <summary>Zoom is shown as a percentage: 100% is one screen pixel (DIP) per metre.</summary>
    private void UpdateZoomText()
    {
        if (Map.Renderer is null)
        {
            _vm.ZoomPercentText = "";
            return;
        }
        double percent = Map.Scale * 100;
        _vm.ZoomPercentText = percent >= 10 ? $"{percent:0}%" : $"{percent:0.#}%";
        _syncingZoom = true;
        _vm.ZoomLog = Math.Log2(Map.Scale);
        _syncingZoom = false;
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingZoom || Map.Renderer is null) return;
        Map.ZoomTo(Math.Pow(2, e.NewValue));
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => Map.ZoomBy(1.25);

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => Map.ZoomBy(1 / 1.25);

    private void ZoomMenu_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = ZoomMenuButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
        var fit = new MenuItem { Header = "Fit to window" };
        fit.Click += (_, _) => Map.FitToView();
        menu.Items.Add(fit);
        menu.Items.Add(new Separator());
        foreach (double percent in new double[] { 1600, 800, 400, 200, 100, 50, 25, 10, 5, 2 })
        {
            var item = new MenuItem { Header = $"{percent}%", IsChecked = Math.Abs(Map.Scale * 100 - percent) < 0.5 };
            item.Click += (_, _) => Map.ZoomTo(percent / 100);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void Fit_Click(object sender, RoutedEventArgs e) => Map.FitToView();


    // ---- prefab info card --------------------------------------------------

    private void TownReroll_Click(object sender, RoutedEventArgs e) => WithMapFocus(_townTools.Reroll);

    private void TraderRandom_Click(object sender, RoutedEventArgs e) => WithMapFocus(_vm.PickRandomTrader);
    private void TownBuild_Click(object sender, RoutedEventArgs e) => WithMapFocus(() => _townTools.Accept());
    private void TownCancel_Click(object sender, RoutedEventArgs e) => WithMapFocus(() => _townTools.Cancel());

    private void BridgeBuild_Click(object sender, RoutedEventArgs e) => WithMapFocus(() => _bridgeTools.Build());
    private void BridgeCancel_Click(object sender, RoutedEventArgs e) => WithMapFocus(() => _bridgeTools.Cancel());
    private void BridgeDelete_Click(object sender, RoutedEventArgs e) => WithMapFocus(() => _bridgeTools.DeleteSelected());
    private void BridgeRebuildAll_Click(object sender, RoutedEventArgs e) => WithMapFocus(() => _bridgeTools.RebuildAll());

    /// <summary>Enter in the deck height box applies it and hands the keyboard back to the map.</summary>
    private void BridgeDeck_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        (sender as TextBox)?.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        WithMapFocus(() => { });
        e.Handled = true;
    }

    private void PrefabCardClose_Click(object sender, RoutedEventArgs e) => _prefabTools.Deselect();
    private void PrefabCardBack_Click(object sender, RoutedEventArgs e) => _vm.CardShowsPick = false;

    private void FavouriteRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PrefabInfo p) _vm.ToggleFavourite(p);
    }

    private void CardFavourite_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.CardPrefab is { } p) _vm.ToggleFavourite(p);
    }

    /// <summary>Saves favourites as soon as they change, so they survive a crash.</summary>
    private void SaveFavourites()
    {
        var names = _vm.Favourites.Order(StringComparer.OrdinalIgnoreCase).ToList();
        if (names.SequenceEqual(_settings.FavouritePrefabs, StringComparer.OrdinalIgnoreCase)) return;
        _settings.FavouritePrefabs = names;
        if (!_settings.Save()) _vm.Status = "Couldn't save favourites; they'll be lost when the editor closes. Details are in crash.log.";
    }
    private void PrefabTurnLeft_Click(object sender, RoutedEventArgs e) => WithMapFocus(() => _prefabTools.Rotate(1));
    private void PrefabTurnRight_Click(object sender, RoutedEventArgs e) => WithMapFocus(() => _prefabTools.Rotate(-1));
    private void PrefabRaise_Click(object sender, RoutedEventArgs e) => WithMapFocus(() => _prefabTools.NudgeHeight(1));
    private void PrefabLower_Click(object sender, RoutedEventArgs e) => WithMapFocus(() => _prefabTools.NudgeHeight(-1));
    private void PrefabReplace_Click(object sender, RoutedEventArgs e) => WithMapFocus(_prefabTools.ReplaceSelected);
    private void PrefabDelete_Click(object sender, RoutedEventArgs e) => WithMapFocus(_prefabTools.DeleteSelected);

    /// <summary>Card buttons act on the map selection even while a prefab is picked for placing.</summary>
    private void WithMapFocus(Action action)
    {
        if (RefuseWhileBusy()) return;
        action();
        Map.Focus();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Refills File > Open Recent each time the File menu opens.</summary>
    private void FileMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource != sender) return;
        RecentMenu.Items.Clear();
        for (int i = 0; i < _settings.RecentFiles.Count; i++)
        {
            string path = _settings.RecentFiles[i];
            bool isProject = path.EndsWith(ProjectStore.Extension, StringComparison.OrdinalIgnoreCase);
            string name = isProject ? Path.GetFileNameWithoutExtension(path) : Path.GetFileName(path.TrimEnd('\\', '/')) + " (world folder)";
            // Underscores in names are doubled so they aren't taken as access keys.
            string accessKey = i < 9 ? $"_{i + 1} " : "";
            var item = new MenuItem { Header = accessKey + name.Replace("_", "__"), ToolTip = path, Tag = path };
            item.Click += RecentItem_Click;
            RecentMenu.Items.Add(item);
        }
        if (RecentMenu.Items.Count > 0)
        {
            RecentMenu.Items.Add(new Separator());
            var clear = new MenuItem { Header = "_Clear Recent List" };
            clear.Click += (_, _) =>
            {
                _settings.RecentFiles.Clear();
                _settings.Save();
            };
            RecentMenu.Items.Add(clear);
        }
        RecentMenu.IsEnabled = RecentMenu.Items.Count > 0;
    }

    private async void RecentItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string path } || _vm.IsBusy || _opening) return;
        bool isProject = path.EndsWith(ProjectStore.Extension, StringComparison.OrdinalIgnoreCase);
        if (isProject ? !File.Exists(path) : !Directory.Exists(path))
        {
            if (ThemedMessageBox.Show(this, $"{path}\n\nThis can't be found. It may have been moved, renamed or deleted. Remove it from the list?",
                    "Open Recent", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                _settings.RecentFiles.Remove(path);
                _settings.Save();
            }
            return;
        }
        if (!await ConfirmDiscardAsync()) return;
        if (isProject) await OpenProjectAsync(path);
        else await OpenWorldAsync(path);
    }

    /// <summary>F12 saves what the window draws to %TEMP%\mapeditor-snapshot.png (a debugging aid).</summary>
    private void SaveSnapshot()
    {
        double dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var content = (FrameworkElement)Content;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)(content.ActualWidth * dpi), (int)(content.ActualHeight * dpi), 96 * dpi, 96 * dpi, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        string path = Path.Combine(Path.GetTempPath(), "mapeditor-snapshot.png");
        using (var file = File.Create(path)) encoder.Save(file);
        _vm.Status = "Snapshot saved to " + path;
    }
}
