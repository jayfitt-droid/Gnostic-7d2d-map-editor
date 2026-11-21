namespace MapEditor.Core.Edit;

/// <summary>Detects when the pointer has moved far enough from its press to start a drag.</summary>
public struct DragThreshold(double startX, double startY, double minimumHorizontal, double minimumVertical)
{
    /// <summary>Whether either axis has reached its minimum distance since the press.</summary>
    public bool Passed { get; private set; }

    /// <summary>Checks the pointer position in the same coordinate space as the press.</summary>
    public bool Update(double x, double y)
    {
        Passed = Passed || Math.Abs(x - startX) >= minimumHorizontal || Math.Abs(y - startY) >= minimumVertical;
        return Passed;
    }
}
