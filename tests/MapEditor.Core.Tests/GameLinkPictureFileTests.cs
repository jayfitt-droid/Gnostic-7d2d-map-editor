using MapEditor.Core.GameLink;

namespace MapEditor.Core.Tests;

public sealed class GameLinkPictureFileTests : IDisposable
{
    private const string PictureName = "gnostic-20261123-123456-789.png";
    private readonly string _folder = Directory.CreateTempSubdirectory("gnostic-link-picture-").FullName;
    private string GamePicturesFolder => Directory.CreateDirectory(Path.Combine(_folder, "game")).FullName;
    private string DestinationPath => Path.Combine(_folder, "project.png");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void CopySavesThePictureAndDeletesTheSource()
    {
        string source = Path.Combine(GamePicturesFolder, PictureName);
        File.WriteAllText(source, "picture");

        GameLinkPictureFile.CopyToProject(GamePicturesFolder, PictureName, DestinationPath);

        Assert.Equal("picture", File.ReadAllText(DestinationPath));
        Assert.False(File.Exists(source));
    }

    [Theory]
    [InlineData("x.png")]
    [InlineData("../x.png")]
    [InlineData("sub/" + PictureName)]
    public void InvalidNameLeavesTheSourceAndDoesNotCopyIt(string fileName)
    {
        if (OperatingSystem.IsWindows()) fileName = fileName.Replace('/', '\\');
        string source = Path.GetFullPath(Path.Combine(GamePicturesFolder, fileName));
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "picture");

        Assert.Throws<ArgumentException>(() => GameLinkPictureFile.CopyToProject(GamePicturesFolder, fileName, DestinationPath));

        Assert.Equal("picture", File.ReadAllText(source));
        Assert.False(File.Exists(DestinationPath));
    }

    [Fact]
    public void FailedCopyLeavesTheSource()
    {
        string source = Path.Combine(GamePicturesFolder, PictureName);
        File.WriteAllText(source, "picture");
        File.WriteAllText(DestinationPath, "existing picture");

        Assert.Throws<IOException>(() => GameLinkPictureFile.CopyToProject(GamePicturesFolder, PictureName, DestinationPath));

        Assert.Equal("picture", File.ReadAllText(source));
        Assert.Equal("existing picture", File.ReadAllText(DestinationPath));
    }

    [Fact]
    public void FailedDeleteLeavesBothCopies()
    {
        if (!OperatingSystem.IsWindows()) return;

        string source = Path.Combine(GamePicturesFolder, PictureName);
        File.WriteAllText(source, "picture");
        using var sourceLock = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.Read);

        GameLinkPictureFile.CopyToProject(GamePicturesFolder, PictureName, DestinationPath);

        Assert.Equal("picture", File.ReadAllText(DestinationPath));
        Assert.True(File.Exists(source));
    }
}
