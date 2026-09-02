using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using VyralBot.Workflows.Abstractions;
using VyralBot.Workflows.Expressions;
using VyralBot.Workflows.Models;
using VyralBot.Workflows.Runtime;
using ExecutionContext = VyralBot.Workflows.Runtime.ExecutionContext;

namespace VyralBot.Workflows.Nodes;

/// <summary>Shared helpers for the condition node family.</summary>
internal static class ConditionNodeHelper
{
    /// <summary>Lenient boolean config parse ("true"/"yes"/"1", case-insensitive; anything else is false).</summary>
    public static bool IsTrue(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var trimmed = raw.Trim();
        return trimmed.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               trimmed == "1";
    }
}

/// <summary>
/// Condition node: branches on Discord role membership. Config: "Role" (role name
/// or id), optional "Username" (defaults to the triggering user). Checks the
/// comma-separated "userRoles" variable seeded by the chat pipeline; when that
/// variable is not set (e.g. interaction triggers), falls back to asking the
/// <see cref="IDiscordWorkflowBridge"/> directly. Outputs "true"/"false".
/// </summary>
public class IfUserHasRoleNode : INode
{
    public string Type => "if_user_has_role";
    public string Category => "Logic";
    public string Title => "If User Has Role";

    public async Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx)
    {
        var role = ctx.Substitute(NodeConfigLookup.Get(node, "Role")).Trim();
        if (role.Length == 0)
            return NodeResult.Fail("No role configured");

        // Preferred path: the pipeline-seeded role list for the triggering user.
        var rolesRaw = ctx.GetVariable("userRoles");
        if (rolesRaw is not null)
        {
            foreach (var name in rolesRaw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.Equals(name, role, StringComparison.OrdinalIgnoreCase))
                    return NodeResult.Success("true");
            }
            return NodeResult.Success("false");
        }

        // Fallback: query the Discord bridge for the (possibly overridden) user.
        var username = ctx.Substitute(NodeConfigLookup.Get(node, "Username")).Trim();
        if (username.Length == 0)
            username = ctx.Username.Trim();
        if (username.Length == 0)
            return NodeResult.Fail("No username (node config or triggering user) to check roles for");

        var bridge = ctx.Services.GetService<IDiscordWorkflowBridge>();
        if (bridge is null)
            return NodeResult.Fail("Cannot check roles: the 'userRoles' variable is not set and no Discord bridge is available");

        try
        {
            var hasRole = await bridge.UserHasRoleAsync(ctx.BotId, username, role).ConfigureAwait(false);
            return NodeResult.Success(hasRole ? "true" : "false");
        }
        catch (Exception ex)
        {
            return NodeResult.Fail($"Role check for '{username}' failed: {ex.Message}");
        }
    }
}

/// <summary>
/// Condition node: branches on the trigger message text. Config: "Text"
/// ({variable}-substituted), optional "CaseSensitive" ("true"/"false").
/// Outputs "true"/"false".
/// </summary>
public class IfMessageContainsNode : INode
{
    public string Type => "if_message_contains";
    public string Category => "Logic";
    public string Title => "If Message Contains";

    public Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx)
    {
        var text = ctx.Substitute(NodeConfigLookup.Get(node, "Text"));
        if (text.Length == 0)
            return Task.FromResult(NodeResult.Fail("No text configured"));

        var message = ctx.Message ?? string.Empty;
        var caseSensitive = ConditionNodeHelper.IsTrue(NodeConfigLookup.Get(node, "CaseSensitive"));

        var found = caseSensitive
            ? message.Contains(text, StringComparison.Ordinal)
            : message.Contains(text, StringComparison.OrdinalIgnoreCase);

        return Task.FromResult(NodeResult.Success(found ? "true" : "false"));
    }
}

/// <summary>
/// Condition node: branches on a stored variable's value. Config: "Name",
/// "Value" ({variable}-substituted; compared case-insensitively).
/// Outputs "true"/"false".
/// </summary>
public class IfVariableEqualsNode : INode
{
    public string Type => "if_variable_equals";
    public string Category => "Logic";
    public string Title => "If Variable Equals";

