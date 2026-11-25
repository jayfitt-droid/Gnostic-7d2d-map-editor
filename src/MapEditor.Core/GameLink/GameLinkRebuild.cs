using MapEditor.Core.GameData;
using MapEditor.Core.IO;
using MapEditor.Core.Model;

namespace MapEditor.Core.GameLink;

public enum AfterExportAction { None, Playtest, Rebuild }

/// <summary>Editor-side preparation and progress checks for rebuilding a save.</summary>
public static class GameLinkRebuild
{
    /// <summary>
    /// Summarizes the rebuild using prefabs from the current export folder, the edited world and
    /// <paramref name="kept"/>, a layout the game may still have loaded (#245). It never counts fewer chunks than the game rebuilds.
    /// </summary>
    public static GameLinkRebuildArea.Summary SummarizeExport(string folder, World world, PrefabLibrary library,
        GameLinkPlayer player, int radius, IEnumerable<PrefabInstance>? kept = null)
    {
        string path = Path.Combine(folder, WorldFiles.Prefabs);
        List<PrefabInstance> previous;
        try { previous = WorldXml.ReadPrefabs(path); }
        catch (FileNotFoundException) { previous = []; }
        catch (DirectoryNotFoundException) { previous = []; }
        int min = world.GridToWorld(0), max = world.GridToWorld(world.Size);
        return GameLinkRebuildArea.Summarize(player.X, player.Z, radius, new(min, min, max, max),
            previous.Concat(kept ?? []).Concat(world.Prefabs).Select(prefab => PrefabFootprint(prefab, library.Find(prefab.Name))));
    }

    /// <summary>Uses the prefab's corner and rotated size, or one block if its size is unknown.</summary>
    public static GameLinkRebuildArea.Footprint PrefabFootprint(PrefabInstance prefab, PrefabInfo? info)
    {
        var (x, _, z) = prefab.Position;
        var (width, depth) = info?.Footprint(prefab.Rotation) ?? (1, 1);
        return new(x, z, x + width, z + depth);
    }

    public static AfterExportAction ExportPreference(AfterExportAction? choice, bool? legacyOpen) =>
        choice is { } action && Enum.IsDefined(action) ? action :
        legacyOpen == true ? AfterExportAction.Playtest : AfterExportAction.None;

    public static string? UnavailableReason(string? folder, GameInstall? install, GameLinkStatus? status,
        string? previousExportFolder = null, bool? exportHasChanges = null, GameLinkError statusError = GameLinkError.None)
    {
        if (install is null) return "Set the game folder to rebuild an area.";
        if (!GameLinkPlaytest.CanOpen(folder, install))
            return "Export to a world folder the game uses, such as GeneratedWorlds.";
        if (exportHasChanges == false && WorldFiles.SameFolder(folder!, previousExportFolder))
            return "There are no map changes since the last export to this folder.";
        if (status is null)
            return GameLinkWorld.ModMismatchText(statusError) ?? "Start the game with Gnostic Game Link to rebuild an area.";
        if (status.State == GameLinkState.Remote)
            return "Host the save to rebuild an area.";
        if (status.State != GameLinkState.Ready || status.Player is null ||
            !GameLinkOpenRequest.IsFolderName(status.Save))
            return "Enter a save and wait for the player to spawn.";
        if (!Same(status.World, Path.GetFileName(folder)!))
            return "The current save uses a different world.";
        if (!status.Hosting) return "Host the save to rebuild an area.";
        if (status.OtherPlayers) return "All other players must leave the save first.";
        if (status.Operation?.State is "leaving" or "opening" or "rebuilding")
            return "Wait for the current Game Link operation to finish.";
        return null;
    }

    public static bool IsSameSave(GameLinkStatus first, GameLinkStatus second) =>
        Same(first.World, second.World) && Same(first.Save, second.Save);

    public static GameLinkWaitResult Wait(GameLinkStatus? status, string world, string save, string id, bool timedOut)
    {
        if (status?.Operation is { Kind: "rebuild" } op && op.Id == id)
        {
            if (op.State == "failed") return new(GameLinkWaitState.Failed, op.Reason);
            if (op.State == "blocked") return new(GameLinkWaitState.Blocked, op.Reason);
            if (op.State == "done" && status is { State: GameLinkState.Ready, Hosting: true } &&
                Same(status.World, world) && Same(status.Save, save))
                return new(GameLinkWaitState.Rebuilt) { Chunks = op.Chunks };
        }
        return new(timedOut ? GameLinkWaitState.TimedOut : GameLinkWaitState.Waiting);
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
