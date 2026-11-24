using System.Text;
using MapEditor.Core.GameLink;

namespace GnosticGameLink;

/// <summary>Keeps unacknowledged pins across game sessions.</summary>
internal static class GameLinkPinQueue
{
    private static readonly object Gate = new();
    private static readonly string Folder = Path.GetDirectoryName(GameLinkProtocol.SessionTokenPath)!;
    private static readonly string QueuePath = Path.Combine(Folder, "pins.tsv");
    private static readonly Encoding Utf8 = new UTF8Encoding(false);

    internal static bool TryAppend(GameLinkPin pin)
    {
        if (!GameLinkPinFile.IsValid(pin)) return false;
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                using var stream = new FileStream(QueuePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
                if (stream.Length > 0)
                {
                    stream.Seek(-1, SeekOrigin.End);
                    if (stream.ReadByte() != '\n') stream.WriteByte((byte)'\n');
                }
                byte[] line = Utf8.GetBytes(GameLinkPinFile.WriteLine(pin) + "\n");
                stream.Write(line, 0, line.Length);
                stream.Flush(flushToDisk: true);
                return true;
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"[Gnostic Game Link] Could not save a pin: {ex}");
            return false;
        }
    }

    internal static string Handle(GameLinkRequest request)
    {
        try
        {
            lock (Gate)
            {
                if (request.Kind == GameLinkRequestKind.Pins)
                    return GameLinkReply.Pins(GameLinkPinFile.ReadBatch(Read(), request.WorldKey, request.PinId));

                var pins = Read().ToList();
                if (GameLinkPinFile.Acknowledge(pins, request.PinId))
                    Rewrite(pins);
                return GameLinkReply.PinsAcknowledged;
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"[Gnostic Game Link] Could not process the pins queue: {ex}");
            return GameLinkReply.Error("failed", "Could not process the pins queue.");
        }
    }

    private static IEnumerable<GameLinkPin> Read()
    {
        StreamReader reader;
        try { reader = File.OpenText(QueuePath); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { yield break; }
        using (reader)
            foreach (var pin in GameLinkPinFile.Read(reader))
                yield return pin;
    }

    private static void Rewrite(List<GameLinkPin> pins)
    {
        string tempPath = QueuePath + ".tmp";
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            using (var writer = new StreamWriter(stream, Utf8, 4096, leaveOpen: true))
                foreach (var pin in pins)
                    writer.WriteLine(GameLinkPinFile.WriteLine(pin));
            stream.Flush(flushToDisk: true);
        }
        File.Replace(tempPath, QueuePath, null);
    }
}
