using VyralBot.Core.Services.Moderation;
using VyralBot.Integrations.Discord;

namespace VyralBot.UI.Services;

/// <summary>
/// Maps a live <see cref="IDiscordService"/> onto the Core-side
/// <see cref="IModDiscordBot"/> mirror. VyralBot.Core cannot reference
/// VyralBot.Integrations (the reference direction is Integrations → Core), so
/// the App.xaml.cs composition wraps every Discord service the mod command
/// engine talks to in this adapter. Every member forwards 1:1 — no behavior.
/// </summary>
public sealed class ModDiscordBotAdapter : IModDiscordBot
{
    private readonly IDiscordService _bot;

    public ModDiscordBotAdapter(IDiscordService bot) => _bot = bot;

    /// <inheritdoc />
    public Task<bool> SendToChannelAsync(string channelNameOrId, string content, string? embedJson, string? guildName = null)
        => _bot.SendToChannelAsync(channelNameOrId, content, embedJson, guildName);

    /// <inheritdoc />
    public Task<bool> SendDmAsync(string username, string content, string? embedJson, string? guildName = null)
        => _bot.SendDmAsync(username, content, embedJson, guildName);

    /// <inheritdoc />
    public Task<bool> AddRoleToUserAsync(string username, string roleIdOrName, string? reason, string? guildName = null)
        => _bot.AddRoleToUserAsync(username, roleIdOrName, reason, guildName);

    /// <inheritdoc />
    public Task<List<string>> GetMemberRoleIdsAsync(string guildId, string userId)
        => _bot.GetMemberRoleIdsAsync(guildId, userId);
}
