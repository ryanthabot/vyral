using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Commands;

public interface ICommandService
{
    /// <summary>
    /// Registers a command in the database
    /// </summary>
    Task<bool> RegisterCommandAsync(Command command);
    
    /// <summary>
    /// Executes a command from a chat message
    /// </summary>
    Task<string?> ExecuteAsync(string message, CommandContext context);
    
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
    /// Updates a command
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
}
