using Microsoft.Extensions.Logging;
using System.Text.Json;
using VyralBot.Core.Services.Variables;
using VyralBot.Data;
using VyralBot.Data.Entities;

namespace VyralBot.Core.Services.Commands;

public class CommandService : ICommandService
{
    private readonly IDataService _dataService;
    private readonly IVariableReplacementService _variableReplacementService;
    private readonly ILogger<CommandService> _logger;
    private readonly char[] _commandPrefixes = ['!', '?', '.'];

    public CommandService(
        IDataService dataService,
        IVariableReplacementService variableReplacementService,
        ILogger<CommandService> logger)
    {
        _dataService = dataService;
        _variableReplacementService = variableReplacementService;
        _logger = logger;
    }

    public bool IsCommand(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;
        
        return _commandPrefixes.Contains(message[0]);
    }

    public async Task<bool> RegisterCommandAsync(Command command)
    {
        try
        {
            // Check if command already exists
            var existing = await _dataService.GetCommandByNameAsync(command.Name);
            if (existing != null && existing.IsBuiltIn)
            {
                _logger.LogWarning("Cannot register command {Name} - built-in command with same name exists", command.Name);
                return false;
            }

            command.Id = Guid.NewGuid().ToString();
            command.CreatedAt = DateTime.UtcNow;
            command.UpdatedAt = DateTime.UtcNow;
            
            await _dataService.SaveCommandAsync(command);
            _logger.LogInformation("Registered command: {Name}", command.Name);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register command {Name}", command.Name);
            return false;
        }
    }

    public async Task<string?> ExecuteAsync(string message, CommandContext context, string? discordBotId = null)
    {
        try
        {
            if (!IsCommand(message))
                return null;

            // Parse command name and args
            var parts = message.TrimStart(_commandPrefixes).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return null;

            var commandName = "!" + parts[0].ToLower();
            var args = parts.Skip(1).ToArray();

            // Get command from database
            var command = await _dataService.GetCommandByNameAsync(commandName);
            if (command == null)
                return null;

            // Check if enabled
            if (!command.IsEnabled)
                return null;

            // Per-bot scope (Discord only): a command pinned to one bot via its
            // BotId never fires for a different bot; commands with a null BotId
            // are "all bots" and always pass. The explicit filter wins, falling
            // back to the value captured on the context.
            var effectiveBotId = discordBotId ?? context.DiscordBotId;
            if (!string.IsNullOrEmpty(effectiveBotId)
                && !string.IsNullOrEmpty(command.BotId)
                && !string.Equals(command.BotId, effectiveBotId, StringComparison.Ordinal))
            {
                _logger.LogDebug("Skipping command {Command} for bot {BotId}: scoped to bot {CommandBotId}",
                    commandName, effectiveBotId, command.BotId);
                return null;
            }

            // Check platform-specific enable status
            if (context.Platform.Equals("Kick", StringComparison.OrdinalIgnoreCase) && !command.EnabledOnKick)
                return null;
            if (context.Platform.Equals("Twitch", StringComparison.OrdinalIgnoreCase) && !command.EnabledOnTwitch)
                return null;

            // Check permissions
            if (!HasPermission(context, command.Permission))
            {
                _logger.LogDebug("User {Username} lacks permission {Permission} for command {Command}", 
                    context.Username, command.Permission, commandName);
                return null;
            }

            // Check cooldowns
            var (onCooldown, secondsRemaining) = await CheckCooldownAsync(command, context);
            if (onCooldown)
            {
                _logger.LogDebug("Command {Command} on cooldown for {Seconds}s", commandName, secondsRemaining);
                return null; // Silently ignore cooldown (or optionally return cooldown message)
            }

            // Execute built-in command logic
            string? response;
            if (command.IsBuiltIn)
            {
                response = await ExecuteBuiltInCommandAsync(command, args, context);
                if (response == null)
                    return null;
            }
            else if (!string.IsNullOrWhiteSpace(command.ActionsJson))
            {
                // Action-sequence commands: Core does NOT execute the steps
                // (they need the live platform senders, which live in the UI
                // layer). Apply the bookkeeping, then return the marker so the
                // UI layer loads the command and runs the sequence itself.
                await ApplyCooldownsAsync(command, context);
                await _dataService.IncrementCommandUsageAsync(command.Id);
                return ICommandService.ActionsMarkerPrefix + command.Id;
            }
            else
            {
                // Custom command: random replies win when configured,
                // falling back to the single Response.
                response = PickResponse(command);
                if (response == null)
                    return null;
            }

            // Parse variables
            response = ParseVariables(response, context);

            // Apply cooldowns
            await ApplyCooldownsAsync(command, context);

            // Increment usage count
            await _dataService.IncrementCommandUsageAsync(command.Id);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute command from message: {Message}", message);
            return null;
        }
    }

