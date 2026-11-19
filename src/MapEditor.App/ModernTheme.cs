using System.Collections;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MapEditor.Core.Theming;
using Microsoft.Win32;

namespace MapEditor.App;

/// <summary>
/// Puts the Modern theme's colours over WPF's Fluent theme. The Flat theme never comes here, so it stays pure Fluent.
/// </summary>
internal static class ModernTheme
{
    private const string FluentAssembly = "PresentationFramework.Fluent";
    private const string WindowBrushKey = "SolidBackgroundFillColorBaseBrush";
    private const int DwmCaptionColor = 35;

    private static ResourceDictionary? _colours;
    private static DispatcherTimer? _refresh;
    private static bool _dark;

    /// <summary>Applies the colours and keeps them matched to the Windows light or dark setting.</summary>
    public static void Start()
    {
        Apply();
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => Paint((Window)sender)));

        // Windows tells WPF about the change at about the same time, so wait for Fluent to swap its colours first.
        _refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _refresh.Tick += (_, _) =>
        {
            _refresh.Stop();
            Apply();
            foreach (Window window in Application.Current.Windows) Paint(window);
        };
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    _refresh.Stop();
                    _refresh.Start();
                });
        };
    }

    private static void Apply()
    {
        var merged = Application.Current.Resources.MergedDictionaries;
        if (_colours is not null) merged.Remove(_colours);
        _colours = null;

        // High contrast belongs to Windows: leave those colours alone.
        var fluent = merged.FirstOrDefault(d => d.Source?.OriginalString.Contains(FluentAssembly, StringComparison.Ordinal) == true);
        if (fluent is null || SystemParameters.HighContrast) return;

        var brushes = new Dictionary<string, Brush>();
        Collect(fluent, brushes);
        // Fluent's file name doesn't say the mode when it follows Windows, but its text colour does.
        _dark = brushes.GetValueOrDefault("TextFillColorPrimaryBrush") is SolidColorBrush { Color.R: > 128 };

        var accents = new HashSet<uint>();
        foreach (string key in ModernPalette.AccentKeys)
            if (brushes.GetValueOrDefault(key) is SolidColorBrush accent) accents.Add(ToArgb(accent.Color));

        _colours = new ResourceDictionary();
        foreach (var (key, brush) in brushes)
            if (Convert(key, brush, _dark, accents) is { } mapped) _colours[key] = mapped;
        merged.Add(_colours);
    }

    /// <summary>Gives a window a solid background and a matching title bar, in place of the Windows backdrop.</summary>
    private static void Paint(Window window)
    {
        if (_colours is null) return;
        window.SetResourceReference(Control.BackgroundProperty, WindowBrushKey);

        nint handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return;
        uint surface = (_dark ? ModernPalette.Dark : ModernPalette.Light).Surface;
        // Windows wants 0x00BBGGRR. Windows 10 doesn't have this setting and ignores it.
        int colour = (int)((surface & 0xFF) << 16 | surface & 0xFF00 | surface >> 16 & 0xFF);
        _ = DwmSetWindowAttribute(handle, DwmCaptionColor, ref colour, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

    private static void Collect(ResourceDictionary dictionary, Dictionary<string, Brush> brushes)
    {
        foreach (var inner in dictionary.MergedDictionaries) Collect(inner, brushes);
        foreach (DictionaryEntry entry in dictionary)
            if (entry.Key is string key && entry.Value is Brush brush) brushes[key] = brush;
    }

    private static Brush? Convert(string key, Brush brush, bool dark, HashSet<uint> accents)
    {
        Color Map(Color colour) => FromArgb(ModernPalette.Map(key, ToArgb(colour), dark, accents));

        switch (brush)
        {
            case SolidColorBrush solid:
                var colour = Map(solid.Color);
                if (colour == solid.Color) return null;
                var mapped = new SolidColorBrush(colour) { Opacity = solid.Opacity };
                mapped.Freeze();
                return mapped;
            case LinearGradientBrush gradient:
                var copy = gradient.Clone();
                foreach (var stop in copy.GradientStops) stop.Color = Map(stop.Color);
                copy.Freeze();
                return copy;
            default:
                return null;
        }
    }

    private static uint ToArgb(Color c) => (uint)c.A << 24 | (uint)c.R << 16 | (uint)c.G << 8 | c.B;

    private static Color FromArgb(uint v) => Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
}
