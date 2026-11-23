using System.Collections;
using System.Net.Sockets;
using MapEditor.Core.GameLink;
using UnityEngine;

namespace GnosticGameLink;

/// <summary>Bridges Game Link requests to the local player on Unity's main thread.</summary>
public sealed class GameLinkMod : IModApi
{
    private readonly GameLinkListener listener = new();
    private readonly Func<GameLinkRequest, Task<string>> handle;
    private (IEnumerator<object> Capture, TaskCompletionSource<string> Reply, float Deadline)? activePicture;

    public GameLinkMod() => handle = HandleRequestAsync;

    /// <inheritdoc />
    public void InitMod(Mod mod)
    {
        try
        {
            listener.Start();
        }
        catch (SocketException ex)
        {
            listener.Dispose();
            Log.Warning($"[Gnostic Game Link] Could not listen on 127.0.0.1:{GameLinkProtocol.Port}: {ex.Message}");
            return;
        }

        ModEvents.UnityUpdate.RegisterHandler(OnUpdate);
        ModEvents.GameShutdown.RegisterHandler(OnShutdown);
        Log.Out($"[Gnostic Game Link] Listening on 127.0.0.1:{GameLinkProtocol.Port}.");
    }

    private void OnUpdate(ref ModEvents.SUnityUpdateData data)
    {
        if (activePicture is { } picture && Time.realtimeSinceStartup >= picture.Deadline)
        {
            Log.Warning("[Gnostic Game Link] Picture timed out while waiting for the game to render.");
            StopPicture();
        }
        listener.Update(handle);
    }

    private void OnShutdown(ref ModEvents.SGameShutdownData data)
    {
        listener.Dispose();
        StopPicture();
    }

    private void StopPicture()
    {
        if (activePicture is not { } picture) return;
        activePicture = null;
        try
        {
            picture.Capture.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warning($"[Gnostic Game Link] Could not restore the view: {ex}");
        }
        finally
        {
            picture.Reply.TrySetResult(GameLinkCapture.Failure);
        }
    }

    private async Task<string> HandleRequestAsync(GameLinkRequest request)
    {
        GameManager? game = GameManager.Instance;
        World? world = game == null ? null : game.World;
        EntityPlayerLocal? player = world?.GetPrimaryPlayer();
        var connection = ConnectionManager.Instance;
        bool hosting = connection != null && connection.IsServer;
        string state = GetState(game != null && game.IsStartingGame, connection != null && connection.IsClient,
            hosting, world, player);

        if (request.Kind == GameLinkRequestKind.Status)
        {
            string worldName = GamePrefs.GetString(EnumGamePrefs.GameWorld) ?? string.Empty;
            if (state == "ready" && player != null)
            {
                Vector3 position = player.GetPosition();
                // The game's compass uses rotation.y: north is 0 degrees and east is 90.
                return GameLinkReply.Ready(worldName, position.x, position.z, Mathf.Repeat(player.rotation.y, 360f));
            }

            return GameLinkReply.Status(state, worldName, hosting);
        }

        if (state == "remote")
            return GameLinkReply.Error("remote", "Game Link works in single-player or when you host the game.");
        if (state == "dead")
            return GameLinkReply.Error("dead", "The player is dead.");
        if (request.Kind is GameLinkRequestKind.Teleport or GameLinkRequestKind.Picture && hosting && player != null &&
            !player.IsSpawned() && player.MoveController?.respawnReason == RespawnType.Teleport)
            return GameLinkReply.Error("busy", "The player is still teleporting.");
        if (state != "ready" || !hosting || world == null || player == null)
            return GameLinkReply.Error("not_ready", "The player is not in the world.");

        if (!world.GetWorldExtent(out Vector3i min, out Vector3i max))
            return GameLinkReply.Error("not_ready", "The world bounds are not available.");
        bool Inside(int x, int z) => x >= min.x && x <= max.x && z >= min.z && z <= max.z;
        if (!Inside(request.X, request.Z))
            return GameLinkReply.Error("off_map", "The destination is outside the world.");

        if (request.Kind == GameLinkRequestKind.Picture)
        {
            if (!Inside(request.LookX, request.LookZ))
                return GameLinkReply.Error("off_map", "The point to look at is outside the world.");
            // The camera can rise above the buildable blocks. Limit vertical coordinates to world scale.
            int span = Math.Max(max.x - min.x, max.z - min.z);
            if (request.Y < min.y - span || request.Y > max.y + span ||
                request.LookY < min.y - span || request.LookY > max.y + span)
                return GameLinkReply.Error("off_map", "The picture height is outside the world.");
            if (request.X == request.LookX && request.Y == request.LookY && request.Z == request.LookZ)
                return GameLinkReply.Error("bad_request", "The camera and the point to look at must differ.");

            var reply = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var capture = GameLinkCapture.Take(world, player, request, min, max, reply);
            activePicture = (capture, reply, Time.realtimeSinceStartup + GameLinkCapture.MaximumRenderSeconds + 5f);
            try
            {
                ThreadManager.StartCoroutine(RunPicture(capture, reply));
                return await reply.Task;
            }
            finally
            {
                activePicture = null;
            }
        }

        return request.Kind == GameLinkRequestKind.Teleport
            ? Teleport(request)
            : SetWaypoint(world, player, request);
    }

