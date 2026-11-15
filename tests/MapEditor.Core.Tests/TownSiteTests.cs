using System.Xml.Linq;
using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Generate;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Tests;

public sealed class TownSiteTests
{
    private static readonly RwgMixer Mixer = RwgMixer.Parse(XDocument.Parse("""
        <rwgmixer>
          <world>
            <property name="world_size" value="0,99999"/>
            <property class="town"><property name="tiles" value="4,6"/><property name="count" value="1, 3, 5"/><property name="distance" value="4"/></property>
            <property class="roadside"><property name="tiles" value="1,1"/><property name="count" value="2, 4, 6"/></property>
          </world>
          <township name="wilderness"/>
          <township name="town"><property name="biomes" value="desert"/></township>
          <township name="roadside"><property name="spawn_trader" value="false"/></township>
          <district name="downtown"><property name="spawn_weight" value="1"/><property name="required_township" value="town"/></district>
          <district name="gateway"><property name="spawn_weight" value="0"/></district>
          <streettile name="rwg_tile_downtown_intersection"><property name="maxtiles" value="2"/></streettile>
        </rwgmixer>
        """), 4096);

    private static readonly PrefabLibrary Library = PrefabLibrary.Of(MakeLibrary());

    private static IEnumerable<PrefabInfo> MakeLibrary()
    {
        foreach (var district in new[] { "downtown", "gateway" })
            foreach (var shape in TownGenerator.ShapeNames)
                yield return new PrefabInfo($"rwg_tile_{district}_{shape}", "RWGTiles", new Int3(150, 22, 150), -12, 0, 0, ["streettile"], null)
                {
                    Markers = [new PrefabMarker(new Int3(10, 12, 10), new Int3(25, 0, 25), MarkerType.POISpawn, "1")],
                };
        yield return new PrefabInfo("shop_01", "POIs", new Int3(24, 20, 25), -1, 0, 2, ["downtown"], null);
    }

    /// <summary>A flat desert world, 150 m cells across.</summary>
    private static World Desert(int size = 4500)
    {
        var world = new World { Name = "T", Size = size, Height = new Grid<ushort>(size, size) };
        Array.Fill(world.Height.Data, World.MetresToHeight(40));
        world.Biomes = new IndexedLayer(new Grid<byte>(size / 8, size / 8), [BiomeLayout.DesertColor]);
        return world;
    }

    private static TownCells Cells(World world, TerrainType[]? plan = null) => TownCells.Build(world, BiomeTable.Vanilla, Library, plan);

    private static TownSiteOptions Options(int towns = 3, int roadside = 4, int seed = 5) => new()
    {
        Seed = seed,
        Rows =
        [
            new TownRow { Township = "town", Count = towns, MinTiles = 4, MaxTiles = 6, Distance = 4 },
            new TownRow { Township = "roadside", Count = roadside, MinTiles = 1, MaxTiles = 1 },
        ],
    };

    [Fact]
    public void Rwgmixer_gives_each_township_its_counts_and_spacing()
    {
        var town = Mixer.Townships.First(t => t.Name == "town");
        Assert.Equal((1, 3, 5), town.Counts);
        Assert.Equal(4, town.Distance);
        Assert.Equal(5, Mixer.Townships.First(t => t.Name == "roadside").Distance);
        var rows = TownSiteStage.DefaultRows(Mixer);
        Assert.Equal([("town", 3, 4, 6), ("roadside", 4, 1, 1)], rows.Select(r => (r.Township, r.Count, r.MinTiles, r.MaxTiles)));
    }

    [Fact]
    public void Towns_keep_off_the_edges_and_apart_and_the_same_seed_gives_the_same_sites()
    {
        var world = Desert();
        var cells = Cells(world);
        var result = TownSiteStage.Plan(cells, Mixer, Options(), [], TestContext.Current.CancellationToken);

        Assert.Empty(result.Shortfalls);
        var towns = result.Sites.Where(s => s.Township == "town").ToList();
        Assert.Equal(3, towns.Count);
        Assert.Equal(4, result.Sites.Count(s => s.Township == "roadside"));
        Assert.All(towns, t => Assert.InRange(t.Cells.Count, 4, 6));
        int margin = cells.Width / 10;
        Assert.All(result.Sites.SelectMany(s => s.Core), c =>
        {
            Assert.InRange(c.Cx, 2, cells.Width - 3);
            Assert.InRange(c.Cz, 2, cells.Width - 3);
        });
        Assert.All(towns, t => Assert.True(t.Core.First().Cx > margin && t.Core.First().Cz > margin, "a town starts inside the edge margin"));
        // No two towns share a cell.
        var all = result.Sites.SelectMany(s => s.AllCells).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());

