using MapEditor.Core.GameLink;
using Platform;
using UnityEngine;

namespace GnosticGameLink;

/// <summary>Advances one menu operation at a time on Unity's main thread.</summary>
internal sealed class GameLinkMenuFlow
{
    private enum Step { Disconnect, WaitForMenu, OpenPage, SelectTarget, PressStart, WaitForSave, WaitForReturn, Idle }

    private const float Timeout = 300f;
    private World? leavingWorld;
    private GameLinkOpenRequest? target;
    private GameLinkRebuildRequest? rebuild;
    private (string World, string Save, UserDataStorageType Storage, GameLinkRebuildArea.Footprint[] Prefabs)? leftSave;
    private XUiC_SavegamesList.ListEntry? selected;
    private XUiC_SavegamesList.ListEntry? blockedEntry;
    private UserDataStorageType? saveStorage;
    private Step step = Step.Idle;
    private float deadline;
    private string id = "";
    private string kind = "";
    private string state = "";
    private string reason = "";
    private int chunks;

    public bool Busy => step != Step.Idle;
    public object? Operation => id.Length == 0 ? null : new { id, kind, state, reason, chunks };

    public string Leave(GameManager game, World world)
    {
        if (LeaveRefusal(game, world) is { } refusal)
            return GameLinkReply.Error(refusal, "The game cannot leave this save right now.");
        leavingWorld = world;
        leftSave = null;
        return Begin(GameLinkProtocol.CreateOperationId(), "leave", "leaving", Step.Disconnect);
    }

    public string Open(string requestId)
    {
        string? error = GameLinkOpenRequest.Take(GameLinkProtocol.RequestDirectory, requestId, DateTimeOffset.UtcNow, out target);
        if (error != null)
            return GameLinkReply.Error(error, "The open request is missing, invalid or expired.");
        leftSave = null;
        return Begin(requestId, "open", "opening", Step.OpenPage);
    }

    public string Rebuild(string requestId)
    {
        string? error = GameLinkRebuildRequest.Take(GameLinkProtocol.RequestDirectory, requestId,
            DateTimeOffset.UtcNow, out var request);
        if (error != null)
            return GameLinkReply.Error(error, "The rebuild request is missing, invalid or expired.");
        if (state != "at_menu" || leftSave is not { } previous ||
            !Same(request!.World, previous.World) || !Same(request.Save, previous.Save))
            return GameLinkReply.Error("save_not_found", "The game did not just leave this save.");
        rebuild = request;
        target = new(request.World, request.Save, create: false);
        return Begin(requestId, "rebuild", "rebuilding", Step.OpenPage);
    }

    private string Begin(string operationId, string operationKind, string progress, Step firstStep)
    {
        id = operationId;
        kind = operationKind;
        state = progress;
        reason = "";
        chunks = 0;
        blockedEntry = null;
        step = firstStep;
        deadline = Time.realtimeSinceStartup + Timeout;
        return GameLinkReply.Accepted(id);
    }

    private void Finish(string progress, string failure = "")
    {
        if (kind is "leave" or "open")
        {
            string detail = failure.Length == 0 ? "" : $" reason={failure}";
            Log.Out($"[Gnostic Game Link] {kind.ToUpperInvariant()} id={id} {state} -> {progress}{detail}");
        }
        state = progress;
        reason = failure;
        step = Step.Idle;
        leavingWorld = null;
        target = null;
        rebuild = null;
        if (progress != "at_menu") leftSave = null;
        selected = null;
        saveStorage = null;
    }

    public void Update()
    {
        try
        {
            var game = GameManager.Instance;
            if (!Busy)
            {
                if (game?.World != null) leftSave = null;
                return;
            }
            if (Time.realtimeSinceStartup >= deadline)
            {
                Finish("failed", "timeout");
                return;
            }

            switch (step)
            {
                case Step.Disconnect:
                    if (LeaveRefusal(game, leavingWorld!) is { } refusal)
                        Finish("failed", refusal);
                    else
                    {
                        var cache = leavingWorld!.ChunkCache;
                        leftSave = (GamePrefs.GetString(EnumGamePrefs.GameWorld), GamePrefs.GetString(EnumGamePrefs.GameName),
                            (UserDataStorageType)GamePrefs.GetInt(EnumGamePrefs.GameSaveStorageType),
                            GameLinkChunkRebuild.Prefabs(cache.ChunkProvider));
                        game.Disconnect();
                        step = Step.WaitForMenu;
                    }
                    break;
                case Step.WaitForMenu:
                    if (UsableMenu() != null) Finish("at_menu");
                    break;
                case Step.OpenPage:
                    OpenPage(target!);
                    break;
                case Step.SelectTarget:
                    SelectTarget(target!);
                    break;
                case Step.PressStart:
                    PressStart(target!);
                    break;
                case Step.WaitForSave:
                case Step.WaitForReturn:
                    FinishWhenReady(game);
                    break;
            }
        }
        catch (Exception ex)
        {
            // Keep the listener available if the game rejects a menu action.
            Log.Warning($"[Gnostic Game Link] Menu operation failed: {ex}");
            if (Busy) Finish("failed", rebuild != null && step is (Step.WaitForSave or Step.WaitForReturn)
                ? "reset_failed" : "failed");
        }
    }

