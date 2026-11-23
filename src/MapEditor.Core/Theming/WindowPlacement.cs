namespace MapEditor.Core.Theming;

/// <summary>Chooses usable startup bounds from saved main window settings.</summary>
public static class WindowPlacement
{
    /// <summary>A rectangle in desktop pixels, with the origin at the primary screen's upper left.</summary>
    public readonly record struct Bounds(double Left, double Top, double Width, double Height)
    {
        public double Right => Left + Width;
        public double Bottom => Top + Height;
    }

    /// <summary>A monitor's usable area in desktop pixels and its effective pixels per DIP.</summary>
    public readonly record struct Monitor(Bounds WorkArea, double Scale);

    /// <summary>
    /// Fits the saved window onto the monitor that shows the most of its title bar.
    /// Returns null when too little of the title bar is visible or no position was saved.
    /// </summary>
    public static Bounds? Restore(double? left, double? top, double widthDips, double heightDips,
        IReadOnlyList<Monitor> monitors)
    {
        if (left is not double x || top is not double y || !double.IsFinite(x) || !double.IsFinite(y)
            || !double.IsFinite(widthDips) || !double.IsFinite(heightDips) || widthDips <= 0 || heightDips <= 0)
            return null;

        Bounds? best = null;
        double bestOverlap = 0;
        foreach (var (workArea, scale) in monitors)
        {
            double width = widthDips * scale;
            double height = heightDips * scale;
            double titleHeight = Math.Min(height, 32 * scale);
            double visibleWidth = Math.Min(x + width, workArea.Right) - Math.Max(x, workArea.Left);
            double visibleHeight = Math.Min(y + titleHeight, workArea.Bottom) - Math.Max(y, workArea.Top);
            if (visibleWidth < Math.Min(width, 100 * scale) || visibleHeight < titleHeight / 2)
                continue;

            double overlap = visibleWidth * visibleHeight;
            if (overlap <= bestOverlap)
                continue;

            bestOverlap = overlap;
            width = Math.Min(width, workArea.Width);
            height = Math.Min(height, workArea.Height);
            best = new Bounds(Math.Clamp(x, workArea.Left, workArea.Right - width),
                Math.Clamp(y, workArea.Top, workArea.Bottom - height), width, height);
        }
        return best;
    }
}
