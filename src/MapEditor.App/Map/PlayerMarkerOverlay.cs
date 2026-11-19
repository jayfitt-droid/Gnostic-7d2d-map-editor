using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MapEditor.Core.GameLink;

namespace MapEditor.App.Map;

/// <summary>Draws the live player over the map without refreshing the terrain or its other overlays.</summary>
public sealed class PlayerMarkerOverlay : FrameworkElement
{
    private static readonly Geometry Arrow = Frozen(Geometry.Parse("M 0,-12 L 9,9 0,5 -9,9 Z"));
    private static readonly Brush Fill = Frozen(new SolidColorBrush(Color.FromRgb(230, 179, 255)));
    private static readonly Pen Outline = Frozen(new Pen(Brushes.Black, 2) { LineJoin = PenLineJoin.Round });

    private MapView? _target;
    private GameLinkPlayerMarker? _marker;
    private bool _fading;

    internal MapView? Target
    {
        get => _target;
        set
        {
            if (_target is not null) _target.ViewChanged -= TargetViewChanged;
            _target = value;
            if (_target is not null) _target.ViewChanged += TargetViewChanged;
            InvalidateVisual();
        }
    }

    internal GameLinkPlayerMarker? LiveMarker => _fading ? null : _marker;

    internal void SetMarker(GameLinkPlayerMarker? marker)
    {
        if (_marker == marker && !_fading) return;
        if (_fading) BeginAnimation(OpacityProperty, null);
        _fading = false;
        _marker = marker;
        InvalidateVisual();
    }

    internal void FadeMarker()
    {
        if (LiveMarker is null) return;
        if (!SystemParameters.ClientAreaAnimation)
        {
            SetMarker(null);
            return;
        }

        _fading = true;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(600)));
    }

    private void TargetViewChanged(object? sender, EventArgs e)
    {
        if (_marker is not null && Opacity > 0) InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_marker is not { } marker || _target is not { } map || Opacity == 0) return;
        var point = new Point(marker.MapX, marker.MapY);
        if (!map.VisibleMapRect.Contains(point)) return;

        Point centre = map.MapToScreen(point);
        var transform = Matrix.Identity;
        transform.Rotate(marker.Yaw);
        transform.Translate(centre.X, centre.Y);
        dc.PushTransform(new MatrixTransform(transform));
        dc.DrawGeometry(Fill, Outline, Arrow);
        dc.Pop();
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
