using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace MapEditor.App;

/// <summary>An in-window, non-modal tip. Only the card itself receives pointer input.</summary>
public partial class TeachingTip : UserControl
{
    private const double TailLength = 10;
    private const double TailHalfWidth = 7;
    private const double EdgeInset = 16;
    private static readonly Side[] HorizontalFirst = [Side.Left, Side.Right, Side.Below, Side.Above];
    private static readonly Side[] VerticalFirst = [Side.Below, Side.Above, Side.Right, Side.Left];
    private (Rect Card, Side? Side, double Offset)? _placement;
    private bool _canGoBack;

    private enum Side { Below, Above, Right, Left }

    public TeachingTip() => InitializeComponent();

    public event EventHandler? BackRequested;
    public event EventHandler? NextRequested;
    public event EventHandler? CloseRequested;
    public bool SuppressOffer => DontShowAgain.IsChecked == true;

    public void ShowStep(string title, string text, bool canGoBack) => Show(title, text, canGoBack, offer: false);

    public void ShowOffer()
    {
        DontShowAgain.IsChecked = false;
        Show("Take a tour?", "Follow the steps to draw islands and build a world.", canGoBack: false, offer: true);
    }

    private void Show(string title, string text, bool canGoBack, bool offer)
    {
        Heading.Text = title;
        Body.Text = text;
        AutomationProperties.SetName(this, title);
        BackButton.Visibility = offer ? Visibility.Collapsed : Visibility.Visible;
        _canGoBack = canGoBack;
        SetNavigationEnabled(true);
        DontShowAgain.Visibility = offer ? Visibility.Visible : Visibility.Collapsed;
        string next = offer ? "Start tour" : "Next";
        NextButton.Content = next;
        AutomationProperties.SetName(NextButton, next);
        Visibility = Visibility.Visible;
    }

    public void SetNavigationEnabled(bool enabled)
    {
        BackButton.IsEnabled = enabled && _canGoBack;
        NextButton.IsEnabled = enabled;
    }

    /// <summary>Places the card beside the visible target, or in the centre when there is no visible target.</summary>
    public void Place(Rect bounds, Rect target, bool preferSide)
    {
        Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = DesiredSize.Width - 2 * TailLength;
        double height = DesiredSize.Height - 2 * TailLength;
        bounds.Inflate(-TailLength, -TailLength);
        if (bounds.IsEmpty || bounds.Width < width || bounds.Height < height) return;

        Rect card = new(bounds.Left + (bounds.Width - width) / 2, bounds.Top + (bounds.Height - height) / 2, width, height);
        if (target.IsEmpty)
        {
            ApplyPlacement(card, null, 0);
            return;
        }

        double targetX = target.Left + target.Width / 2;
        double targetY = target.Top + target.Height / 2;
        foreach (Side side in preferSide ? HorizontalFirst : VerticalFirst)
        {
            double x = Math.Clamp(targetX - width / 2, bounds.Left, bounds.Right - width);
            double y = Math.Clamp(targetY - height / 2, bounds.Top, bounds.Bottom - height);
            switch (side)
            {
                case Side.Below: y = target.Bottom + TailLength; break;
                case Side.Above: y = target.Top - TailLength - height; break;
                case Side.Right: x = target.Right + TailLength; break;
                case Side.Left: x = target.Left - TailLength - width; break;
            }
            Rect candidate = new(x, y, width, height);
            if (!bounds.Contains(candidate)) continue;

            bool vertical = side is Side.Above or Side.Below;
            double offset = vertical ? targetX - x : targetY - y;
            ApplyPlacement(candidate, side, Math.Clamp(offset, EdgeInset, (vertical ? width : height) - EdgeInset));
            return;
        }
        ApplyPlacement(card, null, 0);
    }

    private void ApplyPlacement(Rect card, Side? side, double offset)
    {
        if (_placement == (card, side, offset)) return;
        _placement = (card, side, offset);
        Canvas.SetLeft(this, card.X - TailLength);
        Canvas.SetTop(this, card.Y - TailLength);

        double x = TailLength + offset, y = TailLength + offset;
        double right = TailLength + card.Width, bottom = TailLength + card.Height;
        (Point Start, Point Tip, Point End) points = side switch
        {
            Side.Below => (new(x - TailHalfWidth, TailLength + 1), new(x, 0), new(x + TailHalfWidth, TailLength + 1)),
            Side.Above => (new(x - TailHalfWidth, bottom - 1), new(x, bottom + TailLength), new(x + TailHalfWidth, bottom - 1)),
            Side.Right => (new(TailLength + 1, y - TailHalfWidth), new(0, y), new(TailLength + 1, y + TailHalfWidth)),
            Side.Left => (new(right - 1, y - TailHalfWidth), new(right + TailLength, y), new(right - 1, y + TailHalfWidth)),
            _ => default,
        };
        TailFill.Data = side is null ? null : TailGeometry(points.Start, points.Tip, points.End, filled: true);
        TailStroke.Data = side is null ? null : TailGeometry(points.Start, points.Tip, points.End, filled: false);
    }

    private static StreamGeometry TailGeometry(Point start, Point tip, Point end, bool filled)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, isFilled: filled, isClosed: filled);
            context.LineTo(tip, isStroked: true, isSmoothJoin: false);
            context.LineTo(end, isStroked: true, isSmoothJoin: false);
        }
        geometry.Freeze();
        return geometry;
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
    private void Next_Click(object sender, RoutedEventArgs e) => NextRequested?.Invoke(this, EventArgs.Empty);
    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
}
