using System.Text.Json;
using System.Text.Json.Nodes;

namespace VyralBot.Integrations.Obs.Hosting;

/// <summary>
/// Read view of obs-websocket's config.json. <see cref="ServerPassword"/> is
/// carried so the websocket client can authenticate, but it must never be
/// logged or surfaced in strings that get logged.
/// </summary>
public sealed record ObsWebSocketConfig(string? ServerPassword, int Port, bool ServerEnabled, bool AuthRequired);

/// <summary>
/// Reads and minimally edits the obs-websocket plugin config
/// (<c>plugin_config\obs-websocket\config.json</c> under the OBS config dir,
/// i.e. %APPDATA%\obs-studio). EnableServer flips one boolean via a
/// System.Text.Json round-trip, so every other field — including the password
/// and keys from future plugin versions — is preserved verbatim. All methods
/// return null/false on missing or unparsable files instead of throwing.
/// </summary>
public static class ObsWebSocketConfigEditor
{
    private const int DefaultPort = 4455;

    private static readonly string configRelativePath =
        Path.Combine("plugin_config", "obs-websocket", "config.json");

    public static ObsWebSocketConfig? Read(string obsConfigDir)
    {
        JsonObject? root = ParseRoot(obsConfigDir);
        if (root is null)
        {
            return null;
        }

        return new ObsWebSocketConfig(
            ServerPassword: StringOrNull(root, "server_password"),
            Port: IntOr(root, "server_port", DefaultPort),
            ServerEnabled: BoolOr(root, "server_enabled", fallback: false),
            AuthRequired: BoolOr(root, "auth_required", fallback: true));
    }

    public static bool EnableServer(string obsConfigDir)
    {
        JsonObject? root = ParseRoot(obsConfigDir);
        if (root is null)
        {
            return false;
        }

        try
        {
            root["server_enabled"] = true;
            File.WriteAllText(
                ConfigPath(obsConfigDir),
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch (Exception)
        {
            // Unwritable/locked file — the UI surfaces the false, never a crash.
            return false;
        }
    }

    private static JsonObject? ParseRoot(string obsConfigDir)
    {
        try
        {
            string path = ConfigPath(obsConfigDir);
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        }
        catch (Exception)
        {
            // Missing, unreadable, or not a JSON object → "not configured".
            return null;
        }
    }

    private static string ConfigPath(string obsConfigDir) => Path.Combine(obsConfigDir, configRelativePath);

    // Typed getters that tolerate a field being absent or an unexpected JSON
    // type (obs-websocket owns the file's shape; we only touch one boolean).
    private static string? StringOrNull(JsonObject root, string key) =>
        root[key] is JsonValue value && value.TryGetValue(out string? s) ? s : null;

    private static bool BoolOr(JsonObject root, string key, bool fallback) =>
        root[key] is JsonValue value && value.TryGetValue(out bool b) ? b : fallback;

    private static int IntOr(JsonObject root, string key, int fallback) =>
        root[key] is JsonValue value && value.TryGetValue(out int i) ? i : fallback;
}
