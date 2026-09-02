using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using VyralBot.Workflows.Runtime;
using ExecutionContext = VyralBot.Workflows.Runtime.ExecutionContext;

namespace VyralBot.Workflows.Expressions;

/// <summary>
/// Evaluates structured condition rules (see <see cref="ConditionRule"/>) against an
/// execution context. Combinators ("all"/"any"/"not") recurse; leaves read execution
/// variables and context fields ("userRoles", "isBot", the message, the channel).
/// Never throws: parse errors and unknown rule types evaluate to false and are
/// logged to the execution logger.
/// NOTE: custom_code / javascript rules are explicitly out of scope for v1.
/// </summary>
public static class ConditionRuleEvaluator
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    /// <summary>
    /// Convenience wrapper: parses the rule JSON and evaluates it. Returns false on
    /// parse errors (logged) — an invalid rule never blocks a workflow.
    /// </summary>
    public static bool Evaluate(string? ruleJson, ExecutionContext ctx)
    {
        if (ctx is null)
            return false;

        if (string.IsNullOrWhiteSpace(ruleJson))
        {
            ctx.Logger.LogWarning("Condition rule is empty — evaluating to false");
            return false;
        }

        if (!TryParse(ruleJson, out var rule) || rule is null)
        {
            ctx.Logger.LogWarning("Condition rule is not valid rule JSON ({Snippet}) — evaluating to false", Snippet(ruleJson));
            return false;
        }

        return Evaluate(rule, ctx);
    }

    /// <summary>
    /// Parses rule JSON without logging. False when the text is not a JSON rule
    /// (callers use this to tell "not a rule" apart from "the rule said false").
    /// </summary>
    public static bool TryParse(string? ruleJson, out ConditionRule? rule)
    {
        rule = null;
        if (string.IsNullOrWhiteSpace(ruleJson))
            return false;

        try
        {
            rule = JsonSerializer.Deserialize<ConditionRule>(ruleJson, SerializerOptions);
            return rule is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Evaluates an already-parsed rule. Never throws; errors evaluate to false.</summary>
    public static bool Evaluate(ConditionRule? rule, ExecutionContext ctx)
    {
        if (ctx is null || rule is null)
            return false;

        try
        {
            return EvaluateCore(rule, ctx);
        }
        catch (Exception ex)
        {
            ctx.Logger.LogWarning(ex, "Condition rule evaluation crashed — evaluating to false");
            return false;
        }
    }

    /// <summary>
    /// Shared hour-range check used by the time_between rule and the if_time_between
    /// node: hours wrap around midnight (22 → 6 covers 22:00 through 06:59), both
    /// bounds are inclusive and an equal start/end means the whole day. Values
    /// outside 0-23 are normalized (modulo 24).
    /// </summary>
    public static bool IsHourBetween(int startHour, int endHour, int currentHour)
    {
        startHour = ((startHour % 24) + 24) % 24;
        endHour = ((endHour % 24) + 24) % 24;
        currentHour = ((currentHour % 24) + 24) % 24;

        if (startHour == endHour)
            return true; // degenerate range: the whole day

        return startHour < endHour
            ? currentHour >= startHour && currentHour <= endHour
            : currentHour >= startHour || currentHour <= endHour; // wraps around midnight
    }

    /// <summary>Recursively evaluates one rule node (combinators recurse, leaves test the context).</summary>
    private static bool EvaluateCore(ConditionRule rule, ExecutionContext ctx)
    {
        // Combinators (a non-empty child list wins over the leaf type).
        if (rule.All is { Count: > 0 })
        {
            foreach (var child in rule.All)
            {
                if (!Evaluate(child, ctx))
                    return false; // short-circuit AND
            }
            return true;
        }

        if (rule.Any is { Count: > 0 })
        {
            foreach (var child in rule.Any)
            {
                if (Evaluate(child, ctx))
                    return true; // short-circuit OR
            }
            return false;
        }

        if (rule.Not is not null)
            return !Evaluate(rule.Not, ctx);

        // Leaves.
        switch ((rule.Type ?? string.Empty).Trim().ToLowerInvariant())
        {
            case ConditionRuleTypes.UserHasRole:
                return EvaluateUserHasRole(rule, ctx);

            case ConditionRuleTypes.MessageContains:
                return EvaluateMessageContains(rule, ctx);

            case ConditionRuleTypes.VariableEquals:
                return EvaluateVariableEquals(rule, ctx);

            case ConditionRuleTypes.VariableExists:
                return EvaluateVariableExists(rule, ctx);

            case ConditionRuleTypes.TimeBetween:
                return IsHourBetween(rule.StartHour, rule.EndHour, DateTime.Now.Hour);

            case ConditionRuleTypes.UserIsBot:
                return string.Equals((ctx.GetVariable("isBot") ?? string.Empty).Trim(), "true", StringComparison.OrdinalIgnoreCase);

            case ConditionRuleTypes.ChannelIs:
                return EvaluateChannelIs(rule, ctx);

            default:
                ctx.Logger.LogWarning("Unknown condition rule type '{RuleType}' — evaluating to false", rule.Type ?? "(none)");
                return false;
        }
    }

    /// <summary>Checks the comma-separated "userRoles" variable (seeded by the chat pipeline) case-insensitively.</summary>
    private static bool EvaluateUserHasRole(ConditionRule rule, ExecutionContext ctx)
    {
        var role = (rule.RoleId ?? string.Empty).Trim();
        if (role.Length == 0)
            return false;

        var rolesRaw = ctx.GetVariable("userRoles");
        if (rolesRaw is null)
        {
            // Not seeded (e.g. interaction triggers) — treated as no membership.
            ctx.Logger.LogDebug("Condition rule user_has_role: 'userRoles' variable is not set — evaluating to false");
            return false;
        }

        foreach (var name in rolesRaw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(name, role, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Substring test on the trigger message; empty text never matches.</summary>
    private static bool EvaluateMessageContains(ConditionRule rule, ExecutionContext ctx)
    {
        var text = rule.Text ?? string.Empty;
        if (text.Length == 0)
            return false;

        var message = ctx.Message ?? string.Empty;
        return rule.CaseSensitive
            ? message.Contains(text, StringComparison.Ordinal)
            : message.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Compares an execution variable with a ({variable}-substituted) value, case-insensitively.</summary>
    private static bool EvaluateVariableEquals(ConditionRule rule, ExecutionContext ctx)
    {
        var name = (rule.VariableName ?? string.Empty).Trim();
        if (name.Length == 0)
            return false;

        var actual = ctx.GetVariable(name) ?? string.Empty;
        var expected = ctx.Substitute(rule.Value ?? string.Empty);
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the variable exists (an empty-string value still counts as existing).</summary>
    private static bool EvaluateVariableExists(ConditionRule rule, ExecutionContext ctx)
    {
        var name = (rule.VariableName ?? string.Empty).Trim();
        if (name.Length == 0)
            return false;

        return ctx.GetVariable(name) is not null;
    }

    /// <summary>Compares the trigger channel with a ({variable}-substituted) name, case-insensitively.</summary>
    private static bool EvaluateChannelIs(ConditionRule rule, ExecutionContext ctx)
    {
        var expected = ctx.Substitute(rule.ChannelName ?? string.Empty).Trim();
        if (expected.Length == 0)
            return false;

        return string.Equals((ctx.Channel ?? string.Empty).Trim(), expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Shortens a rule JSON snippet for log messages.</summary>
    private static string Snippet(string ruleJson)
    {
        var trimmed = ruleJson.Trim();
        return trimmed.Length <= 80 ? trimmed : trimmed[..80] + "…";
    }
}
