namespace VyralBot.Integrations.Discord;

/// <summary>
/// Owns MULTIPLE Discord bots. Each bot is a dedicated <see cref="DiscordService"/>
/// instance created by the manager. Persistence (tokens, status, selected
/// channel per bot) lives in the DiscordBots table.
/// </summary>
/// <remarks>
/// The legacy DI-singleton <see cref="IDiscordService"/> (the setup wizard's
/// service) is no longer a second connection world: whenever it connects with
/// the Platform row's token it is ADOPTED into the manager's running set (see
/// <see cref="AdoptPlatformTokenAsync"/>) so there is exactly ONE gateway
/// session per token and every chat event carries a DiscordBots row id.
/// </remarks>
/// <remarks>
/// Chat reply routing V1: incoming <c>DiscordChatMessageEvent</c>s carry the
/// publishing service's <c>BotId</c>, so replies route back through
/// <see cref="Get"/>; untagged events fall back to <see cref="Primary"/>.
/// </remarks>
public interface IDiscordBotManager
{
    /// <summary>
    /// Raised whenever the bot list or any bot's status changes. May fire from
    /// background threads — UI handlers must dispatcher-hop.
    /// </summary>
    event Action? BotsChanged;

    /// <summary>
    /// Reads all bots from the DiscordBots table. On first run (empty table)
    /// migrates the legacy Platform row Name="Discord" (AccessToken, Username,
    /// ChannelId, Status) into a bot named "Primary" when a token exists.
    /// </summary>
    Task<IReadOnlyList<DiscordBotConfig>> LoadBotsAsync();

    /// <summary>
    /// Persists a new bot (id assigned when empty). Returns false when the name
    /// or token is empty, or another bot already uses the name.
    /// </summary>
    Task<bool> AddBotAsync(DiscordBotConfig bot);

    /// <summary>
    /// Updates name/token/selected-channel. Null/empty token or channel keeps
    /// the stored value; a changed token restarts a running bot. Returns false
    /// when the bot does not exist or the new name collides.
    /// </summary>
    Task<bool> UpdateBotAsync(DiscordBotConfig bot);

    /// <summary>Stops the bot when running and deletes its row.</summary>
    Task RemoveBotAsync(string botId);

    /// <summary>
    /// Connects the bot's DiscordService via ConnectAsync(token) and applies the
    /// saved SelectedChannelId. Persists Status="Connected" (or "Error").
    /// Returns false when the bot/token is missing or the connect failed.
    /// </summary>
    Task<bool> StartBotAsync(string botId);

    /// <summary>Disconnects and disposes the bot's service; persists Status="Disconnected".</summary>
    Task StopBotAsync(string botId);

    /// <summary>Stops every running bot.</summary>
    Task StopAllAsync();

    /// <summary>Starts every saved bot whose Status=="Connected" (app startup auto-restore).</summary>
    Task StartAllSavedAsync();

    /// <summary>
    /// Bot id (DiscordBots row id) of the running bot that currently serves
    /// the legacy Platform row's ("Home") token — i.e. the adopted Home
    /// session — or null when no such bot is running. Lets the Home
    /// disconnect flow stop exactly that bot and never an unrelated one.
    /// </summary>
    string? PlatformAdoptedBotId { get; }

    /// <summary>
    /// Single source of truth for "the Discord connection from Home" (the
    /// legacy Platform row Name="Discord" is the CONFIG store; the running
    /// world is this manager): ensures a DiscordBots row exists for
    /// <paramref name="platformToken"/>, points the legacy DI-singleton at
    /// that token (reconnecting it when it still serves a previous token) and
    /// brings exactly ONE gateway session online through the manager —
    /// adopting the legacy singleton's session when it serves the token (the
    /// same machinery <see cref="StartAllSavedAsync"/> uses at startup) or
    /// starting a manager-owned service otherwise.
    /// When the Home token changed since the last adoption, the row that
    /// served the PREVIOUS token — and only that row — is stopped first.
    /// Idempotent: returns success immediately when the token's row already
    /// runs. Rows unrelated to the Home token are never touched.
    /// </summary>
    /// <param name="platformToken">Token saved on the legacy Platform row (required).</param>
    /// <param name="preferredName">
    /// Row-name hint used only when a NEW row must be created and no live
    /// gateway username is available (a detected username always wins;
    /// "Discord Bot" is the fallback; names are uniquified against existing
    /// rows).
    /// </param>
    Task<DiscordAdoptionResult> AdoptPlatformTokenAsync(string platformToken, string? preferredName = null);

