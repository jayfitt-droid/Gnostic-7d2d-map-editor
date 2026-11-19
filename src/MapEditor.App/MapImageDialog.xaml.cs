using System.Windows;
using MapEditor.Core.Rendering;

namespace MapEditor.App;

/// <summary>Asks for the size and layers of a map image export. Starts from what the map view shows.</summary>
public partial class MapImageDialog : Window
{
    public MapImageDialog(int worldSize, MapLayers layers, bool pois, bool grid)
    {
        InitializeComponent();
        SizeFull.Content = $"Full ({worldSize} x {worldSize}, 1 pixel per metre)";
        // Sizes above the world's would only enlarge it; the export stops at the world's size.
        Size1024.IsEnabled = worldSize >= 1024;
        Size2048.IsEnabled = worldSize >= 2048;
        Size4096.IsEnabled = worldSize >= 4096;
        if (!Size2048.IsEnabled)
        {
            Size2048.IsChecked = false;
            SizeFull.IsChecked = true;
        }
        if (worldSize >= 8192)
            SizeNote.Text = "Full is a very large picture and takes a while to draw.";
        else SizeNote.Visibility = Visibility.Collapsed;

        HillshadeBox.IsChecked = (layers & MapLayers.Hillshade) != 0;
        BiomesBox.IsChecked = (layers & MapLayers.Biomes) != 0;
        WaterBox.IsChecked = (layers & MapLayers.Water) != 0;
        RoadsBox.IsChecked = (layers & MapLayers.Roads) != 0;
        RadiationBox.IsChecked = (layers & MapLayers.Radiation) != 0;
        PoisBox.IsChecked = pois;
        GridBox.IsChecked = grid;
    }

    /// <summary>The chosen options, set when the dialog returns true.</summary>
    public MapImageOptions? Options { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        int size = Size1024.IsChecked == true ? 1024 : Size2048.IsChecked == true ? 2048 : Size4096.IsChecked == true ? 4096 : 0;
        var layers = MapLayers.None;
        if (HillshadeBox.IsChecked == true) layers |= MapLayers.Hillshade;
        if (BiomesBox.IsChecked == true) layers |= MapLayers.Biomes;
        if (WaterBox.IsChecked == true) layers |= MapLayers.Water;
        if (RoadsBox.IsChecked == true) layers |= MapLayers.Roads;
        if (RadiationBox.IsChecked == true) layers |= MapLayers.Radiation;
        Options = new MapImageOptions(size, layers, PoisBox.IsChecked == true, GridBox.IsChecked == true);
        DialogResult = true;
    }
}