    private void FinishWhenReady(GameManager game)
    {
        var world = game.World;
        var player = world?.GetPrimaryPlayer();
        if (!ConnectionManager.Instance.IsServer || game.IsStartingGame ||
            player == null || !player.IsSpawned() || player.IsDead() ||
            !Same(GamePrefs.GetString(EnumGamePrefs.GameWorld), target!.World) ||
            !Same(GamePrefs.GetString(EnumGamePrefs.GameName), target.Save))
            return;
        if (rebuild is null || step == Step.WaitForReturn)
        {
            Finish("done");
            return;
        }
        if (HasOtherPlayers(world!))
        {
            Finish("failed", "players");
            return;
        }
        if (GamePrefs.GetInt(EnumGamePrefs.GameSaveStorageType) != (int)leftSave!.Value.Storage)
        {
            Finish("failed", "save_not_found");
            return;
        }
        chunks = GameLinkChunkRebuild.Run(game, world!, player, rebuild, leftSave.Value.Prefabs);
        if (chunks > 0)
            step = Step.WaitForReturn;
        else
            Finish("failed", "reset_failed");
    }

    public static bool HasOtherPlayers(World world) =>
        ConnectionManager.Instance.ClientCount() > 0 || world.GetLocalPlayers().Count > 1;

    private static string? LeaveRefusal(GameManager game, World world)
    {
        var connection = ConnectionManager.Instance;
        if (game == null || game.World != world || connection == null || !connection.IsServer)
            return "not_ready";
        if (HasOtherPlayers(world)) return "players";
        if (!game.IsSafeToDisconnect() || SaveInProgress(world)) return "busy";
        return null;
    }

    private static bool SaveInProgress(World world)
    {
        if (world.ChunkCache.ChunkProvider is not ChunkProviderGenerateWorld provider ||
            provider.m_RegionFileManager is not { } region)
            return false;

        // The region writer holds saveLock during a write and takes the queue locks inside it.
        if (!Monitor.TryEnter(region.saveLock)) return true;
        try
        {
            lock (region.chunkMemoryStreamsToSave)
                if (region.chunkMemoryStreamsToSave.Count != 0) return true;
            lock (region.chunksToSave)
                return region.chunksToSave.Count != 0;
        }
        finally
        {
            Monitor.Exit(region.saveLock);
        }
    }

    private static LocalPlayerUI? UsableMenu()
    {
        var game = GameManager.Instance;
        var connection = ConnectionManager.Instance;
        var ui = LocalPlayerUI.primaryUI;
        if (game == null || game.World != null || game.IsStartingGame ||
            connection == null || connection.CurrentMode != ProtocolManager.NetworkType.None ||
            PlatformApplicationManager.IsRestartRequired || PlatformApplicationManager.isRestarting ||
            ui == null || ui.xui == null)
            return null;

        var windows = ui.windowManager;
        string? modal = windows?.GetModalWindow()?.Id;
        return modal != null && windows!.IsWindowOpen(modal) && (modal == XUiC_MainMenu.ID || modal == XUiC_PlayGameMenu.ID ||
            modal == XUiC_NewGame.ID || modal == XUiC_ContinueGame.ID) ? ui : null;
    }

    private void OpenPage(GameLinkOpenRequest request)
    {
        if (UsableMenu() is not { } ui ||
            PlatformApplicationManager.LoadSaveGame.GetState() != EPlatformLoadSaveGameState.Done)
            return;

        if (!ui.windowManager.IsWindowOpen(XUiC_MainMenu.ID))
        {
            // Enter through Play Game so New Game refreshes its world list after export.
            ui.windowManager.Open(XUiC_MainMenu.ID, true);
            return;
        }

        UserDataStorageType? storage = SaveStorage(request);
        if (storage == null && (!request.Create || Directory.Exists(
            GameIO.GetSaveGameDir(request.World, request.Save, UserDataStorageType.DeviceLocal))))
        {
            Finish("failed", "save_not_found");
            return;
        }
        if (storage == null && !WorldExists(request.World))
        {
            Finish("failed", "world_not_found");
            return;
        }

        var buttons = ui.xui.FindWindowGroupByName(XUiC_MainMenu.ID)?.GetChildByType<XUiC_MainMenuButtons>();
        if (buttons != null && buttons.TryGoToPlayGameWindow(storage != null ? "continueGame" : "newGame"))
        {
            saveStorage = storage;
            step = Step.SelectTarget;
        }
    }

    private void SelectTarget(GameLinkOpenRequest request)
    {
        if (UsableMenu() is not { } ui) return;
        if (saveStorage is { } storage)
        {
            var page = ui.xui.GetChildByType<XUiC_ContinueGame>();
            if (ui.windowManager.GetModalWindow().Id != XUiC_ContinueGame.ID) return;
            var saves = page.GetChildByType<XUiC_SavegamesList>();
            saves.RebuildList();
            if (!saves.TrySelectEntry(request.World, request.Save, storage) ||
                saves.SelectedEntryData is not { } entry)
            {
                Finish("failed", "save_not_found");
                return;
            }
            selected = entry;
        }
        else
        {
            if (ui.windowManager.GetModalWindow().Id != XUiC_NewGame.ID) return;
            var page = ui.xui.GetChildByType<XUiC_NewGame>();
            page.selectWorld(request.World);
            if (!SelectedWorldMatches(page, request.World))
            {
                Finish("failed", "world_not_found");
                return;
            }
            ((XUiC_TextInput)page.GetChildById("txtGameName")).Text = request.Save;
        }
        step = Step.PressStart;
    }

