using MapEditor.Core.GameData;

namespace MapEditor.Core.GameLink;

public enum GameLinkWaitState { Waiting, AtMenu, Teleport, Blocked, Failed, TimedOut }

public readonly record struct GameLinkWaitResult(GameLinkWaitState State, string Reason = "");

/// <summary>Targets and progress checks for opening an exported world.</summary>
public static class GameLinkPlaytest
{
    public static bool CanOpen(string? folder, GameInstall? install) =>
        install is not null && IsWorldFolder(folder, GameInstall.UserGeneratedWorldsDir,
            install.WorldsDir, install.ModsDir, GameInstall.UserModsDir);

    internal static bool IsWorldFolder(string? folder, string generatedWorlds, string gameWorlds,
        string gameMods, string userMods)
    {
        if (folder is null) return false;
        string full = Normalize(folder);
        if (Path.GetDirectoryName(full) is not { } parent || !GameLinkOpenRequest.IsFolderName(Path.GetFileName(full)))
            return false;
        if (Same(parent, Normalize(generatedWorlds)) || Same(parent, Normalize(gameWorlds))) return true;
        if (!Same(Path.GetFileName(parent), "Worlds") || Path.GetDirectoryName(parent) is not { } mod ||
            Path.GetDirectoryName(mod) is not { } mods)
            return false;
        return Same(mods, Normalize(gameMods)) || Same(mods, Normalize(userMods));

        static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    public static string SaveName(string world)
    {
        const string suffix = " Playtest";
        string name = new string(world.Where(GameLinkOpenRequest.IsFolderNameCharacter).ToArray()).Trim().TrimEnd('.');
        if (name.Length > 255 - suffix.Length)
        {
            int length = 255 - suffix.Length;
            if (char.IsHighSurrogate(name[length - 1])) length--;
            name = name[..length].TrimEnd();
        }
        return name.Length == 0 ? "Playtest" : name + suffix;
    }

    /// <summary>Checks whether New Game can enter the full save name without changing it.</summary>
    public static bool CanEnterSaveName(string save) =>
        save.Length is > 0 and <= 50 && save[0] != '.' && save[^1] != '.' && save.Trim() == save &&
        save.All(c => c is >= '\u0100' or >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-' or ' ' or '.');

    /// <summary>Accepts the menu only once the corresponding leave operation reaches it.</summary>
    public static GameLinkWaitResult WaitForMenu(GameLinkStatus? status, string id, bool timedOut)
    {
        if (status?.Operation is { Kind: "leave" } op && op.Id == id)
        {
            if (op.State == "failed") return new(GameLinkWaitState.Failed, op.Reason);
            if (op.State == "at_menu" && status.State == GameLinkState.Menu) return new(GameLinkWaitState.AtMenu);
        }
        return Pending(timedOut);
    }

    /// <summary>Allows teleport only after the target save is ready and its open operation, if sent, is done.</summary>
    public static GameLinkWaitResult WaitForSave(GameLinkStatus? status, string world, string save, string? id, bool timedOut)
    {
        var op = status?.Operation;
        bool matchingOpen = id is not null && op is { Kind: "open" } && op.Id == id;
        if (matchingOpen && op!.State == "failed") return new(GameLinkWaitState.Failed, op.Reason);
        if (matchingOpen && op!.State == "blocked") return new(GameLinkWaitState.Blocked, op.Reason);
        if (id is null && status is { State: GameLinkState.Menu, Blocked: { } blocked } &&
            Same(blocked.World, world) && Same(blocked.Save, save))
            return new(GameLinkWaitState.Blocked, blocked.Reason);
        if (status is { Hosting: true, State: GameLinkState.Ready } && Same(status.World, world) && Same(status.Save, save) &&
            (id is null ? op?.State is not ("leaving" or "opening") : matchingOpen && op!.State == "done"))
            return new(GameLinkWaitState.Teleport);
        return Pending(timedOut);
    }

    private static GameLinkWaitResult Pending(bool timedOut) =>
        new(timedOut ? GameLinkWaitState.TimedOut : GameLinkWaitState.Waiting);

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
