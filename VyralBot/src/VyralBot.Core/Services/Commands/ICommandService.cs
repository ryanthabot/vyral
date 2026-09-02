using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Commands;

public interface ICommandService
{
    /// <summary>
    /// Prefix <see cref="ExecuteAsync"/> returns (immediately followed by the
    /// command id) when the matched custom command carries an action sequence
    /// (Command.ActionsJson). Core never executes action steps — they need the
    /// live platform senders — so the UI layer parses the id, loads the
    /// command and runs the sequence itself (CommandActionRunner).
    /// </summary>
    public const string ActionsMarkerPrefix = "$ACTIONS$";

    /// <summary>
    /// Loads command processing settings (prefixes + per-platform toggles)
    /// from the Settings → Commands config keys. Called after a profile loads
    /// and again whenever those settings change.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Registers a command in the database
    /// </summary>
    Task<bool> RegisterCommandAsync(Command command);
    
    /// <summary>
    /// Executes a command from a chat message. Random-reply commands return
    /// one of their ResponsesJson entries; action-sequence commands return
    /// <see cref="ActionsMarkerPrefix"/> + the command id for the UI layer.
    /// </summary>
    /// <param name="message">Raw chat message starting with a command prefix.</param>
    /// <param name="context">Per-invocation command context.</param>
    /// <param name="discordBotId">
    /// Optional Discord bot origin: commands whose <see cref="Command.BotId"/> is a
    /// DIFFERENT bot are skipped; commands with a null BotId run for every bot.
    /// Only meaningful for Discord messages — pass null for other platforms.
    /// </param>
    Task<string?> ExecuteAsync(string message, CommandContext context, string? discordBotId = null);
    
    /// <summary>
    /// Gets all commands
    /// </summary>
    Task<IEnumerable<Command>> GetAllCommandsAsync(bool? enabledOnly = null);
    
    /// <summary>
    /// Gets a command by name
    /// </summary>
    Task<Command?> GetCommandByNameAsync(string name);
    
    /// <summary>
    /// Checks if a message is a command
    /// </summary>
    bool IsCommand(string message);
    
    /// <summary>
    /// Updates a command. Built-in commands may only have their response text,
    /// cooldowns and enable toggles changed — the stored name, permission and
    /// built-in flag are always preserved (no renames, no deletes).
    /// </summary>
    Task<bool> UpdateCommandAsync(Command command);
    
    /// <summary>
    /// Deletes a custom command (built-in commands cannot be deleted)
    /// </summary>
    Task<bool> DeleteCommandAsync(string id);
    
    /// <summary>
    /// Checks if user has permission to execute command
    /// </summary>
    bool HasPermission(CommandContext context, string permission);
    
    /// <summary>
    /// Checks if command is on cooldown
    /// </summary>
    Task<(bool OnCooldown, int SecondsRemaining)> CheckCooldownAsync(Command command, CommandContext context);
    
    /// <summary>
    /// Parses template variables in response text
    /// </summary>
    string ParseVariables(string text, CommandContext context);
}

/// <summary>
/// Context information for command execution
/// </summary>
public class CommandContext
{
    public required string Username { get; set; }
    public required string Platform { get; set; }
    public required string Channel { get; set; }
    public bool IsModerator { get; set; }
    public bool IsSubscriber { get; set; }
    public bool IsVIP { get; set; }
    public bool IsBroadcaster { get; set; }
    public int CurrencyBalance { get; set; }
    public Dictionary<string, string> CustomVariables { get; set; } = new();

    /// <summary>
    /// Discord bot the triggering message came from (DiscordBots row id, or
    /// "primary" for the setup wizard's singleton). Null for non-Discord
    /// platforms. Used together with the explicit <c>discordBotId</c> filter on
    /// <see cref="ICommandService.ExecuteAsync"/> to skip commands pinned to a
    /// different bot.
    /// </summary>
    public string? DiscordBotId { get; init; }
}
