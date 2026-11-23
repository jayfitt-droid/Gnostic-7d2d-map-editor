namespace MapEditor.Core.GameLink;

/// <summary>Copies picture files from the Game Link mod into a project.</summary>
public static class GameLinkPictureFile
{
    /// <summary>Copies a named Game Link picture, then tries to delete the game's copy.</summary>
    public static void CopyToProject(string gamePicturesFolder, string fileName, string destinationPath)
    {
        if (!GameLinkClient.PictureName.IsMatch(fileName))
            throw new ArgumentException("Invalid Game Link picture file name.", nameof(fileName));

        string sourcePath = Path.Combine(gamePicturesFolder, fileName);
        File.Copy(sourcePath, destinationPath);

        try
        {
            File.Delete(sourcePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // The project copy is already saved. Leave the game's copy if cleanup fails.
        }
    }
}
