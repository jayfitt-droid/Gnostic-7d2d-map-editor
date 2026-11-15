namespace MapEditor.Core.Rendering;

/// <summary>
/// The colours POI outlines are drawn in, as straight (not premultiplied) ARGB. The map view and the map image
/// export share them, so an exported picture looks like the view.
/// </summary>
public static class PoiColours
{
    public const uint Prefab = 0xFFFFAA28;
    public const uint PrefabTint = 0x46FFAA28;
    public const uint Part = 0xC8FFE696;
    public const uint StreetTile = 0xE6EBEBF5;
    public const uint StreetTileTint = 0x2D14141E;
    public const uint Trader = 0xFFEB50FF;
    public const uint TraderTint = 0x6EEB50FF;
    public const uint Halo = 0xAA000000;

    /// <summary>The premultiplied BGRA pixel for a straight ARGB colour.</summary>
    public static uint Premultiply(uint argb)
    {
        uint a = argb >> 24;
        return a << 24 | (((argb >> 16) & 0xFF) * a / 255) << 16 | (((argb >> 8) & 0xFF) * a / 255) << 8 | ((argb & 0xFF) * a / 255);
    }
}
