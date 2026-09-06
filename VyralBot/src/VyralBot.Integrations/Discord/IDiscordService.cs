namespace VyralBot.Integrations.Discord;

/// <summary>
/// Discord bot chat service. Connects a Discord.Net gateway client with a bot
/// token, receives guild/DM messages, and sends messages to a selected channel.
/// </summary>
public interface IDiscordService
{
    /// <summary>
    /// Identity of the bot this instance represents: the DiscordBots row id for
    /// manager-owned instances, or the default "primary" for the setup wizard's
    /// DI-singleton service. Tags published chat events so replies can route
    /// back to the originating bot.
    /// </summary>
    string BotId { get; }

    /// <summary>True while the gateway client is connected.</summary>
    bool IsConnected { get; }

    /// <summary>Username of the logged-in bot account, null when disconnected.</summary>
    string? BotUsername { get; }

    /// <summary>
    /// Discord username auto-detected from the gateway at the last successful
    /// connect (READY). Null until the first capture; retains the last known
    /// value while disconnected.
    /// </summary>
    string? DetectedBotUsername { get; }

    /// <summary>
    /// Discord application/user snowflake ID auto-detected from the gateway at
    /// the last successful connect. Null until the first capture.
    /// </summary>
    string? DetectedApplicationId { get; }

    /// <summary>
    /// Avatar URL (PNG, 128px) auto-detected from the gateway at the last
    /// successful connect. Null when Discord reports no avatar or before the
    /// first capture.
    /// </summary>
    string? DetectedAvatarUrl { get; }

    /// <summary>
    /// Raised once per connection when the gateway makes the bot's verified
    /// identity available (READY observed during connect). The argument is this
    /// instance's manager row id (<see cref="BotId"/>). Duplicate raises for
    /// the same connection are suppressed, and subscriber exceptions are
    /// swallowed by the raiser. The <see cref="DiscordBotManager"/> subscribes
    /// to persist ApplicationId/BotUsername/AvatarUrl on the bot's row.
    /// </summary>
    event Action<string>? BotIdentityDetected;

    /// <summary>Snowflake ID of the channel selected for sending messages.</summary>
    string? SelectedChannelId { get; }

    /// <summary>Snapshot of guilds (with text channels) the bot is currently in.</summary>
    IReadOnlyList<DiscordGuildInfo> ConnectedGuilds { get; }

    /// <summary>
    /// Lightweight check that a bot token is valid (REST login only, no gateway
    /// session). Returns false for invalid/empty tokens or any failure.
    /// </summary>
    Task<bool> ValidateTokenAsync(string botToken);

    /// <summary>
    /// Log in and start the gateway connection. Persists the token, selected
    /// channel and Status="Connected" in the Discord Platform row.
    /// Returns false on failure (never throws).
    /// </summary>
    Task<bool> ConnectAsync(string botToken);

    /// <summary>
    /// Stops the gateway session, clears in-memory state and persists
    /// Status="Disconnected". Graceful: unhooks all client event handlers
    /// before stopping; the underlying client is disposed lazily on the next
    /// <see cref="ConnectAsync"/> instead of at disconnect time.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Reconnect using the bot token saved in the Discord Platform row:
    /// gracefully disconnects the current session (if connected) and then
    /// connects again with a fresh client. Returns false when no token is
    /// saved or the connect attempt fails (never throws).
    /// </summary>
    Task<bool> ReconnectAsync();

    /// <summary>
    /// Select the channel used by <see cref="SendMessageAsync"/>; persists the
    /// choice in the Discord Platform row and applies it immediately.
    /// </summary>
    Task SetSelectedChannelAsync(string channelId);

    /// <summary>
    /// Send a message to the selected channel. Returns false when not
    /// connected, no channel selected/resolvable, or on error.
    /// </summary>
    Task<bool> SendMessageAsync(string message);

