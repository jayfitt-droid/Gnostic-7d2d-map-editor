using System.Net;
using System.Net.Sockets;
using System.Text;
using MapEditor.Core.GameLink;

namespace GnosticGameLink;

/// <summary>Serves one loopback connection at a time and hands off requests to the game thread.</summary>
internal sealed class GameLinkListener : IDisposable
{
    private const int UpdateTimeoutMilliseconds = 2000;
    private const int AcceptRetryMilliseconds = 100;
    private static readonly Encoding Utf8 = new UTF8Encoding(false);
    private readonly TcpListener listener = new(IPAddress.Loopback, GameLinkProtocol.Port);
    private readonly object gate = new();
    private volatile bool stopped;
    private TcpClient? client;
    private PendingRequest? pending;
    private PendingRequest? active;

    private sealed class PendingRequest(GameLinkRequest request)
    {
        public GameLinkRequest Request { get; } = request;
        public TaskCompletionSource<string> Reply { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void Start()
    {
        listener.ExclusiveAddressUse = true;
        listener.Start(backlog: 1);
        new Thread(Listen) { IsBackground = true, Name = "Gnostic Game Link" }.Start();
    }

    /// <summary>Takes at most one request on Unity's main thread.</summary>
    public async void Update(Func<GameLinkRequest, Task<string>> handle)
    {
        PendingRequest? work;
        lock (gate)
        {
            work = pending;
            pending = null;
            if (work != null) active = work;
        }

        if (work == null)
            return;

        string reply;
        try
        {
            reply = await handle(work.Request).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            reply = GameLinkReply.Error("failed", "The game could not process the request.");
            Log.Warning($"[Gnostic Game Link] Request failed: {ex}");
        }

        lock (gate)
        {
            active = null;
            work.Reply.TrySetResult(reply);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (stopped)
                return;
            stopped = true;
            listener.Stop();
            client?.Close();
            string reply = GameLinkReply.Error("not_ready", "The game is shutting down.");
            pending?.Reply.TrySetResult(reply);
            active?.Reply.TrySetResult(reply);
            pending = null;
        }
    }

    private void Listen()
    {
        while (!stopped)
        {
            TcpClient accepted;
            try
            {
                accepted = listener.AcceptTcpClient();
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException or InvalidOperationException)
            {
                if (stopped) return;
                Log.Warning($"[Gnostic Game Link] Could not accept a connection: {ex.Message}");
                if (ex is ObjectDisposedException)
                {
                    Dispose();
                    return;
                }
                Thread.Sleep(AcceptRetryMilliseconds);
                continue;
            }

            using (accepted)
            {
                lock (gate)
                {
                    if (stopped)
                        return;
                    client = accepted;
                }

                Serve(accepted);
                lock (gate)
                    client = null;
            }
        }
    }

    private void Serve(TcpClient connection)
    {
        try
        {
            using NetworkStream stream = connection.GetStream();
            stream.WriteTimeout = GameLinkProtocol.ReadTimeoutMilliseconds;
            string reply = ReadAndDispatch(stream);
            byte[] bytes = Utf8.GetBytes(reply + "\n");
            stream.Write(bytes, 0, bytes.Length);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or InvalidOperationException)
        {
            // A client can close the connection while the game is still processing its request.
        }
    }

    private string ReadAndDispatch(NetworkStream stream)
    {
        GameLinkParseResult result;
        GameLinkRequest request;
        try
        {
            result = GameLinkProtocol.ReadRequest(stream, out request);
        }
        catch (IOException)
        {
            return GameLinkReply.Error("bad_request", "The request could not be read within two seconds.");
        }

        return result switch
        {
            GameLinkParseResult.Ok => Dispatch(request),
            GameLinkParseResult.UnsupportedProtocol =>
                GameLinkReply.Error("unsupported_protocol", $"Use Game Link protocol {GameLinkProtocol.Version}."),
            _ => GameLinkReply.Error("bad_request", "The request is invalid.")
        };
    }

    private string Dispatch(GameLinkRequest request)
    {
        var work = new PendingRequest(request);
        lock (gate)
        {
            if (stopped)
                return GameLinkReply.Error("not_ready", "The game is shutting down.");
            pending = work;
        }

        if (!work.Reply.Task.Wait(UpdateTimeoutMilliseconds))
        {
            lock (gate)
            {
                if (pending == work)
                {
                    // Remove an unclaimed request so it cannot run after returning a timeout.
                    pending = null;
                    return GameLinkReply.Error("not_ready", "The game did not respond.");
                }
            }
        }

        // Once Unity starts the request, wait for its actual result.
        return work.Reply.Task.GetAwaiter().GetResult();
    }
}
