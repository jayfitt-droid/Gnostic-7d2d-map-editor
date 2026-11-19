using System.IO;
using System.Runtime.InteropServices;
using MapEditor.Core.IO;
using Microsoft.Win32;
using Velopack.Locators;

namespace MapEditor.App;

/// <summary>Maintains the Windows project association during Velopack install, update and uninstall.</summary>
internal static class ProjectFileAssociation
{
    private const string ClassesKey = @"Software\Classes";
    private const string IconFileName = "7dmap.ico";
    private const uint ShcneAssocChanged = 0x08000000;

    /// <summary>Registers the project association for the installed editor.</summary>
    internal static void Register() => UpdateRegistry(remove: false);

    /// <summary>Removes this installation's project association.</summary>
    internal static void Unregister() => UpdateRegistry(remove: true);

    private static void UpdateRegistry(bool remove)
    {
        try
        {
            var locator = VelopackLocator.Current;
            if (locator.RootAppDir is not { } root || locator.IsPortable) return;

            // Velopack replaces the contents of current on update and keeps its path.
            string current = Path.Combine(root, "current");
            string exePath = Path.Combine(current, "Gnostic.exe");
            using var classes = remove
                ? Registry.CurrentUser.OpenSubKey(ClassesKey, writable: true)
                : Registry.CurrentUser.CreateSubKey(ClassesKey);
            if (classes is null) return;

            try
            {
                if (remove) Remove(classes, exePath);
                else Write(classes, exePath, Path.Combine(current, IconFileName));
            }
            finally
            {
                SHChangeNotify(ShcneAssocChanged, 0, 0, 0);
            }
        }
        catch
        {
            // File association changes are best effort so the installer can continue.
        }
    }

    private static void Write(RegistryKey classes, string exePath, string iconPath)
    {
        foreach (var (path, value) in FileAssociation.Entries(exePath, iconPath))
        {
            using var key = classes.CreateSubKey(path);
            key.SetValue("", value, RegistryValueKind.String);
        }
    }

    private static void Remove(RegistryKey classes, string exePath)
    {
        using var extension = classes.OpenSubKey(ProjectStore.Extension, writable: true);
        using var command = classes.OpenSubKey(FileAssociation.OpenCommandKey);
        var (extensionDefault, progId) = FileAssociation.Removals(
            extension?.GetValue(null) as string, command?.GetValue(null) as string, exePath);

        if (extensionDefault) extension?.DeleteValue("", throwOnMissingValue: false);
        if (progId) classes.DeleteSubKeyTree(FileAssociation.ProgId, throwOnMissingSubKey: false);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, nint item1, nint item2);
}
