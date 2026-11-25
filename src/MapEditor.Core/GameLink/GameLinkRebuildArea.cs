namespace MapEditor.Core.GameLink;

/// <summary>Chunk selection shared by the editor and the game.</summary>
public static class GameLinkRebuildArea
{
    public const int MinRadius = 16;
    public const int MaxRadius = 512;
    public const int DefaultRadius = 128;
    private const int ChunkShift = 4;

    /// <summary>A block rectangle with exclusive upper bounds.</summary>
    public readonly struct Footprint(int minX, int minZ, int maxX, int maxZ)
    {
        public int MinX { get; } = minX;
        public int MinZ { get; } = minZ;
        public int MaxX { get; } = maxX;
        public int MaxZ { get; } = maxZ;
    }

    public static int ClampRadius(int radius) => Math.Min(MaxRadius, Math.Max(MinRadius, radius));

    internal static bool IsValidPosition(float value) =>
        value >= int.MinValue && (double)value < int.MaxValue;

    /// <summary>Rounds the square outward and keeps all POIs that share a selected chunk whole.</summary>
    public static HashSet<(int X, int Z)> Chunks(float x, float z, int radius, Footprint world,
        IEnumerable<Footprint> prefabs)
    {
        var chunks = new HashSet<(int X, int Z)>();
        if (!IsValidPosition(x) || !IsValidPosition(z) ||
            x < world.MinX || x >= world.MaxX || z < world.MinZ || z >= world.MaxZ)
            return chunks;

        radius = ClampRadius(radius);
        var first = new Footprint(
            (int)Math.Max(world.MinX, Math.Floor((double)x - radius)),
            (int)Math.Max(world.MinZ, Math.Floor((double)z - radius)),
            (int)Math.Min(world.MaxX, Math.Ceiling((double)x + radius)),
            (int)Math.Min(world.MaxZ, Math.Ceiling((double)z + radius)));
        chunks.UnionWith(InBounds(first, world));

        var prefabsByChunk = new Dictionary<(int X, int Z), List<(int X, int Z)[]>>();
        foreach (var prefab in prefabs)
        {
            var footprint = InBounds(prefab, world).ToArray();
            foreach (var chunk in footprint)
            {
                if (!prefabsByChunk.TryGetValue(chunk, out var adjacent))
                    prefabsByChunk.Add(chunk, adjacent = new List<(int X, int Z)[]>());
                adjacent.Add(footprint);
            }
        }

        var pending = new Queue<(int X, int Z)>(chunks);
        var visited = new HashSet<(int X, int Z)[]>();
        while (pending.Count != 0)
        {
            if (!prefabsByChunk.TryGetValue(pending.Dequeue(), out var adjacent)) continue;
            foreach (var footprint in adjacent)
            {
                if (!visited.Add(footprint)) continue;
                foreach (var chunk in footprint)
                    if (chunks.Add(chunk)) pending.Enqueue(chunk);
            }
        }
        return chunks;
    }

    private static IEnumerable<(int X, int Z)> InBounds(Footprint area, Footprint world)
    {
        int minX = Math.Max(area.MinX, world.MinX), maxX = Math.Min(area.MaxX, world.MaxX);
        int minZ = Math.Max(area.MinZ, world.MinZ), maxZ = Math.Min(area.MaxZ, world.MaxZ);
        if (minX >= maxX || minZ >= maxZ) yield break;

        // Arithmetic right shift floors negative block coordinates to the containing chunk.
        for (int z = minZ >> ChunkShift; z <= (maxZ - 1) >> ChunkShift; z++)
            for (int x = minX >> ChunkShift; x <= (maxX - 1) >> ChunkShift; x++)
                yield return (x, z);
    }
}