    private static IEnumerator RunPicture(IEnumerator<object> capture, TaskCompletionSource<string> reply)
    {
        try
        {
            while (AdvancePicture(capture, reply))
                yield return capture.Current;
        }
        finally
        {
            capture.Dispose();
            if (reply.TrySetResult(GameLinkCapture.Failure))
                Log.Warning("[Gnostic Game Link] Picture ended before the game could capture a frame.");
        }
    }

    private static bool AdvancePicture(IEnumerator<object> capture, TaskCompletionSource<string> reply)
    {
        try
        {
            return capture.MoveNext();
        }
        catch (Exception ex)
        {
            Log.Warning($"[Gnostic Game Link] Picture failed: {ex}");
            reply.TrySetResult(GameLinkCapture.Failure);
            return false;
        }
    }

    private static string GetState(bool starting, bool remote, bool hosting, World? world, EntityPlayerLocal? player)
    {
        if (remote || (world != null && !hosting))
            return "remote";
        if (world == null)
            return starting ? "loading" : "menu";
        if (player != null && player.IsDead())
            return "dead";
        return player != null && player.IsSpawned() ? "ready" : "loading";
    }

    private static string Teleport(GameLinkRequest request)
    {
        string command = FormattableString.Invariant($"teleport {request.X} {request.Z}");
        if (request.Facing != null)
            command += " " + request.Facing;

        List<string>? output = SdtdConsole.Instance.ExecuteSync(command, null);
        if (output == null || output.Any(line => line.IndexOf("*** ERROR", StringComparison.Ordinal) >= 0))
            return GameLinkReply.Error("failed", "The game could not teleport the player.");
        return GameLinkReply.Teleported;
    }

    private static string SetWaypoint(World world, EntityPlayerLocal player, GameLinkRequest request)
    {
        DynamicPrefabDecorator? decorator = world.ChunkCache.ChunkProvider.GetDynamicPrefabDecorator();
        var prefabs = new List<PrefabInstance>();
        decorator?.GetWorldPrefabs(prefabs);
        foreach (PrefabInstance instance in prefabs)
        {
            if (!string.Equals(instance.prefab?.PrefabName, request.PrefabName, StringComparison.OrdinalIgnoreCase))
                continue;

            Vector3i origin = instance.boundingBoxPosition;
            Vector3i size = instance.boundingBoxSize;
            if (request.X < origin.x || request.X >= (long)origin.x + size.x ||
                request.Z < origin.z || request.Z >= (long)origin.z + size.z)
                continue;

            return POIWaypoint.TrySet(player, instance.id, false)
                ? GameLinkReply.WaypointSet
                : GameLinkReply.Error("already_set", "This POI already has a temporary waypoint.");
        }

        return GameLinkReply.Error("poi_not_found", "No POI with that name was found at this location.");
    }
}
