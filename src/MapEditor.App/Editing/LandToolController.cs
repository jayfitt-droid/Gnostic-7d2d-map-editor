using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using MapEditor.App.Map;
using MapEditor.Core.Messages;
using MapEditor.Core.Ops;
using MapEditor.Core.Rendering;

namespace MapEditor.App.Editing;

/// <summary>Edits a draft and shows its coastline without changing the world.</summary>
public sealed partial class LandToolController(MapView map, MainViewModel vm) : ObservableObject
{
    private LandDrawingHistory? _history;
    private int _size;
    private Drag? _drag;
    private LandBox? _sliderBox;
    private int _selectedIndex = -1;
    private LandPreviewRequest? _pendingPreview;
    private bool _rendering;
    private int _previewSession;

    private sealed record Drag(Point Start, int Index, LandBox Box, Point? Handle)
    {
        public Point Last { get; init; } = Start;
    }

    private sealed record LandPreviewRequest(int Size, LandDrawing Drawing, int Session);

    public event EventHandler? HistoryChanged;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(RectangleTool))] private bool _selectTool;
    public bool RectangleTool { get => !SelectTool; set { if (value) SelectTool = false; } }

    public IReadOnlyList<string> BoxNames { get; private set; } = [];

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (!SetProperty(ref _selectedIndex, value)) return;
            ShowBoxes();
            RefreshOptions();
        }
    }

    public string? UndoName => _history?.UndoName;
    public string? RedoName => _history?.RedoName;
    public bool IsDragging => _drag is not null || _sliderBox is not null;

    private LandBox? SelectedBox => _sliderBox ?? (_history is { } h && _selectedIndex >= 0 && _selectedIndex < h.Boxes.Count
        ? h.Boxes[_selectedIndex] : null);

    public bool HasSelection => SelectedBox is not null;
    public string BoxSize => SelectedBox is { } b ? $"{b.X1 - b.X0:N0} × {b.Z1 - b.Z0:N0} m" : "";
    public bool WarnSmallBox => SelectedBox is { CutsWater: false } b
        && (double)(b.X1 - b.X0) * (b.Z1 - b.Z0) * (b.Fill / 100.0) < 360 * 360;

    public int CoastSeed
    {
        get => SelectedBox?.Seed ?? 1;
        set { if (SelectedBox is { } b) ChangeBox(b with { Seed = value }, "change coast seed"); }
    }

    public int Fill
    {
        get => SelectedBox?.Fill ?? LandBox.MinFill;
        set { if (SelectedBox is { } b) ChangeBox(b with { Fill = Math.Clamp(value, LandBox.MinFill, LandBox.MaxFill) }, "change fill"); }
    }

    public int Roughness
    {
        get => SelectedBox?.Roughness ?? 0;
        set { if (SelectedBox is { } b) ChangeBox(b with { Roughness = Math.Clamp(value, 0, 100) }, "change roughness"); }
    }

    public void Begin(int size, LandDrawing? drawing)
    {
        _size = size;
        _history = new(drawing);
        SelectTool = _history.Boxes.Count > 0;
        map.Cursor = null;
        map.LandPreview = new(null, [], null);
        DrawingChanged(-1);
    }

    public void End()
    {
        _history = null;
        _drag = null;
        _sliderBox = null;
        _pendingPreview = null;
        _previewSession++;
        map.LandPreview = null;
        map.Cursor = null;
    }

    public LandDrawing? Snapshot() => _history?.Snapshot();

    public void UndoOrRedo(bool redo)
    {
        if (IsDragging || _history is not { } h || !(redo ? h.Redo() : h.Undo())) return;
        DrawingChanged(Math.Min(SelectedIndex, h.Boxes.Count - 1));
    }

    public void Reroll()
    {
        if (SelectedBox is not { } box) return;
        int seed;
        do seed = Random.Shared.Next(1, 1_000_000); while (seed == box.Seed);
        ChangeBox(box with { Seed = seed }, "re-roll coast");
    }

    public void Delete()
    {
        if (_history is not { } history || !HasSelection || IsDragging) return;
        history.Delete(SelectedIndex);
        DrawingChanged(-1);
    }

    public void BeginSlider() => _sliderBox = SelectedBox;

    public void EndSlider(string name, bool cancelled)
    {
        if (_sliderBox is not { } box || _history is not { } h) return;
        _sliderBox = null;
        if (!cancelled && SelectedIndex >= 0) h.Replace(SelectedIndex, box, name);
        DrawingChanged(SelectedIndex);
    }

    private void ChangeBox(LandBox box, string name)
    {
        if (_history is not { } h || _drag is not null || box == SelectedBox) return;
        if (_sliderBox is not null)
        {
            _sliderBox = box;
            RefreshOptions();
            ShowDrawing(SnapshotWith(box, SelectedIndex));
            return;
        }
        h.Replace(SelectedIndex, box, name);
        RefreshOptions();
        ShowDrawing(h.Snapshot());
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CancelGesture()
    {
        map.Cursor = null;
        if (_drag is null)
        {
            SelectedIndex = -1;
            return;
        }
        _drag = null;
        if (_history is { } h) ShowDrawing(h.Snapshot());
    }

    public void OnToolInput(object? sender, ToolInputEventArgs e)
    {
        if (_history is not { } h || vm.IsBusy) return;
        if (e.CaptureLost)
        {
            if (_drag is not null) CancelGesture();
            return;
        }

        var pos = Snap(e.MapPoint);
        if (e.Phase == ToolPhase.Down)
        {
            bool cuts = (e.Modifiers & ModifierKeys.Alt) != 0;
            if (!cuts && HitBoxHandle(e.MapPoint, h.Boxes) is { } hit)
            {
                SelectedIndex = hit.Index;
                var box = h.Boxes[hit.Index];
                _drag = new(pos, hit.Index, box, hit.Handle);
                map.Cursor = ResizeCursor(MapRect(box), hit.Handle);
                return;
            }

            if (e.MapPoint.X < 0 || e.MapPoint.Y < 0 || e.MapPoint.X > _size || e.MapPoint.Y > _size) return;
            if (!SelectTool || cuts)
            {
                // Choose the seed once, so the preview uses the same coast throughout the drag.
                _drag = new(pos, -1, new LandBox(0, 0, 0, 0, cuts, Random.Shared.Next(1, 1_000_000)), null);
                return;
            }

            SelectedIndex = HitBox(e.MapPoint, h.Boxes);
            if (SelectedBox is { } picked) _drag = new(pos, SelectedIndex, picked, null);
            return;
        }

        if (_drag is not { } drag) return;
        if (drag.Index >= 0 && !e.DragThresholdPassed)
        {
            if (e.Phase == ToolPhase.Up)
            {
                _drag = null;
                UpdateCursor(e.MapPoint);
            }
            return;
        }
        if (e.Phase == ToolPhase.Move)
        {
            if (pos == drag.Last) return;
            _drag = drag with { Last = pos };
            var changed = DragBox(drag, pos);
            ShowDrawing(SnapshotWith(changed, drag.Index), drag.Index < 0 ? h.Boxes.Count : drag.Index);
            return;
        }

        _drag = null;
        var finalBox = DragBox(drag, pos);
        if (drag.Index < 0 && finalBox.Bounds.IsEmpty)
        {
            DrawingChanged(HitBox(e.MapPoint, h.Boxes));
            UpdateCursor(e.MapPoint);
            return;
        }
        if (drag.Index < 0)
        {
            h.Add(finalBox);
            DrawingChanged(h.Boxes.Count - 1);
            UpdateCursor(e.MapPoint);
            return;
        }
        h.Replace(drag.Index, finalBox, drag.Handle is null ? "move box" : "resize box");
        DrawingChanged(drag.Index);
        UpdateCursor(e.MapPoint);
    }

    private int HitBox(Point point, IReadOnlyList<LandBox> boxes)
    {
        if (SelectedBox is { } selected && MapRect(selected).Contains(point)) return SelectedIndex;
        for (int i = boxes.Count - 1; i >= 0; i--)
            if (MapRect(boxes[i]).Contains(point)) return i;
        return -1;
    }

    private (int Index, Point Handle)? HitBoxHandle(Point point, IReadOnlyList<LandBox> boxes)
    {
        if (SelectedBox is { } selected && HitHandle(point, MapRect(selected)) is { } handle)
            return (SelectedIndex, handle);
        if (SelectTool)
            for (int i = boxes.Count - 1; i >= 0; i--)
                if (i != SelectedIndex && HitHandle(point, MapRect(boxes[i])) is { } other)
                    return (i, other);
        return null;
    }

    private Point? HitHandle(Point point, Rect rect)
    {
        double grab = 8 / map.Scale, nearest = double.PositiveInfinity;
        Point? picked = null;
        foreach (var handle in LandPreview.ResizeHandles(rect))
        {
            var offset = point - handle;
            if (Math.Abs(offset.X) > grab || Math.Abs(offset.Y) > grab || offset.LengthSquared >= nearest) continue;
            nearest = offset.LengthSquared;
            picked = handle;
        }
        return picked;
    }

    public void UpdateCursor(Point? point)
    {
        if (_history is not { } h || IsDragging || map.IsPanning) return;
        map.Cursor = !vm.IsBusy && (Keyboard.Modifiers & ModifierKeys.Alt) == 0 && point is { } at
            && HitBoxHandle(at, h.Boxes) is { } hit ? ResizeCursor(MapRect(h.Boxes[hit.Index]), hit.Handle) : null;
    }

    private static Cursor ResizeCursor(Rect box, Point handle)
    {
        bool horizontal = handle.X == box.Left || handle.X == box.Right;
        bool vertical = handle.Y == box.Top || handle.Y == box.Bottom;
        if (!horizontal) return Cursors.SizeNS;
        if (!vertical) return Cursors.SizeWE;
        return (handle.X == box.Left) == (handle.Y == box.Top) ? Cursors.SizeNWSE : Cursors.SizeNESW;
    }

    private LandBox DragBox(Drag drag, Point point)
    {
        if (drag.Index < 0) return WithRect(drag.Box, new Rect(drag.Start, point));
        var r = MapRect(drag.Box);
        var delta = point - drag.Start;
        if (drag.Handle is not { } handle)
        {
            r.Offset(Math.Clamp(delta.X, -r.Left, _size - r.Right), Math.Clamp(delta.Y, -r.Top, _size - r.Bottom));
            return WithRect(drag.Box, r);
        }

        double minWidth = Math.Min(r.Width, LandField.Cell), minDepth = Math.Min(r.Height, LandField.Cell);
        double left = handle.X == r.Left ? Math.Clamp(r.Left + delta.X, 0, r.Right - minWidth) : r.Left;
        double right = handle.X == r.Right ? Math.Clamp(r.Right + delta.X, r.Left + minWidth, _size) : r.Right;
        double top = handle.Y == r.Top ? Math.Clamp(r.Top + delta.Y, 0, r.Bottom - minDepth) : r.Top;
        double bottom = handle.Y == r.Bottom ? Math.Clamp(r.Bottom + delta.Y, r.Top + minDepth, _size) : r.Bottom;
        return WithRect(drag.Box, new Rect(new Point(left, top), new Point(right, bottom)));
    }

    private Point Snap(Point point)
    {
        double Cell(double n) => Math.Clamp(Math.Round(n / LandField.Cell) * LandField.Cell, 0, _size);
        return new(Cell(point.X), Cell(point.Y));
    }

    private Rect MapRect(LandBox b) => new(b.X0, _size - b.Z1, b.X1 - b.X0, b.Z1 - b.Z0);

    private LandBox WithRect(LandBox b, Rect r) =>
        b with { X0 = (int)r.Left, X1 = (int)r.Right, Z0 = _size - (int)r.Bottom, Z1 = _size - (int)r.Top };

    private LandDrawing SnapshotWith(LandBox box, int index)
    {
        var drawing = _history!.Snapshot();
        if (index < 0) drawing.Boxes.Add(box);
        else drawing.Boxes[index] = box;
        return drawing;
    }

    private void DrawingChanged(int selected)
    {
        if (_history is not { } h) return;
        BoxNames = h.Boxes.Select((b, i) => $"{i + 1}. {(b.CutsWater ? "Water" : "Land")}").ToList();
        OnPropertyChanged(nameof(BoxNames));
        _selectedIndex = selected;
        OnPropertyChanged(nameof(SelectedIndex));
        RefreshOptions();
        ShowDrawing(h.Snapshot());
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshOptions()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(BoxSize));
        OnPropertyChanged(nameof(WarnSmallBox));
        OnPropertyChanged(nameof(CoastSeed));
        OnPropertyChanged(nameof(Fill));
        OnPropertyChanged(nameof(Roughness));
    }

    private void ShowBoxes(IReadOnlyList<LandBox>? boxes = null, int? selected = null)
    {
        if (_history is not { } h || map.LandPreview is not { } current) return;
        map.LandPreview = current with
        {
            Boxes = (boxes ?? h.Boxes).Select(b => (MapRect(b), b.CutsWater)).ToArray(),
            Selected = selected ?? SelectedIndex,
        };
    }

    private void ShowDrawing(LandDrawing drawing, int? selected = null)
    {
        ShowBoxes(drawing.Boxes, selected);
        _pendingPreview = new(_size, drawing, _previewSession);
        if (!_rendering) _ = RenderLatestAsync();
    }

    /// <summary>At most one preview is rendered at a time. While it runs, only the latest draft is queued.</summary>
    private async Task RenderLatestAsync()
    {
        _rendering = true;
        try
        {
            while (_pendingPreview is { } request)
            {
                _pendingPreview = null;
                var image = await Task.Run(() => CoastImage(request.Size, request.Drawing));
                if (map.LandPreview is { } current && request.Session == _previewSession)
                    map.LandPreview = current with { Coast = image };
            }
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            if (_history is not null) vm.Report("Could not preview the coast.", MessageKind.Warning);
        }
        finally
        {
            _rendering = false;
        }
    }

    private static uint AverageCoastColours(uint a, uint b, uint c, uint d)
    {
        const uint redBlue = 0xFF00FF, green = 0x00FF00;
        uint rb = ((a & redBlue) + (b & redBlue) + (c & redBlue) + (d & redBlue) + 0x020002) >> 2;
        uint g = ((a & green) + (b & green) + (c & green) + (d & green) + 0x000200) >> 2;
        return (rb & redBlue) | (g & green);
    }

    private static BitmapSource CoastImage(int size, LandDrawing drawing)
    {
        var field = LandShapes.Build(size, new LandShape { Kind = LandShapeKind.Drawn, Drawing = drawing })!;
        int w = field.Width * 4;
        float spacing = (float)size / w;
        float quarter = spacing / 4;
        var pixels = new uint[w * w];
        for (int row = 0; row < w; row++)
        {
            float z = size - (row + 0.5f) * spacing;
            for (int col = 0; col < w; col++)
            {
                float x = (col + 0.5f) * spacing;
                float d = field.Sample(x, z);
                // Sample the four quadrant centres within a pixel of the near-shore stops.
                uint colour = Math.Abs(d + 32) < spacing || Math.Abs(d + 4) < spacing || Math.Abs(d - 3) < spacing
                    || Math.Abs(d - 20) < spacing || Math.Abs(d - 30) < spacing
                    ? AverageCoastColours(CoastColours.AtDistance(field.Sample(x - quarter, z - quarter)),
                        CoastColours.AtDistance(field.Sample(x + quarter, z - quarter)),
                        CoastColours.AtDistance(field.Sample(x - quarter, z + quarter)),
                        CoastColours.AtDistance(field.Sample(x + quarter, z + quarter)))
                    : CoastColours.AtDistance(d);
                pixels[col + row * w] = colour;
            }
        }
        var bitmap = BitmapSource.Create(w, w, 96, 96, PixelFormats.Bgr32, null, pixels, w * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
