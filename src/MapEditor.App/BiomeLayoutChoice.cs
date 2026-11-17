using MapEditor.Core.Ops;

namespace MapEditor.App;

/// <summary>A biome layout as New World and the Terrain stage list it.</summary>
public sealed record BiomeLayoutChoice(BiomeLayoutKind Kind, string Label)
{
    private static readonly BiomeLayoutChoice[] Rwg =
    [
        new(BiomeLayoutKind.CenterForest, "Forest in the middle"),
        new(BiomeLayoutKind.CenterWasteland, "Wasteland in the middle"),
        new(BiomeLayoutKind.Circle, "Ring"),
        new(BiomeLayoutKind.Circle2, "Two rings"),
        new(BiomeLayoutKind.Line, "Line"),
    ];

    /// <summary>One biome per island, harder outward (#46): only for archipelagos.</summary>
    public static readonly BiomeLayoutChoice PerIsland = new(BiomeLayoutKind.PerIsland, "One biome per island");

    /// <summary>The layouts for a land shape: the game's, plus one biome per island on an archipelago.</summary>
    public static IReadOnlyList<BiomeLayoutChoice> For(LandShapeKind shape) =>
        shape == LandShapeKind.Archipelago ? [.. Rwg, PerIsland] : Rwg;
}