    /// <summary>
    /// Setup test: in every server the bot is in, creates (or reuses) a private text
    /// channel named "vyral-test" visible only to the server owner and the bot,
    /// and pings the owner in it. Returns a human-readable result per server.
    /// </summary>
    Task<string> SendOwnerTestMessageAsync();

    /// <summary>
    /// Kick a member from one of the bot's guilds. Returns a human-readable
    /// result ("Kicked @user from Server", "The bot lacks the Kick Members
    /// permission", ...). Never throws.
    /// </summary>
    /// <param name="guildId">Snowflake ID of the guild.</param>
    /// <param name="userId">Snowflake ID of the member to kick.</param>
    /// <param name="reason">Optional audit-log reason.</param>
    Task<string> KickMemberAsync(string guildId, string userId, string? reason);

    /// <summary>
    /// Ban a user from one of the bot's guilds (works whether or not the user
    /// is currently a member). Returns a human-readable result; never throws.
    /// </summary>
    /// <param name="guildId">Snowflake ID of the guild.</param>
    /// <param name="userId">Snowflake ID of the user to ban.</param>
    /// <param name="reason">Optional audit-log reason.</param>
    /// <param name="deleteMessageDays">Days of the user's recent messages to delete (0-7, Discord limit).</param>
    Task<string> BanMemberAsync(string guildId, string userId, string? reason, int deleteMessageDays = 0);

    /// <summary>
    /// Remove an existing ban for a user in one of the bot's guilds. Returns a
    /// human-readable result; never throws.
    /// </summary>
    /// <param name="guildId">Snowflake ID of the guild.</param>
    /// <param name="userId">Snowflake ID of the banned user.</param>
    Task<string> UnbanMemberAsync(string guildId, string userId);

    /// <summary>
    /// Grant a role to a guild member (command action sequences and moderation).
    /// Returns a human-readable result; never throws.
    /// </summary>
    /// <param name="guildId">Snowflake ID of the guild.</param>
    /// <param name="userId">
    /// Snowflake ID of the member — or a username, resolved via the member
    /// cache with a REST search fallback (command contexts only carry names).
    /// </param>
    /// <param name="roleId">Snowflake ID of the role to grant.</param>
    Task<string> GrantRoleAsync(string guildId, string userId, string roleId);

    /// <summary>
    /// Remove a role from a guild member (command action sequences and
    /// moderation). Returns a human-readable result; never throws.
    /// </summary>
    /// <param name="guildId">Snowflake ID of the guild.</param>
    /// <param name="userId">Snowflake ID of the member (or a username, see <see cref="GrantRoleAsync"/>).</param>
    /// <param name="roleId">Snowflake ID of the role to remove.</param>
    Task<string> RemoveRoleAsync(string guildId, string userId, string roleId);

    /// <summary>
    /// Apply a Discord timeout (communication disabled) to a guild member for
    /// the given number of minutes (1-40320, i.e. Discord's 28-day maximum).
    /// Returns a human-readable result; never throws.
    /// </summary>
    /// <param name="guildId">Snowflake ID of the guild.</param>
    /// <param name="userId">Snowflake ID of the member.</param>
    /// <param name="minutes">Timeout length in minutes (1-40320).</param>
    /// <param name="reason">
    /// Ignored by Discord's timeout endpoint (it accepts no audit-log reason);
    /// kept for signature symmetry with the other moderation methods.
    /// </param>
    Task<string> TimeoutMemberAsync(string guildId, string userId, int minutes, string? reason);

    /// <summary>
    /// Remove an active timeout from a guild member. Returns a human-readable
    /// result; never throws.
    /// </summary>
    /// <param name="guildId">Snowflake ID of the guild.</param>
    /// <param name="userId">Snowflake ID of the member.</param>
    Task<string> RemoveTimeoutAsync(string guildId, string userId);

