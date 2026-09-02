using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VyralBot.Core.Services.Commands;
using VyralBot.Core.Services.Currency;
using VyralBot.Core.Services.Variables;
using VyralBot.Data.Entities;
using VyralBot.Integrations.Discord;
using VyralBot.Integrations.Kick;
using VyralBot.Integrations.Twitch;

namespace VyralBot.UI.Services;

/// <summary>
/// Executes a custom command's ordered ACTION SEQUENCE (Command.ActionsJson) —
/// BotGhost-style steps: Send Message, Wait, Grant/Remove Role (Discord) and
/// Add/Remove Currency.
///
/// Core's <see cref="ICommandService"/> deliberately does NOT run these steps
/// (they need the live platform senders, which only exist in the UI layer); it
/// returns the <see cref="ICommandService.ActionsMarkerPrefix"/> marker and
/// ChatViewModel hands the command to this runner.
///
/// Every step is wrapped in try/catch: a failed step logs, is summarized and
/// the sequence CONTINUES. Returns a short human-readable summary (joined
/// with a middle dot) used as the chat reply, or null when nothing ran.
/// </summary>
/// <remarks>
/// Step JSON shapes (ActionsJson is an array of these):
/// <c>{"type":"send","text":"..."}</c>,
/// <c>{"type":"wait","seconds":5}</c>,
/// <c>{"type":"grant_role","roleId":"123"}</c>,
/// <c>{"type":"remove_role","roleId":"123"}</c>,
/// <c>{"type":"add_currency","amount":100}</c>,
/// <c>{"type":"remove_currency","amount":100}</c>.
/// </remarks>
public class CommandActionRunner
{
    /// <summary>Upper bound for wait steps — keeps sequences chat-friendly.</summary>
    private const int MaxWaitSeconds = 30;

    private readonly ILogger<CommandActionRunner> _logger;
    private readonly ITwitchService _twitchService;
    private readonly IKickService _kickService;
    private readonly IDiscordService _discordService;
    private readonly IDiscordBotManager _discordBotManager;
    private readonly IVariableReplacementService _variableReplacementService;
    private readonly ICurrencyService? _currencyService;

    public CommandActionRunner(
        ILogger<CommandActionRunner> logger,
        ITwitchService twitchService,
        IKickService kickService,
        IDiscordService discordService,
        IDiscordBotManager discordBotManager,
        IVariableReplacementService variableReplacementService,
        ICurrencyService? currencyService = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _twitchService = twitchService ?? throw new ArgumentNullException(nameof(twitchService));
        _kickService = kickService ?? throw new ArgumentNullException(nameof(kickService));
        _discordService = discordService ?? throw new ArgumentNullException(nameof(discordService));
        _discordBotManager = discordBotManager ?? throw new ArgumentNullException(nameof(discordBotManager));
        _variableReplacementService = variableReplacementService ?? throw new ArgumentNullException(nameof(variableReplacementService));
        _currencyService = currencyService;
    }

    /// <summary>
    /// Runs every action step of <paramref name="command"/> in order for the
    /// invocation captured in <paramref name="ctx"/>. Returns the summary
    /// string for the reply, or null when the command has no runnable steps.
    /// </summary>
    public async Task<string?> RunAsync(Command command, CommandContext ctx)
    {
        var steps = ParseSteps(command.ActionsJson);
        if (steps.Count == 0)
        {
            _logger.LogWarning("Command {Command} has an action sequence but no runnable steps", command.Name);
            return null;
        }

        var summary = new List<string>(steps.Count);

        foreach (var step in steps)
        {
            try
            {
                switch (step.Type)
                {
                    case "send":
                        summary.Add(await SendStepAsync(step.Text ?? string.Empty, ctx).ConfigureAwait(false));
                        break;

                    case "wait":
                        summary.Add(await WaitStepAsync(step.Seconds).ConfigureAwait(false));
                        break;

                    case "grant_role":
                    case "remove_role":
                        summary.Add(await RoleStepAsync(step.Type, step.RoleId, ctx).ConfigureAwait(false));
                        break;

                    case "add_currency":
                    case "remove_currency":
                        summary.Add(await CurrencyStepAsync(step.Type, step.Amount, ctx).ConfigureAwait(false));
                        break;

                    default:
                        _logger.LogWarning("Unknown action step type '{Type}' on command {Command}", step.Type, command.Name);
                        summary.Add($"skipped unknown step '{step.Type}'");
                        break;
                }
            }
            catch (Exception ex)
            {
                // One bad step never aborts the sequence: log, note, continue.
                _logger.LogError(ex, "Action step '{Type}' failed for command {Command}", step.Type, command.Name);
                summary.Add($"{step.Type} failed");
            }
        }

        return summary.Count == 0 ? null : string.Join(" · ", summary);
    }

