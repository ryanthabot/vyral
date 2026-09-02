using System.Text.Json.Serialization;

namespace VyralBot.Core.Models.Rules;

/// <summary>
/// Types of conditions that can be evaluated
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ConditionType
{
    Value,      // Numeric/string comparisons (count == 100, username contains "test")
    Status,     // Boolean checks (is_subscriber, is_moderator, is_vip)
    Time,       // Time-based conditions (time_of_day, day_of_week)
    Complex     // Multiple conditions with AND/OR logic
}

/// <summary>
/// Operators for value comparisons
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ComparisonOperator
{
    Equals,             // ==
    NotEquals,          // !=
    GreaterThan,        // >
    LessThan,           // <
    GreaterOrEqual,     // >=
    LessOrEqual,        // <=
    Contains,           // String contains
    StartsWith,         // String starts with
    EndsWith,           // String ends with
    Modulo              // % (for milestone checks like count % 100 == 0)
}

/// <summary>
/// Logical operators for combining multiple conditions
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LogicalOperator
{
    And,
    Or
}

/// <summary>
/// Represents a single condition in a rule
/// </summary>
public class RuleCondition
{
    /// <summary>
    /// Type of condition
    /// </summary>
    public ConditionType Type { get; set; }

    /// <summary>
    /// Field name to evaluate (count, username, is_subscriber, etc.)
    /// </summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>
    /// Comparison operator to use
    /// </summary>
    public ComparisonOperator Operator { get; set; }

    /// <summary>
    /// Value to compare against (can be numeric or string)
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// For Modulo operator: the divisor value (e.g., 100 for "count % 100 == 0")
    /// </summary>
    public int? ModuloDivisor { get; set; }

    /// <summary>
    /// For complex conditions: nested conditions
    /// </summary>
    public List<RuleCondition>? SubConditions { get; set; }

    /// <summary>
    /// For complex conditions: how to combine sub-conditions
    /// </summary>
    public LogicalOperator? LogicalOperator { get; set; }
}
