using System.Globalization;
using MapEditor.Core.GameLink;
using Newtonsoft.Json;

namespace GnosticGameLink;

/// <summary>Formats protocol replies without exposing game objects to the listener.</summary>
internal static class GameLinkReply
{
    private static readonly string Version = typeof(GameLinkReply).Assembly.GetName().Version!.ToString(3);

    public const string Teleported = "{\"ok\":true}";
    public const string WaypointSet = "{\"ok\":true,\"message\":\"Temporary waypoint added. The game does not save it.\"}";

    public static string Error(string error, string message) =>
        ToJson(new { ok = false, error, message });

    public static string PictureSaved(string file) => ToJson(new { ok = true, file });

    public static string Accepted(string id) => ToJson(new { ok = true, id });

    public static string Status(string state, string world, bool hosting, string save, object? op,
        object? blocked, object? player = null) =>
        ToJson(new
        {
            ok = true,
            protocol = GameLinkProtocol.Version,
            mod = Version,
            state,
            world,
            hosting,
            save,
            op,
            blocked,
            player,
        });

    private static string ToJson(object reply)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        // Other game mods can change JsonConvert.DefaultSettings.
        JsonSerializer.Create(new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }).Serialize(writer, reply);
        return writer.ToString();
    }
}