    private async Task<string?> ExecuteBuiltInCommandAsync(Command command, string[] args, CommandContext context)
    {
        return command.Name.ToLower() switch
        {
            "!commands" => await ExecuteCommandsListAsync(context),
            "!uptime" => ExecuteUptimeAsync(),
            // !watchtime defers to WatchtimeCommandService via the ChatViewModel
            // fallback chain: returning null (without applying cooldowns) lets
            // ExecuteAsync fall through so the real implementation answers
            // instead of this placeholder shadowing it (see KI-18).
            "!watchtime" => null,
            "!socials" => command.Response, // Uses template in Response
            "!discord" => command.Response,
            _ => command.Response
        };
    }

    /// <summary>
    /// Picks the reply for a custom command: a random entry from
    /// <see cref="Command.ResponsesJson"/> when alternate replies are
    /// configured, otherwise the single <see cref="Command.Response"/>.
    /// </summary>
    private static string? PickResponse(Command command)
    {
        var alternates = ParseResponses(command.ResponsesJson);
        if (alternates.Count > 0)
        {
            return alternates[Random.Shared.Next(alternates.Count)];
        }

        return command.Response;
    }

    /// <summary>
    /// Deserializes the ResponsesJson string array. Empty, null, blank or
    /// malformed JSON yields no entries (the caller falls back to Response).
    /// Public so the command builder can load random replies for editing.
    /// </summary>
    public static List<string> ParseResponses(string? responsesJson)
    {
        if (string.IsNullOrWhiteSpace(responsesJson))
        {
            return [];
        }

        try
        {
            var entries = JsonSerializer.Deserialize<List<string>>(responsesJson);
            return entries?
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task<string> ExecuteCommandsListAsync(CommandContext context)
    {
        var commands = await _dataService.GetCommandsAsync(enabledOnly: true);
        
        // Filter by platform
        var platformCommands = commands.Where(c =>
            (context.Platform.Equals("Kick", StringComparison.OrdinalIgnoreCase) && c.EnabledOnKick) ||
            (context.Platform.Equals("Twitch", StringComparison.OrdinalIgnoreCase) && c.EnabledOnTwitch)
        );

        // Filter by permission
        var availableCommands = platformCommands
            .Where(c => HasPermission(context, c.Permission))
            .Select(c => c.Name)
            .Take(10);

        var commandList = string.Join(", ", availableCommands);
        return $"Available commands: {commandList}";
    }

    private string ExecuteUptimeAsync()
    {
        // TODO: Implement stream uptime tracking
        // For now, return placeholder
        return "Stream uptime tracking not yet implemented. Coming soon!";
    }

    public async Task<IEnumerable<Command>> GetAllCommandsAsync(bool? enabledOnly = null)
    {
        return await _dataService.GetCommandsAsync(enabledOnly);
    }

    public async Task<Command?> GetCommandByNameAsync(string name)
    {
        return await _dataService.GetCommandByNameAsync(name);
    }

    public async Task<bool> UpdateCommandAsync(Command command)
    {
        try
        {
            // Built-in commands are EDITABLE but LOCKED: response text,
            // cooldowns and enable toggles update, while the name, permission
            // and built-in flag keep their stored values — a built-in can
            // never be renamed, re-permissioned or turned into a custom one
            // (and deletes stay blocked in DeleteCommandAsync).
            var stored = await _dataService.GetCommandAsync(command.Id);
            if (stored is { IsBuiltIn: true })
            {
                command.Name = stored.Name;
                command.Permission = stored.Permission;
                command.IsBuiltIn = true;
            }

            command.UpdatedAt = DateTime.UtcNow;
            await _dataService.SaveCommandAsync(command);
            _logger.LogInformation("Updated command: {Name}", command.Name);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update command {Name}", command.Name);
            return false;
        }
    }

    public async Task<bool> DeleteCommandAsync(string id)
    {
        try
        {
            var command = await _dataService.GetCommandAsync(id);
            if (command == null)
                return false;

            if (command.IsBuiltIn)
            {
                _logger.LogWarning("Cannot delete built-in command {Name}", command.Name);
                return false;
            }

            await _dataService.DeleteCommandAsync(id);
            _logger.LogInformation("Deleted command: {Name}", command.Name);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete command {Id}", id);
            return false;
        }
    }

    public bool HasPermission(CommandContext context, string permission)
    {
        return permission.ToLower() switch
        {
            "everyone" => true,
            "subscriber" or "sub" => context.IsSubscriber || context.IsVIP || context.IsModerator || context.IsBroadcaster,
            "vip" => context.IsVIP || context.IsModerator || context.IsBroadcaster,
            "moderator" or "mod" => context.IsModerator || context.IsBroadcaster,
            "broadcaster" => context.IsBroadcaster,
            _ => false
        };
    }

    public async Task<(bool OnCooldown, int SecondsRemaining)> CheckCooldownAsync(Command command, CommandContext context)
    {
        // Check global cooldown
        if (command.GlobalCooldown > 0)
        {
            var globalCooldown = await _dataService.GetCommandCooldownAsync(command.Id, null, context.Platform);
            if (globalCooldown != null)
            {
                var remaining = (int)(globalCooldown.ExpiresAt - DateTime.UtcNow).TotalSeconds;
                return (true, remaining);
            }
        }

        // Check user cooldown
        if (command.UserCooldown > 0)
        {
            var userCooldown = await _dataService.GetCommandCooldownAsync(command.Id, context.Username, context.Platform);
            if (userCooldown != null)
            {
                var remaining = (int)(userCooldown.ExpiresAt - DateTime.UtcNow).TotalSeconds;
                return (true, remaining);
            }
        }

        return (false, 0);
    }

    private async Task ApplyCooldownsAsync(Command command, CommandContext context)
    {
        var now = DateTime.UtcNow;

        // Apply global cooldown
        if (command.GlobalCooldown > 0)
        {
            var globalCooldown = new CommandCooldown
            {
                Id = Guid.NewGuid().ToString(),
                CommandId = command.Id,
                CommandName = command.Name,
                Username = null,
                Platform = context.Platform,
                ExpiresAt = now.AddSeconds(command.GlobalCooldown),
                CreatedAt = now
            };
            await _dataService.SaveCommandCooldownAsync(globalCooldown);
        }

        // Apply user cooldown
        if (command.UserCooldown > 0)
        {
            var userCooldown = new CommandCooldown
            {
                Id = Guid.NewGuid().ToString(),
                CommandId = command.Id,
                CommandName = command.Name,
                Username = context.Username,
                Platform = context.Platform,
                ExpiresAt = now.AddSeconds(command.UserCooldown),
                CreatedAt = now
            };
            await _dataService.SaveCommandCooldownAsync(userCooldown);
        }
    }

    public string ParseVariables(string text, CommandContext context)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Build context dictionary with priority variables
        var contextVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "username", context.Username },
            { "platform", context.Platform },
            { "channel", context.Channel },
            { "balance", context.CurrencyBalance.ToString() },
            { "role", GetUserRole(context) }
        };

        // Add custom variables from context
        foreach (var kvp in context.CustomVariables)
        {
            contextVariables[kvp.Key] = kvp.Value;
        }

        // Use the variable replacement service with async-over-sync
        var result = _variableReplacementService
            .ReplaceVariablesAsync(text, contextVariables, context.Platform)
            .GetAwaiter()
            .GetResult();

        return result;
    }

    private string GetUserRole(CommandContext context)
    {
        if (context.IsBroadcaster) return "Broadcaster";
        if (context.IsModerator) return "Moderator";
        if (context.IsVIP) return "VIP";
        if (context.IsSubscriber) return "Subscriber";
        return "Viewer";
    }
}
