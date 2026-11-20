namespace MapEditor.Core.Rendering;

/// <summary>Coast preview and map water colours, packed as RGB.</summary>
public static class CoastColours
{
    public const uint DeepWater = 0x203952;
    private const uint ShallowWater = 0x70A2BC;
    private const int DeepDistance = -320;
    private const int ShallowDistance = -4;
    private const int InlandDistance = 30;
    private const int StepsPerMetre = 2;
    private const float DeepWaterDepth = 12;
    private static readonly uint[] Ramp = BuildRamp();

    /// <summary>Preview colour at a signed distance from the coast in metres, positive inland.</summary>
    public static uint AtDistance(float distance)
    {
        float index = (distance - DeepDistance) * StepsPerMetre;
        return Ramp[(int)Math.Clamp(index + 0.5f, 0, Ramp.Length - 1)];
    }

    /// <summary>Map water colour from the surface down to the depth at which the sea floor reaches its darkest colour.</summary>
    public static uint AtWaterDepth(float metres)
    {
        float depth = Math.Clamp(metres / DeepWaterDepth, 0, 1);
        return AtDistance(ShallowDistance + (DeepDistance - ShallowDistance) * depth);
    }

    /// <summary>Precompute the smoothstep colour ramp at half-metre intervals in signed coast distance.</summary>
    private static uint[] BuildRamp()
    {
        (int Metres, uint Rgb)[] stops =
        [
            (DeepDistance, DeepWater), (-115, 0x2B4E6E), (-32, 0x457699), (ShallowDistance, ShallowWater),
            (3, 0xD2C693), (20, 0xD2C693), (InlandDistance, 0x608965),
        ];
        var ramp = new uint[(InlandDistance - DeepDistance) * StepsPerMetre + 1];
        for (int i = 1; i < stops.Length; i++)
        {
            var (lo, from) = stops[i - 1];
            var (hi, to) = stops[i];
            int first = (lo - DeepDistance) * StepsPerMetre;
            int steps = (hi - lo) * StepsPerMetre;
            for (int n = 0; n <= steps; n++)
            {
                float t = (float)n / steps;
                t = t * t * (3 - 2 * t);
                uint r = BlendChannel(from >> 16, to >> 16, t);
                uint g = BlendChannel((from >> 8) & 255, (to >> 8) & 255, t);
                uint b = BlendChannel(from & 255, to & 255, t);
                ramp[first + n] = (r << 16) | (g << 8) | b;
            }
        }
        return ramp;
    }

    private static uint BlendChannel(float from, float to, float t) => (uint)(from + (to - from) * t + 0.5f);
}
