using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ExecutionContext = VyralBot.Workflows.Runtime.ExecutionContext;

namespace VyralBot.Workflows.Runtime;

/// <summary>
/// Per-execution context shared by every node of a single workflow run.
/// Carries the triggering platform/user/message, execution-scoped variables,
/// DI services, cancellation and logging.
/// </summary>
public partial class ExecutionContext
{
    private static readonly Regex VariableTokenRegex = CreateVariableTokenRegex();

    /// <summary>Unique ID of this execution (one per run).</summary>
    public string ExecutionId { get; } = Guid.NewGuid().ToString("N");

    /// <summary>ID of the workflow being executed.</summary>
    public string WorkflowId { get; set; } = string.Empty;

    /// <summary>Platform the trigger came from: "Twitch", "Kick", "Discord", "YouTube", ...</summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>Discord bot ID (Discord multi-bot setups).</summary>
    public string? BotId { get; set; }

    /// <summary>Discord guild/server name, when applicable.</summary>
    public string? GuildName { get; set; }

    /// <summary>Channel the trigger came from.</summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>User who triggered the workflow.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Chat message that triggered the workflow (command/chat triggers).</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Execution-scoped variables (case-insensitive). Not persisted.</summary>
    public Dictionary<string, string> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>DI service provider; nodes lazily resolve Core services from here.</summary>
    public IServiceProvider Services { get; }

    /// <summary>Cooperative cancellation for the run (e.g. app shutdown).</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>Logger scoped to this execution.</summary>
    public ILogger Logger { get; }

    /// <summary>The originating platform event object, if any (e.g. a ChatMessageReceivedEvent).</summary>
    public object? Origin { get; set; }

    public ExecutionContext(
        IServiceProvider services,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Logger = logger ?? NullLogger.Instance;
        CancellationToken = cancellationToken;
    }

    /// <summary>
    /// Looks a variable up in this execution: first <see cref="Variables"/>, then the
    /// built-in context fields ({username}, {channel}, {platform}, {message},
    /// {botid}, {guild}). Returns null when unknown.
    /// </summary>
    public string? GetVariable(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        if (Variables.TryGetValue(name, out var value))
            return value;

        return name.ToLowerInvariant() switch
        {
            "username" => Username,
            "user" => Username,
            "channel" => Channel,
            "platform" => Platform,
            "message" => Message,
            "botid" => BotId,
            "guild" => GuildName,
            "guildname" => GuildName,
            _ => null
        };
    }

    /// <summary>Sets an execution-scoped variable (null clears it).</summary>
    public void SetVariable(string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (value is null)
            Variables.Remove(name);
        else
            Variables[name] = value;
    }

    /// <summary>
    /// Replaces {token} placeholders with variable values from this context
    /// ({username}, {channel}, {platform}, {message} plus anything set via
    /// <see cref="SetVariable"/>). Unknown tokens are left as-is.
    /// </summary>
    public string Substitute(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return VariableTokenRegex.Replace(text, match =>
        {
            var value = GetVariable(match.Groups[1].Value);
            return value ?? match.Value;
        });
    }

    [GeneratedRegex(@"\{([A-Za-z0-9_\-.]+)\}")]
    private static partial Regex CreateVariableTokenRegex();
}
