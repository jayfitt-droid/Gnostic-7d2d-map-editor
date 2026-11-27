using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace MapEditor.Core.Messages;

/// <summary>What a status message is, so the Messages panel can mark it (#225).</summary>
public enum MessageKind
{
    /// <summary>An ordinary message, such as what a tool or a stage did.</summary>
    Normal,

    /// <summary>A message the editor sends by itself and often, such as an autosave. It does not replace the status bar text.</summary>
    Routine,

    /// <summary>Something worked only in part, or a setting was not saved.</summary>
    Warning,

    /// <summary>An operation failed.</summary>
    Error,

    /// <summary>The changing text of an operation that runs. A later progress message from the same source updates the line.</summary>
    Progress,
}

/// <summary>
/// One line of the Messages panel: the message, when it came and what kind it is. A class, not a record: two lines
/// with the same text and time are still two lines, so a list can select one of them.
/// </summary>
public sealed class MessageLine(DateTime time, string text, MessageKind kind, string? source)
{
    public DateTime Time { get; } = time;
    public string Text { get; } = text;
    public MessageKind Kind { get; } = kind;

    /// <summary>Where the message came from. Progress messages from the same source share a line.</summary>
    public string? Source { get; } = source;

    /// <summary>The time as the panel shows it, for example "14:05".</summary>
    public string TimeText => FormatTime(CultureInfo.CurrentCulture);

    /// <summary>The time in the short time format of <paramref name="culture"/>.</summary>
    public string FormatTime(IFormatProvider culture) => Time.ToString("t", culture);
}

/// <summary>
/// The status messages of this session, oldest first, for the Messages panel (#225). It keeps the last
/// <see cref="MaxLines"/> lines and is not saved.
/// </summary>
public sealed class MessageLog
{
    /// <summary>The most lines the log keeps; the oldest line goes when a new line would pass it.</summary>
    public const int MaxLines = 1000;

    private readonly ObservableCollection<MessageLine> _lines = [];
    private readonly Func<DateTime> _clock;

    /// <param name="clock">Gives the time of each message; the local time when null.</param>
    public MessageLog(Func<DateTime>? clock = null)
    {
        _clock = clock ?? (() => DateTime.Now);
        Lines = new ReadOnlyObservableCollection<MessageLine>(_lines);
    }

    /// <summary>The lines, oldest first. A progress update replaces its line in place.</summary>
    public ReadOnlyObservableCollection<MessageLine> Lines { get; }

    /// <summary>
    /// Adds a message with the current time. A <see cref="MessageKind.Progress"/> message updates the last progress line
    /// from the same source instead, if only routine lines or progress lines from other sources came after it. Empty
    /// text adds nothing. Returns the line added or updated, or null.
    /// </summary>
    public MessageLine? Add(string text, MessageKind kind = MessageKind.Normal, string? source = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var line = new MessageLine(_clock(), text.Trim(), kind, source);
        if (kind == MessageKind.Progress && OpenProgressLine(source) is int index)
        {
            _lines[index] = line;
            return line;
        }
        if (_lines.Count >= MaxLines) _lines.RemoveAt(0);
        _lines.Add(line);
        return line;
    }

    /// <summary>Removes every line.</summary>
    public void Clear() => _lines.Clear();

    /// <summary>
    /// The lines as text to paste, for example into an issue: one line each, with the time, and "Error:" or "Warning:"
    /// before the text of errors and warnings.
    /// </summary>
    /// <param name="culture">Formats the times; the current culture when null.</param>
    public static string CopyText(IEnumerable<MessageLine> lines, IFormatProvider? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var text = new StringBuilder();
        foreach (var line in lines)
        {
            string mark = line.Kind switch
            {
                MessageKind.Error => "Error: ",
                MessageKind.Warning => "Warning: ",
                _ => "",
            };
            text.Append(line.FormatTime(culture)).Append("  ").Append(mark).Append(line.Text).AppendLine();
        }
        return text.ToString();
    }

    /// <summary>The index of the progress line from <paramref name="source"/> that a new progress message updates, or null.</summary>
    private int? OpenProgressLine(string? source)
    {
        for (int i = _lines.Count - 1; i >= 0; i--)
        {
            var line = _lines[i];
            if (line.Kind == MessageKind.Progress && line.Source == source) return i;
            if (line.Kind is not (MessageKind.Progress or MessageKind.Routine)) return null;
        }
        return null;
    }
}
