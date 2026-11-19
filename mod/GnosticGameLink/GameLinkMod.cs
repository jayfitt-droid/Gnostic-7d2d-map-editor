using System.Globalization;
using System.Net.Sockets;
using MapEditor.Core.GameLink;
using UnityEngine;

namespace GnosticGameLink;

/// <summary>Bridges Game Link requests to the local player on Unity's main thread.</summary>
public sealed class GameLinkMod : IModApi
{
    private readonly GameLinkListener listener = new();
    private static readonly Func<GameLinkRequest, string> Handle = HandleRequest;

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

    private void OnUpdate(ref ModEvents.SUnityUpdateData data) => listener.Update(Handle);

    private void OnShutdown(ref ModEvents.SGameShutdownData data) => listener.Dispose();

    private static string HandleRequest(GameLinkRequest request)
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
        if (request.Kind == GameLinkRequestKind.Teleport && hosting && player != null &&
            !player.IsSpawned() && player.MoveController?.respawnReason == RespawnType.Teleport)
            return GameLinkReply.Error("busy", "The player is still teleporting.");
        if (state != "ready" || !hosting || world == null || player == null)
            return GameLinkReply.Error("not_ready", "The player is not in the world.");

        if (!world.GetWorldExtent(out Vector3i min, out Vector3i max))
            return GameLinkReply.Error("not_ready", "The world bounds are not available.");
        if (request.X < min.x || request.X > max.x || request.Z < min.z || request.Z > max.z)
            return GameLinkReply.Error("off_map", "The destination is outside the world.");

        return request.Kind == GameLinkRequestKind.Teleport
            ? Teleport(request)
            : SetWaypoint(world, player, request);
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
            if (!string.Equals(instance.prefab?.PrefabName, request.PrefabName, StringComparison.Ordinal))
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
