using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Generate;

/// <summary>What RWG raises on a 256 m tile.</summary>
public enum TerrainType : byte
{
    None,
    Plains,
    Hills,
    Mountains,
}

/// <summary>RWG's five biomes, in its BiomeType order. Biomes it doesn't know (mods) count as forest, as RWG's none does.</summary>
public enum RwgBiome
{
    Forest,
    BurntForest,
    Desert,
    Snow,
    Wasteland,
}

/// <summary>How much of a biome's tiles get plains, hills and mountains; the weights are compared with each other.</summary>
public sealed record TerrainWeights(int Plains = 4, int Hills = 4, int Mountains = 2)
{
    /// <summary>RWG's defaults (WorldBuilder's Plains, Hills, Mountains).</summary>
    public static TerrainWeights Rwg { get; } = new();
}

/// <summary>The terrain stage's settings.</summary>
public sealed class TerrainOptions
{
    /// <summary>Weights for every biome without its own.</summary>
    public TerrainWeights Weights { get; set; } = TerrainWeights.Rwg;

    /// <summary>
    /// A biome's own weights. Wasteland without its own gets no mountains: RWG turns its mountain tiles to plains
    /// after counting them.
    /// </summary>
    public Dictionary<RwgBiome, TerrainWeights> BiomeWeights { get; set; } = [];

    public int Seed { get; set; } = 1;

    /// <summary>Ground height before any stamp (RWG's 35 m).</summary>
    public float BaseHeight { get; set; } = 35;

    /// <summary>North, east, south, west: land or ocean borders, redrawn when the whole map is generated.</summary>
    public EdgeStyle[] Edges { get; set; } = [EdgeStyle.Land, EdgeStyle.Land, EdgeStyle.Land, EdgeStyle.Land];

    /// <summary>The land's outline. An island, archipelago or peninsula is used instead of <see cref="Edges"/>.</summary>
    public LandShape Shape { get; set; } = new();

    public TerrainWeights WeightsFor(RwgBiome biome) =>
        BiomeWeights.TryGetValue(biome, out var own) ? own
        : biome == RwgBiome.Wasteland ? Weights with { Mountains = 0 }
        : Weights;

    public TerrainOptions Clone() => new()
    {
        Weights = Weights,
        BiomeWeights = new(BiomeWeights),
        Seed = Seed,
        BaseHeight = BaseHeight,
        Edges = [.. Edges],
        Shape = Shape.Clone(),
    };
}

/// <summary>Biome cells a terrain run painted with its mountains' biome mask, so a re-run can put them back.</summary>
public sealed class BiomeMaskRecord
{
    private readonly Dictionary<int, (byte Old, byte Set)> _cells = [];

    public int Count => _cells.Count;

    public BiomeMaskRecord Clone()
    {
        var copy = new BiomeMaskRecord();
        foreach (var (cell, entry) in _cells) copy._cells[cell] = entry;
        return copy;
    }

    internal void Note(int cell, byte old, byte set)
    {
        if (_cells.TryGetValue(cell, out var had)) _cells[cell] = (had.Old, set);
        else if (old != set) _cells[cell] = (old, set);
    }

    /// <summary>
    /// Puts back the cells inside <paramref name="area"/> (biome cells; null for all) that still hold what the mask set,
    /// so paint added since is kept. Returns the cells put back.
    /// </summary>
    internal List<int> Revert(Grid<byte> indices, GridRect? area)
    {
        var back = new List<int>();
        foreach (var (cell, (old, set)) in _cells)
        {
            int x = cell % indices.Width, z = cell / indices.Width;
            if (area is { } a && !a.Contains(x, z)) continue;
            if (indices.Data[cell] == set) indices.Data[cell] = old;
            back.Add(cell);
        }
        foreach (var cell in back) _cells.Remove(cell);
        return back;
    }

    internal IEnumerable<int> Cells => _cells.Keys;
}

/// <summary>What a terrain run did; <paramref name="BiomesPlanned"/> when it planned the biomes again for the land.</summary>
public sealed record TerrainResult(TerrainType[] Plan, int Stamps, BiomeMaskRecord Mask, bool BiomesPlanned = false);

