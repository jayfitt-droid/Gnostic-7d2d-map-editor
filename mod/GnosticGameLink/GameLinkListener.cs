using System.Net;
using System.Net.Sockets;
using System.Text;
using MapEditor.Core.GameLink;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
        string sessionToken = GameLinkProtocol.CreateSessionToken();
        PublishSessionToken(sessionToken);
        new Thread(() => Listen(sessionToken)) { IsBackground = true, Name = "Gnostic Game Link" }.Start();
    }

    private static void PublishSessionToken(string sessionToken)
    {
        string path = GameLinkProtocol.SessionTokenPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, sessionToken, Encoding.ASCII);
            if (File.Exists(path))
                File.Replace(temporaryPath, path, null);
            else
                File.Move(temporaryPath, path);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
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

    private void Listen(string sessionToken)
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

                Serve(accepted, sessionToken);
                lock (gate)
                    client = null;
            }
        }
    }

    private void Serve(TcpClient connection, string sessionToken)
    {
        try
        {
            using NetworkStream stream = connection.GetStream();
            stream.WriteTimeout = GameLinkProtocol.ReadTimeoutMilliseconds;
            string reply = ReadAndDispatch(stream, sessionToken);
            byte[] bytes = Utf8.GetBytes(reply + "\n");
            stream.Write(bytes, 0, bytes.Length);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or InvalidOperationException)
        {
            // A client can close the connection while the game is still processing its request.
        }
    }

    private string ReadAndDispatch(NetworkStream stream, string sessionToken)
    {
        GameLinkParseResult result = GameLinkProtocol.ReadRequest(stream, sessionToken, out var request);
        return result switch
        {
            GameLinkParseResult.Ok => DispatchAndLog(request),
            GameLinkParseResult.InvalidToken =>
                GameLinkReply.Error("invalid_token", "Game Link could not verify the session token. Restart the game or install the mod that matches the editor."),
            GameLinkParseResult.UnsupportedProtocol =>
                GameLinkReply.Error("unsupported_protocol", $"Use Game Link protocol {GameLinkProtocol.Version}."),
            _ => GameLinkReply.Error("bad_request", "The request is invalid.")
        };
    }

    private string DispatchAndLog(GameLinkRequest request)
    {
        if (request.Kind is not (GameLinkRequestKind.Leave or GameLinkRequestKind.Open or GameLinkRequestKind.Teleport))
            return Dispatch(request);

        string details = request.Kind switch
        {
            GameLinkRequestKind.Open => OpenTarget(request.OperationId!),
            GameLinkRequestKind.Teleport => FormattableString.Invariant($" x={request.X} z={request.Z}"),
            _ => "",
        };
        string reply = Dispatch(request);
        var json = JObject.Parse(reply);
        string outcome = (bool?)json["ok"] == true ? "accepted" : (string?)json["error"] ?? "failed";
        string id = request.OperationId ?? (string?)json["id"] ?? GameLinkProtocol.CreateOperationId();
        Log.Out($"[Gnostic Game Link] {request.Kind.ToString().ToUpperInvariant()} id={id}{details} reply={outcome}");
        return reply;
    }

    private static string OpenTarget(string id) =>
        GameLinkOpenRequest.Peek(GameLinkProtocol.RequestDirectory, id) is { } target
            ? $" world={JsonConvert.ToString(target.World)} save={JsonConvert.ToString(target.Save)}"
            : " target=unavailable";

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