    public Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx)
    {
        var name = NodeConfigLookup.Get(node, "Name").Trim();
        if (name.Length == 0)
            return Task.FromResult(NodeResult.Fail("No variable name configured"));

        var actual = ctx.GetVariable(name) ?? string.Empty;
        var expected = ctx.Substitute(NodeConfigLookup.Get(node, "Value"));
        var equals = string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

        return Task.FromResult(NodeResult.Success(equals ? "true" : "false"));
    }
}

/// <summary>
/// Condition node: branches on the current local hour. Config: "StartHour",
/// "EndHour" (0-23, inclusive; a start after the end wraps around midnight,
/// e.g. 22 → 6). Outputs "true"/"false".
/// </summary>
public class IfTimeBetweenNode : INode
{
    public string Type => "if_time_between";
    public string Category => "Logic";
    public string Title => "If Time Between";

    public Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx)
    {
        var startRaw = ctx.Substitute(NodeConfigLookup.GetOrDefault(node, "22", "StartHour"));
        var endRaw = ctx.Substitute(NodeConfigLookup.GetOrDefault(node, "6", "EndHour"));

        if (!int.TryParse(startRaw, out var startHour))
            return Task.FromResult(NodeResult.Fail($"Cannot parse 'StartHour' value '{startRaw}' as an integer"));

        if (!int.TryParse(endRaw, out var endHour))
            return Task.FromResult(NodeResult.Fail($"Cannot parse 'EndHour' value '{endRaw}' as an integer"));

        var inRange = ConditionRuleEvaluator.IsHourBetween(startHour, endHour, DateTime.Now.Hour);
        return Task.FromResult(NodeResult.Success(inRange ? "true" : "false"));
    }
}

/// <summary>
/// Condition node: branches on whether the triggering user is a bot account
/// (checks the "isBot" variable seeded by the chat pipeline). No config.
/// Outputs "true"/"false".
/// </summary>
public class IfUserIsBotNode : INode
{
    public string Type => "if_user_is_bot";
    public string Category => "Logic";
    public string Title => "If User Is Bot";

    public Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx)
    {
        var isBot = string.Equals((ctx.GetVariable("isBot") ?? string.Empty).Trim(), "true", StringComparison.OrdinalIgnoreCase);
        return Task.FromResult(NodeResult.Success(isBot ? "true" : "false"));
    }
}

/// <summary>
/// Condition node (BotGhost "Chance Condition"): rolls a random chance and
/// branches on it. Config: "Percent" (variable-substituted, 0-100; invalid or
/// missing values fall back to 50). Outputs "true" when the roll lands at or
/// below the configured percentage, "false" otherwise — the runtime's existing
/// output-driven routing takes BranchTrue/BranchFalse (see WorkflowRuntime).
/// </summary>
public class ChanceNode : INode
{
    private static readonly Random Random = new();

    public string Type => "chance";
    public string Category => "Logic";
    public string Title => "Chance Condition";

    public Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx)
    {
        var raw = ctx.Substitute(NodeConfigLookup.GetOrDefault(node, "50", "Percent"));

        if (!double.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var percent))
            percent = 50;

        percent = Math.Clamp(percent, 0, 100);

        double roll;
        lock (Random)
        {
            roll = Random.NextDouble() * 100.0; // [0, 100): 0% never passes, 100% always passes
        }

        return Task.FromResult(NodeResult.Success(roll < percent ? "true" : "false"));
    }
}