    /// <summary>
    /// Set the slow-mode delay of a text channel. Returns a human-readable
    /// result; never throws.
    /// </summary>
    /// <param name="channelId">Snowflake ID of the text channel.</param>
    /// <param name="seconds">Delay in seconds; 0 disables slow-mode (max 21600).</param>
    Task<string> SetSlowmodeAsync(string channelId, int seconds);

    /// <summary>
    /// Bulk-delete the newest messages of a text channel. Returns a
    /// human-readable result; never throws. Note that Discord only allows
    /// bulk deletion of messages younger than 14 days.
    /// </summary>
    /// <param name="channelId">Snowflake ID of the text channel.</param>
    /// <param name="count">Number of messages to delete (1-100).</param>
    Task<string> PurgeMessagesAsync(string channelId, int count);

    /// <summary>
    /// Distinct external bot accounts (BotGhost, MEE6, ...) observed in the
    /// bot's servers, most recently seen first. Populated primarily from live
    /// gateway messages and seeded best-effort (and only partially — the client
    /// runs without the GuildMembers intent) from the guild member cache; this
    /// bot's own account is never included. Purely in-memory: nothing is
    /// published to the EventBus and nothing is persisted.
    /// </summary>
    IReadOnlyList<DiscordSeenBot> SeenBots { get; }

    /// <summary>
    /// Send a message (e.g. a help trigger such as "!help") to a specific
    /// channel and wait for the next message authored by the given bot,
    /// returning its raw content (an empty string for embed-only replies).
    /// Returns null on invalid arguments, when not connected, on send failure
    /// or on timeout. Only one capture per bot at a time: a concurrent call for
    /// the same bot completes the previous wait with null and takes over.
    /// Never throws.
    /// </summary>
    /// <param name="channelId">Snowflake ID of the channel to send the trigger into.</param>
    /// <param name="message">Trigger text to send (e.g. "!help").</param>
    /// <param name="authorBotId">Snowflake ID of the bot whose reply to capture.</param>
    /// <param name="timeout">Maximum time to wait for the reply.</param>
    Task<string?> SendAndCaptureReplyAsync(string channelId, string message, string authorBotId, TimeSpan timeout);

    // ==================================================================
    // Workflow / node-editor surface (additive, V1)
    //
    // Backs IDiscordWorkflowBridge (VyralBot.Workflows): every method is
    // best-effort — it returns false / an empty list / an error string
    // instead of throwing, with the failure logged by the implementation.
    // "Origin channel/guild" means the channel/guild of the most recent
    // message or interaction this service saw; when nothing was seen yet
    // the selected channel / first guild is used, mirroring
    // SendMessageAsync's convention.
    // ==================================================================

    /// <summary>
    /// Raised for EVERY slash command this bot receives on the gateway
    /// (raw Discord <c>SlashCommandExecuted</c>). Subscribers gate on the
    /// workflow command names they registered via
    /// <see cref="RegisterWorkflowSlashCommandAsync"/>. The interaction is
    /// deferred ephemeral before the event is raised; the payload's
    /// <c>Respond</c> delegate delivers replies as interaction followups.
    /// </summary>
    event EventHandler<DiscordSlashInvocation>? SlashCommandInvoked;

    /// <summary>
    /// Raised for component (button / select-menu) interactions whose custom
    /// id starts with the workflow namespace prefix "vyralwf:". Same deferral
    /// and reply convention as <see cref="SlashCommandInvoked"/>.
    /// </summary>
    event EventHandler<DiscordComponentInteraction>? ComponentInteractionInvoked;

    /// <summary>
    /// Send a text and/or embed message to a channel resolved by name (with or
    /// without a leading '#') or snowflake id; empty resolves to the origin /
    /// selected channel (same convention as <see cref="SendMessageAsync"/>).
    /// Returns false when not connected or on any failure (never throws).
    /// </summary>
    /// <param name="channelNameOrId">Channel name ("#general" / "general") or snowflake id; empty = origin/selected channel.</param>
    /// <param name="content">Message text (may be empty when embedJson is provided).</param>
    /// <param name="embedJson">Optional embed JSON (tolerant schema — see the service implementation).</param>
    /// <param name="guildName">Optional guild name used to disambiguate same-named channels across guilds.</param>
    Task<bool> SendToChannelAsync(string channelNameOrId, string content, string? embedJson, string? guildName = null);

