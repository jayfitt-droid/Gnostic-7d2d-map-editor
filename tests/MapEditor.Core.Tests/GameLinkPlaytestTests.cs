using MapEditor.Core.GameLink;

namespace MapEditor.Core.Tests;

public class GameLinkPlaytestTests
{
    private const string Id = "0123456789abcdef";
    private const string World = "Test World";
    private const string Save = World + " Playtest";
    private static readonly GameLinkStatus Ready = new(World, true, GameLinkState.Ready, "1.1.0", new(0, 0, 0))
    {
        Save = Save,
    };

    [Fact]
    public void TheGameCountsAsClosedAfterThreeFailedRepliesInARow()
    {
        var missed = new GameLinkMissedReplies();

        Assert.False(missed.Record(false));
        Assert.False(missed.Record(false));
        Assert.True(missed.Record(false));
    }

    [Fact]
    public void AGoodReplyRestartsTheCountOfFailedReplies()
    {
        var missed = new GameLinkMissedReplies();

        Assert.False(missed.Record(false));
        Assert.False(missed.Record(false));
        Assert.False(missed.Record(true));
        Assert.False(missed.Record(false));
        Assert.False(missed.Record(false));
        Assert.True(missed.Record(false));
    }

    [Theory]
    [InlineData(@"GeneratedWorlds\Test", true)]
    [InlineData(@"generatedworlds\test\", true)]
    [InlineData(@"Game\Data\Worlds\Test", true)]
    [InlineData(@"Game\Mods\Map Mod\Worlds\Test", true)]
    [InlineData(@"User\Mods\Map Mod\worlds\Test\", true)]
    [InlineData(@"GeneratedWorlds", false)]
    [InlineData(@"GeneratedWorlds\Pack\Test", false)]
    [InlineData(@"Game\Mods\Worlds\Test", false)]
    [InlineData(@"Game\Mods\Map Mod\Worlds\Pack\Test", false)]
    [InlineData(@"Elsewhere\Test", false)]
    public void ExportMustBeADirectChildOfAGameWorldFolder(string relative, bool expected)
    {
        string root = AppContext.BaseDirectory;
        string Folder(string path) => Path.Combine(root, path);

        Assert.Equal(expected, GameLinkPlaytest.IsWorldFolder(Folder(relative), Folder("GeneratedWorlds"),
            Folder(@"Game\Data\Worlds\"), Folder(@"Game\Mods"), Folder(@"User\Mods\")));
    }

    [Theory]
    [InlineData("Test World", "Test World Playtest")]
    [InlineData("\u0141u\u017Cyce", "\u0141u\u017Cyce Playtest")]
    [InlineData("Test<>:\"/\\|?*World. ", "TestWorld Playtest")]
    [InlineData(" .?/ ", "Playtest")]
    public void PlaytestNameUsesTheWorldWithInvalidCharactersRemoved(string world, string expected) =>
        Assert.Equal(expected, GameLinkPlaytest.SaveName(world));

    [Fact]
    public void LongPlaytestNameFitsAFolderComponent()
    {
        string name = GameLinkPlaytest.SaveName(new string('a', 245) + "\U0001F333 Reserve");
        Assert.Equal(new string('a', 245) + " Playtest", name);
        Assert.True(GameLinkOpenRequest.IsFolderName(name));
    }

    [Theory]
    [InlineData("West County", true)]
    [InlineData("\u0141u\u017Cyce", true)]
    [InlineData("Caf\u00E9", false)]
    [InlineData("West County!", false)]
    public void NewGameMustBeAbleToEnterTheExactSaveName(string world, bool allowed) =>
        Assert.Equal(allowed, GameLinkPlaytest.CanEnterSaveName(GameLinkPlaytest.SaveName(world)));

    [Fact]
    public void NewGameLimitsThePlaytestNameTo50Characters()
    {
        Assert.True(GameLinkPlaytest.CanEnterSaveName(GameLinkPlaytest.SaveName(new string('a', 41))));
        Assert.False(GameLinkPlaytest.CanEnterSaveName(GameLinkPlaytest.SaveName(new string('a', 42))));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OpenRequestCanBeLoggedBeforeItIsUsedOnce(bool create)
    {
        string folder = AppContext.BaseDirectory;
        string world = new string('\u754C', 140) + " Test World";
        string save = GameLinkPlaytest.SaveName(world);
        var now = DateTimeOffset.UtcNow;
        string id = new GameLinkOpenRequest(world, save, create).Write(folder, now);
        string path = GameLinkOpenRequest.RequestPath(folder, id);
        try
        {
            var target = GameLinkOpenRequest.Peek(folder, id);
            Assert.NotNull(target);
            Assert.Equal((world, save, create), (target.World, target.Save, target.Create));
            Assert.True(File.Exists(path));
            Assert.Null(GameLinkOpenRequest.Take(folder, id, now.AddSeconds(119), out var request));
            Assert.NotNull(request);
            Assert.Equal((world, save, create), (request.World, request.Save, request.Create));
            Assert.False(File.Exists(path));
            Assert.Equal("bad_request", GameLinkOpenRequest.Take(folder, id, now, out _));
            Assert.Null(GameLinkOpenRequest.Peek(folder, id));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExpiredOpenRequestIsRejectedAndRemoved()
    {
        string folder = AppContext.BaseDirectory;
        var now = DateTimeOffset.UtcNow;
        string id = new GameLinkOpenRequest(World, Save, true).Write(folder, now.AddMinutes(-2));
        string path = GameLinkOpenRequest.RequestPath(folder, id);
        try
        {
            var target = GameLinkOpenRequest.Peek(folder, id);
            Assert.NotNull(target);
            Assert.Equal((World, Save), (target.World, target.Save));
            Assert.Equal("expired", GameLinkOpenRequest.Take(folder, id, now, out var request));
            Assert.Null(request);
            Assert.False(File.Exists(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void WaitForMenuIgnoresTheOldReadySaveEvenWhenItMatches()
    {
        var leaving = Ready with { Operation = new(Id, "leave", "leaving", "") };
        Assert.Equal(GameLinkWaitState.Waiting, GameLinkPlaytest.WaitForMenu(leaving, Id, false).State);
        Assert.Equal(GameLinkWaitState.Waiting, GameLinkPlaytest.WaitForSave(leaving, World, Save, null, false).State);
        var menu = leaving with { State = GameLinkState.Menu, Operation = new(Id, "leave", "at_menu", "") };
        Assert.Equal(GameLinkWaitState.AtMenu, GameLinkPlaytest.WaitForMenu(menu, Id, false).State);
        Assert.Equal(GameLinkWaitState.Waiting, GameLinkPlaytest.WaitForMenu(menu, "another operation", false).State);
    }

    [Fact]
    public void WaitForSaveRequiresTheCorrectWorldSaveAndOperation()
    {
        var done = Ready with { Operation = new(Id, "open", "done", "") };
        Assert.Equal(GameLinkWaitState.Teleport, Wait(done).State);
        Assert.Equal(GameLinkWaitState.Teleport, GameLinkPlaytest.WaitForSave(Ready, World, Save, null, false).State);
        Assert.Equal(GameLinkWaitState.Waiting, Wait(done with { World = "Other World" }).State);
        Assert.Equal(GameLinkWaitState.Waiting, Wait(done with { Save = "Other Save" }).State);
        Assert.Equal(GameLinkWaitState.Waiting, Wait(done with { Hosting = false }).State);
        Assert.Equal(GameLinkWaitState.Waiting, Wait(done with { Operation = new(Id, "open", "opening", "") }).State);
        Assert.Equal(GameLinkWaitState.Waiting, Wait(Ready).State);
        Assert.Equal(GameLinkWaitState.TimedOut, Wait(null, expired: true).State);
    }

    [Fact]
    public void WaitForSaveReportsFailureOrTheExactBlockedSave()
    {
        var menu = new GameLinkStatus(World, false, GameLinkState.Menu, "1.1.0", null);
        var failed = menu with { Operation = new(Id, "open", "failed", "world_not_found") };
        Assert.Equal(new(GameLinkWaitState.Failed, "world_not_found"), Wait(failed));
        var blocked = menu with
        {
            Operation = new(Id, "open", "blocked", "older_version"),
            Blocked = new(World, Save, "older_version"),
        };
        Assert.Equal(new(GameLinkWaitState.Blocked, "older_version"), Wait(blocked));
        Assert.Equal(new(GameLinkWaitState.Blocked, "older_version"),
            GameLinkPlaytest.WaitForSave(blocked with { Operation = null }, World, Save, null, false));
        Assert.Equal(new(GameLinkWaitState.Blocked, "older_version"), Wait(blocked with { Blocked = null }));
        var otherSave = blocked with { Operation = null, Blocked = new(World, "Other Save", "older_version") };
        Assert.Equal(GameLinkWaitState.Waiting,
            GameLinkPlaytest.WaitForSave(otherSave, World, Save, null, false).State);
    }

    private static GameLinkWaitResult Wait(GameLinkStatus? status, bool expired = false) =>
        GameLinkPlaytest.WaitForSave(status, World, Save, Id, expired);
}
