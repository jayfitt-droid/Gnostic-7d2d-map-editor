using MapEditor.Core.Ops;

namespace MapEditor.App;

/// <summary>A land shape as offered in New World and the Generate panel.</summary>
public sealed record LandShapeChoice(LandShapeKind Kind, string Label, string Hint)
{
    public static IReadOnlyList<LandShapeChoice> All { get; } =
    [
        new(LandShapeKind.Edges, "Land to the edges", "Land everywhere; each edge gets land or ocean borders, as in the game."),
        new(LandShapeKind.Island, "One island", "One island with ocean all round."),
        new(LandShapeKind.Archipelago, "Several islands", "A group of islands with ocean between them and all round."),
        new(LandShapeKind.Peninsula, "Peninsula", "Land joined to one edge, with ocean on the other three."),
        new(LandShapeKind.Drawn, "Draw your own", "Draw the land and water in Land mode."),
    ];
}