    /// <summary>
    /// Send a text and/or embed message with optional INTERACTIVE components,
    /// resolved and sent like <see cref="SendToChannelAsync"/>.
    /// <paramref name="buttonsJson"/> is a JSON array of button definitions
    /// ([{"label":"Yes","style":"Success","emoji":"✅"}], style one of
    /// Primary|Secondary|Success|Danger|Link; a Link needs a "url");
    /// <paramref name="menuJson"/> is a select-menu definition
    /// ({"placeholder":"Choose...","options":[{"label":"A","value":"a"}]}).
    /// Both are tolerant of malformed JSON (bad parts are skipped, logged).
    /// <paramref name="componentPrefix"/> is REQUIRED for interactive
    /// components: the "vyralwf:{workflowId}:{nodeId}" prefix that becomes the
    /// components' custom ids ("...:btn{i}" / "...:menu") so clicks dispatch
    /// back into the workflow. When components are requested but the prefix is
    /// missing, they are dropped (logged) and the message still sends so a
    /// send never fails entirely over authoring mistakes. Returns false when
    /// not connected or the send itself fails (never throws).
    /// </summary>
    /// <param name="channelNameOrId">Channel name ("#general" / "general") or snowflake id; empty = origin/selected channel.</param>
    /// <param name="content">Message text (may be empty when embedJson/components are provided).</param>
    /// <param name="embedJson">Optional embed JSON (same tolerant schema as <see cref="SendToChannelAsync"/>).</param>
    /// <param name="buttonsJson">Optional JSON array of button definitions (max 5).</param>
    /// <param name="menuJson">Optional JSON select-menu definition (max 25 options).</param>
    /// <param name="componentPrefix">"vyralwf:{workflowId}:{nodeId}" — the custom-id stem for the components; required for interactive parts.</param>
    /// <param name="guildName">Optional guild name used to disambiguate same-named channels across guilds.</param>
    Task<bool> SendToChannelWithComponentsAsync(string channelNameOrId, string content, string? embedJson, string? buttonsJson, string? menuJson, string? componentPrefix, string? guildName = null);

    /// <summary>
    /// Direct-messages a user by name (resolved across the bot's guilds, with a
    /// seen-bots REST fallback for bot accounts) with optional embed.
    /// Returns false when the user cannot be resolved or the send fails.
    /// </summary>
    Task<bool> SendDmAsync(string username, string content, string? embedJson, string? guildName = null);

    /// <summary>
    /// Sends a text and/or embed message straight to a channel by its snowflake
    /// id — typically the DM channel snowflake carried by a received
    /// DiscordChatMessageEvent, so replies land back in the same DM without the
    /// bot having to resolve the user by name. Returns false when not
    /// connected, the id is invalid/unresolvable or the send fails.
    /// </summary>
    /// <param name="channelId">Snowflake ID of the (DM or guild) channel to send into.</param>
    /// <param name="content">Message text (may be empty when embedJson is provided).</param>
    /// <param name="embedJson">Optional embed JSON (same tolerant schema as <see cref="SendToChannelAsync"/>).</param>
    Task<bool> SendDmToChannelAsync(string channelId, string content, string? embedJson = null);

    /// <summary>
    /// Grants a role (name or snowflake id) to a user (name, display name or
    /// snowflake id) in the origin/first guild. Returns false on failure.
    /// </summary>
    /// <param name="reason">Optional audit-log reason.</param>
    Task<bool> AddRoleToUserAsync(string username, string roleIdOrName, string? reason, string? guildName = null);

