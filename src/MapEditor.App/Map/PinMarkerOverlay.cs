using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MapEditor.Core.GameLink;
using static MapEditor.App.Map.Freezables;

namespace MapEditor.App.Map;

/// <summary>Draws playtest pins without taking mouse input from the map.</summary>
public sealed class PinMarkerOverlay : FrameworkElement
{
    private static readonly Geometry Marker = Frozen(Geometry.Parse("M 0,0 L -5,-8 A 6,6 0 1 1 5,-8 Z"));
    private static readonly Brush Fill = Frozen(new SolidColorBrush(Color.FromRgb(255, 202, 93)));
    private static readonly Pen Outline = Frozen(new Pen(Brushes.Black, 1.5) { LineJoin = PenLineJoin.Round });
    private static readonly Typeface LabelFont = new("Segoe UI");
    private MapView? _target;
    private (Point Point, string Note)[] _markers = [];

    internal MapView? Target
    {
        get => _target;
        set
        {
            if (_target is not null) _target.ViewChanged -= ViewChanged;
            _target = value;
            if (_target is not null) _target.ViewChanged += ViewChanged;
            InvalidateVisual();
        }
    }

    internal void SetPins(IEnumerable<GameLinkPin> pins, int size)
    {
        var markers = new List<(Point, string)>();
        foreach (var pin in pins)
            if (MainWindow.PinMapPoint(pin, size) is { } point)
                markers.Add((point, pin.Note));
        _markers = markers.ToArray();
        InvalidateVisual();
    }

    private void ViewChanged(object? sender, EventArgs e)
    {
        if (_markers.Length > 0) InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_target is not { } map || _markers.Length == 0) return;
        Rect visible = map.VisibleMapRect;
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var (point, note) in _markers)
        {
            if (!visible.Contains(point)) continue;
            Point anchor = map.MapToScreen(point);
            dc.PushTransform(new TranslateTransform(anchor.X, anchor.Y));
            dc.DrawGeometry(Fill, Outline, Marker);
            if (note.Length > 0)
            {
                var text = new FormattedText(note, CultureInfo.CurrentUICulture, FlowDirection, LabelFont, 12,
                    Brushes.White, pixelsPerDip) { MaxTextWidth = 180, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
                dc.DrawRoundedRectangle(Brushes.Black, null, new Rect(8, -18, text.Width + 8, text.Height + 4), 3, 3);
                dc.DrawText(text, new Point(12, -16));
            }
            dc.Pop();
        }
    }
}