/// <summary>
/// Condition node (BotGhost "Comparison Condition"): compares
/// ({variable}-substituted) values. Two modes:
/// <list type="bullet">
/// <item>
/// Multi-rule (BotGhost-style): Config "Comparisons" holds a JSON array of
/// rules, e.g.
/// <c>[{"left":"10","operator":"eq","right":"5"},{"left":"{args}","operator":"contains","right":"hi"}]</c>
/// — an EMPTY array ("[]") is also multi-rule mode, just with zero rules.
/// Rules are evaluated IN ORDER with the shared 14-operator evaluator
/// (<see cref="EvaluateRule"/>) and the FIRST match wins: the output becomes
/// that rule's case key <c>"case:{i}"</c>, which the runtime routes through the
/// node's Branches["case:{i}"] entry (wired to the "comparison_case" attachment
/// cards). No rule matching outputs "else" (routed via Branches["else"], the
/// "comparison_else" card, falling back to BranchFalse/Next) — with zero rules
/// that is EVERY execution, so a freshly placed comparison always runs its
/// Else chain until the user adds rules.
/// </item>
/// <item>
/// Legacy single-rule (Config "Left" (default "{args}"), "Operator", "Right",
/// i.e. when "Comparisons" is absent/blank/malformed/not an array): outputs
/// "true"/"false" exactly as before.
/// </item>
/// </list>
/// Operators (canonical, as stored by the editor):
/// lt, lte, gt, gte, eq, neq, contains, not_contains, starts_with, ends_with,
/// not_starts_with, not_ends_with, collection_contains, collection_not_contains.
/// Legacy stored symbols are mapped for backward compatibility (case-insensitive,
/// trimmed): "==" or "equal" → eq, "!=" → neq, "&gt;" → gt, "&lt;" → lt,
/// "&gt;=" → gte, "&lt;=" → lte (the old "Contains" value normalizes to
/// contains). Semantics: lt/lte/gt/gte/eq/neq compare numerically
/// (InvariantCulture) when BOTH sides parse as numbers, otherwise as an ordinal
/// ignore-case string comparison; contains/not_contains are ordinal ignore-case
/// substring checks; starts_with/ends_with (and their negations) are ordinal
/// ignore-case prefix/suffix checks; collection_contains/collection_not_contains
/// treat LEFT as a comma-separated collection and RIGHT as the item (ordinal
/// ignore-case membership). Unknown operators fall back to eq.
/// </summary>
public class ComparisonNode : INode
{
    public string Type => "comparison";
    public string Category => "Logic";
    public string Title => "Comparison Condition";

    /// <summary>Normalizes a stored operator to the canonical set; unknown → eq. Shared with the catalog's rule summaries.</summary>
    public static string NormalizeOperator(string raw)
    {
        var op = raw.Trim().ToLowerInvariant();
        return op switch
        {
            // Legacy stored symbols for backward compatibility with old saves.
            "==" or "equal" => "eq",
            "!=" => "neq",
            ">" => "gt",
            "<" => "lt",
            ">=" => "gte",
            "<=" => "lte",

            // Canonical operator set (matches the catalog's Select choices).
            "lt" or "lte" or "gt" or "gte" or "eq" or "neq" or
            "contains" or "not_contains" or
            "starts_with" or "ends_with" or "not_starts_with" or "not_ends_with" or
            "collection_contains" or "collection_not_contains" => op,

            _ => "eq" // unknown/empty operators compare for equality
        };
    }

