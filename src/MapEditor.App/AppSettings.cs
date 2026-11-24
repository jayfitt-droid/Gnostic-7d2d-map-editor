using System.IO;
using System.Text.Json;
using MapEditor.Core.GameData;
using MapEditor.Core.Generate;

namespace MapEditor.App;

/// <summary>Per-user settings, stored as JSON in %APPDATA%\7D2DMapEditor.</summary>
public sealed class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "7D2DMapEditor", "settings.json");

    public string? GameFolder { get; set; }
    public string? LastWorldFolder { get; set; }
    public bool OpenInGameAfterExport { get; set; }
    public bool Autosave { get; set; } = true;

    /// <summary>The side panel's width (DIPs); 0 for the default.</summary>
    public double SidePanelWidth { get; set; }

    /// <summary>Whether the side panel was open when the editor last closed. Shown on a first run.</summary>
    public bool ShowSidePanel { get; set; } = true;

    /// <summary>In the Modern theme, whether the side bar is on the right of the map. The Flat theme always has it there.</summary>
    public bool SideBarOnRight { get; set; }

    /// <summary>Whether View > Mini Map is on.</summary>
    public bool ShowMiniMap { get; set; } = true;

    /// <summary>Projects (.7dmap) and world folders opened or saved lately, newest first.</summary>
    public List<string> RecentFiles { get; set; } = [];

    public const int MaxRecentFiles = 10;

    /// <summary>Prefab names starred in the browser, kept by name so they carry across worlds and game updates.</summary>
    public List<string> FavouritePrefabs { get; set; } = [];

    /// <summary>Puts a project or world folder at the top of <see cref="RecentFiles"/>.</summary>
    public void AddRecent(string path)
    {
        path = Path.GetFullPath(path);
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > MaxRecentFiles) RecentFiles.RemoveRange(MaxRecentFiles, RecentFiles.Count - MaxRecentFiles);
    }

    /// <summary>The ribbon's size: automatic, full or compact (View > Ribbon).</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<RibbonMode>))]
    public RibbonMode RibbonMode { get; set; } = RibbonMode.Auto;

    /// <summary>The interface theme: "Flat" or "Modern". Text, so a value this version doesn't know can't reset the other settings.</summary>
    public string Theme { get; set; } = FlatTheme;

    public const string FlatTheme = "Flat", ModernTheme = "Modern";

    /// <summary>
    /// Whether this run uses the Modern theme. The GNOSTIC_THEME environment variable overrides the saved setting for
    /// one run, for testing.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool UsesModernTheme =>
        string.Equals(Environment.GetEnvironmentVariable("GNOSTIC_THEME") ?? Theme, ModernTheme, StringComparison.OrdinalIgnoreCase);

    /// <summary>Generator presets saved from the Generate panel ("Game defaults" isn't stored).</summary>
    public List<MapEditor.Core.Generate.GeneratorPreset> GeneratorPresets { get; set; } = [];

    /// <summary>Town presets saved from the Town sites card.</summary>
    public List<TownPreset> TownPresets { get; set; } = [];

    /// <summary>Names of the starter town presets already offered, so a deleted starter doesn't come back.</summary>
    public List<string> OfferedTownPresets { get; set; } = [];

    /// <summary>Checks for updates when the editor starts.</summary>
    public bool CheckForUpdatesAtStart { get; set; } = true;

    /// <summary>Overrides the release feed for update checks (see Updates).</summary>
    public string? UpdateUrl { get; set; }

    /// <summary>The main window's last size (DIPs) and whether it was maximized.</summary>
    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    /// <summary>The main window's normal position in desktop pixels; null before a position has been saved.</summary>
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }

    public static AppSettings Load()
    {
        AppSettings settings;
        try
        {
            settings = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            settings = new AppSettings();
        }

        StarterTownPresets.AddNew(settings.TownPresets, settings.OfferedTownPresets);

        if (settings.GameFolder is null || !GameInstall.IsValid(settings.GameFolder))
            settings.GameFolder = GameInstall.FindCandidates().FirstOrDefault();
        // Settings from before the recent list still know the last world.
        if (settings.RecentFiles.Count == 0 && settings.LastWorldFolder is { } last && (File.Exists(last) || Directory.Exists(last)))
            settings.AddRecent(last);
        return settings;
    }

    /// <summary>
    /// Writes the settings to a temporary file and moves it into place, so a crash mid-write can't leave a broken file
    /// (which <see cref="Load"/> would replace with defaults). A failure is logged, not thrown: it must never turn a
    /// save, export or open that worked into a reported failure (#18). Returns whether the settings were written.
    /// </summary>
    public bool Save()
    {
        string temp = FilePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, FilePath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.LogError(ex);
            try { File.Delete(temp); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            return false;
        }
    }
}
