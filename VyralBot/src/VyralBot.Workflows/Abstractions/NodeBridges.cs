namespace VyralBot.Workflows.Abstractions;

/// <summary>
/// Discord capability bridge consumed by workflow nodes (the engine has no
/// Integration dependencies — the UI layer registers an implementation that
/// adapts over IDiscordBotManager/IDiscordService). Nodes resolve it lazily
/// from ExecutionContext.Services and must treat a missing registration as a
/// failed node result, never a throw. All methods are best-effort: they return
/// false (with the failure logged by the implementation) instead of throwing.
/// All "botId" parameters may be null — implementations route to the origin
/// bot when known, else the primary/selected bot (same convention as
/// WorkflowMessageSender).
/// </summary>
public interface IDiscordWorkflowBridge
{
    /// <summary>Sends a text and/or embed message. Empty channel override = origin channel; a name or id is resolved within the guild.</summary>
    Task<bool> SendToChannelAsync(string? botId, string? guild, string? channelOverride, string content, string? embedJson);

    /// <summary>
    /// Component-aware send (Buttons/Menu node config JSON). <paramref name="componentPrefix"/>
    /// is the "vyralwf:{workflowId}:{nodeId}" stem that becomes the components'
    /// custom ids ("...:btn{i}" / "...:menu") so clicks dispatch back into the
    /// workflow; when it is missing the implementation drops the components
    /// (logged) and still delivers the text/embed.
    /// </summary>
    Task<bool> SendToChannelWithComponentsAsync(string? botId, string? guild, string? channelOverride, string content, string? embedJson, string buttonsJson, string menuJson, string? componentPrefix = null);

    /// <summary>Direct-messages a user by name (resolved via the guild) with optional embed.</summary>
    Task<bool> SendDmAsync(string? botId, string username, string content, string? embedJson);

    /// <summary>Grants a role (name or id) to a user in the origin guild.</summary>
    Task<bool> AddRoleAsync(string? botId, string username, string roleIdOrName, string? reason);

    /// <summary>Removes a role (name or id) from a user in the origin guild.</summary>
    Task<bool> RemoveRoleAsync(string? botId, string username, string roleIdOrName, string? reason);

    /// <summary>Creates a channel. channelType: "Text", "Voice" or "Forum"; category optional.</summary>
    Task<bool> CreateChannelAsync(string? botId, string name, string channelType, string? category);

    /// <summary>Deletes a message. scope: "trigger" (the message that started the run) or "last_bot" (the bot's most recent sent message in the origin channel).</summary>
    Task<bool> DeleteMessageAsync(string? botId, string scope);

    /// <summary>Edits the bot's most recent sent message in the origin channel.</summary>
    Task<bool> EditLastBotMessageAsync(string? botId, string newContent, string? embedJson);

    /// <summary>Adds an emoji reaction. scope: "trigger" or "last_bot".</summary>
    Task<bool> ReactAsync(string? botId, string emoji, string scope);

    /// <summary>Creates a thread from the bot's most recent message in the origin channel.</summary>
    Task<bool> CreateThreadAsync(string? botId, string threadName, int autoArchiveMinutes);

    /// <summary>Locks (denies @everyone SendMessages) or unlocks the origin channel.</summary>
    Task<bool> SetChannelLockAsync(string? botId, bool locked, string? reason);

    /// <summary>Role names in the origin guild (editor role pickers). Empty list when no bot is connected.</summary>
    Task<IReadOnlyList<string>> GetRoleNamesAsync(string? botId);

    /// <summary>Text channel names in the origin guild (editor channel pickers / restriction lists). Empty list when no bot is connected.</summary>
    Task<IReadOnlyList<string>> GetChannelNamesAsync(string? botId);

    /// <summary>Checks whether a user holds a role (name or id) in the origin guild. False when unresolvable.</summary>
    Task<bool> UserHasRoleAsync(string? botId, string username, string roleIdOrName);

