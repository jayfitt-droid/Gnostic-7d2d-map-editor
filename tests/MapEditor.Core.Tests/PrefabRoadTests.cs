using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;
using MapEditor.Core.Validate;

namespace MapEditor.Core.Tests;

/// <summary>Placing prefabs leaves drawn roads as they are; a prefab that meets a road takes its height instead.</summary>
public sealed class PrefabRoadTests
{
    private static float H(World world, int x, int z) => World.HeightToMetres(world.Height[x, z]);

    /// <summary>A flat 40 m world with an east-west asphalt road along z = 300, raised to 43 m.</summary>
    private static World WorldWithRoad()
    {
        var world = new World { Name = "T", Size = 600, Height = new Grid<ushort>(600, 600) };
        Array.Fill(world.Height.Data, World.MetresToHeight(40));
        world.Roads = new Grid<byte>(600, 600);
        for (int z = 295; z < 305; z++)
            for (int x = 0; x < 600; x++)
            {
                world.Roads[x, z] = (byte)RoadType.Asphalt;
                world.Height[x, z] = World.MetresToHeight(43);
            }
        return world;
    }

    [Fact]
    public void Flattening_beside_a_road_leaves_the_road_as_it_was()
    {
        var world = WorldWithRoad();
        PrefabPlacer.Flatten(world, new GridRect(200, 308, 240, 348), 46, PrefabPlacer.StreetTileBlend, new EditSession("Place"));

        for (int x = 150; x < 300; x += 5)
            for (int z = 295; z < 305; z++)
                Assert.Equal(43, H(world, x, z), 2);
        // Away from the road the blend still runs.
        Assert.InRange(H(world, 220, 360), 44, 46);
        // Beside the road it eases in: little next to the road's edge, more a few metres out.
        Assert.InRange(H(world, 220, 294), 40, 40.5f);
        Assert.InRange(H(world, 220, 280), 41.5f, 46);
    }

    [Fact]
    public void A_poi_that_meets_a_road_takes_its_height()
    {
        var world = WorldWithRoad();
        var poi = new PrefabInfo("trader_joel", "POIs", new Int3(40, 20, 40), -2, 0, 0, ["trader"], null);
        var library = PrefabLibrary.Of([poi]);

        // Its south edge (z 306) is 2 m from the road's north edge (z 304).
        var touching = PrefabPlacer.SpotAt(world, library, poi, 220, 326, 0, out _);
        Assert.Equal(PrefabPlacer.PrefabY(poi, 43), touching.Y);
        PrefabPlacer.Place(world, poi, touching, flatten: true, new EditSession("Place"));
        Assert.Equal(43, H(world, 220, 326), 2);
        Assert.Equal(43, H(world, 220, 300), 2);

        // Well away from the road it sits on the ground.
        var away = PrefabPlacer.SpotAt(world, library, poi, 220, 450, 0, out _);
        Assert.Equal(PrefabPlacer.PrefabY(poi, 40), away.Y);
    }
    [Fact]
    public void A_road_level_with_the_prefab_is_not_flagged()
    {
        var world = WorldWithRoad();
        Assert.Equal(0, PrefabPlacer.RoadMismatch(world, new GridRect(200, 305, 240, 345), 43), 2);
        Assert.Equal(0, PrefabPlacer.RoadMismatch(world, new GridRect(200, 320, 240, 360), 40), 2); // no road within 3 m
    }

    [Fact]
    public void A_one_block_step_to_the_road_is_not_flagged()
    {
        // RWG leaves many 1 m steps between a POI and the road beside it; only bigger ones are worth fixing.
        var world = WorldWithRoad();
        var cells = new GridRect(200, 305, 240, 345);
        Assert.False(PrefabPlacer.RoadMismatch(world, cells, 42) > PrefabPlacer.RoadStep);
        Assert.False(PrefabPlacer.RoadMismatch(world, cells, 44) > PrefabPlacer.RoadStep);
        Assert.True(PrefabPlacer.RoadMismatch(world, cells, 41) > PrefabPlacer.RoadStep);
        Assert.True(PrefabPlacer.RoadMismatch(world, cells, 45) > PrefabPlacer.RoadStep);
    }

    [Fact]
    public void Validate_skips_the_road_level_warning_for_a_bridge_over_a_road()
    {
        var world = WorldWithRoad();
        var bridge = new PrefabInfo(BridgeOp.Tile, "POIs", new Int3(14, 14, 9), -1, 0, 0, ["rwgonly"], null);
        var poi = new PrefabInfo("plain_poi", "POIs", bridge.Size, -1, 0, 0, [], null);
        var library = PrefabLibrary.Of([bridge, poi]);
        var position = new Int3(world.GridToWorld(220), 65, world.GridToWorld(297));
        world.Prefabs.Add(new PrefabInstance { Name = bridge.Name, Position = position });

        Assert.DoesNotContain(WorldValidator.Validate(world, library), i => i.Fix?.Kind == FixKind.LevelRoad);

        world.Prefabs.Clear();
        var ordinary = new PrefabInstance { Name = poi.Name, Position = position };
        world.Prefabs.Add(ordinary);

        var issue = Assert.Single(WorldValidator.Validate(world, library), i => i.Fix?.Kind == FixKind.LevelRoad);
        Assert.Equal(IssueSeverity.Warning, issue.Severity);
        Assert.Equal(new IssueFix(FixKind.LevelRoad, ordinary), issue.Fix);
    }

    [Fact]
    public void A_road_sloping_past_the_prefab_is_flagged()
    {
        var world = WorldWithRoad();
        // The road climbs 5% eastwards: 40 m long frontage, 2 m rise.
        for (int z = 295; z < 305; z++)
            for (int x = 0; x < 600; x++) world.Height[x, z] = World.MetresToHeight(30 + x * 0.05f);
        var cells = new GridRect(200, 305, 240, 345);
        int ground = PrefabPlacer.RoadGround(world, cells)!.Value;
        Assert.Equal(41, ground);
        Assert.InRange(PrefabPlacer.RoadMismatch(world, cells, ground), 1.0f, 1.2f);
        Assert.True(PrefabPlacer.RoadMismatch(world, cells, ground) > PrefabPlacer.RoadStep);
    }
}
