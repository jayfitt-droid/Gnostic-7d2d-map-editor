using System.Numerics;
using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Tests;

public sealed class RoadBankTests
{
    private const int Size = 128;
    private const float Centre = 64;
    private const float Level = 60;
    private static readonly List<Vector2> Line = [new(Centre, 20), new(Centre, 108)];

    [Theory]
    [InlineData(RoadType.Gravel, 4.5f)]
    [InlineData(RoadType.Asphalt, 9f)]
    public void Banks_ease_into_a_slope_on_both_sides(RoadType type, float radius)
    {
        var road = new RoadPath(Line, type, radius);
        var world = Ground(x =>
        {
            float offset = x + 0.5f - Centre;
            float rise = Math.Clamp((MathF.Abs(offset) - radius + 10) * 0.55f, 0, 14);
            return Level + MathF.CopySign(rise, offset);
        });
        var original = world.Height.Row(64).ToArray();
        var session = new EditSession("Road", world);
        RoadOp.Draw(world, road, Line, [Level, Level], session);

        var heights = world.Height.Row(64);
        for (int x = 1; x < heights.Length; x++)
        {
            float rise = World.HeightToMetres((ushort)Math.Abs(heights[x] - heights[x - 1]));
            Assert.True(rise <= 0.51f, $"{type} at X={x}: {rise:F3} m in 1 m");
        }
        Assert.NotEqual(original[64 + (int)(radius + road.BlendDistance)], heights[64 + (int)(radius + road.BlendDistance)]);
        Assert.Equal(original[110], heights[110]);
        Assert.Equal(original[17], heights[17]);
        session.ToStep().Swap();
        Assert.Equal(original, world.Height.Row(64).ToArray());
        Assert.Null(world.Roads);
    }

    [Theory]
    [InlineData(RoadType.Gravel, 0.22f)]
    [InlineData(RoadType.Asphalt, 0.20f)]
    public void Flat_and_gentle_ground_stay_close_to_the_old_bank_with_a_softer_edge(RoadType type, float edgeRise)
    {
        var road = new RoadPath(Line, type, 4);
        var flat = Ground(_ => Level);
        var gentle = Ground(_ => Level);
        for (int z = 0; z < Size; z++)
            gentle.Height.Row(z).Fill(TerrainBrush.ToRaw(Level + Math.Clamp((z - 40) / 24f, 0, 1) * road.BlendDistance / 4));
        RoadOp.Draw(flat, road, Line, [Level, Level], new EditSession("Flat road", flat));
        RoadOp.Draw(gentle, road, Line, [Level, Level], new EditSession("Gentle road", gentle));

        for (int z = 40; z <= 64; z++)
            for (int x = 0; x < Size; x++)
            {
                Assert.Equal(TerrainBrush.ToRaw(Level), flat.Height[x, z]);
                float rise = (z - 40) / 24f * road.BlendDistance / 4;
                float fromShoulder = Math.Clamp(MathF.Abs(x + 0.5f - Centre) - road.Radius, 0, road.BlendDistance);
                // The legacy cross section was a parabola through the road edge and the natural ground.
                double expected = Level + (rise / Math.Pow(road.BlendDistance, 2)) * Math.Pow(fromShoulder, 2);
                Assert.InRange(Math.Abs(World.HeightToMetres(gentle.Height[x, z]) - expected), 0, 0.25);
            }

        int edge = (int)(Centre + road.Radius + road.BlendDistance);
        Assert.InRange(World.HeightToMetres(gentle.Height[edge, 64]) - World.HeightToMetres(gentle.Height[edge - 1, 64]),
            0, edgeRise);
    }

    [Fact]
    public void The_bank_leaves_prefabs_street_tiles_and_other_roads_at_their_original_height()
    {
        const int size = 512, centre = 160;
        var world = Ground(x => MathF.Abs(x + 0.5f - centre) <= 5 ? Level : Level + 14, size);
        const string poi = "house_test", street = "rwg_tile_test";
        var library = PrefabLibrary.Of([
            new PrefabInfo(poi, "Test", new Int3(8, 10, 12), 0, 0, 0, [], null),
            new PrefabInfo(street, "RWGTiles", new Int3(150, 22, 150), 0, 0, 0, ["streettile"], null),
        ]);
        world.Prefabs.Add(new() { Name = poi, Position = new(world.GridToWorld(167), 74, world.GridToWorld(40)), Rotation = 1 });
        world.Prefabs.Add(new() { Name = street, Position = new(world.GridToWorld(0), 74, world.GridToWorld(0)) });
        world.Roads = new Grid<byte>(size, size);
        world.Roads[167, 60] = (byte)RoadType.Gravel;
        world.Roads[168, 60] = (byte)RoadType.Asphalt;
        var plan = new TownPlan { Township = new("test", [], false, false, null, 0), Ground = 74 };
        // Put the town well clear of the road so flattening it does not affect the bank.
        plan.Tiles.Add(new TownTile(2, 2));
        plan.Roads.Add(new RoadPath([new(centre, 20), new(centre, 108)], RoadType.Asphalt, 5));
        TownGenerator.Apply(world, library, plan, new EditSession("Town", world));

        ushort original = TerrainBrush.ToRaw(Level + 14);
        Assert.NotEqual(original, world.Height[167, 39]);
        Assert.NotEqual(original, world.Height[150, 45]);
        foreach (var prefab in world.Prefabs)
        {
            var rect = PrefabPlacer.Footprint(world, prefab, library);
            for (int z = rect.Z0; z < rect.Z1; z++)
                for (int x = rect.X0; x < rect.X1; x++)
                    Assert.Equal(original, world.Height[x, z]);
        }
        Assert.Equal((original, (byte)RoadType.Gravel), (world.Height[167, 60], world.Roads[167, 60]));
        Assert.Equal((original, (byte)RoadType.Asphalt), (world.Height[168, 60], world.Roads[168, 60]));
        Assert.NotEqual(original, world.Height[180, 60]);
    }

    private static World Ground(Func<int, float> height, int size = Size)
    {
        var world = new World { Name = "Banks", Size = size, Height = new Grid<ushort>(size, size) };
        for (int x = 0; x < size; x++)
        {
            ushort raw = TerrainBrush.ToRaw(height(x));
            for (int z = 0; z < size; z++)
                world.Height[x, z] = raw;
        }
        return world;
    }
}
