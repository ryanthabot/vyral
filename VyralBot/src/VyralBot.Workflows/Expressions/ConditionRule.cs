using System.Text.Json.Serialization;

namespace VyralBot.Workflows.Expressions;

/// <summary>Type keys of a <see cref="ConditionRule"/> ("type" discriminator plus combinator keys).</summary>
public static class ConditionRuleTypes
{
    /// <summary>Combinator: true when every child rule is true.</summary>
    public const string All = "all";

    /// <summary>Combinator: true when at least one child rule is true.</summary>
    public const string Any = "any";

    /// <summary>Combinator: true when the single child rule is false.</summary>
    public const string Not = "not";

    /// <summary>Leaf: the triggering user holds a role ("roleId" = role name or id).</summary>
    public const string UserHasRole = "user_has_role";

    /// <summary>Leaf: the trigger message contains text ("text", "caseSensitive").</summary>
    public const string MessageContains = "message_contains";

    /// <summary>Leaf: an execution variable equals a value ("variableName", "value").</summary>
    public const string VariableEquals = "variable_equals";

    /// <summary>Leaf: an execution variable is set ("variableName").</summary>
    public const string VariableExists = "variable_exists";

    /// <summary>Leaf: the current local hour is within a range ("startHour", "endHour"; wraps around midnight).</summary>
    public const string TimeBetween = "time_between";

    /// <summary>Leaf: the triggering user is a bot account (checks the "isBot" variable).</summary>
    public const string UserIsBot = "user_is_bot";

    /// <summary>Leaf: the trigger channel matches ("channelName").</summary>
    public const string ChannelIs = "channel_is";
}

/// <summary>
/// One node of a structured condition rule (the rule builder's output, stored as
/// JSON in a node's "ConditionRule" config and evaluated by
/// <see cref="ConditionRuleEvaluator"/>). A rule is either a combinator
/// ("all"/"any"/"not" — which recurse) or a typed leaf ("type" set to one of the
/// <see cref="ConditionRuleTypes"/> leaf constants).
/// NOTE: custom_code / javascript rules are explicitly out of scope for v1.
/// </summary>
public class ConditionRule
{
    /// <summary>Leaf type (one of the <see cref="ConditionRuleTypes"/> leaf constants). Null for pure combinators.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>"all" combinator: true when every child rule is true.</summary>
    [JsonPropertyName("all")]
    public List<ConditionRule>? All { get; set; }

    /// <summary>"any" combinator: true when at least one child rule is true.</summary>
    [JsonPropertyName("any")]
    public List<ConditionRule>? Any { get; set; }

    /// <summary>"not" combinator: true when the child rule is false.</summary>
    [JsonPropertyName("not")]
    public ConditionRule? Not { get; set; }

    /// <summary>Role name or id (<see cref="ConditionRuleTypes.UserHasRole"/>).</summary>
    [JsonPropertyName("roleId")]
    public string? RoleId { get; set; }

    /// <summary>Text to look for (<see cref="ConditionRuleTypes.MessageContains"/>).</summary>
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary>Case-sensitive matching (<see cref="ConditionRuleTypes.MessageContains"/>).</summary>
    [JsonPropertyName("caseSensitive")]
    public bool CaseSensitive { get; set; }

    /// <summary>Variable name (<see cref="ConditionRuleTypes.VariableEquals"/> / <see cref="ConditionRuleTypes.VariableExists"/>).</summary>
    [JsonPropertyName("variableName")]
    public string? VariableName { get; set; }

    /// <summary>Expected value ({variable} substitution applies; <see cref="ConditionRuleTypes.VariableEquals"/>).</summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    /// <summary>Start hour 0-23, inclusive (<see cref="ConditionRuleTypes.TimeBetween"/>).</summary>
    [JsonPropertyName("startHour")]
    public int StartHour { get; set; }

    /// <summary>End hour 0-23, inclusive; may be lower than start to wrap around midnight (<see cref="ConditionRuleTypes.TimeBetween"/>).</summary>
    [JsonPropertyName("endHour")]
    public int EndHour { get; set; }

    /// <summary>Expected channel name (<see cref="ConditionRuleTypes.ChannelIs"/>).</summary>
    [JsonPropertyName("channelName")]
    public string? ChannelName { get; set; }
}
