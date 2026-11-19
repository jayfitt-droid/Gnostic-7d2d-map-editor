using MapEditor.Core.Edit;

namespace MapEditor.Core.Ops;

/// <summary>Works on a copy of the drawing, with undo steps that never reach the world's history.</summary>
public sealed class LandDrawingHistory
{
    private readonly LandDrawing _drawing;
    private readonly UndoStack _undo = new();

    public LandDrawingHistory(LandDrawing? drawing)
    {
        _drawing = drawing?.Clone() ?? new();
        Boxes = _drawing.Boxes.AsReadOnly();
    }

    public IReadOnlyList<LandBox> Boxes { get; }
    public string? UndoName => _undo.UndoName;
    public string? RedoName => _undo.RedoName;

    public LandDrawing Snapshot() => _drawing.Clone();

    public void Add(LandBox box) => Change("draw box", () => _drawing.Boxes.Add(box));

    public void Replace(int index, LandBox box, string name)
    {
        if (_drawing.Boxes[index] != box) Change(name, () => _drawing.Boxes[index] = box);
    }

    public void Delete(int index) => Change("delete box", () => _drawing.Boxes.RemoveAt(index));

    public bool Undo() => _undo.Undo() is not null;
    public bool Redo() => _undo.Redo() is not null;

    private void Change(string name, Action change)
    {
        var session = new EditSession(name);
        session.TouchList(_drawing.Boxes, box => box, GridRect.Empty); // Boxes are immutable.
        change();
        _undo.Push(session);
    }
}
