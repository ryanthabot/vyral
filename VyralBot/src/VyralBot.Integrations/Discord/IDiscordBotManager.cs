namespace VyralBot.Integrations.Discord;

/// <summary>
/// Owns MULTIPLE Discord bots. Each bot is a dedicated <see cref="DiscordService"/>
/// instance created by the manager (the DI-singleton <see cref="IDiscordService"/>
/// stays reserved for the setup wizard). Persistence (tokens, status, selected
/// channel per bot) lives in the DiscordBots table.
/// </summary>
/// <remarks>
/// Chat reply routing V1: incoming <c>DiscordChatMessageEvent</c>s are NOT tagged
/// with a bot id (the shared EventBus carries no per-instance origin), so
/// ChatViewModel sends Discord replies through <see cref="Primary"/> — the first
/// connected bot. Per-bot routing needs event tagging in a future iteration.
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

    /// <summary>The live service instance for a running bot, null otherwise.</summary>
    IDiscordService? Get(string botId);

    /// <summary>Snapshots of all currently connected bot services.</summary>
    IReadOnlyList<IDiscordService> Connected { get; }

    /// <summary>First connected bot (V1 reply-routing target), null when none run.</summary>
    IDiscordService? Primary { get; }
}
