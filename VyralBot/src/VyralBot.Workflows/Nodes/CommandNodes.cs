using Microsoft.Extensions.DependencyInjection;
using VyralBot.Core.Services.Commands;
using VyralBot.Workflows.Models;
using VyralBot.Workflows.Runtime;
using ExecutionContext = VyralBot.Workflows.Runtime.ExecutionContext;

namespace VyralBot.Workflows.Nodes;

/// <summary>
/// Runs a legacy built-in command through the Core
/// <see cref="ICommandService"/> (the Commands table engine) from inside a
/// flow. Config: "Name" (required, command name with or without prefix),
/// "Args" (default "{args}" — everything after the trigger command name) and
/// "OutputVariable" (default "builtin_response").
///
/// Variable contract: the node builds the invocation "!Name Args" (variables
/// substituted), seeds a <see cref="CommandContext"/> from the execution —
/// username/platform/channel directly, the moderation/role flags from the
/// pipeline-seeded "userRoles"/"isModerator" variables (the same
/// moderator-derivation the permission option uses) and every execution
/// variable as a custom variable so response templates like {username} or
/// {args} still resolve — and stores the command's reply text into the
/// {"OutputVariable"} execution variable for later nodes. A null reply
/// (unknown command, cooldown, missing permission, disabled row) fails the
/// node; action-marker replies fail with a dedicated message (action
/// sequences cannot run inside a flow).
/// </summary>
public class BuiltinCommandNode : INode
{
    /// <summary>Variable the command's reply is stored into by default.</summary>
    public const string DefaultOutputVariable = "builtin_response";

    public string Type => "builtin";
    public string Category => "Commands";
    public string Title => "Run Built-in Command";

    public async Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx)
    {
        var commandService = ctx.Services.GetService<ICommandService>();
        if (commandService is null)
            return NodeResult.Fail("ICommandService is not available");

        var name = ctx.Substitute(NodeConfigLookup.Get(node, "Name")).Trim();
        if (name.Length == 0)
            return NodeResult.Fail("No command name configured");

        var args = ctx.Substitute(NodeConfigLookup.GetOrDefault(node, "{args}", "Args"));
        var invocation = args.Length > 0 ? "!" + name + " " + args : "!" + name;

        var context = BuildCommandContext(ctx);

        string? response;
        try
        {
            response = await commandService.ExecuteAsync(invocation, context, ctx.BotId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return NodeResult.Fail($"Built-in '{name}' threw: {ex.Message}");
        }

        if (response is null)
            return NodeResult.Fail($"Built-in '{name}' unavailable or not permitted");

        if (response.StartsWith(ICommandService.ActionsMarkerPrefix, StringComparison.Ordinal))
            return NodeResult.Fail("Action-based custom commands cannot run inside flows");

        var outputVariable = ctx.Substitute(NodeConfigLookup.GetOrDefault(node, DefaultOutputVariable, "OutputVariable")).Trim();
        if (outputVariable.Length == 0)
            outputVariable = DefaultOutputVariable;

        ctx.SetVariable(outputVariable, response);
        return NodeResult.Success(response);
    }

    /// <summary>
    /// Builds a Core <see cref="CommandContext"/> from the execution: identity
    /// fields directly, role flags derived from the pipeline-seeded
    /// "userRoles"/"isModerator" variables (exactly the permission option's
    /// derivation), the balance from "balance" when numeric, and every
    /// execution variable forwarded as a custom variable so response
    /// templates keep resolving.
    /// </summary>
    private static CommandContext BuildCommandContext(ExecutionContext ctx)
    {
        var rolesLower = (ctx.GetVariable("userRoles") ?? string.Empty).ToLowerInvariant();
        var isModeratorVariable = string.Equals(ctx.GetVariable("isModerator"), "true", StringComparison.OrdinalIgnoreCase);

        var balance = 0;
        var balanceRaw = ctx.GetVariable("balance");
        if (!string.IsNullOrWhiteSpace(balanceRaw))
        {
            int.TryParse(balanceRaw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out balance);
        }

        var context = new CommandContext
        {
            Username = ctx.Username,
            Platform = ctx.Platform,
            Channel = ctx.Channel,
            IsModerator = isModeratorVariable
                || rolesLower.Contains("moderator")
                || rolesLower.Contains("broadcaster"),
            IsBroadcaster = rolesLower.Contains("broadcaster"),
            IsVIP = rolesLower.Contains("vip"),
            IsSubscriber = rolesLower.Contains("subscriber"),
            CurrencyBalance = balance,
            DiscordBotId = ctx.BotId
        };

        foreach (var pair in ctx.Variables)
        {
            context.CustomVariables[pair.Key] = pair.Value;
        }

        return context;
    }
}

