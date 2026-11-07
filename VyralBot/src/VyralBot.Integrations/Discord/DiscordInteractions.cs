namespace VyralBot.Integrations.Discord;

/// <summary>
/// One Discord slash-command invocation captured from a bot's gateway
/// (<c>SlashCommandExecuted</c>). Raised by <see cref="DiscordService.SlashCommandInvoked"/>
/// for EVERY slash command the bot receives — the workflow layer gates on the
/// command names it registered via
/// <see cref="IDiscordService.RegisterWorkflowSlashCommandAsync"/> and ignores the rest.
/// </summary>
public sealed record DiscordSlashInvocation
{
    /// <summary>Id of the bot instance that received the invocation (DiscordBots row id or "primary").</summary>
    public string BotId { get; set; } = string.Empty;

    /// <summary>Normalized slash-command name as Discord reported it (e.g. "my-workflow").</summary>
    public string CommandName { get; set; } = string.Empty;

    /// <summary>
    /// Everything after the command name (single "args" text option), already
    /// trimmed. Empty when the user passed no arguments.
    /// </summary>
    public string Arguments { get; set; } = string.Empty;

    /// <summary>
    /// Per-option values of typed slash commands (one entry per Discord option,
    /// keyed by the option's Discord name — plus, where resolvable, the
    /// workflow parameter's original authored name). Values are stringified
    /// tolerantly per option type: User→username, Channel→channel name,
    /// Role→role name, Boolean→"true"/"false", Integer/other→invariant
    /// ToString. Empty when the command has no typed options.
    /// </summary>
    public System.Collections.Generic.Dictionary<string, string> OptionValues { get; set; } = new();

    /// <summary>Discord username of the invoking user ("Unknown" when unavailable).</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Name of the channel the command was used in ("DirectMessage" for DMs, "" when unknown).</summary>
    public string ChannelName { get; set; } = string.Empty;

    /// <summary>Name of the guild the command was used in; null for direct messages/unknown.</summary>
    public string? GuildName { get; set; }

    /// <summary>
    /// Optional reply channel (interaction followup — the interaction is
    /// already deferred at trigger entry, so this works even when the workflow
    /// engine needs more than Discord's 3-second response window). Replies are
    /// ephemeral (Discord MessageFlags.Ephemeral), visible only
    /// to the invoking user (see DiscordService.RespondEphemeralAsync).
    /// Best-effort: never throws. Null when the interaction could not be
    /// captured.
    /// </summary>
    public Func<string, Task>? Respond { get; set; }
}

/// <summary>
/// One Discord component (button / select-menu) interaction captured from a
/// bot's gateway. Raised by <see cref="DiscordService.ComponentInteractionInvoked"/>
/// ONLY for components whose custom id starts with the workflow namespace
/// prefix "vyralwf:" (workflow components use "vyralwf:{workflowId}(:{extra})?").
/// </summary>
public sealed record DiscordComponentInteraction
{
    /// <summary>Id of the bot instance that received the interaction (DiscordBots row id or "primary").</summary>
    public string BotId { get; set; } = string.Empty;

    /// <summary>Component custom id ("vyralwf:{workflowId}(:{extra})?").</summary>
    public string CustomId { get; set; } = string.Empty;

    /// <summary>Discord username of the invoking user ("Unknown" when unavailable).</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Name of the channel the component lives in ("DirectMessage" for DMs, "" when unknown).</summary>
    public string ChannelName { get; set; } = string.Empty;

    /// <summary>Name of the guild the component lives in; null for direct messages/unknown.</summary>
    public string? GuildName { get; set; }

    /// <summary>Selected values (select menus); empty for buttons.</summary>
    public string[] Values { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Optional reply channel (interaction followup — the interaction is
    /// already deferred at trigger entry, so this works even when the workflow
    /// engine needs more than Discord's 3-second response window). Replies are
    /// ephemeral (Discord MessageFlags.Ephemeral), visible only to the
    /// invoking user (see DiscordService.RespondEphemeralAsync). Best-effort:
    /// never throws. Null when the interaction could not be captured.
    /// </summary>
    public Func<string, Task>? Respond { get; set; }
}