/// <summary>
/// RWG's terrain (WorldBuilder.GenerateTerrainTiles, GenerateBaseStamps and GenerateTerrainFromTiles): each 256 m tile
/// is given plains, hills or mountains by the weights, then clusters of the game's stamps raise them, plains in
/// 1024 m blocks, hills in 512 m blocks and mountains per tile. Mountains also spread their biome where their stamp
/// reaches. Not seed-exact with RWG; the same seed and settings give the same terrain here.
/// </summary>
public static class TerrainStage
{
    public const int TileSize = 256;

    /// <summary>Metres the selected area blends into the terrain around it.</summary>
    public const int Feather = 128;

    private static readonly (int X, int Z)[] Directions = [(0, 1), (1, 0), (0, -1), (-1, 0)];

    /// <summary>The selected area in metres, including its blend band and limited to the world.</summary>
    internal static GridRect BlendRegion(GridRect areaMetres, int worldSize) =>
        new GridRect(areaMetres.X0 - Feather, areaMetres.Z0 - Feather, areaMetres.X1 + Feather, areaMetres.Z1 + Feather)
            .Clamp(worldSize, worldSize);

    /// <summary>
    /// Generates the terrain into <paramref name="world"/> as part of <paramref name="session"/>.
    /// </summary>
    /// <param name="painted">Tile types painted by hand (x + z * tiles, z from the south); generation fills the rest.</param>
    /// <param name="area">Tiles to generate (in 256 m tiles); null for the whole map, which also resets the ground and redraws the edges.</param>
    /// <param name="previousMask">The last run's biome mask, put back first so re-runs don't pile up.</param>
    /// <param name="replanBiomes">
    /// A biome layout to plan again, fitted to the land shape, before the terrain (whole-map runs only). It replaces the
    /// biome map, hand painting included.
    /// </param>
    public static TerrainResult Run(World world, TerrainOptions options, StampLibrary stamps, RwgMixer mixer,
        TerrainType[]? painted, GridRect? area, BiomeMaskRecord? previousMask, EditSession session,
        IProgress<double>? progress = null, CancellationToken cancel = default, BiomeLayoutSettings? replanBiomes = null)
    {
        int tiles = world.Size / TileSize;
        if (painted is not null && painted.Length != tiles * tiles) painted = null;
        var selected = (area ?? new GridRect(0, 0, tiles, tiles)).Clamp(tiles, tiles);
        if (selected.IsEmpty) throw new ArgumentException("The area holds no whole 256 m tile.", nameof(area));

        var biomes = world.Biomes ?? throw new InvalidOperationException("The world has no biome map.");
        var biomeGrid = biomes.Indices;
        int cell = world.Size / biomeGrid.Width;
        var areaMetres = selected.Scale(TileSize);
        GridRect? biomeArea = area is null ? null
            : new GridRect(areaMetres.X0 / cell, areaMetres.Z0 / cell, areaMetres.X1 / cell, areaMetres.Z1 / cell);

        // Put back the last run's mountain biome first, so tiles keep the biome they had and re-runs don't spread it.
        // A copy, so a run that fails leaves the caller's record as it was.
        var mask = previousMask?.Clone() ?? new BiomeMaskRecord();
        session.Touch(biomeGrid, new GridRect(0, 0, biomeGrid.Width, biomeGrid.Height), cell);
        mask.Revert(biomeGrid, biomeArea);

        var land = LandShapes.Build(world.Size, options.Shape);
        bool biomesPlanned = area is null && replanBiomes is not null && ReplanBiomes(biomes, world.Size, replanBiomes, stamps, land);
        cancel.ThrowIfCancellationRequested();

        var tileBiome = TileBiomes(world, tiles);
        var rand = new Random(options.Seed);

        // Tiles that are mostly sea get no terrain, so nothing rises out of the water.
        bool[]? sea = null;
        if (land is not null)
        {
            var share = land.TileLand(TileSize);
            sea = new bool[tiles * tiles];
            for (int z = 0; z < tiles; z++)
                for (int x = 0; x < tiles; x++)
                    sea[x + z * tiles] = share[x, z] < 0.5f;
        }

        var plan = Plan(tiles, tileBiome, options, painted, selected, rand, sea);
        cancel.ThrowIfCancellationRequested();

        // Every stamp, in RWG's order: all plains, then hills, then mountains.
        var draws = new List<(StampImage Stamp, StampPlacement At, TerrainRule Rule, byte BiomeIndex)>();
        foreach (var (type, block) in new[] { (TerrainType.Plains, 4), (TerrainType.Hills, 2), (TerrainType.Mountains, 1) })
            AddClusters(draws, type, block, tiles, plan, selected, tileBiome, stamps, mixer, rand);

        // Where the stamps are drawn: the world itself, or for an area a scratch copy blended in afterwards.
        var region = area is null ? new GridRect(0, 0, world.Size, world.Size)
            : BlendRegion(areaMetres, world.Size);
        session.Touch(world.Height, region);

        Grid<ushort> target;
        if (area is null)
        {
            target = world.Height;
            ushort baseRaw = TerrainBrush.ToRaw(options.BaseHeight);
            Array.Fill(target.Data, baseRaw);
            ClearWater(world.Water ??= new Grid<byte>(world.Size, world.Size), session);
            // A land shape decides the edges itself.
            if (land is null) NewWorldGenerator.AddBorders(world, options.Edges, stamps, new Random(options.Seed + 1), session);
        }
        else
        {
            target = new Grid<ushort>(region.X1 - region.X0, region.Z1 - region.Z0);
            Array.Fill(target.Data, TerrainBrush.ToRaw(options.BaseHeight));
        }

        for (int i = 0; i < draws.Count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            var (stamp, at, rule, biomeIndex) = draws[i];
            var local = area is null ? at : at with { X = at.X - region.X0, Z = at.Z - region.Z0 };
            StampOp.Draw(target, null, stamp, local);
            if (rule.UseBiomeMask) PaintBiomeMask(biomeGrid, cell, stamp, at, rule.BiomeMaskMin, biomeIndex, biomeArea, mask);
            progress?.Report((i + 1) / (double)draws.Count);
        }

        // RWG keeps the ground at least 2 m up (AdjustHeights).
        ushort floor = TerrainBrush.ToRaw(2);
        if (area is null)
        {
            Parallel.For(0, target.Height, z =>
            {
                foreach (ref var h in target.Row(z)) if (h < floor) h = floor;
            });
            if (land is not null) LandShapes.Press(target, land, region);
            StampOp.FinalizeWater(world.Height, world.Water!, region);
            if (land is not null) LandShapes.FillSea(world.Height, world.Water!, land, region, session);
        }
        else
        {
            if (land is not null) LandShapes.Press(target, land, region, region.X0, region.Z0);
            Blend(world.Height, target, region, areaMetres, floor);
            if (world.Water is null && land is not null) world.Water = new Grid<byte>(world.Size, world.Size);
            if (world.Water is { } water)
            {
                session.Touch(water, region);
                StampOp.FinalizeWater(world.Height, water, region);
                if (land is not null) LandShapes.FillSea(world.Height, water, land, region);
            }
        }
        return new TerrainResult(plan, draws.Count, mask, biomesPlanned);
    }

