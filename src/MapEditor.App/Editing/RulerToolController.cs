using System.Numerics;
using System.Windows;
using MapEditor.App.Map;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.App.Editing;

/// <summary>Keeps the ruler's path and live leg in the map view, with no world edits.</summary>
public sealed class RulerToolController
{
    private readonly MapView _map;
    private readonly MainViewModel _vm;
    private readonly List<Point> _points = [];
    private Point? _press;
    private int _clickCount;
    private bool _dragging;
    private bool _finished;
    private World? _world;

    public RulerToolController(MapView map, MainViewModel vm)
    {
        _map = map;
        _vm = vm;
        _map.ToolInput += OnToolInput;
    }

    public World? World
    {
        get => _world;
        set
        {
            _world = value;
            Cancel();
        }
    }

    private void OnToolInput(object? sender, ToolInputEventArgs e)
    {
        if (World is not { } world || !_vm.IsRulerTool) return;
        var point = e.MapPoint;
        var edge = new Rect(0, 0, world.Size, world.Size);
        switch (e.Phase)
        {
            case ToolPhase.Down when !_vm.IsBusy && edge.Contains(point):
                _press = point;
                _clickCount = e.ClickCount;
                _map.RulerOverlay = Preview;
                break;
            case ToolPhase.Move when _press is not null && e.DragThresholdPassed:
                _dragging = true;
                break;
            case ToolPhase.Up when _press is { } start:
                _press = null;
                _dragging = false;
                if (!e.CaptureLost && !_vm.IsBusy)
                {
                    if (e.DragThresholdPassed)
                    {
                        _points.Clear();
                        _points.Add(start);
                        AddPoint(Clamp(point, world.Size));
                        _finished = true;
                    }
                    else if (_clickCount == 2 && !_finished && _points.Count > 0)
                        _finished = true; // The first click of the pair already placed the endpoint.
                    else
                    {
                        if (_finished) _points.Clear();
                        _finished = false;
                        AddPoint(start);
                    }
                }
                Refresh();
                break;
        }
    }

    private void AddPoint(Point point)
    {
        if (_points.Count == 0 || _points[^1] != point) _points.Add(point);
    }

    /// <summary>Ends the click path and leaves its points on the map.</summary>
    public bool Finish()
    {
        if (_finished || _points.Count == 0 || _press is not null) return false;
        _finished = true;
        Refresh();
        return true;
    }

    /// <summary>Removes the most recent placed point.</summary>
    public bool RemoveLastPoint()
    {
        if (_points.Count == 0 || _press is not null) return false;
        _points.RemoveAt(_points.Count - 1);
        if (_points.Count == 0) _finished = false;
        Refresh();
        return true;
    }

    /// <summary>Clears the measurement. Returns whether there was a path or a press to clear.</summary>
    public bool Cancel()
    {
        if (_points.Count == 0 && _press is null) return false;
        _points.Clear();
        _press = null;
        _dragging = false;
        _finished = false;
        Refresh();
        return true;
    }

    private void Refresh()
    {
        _map.RulerOverlay = _points.Count > 0 ? Preview : null;
    }

    private static Point Clamp(Point point, int size) =>
        new(Math.Clamp(point.X, 0, size), Math.Clamp(point.Y, 0, size));

    private RulerOverlay? Preview(Point? pointer)
    {
        if (World is not { } world || _vm.IsBusy) return null;
        List<Point> points = _dragging && _press is { } start ? [start] : [.. _points];
        if (points.Count == 0) return null;

        bool live = false;
        if ((_dragging || !_finished) && pointer is { } mouse)
        {
            var end = Clamp(mouse, world.Size);
            if (end != points[^1])
            {
                points.Add(end);
                live = true;
            }
        }

        var grid = points.Select(p => new Vector2((float)p.X, (float)(world.Size - p.Y))).ToArray();
        return new RulerOverlay(points, RulerPath.Measure(world, grid), live);
    }
}
