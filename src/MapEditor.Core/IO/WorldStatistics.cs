using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Generate;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.IO;

/// <summary>A measured overview of a world at one point in its edit history.</summary>
public sealed record WorldStatistics(
    string Name,
    int Size,
    float MinHeightMetres,
    float MaxHeightMetres,
    double LandPercent,
    double WaterPercent,
    IReadOnlyDictionary<string, double> BiomeLandPercent,
    double AsphaltKm,
    double GravelKm,
    int Prefabs,
    int Pois,
    IReadOnlyDictionary<int, int> PoisByTier,
    int PoisWithUnknownTier,
    IReadOnlyDictionary<string, int> PoisByBiome,
    int StreetTiles,
    int Towns,
    int Traders,
    int SpawnPoints,
    (int Width, int Height, int Marked)? Radiation,
    IReadOnlyList<string> ExtraFiles,
    IReadOnlyList<string> Warnings)
{
    // The Road tool's default widths. Paint records area and has no centre line or width.
    internal const int AsphaltWidth = 18;
    internal const int GravelWidth = 9;
    private const string UnknownBiome = "Unknown";

    /// <summary>Measures a world while it is held still, for example by the CLI.</summary>
    public static WorldStatistics Measure(World world, BiomeTable biomes, PrefabLibrary library) =>
        Measure(world, biomes, library, null, default);

    /// <summary>Measures a snapshot on a worker. Consumes its grid bands in order, once.</summary>
    public static WorldStatistics Measure(
        WorldSnapshot snapshot, BiomeTable biomes, PrefabLibrary library, CancellationToken cancellationToken = default) =>
        Measure(snapshot.World, biomes, library, snapshot, cancellationToken);

    private static WorldStatistics Measure(
        World world, BiomeTable biomes, PrefabLibrary library, WorldSnapshot? snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var biomeLayer = world.Biomes;
        var names = biomeLayer?.Palette.Select(rgb => biomes.FindByColor(rgb)?.Name ?? UnknownBiome).ToArray() ?? [];
        string BiomeName(byte index) => index < names.Length ? names[index] : UnknownBiome;

        var poisByTier = new Dictionary<int, int>();
        var poisByBiome = new Dictionary<string, int>(StringComparer.Ordinal);
        var poiColumnsByRow = new Dictionary<int, List<int>>();
        var tiles = new Dictionary<(int, int), GridRect>();
        int traders = 0, streetTiles = 0, pois = 0, unknownTier = 0;
        foreach (var prefab in world.Prefabs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = library.Find(prefab.Name);
            var footprint = PrefabPlacer.Footprint(world, prefab, library);
            if (info?.IsStreetTile ?? prefab.Name.StartsWith("rwg_tile_", StringComparison.OrdinalIgnoreCase))
            {
                if (info is null)
                    footprint = new GridRect(footprint.X0, footprint.Z0,
                        footprint.X0 + TownSiteStage.Cell, footprint.Z0 + TownSiteStage.Cell);
                tiles[(footprint.X0, footprint.Z0)] = footprint;
                streetTiles++;
                continue;
            }
            if (info is { IsPart: true } or { IsInternal: true }) continue;
            if (info?.IsTrader ?? prefab.Name.StartsWith("trader_", StringComparison.OrdinalIgnoreCase)) traders++;
            pois++;
            if (info is null) unknownTier++;
            else
            {
                int tier = info.HasDifficultyTier ? info.DifficultyTier : 0;
                poisByTier[tier] = poisByTier.GetValueOrDefault(tier) + 1;
            }

            int x = footprint.X0 + (footprint.X1 - footprint.X0) / 2;
            int z = footprint.Z0 + (footprint.Z1 - footprint.Z0) / 2;
            if (biomeLayer is null || !world.Height.Contains(x, z))
            {
                poisByBiome[UnknownBiome] = poisByBiome.GetValueOrDefault(UnknownBiome) + 1;
                continue;
            }
            if (!poiColumnsByRow.TryGetValue(z, out var columns)) poiColumnsByRow[z] = columns = [];
            columns.Add(x);
        }

        var height = new BandRows<ushort>(world.Height, snapshot);
        var water = new BandRows<byte>(world.Water, snapshot);
        var biome = new BandRows<byte>(biomeLayer?.Indices, snapshot);
        int scale = biomeLayer is null ? world.Size : world.Size / biomeLayer.Indices.Width;
        var biomeLand = new long[256];
        long land = 0;
        ushort min = ushort.MaxValue, max = 0;
        for (int z = 0; z < world.Height.Height; z++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var heights = height.Row(z);
            var surfaces = water.Row(z);
            var indices = biome.Row(z / scale);
            if (poiColumnsByRow.TryGetValue(z, out var columns))
                foreach (int x in columns)
                {
                    string name = BiomeName(indices[x / scale]);
                    poisByBiome[name] = poisByBiome.GetValueOrDefault(name) + 1;
                }

            // One biome sample covers scale x scale metres. Count only dry metres in each sample.
            for (int x = 0, bx = 0; x < heights.Length; bx++)
            {
                int end = Math.Min(heights.Length, x + scale);
                int dry = end - x;
                for (; x < end; x++)
                {
                    ushort h = heights[x];
                    if (h < min) min = h;
                    if (h > max) max = h;
                    // The renderer sees water where a surface is set and the terrain is at most 0.5 m above it.
                    byte surface = surfaces.IsEmpty ? (byte)0 : surfaces[x];
                    if (surface > 0 && h <= surface * 257 + 128) dry--;
                }
                land += dry;
                if (!indices.IsEmpty) biomeLand[indices[bx]] += dry;
            }
        }

        double area = world.Height.Data.Length;
        var biomePercent = new Dictionary<string, double>(StringComparer.Ordinal);
        for (int i = 0; i < biomeLand.Length; i++)
        {
            if (biomeLand[i] == 0) continue;
            string name = BiomeName((byte)i);
            biomePercent[name] = biomePercent.GetValueOrDefault(name) + 100.0 * biomeLand[i] / land;
        }

        var roads = new BandRows<byte>(world.Roads, snapshot);
        int asphalt = 0, gravel = 0;
        for (int z = 0; z < roads.Height; z++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = roads.Row(z);
            asphalt += row.Count((byte)RoadType.Asphalt);
            gravel += row.Count((byte)RoadType.Gravel);
        }
        var radiation = new BandRows<byte>(world.Radiation, snapshot);
        int radiationPixels = 0;
        for (int z = 0; z < radiation.Height; z++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = radiation.Row(z);
            radiationPixels += row.Length - row.Count((byte)0);
        }

        return new WorldStatistics(world.Name, world.Size, World.HeightToMetres(min), World.HeightToMetres(max),
            100.0 * land / area, 100.0 * (area - land) / area, biomePercent,
            asphalt / (1000.0 * AsphaltWidth), gravel / (1000.0 * GravelWidth), world.Prefabs.Count,
            pois, poisByTier, unknownTier, poisByBiome, streetTiles, CountTowns(tiles), traders, world.Spawns.Count,
            world.Radiation is { } rad ? (rad.Width, rad.Height, radiationPixels) : null,
            world.ExtraFiles.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray(), [.. world.Warnings]);
    }

    private static int CountTowns(Dictionary<(int, int), GridRect> tiles)
    {
        Func<(int, int), bool> hasTile = tiles.ContainsKey;
        var seen = new HashSet<(int, int)>();
        var queue = new Queue<GridRect>();
        int towns = 0;
        foreach (var start in tiles.Values.OrderBy(t => t.Z0).ThenBy(t => t.X0))
        {
            if (!seen.Add((start.X0, start.Z0))) continue;
            towns++;
            queue.Enqueue(start);
            while (queue.TryDequeue(out var tile))
                for (int dir = 0; dir < 4; dir++)
                    if (HighwayStage.AdjacentStreetTile(tile, dir, hasTile) is { } next && seen.Add(next))
                        queue.Enqueue(tiles[next]);
        }
        return towns;
    }

    /// <summary>Reads rows in order, retaining one band when the grid is frozen.</summary>
    private sealed class BandRows<T>(Grid<T>? grid, WorldSnapshot? snapshot) where T : unmanaged
    {
        private const int Band = FrozenGrid<T>.Band;
        private readonly FrozenGrid<T>? _frozen = grid is null ? null : snapshot?.Find(grid);
        private T[]? _cells;
        private int _band = -1;

        public int Height => grid?.Height ?? 0;

        public ReadOnlySpan<T> Row(int z)
        {
            if (grid is null) return [];
            if (_frozen is null) return grid.Row(z);

            int band = z / Band;
            if (band != _band)
            {
                _cells ??= new T[Math.Min(Band, grid.Height) * grid.Width];
                _frozen.CopyBand(band, _cells, final: true);
                _band = band;
            }
            return _cells.AsSpan(z % Band * grid.Width, grid.Width);
        }
    }
}