    private void PressStart(GameLinkOpenRequest request)
    {
        if (UsableMenu() is not { } ui) return;
        XUiC_NewContinueBase page;
        if (selected is { } entry)
        {
            if (ui.windowManager.GetModalWindow().Id != XUiC_ContinueGame.ID) return;
            page = ui.xui.GetChildByType<XUiC_ContinueGame>();
            if (page.GetChildByType<XUiC_SavegamesList>().SelectedEntryData != entry)
            {
                Finish("failed", "save_not_found");
                return;
            }
            if (BlockReason(entry, page) is { } blocked)
            {
                blockedEntry = entry;
                Finish("blocked", blocked);
                return;
            }
            if (!Same(GamePrefs.GetString(EnumGamePrefs.GameWorld), entry.WorldName) ||
                !Same(GamePrefs.GetString(EnumGamePrefs.GameName), entry.SaveName) ||
                GamePrefs.GetInt(EnumGamePrefs.GameSaveStorageType) != (int)entry.StorageType)
            {
                Finish("failed", "save_not_found");
                return;
            }
        }
        else
        {
            if (ui.windowManager.GetModalWindow().Id != XUiC_NewGame.ID) return;
            var newGame = ui.xui.GetChildByType<XUiC_NewGame>();
            if (!SelectedWorldMatches(newGame, request.World) || !WorldExists(request.World) ||
                !Same(GamePrefs.GetString(EnumGamePrefs.GameName), request.Save) || SaveStorage(request) != null)
            {
                Finish("failed", "not_ready");
                return;
            }
            page = newGame;
        }

        if (!StartView(page).Enabled)
        {
            Finish("failed", "not_ready");
            return;
        }
        step = Step.WaitForSave;
        page.BtnStart_OnPressed(page, -1);
    }

    /// <summary>Reports the selected blocked save for an OPEN request or the game's launch automation.</summary>
    public object? GetBlockedSave()
    {
        var ui = LocalPlayerUI.primaryUI;
        if (ui == null || ui.windowManager == null || !ui.windowManager.IsWindowOpen(XUiC_ContinueGame.ID))
            return null;

        var page = ui.xui.GetChildByType<XUiC_ContinueGame>();
        var entry = page.GetChildByType<XUiC_SavegamesList>().SelectedEntryData;
        if (entry == null || StartView(page).Enabled || entry != blockedEntry && !IsNativeLaunchTarget(entry))
            return null;

        return new { world = entry.WorldName, save = entry.SaveName, reason = BlockReason(entry, page) ?? "start_disabled" };
    }

    private static bool IsNativeLaunchTarget(XUiC_SavegamesList.ListEntry entry) =>
        Same(entry.SaveName, PlatformApplicationManager.LoadSaveGame.GameName) &&
        Same(entry.WorldName, PlatformApplicationManager.LoadSaveGame.GameWorld) &&
        entry.StorageType == PlatformApplicationManager.LoadSaveGame.SaveStorageType &&
        PlatformApplicationManager.LoadSaveGame.GetState() == EPlatformLoadSaveGameState.Done;

    private static string? BlockReason(XUiC_SavegamesList.ListEntry entry, XUiC_NewContinueBase page)
    {
        if (!entry.WorldExists) return "world_not_found";
        if (!entry.Playable)
            return entry.VersionComparison switch
            {
                XUiC_SavegamesList.ListEntry.ESaveCompatibility.OldVersion => "older_version",
                XUiC_SavegamesList.ListEntry.ESaveCompatibility.FutureVersion => "newer_version",
                _ => "save_unplayable",
            };
        return StartView(page).Enabled ? null : "start_disabled";
    }

    private UserDataStorageType? SaveStorage(GameLinkOpenRequest request)
    {
        UserDataStorageType? storage = null;
        GameIO.GetPlayerSaves((foundStorage, save, world, _, _, _) =>
        {
            if (Same(world, request.World) && Same(save, request.Save) &&
                (rebuild == null || foundStorage == leftSave!.Value.Storage) &&
                (storage == null || foundStorage == UserDataStorageType.DeviceLocal))
                storage = foundStorage;
        }, includeArchived: true);
        return storage;
    }

    private static bool WorldExists(string world) =>
        PathAbstractions.WorldsSearchPaths.GetAvailablePathsList().Any(path => Same(path.Name, world));

    private static bool SelectedWorldMatches(XUiC_NewGame page, string world) =>
        Same(page.SelectedWorld?.RealName, world);

    private static XUiView StartView(XUiC_NewContinueBase page) => page.GetChildById("btnStart").ViewComponent;

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
