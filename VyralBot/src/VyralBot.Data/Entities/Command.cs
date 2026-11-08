using System.Text.Json;

namespace VyralBot.Data.Entities;

public class Command
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Response { get; set; }
    public required string Permission { get; set; } = "Everyone";
    public int GlobalCooldown { get; set; } = 0;
    public int UserCooldown { get; set; } = 0;
    public bool IsEnabled { get; set; } = true;
    public bool IsBuiltIn { get; set; } = false;
    public bool EnabledOnKick { get; set; } = true;
    public bool EnabledOnTwitch { get; set; } = true;

    /// <summary>
    /// Discord bot scope: the DiscordBots row id this command is pinned to.
    /// Null means the command runs for EVERY Discord bot (and, as before, on
    /// any platform enabled above).
    /// </summary>
    public string? BotId { get; set; }

    /// <summary>
    /// JSON array of alternate replies (strings), one per line in the builder.
    /// When present and non-empty, execution picks one entry AT RANDOM instead
    /// of <see cref="Response"/> (custom commands only).
    /// </summary>
    public string? ResponsesJson { get; set; }

    /// <summary>
    /// JSON object mapping platform name → reply text —
    /// {"Twitch":"Follow on Twitch!","Kick":"Follow on Kick!"}. A non-blank
    /// entry for the ORIGINATING platform wins over both the random
    /// <see cref="ResponsesJson"/> replies and the single <see cref="Response"/>
    /// (see <see cref="ResolvePlatformResponse"/>); null/empty — or a
    /// missing/blank entry for the platform — keeps the legacy single-response
    /// behavior, so the column is fully backward compatible.
    /// </summary>
    public string? PlatformResponsesJson { get; set; }

    /// <summary>
    /// JSON array of ordered action steps ({type, ...config} objects) — the
    /// BotGhost-style ACTION SEQUENCE. When present (custom commands only),
    /// <c>CommandService.ExecuteAsync</c> does NOT run the steps (they need the
    /// live platform senders); it returns the
    /// <see cref="VyralBot.Core.Services.Commands.ICommandService.ActionsMarkerPrefix"/>
    /// marker so the UI layer's CommandActionRunner executes the sequence.
    /// </summary>
    public string? ActionsJson { get; set; }

    public int UsageCount { get; set; } = 0;
    public DateTime? LastUsedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // ------------------------------------------------------------------
    // Per-platform responses (PlatformResponsesJson) — one command, a
    // different reply text per platform. Shared by every pick site (Core
    // CommandService chat pipeline, CommandTimerService, flow editor).
    // ------------------------------------------------------------------

    /// <summary>
    /// Deserializes a <see cref="PlatformResponsesJson"/> value into a
    /// platform → reply map. Null, blank, malformed JSON or a non-object
    /// root yield an empty map (callers fall back to the legacy Response
    /// pick). Keys and platform lookups compare case-insensitively, blank
    /// entries are dropped, values are trimmed.
    /// </summary>
    public static Dictionary<string, string> ParsePlatformResponses(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (raw is null)
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            var map = new Dictionary<string, string>(raw.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var (platform, reply) in raw)
            {
                var key = platform?.Trim();
                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(reply))
                {
                    map[key] = reply.Trim();
                }
            }

            return map;
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// The per-platform reply override for <paramref name="platform"/>, or
    /// null when none applies (no JSON, no entry for the platform, or a blank
    /// entry) — the caller then keeps the legacy pick: random
    /// <see cref="ResponsesJson"/> replies, else the single <see cref="Response"/>.
    /// </summary>
    public string? ResolvePlatformResponse(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform))
        {
            return null;
        }

        var map = ParsePlatformResponses(PlatformResponsesJson);
        return map.TryGetValue(platform.Trim(), out var reply) ? reply : null;
    }

    /// <summary>
    /// Serializes per-platform reply overrides, dropping blank keys/values;
    /// returns null when nothing survives so untouched rows keep a NULL
    /// column. Inverse of <see cref="ParsePlatformResponses"/>.
    /// </summary>
    public static string? BuildPlatformResponsesJson(IDictionary<string, string?>? perPlatform)
    {
        if (perPlatform is null)
        {
            return null;
        }

        var map = new Dictionary<string, string>(perPlatform.Count, StringComparer.Ordinal);
        foreach (var (platform, reply) in perPlatform)
        {
            var key = platform?.Trim();
            var value = reply?.Trim();
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                map[key] = value!;
            }
        }

        return map.Count > 0 ? JsonSerializer.Serialize(map) : null;
    }
}