    /// <summary>
    /// Plans the biome layout again for <paramref name="shape"/> on the ground as it is, with no terrain run: the
    /// Biome seed's Re-roll, which leaves a terrain the user likes alone (#26). Unlike RWG, biomes may then sit on
    /// ground made for others (mountains in wasteland, say): that is the user's choice. False (and nothing changed) if
    /// the world has no biome map or it isn't the layout's size.
    /// </summary>
    public static bool PlanBiomes(World world, BiomeLayoutSettings layout, LandShape shape, StampLibrary stamps, EditSession session)
    {
        if (world.Biomes is not { } biomes) return false;
        var grid = biomes.Indices;
        int cell = world.Size / grid.Width;
        session.Touch(grid, new GridRect(0, 0, grid.Width, grid.Height), cell);
        return ReplanBiomes(biomes, world.Size, layout, stamps, LandShapes.Build(world.Size, shape));
    }

    /// <summary>
    /// Plans the biome layout again, fitted to <paramref name="land"/>, into the world's biome map (keeping its palette,
    /// adding any colour it lacks). False if the biome map isn't the layout's size (an imported world).
    /// </summary>
    private static bool ReplanBiomes(IndexedLayer biomes, int size, BiomeLayoutSettings layout, StampLibrary stamps, LandField? land)
    {
        if (layout.Shares.All(s => s.Weight <= 0)) return false;
        var fresh = NewWorldGenerator.GenerateBiomes(size, layout.ToPlan(), stamps, layout.Seed, land);
        var grid = biomes.Indices;
        if (fresh.Indices.Width != grid.Width || fresh.Indices.Height != grid.Height) return false;
        var map = new byte[fresh.Palette.Count];
        for (int i = 0; i < map.Length; i++)
        {
            int at = biomes.Palette.IndexOf(fresh.Palette[i]);
            if (at < 0 && biomes.Palette.Count < 256)
            {
                biomes.Palette.Add(fresh.Palette[i]);
                at = biomes.Palette.Count - 1;
            }
            map[i] = (byte)Math.Max(0, at);
        }
        for (int i = 0; i < grid.Data.Length; i++) grid.Data[i] = map[fresh.Indices.Data[i]];
        return true;
    }

