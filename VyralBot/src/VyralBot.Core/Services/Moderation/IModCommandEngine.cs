namespace VyralBot.Core.Services.Moderation;

/// <summary>
/// Listens for "!mod &lt;action&gt; [args...]" commands posted in the configured
/// Discord mod channel, enforces the <see cref="ModTiers"/> ladder and executes
/// the action (currency, giveaways, polls, moderation delegation, role
/// assignment), persisting every executed action as a <c>ModAction</c> audit row
/// and replying into the mod channel.
///
/// DI wiring (App.xaml.cs composition, all settable properties):
///   - <see cref="ModCommandEngine.ModChannelIdProvider"/>: returns the
///     configured mod-command channel snowflake id (null = engine inert).
///   - <see cref="ModCommandEngine.BotResolver"/>: maps an event's BotId to that
///     bot's Discord service wrapped in <see cref="IModDiscordBot"/>.
///   - <see cref="ModCommandEngine.DiscordChatSubscriber"/>: subscribes the
///     engine to DiscordChatMessageEvent on the IEventBus (the event type lives
///     in VyralBot.Integrations, which Core cannot reference).
///   - <see cref="ModCommandEngine.ModerationInvoker"/>: forwards raw moderation
///     commands ("!kick @user") to the underlying IDiscordCommandHandler.
/// </summary>
public interface IModCommandEngine
{
    /// <summary>
    /// Subscribes the engine to Discord chat messages through
    /// <see cref="ModCommandEngine.DiscordChatSubscriber"/> and stores the
    /// subscription Guid. Idempotent; a no-op (logged) when no subscriber is
    /// wired.
    /// </summary>
    Task StartAsync();

    /// <summary>Unsubscribes the engine (no-op when not subscribed).</summary>
    Task StopAsync();
}

/// <summary>
/// Core-side mirror of the Discord chat event fields the engine needs
/// (VyralBot.Integrations' DiscordChatMessageEvent is projected onto this
/// record by the DI composition — Core cannot reference Integrations).
/// </summary>
/// <param name="ChannelId">Snowflake id of the channel the message was posted in (null when unknown).</param>
/// <param name="SenderUserId">Discord snowflake id of the sender (mod tier lookup key).</param>
/// <param name="Username">Display name of the sender (audit log).</param>
/// <param name="Message">Raw message text.</param>
/// <param name="BotId">Id of the bot instance that received the message (bot routing).</param>
public sealed record ModChannelMessage(
    string? ChannelId,
    string? SenderUserId,
    string Username,
    string Message,
    string? BotId)
{
    /// <summary>
    /// Snowflake id of the guild the message was posted in; null when unknown.
    /// Null (the default) skips the Discord-role→tier resolution and answers
    /// with the stored assignment alone.
    /// </summary>
    public string? GuildId { get; init; }
}

/// <summary>
/// Core-side mirror of the per-bot Discord surface the engine needs — the
/// members match IDiscordService's signatures 1:1, so the DI composition wraps
/// a DiscordService in a tiny adapter (see the W5B report for the adapter code).
/// All methods are best-effort and return false instead of throwing.
/// </summary>
public interface IModDiscordBot
{
    /// <summary>Sends a text message into a channel (name or snowflake id).</summary>
    Task<bool> SendToChannelAsync(string channelNameOrId, string content, string? embedJson, string? guildName = null);

    /// <summary>Direct-messages a user by name.</summary>
    Task<bool> SendDmAsync(string username, string content, string? embedJson, string? guildName = null);

    /// <summary>Grants a role (name or snowflake id) to a user in the origin/first guild.</summary>
    Task<bool> AddRoleToUserAsync(string username, string roleIdOrName, string? reason, string? guildName = null);

    /// <summary>
    /// Snowflake IDs of the roles a member holds in the given guild (the
    /// role→tier mapping lookup). Best-effort and empty on failure — the
    /// client runs without the GuildMembers intent, so the member may not be
    /// resolvable.
    /// </summary>
    Task<List<string>> GetMemberRoleIdsAsync(string guildId, string userId);
}