    /// <summary>
    /// Removes a role (name or snowflake id) from a user in the origin/first
    /// guild. Returns false on failure.
    /// </summary>
    /// <param name="reason">Optional audit-log reason.</param>
    Task<bool> RemoveRoleFromUserAsync(string username, string roleIdOrName, string? reason, string? guildName = null);

    /// <summary>
    /// Creates a channel in the origin/first guild. <paramref name="channelType"/>
    /// is one of "Text", "Voice" or "Forum" (case-insensitive);
    /// <paramref name="category"/> optionally names/ids a parent category.
    /// Returns false on failure.
    /// </summary>
    Task<bool> CreateWorkflowChannelAsync(string name, string channelType, string? category, string? guildName = null);

    /// <summary>
    /// Deletes one message by scope: "trigger" = the message/interaction that
    /// most recently started a run on this bot (falls back to the newest
    /// message authored by somebody else in the origin channel), "last_bot" =
    /// this bot's most recent successfully sent message in the origin channel.
    /// Returns false when nothing resolvable exists or the delete fails.
    /// </summary>
    Task<bool> DeleteMessageByScopeAsync(string scope, string? guildName = null);

    /// <summary>
    /// Edits this bot's most recent successfully sent message in the origin
    /// channel. Returns false when no tracked message exists or the edit fails.
    /// </summary>
    Task<bool> EditLastBotMessageAsync(string newContent, string? embedJson, string? guildName = null);

    /// <summary>
    /// Adds an emoji reaction to the message identified by scope (see
    /// <see cref="DeleteMessageByScopeAsync"/>). Accepts unicode emoji and
    /// custom ":name:id" emotes. Returns false on failure.
    /// </summary>
    Task<bool> ReactToMessageAsync(string emoji, string scope, string? guildName = null);

    /// <summary>
    /// Creates a public thread off this bot's most recent sent message in the
    /// origin channel. <paramref name="autoArchiveMinutes"/> is clamped to
    /// Discord's supported archive durations (60 / 1440 / 4320 / 10080).
    /// Returns false when no tracked message exists or creation fails.
    /// </summary>
    Task<bool> CreateThreadFromLastAsync(string threadName, int autoArchiveMinutes, string? guildName = null);

    /// <summary>
    /// Locks (denies @everyone SendMessages) or unlocks the origin/selected
    /// channel by updating its @everyone permission overwrite. Returns false
    /// when the channel is not a guild text channel or the modify fails.
    /// </summary>
    /// <param name="reason">Optional audit-log reason.</param>
    Task<bool> SetChannelLockAsync(bool locked, string? reason, string? guildName = null);

    /// <summary>
    /// Checks whether a user holds a role (name or snowflake id) in the
    /// origin/first guild. Returns false when user or role cannot be resolved.
    /// </summary>
    Task<bool> UserHasRoleAsync(string username, string roleIdOrName, string? guildName = null);

    /// <summary>
    /// Role names in the origin/first guild (editor role pickers): managed
    /// roles and @everyone are skipped. Empty list when no guild is available.
    /// </summary>
        /// <summary>Checks whether a user holds a guild permission (e.g. "Administrator", "ManageMessages") in the origin guild. False when unresolvable.</summary>
    Task<bool> UserHasPermissionAsync(string username, string permission, string? guildName = null);

Task<IReadOnlyList<string>> GetWorkflowRoleNamesAsync(string? guildName = null);

    /// <summary>
    /// Text channel names in the origin/first guild (editor channel pickers).
    /// Empty list when no guild is available.
    /// </summary>
    Task<IReadOnlyList<string>> GetWorkflowChannelNamesAsync(string? guildName = null);

