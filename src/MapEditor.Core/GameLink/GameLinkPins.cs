namespace MapEditor.Core.GameLink;

/// <summary>Collects matching playtest notes in the existing project list.</summary>
public static class GameLinkPins
{
    /// <summary>Identifies a world's queue without putting a file name in a request.</summary>
    public static string WorldKey(string world) => GameLinkPinFile.WorldKey(world);

    public static int Merge(List<GameLinkPin> projectPins, IEnumerable<GameLinkPin> received, string? exportFolder)
    {
        string? world = GameLinkWorld.ExportName(exportFolder);
        if (world is null) return 0;

        var seen = projectPins.Select(pin => pin.Id).ToHashSet();
        int before = projectPins.Count;
        foreach (var pin in received)
            if (string.Equals(pin.World, world, StringComparison.OrdinalIgnoreCase) && seen.Add(pin.Id))
                projectPins.Add(pin);
        return projectPins.Count - before;
    }
}
