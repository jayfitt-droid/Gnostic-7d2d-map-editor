using System.Numerics;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;

namespace MapEditor.Core.GameLink;

/// <summary>A picture's framing and the ground point to teleport to before taking it.</summary>
public readonly record struct GameLinkPicture(Int3 Camera, Int3 Look)
{
    private static readonly float HalfField = MathF.Tan(GameLinkProtocol.PictureVerticalFov * MathF.PI / 360);

    public (int X, int Z) StandingPoint => (Camera.X, Camera.Z);

    /// <summary>The ground heading towards the target, rounded to the nearest compass direction.</summary>
    public Facing? StandingFacing
    {
        get
        {
            int dx = Look.X - Camera.X, dz = Look.Z - Camera.Z;
            if (dx == 0 && dz == 0) return null;
            int heading = (int)Math.Round(Math.Atan2(dx, dz) / (Math.PI / 4));
            return (Facing)(heading & 7);
        }
    }

    /// <summary>Frames the rotated bounding box of a placed prefab from its front.</summary>
    public static GameLinkPicture ForPrefab(World world, PrefabInstance prefab, PrefabInfo? info)
    {
        if (info is null) return ForSpot(world, prefab.Position.X, prefab.Position.Z);
        var (width, depth) = info.Footprint(prefab.Rotation);
        int bottom = prefab.Position.Y + (prefab.NeedsGroundLevelOffset ? info.YOffset : 0);
        return Frame(world, new(prefab.Position.X + width / 2, bottom + info.Size.Y / 2,
            prefab.Position.Z + depth / 2), new(width, info.Size.Y, depth), info.FrontDirection(prefab.Rotation),
            slope: 0.27f, frameFraction: 0.90f);
    }

    /// <summary>Frames a 32 m square centred on a spot in game coordinates.</summary>
    public static GameLinkPicture ForSpot(World world, int x, int z) =>
        Frame(world, new(x, GroundAt(world, x, z), z), new(32, 0, 32), front: 2, slope: 0.4f, frameFraction: 0.82f);

    private static GameLinkPicture Frame(World world, Int3 look, Int3 size, int front, float slope, float frameFraction)
    {
        int min = world.GridToWorld(0), max = world.GridToWorld(world.Size - 1);
        look = look with { X = Math.Clamp(look.X, min, max), Z = Math.Clamp(look.Z, min, max) };
        // Full-size worlds push players away from the outer edge. Leave room for the player below the camera.
        int margin = world.Size > 1024 ? 130 : 0;
        int minCamera = min + margin, maxCamera = max - margin;
        var halfSize = new Vector3(MathF.Ceiling(size.X / 2f), MathF.Ceiling(size.Y / 2f), MathF.Ceiling(size.Z / 2f));
        // Prefab fronts count counter-clockwise from north.
        var (frontX, frontZ) = front switch { 0 => (0, 1), 1 => (-1, 0), 2 => (0, -1), _ => (1, 0) };
        bool eastWest = frontX != 0;
        Vector3 forward = Vector3.Normalize(new Vector3(-frontX, -slope, -frontZ));
        int setback = (int)MathF.Ceiling(DistanceToFit(halfSize, forward, frameFraction) * Math.Abs(eastWest ? forward.X : forward.Z)) + 1;
        Int3 camera = Position(setback);
        var sightLine = new Vector3(look.X - camera.X, look.Y - camera.Y, look.Z - camera.Z);
        if (sightLine.Length() >= DistanceToFit(halfSize, Vector3.Normalize(sightLine), 0.95f))
            return new(camera, look);

        // If the edge or the ground forces a different angle, fit the box's enclosing sphere.
        float distance = Math.Max(8, halfSize.Length()) / (0.95f * MathF.Sin(MathF.Atan(HalfField)));
        return new(Position((int)MathF.Ceiling(distance), distance), look);

        Int3 Position(int offset, float minimumDistance = 0)
        {
            int frontAxis = eastWest ? look.X : look.Z;
            int sideAxis = eastWest ? look.Z : look.X;
            int cameraFront = Math.Clamp(frontAxis + (frontX + frontZ) * offset, minCamera, maxCamera);
            int cameraSide = Math.Clamp(sideAxis, minCamera, maxCamera);
            int frontDistance = Math.Abs(frontAxis - cameraFront);
            if (frontDistance < offset)
            {
                int side = sideAxis - minCamera > maxCamera - sideAxis ? -1 : 1;
                int lateral = (int)Math.Ceiling(Math.Sqrt((double)offset * offset - (double)frontDistance * frontDistance));
                cameraSide = Math.Clamp(sideAxis + side * lateral, minCamera, maxCamera);
            }

            int x = eastWest ? cameraFront : cameraSide;
            int z = eastWest ? cameraSide : cameraFront;
            double sideDistance = sideAxis - cameraSide;
            double horizontalSquared = sideDistance * sideDistance + (double)frontDistance * frontDistance;
            double rise = Math.Max(slope * Math.Sqrt(horizontalSquared),
                Math.Sqrt(Math.Max(0, (double)minimumDistance * minimumDistance - horizontalSquared)));
            int y = Math.Max(look.Y + (int)Math.Ceiling(Math.Max(2, rise)), GroundAt(world, x, z) + 8);
            return new(x, y, z);
        }
    }

    private static float DistanceToFit(Vector3 halfSize, Vector3 forward, float frameFraction)
    {
        var right = Vector3.Cross(Vector3.UnitY, forward);
        right = right == Vector3.Zero ? Vector3.UnitX : Vector3.Normalize(right);
        var up = Vector3.Cross(forward, right);
        float vertical = HalfField * frameFraction;
        float horizontal = vertical * GameLinkProtocol.PictureMinimumAspect;
        float distance = 0;
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            var corner = new Vector3(x * halfSize.X, y * halfSize.Y, z * halfSize.Z);
            float fromCentre = Vector3.Dot(forward, corner);
            // A corner fits when its depth can contain both of its projected offsets.
            float requiredDepth = Math.Max(Math.Abs(Vector3.Dot(right, corner)) / horizontal,
                Math.Abs(Vector3.Dot(up, corner)) / vertical);
            distance = Math.Max(distance, requiredDepth - fromCentre);
        }
        return distance;
    }

    private static int GroundAt(World world, int x, int z) =>
        (int)Math.Ceiling(World.HeightToMetres(world.Height[
            Math.Clamp(world.WorldToGrid(x), 0, world.Size - 1), Math.Clamp(world.WorldToGrid(z), 0, world.Size - 1)]));
}
