using MapEditor.Core.Theming;

namespace MapEditor.Core.Tests;

public sealed class ModernPaletteTests
{
    private const uint WindowsAccent = 0xFF9FC6D5;
    private static readonly HashSet<uint> Accents = [WindowsAccent, 0xFFC1E7ED, 0xFF4E789C];

    private static uint Dark(string key, uint argb) => ModernPalette.Map(key, argb, dark: true, Accents);
    private static uint Light(string key, uint argb) => ModernPalette.Map(key, argb, dark: false, Accents);

    public static TheoryData<bool> Modes => new() { true, false };

    [Theory]
    [InlineData("ButtonBackground", 0x00FFFFFFu)]
    [InlineData("SystemColorHighlightColorBrush", 0xFFFF00FFu)]
    [InlineData("ButtonForegroundDisabled", 0x5DFFFFFFu)]
    [InlineData("TextFillColorDarkPrimaryBrush", 0xE4000000u)]
    [InlineData("ControlStrongStrokeColorDefaultBrush", 0x8BFFFFFFu)]
    public void Leaves_transparent_high_contrast_disabled_and_fixed_brushes_alone(string key, uint argb) =>
        Assert.Equal(argb, Dark(key, argb));

    [Fact]
    public void Replaces_the_windows_accent_with_the_themes_blue()
    {
        Assert.Equal(ModernPalette.Dark.Accent, Dark("AccentButtonBackground", WindowsAccent));
        Assert.Equal(ModernPalette.Dark.AccentHover, Dark("AccentButtonBackgroundPointerOver", WindowsAccent));
        Assert.Equal(ModernPalette.Dark.AccentPressed, Dark("AccentButtonBackgroundPressed", WindowsAccent));
        Assert.Equal(ModernPalette.Dark.AccentText, Dark("HyperlinkButtonForeground", 0xFFC1E7ED));
        Assert.Equal(ModernPalette.Light.Accent, Light("AccentFillColorDefaultBrush", WindowsAccent));
    }

    [Fact]
    public void A_selected_row_gets_the_row_colour_and_not_the_accent() =>
        Assert.Equal(ModernPalette.Dark.RowSelected, Dark("ListBoxItemSelectedBackground", 0xFF4E789C));

    [Fact]
    public void Text_on_an_accent_fill_is_white_and_keeps_its_alpha()
    {
        Assert.Equal(0xFFFFFFFFu, Dark("AccentButtonForeground", 0xFF000000));
        Assert.Equal(0x80FFFFFFu, Dark("AccentButtonForegroundPressed", 0x80000000));
        Assert.Equal(0xFFFFFFFFu, Dark("CheckBoxCheckGlyphForeground", 0xFF000000));
    }

    [Fact]
    public void Text_steps_map_in_both_modes()
    {
        Assert.Equal(ModernPalette.Dark.Text, Dark("TextFillColorPrimaryBrush", 0xFFFFFFFF));
        Assert.Equal(ModernPalette.Dark.Muted, Dark("TextFillColorSecondaryBrush", 0xC5FFFFFF));
        Assert.Equal(ModernPalette.Dark.Faint, Dark("TextFillColorTertiaryBrush", 0x87FFFFFF));
        Assert.Equal(ModernPalette.Light.Text, Light("ButtonForeground", 0xE4000000));
        Assert.Equal(ModernPalette.Light.Muted, Light("TextFillColorSecondaryBrush", 0x9E000000));
    }

    [Fact]
    public void Dividers_are_quieter_than_control_borders()
    {
        Assert.Equal(ModernPalette.Dark.Divider, Dark("DividerStrokeColorDefaultBrush", 0x15FFFFFF));
        Assert.Equal(ModernPalette.Dark.Divider, Dark("CardStrokeColorDefaultBrush", 0x19000000));
        Assert.Equal(ModernPalette.Dark.ControlBorder, Dark("ControlStrokeColorDefaultBrush", 0x12FFFFFF));
    }

    [Fact]
    public void Surfaces_inputs_and_buttons_become_solid_steps()
    {
        Assert.Equal(ModernPalette.Dark.Window, Dark("ApplicationBackgroundBrush", 0xFF202020));
        Assert.Equal(ModernPalette.Dark.Surface, Dark("SolidBackgroundFillColorBaseBrush", 0xFF202020));
        Assert.Equal(ModernPalette.Dark.Popup, Dark("FlyoutBackground", 0xFF2C2C2C));
        Assert.Equal(ModernPalette.Dark.Input, Dark("TextControlBackgroundFocused", 0xB31E1E1E));
        Assert.Equal(ModernPalette.Dark.Button, Dark("ButtonBackground", 0x0FFFFFFF));
        Assert.Equal(ModernPalette.Dark.ButtonHover, Dark("ButtonBackgroundPointerOver", 0x15FFFFFF));
        Assert.Equal(ModernPalette.Dark.ButtonPressed, Dark("ComboBoxBackgroundPressed", 0x08FFFFFF));
    }

    [Fact]
    public void Status_colours_change_but_their_message_backgrounds_do_not()
    {
        Assert.Equal(ModernPalette.Dark.Error, Dark("SystemFillColorCriticalBrush", 0xFFFF99A4));
        Assert.Equal(ModernPalette.Dark.Warning, Dark("SystemFillColorCautionBrush", 0xFFFCE100));
        Assert.Equal(ModernPalette.Dark.Success, Dark("SystemFillColorSuccessBrush", 0xFF6CCB5F));
        Assert.Equal(0xFF442726u, Dark("SystemFillColorCriticalBackgroundBrush", 0xFF442726));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Text_is_readable_on_every_surface(bool dark)
    {
        var c = dark ? ModernPalette.Dark : ModernPalette.Light;
        foreach (uint surface in new[] { c.Window, c.Surface, c.Popup })
            foreach (uint text in new[] { c.Text, c.Strong, c.Muted, c.Faint, c.AccentText, c.Error, c.Warning, c.Success })
                Assert.True(ModernPalette.Contrast(text, surface) >= 4.5, $"{text:X8} on {surface:X8}");

        Assert.True(ModernPalette.Contrast(c.Text, c.Input) >= 4.5);
        Assert.True(ModernPalette.Contrast(c.Text, c.Button) >= 4.5);
        Assert.True(ModernPalette.Contrast(c.Text, c.ButtonHover) >= 4.5);
        Assert.True(ModernPalette.Contrast(c.Strong, c.RowSelected) >= 4.5);
        Assert.True(ModernPalette.Contrast(0xFFFFFFFF, c.Accent) >= 4.5);
    }
}