    /// <summary>
    /// Evaluates one comparison rule with the shared 14-operator semantics
    /// (see the <see cref="ComparisonNode"/> doc comment). <paramref name="op"/>
    /// may be any stored spelling; it is normalized first.
    /// </summary>
    public static bool EvaluateRule(string left, string op, string right)
    {
        op = NormalizeOperator(op);

        // Ordering operators (and equality as their special case): numeric when
        // both sides parse (InvariantCulture), otherwise ordinal ignore-case.
        if (op is "lt" or "lte" or "gt" or "gte" or "eq" or "neq")
        {
            if (double.TryParse(left, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands,
                    System.Globalization.CultureInfo.InvariantCulture, out var leftNumber) &&
                double.TryParse(right, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands,
                    System.Globalization.CultureInfo.InvariantCulture, out var rightNumber))
            {
                return op switch
                {
                    "lt" => leftNumber < rightNumber,
                    "lte" => leftNumber <= rightNumber,
                    "gt" => leftNumber > rightNumber,
                    "gte" => leftNumber >= rightNumber,
                    "neq" => leftNumber != rightNumber,
                    _ => leftNumber == rightNumber // eq
                };
            }

            var comparison = string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
            return op switch
            {
                "lt" => comparison < 0,
                "lte" => comparison <= 0,
                "gt" => comparison > 0,
                "gte" => comparison >= 0,
                "neq" => comparison != 0,
                _ => comparison == 0 // eq
            };
        }

        switch (op)
        {
            case "contains":
                return left.Contains(right, StringComparison.OrdinalIgnoreCase);
            case "not_contains":
                return !left.Contains(right, StringComparison.OrdinalIgnoreCase);
            case "starts_with" or "not_starts_with":
            {
                var matched = left.StartsWith(right, StringComparison.OrdinalIgnoreCase);
                return op == "starts_with" ? matched : !matched;
            }
            case "ends_with" or "not_ends_with":
            {
                var matched = left.EndsWith(right, StringComparison.OrdinalIgnoreCase);
                return op == "ends_with" ? matched : !matched;
            }
            case "collection_contains" or "collection_not_contains":
            {
                // LEFT is the comma-separated collection, RIGHT the item to look up.
                var found = left.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Contains(right, StringComparer.OrdinalIgnoreCase);
                return op == "collection_contains" ? found : !found;
            }
            default:
                // Unreachable: NormalizeOperator always yields a handled operator.
                return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    public Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx)
    {
        // Multi-rule mode: "Comparisons" holds a JSON rule array (an EMPTY
        // array counts — zero rules simply never match). The first matching
        // rule (in order) wins, its index becomes "case:{i}"; no match outputs
        // "else" (see ResolveNextId in WorkflowRuntime).
        var rules = ParseComparisonRules(NodeConfigLookup.Get(node, "Comparisons"));
        if (rules is not null)
        {
            for (var i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                var ruleLeft = ctx.Substitute(rule.Left);
                var ruleRight = ctx.Substitute(rule.Right);
                if (EvaluateRule(ruleLeft, rule.Operator, ruleRight))
                    return Task.FromResult(NodeResult.Success($"case:{i}"));
            }

            return Task.FromResult(NodeResult.Success("else"));
        }

        // Legacy single-rule behavior (byte-for-byte as before multi-rule existed):
        // Left/Operator/Right → "true"/"false".
        var left = ctx.Substitute(NodeConfigLookup.GetOrDefault(node, "{args}", "Left"));
        var right = ctx.Substitute(NodeConfigLookup.Get(node, "Right"));
        var matched = EvaluateRule(left, NodeConfigLookup.Get(node, "Operator"), right);
        return Task.FromResult(NodeResult.Success(matched ? "true" : "false"));
    }

    /// <summary>One parsed rule of the "Comparisons" config array (raw, unsubstituted values).</summary>
    private sealed record ComparisonRule(string Left, string Operator, string Right);

    /// <summary>
    /// Parses the "Comparisons" config value: a JSON array of
    /// <c>{"left","operator","right"}</c> objects (property match is
    /// case-insensitive; non-string scalars are taken verbatim, non-object
    /// entries are skipped). Returns null (→ legacy single-rule mode) when the
    /// value is absent/blank, not valid JSON, or not an array. Returns the
    /// rule list — possibly EMPTY — when the key holds a valid JSON array: an
    /// empty array means multi-rule mode with zero rules, so every execution
    /// outputs "else" (this is how freshly placed comparisons behave until the
    /// user adds rules).
    /// </summary>
    private static List<ComparisonRule>? ParseComparisonRules(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return null;

            var rules = new List<ComparisonRule>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                    continue;

                rules.Add(new ComparisonRule(
                    GetStringProperty(element, "left"),
                    GetStringProperty(element, "operator"),
                    GetStringProperty(element, "right")));
            }

            // Empty list = multi-rule with zero rules → always "else"
            // (legacy single-rule mode is reserved for a MISSING/invalid key).
            return rules;
        }
        catch (JsonException)
        {
            return null; // invalid JSON → legacy single-rule behavior
        }
    }

    /// <summary>Reads one object property case-insensitively (strings verbatim, other scalars as raw text; missing → empty).</summary>
    private static string GetStringProperty(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                continue;

            return property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => property.Value.GetRawText(),
                _ => string.Empty
            };
        }

        return string.Empty;
    }
}