    /// <summary>Clears all water, keeping only the wet 64 m blocks for undo (a dry 16k map would otherwise keep 256 MiB).</summary>
    private static void ClearWater(Grid<byte> water, EditSession session)
    {
        const int block = EditSession.TileSize;
        for (int z0 = 0; z0 < water.Height; z0 += block)
            for (int x0 = 0; x0 < water.Width; x0 += block)
            {
                var rect = new GridRect(x0, z0, Math.Min(x0 + block, water.Width), Math.Min(z0 + block, water.Height));
                bool wet = false;
                for (int z = rect.Z0; z < rect.Z1 && !wet; z++)
                    wet = water.Row(z)[rect.X0..rect.X1].ContainsAnyExcept((byte)0);
                if (!wet) continue;
                session.Touch(water, rect);
                for (int z = rect.Z0; z < rect.Z1; z++) water.Row(z)[rect.X0..rect.X1].Clear();
            }
    }

    /// <summary>The RWG biome and biome-map palette index of each 256 m tile: the colour most of its cells hold.</summary>
    internal static (RwgBiome Biome, byte Index)[] TileBiomes(World world, int tiles)
    {
        var grid = world.Biomes!.Indices;
        var palette = world.Biomes.Palette;
        int cellsPerTile = Math.Max(1, grid.Width / tiles);
        var result = new (RwgBiome, byte)[tiles * tiles];
        Span<int> counts = stackalloc int[256];
        for (int tz = 0; tz < tiles; tz++)
            for (int tx = 0; tx < tiles; tx++)
            {
                counts.Clear();
                for (int z = tz * cellsPerTile; z < Math.Min(grid.Height, (tz + 1) * cellsPerTile); z++)
                    for (int x = tx * cellsPerTile; x < Math.Min(grid.Width, (tx + 1) * cellsPerTile); x++)
                        counts[grid.Data[x + z * grid.Width]]++;
                int best = 0;
                for (int i = 1; i < 256; i++) if (counts[i] > counts[best]) best = i;
                uint colour = best < palette.Count ? palette[best] : BiomeLayout.ForestColor;
                result[tx + tz * tiles] = (ToRwg(colour), (byte)best);
            }
        return result;
    }

    public static RwgBiome ToRwg(uint colour) => colour switch
    {
        BiomeLayout.BurntColor => RwgBiome.BurntForest,
        BiomeLayout.DesertColor => RwgBiome.Desert,
        BiomeLayout.SnowColor => RwgBiome.Snow,
        BiomeLayout.WastelandColor => RwgBiome.Wasteland,
        _ => RwgBiome.Forest,
    };

    /// <summary>RWG's name for a biome, as used in rwgmixer classes and stamp names.</summary>
    public static string RwgName(RwgBiome biome) => biome switch
    {
        RwgBiome.BurntForest => "burntForest",
        RwgBiome.Desert => "desert",
        RwgBiome.Snow => "snow",
        RwgBiome.Wasteland => "wasteland",
        _ => "forest",
    };

    private static string RwgName(TerrainType type) => type switch
    {
        TerrainType.Hills => "hills",
        TerrainType.Mountains => "mountains",
        _ => "plains",
    };

