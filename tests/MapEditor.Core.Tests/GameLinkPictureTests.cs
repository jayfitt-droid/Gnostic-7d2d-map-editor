using System.Numerics;
using MapEditor.Core.GameData;
using MapEditor.Core.GameLink;
using MapEditor.Core.Model;

namespace MapEditor.Core.Tests;

public sealed class GameLinkPictureTests
{
    private readonly World _world = new()
    {
        Name = "P",
        Size = 512,
        Height = new Grid<ushort>(512, 512, Enumerable.Repeat<ushort>(60 * 257, 512 * 512).ToArray()),
    };

    [Theory]
    [InlineData(10, 12, 20)]
    [InlineData(120, 65, 90)]
    public void PictureFramesTheRotatedPoiFromTheSouth(int width, int height, int depth)
    {
        var prefab = new PrefabInstance { Name = "farm_16", Position = new(-50, 60, -40), Rotation = 1 };
        var info = new PrefabInfo(prefab.Name, "", new(width, height, depth), 0, 3, 0, [], null);

        var frame = GameLinkPicture.ForPrefab(_world, prefab, info);

        Assert.Equal(new Int3(-50 + depth / 2, 60 + height / 2, -40 + width / 2), frame.Look);
        Assert.True(frame.Camera.Z < frame.Look.Z - width / 2);
        Assert.True(frame.Camera.Y >= 68);
        Assert.InRange(AngleBelowHorizon(frame), 12, 18);
        Assert.InRange(FrameCoverage(frame, new(depth, height, width)), 0.65f, 0.95f);
    }

    [Theory]
    [InlineData(1, 0, 1, Facing.S)]
    [InlineData(2, -1, 0, Facing.E)]
    [InlineData(3, 0, -1, Facing.N)]
    [InlineData(0, 1, 0, Facing.W)]
    public void PictureLooksAtThePlacedPoiFromItsFront(int rotation, int sideX, int sideZ, Facing facing)
    {
        var prefab = new PrefabInstance { Name = "checkpoint", Position = new(-40, 60, 20), Rotation = rotation };
        var info = new PrefabInfo(prefab.Name, "", new(42, 14, 28), 0, 1, 0, [], null);

        var frame = GameLinkPicture.ForPrefab(_world, prefab, info);
        var (width, depth) = info.Footprint(rotation);

        Assert.Equal((sideX, sideZ), (Math.Sign(frame.Camera.X - frame.Look.X), Math.Sign(frame.Camera.Z - frame.Look.Z)));
        Assert.Equal(facing, frame.StandingFacing);
        Assert.InRange(AngleBelowHorizon(frame), 12, 18);
        int setback = Math.Abs(frame.Camera.X - frame.Look.X) + Math.Abs(frame.Camera.Z - frame.Look.Z);
        Assert.InRange(setback, 22, 50);
        Assert.InRange(FrameCoverage(frame, new(width, info.Size.Y, depth)), 0.60f, 0.95f);
    }

    [Fact]
    public void PictureAtSouthWestEdgeUsesAvailableRoom()
    {
        var prefab = new PrefabInstance { Name = "farm_16", Position = new(-256, 60, -256) };
        var info = new PrefabInfo(prefab.Name, "", new(32, 16, 32), 0, 2, 0, [], null);
        var frame = GameLinkPicture.ForPrefab(_world, prefab, info);

        Assert.Equal(-256, frame.Camera.Z);
        Assert.InRange(frame.Camera.X, frame.Look.X + 1, 255);
        FrameCoverage(frame, info.Size);
    }

    [Fact]
    public void EastFacingPoiAtTheEastEdgeUsesAvailableRoom()
    {
        var prefab = new PrefabInstance { Name = "checkpoint", Position = new(224, 60, -16) };
        var info = new PrefabInfo(prefab.Name, "", new(32, 16, 32), 0, 1, 0, [], null);

        var frame = GameLinkPicture.ForPrefab(_world, prefab, info);

        Assert.Equal(255, frame.Camera.X);
        Assert.True(Math.Abs(frame.Camera.Z - frame.Look.Z) > 16);
        FrameCoverage(frame, info.Size);
    }

    [Fact]
    public void WorldEdgePictureKeepsThePlayerInsideThePlayableArea()
    {
        var world = new World { Name = "P", Size = 2048, Height = new Grid<ushort>(2048, 2048) };
        var frame = GameLinkPicture.ForSpot(world, -1024, -1024);

        Assert.Equal(new Int3(-1024, 0, -1024), frame.Look);
        Assert.InRange(frame.StandingPoint.X, -894, 893);
        Assert.InRange(frame.StandingPoint.Z, -894, 893);
        Assert.InRange(AngleBelowHorizon(frame), 18, 25);
        FrameCoverage(frame, new(32, 0, 32));
    }

    [Fact]
    public void IssuePictureFramesA32MetreSpotAndClearsTerrainUnderTheCamera()
    {
        var frame = GameLinkPicture.ForSpot(_world, 0, 0);
        var (x, z) = frame.StandingPoint;
        Assert.Equal((frame.Camera.X, frame.Camera.Z), (x, z));
        Assert.InRange(AngleBelowHorizon(frame), 18, 25);
        Assert.InRange(FrameCoverage(frame, new(32, 0, 32)), 0.65f, 0.95f);
        _world.Height[_world.WorldToGrid(x), _world.WorldToGrid(z)] = 180 * 257;
        frame = GameLinkPicture.ForSpot(_world, 0, 0);

        Assert.Equal(new Int3(0, 60, 0), frame.Look);
        Assert.True(frame.Camera.Y > 180);
        Assert.Equal((x, z), frame.StandingPoint);
        FrameCoverage(frame, new(32, 0, 32));
    }

    private static double AngleBelowHorizon(GameLinkPicture frame)
    {
        double dx = frame.Camera.X - frame.Look.X, dz = frame.Camera.Z - frame.Look.Z;
        return Math.Atan2(frame.Camera.Y - frame.Look.Y, Math.Sqrt(dx * dx + dz * dz)) * 180 / Math.PI;
    }

    private static float FrameCoverage(GameLinkPicture frame, Int3 size)
    {
        var (x, y, z) = frame.Camera;
        var look = new Vector3(frame.Look.X - x, frame.Look.Y - y, frame.Look.Z - z);
        var forward = Vector3.Normalize(look);
        var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
        var up = Vector3.Cross(forward, right);
        float vertical = MathF.Tan(GameLinkProtocol.PictureVerticalFov * MathF.PI / 360);
        float horizontal = vertical * GameLinkProtocol.PictureMinimumAspect;
        Vector2 min = Vector2.One, max = -Vector2.One;
        foreach (int sx in new[] { -1, 1 })
        foreach (int sy in new[] { -1, 1 })
        foreach (int sz in new[] { -1, 1 })
        {
            var corner = look + new Vector3(sx * size.X / 2f, sy * size.Y / 2f, sz * size.Z / 2f);
            float depth = Vector3.Dot(forward, corner);
            Assert.True(depth > 0);
            var point = new Vector2(Vector3.Dot(right, corner) / (depth * horizontal),
                Vector3.Dot(up, corner) / (depth * vertical));
            Assert.InRange(point.X, -1, 1);
            Assert.InRange(point.Y, -1, 1);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        Vector2 coverage = (max - min) / 2;
        return Math.Max(coverage.X, coverage.Y);
    }
}