    /// <summary>
    /// Registers a workflow slash command PER GUILD for every guild the bot is
    /// currently in (guild commands apply instantly; global ones take up to an
    /// hour). The name is normalized (lowercase, letters/digits/'-'/'_' only,
    /// 1-32 chars, spaces → dashes); an existing command with the same name is
    /// replaced. Registered commands are re-registered automatically on
    /// Ready/GuildAvailable so reconnects restore them.
    /// Returns null on success, otherwise a human-readable error string.
    /// </summary>
    /// <param name="name">Desired command name (normalized before registration).</param>
    /// <param name="description">User-facing description ("Vyral workflow command" when empty).</param>
    /// <param name="parameters">
    /// Optional typed slash options (one Discord option per entry, capped at
    /// Discord's 25): text→String, number→Integer, user→User, channel→Channel,
    /// role→Role, choice→Boolean, attachment→Attachment. When null/empty the
    /// legacy single "args" text option is registered instead. Option names
    /// are normalized with the same rules as the command name; the values the
    /// user picks are published per option in
    /// <see cref="DiscordSlashInvocation.OptionValues"/>.
    /// </param>
    Task<string?> RegisterWorkflowSlashCommandAsync(string name, string description, IReadOnlyList<VyralBot.Workflows.Models.WorkflowParameter>? parameters = null);

    /// <summary>
    /// Deletes the workflow slash command with the given name from every guild
    /// the bot is currently in and forgets it (no re-registration on
    /// reconnect). Returns null on success, otherwise a human-readable error
    /// string; unknown commands are not an error.
    /// </summary>
    /// <param name="name">Command name (normalized before lookup).</param>
    Task<string?> UnregisterWorkflowSlashCommandAsync(string name);

    /// <summary>
    /// Kicks a member (resolved from a username, display name or snowflake via
    /// the same resolution the role actions use) from the origin/first guild.
    /// Returns false when the user/guild cannot be resolved or the kick fails.
    /// </summary>
    /// <param name="reason">Optional audit-log reason.</param>
    Task<bool> KickMemberByNameAsync(string username, string? reason, string? guildName = null);

    /// <summary>
    /// Bans a user (resolved from a username, display name or snowflake) from
    /// the origin/first guild. Returns false on failure.
    /// </summary>
    /// <param name="reason">Optional audit-log reason.</param>
    Task<bool> BanMemberByNameAsync(string username, string? reason, string? guildName = null);

    /// <summary>
    /// Removes an existing ban for a user (resolved from a username, display
    /// name or snowflake — banned users are resolved best-effort since they are
    /// no longer guild members) in the origin/first guild. Returns false on failure.
    /// </summary>
    Task<bool> UnbanMemberByNameAsync(string username, string? guildName = null);

    /// <summary>
    /// Applies a Discord timeout (communication disabled) to a guild member
    /// (resolved from a username, display name or snowflake) for the given
    /// number of minutes (1-40320). Returns false on failure.
    /// </summary>
    /// <param name="reason">Kept for signature symmetry (Discord's timeout endpoint accepts no audit-log reason).</param>
    Task<bool> TimeoutMemberByNameAsync(string username, int minutes, string? reason, string? guildName = null);

    /// <summary>
    /// Removes an active timeout from a guild member (resolved from a username,
    /// display name or snowflake) in the origin/first guild. Returns false on failure.
    /// </summary>
    Task<bool> RemoveTimeoutMemberByNameAsync(string username, string? guildName = null);

    /// <summary>
    /// Bulk-deletes the newest messages of the origin/selected channel (same
    /// channel convention as <see cref="SendMessageAsync"/>). Returns false when
    /// the channel is not a guild text channel or the purge fails.
    /// </summary>
    Task<bool> PurgeOriginChannelAsync(int count, string? guildName = null);

    /// <summary>
    /// Sets the slow-mode delay of the origin/selected channel in seconds
    /// (0 disables slow-mode). Returns false when the channel is not a guild
    /// text channel or the modify fails.
    /// </summary>
    Task<bool> SetSlowmodeOnOriginChannelAsync(int seconds, string? guildName = null);
}