    /// <summary>
    /// Gives each selected, unpainted tile a type (GenerateTerrainTiles): per biome, the tiles are split by the
    /// weights, then taken in random order; hills fill aligned 2×2 blocks, and a mountain carries on into a free
    /// neighbour 80% of the time.
    /// </summary>
    /// <param name="sea">Tiles left without terrain because they are sea (x + z * tiles); null for none.</param>
    internal static TerrainType[] Plan(int tiles, (RwgBiome Biome, byte Index)[] tileBiome, TerrainOptions options, TerrainType[]? painted,
        GridRect selected, Random rand, bool[]? sea = null)
    {
        var plan = new TerrainType[tiles * tiles];
        if (painted is not null) Array.Copy(painted, plan, plan.Length);
        bool Free(int x, int z) => selected.Contains(x, z) && (painted is null || painted[x + z * tiles] == TerrainType.None)
                                   && (sea is null || !sea[x + z * tiles]);

        foreach (var biome in Enum.GetValues<RwgBiome>())
        {
            var positions = new List<(int X, int Z)>();
            for (int x = 0; x < tiles; x++)
                for (int z = 0; z < tiles; z++)
                    if (tileBiome[x + z * tiles].Biome == biome && Free(x, z)) positions.Add((x, z));
            if (positions.Count == 0) continue;

            var w = options.WeightsFor(biome);
            int pw = Math.Max(0, w.Plains), hw = Math.Max(0, w.Hills), mw = Math.Max(0, w.Mountains);
            float total = pw + hw + mw;
            if (total == 0)
            {
                pw = 1;
                total = 1;
            }
            int plains = (int)MathF.Floor(pw / total * positions.Count);
            int hills = (int)MathF.Floor(hw / total * positions.Count);
            int mountains = (int)MathF.Floor(mw / total * positions.Count);
            while (positions.Count > plains + hills + mountains)
            {
                int pick = rand.Next(3);
                if (pick == 0) { if (pw > 0) plains++; else pick++; }
                if (pick == 1) { if (hw > 0) hills++; else pick++; }
                if (pick == 2 && mw > 0) mountains++;
            }

            int index = rand.Next(positions.Count);
            while (positions.Count > 0)
            {
                var (x, z) = positions[index];
                positions.RemoveAt(index);
                index = rand.Next(positions.Count);
                if (plan[x + z * tiles] != TerrainType.None) continue;

                if (hills > 0)
                {
                    // RWG sets the whole aligned block, even tiles of another biome; painted and unselected tiles are kept.
                    if (hills >= 2)
                    {
                        int bx = x & ~1, bz = z & ~1;
                        for (int dz = 0; dz < 2; dz++)
                            for (int dx = 0; dx < 2; dx++)
                                if (bx + dx < tiles && bz + dz < tiles && Free(bx + dx, bz + dz))
                                    plan[bx + dx + (bz + dz) * tiles] = TerrainType.Hills;
                    }
                    else plan[x + z * tiles] = TerrainType.Plains; // RWG leaves a lone last hill tile bare; plains is closest.
                    hills -= 4;
                }
                else if (mountains > 0)
                {
                    mountains--;
                    plan[x + z * tiles] = TerrainType.Mountains;
                    if (mountains > 0 && rand.NextDouble() < 0.8)
                    {
                        int dir = rand.Next(4);
                        for (int i = 0; i < 4; i++)
                        {
                            dir = (dir + 1) & 3;
                            var next = (x + Directions[dir].X, z + Directions[dir].Z);
                            int found = positions.IndexOf(next);
                            if (found >= 0 && plan[next.Item1 + next.Item2 * tiles] == TerrainType.None)
                            {
                                index = found;
                                break;
                            }
                        }
                    }
                }
                else plan[x + z * tiles] = TerrainType.Plains;
            }
        }
        return plan;
    }

