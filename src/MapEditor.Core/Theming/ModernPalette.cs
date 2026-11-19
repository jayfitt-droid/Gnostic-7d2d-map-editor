namespace MapEditor.Core.Theming;

/// <summary>One colour set of the Modern theme. Every value is 0xAARRGGBB.</summary>
public sealed record ModernColours
{
    public required uint Window { get; init; }
    public required uint Surface { get; init; }
    public required uint Popup { get; init; }
    public required uint Divider { get; init; }
    public required uint ControlBorder { get; init; }
    public required uint Text { get; init; }
    public required uint Strong { get; init; }
    public required uint Muted { get; init; }
    public required uint Faint { get; init; }
    public required uint Input { get; init; }
    public required uint Button { get; init; }
    public required uint ButtonHover { get; init; }
    public required uint ButtonPressed { get; init; }
    public required uint Accent { get; init; }
    public required uint AccentHover { get; init; }
    public required uint AccentPressed { get; init; }
    public required uint AccentText { get; init; }
    public required uint RowSelected { get; init; }
    public required uint Error { get; init; }
    public required uint Warning { get; init; }
    public required uint Success { get; init; }
}

/// <summary>
/// Turns the colours of WPF's Fluent theme into the Modern theme's. Fluent has about 500 named brushes, most of them
/// for one state of one control, so they are mapped by rule from each brush's name and colour.
/// </summary>
public static class ModernPalette
{
    public static readonly ModernColours Dark = new()
    {
        Window = 0xFF1F1F1F, Surface = 0xFF181818, Popup = 0xFF202020,
        Divider = 0xFF2B2B2B, ControlBorder = 0xFF3C3C3C,
        Text = 0xFFCCCCCC, Strong = 0xFFE7E7E7, Muted = 0xFF9D9D9D, Faint = 0xFF8B8B8B,
        Input = 0xFF313131, Button = 0xFF313131, ButtonHover = 0xFF3C3C3C, ButtonPressed = 0xFF2A2A2A,
        Accent = 0xFF0078D4, AccentHover = 0xFF026EC1, AccentPressed = 0xFF0366B3, AccentText = 0xFF4DAAFC,
        RowSelected = 0xFF04395E,
        Error = 0xFFF14C4C, Warning = 0xFFCCA700, Success = 0xFF89D185,
    };

    public static readonly ModernColours Light = new()
    {
        Window = 0xFFFFFFFF, Surface = 0xFFF8F8F8, Popup = 0xFFFFFFFF,
        Divider = 0xFFE5E5E5, ControlBorder = 0xFFCECECE,
        Text = 0xFF3B3B3B, Strong = 0xFF1F1F1F, Muted = 0xFF6E6E6E, Faint = 0xFF707070,
        Input = 0xFFFFFFFF, Button = 0xFFE5E5E5, ButtonHover = 0xFFDADADA, ButtonPressed = 0xFFCECECE,
        Accent = 0xFF005FB8, AccentHover = 0xFF0258A8, AccentPressed = 0xFF024F96, AccentText = 0xFF005FB8,
        RowSelected = 0xFFCCE4F7,
        Error = 0xFFC72E0F, Warning = 0xFF895503, Success = 0xFF2E7D32,
    };

    /// <summary>Fluent's brushes that hold the Windows accent colour. Their colours identify the accent family.</summary>
    public static readonly IReadOnlyList<string> AccentKeys =
    [
        "AccentFillColorDefaultBrush", "AccentFillColorSecondaryBrush", "AccentFillColorTertiaryBrush",
        "AccentFillColorSelectedTextBackgroundBrush", "AccentTextFillColorPrimaryBrush",
        "AccentTextFillColorSecondaryBrush", "AccentTextFillColorTertiaryBrush",
    ];

    /// <summary>Gives the Modern colour for one Fluent brush.</summary>
    /// <param name="key">The brush's resource name.</param>
    /// <param name="argb">The brush's Fluent colour.</param>
    /// <param name="dark">Whether Fluent is in its dark mode.</param>
    /// <param name="accents">The colours of <see cref="AccentKeys"/> in the current Fluent theme.</param>
    public static uint Map(string key, uint argb, bool dark, IReadOnlySet<uint> accents)
    {
        var c = dark ? Dark : Light;
        uint alpha = argb >> 24;
        if (alpha == 0 || IsFixed(key) || key.Contains("Disabled")) return argb;

        if (key == "DataGridRowSelectedForegroundThemeBrush") return c.Strong;
        if (IsOnAccent(key)) return alpha << 24 | 0xFFFFFF;
        if (StatusColour(key, c) is { } status) return status;
        if (accents.Contains(argb)) return MapAccent(key, c);

        if (IsTextKey(key)) return MapText(argb, dark, c);
        if (IsStrokeKey(key)) return MapStroke(key, argb, alpha, c);
        return MapFill(key, argb, dark, c);
    }

