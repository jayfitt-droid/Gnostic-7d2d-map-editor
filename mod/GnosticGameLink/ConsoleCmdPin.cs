using MapEditor.Core.GameLink;
using UnityEngine;

namespace GnosticGameLink;

/// <summary>Records a playtest pin from the host's local game console.</summary>
public sealed class ConsoleCmdPin : ConsoleCmdAbstract
{
    public override string[] getCommands() => ["pin"];
    public override string getDescription() => "Save a playtest pin for Gnostic Game Link.";
    public override string getHelp() => "Usage: pin [note]";

    public override void Execute(List<string> args, CommandSenderInfo sender)
    {
        var console = SdtdConsole.Instance;
        var connection = ConnectionManager.Instance;
        if (!sender.IsLocalGame || connection == null || !connection.IsServer || connection.IsClient ||
            GameManager.Instance?.World?.GetPrimaryPlayer() is not { } player || player.IsDead() || !player.IsSpawned())
        {
            console.Output("Enter a world in single-player or as host to add a pin.");
            return;
        }

        var position = player.GetPosition();
        var pin = new GameLinkPin(Guid.NewGuid(), position.x, position.y, position.z,
            Mathf.Repeat(player.rotation.y, 360f), GamePrefs.GetString(EnumGamePrefs.GameWorld) ?? string.Empty,
            GamePrefs.GetString(EnumGamePrefs.GameName) ?? string.Empty, DateTimeOffset.UtcNow,
            GameLinkPinFile.CleanNote(string.Join(" ", args)));

        console.Output(GameLinkPinQueue.TryAppend(pin) ? "Playtest pin saved." : "Could not save the playtest pin.");
    }
}
