namespace MapEditor.Core.Tours;

/// <summary>The title, body and Back availability of a tour tip.</summary>
public sealed record TourTipContent(string Title, string Body, bool CanGoBack);

/// <summary>Moves through a tour using the current editor state supplied by its host.</summary>
public sealed class TourRunner
{
    private readonly Tour _tour;
    private readonly Func<TourCondition, bool> _holds;
    private int _index = -1;
    private bool _wasDone;

    public TourRunner(Tour tour, Func<TourCondition, bool> holds)
    {
        _tour = tour;
        _holds = holds;
        Next();
    }

    public TourStep? Current => IsFinished ? null : _tour.Steps[_index];
    public bool IsFinished => _index == _tour.Steps.Count;
    public bool CanGoBack => !IsFinished && PreviousIndex() >= 0;

    /// <summary>What the tip shows now, or null when the tour is finished. It changes with the editor state.</summary>
    public TourTipContent? Tip => Current is { } step ? new TourTipContent(step.Title, step.Body(_holds), CanGoBack) : null;

    /// <summary>Moves forward past completed or unavailable steps.</summary>
    public void Next()
    {
        if (IsFinished) return;
        do { _index++; }
        while (Current is { } step && (Skip(step) || step.IsDone(_holds)));
        _wasDone = false;
    }

    /// <summary>Revisits a step even if its action is complete, provided its control still applies.</summary>
    public void Back()
    {
        if (IsFinished) return;
        int previous = PreviousIndex();
        if (previous < 0) return;
        _index = previous;
        _wasDone = Current!.IsDone(_holds);
    }

    /// <summary>Advances when the current action becomes complete or the step no longer applies.</summary>
    public void Update()
    {
        if (Current is not { } step) return;
        bool done = step.IsDone(_holds);
        if (Skip(step) || (done && !_wasDone)) Next();
        else _wasDone = done;
    }

    public void Close() => _index = _tour.Steps.Count;

    private int PreviousIndex()
    {
        int previous = _index - 1;
        while (previous >= 0 && Skip(_tour.Steps[previous])) previous--;
        return previous;
    }

    private bool Skip(TourStep step) => step.SkipIf != TourCondition.None && _holds(step.SkipIf);
}
