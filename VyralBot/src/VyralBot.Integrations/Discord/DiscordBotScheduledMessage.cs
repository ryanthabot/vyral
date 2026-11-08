using System.Globalization;
using System.Text.Json;

namespace VyralBot.Integrations.Discord;

/// <summary>
/// One recurring announcement a bot posts on a fixed minute interval (the
/// streamer.bot "scheduled message" staple). The bot detail page edits a
/// bounded list of these per bot; <see cref="DiscordBotManager"/> evaluates
/// the due ones every 30 seconds with a single manager-wide timer and sends
/// them through the bot's own service while it is connected.
/// </summary>
/// <param name="Id">Stable row id (GUID "N" string) so edits/removes survive round-trips.</param>
/// <param name="Enabled">Master switch; disabled entries are kept but never fire.</param>
/// <param name="ChannelId">Snowflake id of the channel receiving the message; null = never fires.</param>
/// <param name="IntervalMinutes">Recurrence in minutes, clamped to <see cref="MinIntervalMinutes"/>..<see cref="MaxIntervalMinutes"/>.</param>
/// <param name="Message">Message template; <c>{time}</c>/<c>{date}</c> are applied at send time.</param>
/// <param name="NextRunUtc">
/// UTC instant the entry is next due. Null on a freshly saved entry — the
/// timer initializes it to save-time + interval so an edit always restarts
/// the cycle (and a restart never catch-up-spams missed ticks).
/// </param>
public record DiscordBotScheduledMessage(
    string Id,
    bool Enabled = false,
    string? ChannelId = null,
    // Qualified: the primary-constructor parameter list cannot see the
    // record body's members by simple name (CS0103).
    int IntervalMinutes = DiscordBotScheduledMessage.MinIntervalMinutes,
    string? Message = null,
    DateTime? NextRunUtc = null)
{
    /// <summary>Shortest allowed recurrence (minutes) — protects against runaway loops.</summary>
    public const int MinIntervalMinutes = 5;

    /// <summary>Longest allowed recurrence (minutes) — one day.</summary>
    public const int MaxIntervalMinutes = 1440;

    /// <summary>Per-bot schedule-list bound; the card's ADD button stops here.</summary>
    public const int MaxPerBot = 25;

    /// <summary>Configs-row key the list is stored under for one bot.</summary>
    public static string ConfigKey(string botId) => $"DiscordBot.{botId}.ScheduledMessages";

    /// <summary>Interval clamped into the allowed range (a stored 0/7-day typo can never spin the timer).</summary>
    public int EffectiveIntervalMinutes =>
        Math.Clamp(IntervalMinutes < MinIntervalMinutes ? MinIntervalMinutes : IntervalMinutes,
            MinIntervalMinutes, MaxIntervalMinutes);

    /// <summary>
    /// Parses the persisted JSON list; malformed or missing rows yield an
    /// empty list (a broken row must never disable-or-fire entries by throwing).
    /// </summary>
    public static List<DiscordBotScheduledMessage> ListFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<DiscordBotScheduledMessage>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Serializes the list for <c>SetConfigValueAsync</c> (PascalCase, matching the sibling DiscordBot.* keys).</summary>
    public static string ListToJson(IReadOnlyList<DiscordBotScheduledMessage> messages) =>
        JsonSerializer.Serialize(messages);

    /// <summary>
    /// Applies the send-time variables (case-insensitive):
    /// <c>{time}</c> → short local time, <c>{date}</c> → short local date.
    /// </summary>
    public static string FormatMessage(string? template)
    {
        var text = string.IsNullOrWhiteSpace(template) ? string.Empty : template!;
        return text
            .Replace("{time}", DateTime.Now.ToString("HH:mm", CultureInfo.CurrentCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{date}", DateTime.Now.ToString("d", CultureInfo.CurrentCulture), StringComparison.OrdinalIgnoreCase);
    }
}
