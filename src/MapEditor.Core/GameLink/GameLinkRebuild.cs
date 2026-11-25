using MapEditor.Core.GameData;

namespace MapEditor.Core.GameLink;

public enum AfterExportAction { None, Playtest, Rebuild }

/// <summary>Editor-side eligibility and progress checks for rebuilding a save.</summary>
public static class GameLinkRebuild
{
    public static AfterExportAction ExportPreference(AfterExportAction? choice, bool? legacyOpen) =>
        choice is { } action && Enum.IsDefined(action) ? action :
        legacyOpen == true ? AfterExportAction.Playtest : AfterExportAction.None;

    public static string? UnavailableReason(string? folder, GameInstall? install, GameLinkStatus? status)
    {
        if (install is null) return "Set the game folder to rebuild an area.";
        if (!GameLinkPlaytest.CanOpen(folder, install))
            return "Export to a world folder the game uses, such as GeneratedWorlds.";
        if (status is null) return "Start the game with Gnostic Game Link to rebuild an area.";
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
