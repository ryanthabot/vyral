using System.IO;
using System.Text.Json;

namespace VyralBot.Integrations.Obs.WebSocket;

public sealed record ObsRemoteConfig(string? Password, int Port, bool ServerEnabled);

/// <summary>
/// Reads the obs-websocket plugin config (%APPDATA%\obs-studio\plugin_config\
/// obs-websocket\config.json). Own reader for now — Task 1's editor lands in a
/// sibling folder and Task 4 may unify them. The password is returned for the
/// auth hash only and must NEVER be logged or included in any message.
/// </summary>
public static class ObsWebSocketConfigReader
{
    public static ObsRemoteConfig? Read(string obsConfigDir)
    {
        try
        {
            var path = Path.Combine(obsConfigDir, "config.json");
            if (!File.Exists(path))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var password = root.TryGetProperty("server_password", out var passwordElement)
                && passwordElement.ValueKind == JsonValueKind.String
                    ? passwordElement.GetString()
                    : null;
            var port = root.TryGetProperty("server_port", out var portElement)
                && portElement.TryGetInt32(out var parsedPort)
                && parsedPort is > 0 and <= 65535
                    ? parsedPort
                    : 4455;
            var enabled = root.TryGetProperty("server_enabled", out var enabledElement)
                && enabledElement.ValueKind == JsonValueKind.True;

            return new ObsRemoteConfig(password, port, enabled);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