/// <summary>
/// Moderates a member through the <see cref="IDiscordWorkflowBridge"/>
/// (Discord only). Config: "Action" ("Kick", "Ban", "Unban", "Timeout",
/// "Remove Timeout", "Purge" or "Slowmode"), "User" (defaults to the
/// triggering user; unused by Purge/Slowmode), "Minutes" (Timeout, default
/// 10), "Count" (Purge, default 10), "Seconds" (Slowmode, default 10) and
/// "Reason". Success outputs the action name — a confirmation message, when
/// wanted, is simply the author's next node.
/// </summary>
public class ModerateMemberNode : INode
{
    /// <summary>Default timeout minutes when "Minutes" is unset.</summary>
    public const int DefaultTimeoutMinutes = 10;

    /// <summary>Default purge count when "Count" is unset.</summary>
    public const int DefaultPurgeCount = 10;

    /// <summary>Default slowmode seconds when "Seconds" is unset.</summary>
    public const int DefaultSlowmodeSeconds = 10;

    public string Type => "moderate";
    public string Category => "Discord";
    public string Title => "Moderate Member";

    public Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx)
    {
        var bridge = DiscordNodeHelper.ResolveBridge(ctx);
        if (bridge is null)
            return Task.FromResult(NodeResult.Fail("Discord bridge unavailable"));

        var action = ctx.Substitute(NodeConfigLookup.Get(node, "Action")).Trim();
        if (action.Length == 0)
            return Task.FromResult(NodeResult.Fail("No moderation action configured"));

        var reason = ctx.Substitute(NodeConfigLookup.Get(node, "Reason"));
        if (string.IsNullOrWhiteSpace(reason))
            reason = $"Workflow: moderate {action} node";

        switch (action.ToLowerInvariant())
        {
            case "kick":
                return ModerateUserAsync(node, ctx, "Kick",
                    username => bridge.KickUserAsync(ctx.BotId, username, reason));

            case "ban":
                return ModerateUserAsync(node, ctx, "Ban",
                    username => bridge.BanUserAsync(ctx.BotId, username, reason));

            case "unban":
                return ModerateUserAsync(node, ctx, "Unban",
                    username => bridge.UnbanUserAsync(ctx.BotId, username));

            case "timeout":
            {
                var minutes = ParseNumber(node, ctx, "Minutes", DefaultTimeoutMinutes);
                if (minutes is null)
                    return Task.FromResult(FailUnparseable(node, ctx, "Minutes"));

                return ModerateUserAsync(node, ctx, "Timeout",
                    username => bridge.TimeoutUserAsync(ctx.BotId, username, minutes.Value, reason));
            }

            case "remove timeout" or "removetimeout":
                return ModerateUserAsync(node, ctx, "Remove Timeout",
                    username => bridge.RemoveTimeoutUserAsync(ctx.BotId, username));

            case "purge":
            {
                var count = ParseNumber(node, ctx, "Count", DefaultPurgeCount);
                if (count is null)
                    return Task.FromResult(FailUnparseable(node, ctx, "Count"));

                return DiscordNodeHelper.InvokeAsync(
                    $"Purge channel '{ctx.Channel}'",
                    () => bridge.PurgeChannelAsync(ctx.BotId, count.Value),
                    count.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            case "slowmode":
            {
                var seconds = ParseNumber(node, ctx, "Seconds", DefaultSlowmodeSeconds);
                if (seconds is null)
                    return Task.FromResult(FailUnparseable(node, ctx, "Seconds"));

                return DiscordNodeHelper.InvokeAsync(
                    $"Set slowmode on channel '{ctx.Channel}'",
                    () => bridge.SetSlowmodeAsync(ctx.BotId, seconds.Value),
                    seconds.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            default:
                return Task.FromResult(NodeResult.Fail($"Unknown moderation action '{action}'"));
        }
    }

    /// <summary>Builds the shared "cannot parse" failure for a numeric config key.</summary>
    private static NodeResult FailUnparseable(WorkflowNode node, ExecutionContext ctx, string key) =>
        NodeResult.Fail($"Cannot parse '{key}' value '{ctx.Substitute(NodeConfigLookup.Get(node, key))}' as an integer");

    /// <summary>Runs a user-targeted moderation: resolves the username then invokes the bridge call.</summary>
    private static Task<NodeResult> ModerateUserAsync(
        WorkflowNode node, ExecutionContext ctx, string label,
        Func<string, Task<bool>> call)
    {
        var username = DiscordNodeHelper.ResolveUsername(node, ctx);
        if (username.Length == 0)
            return Task.FromResult(NodeResult.Fail($"No username (node config or triggering user) to {label.ToLowerInvariant()}"));

        return DiscordNodeHelper.InvokeAsync(
            $"{label} '{username}'",
            () => call(username),
            label);
    }

    /// <summary>
    /// Parses a ({variable}-substituted) integer config value; the fallback
    /// when the value is blank, null when it is present but unparseable.
    /// </summary>
    private static int? ParseNumber(WorkflowNode node, ExecutionContext ctx, string key, int fallback)
    {
        var raw = ctx.Substitute(NodeConfigLookup.Get(node, key));
        if (raw.Trim().Length == 0)
            return fallback;

        return int.TryParse(raw, out var parsed)
            ? parsed
            : null;
    }
}
