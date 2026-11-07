using System.Text.Json;

namespace VyralBot.Integrations.Discord;

/// <summary>
/// Per-bot "welcome + auto-role on member join" settings. When enabled, the
/// bot's detail page's Welcome card posts <paramref name="Message"/> (with
/// variables applied) to <paramref name="ChannelId"/> and grants
/// <paramref name="AutoRoleId"/> to every member joining
/// <paramref name="GuildId"/> — see <see cref="DiscordBotManager"/>'s
/// <c>DiscordMemberJoinedEvent</c> handler.
/// </summary>
/// <remarks>
/// Persisted by <see cref="DiscordBotManager"/> as JSON under the
/// <c>DiscordBot.{botId}.WelcomeSettings</c> Configs key — the same per-bot
/// store as <c>DiscordBot.{botId}.Integrations</c> / <c>.DisabledBuiltIns</c>
/// (channel, role and guild are snowflake ids, all guild-scoped, which is why
/// the settings only act on joins of <paramref name="GuildId"/>).
/// </remarks>
/// <param name="Enabled">Master switch for the whole welcome/auto-role feature.</param>
/// <param name="GuildId">Snowflake id of the guild the channel/role belong to; joins of other guilds are ignored.</param>
/// <param name="ChannelId">Snowflake id of the channel receiving the welcome message; null = no message.</param>
/// <param name="Message">Message template; <see cref="DefaultMessage"/> variables supported. Null/empty = no message.</param>
/// <param name="AutoRoleId">Snowflake id of the role granted on join; null = no role grant.</param>
public record DiscordBotWelcomeSettings(
    bool Enabled = false,
    string? GuildId = null,
    string? ChannelId = null,
    string? Message = null,
    string? AutoRoleId = null)
{
    /// <summary>Suggested template used to seed the editor (also the fallback when the saved message is blank).</summary>
    public const string DefaultMessage = "Welcome to {server}, {user}! 🎉";

    /// <summary>A fresh, feature-off settings instance.</summary>
    public static DiscordBotWelcomeSettings Default { get; } = new();

    /// <summary>Configs-row key the settings are stored under for one bot.</summary>
    public static string ConfigKey(string botId) => $"DiscordBot.{botId}.WelcomeSettings";

    /// <summary>
    /// Parses the persisted JSON; malformed or missing rows fall back to
    /// <see cref="Default"/> (a broken row must never disable-or-enable the
    /// feature by throwing).
    /// </summary>
    public static DiscordBotWelcomeSettings FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Default;
        }

        try
        {
            return JsonSerializer.Deserialize<DiscordBotWelcomeSettings>(json) ?? Default;
        }
        catch (JsonException)
        {
            return Default;
        }
    }

    /// <summary>Serializes for <c>SetConfigValueAsync</c> (PascalCase, matching the sibling DiscordBot.* keys).</summary>
    public string ToJson() => JsonSerializer.Serialize(this);

    /// <summary>
    /// Applies the template variables (case-insensitive):
    /// <c>{user}</c> → mention of the joining member (plain name fallback),
    /// <c>{username}</c> → plain username, <c>{server}</c> → guild name.
    /// </summary>
    public static string FormatMessage(string? template, string username, string? displayName, string? userId, string? guildName)
    {
        var text = string.IsNullOrWhiteSpace(template) ? DefaultMessage : template!;
        var name = string.IsNullOrWhiteSpace(displayName) ? username : displayName!;

        var mention = string.IsNullOrWhiteSpace(userId)
            ? name
            : $"<@{userId}>";

        return text
            .Replace("{user}", mention, StringComparison.OrdinalIgnoreCase)
            .Replace("{username}", username, StringComparison.OrdinalIgnoreCase)
            .Replace("{server}", guildName ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