    /// <summary>The live service instance for a running bot, null otherwise.</summary>
    IDiscordService? Get(string botId);

    /// <summary>Snapshots of all currently connected bot services.</summary>
    IReadOnlyList<IDiscordService> Connected { get; }

    /// <summary>First connected bot (V1 reply-routing target), null when none run.</summary>
    IDiscordService? Primary { get; }

    /// <summary>
    /// ID of the bot row currently flagged with the given single-assignment
    /// role (one of <see cref="DiscordBotRoles.All"/>), or null when the role
    /// is unassigned. Case-sensitive on the role constants.
    /// </summary>
    Task<string?> GetBotIdForRoleAsync(string role);

    /// <summary>
    /// <see cref="GetBotIdForRoleAsync"/> with Primary fallback: the flagged
    /// bot's id, else the first connected bot's id (never null while any bot
    /// is connected — unconfigured setups keep today's Primary behavior).
    /// </summary>
    Task<string?> ResolveBotIdForRoleAsync(string role);

    /// <summary>
    /// Assigns (or unassigns) a single-assignment role for a bot.
    /// <paramref name="assigned"/>=true flags that bot's row and CLEARS the
    /// same role on every other row; false clears just that bot. Persists the
    /// changed row(s) and raises <see cref="BotsChanged"/>.
    /// </summary>
    Task AssignRoleAsync(string botId, string role, bool assigned);

    /// <summary>
    /// Loads one bot's welcome/auto-role-on-join settings (persisted under
    /// <c>DiscordBot.{botId}.WelcomeSettings</c>). Never null: missing or
    /// malformed rows yield <see cref="DiscordBotWelcomeSettings.Default"/>.
    /// </summary>
    Task<DiscordBotWelcomeSettings> GetWelcomeSettingsAsync(string botId);

    /// <summary>
    /// Persists one bot's welcome/auto-role-on-join settings. The manager's
    /// <c>DiscordMemberJoinedEvent</c> handler reads them live on each join,
    /// so a save takes effect immediately — no restart needed.
    /// </summary>
    Task SaveWelcomeSettingsAsync(string botId, DiscordBotWelcomeSettings settings);

    /// <summary>
    /// Loads one bot's ALLOWED CHANNELS command permissions (persisted under
    /// <c>DiscordBot.{botId}.AllowedChannels</c>). Never null: missing or
    /// malformed rows yield <see cref="DiscordBotAllowedChannels.Default"/>
    /// (unrestricted). Enforced by <c>CommandService</c> when the bot's
    /// commands are matched; moderators/broadcasters are never restricted.
    /// </summary>
    Task<DiscordBotAllowedChannels> GetAllowedChannelsAsync(string botId);

    /// <summary>
    /// Persists one bot's ALLOWED CHANNELS list. An empty list means commands
    /// work in every channel (legacy behavior). Takes effect on the next
    /// command — no restart needed.
    /// </summary>
    Task SaveAllowedChannelsAsync(string botId, DiscordBotAllowedChannels allowed);

    /// <summary>
    /// Loads one bot's recurring announcements (persisted under
    /// <c>DiscordBot.{botId}.ScheduledMessages</c>). Never null: missing or
    /// malformed rows yield an empty list.
    /// </summary>
    Task<IReadOnlyList<DiscordBotScheduledMessage>> GetScheduledMessagesAsync(string botId);

    /// <summary>
    /// Persists one bot's recurring announcements (bounded by
    /// <see cref="DiscordBotScheduledMessage.MaxPerBot"/>, extra rows
    /// dropped). Saving resets every entry's next-run clock, so the cycle
    /// restarts interval minutes after the save.
    /// </summary>
    Task SaveScheduledMessagesAsync(string botId, IReadOnlyList<DiscordBotScheduledMessage> messages);

    /// <summary>
    /// Unassigns the role from every bot, persists the changed row(s) and
    /// raises <see cref="BotsChanged"/>.
    /// </summary>
    Task ClearRoleAsync(string role);
}

/// <summary>
/// Outcome of <see cref="IDiscordBotManager.AdoptPlatformTokenAsync"/>.
/// </summary>
/// <param name="Success">True when a DiscordBots row for the token exists and is running.</param>
/// <param name="BotId">DiscordBots row id now serving the Home token; null on failure.</param>
/// <param name="BotUsername">Live gateway username of the running bot when known.</param>
/// <param name="Error">Human-readable failure reason when <paramref name="Success"/> is false.</param>
public sealed record DiscordAdoptionResult(
    bool Success,
    string? BotId = null,
    string? BotUsername = null,
    string? Error = null);
