using System.Windows;

namespace MapEditor.App.Map;

/// <summary>Creates frozen freezables for use in static field initializers.</summary>
internal static class Freezables
{
    /// <summary>Freezes <paramref name="freezable"/> and returns it.</summary>
    public static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