    // ----------------------------------------------------------------------
    // Steps
    // ----------------------------------------------------------------------

    /// <summary>
    /// Sends the step text to the ORIGINATING platform (the same senders
    /// ChatViewModel uses for replies), with variables replaced from ctx.
    /// </summary>
    private async Task<string> SendStepAsync(string text, CommandContext ctx)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "send skipped (empty text)";
        }

        var message = await ReplaceVariablesAsync(text, ctx).ConfigureAwait(false);

        if (string.Equals(ctx.Platform, "Twitch", StringComparison.OrdinalIgnoreCase))
        {
            await _twitchService.SendMessageAsync(message).ConfigureAwait(false);
            return "sent on Twitch";
        }

        if (string.Equals(ctx.Platform, "Kick", StringComparison.OrdinalIgnoreCase))
        {
            await _kickService.SendMessageAsync(message).ConfigureAwait(false);
            return "sent on Kick";
        }

        if (string.Equals(ctx.Platform, "Discord", StringComparison.OrdinalIgnoreCase))
        {
            var target = ResolveDiscordTarget(ctx.DiscordBotId);
            if (target == null)
            {
                return "send skipped (no Discord bot connected)";
            }

            await target.SendMessageAsync(message).ConfigureAwait(false);
            return "sent on Discord";
        }

        _logger.LogInformation("Send action skipped: unsupported platform {Platform}", ctx.Platform);
        return $"send skipped ({ctx.Platform} not supported)";
    }

    private async Task<string> WaitStepAsync(int seconds)
    {
        if (seconds <= 0)
        {
            return "wait skipped (no duration)";
        }

        var capped = Math.Min(seconds, MaxWaitSeconds);
        if (capped < seconds)
        {
            _logger.LogInformation("Wait step capped from {Requested}s to {Capped}s", seconds, capped);
        }

        await Task.Delay(TimeSpan.FromSeconds(capped)).ConfigureAwait(false);
        return $"waited {capped}s";
    }

    /// <summary>
    /// Grants/removes a Discord role for the triggering user. No-ops with a
    /// log line on non-Discord platforms. The guild is resolved from the
    /// origin bot's ConnectedGuilds (single-guild fallback, or the guild that
    /// owns the triggering channel).
    /// </summary>
    private async Task<string> RoleStepAsync(string type, string? roleId, CommandContext ctx)
    {
        if (!string.Equals(ctx.Platform, "Discord", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Role action {Type} skipped: {Username} triggered it on {Platform}, not Discord",
                type, ctx.Username, ctx.Platform);
            return $"{type} skipped (Discord only)";
        }

        if (string.IsNullOrWhiteSpace(roleId))
        {
            return $"{type} skipped (no role ID)";
        }

        var target = ResolveDiscordTarget(ctx.DiscordBotId);
        if (target == null)
        {
            return $"{type} skipped (no Discord bot connected)";
        }

        var guild = ResolveGuild(target, ctx.Channel);
        if (guild == null)
        {
            _logger.LogWarning("Role action {Type}: could not resolve the guild for channel '{Channel}'",
                type, ctx.Channel);
            return $"{type} skipped (could not resolve the server)";
        }

        // Command contexts carry usernames, not snowflakes — GrantRoleAsync/
        // RemoveRoleAsync resolve the member from either.
        var result = type == "grant_role"
            ? await target.GrantRoleAsync(guild.Id, ctx.Username, roleId).ConfigureAwait(false)
            : await target.RemoveRoleAsync(guild.Id, ctx.Username, roleId).ConfigureAwait(false);

        _logger.LogInformation("Role action {Type} in {Guild}: {Result}", type, guild.Name, result);
        return result;
    }

    /// <summary>
    /// Adds/removes currency for the triggering user on their own platform,
    /// with the audit reason "Command action".
    /// </summary>
    private async Task<string> CurrencyStepAsync(string type, int amount, CommandContext ctx)
    {
        if (_currencyService == null)
        {
            return $"{type} skipped (currency system unavailable)";
        }

        if (amount <= 0)
        {
            return $"{type} skipped (amount must be positive)";
        }

        if (type == "add_currency")
        {
            var ok = await _currencyService
                .AddCurrencyAsync(ctx.Username, ctx.Platform, amount, "Command action", "Earn")
                .ConfigureAwait(false);
            return ok ? $"gave {amount} to {ctx.Username}" : "add currency failed";
        }

        var removed = await _currencyService
            .RemoveCurrencyAsync(ctx.Username, ctx.Platform, amount, "Command action", "Spend")
            .ConfigureAwait(false);
        return removed ? $"took {amount} from {ctx.Username}" : "remove currency failed";
    }

    // ----------------------------------------------------------------------
    // Resolution helpers
    // ----------------------------------------------------------------------

    /// <summary>
    /// Discord send target for an invocation, mirroring ChatViewModel's
    /// reply routing: the originating manager bot first, then the manager's
    /// Primary bot, then the setup wizard's singleton.
    /// </summary>
    private IDiscordService? ResolveDiscordTarget(string? botId)
    {
        if (!string.IsNullOrWhiteSpace(botId)
            && !string.Equals(botId, "primary", StringComparison.Ordinal))
        {
            var byId = _discordBotManager.Get(botId);
            if (byId is { IsConnected: true })
            {
                return byId;
            }
        }

        if (string.Equals(botId, "primary", StringComparison.Ordinal) && _discordService.IsConnected)
        {
            return _discordService;
        }

        return _discordBotManager.Primary ?? (_discordService.IsConnected ? _discordService : null);
    }

    /// <summary>
    /// Guild the role action targets: unambiguous when the bot is in exactly
    /// one server; otherwise the server that owns the triggering channel
    /// (matched by channel name, since CommandContext carries names).
    /// </summary>
    private static DiscordGuildInfo? ResolveGuild(IDiscordService bot, string? channelName)
    {
        var guilds = bot.ConnectedGuilds;

        if (guilds.Count == 1)
        {
            return guilds[0];
        }

        if (!string.IsNullOrWhiteSpace(channelName))
        {
            return guilds.FirstOrDefault(g => g.TextChannels.Any(c =>
                string.Equals(c.Name, channelName, StringComparison.OrdinalIgnoreCase)));
        }

        return null;
    }

    /// <summary>
    /// Replaces {variables} in a step's text using the same context variables
    /// the command engine supplies (username, platform, channel, balance,
    /// role plus any custom variables).
    /// </summary>
    private async Task<string> ReplaceVariablesAsync(string text, CommandContext ctx)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["username"] = ctx.Username,
            ["platform"] = ctx.Platform,
            ["channel"] = ctx.Channel,
            ["balance"] = ctx.CurrencyBalance.ToString(CultureInfo.InvariantCulture),
            ["role"] = UserRole(ctx)
        };

        foreach (var kvp in ctx.CustomVariables)
        {
            variables[kvp.Key] = kvp.Value;
        }

        return await _variableReplacementService
            .ReplaceVariablesAsync(text, variables, ctx.Platform)
            .ConfigureAwait(false);
    }

    private static string UserRole(CommandContext ctx)
    {
        if (ctx.IsBroadcaster) return "Broadcaster";
        if (ctx.IsModerator) return "Moderator";
        if (ctx.IsVIP) return "VIP";
        if (ctx.IsSubscriber) return "Subscriber";
        return "Viewer";
    }

    // ----------------------------------------------------------------------
    // Step JSON
    // ----------------------------------------------------------------------

    private sealed record ActionStep(string Type, string? Text, int Seconds, string? RoleId, int Amount);

    /// <summary>
    /// Tolerantly parses the ActionsJson array; malformed JSON or a non-array
    /// root yields no steps.
    /// </summary>
    private static List<ActionStep> ParseSteps(string? actionsJson)
    {
        var steps = new List<ActionStep>();

        if (string.IsNullOrWhiteSpace(actionsJson))
        {
            return steps;
        }

        try
        {
            using var doc = JsonDocument.Parse(actionsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return steps;
            }

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object
                    || !element.TryGetProperty("type", out var typeProp)
                    || typeProp.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var type = typeProp.GetString();
                if (string.IsNullOrWhiteSpace(type))
                {
                    continue;
                }

                string? text = GetString(element, "text");

                var seconds = 0;
                if (element.TryGetProperty("seconds", out var secondsProp)
                    && (secondsProp.ValueKind == JsonValueKind.Number || secondsProp.ValueKind == JsonValueKind.String)
                    && secondsProp.TryGetInt32(out var parsedSeconds))
                {
                    seconds = parsedSeconds;
                }

                var roleId = GetString(element, "roleId") ?? GetString(element, "role");

                var amount = 0;
                if (element.TryGetProperty("amount", out var amountProp)
                    && (amountProp.ValueKind == JsonValueKind.Number || amountProp.ValueKind == JsonValueKind.String)
                    && amountProp.TryGetInt32(out var parsedAmount))
                {
                    amount = parsedAmount;
                }

                steps.Add(new ActionStep(type, text, seconds, roleId, amount));
            }
        }
        catch (JsonException)
        {
            // Malformed sequence JSON: nothing runnable.
        }

        return steps;
    }

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
}
