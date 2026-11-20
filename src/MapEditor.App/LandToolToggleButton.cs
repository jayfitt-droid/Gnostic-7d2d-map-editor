using System.Windows.Controls.Primitives;

namespace MapEditor.App;

/// <summary>A Land mode tool button that stays checked until the other tool is chosen.</summary>
public sealed class LandToolToggleButton : ToggleButton
{
    protected override void OnToggle() => SetCurrentValue(IsCheckedProperty, true);
}
