using System.Text.Json.Serialization;
using MapEditor.Core.Edit;

namespace MapEditor.Core.Ops;

/// <summary>A box in world metres (Z from the south), with its own coastline settings.</summary>
public sealed record LandBox(int X0, int Z0, int X1, int Z1, bool CutsWater = false, int Seed = 1,
    int Fill = 60, int Roughness = 50)
{
    public const int MinFill = 10, MaxFill = 80;

    [JsonIgnore] public GridRect Bounds => new(X0, Z0, X1, Z1);
}

/// <summary>The editable sources of a drawn coastline.</summary>
public sealed class LandDrawing
{
    private readonly List<LandBox> _boxes = [];
    public List<LandBox> Boxes { get => _boxes; init => _boxes = value ?? []; }

    public LandDrawing Clone() => new() { Boxes = [.. Boxes] };

    public bool SameLand(LandDrawing? other) =>
        other is null ? Boxes.Count == 0 : Boxes.ToHashSet().SetEquals(other.Boxes);

    /// <summary>Adds land boxes and then applies the water boxes, independent of their order in the drawing.</summary>
    internal void Rasterize(int worldSize, bool[] land, sbyte[] forced)
    {
        int w = worldSize / LandField.Cell;
        foreach (var box in Boxes.OrderBy(b => b.CutsWater))
        {
            var bounds = box.Bounds.Clamp(worldSize, worldSize);
            if (bounds.IsEmpty) continue;
            int width = bounds.X1 - bounds.X0, depth = bounds.Z1 - bounds.Z0;
            var noise = new Noise(box.Seed);
            float rough = Math.Clamp(box.Roughness, 0, 100) / 100f;
            float fill = Math.Clamp(box.Fill, LandBox.MinFill, LandBox.MaxFill) / 100f;
            int x0 = bounds.X0 / LandField.Cell, x1 = Math.Min(w, (bounds.X1 + LandField.Cell - 1) / LandField.Cell);
            int z0 = bounds.Z0 / LandField.Cell, z1 = Math.Min(w, (bounds.Z1 + LandField.Cell - 1) / LandField.Cell);
            Parallel.For(z0, z1, z =>
            {
                float v = ((z + 0.5f) * LandField.Cell - bounds.Z0) / depth;
                if (v < 0 || v >= 1) return;
                float b = Axis(v, bounds.Z0 == 0, bounds.Z1 == worldSize);
                for (int x = x0; x < x1; x++)
                {
                    int i = x + z * w;
                    float u = ((x + 0.5f) * LandField.Cell - bounds.X0) / width;
                    if (u < 0 || u >= 1) continue;
                    if (!box.CutsWater) forced[i] = 0; // Cleanup may only add land within a land box.
                    if (land[i] == !box.CutsWater) continue;
                    float a = Axis(u, bounds.X0 == 0, bounds.X1 == worldSize);
                    // The rounded base and the fade both keep the coast off the box's sides. Noise is
                    // measured in fractions of the box, so it moves with the box and scales with its size.
                    float outline = a * a * (0.75f + 0.25f * a) + b * b * (0.75f + 0.25f * b);
                    float variation = noise.Fbm(u * 3 + 12.3f, v * 3 + 5.7f, 4) * (0.12f + 0.58f * rough);
                    float fade = Math.Clamp((1 - Math.Max(a, b)) / 0.17f, 0, 1);
                    if (fade * (0.14f + fill + variation) <= outline) continue;
                    land[i] = !box.CutsWater;
                    if (box.CutsWater) forced[i] = -1;
                }
            });
        }
    }

    // At a world edge, the rounded shape continues through the edge, as if the rest of it were off the map.
    private static float Axis(float t, bool lowEdge, bool highEdge) =>
        lowEdge ? (highEdge ? 0 : t) : highEdge ? 1 - t : MathF.Abs(2 * t - 1);
}