        var again = TownSiteStage.Plan(Cells(Desert()), Mixer, Options(), [], TestContext.Current.CancellationToken);
        Assert.Equal(result.Sites.Select(s => string.Join(";", s.Core)), again.Sites.Select(s => string.Join(";", s.Core)));
        var other = TownSiteStage.Plan(Cells(Desert()), Mixer, Options(seed: 6), [], TestContext.Current.CancellationToken);
        Assert.NotEqual(result.Sites.Select(s => string.Join(";", s.Core)), other.Sites.Select(s => string.Join(";", s.Core)));
    }

    [Fact]
    public void Water_mountains_mixed_biomes_and_existing_prefabs_make_cells_unusable()
    {
        var world = Desert(3000);
        world.Water = new Grid<byte>(3000, 3000);
        for (int z = 0; z < 150; z++) for (int x = 0; x < 150; x++) world.Water[x + 300, z + 300] = 50; // cell (2, 2)
        // A snow strip across cell (5, 2) mixes biomes there.
        world.Biomes!.Palette.Add(BiomeLayout.SnowColor);
        for (int z = 300 / 8; z < 450 / 8; z++) world.Biomes.Indices[800 / 8, z] = 1;
        world.Prefabs.Add(new PrefabInstance { Name = "shop_01", Position = new Int3(world.GridToWorld(1210), 40, world.GridToWorld(310)), Rotation = 0 });
        var plan = new TerrainType[(3000 / 256) * (3000 / 256)];
        plan[(1100 / 256) + (375 / 256) * (3000 / 256)] = TerrainType.Mountains; // cell (7, 2)

        var cells = Cells(world, plan);
        Assert.False(cells.IsValid(2, 2));
        Assert.False(cells.IsValid(5, 2));
        Assert.False(cells.IsValid(8, 2));
        Assert.False(cells.IsValid(7, 2));
        Assert.True(cells.IsValid(10, 10));
        Assert.Equal("desert", cells.Biome(10, 10));
    }

    [Fact]
    public void Pinned_towns_stay_and_new_ones_keep_clear_of_them()
    {
        var world = Desert();
        var cells = Cells(world);
        var pinned = TownSiteStage.Regrow(cells, Mixer, [], "town", (15, 15), 6, 3)!;
        pinned.Pinned = true;
        Assert.Equal(6, pinned.Cells.Count);

        var result = TownSiteStage.Plan(cells, Mixer, Options(), [pinned], TestContext.Current.CancellationToken);
        Assert.Equal(pinned.Cells.Select(c => (c[0], c[1])), result.Sites[0].Core);
        Assert.True(result.Sites[0].Pinned);
        var pinnedCells = pinned.Core.ToHashSet();
        foreach (var s in result.Sites.Skip(1))
            foreach (var (x, z) in s.Core)
                Assert.False(pinnedCells.Any(p => Math.Abs(p.Cx - x) <= 1 && Math.Abs(p.Cz - z) <= 1), "towns stay outside each other's buffer");
    }

    [Fact]
    public void Planning_again_over_kept_towns_adds_only_what_is_missing()
    {
        var cells = Cells(Desert());
        var first = TownSiteStage.Plan(cells, Mixer, Options(), [], TestContext.Current.CancellationToken).Sites;
        foreach (var s in first) s.Pinned = true;
        var again = TownSiteStage.Plan(cells, Mixer, Options(), first, TestContext.Current.CancellationToken);
        Assert.Equal(first.Count, again.Sites.Count);

        var more = TownSiteStage.Plan(cells, Mixer, Options(towns: 4), first, TestContext.Current.CancellationToken);
        Assert.Equal(first.Count + 1, more.Sites.Count);
    }

    [Fact]
    public void Sites_planned_before_the_ground_changed_are_not_planned_over_water()
    {
        // #12
        var world = Desert();
        var sites = TownSiteStage.Plan(Cells(world), Mixer, Options(), [], TestContext.Current.CancellationToken).Sites;
        FloodCore(world, sites[0]);
        var cells = Cells(world);
        Assert.Contains(sites[0].Core, c => !cells.IsValid(c.Cx, c.Cz));

        var towns = TownSiteStage.PlanTowns(world, Library, Mixer, BiomeTable.Vanilla, cells, sites, TestContext.Current.CancellationToken);
        Assert.All(towns.SelectMany(t => t.Plan.Tiles), t => Assert.True(cells.IsValid(t.Cx, t.Cz), $"street tile on unusable cell ({t.Cx}, {t.Cz})"));
    }

    private static void FloodCore(World world, TownSite site)
    {
        world.Water ??= new Grid<byte>(world.Size, world.Size);
        foreach (var (cx, cz) in site.Core)
            for (int z = 0; z < 150; z++)
                for (int x = 0; x < 150; x++)
                    world.Water[cx * 150 + x, cz * 150 + z] = 50;
    }

    [Fact]
    public void A_whole_map_run_clears_unpinned_sites_and_keeps_pinned_ones_on_dry_land()
    {
        var world = Desert();
        var sites = TownSiteStage.Plan(Cells(world), Mixer, Options(), [], TestContext.Current.CancellationToken).Sites;
        sites[0].Pinned = true;
        var pinned = sites[0];

        var (cleared, unusable) = TownSiteStage.DropForNewGround(sites, Cells(world), wholeMap: true);
        Assert.Equal(6, cleared);
        Assert.Equal(0, unusable);
        Assert.Equal([pinned], sites);
    }

    [Fact]
    public void A_whole_map_run_drops_a_pinned_site_that_is_now_over_water()
    {
        var world = Desert();
        var sites = TownSiteStage.Plan(Cells(world), Mixer, Options(), [], TestContext.Current.CancellationToken).Sites;
        sites[0].Pinned = true;
        sites[1].Pinned = true;
        var dry = sites[1];
        FloodCore(world, sites[0]);

        var (cleared, unusable) = TownSiteStage.DropForNewGround(sites, Cells(world), wholeMap: true);
        Assert.Equal(5, cleared);
        Assert.Equal(1, unusable);
        Assert.Equal([dry], sites);
    }

    [Fact]
    public void An_area_run_drops_only_unbuilt_sites_over_water()
    {
        var world = Desert();
        var sites = TownSiteStage.Plan(Cells(world), Mixer, Options(), [], TestContext.Current.CancellationToken).Sites;
        var wet = sites[0];
        var wetBuilt = sites[1];
        wetBuilt.Built = true;
        FloodCore(world, wet);
        FloodCore(world, wetBuilt);
        int before = sites.Count;

        var (cleared, unusable) = TownSiteStage.DropForNewGround(sites, Cells(world), wholeMap: false);
        Assert.Equal(0, cleared);
        Assert.Equal(1, unusable);
        Assert.Equal(before - 1, sites.Count);
        Assert.DoesNotContain(wet, sites);
        Assert.Contains(wetBuilt, sites);
    }

    [Fact]
    public void An_area_run_keeps_sites_on_dry_land()
    {
        var world = Desert();
        var sites = TownSiteStage.Plan(Cells(world), Mixer, Options(), [], TestContext.Current.CancellationToken).Sites;
        int before = sites.Count;
        Assert.Equal((0, 0), TownSiteStage.DropForNewGround(sites, Cells(world), wholeMap: false));
        Assert.Equal(before, sites.Count);
    }

    [Fact]
    public void A_drawn_box_is_the_whole_town()
    {
        var world = Desert();
        var cells = Cells(world);
        var box = new GridRect(12, 12, 15, 14);
        var site = TownSiteStage.Regrow(cells, Mixer, [], "town", (13, 12), 6, 3, box)!;
        Assert.All(site.Core, c => Assert.True(c.Cx >= 12 && c.Cx < 15 && c.Cz >= 12 && c.Cz < 14));
        Assert.Equal(6, site.Cells.Count);
        var planned = TownSiteStage.PlanTowns(world, Library, Mixer, BiomeTable.Vanilla, cells, [site], TestContext.Current.CancellationToken);
        Assert.All(planned[0].Plan.Tiles.Where(t => !t.IsGateway), t => Assert.Contains((t.Cx, t.Cz), site.Core));
        Assert.Contains(planned[0].Plan.Tiles, t => t.IsGateway);
    }

    [Fact]
    public void Built_towns_go_up_as_one_edit_and_come_down_again()
    {
        var world = Desert();
        var cells = Cells(world);
        var sites = TownSiteStage.Plan(cells, Mixer, Options(), [], TestContext.Current.CancellationToken).Sites;
        var towns = TownSiteStage.PlanTowns(world, Library, Mixer, BiomeTable.Vanilla, cells, sites, TestContext.Current.CancellationToken);
        Assert.Equal(sites.Count, towns.Count);
        Assert.All(towns, t => Assert.NotEmpty(t.Plan.Tiles));
        // Planned together, the towns never share a cell.
        var tiles = towns.SelectMany(t => t.Plan.Tiles.Select(x => (x.Cx, x.Cz))).ToList();
        Assert.Equal(tiles.Count, tiles.Distinct().Count());

        var runner = new EditRunner(new UndoStack());
        Assert.True(runner.Run(world, "Build towns", s => TownSiteStage.Build(world, Library, towns, s)));
        Assert.All(sites, s => Assert.True(s.Built));
        int built = world.Prefabs.Count;
        Assert.True(built >= tiles.Count);

        var session = new EditSession("Generate terrain", world);
        Assert.Equal(sites.Count, TownSiteStage.TakeDown(world, Library, sites, null, session));
        Assert.Empty(world.Prefabs);
        Assert.All(sites, s => Assert.False(s.Built));
        session.Rollback();
        Assert.Equal(built, world.Prefabs.Count);
    }

    [Fact]
    public void A_city_bigger_than_rwg_makes_gets_more_of_each_capped_street_tile()
    {
        int Intersections(int tiles)
        {
            var world = Desert();
            var cells = Cells(world);
            var site = TownSiteStage.Regrow(cells, Mixer, [], "town", (15, 15), tiles, 4)!;
            var town = Assert.Single(TownSiteStage.PlanTowns(world, Library, Mixer, BiomeTable.Vanilla, cells, [site]));
            return town.Plan.Prefabs.Count(p => p.Name == "rwg_tile_downtown_intersection");
        }
        Assert.True(Intersections(6) <= 2, "within RWG's sizes the cap holds");
        Assert.True(Intersections(36) > 2, "a city six times RWG's biggest may have six times the cap");
    }

    [Fact]
    public void Towns_straddle_a_biome_border_only_when_allowed()
    {
        // Desert west of x = 2240, forest east: the border runs through cell column 14 (2100 to 2249).
        var world = Desert();
        var biomes = new Grid<byte>(world.Size / 8, world.Size / 8);
        for (int z = 0; z < biomes.Height; z++)
            for (int x = 2240 / 8; x < biomes.Width; x++)
                biomes[x, z] = 1;
        world.Biomes = new IndexedLayer(biomes, [BiomeLayout.DesertColor, BiomeLayout.ForestColor]);

        var rwg = TownCells.Build(world, BiomeTable.Vanilla, Library, null);
        Assert.False(rwg.IsValid(14, 15));
        Assert.Null(TownSiteStage.Regrow(rwg, Mixer, [], "town", (14, 15), 6, 3));

        var cross = TownCells.Build(world, BiomeTable.Vanilla, Library, null, crossBorders: true);
        Assert.True(cross.IsValid(14, 15));
        Assert.Equal("desert", cross.Biome(14, 15));
        var site = TownSiteStage.Regrow(cross, Mixer, [], "town", (14, 15), 6, 3);
        Assert.NotNull(site);
        var columns = site.Core.Select(c => c.Cx).ToList();
        Assert.True(columns.Min() <= 14 && columns.Max() >= 15, "the town reaches both sides of the border");
    }

    [Fact]
    public void Rwg_trader_caps_give_a_biome_two_to_four_of_its_own_trader()
    {
        var mixer = RwgMixer.Parse(XDocument.Parse("""
            <rwgmixer>
              <world>
                <property name="world_size" value="0,99999"/>
                <property class="town"><property name="tiles" value="4,6"/><property name="count" value="1, 3, 5"/><property name="distance" value="4"/></property>
                <property class="roadside"><property name="tiles" value="1,1"/><property name="count" value="2, 4, 6"/></property>
              </world>
              <township name="town"><property name="biomes" value="desert"/></township>
              <township name="roadside"/>
              <district name="downtown"><property name="spawn_weight" value="1"/><property name="required_township" value="town"/></district>
              <district name="gateway"><property name="spawn_weight" value="0"/></district>
              <prefab_spawn_adjust partial_name="trader_bob" biomeTags="desert" bias="20" min_count="2" max_count="4"/>
              <prefab_spawn_adjust partial_name="trader_hugh" biomeTags="snow" bias="20" min_count="2" max_count="4"/>
            </rwgmixer>
            """), 4096);
        var library = PrefabLibrary.Of([.. MakeLibrary(),
            new PrefabInfo("trader_bob", "POIs", new Int3(24, 20, 24), -1, 0, 0, ["trader"], null),
            new PrefabInfo("trader_hugh", "POIs", new Int3(24, 20, 24), -1, 0, 0, ["trader"], null)]);
        int Traders(TraderMode mode, int towns, int roadside, out int hugh)
        {
            var world = Desert(6000);
            var cells = TownCells.Build(world, BiomeTable.Vanilla, library, null);
            var sites = TownSiteStage.Plan(cells, mixer, Options(towns, roadside), []).Sites;
            var prefabs = TownSiteStage.PlanTowns(world, library, mixer, BiomeTable.Vanilla, cells, sites, default, mode).SelectMany(t => t.Plan.Prefabs).ToList();
            hugh = prefabs.Count(p => p.Name == "trader_hugh");
            return prefabs.Count(p => p.Name == "trader_bob");
        }

        Assert.Equal(4, Traders(TraderMode.RwgCaps, 5, 6, out int hugh));
        Assert.Equal(0, hugh);
        Assert.True(Traders(TraderMode.OnePerTown, 5, 6, out _) > 4, "one per town, roadside stops included");
        // A lone town gets two lots kept so the biome reaches its minimum.
        Assert.Equal(2, Traders(TraderMode.RwgCaps, 1, 0, out _));
    }
}
