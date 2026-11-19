using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MapEditor.Core.Rendering;

namespace MapEditor.App.Map;

/// <summary>Whole-world overview with the main view's viewport; click or drag to move the view.</summary>
public sealed class MiniMap : FrameworkElement
{
    private static readonly Pen ViewportPen = Frozen(new Pen(Brushes.White, 1.5));
    private static readonly Pen BorderPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), 1));
    private BitmapSource? _overview;
    private MapView? _target;
    private MapRenderer? _renderer;
    private MapLayers _layers;
    private bool _rendering;
    private bool _stale;

    public MiniMap()
    {
        // Hidden minimaps skip redraws; catch up once shown again.
        IsVisibleChanged += (_, _) => { if (IsVisible && _stale) Refresh(); };
    }

    public MapView? Target
    {
        get => _target;
        set
        {
            if (_target is not null) _target.ViewChanged -= OnTargetViewChanged;
            _target = value;
            if (_target is not null) _target.ViewChanged += OnTargetViewChanged;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Redraws the overview from the top mip level on a background thread. The old image stays up until the
    /// new one is ready; calls made during a redraw are combined into one more redraw once it finishes.
    /// </summary>
    public void SetRenderer(MapRenderer? renderer, MapLayers layers)
    {
        if (renderer != _renderer)
        {
            _overview = null;
            InvalidateVisual();
        }
        _renderer = renderer;
        _layers = layers;
        _stale = renderer is not null;
        Refresh();
    }

    private async void Refresh()
    {
        if (_rendering) return;
        _rendering = true;
        try
        {
            while (_stale && IsVisible && _renderer is { } renderer)
            {
                _stale = false;
                var layers = _layers;
                var bitmap = await Task.Run(() => Render(renderer, layers));
                if (renderer != _renderer) continue;
                _overview = bitmap;
                InvalidateVisual();
            }
        }
        catch (Exception)
        {
            // A redraw racing an edit can read a grid mid-change; the next edit redraws it again.
        }
        finally
        {
            _rendering = false;
        }
    }

    private static BitmapSource Render(MapRenderer renderer, MapLayers layers)
    {
        int top = renderer.Levels - 1;
        var buffer = new uint[MapRenderer.TileSize * MapRenderer.TileSize];
        renderer.RenderTile(top, 0, 0, layers & ~MapLayers.Radiation, buffer);
        int used = (int)Math.Ceiling(renderer.World.Size / (double)MapRenderer.MetresPerPixel(top));
        var full = BitmapSource.Create(MapRenderer.TileSize, MapRenderer.TileSize, 96, 96, PixelFormats.Pbgra32, null, buffer, MapRenderer.TileSize * 4);
        var cropped = new CroppedBitmap(full, new Int32Rect(0, 0, used, used));
        cropped.Freeze();
        return cropped;
    }

    private void OnTargetViewChanged(object? sender, EventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(RenderSize);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(200, 20, 20, 24)), BorderPen, bounds);
        if (_overview is null || _target?.Renderer is not { } renderer || renderer != _renderer) return;

        dc.DrawImage(_overview, bounds);
        double k = bounds.Width / renderer.World.Size;
        var v = _target.VisibleMapRect;
        var viewport = new Rect(v.X * k, v.Y * k, v.Width * k, v.Height * k);
        viewport.Intersect(bounds);
        if (!viewport.IsEmpty) dc.DrawRectangle(null, ViewportPen, viewport);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        CaptureMouse();
        MoveTarget(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsMouseCaptured) MoveTarget(e.GetPosition(this));
    }

    protected override void OnMouseUp(MouseButtonEventArgs e) => ReleaseMouseCapture();

    private void MoveTarget(Point p)
    {
        if (_target?.Renderer is not { } renderer || ActualWidth <= 0) return;
        double k = renderer.World.Size / ActualWidth;
        _target.PanTo(new Point(p.X * k, p.Y * k));
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
