namespace MapEditor.Core.IO;

/// <summary>The per-user Windows shell association for Gnostic projects.</summary>
public static class FileAssociation
{
    /// <summary>The Windows program identifier for Gnostic projects.</summary>
    public const string ProgId = "Gnostic.7dmap";

    /// <summary>The key that holds the command to open a Gnostic project.</summary>
    public const string OpenCommandKey = ProgId + @"\shell\open\command";

    /// <summary>The key paths and default values to write under Software\Classes.</summary>
    public static IReadOnlyList<(string Key, string Value)> Entries(string exePath, string iconPath) =>
    [
        (ProgId, "Gnostic map project"),
        (ProgId + @"\DefaultIcon", $"\"{iconPath}\",0"),
        (OpenCommandKey, OpenCommand(exePath)),
        (ProjectStore.Extension, ProgId),
    ];

    /// <summary>Returns which entries still match the ProgId and command written by this installation.</summary>
    public static (bool ExtensionDefault, bool ProgId) Removals(string? extensionProgId, string? openCommand, string exePath)
    {
        bool ownsCommand = string.Equals(openCommand, OpenCommand(exePath), StringComparison.OrdinalIgnoreCase);
        return (ownsCommand && string.Equals(extensionProgId, ProgId, StringComparison.OrdinalIgnoreCase), ownsCommand);
    }

    private static string OpenCommand(string exePath) => $"\"{exePath}\" \"%1\"";
}
