namespace MapEditor.Core.Tours;

/// <summary>The editor state a tour can observe. These keys are kept out of world saves.</summary>
public enum TourCondition
{
    None, WorldOpen, OutsideLandMode, LandBoxDrawn, DrawnTerrainBuilt, TerrainGenerated,
    TownSitesGenerated, FeaturesGenerated, HighwaysGenerated, WildernessGenerated, SpawnsGenerated,
    Raised, Smoothed, TownBuilt, RoadDrawn, Validated, Exported,
}

/// <summary>The editor control a step describes.</summary>
public enum TourTarget
{
    NewWorld, Rectangle, SelectBoxes, BuildWorld, Terrain, TownSites, Features, Highways, Wilderness, Spawns,
    Raise, Smooth, Town, Road, Validate, Export,
}

/// <summary>A tip and the editor state that completes it or makes it unavailable.</summary>
public sealed record TourStep(string Id, string Title, string Text, TourTarget Target,
    TourCondition Done = TourCondition.None, TourCondition SkipIf = TourCondition.None)
{
    public bool ExplainDrawnTerrain { get; init; }

    internal bool IsDone(Func<TourCondition, bool> holds) =>
        Done != TourCondition.None && holds(Done) && !(ExplainDrawnTerrain && holds(TourCondition.DrawnTerrainBuilt));

    public string Body(Func<TourCondition, bool> holds) =>
        ExplainDrawnTerrain && holds(TourCondition.DrawnTerrainBuilt)
            ? "Build World made the terrain. Click Next."
            : Text;
}

/// <summary>A named sequence of guided editor steps.</summary>
public sealed record Tour(string Name, IReadOnlyList<TourStep> Steps);

/// <summary>The two tours offered by the editor.</summary>
public static class GuidedTours
{
    private static readonly TourStep Terrain = new("terrain", "Stage 1. Terrain", "Click Generate to make the terrain.",
        TourTarget.Terrain, TourCondition.TerrainGenerated);
    private static readonly TourStep Spawns = new("spawns", "Stage 6. Spawn points", "Click Generate to add spawn points.",
        TourTarget.Spawns, TourCondition.SpawnsGenerated);
    private static readonly TourStep Validate = new("validate", "Validate", "Press F7 to check the world for problems.",
        TourTarget.Validate, TourCondition.Validated);
    private static readonly TourStep Export = new("export", "Export", "Press Ctrl+E to export the world to the game.",
        TourTarget.Export, TourCondition.Exported);

    public static Tour DrawIslands { get; } = new("Draw your own islands",
    [
        new("new-world", "New World", "Click New World. Under Land shape, select Draw your own, then click Create.",
            TourTarget.NewWorld, TourCondition.WorldOpen, TourCondition.WorldOpen),
        new("rectangle", "Rectangle", "Select Rectangle. Drag boxes on the map to make islands. Hold Alt to draw water.",
            TourTarget.Rectangle, TourCondition.LandBoxDrawn, TourCondition.OutsideLandMode),
        new("select-boxes", "Select boxes", "Click Select boxes, then click a box to move or resize it. Set Fill and Roughness in the Land panel.",
            TourTarget.SelectBoxes, SkipIf: TourCondition.OutsideLandMode),
        new("build-world", "Build World", "Click Build World to make the ground, beaches and biomes.",
            TourTarget.BuildWorld, TourCondition.DrawnTerrainBuilt, TourCondition.OutsideLandMode),
        Terrain with { ExplainDrawnTerrain = true },
        new("town-sites", "Stage 2. Town sites", "Click Plan towns. Look at the towns on the map, then click Build.",
            TourTarget.TownSites, TourCondition.TownSitesGenerated),
        new("features", "Stage 3. Lakes, rivers, canyons, craters", "Click Generate.",
            TourTarget.Features, TourCondition.FeaturesGenerated),
        new("highways", "Stage 4. Highways", "Click Generate to join the towns.",
            TourTarget.Highways, TourCondition.HighwaysGenerated),
        new("wilderness", "Stage 5. Wilderness POIs and gravel roads", "Click Generate.",
            TourTarget.Wilderness, TourCondition.WildernessGenerated),
        Spawns, Validate, Export,
    ]);

    public static Tour StagesAndTools { get; } = new("Stages and tools together",
    [
        new("new-world", "New World", "Click New World. Keep Land to the edges, then click Create.",
            TourTarget.NewWorld, TourCondition.WorldOpen, TourCondition.WorldOpen),
        Terrain,
        new("raise", "Raise", "Select Raise and drag on the map to make a hill.", TourTarget.Raise, TourCondition.Raised),
        new("smooth", "Smooth", "Select Smooth and drag over the hill to soften it.", TourTarget.Smooth, TourCondition.Smoothed),
        new("town", "Town", "Select Town and drag a rectangle to plan your own city. Press Enter to build it.",
            TourTarget.Town, TourCondition.TownBuilt),
        new("town-sites", "Stage 2. Town sites", "Click Plan towns, then Build. Your city stays.",
            TourTarget.TownSites, TourCondition.TownSitesGenerated),
        new("highways", "Stage 4. Highways", "Skip stage 3 for this tour. Click Generate to join your city to the other towns.",
            TourTarget.Highways, TourCondition.HighwaysGenerated),
        new("road", "Road", "Select Road. Click points on the map, then double-click to finish a road.",
            TourTarget.Road, TourCondition.RoadDrawn),
        new("wilderness", "Stage 5. Wilderness POIs and gravel roads", "Click Generate. Gravel roads can join your road.",
            TourTarget.Wilderness, TourCondition.WildernessGenerated),
        Spawns, Validate, Export,
    ]);
}

/// <summary>Whether new worlds should offer a tour after the user's choice.</summary>
public static class TourOffer
{
    public static bool ShouldShow(bool dismissed) => !dismissed;

    public static bool IsDismissed(bool dismissed, bool started, bool dontShowAgain) =>
        dismissed || started || dontShowAgain;
}
