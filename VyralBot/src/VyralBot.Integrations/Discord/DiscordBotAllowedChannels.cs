using System.Text.Json;

namespace VyralBot.Integrations.Discord;

/// <summary>
/// Per-bot ALLOWED CHANNELS command permissions. When the list is non-empty,
/// the bot ignores commands issued outside exactly these channels — except
/// moderators/broadcasters, who are never restricted (and the built-in !mod
/// family routes through <c>IDiscordCommandHandler</c>, a separate path that
/// this list never gates). An EMPTY list means "every channel" (legacy
/// behavior) so existing setups keep working until channels are picked.
/// </summary>
/// <remarks>
/// Persisted by <see cref="DiscordBotManager"/> as a plain JSON string array
/// under the <c>DiscordBot.{botId}.AllowedChannels</c> Configs key — the same
/// shape as <c>DiscordBot.{botId}.DisabledBuiltIns</c>, so
/// <c>CommandService</c> (Core, which cannot reference Integrations) parses it
/// with a plain <c>List&lt;string&gt;</c> deserialize.
/// </remarks>
public record DiscordBotAllowedChannels(
    IReadOnlyList<string> ChannelIds)
{
    /// <summary>A fresh, unrestricted instance (every channel allowed).</summary>
    public static DiscordBotAllowedChannels Default { get; } = new([]);

    /// <summary>Configs-row key the list is stored under for one bot.</summary>
    public static string ConfigKey(string botId) => $"DiscordBot.{botId}.AllowedChannels";

    /// <summary>True when no channels are picked — commands work everywhere.</summary>
    public bool IsUnrestricted => ChannelIds.Count == 0;

    /// <summary>
    /// Parses the persisted JSON; malformed or missing rows fall back to
    /// <see cref="Default"/> (a broken row must never lock a bot out of its
    /// own commands by throwing).
    /// </summary>
    public static DiscordBotAllowedChannels FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Default;
        }

        try
        {
            var ids = JsonSerializer.Deserialize<List<string>>(json);
            return ids == null
                ? Default
                : new DiscordBotAllowedChannels(ids
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .ToList());
        }
        catch (JsonException)
        {
            return Default;
        }
    }

    /// <summary>
    /// Serializes for <c>SetConfigValueAsync</c> as a plain string array
    /// (matching <c>DiscordBot.{botId}.DisabledBuiltIns</c>).
    /// </summary>
    public string ToJson() => JsonSerializer.Serialize(ChannelIds);

    /// <summary>
    /// True when <paramref name="channelId"/> is on the list (ordinal, trimmed
    /// compare). Always true while the list is empty.
    /// </summary>
    public bool Allows(string? channelId)
    {
        if (IsUnrestricted)
        {
            return true;
        }

        var id = channelId?.Trim();
        return !string.IsNullOrEmpty(id)
               && ChannelIds.Any(picked => string.Equals(picked.Trim(), id, StringComparison.Ordinal));
    }

    /// <summary>
    /// Pure list membership (no unrestricted shortcut) — the checkbox state
    /// for the detail page: an unrestricted bot shows NOTHING checked until
    /// the user actually picks channels.
    /// </summary>
    public bool IsPicked(string? channelId)
    {
        var id = channelId?.Trim();
        return !string.IsNullOrEmpty(id)
               && ChannelIds.Any(picked => string.Equals(picked.Trim(), id, StringComparison.Ordinal));
    }
}
