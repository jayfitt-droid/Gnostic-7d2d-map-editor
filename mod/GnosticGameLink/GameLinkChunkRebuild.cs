using MapEditor.Core.GameLink;
using UnityEngine;

namespace GnosticGameLink;

/// <summary>Resets selected save chunks through the game's region and generation APIs.</summary>
internal static class GameLinkChunkRebuild
{
    public static GameLinkRebuildArea.Footprint[] Prefabs(IChunkProvider provider)
    {
        var prefabs = new List<PrefabInstance>();
        provider.GetDynamicPrefabDecorator()?.GetAllPrefabs(prefabs);
        return prefabs.Select(prefab =>
        {
            var start = prefab.boundingBoxPosition;
            var end = start + prefab.boundingBoxSize;
            return new GameLinkRebuildArea.Footprint(start.x, start.z, end.x, end.z);
        }).ToArray();
    }

    public static int Run(GameManager game, World world, EntityPlayerLocal player, GameLinkRebuildRequest request,
        GameLinkRebuildArea.Footprint[] previousPrefabs)
    {
        var cache = world.ChunkCache;
        if (cache.ChunkProvider is not ChunkProviderGenerateWorld provider || provider.m_RegionFileManager == null ||
            !world.GetWorldExtent(out var min, out var max))
            return 0;

        var bounds = new GameLinkRebuildArea.Footprint(min.x, min.z, max.x, max.z);
        var positions = GameLinkRebuildArea.Chunks(request.X, request.Z, request.Radius, bounds,
            previousPrefabs.Concat(Prefabs(provider)));
        if (positions.Count == 0) return 0;

        var keys = new HashSetLong();
        foreach (var (x, z) in positions) keys.Add(WorldChunkCache.MakeChunkKey(x, z));
        long centre = Key(World.worldToBlockPos(new Vector3(request.X, 0, request.Z)));
        if (player.AttachedToEntity != null) player.Detach();

        // Remove live instances before generation can add the new prefab entities.
        var entities = world.Entities.list
            .Where(entity => entity is not EntityPlayer && keys.Contains(Key(World.worldToBlockPos(entity.GetPosition()))))
            .ToArray();
        foreach (var entity in entities)
            world.RemoveEntity(entity.entityId, EnumRemoveEntityReason.Despawned);

        ClearPlayerMarkers(game.GetPersistentPlayerList(), keys);
        LockManager.Instance.ForceUnlockByChunk(keys);
        provider.RemoveChunks(keys);

        // Generate loaded chunks in place. The regular provider generates the rest on demand.
        bool generated = true;
        foreach (long key in keys)
            if ((key == centre || cache.ContainsChunkSync(key)) && !provider.GenerateSingleChunk(cache, key, true))
                generated = false;
        world.m_ChunkManager.ResendChunksToClients(keys);
        if (!generated) return 0;

        player.TeleportToPosition(new Vector3(request.X, -1f, request.Z));
        return keys.Count;
    }

    private static void ClearPlayerMarkers(PersistentPlayerList players, HashSetLong keys)
    {
        bool changedBedroll = false;
        foreach (var data in players.Players.Values)
        {
            var claims = data.GetLandProtectionBlocks();
            for (int i = claims.Count - 1; i >= 0; i--)
                if (keys.Contains(Key(claims[i]))) players.RemoveLandProtectionBlock(claims[i]);

            if (!data.HasBedrollPos || !keys.Contains(Key(data.BedrollPos))) continue;
            data.ClearBedroll();
            data.BedrollPos = new Vector3i(0, PersistentPlayerData.cBedrollUnsetY, 0);
            changedBedroll = true;
        }
        if (changedBedroll) players.SavePersistentPlayerData();
    }

    private static long Key(Vector3i block) =>
        WorldChunkCache.MakeChunkKey(World.toChunkXZ(block.x), World.toChunkXZ(block.z));
}
