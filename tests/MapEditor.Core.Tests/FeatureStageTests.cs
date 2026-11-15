using System.Xml.Linq;
using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Generate;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Tests;

public sealed class FeatureStageTests : IDisposable
{
    private const int Size = 4096;
    private readonly string _dir = Directory.CreateTempSubdirectory("mapeditor-features-").FullName;
    private readonly StampLibrary _stamps;

    private static readonly RwgMixer Mixer = RwgMixer.Parse(XDocument.Parse("""
        <rwgmixer><world name="any"><property name="world_size" value="0,99999"/>
          <property class="lakes"><property name="scale" value="1, 1"/><property name="count" value="1, 3, 5"/></property>
          <property class="rivers"><property name="scale" value="1, 1"/><property name="count" value="1, 2, 4"/></property>
          <property class="canyons"><property name="scale" value="1, 1"/><property name="count" value="1, 2, 3"/></property>
          <property class="craters"><property name="scale" value=".5, .75"/><property name="count" value="2, 4, 8"/></property>
        </world></rwgmixer>
        """), Size);

    public FeatureStageTests()
    {
        // Like the game's: lakes and canyons have no height of their own and dig by their alpha; craters have a bowl.
        WriteStamp("lake_01", 128, d => d < 1 ? (0, 0.5f * (1 - d), 20) : (0, 0, 0));
        WriteStamp("canyon_01", 128, d => d < 1 ? (0, 0.8f * (1 - d), 0) : (0, 0, 0));
        WriteStamp("crater_01", 128, d => d < 1 ? (20, 0.9f * (1 - d), 0) : (0, 0, 0));
        // A river running straight across, west edge to east edge, with water down its middle.
        WriteStamp("river_01", 128, (x, y) => Math.Abs(y - 64) < 12 ? (15, 1, Math.Abs(y - 64) < 6 ? 5 : 0) : (0, 0, 0));
        _stamps = StampLibrary.Load(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void WriteStamp(string name, int size, Func<float, (float H, float A, float W)> byDistance) =>
        WriteStamp(name, size, (x, y) => byDistance(MathF.Sqrt(MathF.Pow(x - size / 2f, 2) + MathF.Pow(y - size / 2f, 2)) / (size / 2f)));

    private void WriteStamp(string name, int size, Func<int, int, (float H, float A, float W)> pixel)
    {
        var bytes = new byte[size * size * 6];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var (h, a, w) = pixel(x, y);
                int o = (x + y * size) * 6;
                BitConverter.TryWriteBytes(bytes.AsSpan(o), (ushort)(h / 255 * 65535));
                BitConverter.TryWriteBytes(bytes.AsSpan(o + 2), (ushort)(a * 65535));
                BitConverter.TryWriteBytes(bytes.AsSpan(o + 4), (ushort)(w / 255 * 65535));
            }
        File.WriteAllBytes(Path.Combine(_dir, name + ".raw"), bytes);
    }

    private static World Flat(float metres = 40, Func<int, int, float>? ground = null)
    {
        var world = new World { Name = "T", Size = Size, Height = new Grid<ushort>(Size, Size), Water = new Grid<byte>(Size, Size) };
        if (ground is null) Array.Fill(world.Height.Data, TerrainBrush.ToRaw(metres));
        else
            for (int z = 0; z < Size; z++)
                for (int x = 0; x < Size; x++)
                    world.Height[x, z] = TerrainBrush.ToRaw(ground(x, z));
        return world;
    }

    private static FeatureOptions Options(int lakes = 3, int rivers = 0, int canyons = 0, int craters = 0, int seed = 3) => new()
    {
        Seed = seed,
        Rows =
        [
            new FeatureRow { Kind = FeatureKind.Lake, Count = lakes, ScaleMin = 1, ScaleMax = 1 },
            new FeatureRow { Kind = FeatureKind.River, Count = rivers, ScaleMin = 1, ScaleMax = 1 },
            new FeatureRow { Kind = FeatureKind.Canyon, Count = canyons, ScaleMin = 1, ScaleMax = 1 },
            new FeatureRow { Kind = FeatureKind.Crater, Count = craters, ScaleMin = 1, ScaleMax = 1 },
        ],
    };

    private FeatureResult Run(World world, FeatureOptions options, List<Feature> features, IEnumerable<TownSite>? sites = null,
        TerrainType[]? plan = null, EditSession? session = null) =>
        FeatureStage.Run(world, options, features, _stamps, Mixer, PrefabLibrary.Empty, sites ?? [], plan, 35,
            session ?? new EditSession("Features", world));

    private static int Wet(World world) => world.Water!.Data.Count(w => w > 0);

    [Fact]
    public void Rwgmixer_gives_each_feature_its_default_count_and_scale()
    {
        var rows = FeatureStage.DefaultRows(Mixer);
        Assert.Equal([(FeatureKind.Lake, 3, 1f, 1f), (FeatureKind.River, 2, 1f, 1f), (FeatureKind.Canyon, 2, 1f, 1f), (FeatureKind.Crater, 4, 0.5f, 0.75f)],
            rows.Select(r => (r.Kind, r.Count, r.ScaleMin, r.ScaleMax)));
        // A kind left out of the options' rows comes from rwgmixer.
        var some = new FeatureOptions { Rows = [new FeatureRow { Kind = FeatureKind.Lake, Count = 9 }] };
        Assert.Equal([9, 2, 2, 4], FeatureStage.RowsFor(some, Mixer).Select(r => r.Count));
    }

    [Fact]
    public void Features_go_on_free_cells_away_from_towns_mountains_and_the_edge_and_the_same_seed_repeats()
    {
        int cells = Size / FeatureStage.Cell;
        // A town over the middle, and the terrain plan's mountains in the south-west 256 m tiles.
        var town = new TownSite { Township = "town", Cells = [.. Enumerable.Range(10, 6).SelectMany(x => Enumerable.Range(10, 6).Select(z => new[] { x, z }))] };
        int tiles = Size / TerrainStage.TileSize;
        var plan = new TerrainType[tiles * tiles];
        for (int tz = 0; tz < tiles / 2; tz++)
            for (int tx = 0; tx < tiles / 2; tx++)
                plan[tx + tz * tiles] = TerrainType.Mountains;

        var world = Flat();
        var features = new List<Feature>();
        var result = Run(world, Options(lakes: 4, canyons: 3, craters: 2), features, [town], plan);

        Assert.Empty(result.Shortfalls);
        Assert.Equal(9, features.Count);
        var townCells = town.AllCells.ToHashSet();
        var taken = new HashSet<(int, int)>();
        foreach (var f in features)
        {
            int cx = (int)(f.X / FeatureStage.Cell), cz = (int)(f.Z / FeatureStage.Cell);
            Assert.InRange(cx, 1, cells - 2);
            Assert.InRange(cz, 1, cells - 2);
            Assert.NotEqual(TerrainType.Mountains, plan[(int)f.X / TerrainStage.TileSize + (int)f.Z / TerrainStage.TileSize * tiles]);
            foreach (var c in f.Cells)
            {
                Assert.DoesNotContain((c[0], c[1]), townCells);
                Assert.True(taken.Add((c[0], c[1])), "Two features share a cell.");
            }
            Assert.True(f.Drawn);
        }
        // Lakes fill at RWG's 30 m; canyons and craters on dry ground stay dry.
        Assert.All(features.Where(f => f.Kind == FeatureKind.Lake), f => Assert.Equal(30, f.WaterLevel));
        Assert.All(features.Where(f => f.Kind != FeatureKind.Lake), f => Assert.Equal(0, f.WaterLevel));
        Assert.True(Wet(world) > 0);

        var again = new List<Feature>();
        Run(Flat(), Options(lakes: 4, canyons: 3, craters: 2), again, [town], plan);
        Assert.Equal(features.Select(f => (f.Kind, f.X, f.Z, f.Rotation)), again.Select(f => (f.Kind, f.X, f.Z, f.Rotation)));
    }

    [Fact]
    public void What_doesnt_fit_is_reported()
    {
        var features = new List<Feature>();
        var result = Run(Flat(), Options(lakes: 400), features);
        Assert.InRange(features.Count, 1, 399);
        Assert.Equal([$"Placed {features.Count} of 400 lakes."], result.Shortfalls);
    }

    [Fact]
    public void A_rerun_takes_the_last_features_out_exactly_and_keeps_pinned_ones()
    {
        var world = Flat(ground: (x, z) => 38 + (x * 7 + z * 3) % 11);
        var ground = (ushort[])world.Height.Data.Clone();
        var features = new List<Feature>();
        var mixer = Mixer;
        var pinned = FeatureStage.PlaceByHand(world, FeatureKind.Crater, 2000, 2000, Options(), features, _stamps, mixer,
            PrefabLibrary.Empty, [], null, 35, 11, new EditSession("Place", world));
        var withPin = (ushort[])world.Height.Data.Clone();
        Run(world, Options(lakes: 3, canyons: 2), features);
        Assert.Equal(6, features.Count);
        Assert.NotEqual(withPin, world.Height.Data);

        // Nothing asked for: the run takes its own features out and leaves the pinned crater.
        var result = Run(world, Options(lakes: 0), features);
        Assert.Equal(5, result.Removed);
        Assert.Equal(pinned, features);
        Assert.Equal(withPin, world.Height.Data);
        Assert.Equal(0, Wet(world));

        FeatureStage.Remove(world, features, features.ToList(), _stamps, new EditSession("Drop", world));
        Assert.Empty(features);
        Assert.Equal(ground, world.Height.Data);
    }

    [Fact]
    public void Undo_puts_back_the_ground_the_water_and_the_list()
    {
        var world = Flat();
        var features = new List<Feature>();
        var session = new EditSession("Features", world);
        Run(world, Options(lakes: 2), features, session: session);
        var step = session.ToStep();
        step.Swap();
        Assert.Empty(features);
        Assert.All(world.Height.Data, h => Assert.Equal(TerrainBrush.ToRaw(40), h));
        Assert.Equal(0, Wet(world));
        step.Swap();
        Assert.Equal(2, features.Count);
        Assert.True(Wet(world) > 0);
    }

    [Fact]
    public void A_crater_touching_water_fills_and_one_on_dry_ground_doesnt()
    {
        var world = Flat();
        // A pond at 30 m in the west half.
        for (int z = 0; z < Size; z++)
            for (int x = 0; x < Size / 2; x++)
            {
                world.Height[x, z] = TerrainBrush.ToRaw(25);
                world.Water![x, z] = 30;
            }
        var features = new List<Feature>();
        Run(world, Options(lakes: 0, craters: 12, seed: 8), features);
        var wet = features.Where(f => f.X - f.Size < Size / 2).ToList();
        var dry = features.Where(f => f.X - f.Size * 0.8f > Size / 2).ToList();
        Assert.NotEmpty(wet);
        Assert.NotEmpty(dry);
        Assert.All(wet, f => Assert.Equal(30, f.WaterLevel));
        Assert.All(dry, f => Assert.Equal(0, f.WaterLevel));
    }

    [Fact]
    public void No_feature_starts_out_in_a_land_shapes_sea()
    {
        var world = Flat();
        int tiles = Size / TerrainStage.TileSize;
        // The west half is sea the terrain stage left bare; the east half is land.
        var plan = new TerrainType[tiles * tiles];
        for (int z = 0; z < tiles; z++)
            for (int x = tiles / 2; x < tiles; x++)
                plan[x + z * tiles] = TerrainType.Plains;
        for (int z = 0; z < Size; z++)
            for (int x = 0; x < Size / 2; x++)
            {
                world.Height[x, z] = TerrainBrush.ToRaw(18);
                world.Water![x, z] = 30;
            }
        var features = new List<Feature>();
        Run(world, Options(lakes: 6, craters: 12, seed: 8), features, plan: plan);
        Assert.NotEmpty(features);
        Assert.All(features, f => Assert.True(f.X >= Size / 2, $"{f.Kind} starts at x {f.X}, in the sea"));
    }

    [Fact]
    public void Mountain_lakes_hold_water_at_their_own_level_and_may_sit_on_mountains()
    {
        int tiles = Size / TerrainStage.TileSize;
        var allMountains = Enumerable.Repeat(TerrainType.Mountains, tiles * tiles).ToArray();

        var high = Flat(100);
        var rwg = new List<Feature>();
        var result = Run(high, Options(lakes: 2), rwg, plan: allMountains);
        Assert.Empty(rwg);
        Assert.Single(result.Shortfalls);

        var options = Options(lakes: 2);
        options.MountainLakes = true;
        var own = new List<Feature>();
        Run(high, options, own, plan: allMountains);
        Assert.Equal(2, own.Count);
        // As far under the 100 m rim as RWG's 30 m sits under its 35 m base.
        Assert.All(own, f => Assert.Equal(95, f.WaterLevel));
        Assert.True(Wet(high) > 0);
    }

    [Fact]
    public void Long_rivers_run_downhill_with_their_water_stepping_down()
    {
        // Ground falling 1 m every 40 m from west to east, from 140 m to 38 m.
        var world = Flat(ground: (x, _) => 140 - x / 40f);
        var options = Options(lakes: 0, rivers: 1, seed: 4);
        options.LongRivers = true;
        options.RiverSegments = 6;
        var features = new List<Feature>();
        Run(world, options, features);

        var river = features.Where(f => f.Kind == FeatureKind.River).ToList();
        Assert.True(river.Count >= 3, $"Only {river.Count} river stamps.");
        Assert.All(features, f => Assert.Equal(1, f.Chain));
        for (int i = 1; i < river.Count; i++)
        {
            Assert.True(river[i].X > river[i - 1].X, "The river should head east, downhill.");
            Assert.True(river[i].WaterLevel <= river[i - 1].WaterLevel);
        }
        Assert.True(river[^1].WaterLevel < river[0].WaterLevel);
        Assert.All(river, f => Assert.NotEmpty(f.Water));

        // Dropping one stamp takes the whole river.
        FeatureStage.Remove(world, features, [river[1]], _stamps, new EditSession("Drop", world));
        Assert.Empty(features);
        Assert.Equal(0, Wet(world));
    }

    [Fact]
    public void A_terrain_run_takes_down_features_it_reaches_and_pinned_ones_come_back_on_the_next_run()
    {
        var world = Flat();
        var features = new List<Feature>();
        FeatureStage.PlaceByHand(world, FeatureKind.Lake, 600, 600, Options(), features, _stamps, Mixer, PrefabLibrary.Empty, [], null, 35, 1,
            new EditSession("Place", world));
        Run(world, Options(lakes: 3), features);
        var pin = features.Single(f => f.Pinned);
        var far = features.Where(f => !f.Pinned && Math.Abs(f.X - 600) > 1200 && Math.Abs(f.Z - 600) > 1200).ToList();

        int down = FeatureStage.TakeDown(world, features, new GridRect(0, 0, 1000, 1000), _stamps, new EditSession("Terrain", world));
        Assert.True(down >= 1);
        Assert.Contains(pin, features);
        Assert.False(pin.Drawn);
        Assert.All(far, f => Assert.Contains(f, features));

        Run(world, Options(lakes: 1), features);
        Assert.True(pin.Drawn);
        Assert.NotEmpty(pin.Water);
    }
}