/// <summary>
/// Condition node (BotGhost "Permissions Condition"): branches on a Discord
/// permission of a user. Config: "Permission" (e.g. "Administrator",
/// "ManageMessages"), optional "Username" (defaults to the triggering user).
/// Resolves the <see cref="IDiscordWorkflowBridge"/>; a missing bridge fails the
/// node. Outputs "true"/"false".
/// </summary>
public class IfUserPermissionNode : INode
{
    public string Type => "if_user_permission";
    public string Category => "Logic";
    public string Title => "Permissions Condition";

    public async Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx)
    {
        var permission = ctx.Substitute(NodeConfigLookup.Get(node, "Permission")).Trim();
        if (permission.Length == 0)
            return NodeResult.Fail("No permission configured");

        var username = ctx.Substitute(NodeConfigLookup.Get(node, "Username")).Trim();
        if (username.Length == 0)
            username = ctx.Username.Trim();
        if (username.Length == 0)
            return NodeResult.Fail("No username (node config or triggering user) to check permissions for");

        var bridge = ctx.Services.GetService<IDiscordWorkflowBridge>();
        if (bridge is null)
            return NodeResult.Fail("Discord bridge unavailable");

        try
        {
            var hasPermission = await bridge.UserHasPermissionAsync(ctx.BotId, username, permission).ConfigureAwait(false);
            return NodeResult.Success(hasPermission ? "true" : "false");
        }
        catch (Exception ex)
        {
            return NodeResult.Fail($"Permission check for '{username}' failed: {ex.Message}");
        }
    }
}

/// <summary>
/// Condition node (BotGhost "Channel Condition"): branches on the channel the
/// workflow was triggered from. Config: "Channels" (comma-separated channel
/// names/ids; leading '#' tolerated; ordinal ignore-case). Outputs
/// "true"/"false".
/// </summary>
public class IfChannelNode : INode
{
    public string Type => "if_channel";
    public string Category => "Logic";
    public string Title => "Channel Condition";

    public Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx)
    {
        var candidates = ctx.Substitute(NodeConfigLookup.Get(node, "Channels"))
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (candidates.Length == 0)
            return Task.FromResult(NodeResult.Fail("No channels configured"));

        var current = (ctx.Channel ?? string.Empty).Trim().TrimStart('#');
        if (current.Length == 0)
            return Task.FromResult(NodeResult.Success("false"));

        foreach (var candidate in candidates)
        {
            if (string.Equals(candidate.TrimStart('#'), current, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(NodeResult.Success("true"));
        }

        return Task.FromResult(NodeResult.Success("false"));
    }
}

/// <summary>
/// Condition node (BotGhost "User Condition"): branches on who triggered the
/// workflow. Config: "Users" (comma-separated usernames; ordinal ignore-case).
/// Outputs "true"/"false".
/// </summary>
public class IfUserNode : INode
{
    public string Type => "if_user";
    public string Category => "Logic";
    public string Title => "User Condition";

    public Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx)
    {
        var candidates = ctx.Substitute(NodeConfigLookup.Get(node, "Users"))
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (candidates.Length == 0)
            return Task.FromResult(NodeResult.Fail("No users configured"));

        var current = (ctx.Username ?? string.Empty).Trim();
        if (current.Length == 0)
            return Task.FromResult(NodeResult.Success("false"));

        foreach (var candidate in candidates)
        {
            if (string.Equals(candidate, current, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(NodeResult.Success("true"));
        }

        return Task.FromResult(NodeResult.Success("false"));
    }
}