    /// <summary>
    /// One cluster of stamps per block of <paramref name="block"/>² tiles holding a tile of <paramref name="type"/>
    /// (GenerateTerrainFromTiles). The first stamp sits in the block's middle; the rest sit the cluster radius away in
    /// a random direction, at 45% strength and additive.
    /// </summary>
    private static void AddClusters(List<(StampImage, StampPlacement, TerrainRule, byte)> draws, TerrainType type, int block,
        int tiles, TerrainType[] plan, GridRect selected, (RwgBiome Biome, byte Index)[] tileBiome, StampLibrary stamps, RwgMixer mixer, Random rand)
    {
        string typeName = RwgName(type);
        for (int x = 0; x < tiles; x += block)
            for (int z = 0; z < tiles; z += block)
            {
                bool any = false;
                for (int dz = 0; dz < block && !any; dz++)
                    for (int dx = 0; dx < block && !any; dx++)
                        any = x + dx < tiles && z + dz < tiles && selected.Contains(x + dx, z + dz) && plan[x + dx + (z + dz) * tiles] == type;
                if (!any) continue;

                var (biome, biomeIndex) = tileBiome[x + z * tiles];
                string biomeName = RwgName(biome);
                var rule = mixer.TerrainRule(biomeName, typeName);
                var names = stamps.WithPrefix($"{biomeName}_{typeName}");
                if (names.Count == 0) names = stamps.WithPrefix(typeName);
                if (names.Count == 0) continue;

                float scaleMin = rule.Scale.Min * 0.5f * block, scaleMax = rule.Scale.Max * 0.5f * block;
                float radius = rule.ClusterRadius * TileSize * block;
                float cx = x * TileSize + block * TileSize / 2f, cz = z * TileSize + block * TileSize / 2f;
                float offset = 0, strength = rule.ClusterStrength;
                bool additive = false;
                for (int i = 0; i < rule.ClusterCount; i++)
                {
                    var stamp = stamps.Get(names[rand.Next(names.Count)]);
                    double a = rand.NextDouble() * Math.PI * 2;
                    float px = cx + MathF.Round((float)Math.Cos(a) * offset), pz = cz + MathF.Round((float)Math.Sin(a) * offset);
                    float scale = scaleMin + (float)rand.NextDouble() * (scaleMax - scaleMin);
                    draws.Add((stamp, new StampPlacement(px, pz, stamp.Size * scale, rand.Next(360), strength, additive), rule, biomeIndex));
                    offset = radius;
                    strength = rule.ClusterStrength * 0.45f;
                    additive = true;
                }
            }
    }

    /// <summary>Paints the stamp's tile biome where the stamp's alpha is above the cut-off (StampManager.DrawBiomeStamp).</summary>
    private static void PaintBiomeMask(Grid<byte> grid, int cell, StampImage stamp, StampPlacement p, float cutoff, byte index,
        GridRect? area, BiomeMaskRecord mask)
    {
        var reach = StampOp.Reach(p, grid.Width * cell, grid.Height * cell);
        var cells = new GridRect(reach.X0 / cell, reach.Z0 / cell, (reach.X1 + cell - 1) / cell, (reach.Z1 + cell - 1) / cell)
            .Clamp(grid.Width, grid.Height);
        if (area is { } a) cells = new GridRect(Math.Max(cells.X0, a.X0), Math.Max(cells.Z0, a.Z0), Math.Min(cells.X1, a.X1), Math.Min(cells.Z1, a.Z1));
        if (cells.IsEmpty) return;

        float scale = p.SizeMetres / stamp.Size;
        float angle = -p.RotationDegrees * MathF.PI / 180f;
        float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
        float centre = stamp.Size / 2f;
        for (int z = cells.Z0; z < cells.Z1; z++)
        {
            float dz = (z + 0.5f) * cell - p.Z;
            for (int x = cells.X0; x < cells.X1; x++)
            {
                float dx = (x + 0.5f) * cell - p.X;
                float sx = (dx * cos + dz * sin) / scale + centre;
                float sy = (-dx * sin + dz * cos) / scale + centre;
                if (stamp.SampleAlpha(sx, sy) <= cutoff) continue;
                int i = x + z * grid.Width;
                mask.Note(i, grid.Data[i], index);
                grid.Data[i] = index;
            }
        }
    }

    /// <summary>
    /// Writes an area's new terrain into the world: fully inside the area, fading to the old ground over
    /// <see cref="Feather"/> metres around it.
    /// </summary>
    private static void Blend(Grid<ushort> height, Grid<ushort> scratch, GridRect region, GridRect area, ushort floor)
    {
        Parallel.For(region.Z0, region.Z1, z =>
        {
            var row = height.Row(z);
            var src = scratch.Row(z - region.Z0);
            int dz = z < area.Z0 ? area.Z0 - z : z >= area.Z1 ? z - area.Z1 + 1 : 0;
            for (int x = region.X0; x < region.X1; x++)
            {
                int dx = x < area.X0 ? area.X0 - x : x >= area.X1 ? x - area.X1 + 1 : 0;
                float d = MathF.Sqrt(dx * dx + dz * dz) / Feather;
                if (d >= 1) continue;
                float t = 1 - d;
                t = t * t * (3 - 2 * t);
                float h = Math.Max(src[x - region.X0], floor);
                row[x] = (ushort)MathF.Round(row[x] + (h - row[x]) * t);
            }
        });
    }
}
