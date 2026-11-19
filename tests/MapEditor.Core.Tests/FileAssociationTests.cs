using MapEditor.Core.IO;

namespace MapEditor.Core.Tests;

public sealed class FileAssociationTests
{
    private const string ExePath = @"C:\Program Files\Gnostic\current\Gnostic.exe";
    private const string IconPath = @"C:\Program Files\Gnostic\current\7dmap.ico";
    private const string OpenCommand = "\"C:\\Program Files\\Gnostic\\current\\Gnostic.exe\" \"%1\"";

    [Fact]
    public void Project_extension_maps_to_the_Gnostic_program_identifier()
    {
        var entries = FileAssociation.Entries(ExePath, IconPath).ToDictionary(entry => entry.Key, entry => entry.Value);

        Assert.Equal("Gnostic.7dmap", entries[".7dmap"]);
        Assert.Equal("Gnostic map project", entries["Gnostic.7dmap"]);
    }

    [Fact]
    public void Open_command_quotes_the_exe_path_and_project_argument()
    {
        var entries = FileAssociation.Entries(ExePath, IconPath).ToDictionary(entry => entry.Key, entry => entry.Value);

        Assert.Equal(OpenCommand, entries[@"Gnostic.7dmap\shell\open\command"]);
        Assert.Equal("\"C:\\Program Files\\Gnostic\\current\\7dmap.ico\",0", entries[@"Gnostic.7dmap\DefaultIcon"]);
    }

    [Theory]
    [InlineData("Gnostic.7dmap", OpenCommand, true, true)]
    [InlineData("gnostic.7DMAP", "\"c:\\program files\\gnostic\\current\\GNOSTIC.EXE\" \"%1\"", true, true)]
    [InlineData("OtherApp.7dmap", OpenCommand, false, true)]
    [InlineData(null, OpenCommand, false, true)]
    [InlineData("Gnostic.7dmap", "\"C:\\Other Editor\\Gnostic.exe\" \"%1\"", false, false)]
    [InlineData("Gnostic.7dmap", "\"C:\\Program Files\\Gnostic\\current\\Gnostic.exe.old\" \"%1\"", false, false)]
    [InlineData("Gnostic.7dmap", null, false, false)]
    [InlineData(null, null, false, false)]
    public void Uninstall_removes_only_entries_that_point_at_this_installation(
        string? extensionProgId, string? openCommand, bool removeExtensionDefault, bool removeProgId)
    {
        Assert.Equal((removeExtensionDefault, removeProgId), FileAssociation.Removals(extensionProgId, openCommand, ExePath));
    }
}