    /// <summary>Checks whether a user holds a guild permission (e.g. "Administrator", "ManageMessages") in the origin guild. False when unresolvable.</summary>
    Task<bool> UserHasPermissionAsync(string? botId, string username, string permission);

    // ------------------------------------------------------------------
    // Moderation (best-effort like everything above). Users are resolved
    // from a username/display-name/snowflake via the same resolution the
    // implementation already uses for DMs and role actions; Purge/Slowmode
    // target the origin channel and need no user.
    // ------------------------------------------------------------------

    /// <summary>Kicks a member (resolved from a username or snowflake) from the origin guild.</summary>
    Task<bool> KickUserAsync(string? botId, string username, string? reason);

    /// <summary>Bans a user (resolved from a username or snowflake) from the origin guild.</summary>
    Task<bool> BanUserAsync(string? botId, string username, string? reason);

    /// <summary>Lifts a ban for a user (resolved from a username or snowflake) in the origin guild.</summary>
    Task<bool> UnbanUserAsync(string? botId, string username);

    /// <summary>Applies a Discord timeout (communication disabled) to a member for the given minutes.</summary>
    Task<bool> TimeoutUserAsync(string? botId, string username, int minutes, string? reason);

    /// <summary>Removes an active timeout from a member (resolved from a username or snowflake).</summary>
    Task<bool> RemoveTimeoutUserAsync(string? botId, string username);

    /// <summary>Bulk-deletes the newest <paramref name="count"/> messages of the origin channel (1-100).</summary>
    Task<bool> PurgeChannelAsync(string? botId, int count);

    /// <summary>Sets the slow-mode delay of the origin channel in seconds (0 disables).</summary>
    Task<bool> SetSlowmodeAsync(string? botId, int seconds);
}

/// <summary>A Discord slash-command invocation routed to the workflow engine.</summary>
public class WorkflowSlashInvocation
{
    public string BotId { get; set; } = string.Empty;
    public string CommandName { get; set; } = string.Empty;
    /// <summary>Everything after the command name (single text argument), already trimmed.</summary>
    public string Arguments { get; set; } = string.Empty;

    /// <summary>Typed slash-option values keyed by authored parameter name (mirrored under normalized names). Empty for plain commands.</summary>
    public System.Collections.Generic.Dictionary<string, string> OptionValues { get; set; } = new();
    public string Username { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public string? GuildName { get; set; }
    /// <summary>Optional reply channel (interaction followup). Null when the caller replies itself.</summary>
    public Func<string, Task>? Reply { get; set; }
}

/// <summary>A Discord button/select-menu interaction routed to the workflow engine.</summary>
public class WorkflowComponentInteraction
{
    public string BotId { get; set; } = string.Empty;
    /// <summary>Component custom id. Workflow components use "vyralwf:{workflowId}(:{extra})?".</summary>
    public string CustomId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public string? GuildName { get; set; }
    /// <summary>Selected values (select menus); empty for buttons.</summary>
    public string[] Values { get; set; } = Array.Empty<string>();
    /// <summary>Optional reply channel (interaction followup). Null when the caller replies itself.</summary>
    public Func<string, Task>? Reply { get; set; }
}

/// <summary>
/// Registry letting the "wait_for_response" node suspend until the triggering
/// user posts another message on the same platform/channel. The UI layer
/// registers the implementation and feeds it from every chat pipeline
/// (ChatViewModel/DiscordService). Feed() never blocks; WaitForResponseAsync
/// returns null on timeout/cancellation.
/// </summary>
public interface IResponseWaiterRegistry
{
    /// <summary>Waits for the next message from <paramref name="username"/> in <paramref name="channel"/> on <paramref name="platform"/>.</summary>
    Task<string?> WaitForResponseAsync(string platform, string username, string channel, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Called by chat pipelines for every inbound user message (cheap no-op when nobody is waiting).</summary>
    void Feed(string platform, string username, string channel, string message);
}