    /// <summary>The WCAG contrast ratio of two opaque colours, from 1 to 21.</summary>
    public static double Contrast(uint a, uint b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(uint argb)
    {
        static double Channel(uint v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(argb >> 16 & 0xFF) + 0.7152 * Channel(argb >> 8 & 0xFF) + 0.0722 * Channel(argb & 0xFF);
    }

    /// <summary>Brushes that must keep their colour: high contrast, overlays on pictures, focus rings and always-light or always-dark text.</summary>
    private static bool IsFixed(string key) =>
        key.StartsWith("SystemColor", StringComparison.Ordinal)
        || key.StartsWith("TextFillColorDark", StringComparison.Ordinal)
        || key.StartsWith("TextFillColorLight", StringComparison.Ordinal)
        || key.StartsWith("TextFillColorInverse", StringComparison.Ordinal)
        || key.StartsWith("ApplicationBackgroundColor", StringComparison.Ordinal)
        || key.StartsWith("ControlOnImage", StringComparison.Ordinal)
        || key.StartsWith("ControlStrokeColorOnAccent", StringComparison.Ordinal)
        || key.StartsWith("ControlStrokeColorForStrongFill", StringComparison.Ordinal)
        || key.StartsWith("ControlStrongFillColor", StringComparison.Ordinal)
        || key.StartsWith("ControlStrongStrokeColor", StringComparison.Ordinal)
        || key.StartsWith("FocusStroke", StringComparison.Ordinal)
        || key.StartsWith("KeyboardFocus", StringComparison.Ordinal)
        || key.StartsWith("LayerOn", StringComparison.Ordinal)
        || key.Contains("Smoke");

    /// <summary>Text and glyphs drawn on an accent fill. The Modern accents are dark enough for white in both modes.</summary>
    private static bool IsOnAccent(string key) =>
        key.StartsWith("AccentButtonForeground", StringComparison.Ordinal)
        || key.StartsWith("TextOnAccent", StringComparison.Ordinal)
        || key.StartsWith("CheckBoxCheckGlyphForeground", StringComparison.Ordinal)
        || key.StartsWith("RadioButtonCheckGlyphFill", StringComparison.Ordinal)
        || key.StartsWith("ToggleButtonForegroundChecked", StringComparison.Ordinal)
        || key.StartsWith("ToggleSwitchKnobFillOn", StringComparison.Ordinal)
        || key == "CalendarViewTodayForeground";

    private static uint? StatusColour(string key, ModernColours c)
    {
        // The pale fills behind a message stay as they are; the icon's own background is the status colour.
        if (key.Contains("Background") && !key.Contains("IconBackground")) return null;
        if (key.Contains("Critical") || key.Contains("InfoBarError")) return c.Error;
        if (key.Contains("Caution") || key.Contains("InfoBarWarning")) return c.Warning;
        if (key.Contains("Success")) return c.Success;
        return null;
    }

    private static uint MapAccent(string key, ModernColours c)
    {
        if (key.Contains("Selected") && (key.Contains("Background") || key.Contains("Fill"))) return c.RowSelected;
        if (IsTextKey(key) || key.Contains("Hyperlink")) return c.AccentText;
        if (key.Contains("PointerOver")) return c.AccentHover;
        if (key.Contains("Pressed")) return c.AccentPressed;
        return c.Accent;
    }

    private static bool IsTextKey(string key) => key.Contains("Foreground") || key.Contains("TextFill");

    private static uint MapText(uint argb, bool dark, ModernColours c) => (dark, argb) switch
    {
        (true, 0xFFFFFFFF) or (false, 0xE4000000) => c.Text,
        (true, 0xC5FFFFFF) or (false, 0x9E000000) => c.Muted,
        (true, 0x87FFFFFF) or (false, 0x72000000) => c.Faint,
        _ => argb,
    };

    private static bool IsStrokeKey(string key) =>
        key.Contains("Stroke") || key.Contains("Border") || key.Contains("Divider") || key.Contains("Separator");

    private static uint MapStroke(string key, uint argb, uint alpha, ModernColours c)
    {
        // A strong outline, such as an unticked check box's, has to stand out from every surface.
        if (alpha >= 0x70 && (argb & 0xFFFFFF) is 0xFFFFFF or 0) return argb;
        bool quiet = key.Contains("Divider") || key.Contains("Separator") || key.Contains("Card") || key.Contains("Surface")
            || key.Contains("Flyout") || key.Contains("ContextMenu") || key.Contains("DropDown") || key.Contains("Dialog")
            || key.Contains("Menu");
        return quiet ? c.Divider : c.ControlBorder;
    }

    private static uint MapFill(string key, uint argb, bool dark, ModernColours c)
    {
        if (key is "ApplicationBackgroundBrush" or "WindowBackground" or "NavigationWindowBackground"
            or "LoadingScreenBackground" or "SolidBackgroundFillColorSecondaryBrush")
            return c.Window;
        if (key is "SolidBackgroundFillColorBaseBrush" or "SolidBackgroundFillColorTertiaryBrush"
            or "MessageBoxBackground" or "ContentDialogBackground")
            return c.Surface;
        if (key == "SolidBackgroundFillColorQuarternaryBrush" || argb == (dark ? 0xFF2C2C2C : 0xFFF9F9F9)) return c.Popup;

        if (key.StartsWith("TextControlBackground", StringComparison.Ordinal) || key == "ControlFillColorInputActiveBrush")
            return c.Input;

        bool buttonLike = key.StartsWith("ButtonBackground", StringComparison.Ordinal)
            || key.StartsWith("ComboBoxBackground", StringComparison.Ordinal)
            || key.StartsWith("RepeatButtonBackground", StringComparison.Ordinal)
            || key.StartsWith("ToggleButtonBackground", StringComparison.Ordinal)
            || key is "ControlFillColorDefaultBrush" or "ControlFillColorSecondaryBrush" or "ControlFillColorTertiaryBrush";
        if (!buttonLike) return argb;
        if (key.Contains("PointerOver") || key == "ControlFillColorSecondaryBrush") return c.ButtonHover;
        if (key.Contains("Pressed") || key == "ControlFillColorTertiaryBrush") return c.ButtonPressed;
        return c.Button;
    }
}
