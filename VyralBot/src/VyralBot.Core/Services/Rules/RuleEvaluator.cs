using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VyralBot.Core.Models.Rules;

namespace VyralBot.Core.Services.Rules;

public interface IRuleEvaluator
{
    /// <summary>
    /// Evaluates a rule condition against the provided context
    /// </summary>
    bool EvaluateCondition(RuleCondition condition, Dictionary<string, object> context);
}

public class RuleEvaluator : IRuleEvaluator
{
    private readonly ILogger<RuleEvaluator> _logger;

    public RuleEvaluator(ILogger<RuleEvaluator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool EvaluateCondition(RuleCondition condition, Dictionary<string, object> context)
    {
        try
        {
            // A null condition or an empty default condition (the "{}" JSON that
            // "New Rule"/"Clear All" produce, deserialized to Field="") means
            // "no condition" - the rule always matches.
            if (condition is null)
                return true;

            if (condition.Type != ConditionType.Complex && string.IsNullOrWhiteSpace(condition.Field))
                return true;

            return condition.Type switch
            {
                ConditionType.Value => EvaluateValueCondition(condition, context),
                ConditionType.Status => EvaluateStatusCondition(condition, context),
                ConditionType.Time => EvaluateTimeCondition(condition, context),
                ConditionType.Complex => EvaluateComplexCondition(condition, context),
                _ => false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to evaluate condition: {Field} {Operator} {Value}", 
                condition.Field, condition.Operator, condition.Value);
            return false;
        }
    }

    private bool EvaluateValueCondition(RuleCondition condition, Dictionary<string, object> context)
    {
        if (!context.TryGetValue(condition.Field, out var fieldValue))
        {
            _logger.LogWarning("Field {Field} not found in context", condition.Field);
            return false;
        }

        // Handle modulo operator specially. UI semantics: Value IS the divisor
        // (e.g. "count % 100 == 0" => Field=count, Operator=Modulo, Value=100),
        // with ModuloDivisor as an optional explicit override.
        if (condition.Operator == ComparisonOperator.Modulo)
        {
            var divisor = condition.ModuloDivisor ?? 0;
            if (divisor == 0 && !int.TryParse(condition.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out divisor))
            {
                _logger.LogWarning("Modulo condition for field {Field} has no valid divisor (Value='{Value}')",
                    condition.Field, condition.Value);
                return false;
            }

            if (divisor == 0)
            {
                _logger.LogWarning("Modulo condition for field {Field} has a zero divisor", condition.Field);
                return false;
            }

            if (!IsNumericType(fieldValue))
            {
                _logger.LogWarning("Modulo condition field {Field} value is not numeric", condition.Field);
                return false;
            }

            var numericFieldValue = Convert.ToDouble(fieldValue);
            return Math.Abs(numericFieldValue % divisor) < 0.0001;
        }

        // Try numeric comparison first
        if (IsNumericType(fieldValue) && double.TryParse(condition.Value, out var numericValue))
        {
            var actualValue = Convert.ToDouble(fieldValue);
            return condition.Operator switch
            {
                ComparisonOperator.Equals => Math.Abs(actualValue - numericValue) < 0.0001,
                ComparisonOperator.NotEquals => Math.Abs(actualValue - numericValue) >= 0.0001,
                ComparisonOperator.GreaterThan => actualValue > numericValue,
                ComparisonOperator.LessThan => actualValue < numericValue,
                ComparisonOperator.GreaterOrEqual => actualValue >= numericValue,
                ComparisonOperator.LessOrEqual => actualValue <= numericValue,
                _ => false
            };
        }

        // String comparison
        var fieldString = fieldValue?.ToString() ?? string.Empty;
        var valueString = condition.Value ?? string.Empty;

        return condition.Operator switch
        {
            ComparisonOperator.Equals => string.Equals(fieldString, valueString, StringComparison.OrdinalIgnoreCase),
            ComparisonOperator.NotEquals => !string.Equals(fieldString, valueString, StringComparison.OrdinalIgnoreCase),
            ComparisonOperator.Contains => fieldString.Contains(valueString, StringComparison.OrdinalIgnoreCase),
            ComparisonOperator.StartsWith => fieldString.StartsWith(valueString, StringComparison.OrdinalIgnoreCase),
            ComparisonOperator.EndsWith => fieldString.EndsWith(valueString, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private bool EvaluateStatusCondition(RuleCondition condition, Dictionary<string, object> context)
    {
        if (!context.TryGetValue(condition.Field, out var fieldValue))
        {
            _logger.LogWarning("Status field {Field} not found in context", condition.Field);
            return false;
        }

        // Status conditions are boolean checks
        if (fieldValue is bool boolValue)
        {
            var expectedValue = bool.TryParse(condition.Value, out var expected) && expected;
            return condition.Operator == ComparisonOperator.Equals ? boolValue == expectedValue : boolValue != expectedValue;
        }

        return false;
    }

    private bool EvaluateTimeCondition(RuleCondition condition, Dictionary<string, object> context)
    {
        var now = DateTime.Now;

        return condition.Field.ToLowerInvariant() switch
        {
            "time_of_day" => EvaluateTimeOfDay(now, condition),
            "day_of_week" => EvaluateDayOfWeek(now, condition),
            "hour" => EvaluateHour(now, condition),
            "date" => EvaluateDate(now, condition),
            _ => false
        };
    }

    private bool EvaluateTimeOfDay(DateTime now, RuleCondition condition)
    {
        // Expected format: "HH:mm" (e.g., "14:30")
        if (!TimeSpan.TryParse(condition.Value, out var targetTime))
            return false;

        var currentTime = now.TimeOfDay;

        return condition.Operator switch
        {
            ComparisonOperator.Equals => Math.Abs((currentTime - targetTime).TotalMinutes) < 1,
            ComparisonOperator.GreaterThan => currentTime > targetTime,
            ComparisonOperator.LessThan => currentTime < targetTime,
            ComparisonOperator.GreaterOrEqual => currentTime >= targetTime,
            ComparisonOperator.LessOrEqual => currentTime <= targetTime,
            _ => false
        };
    }

    private bool EvaluateDayOfWeek(DateTime now, RuleCondition condition)
    {
        // Expected format: day name (e.g., "Monday", "Tuesday")
        if (!Enum.TryParse<DayOfWeek>(condition.Value, true, out var targetDay))
            return false;

        return condition.Operator == ComparisonOperator.Equals 
            ? now.DayOfWeek == targetDay 
            : now.DayOfWeek != targetDay;
    }

    private bool EvaluateHour(DateTime now, RuleCondition condition)
    {
        if (!int.TryParse(condition.Value, out var targetHour))
            return false;

        return condition.Operator switch
        {
            ComparisonOperator.Equals => now.Hour == targetHour,
            ComparisonOperator.NotEquals => now.Hour != targetHour,
            ComparisonOperator.GreaterThan => now.Hour > targetHour,
            ComparisonOperator.LessThan => now.Hour < targetHour,
            ComparisonOperator.GreaterOrEqual => now.Hour >= targetHour,
            ComparisonOperator.LessOrEqual => now.Hour <= targetHour,
            _ => false
        };
    }

    private bool EvaluateDate(DateTime now, RuleCondition condition)
    {
        if (!DateTime.TryParse(condition.Value, out var targetDate))
            return false;

        var currentDate = now.Date;
        var compareDate = targetDate.Date;

        return condition.Operator switch
        {
            ComparisonOperator.Equals => currentDate == compareDate,
            ComparisonOperator.NotEquals => currentDate != compareDate,
            ComparisonOperator.GreaterThan => currentDate > compareDate,
            ComparisonOperator.LessThan => currentDate < compareDate,
            ComparisonOperator.GreaterOrEqual => currentDate >= compareDate,
            ComparisonOperator.LessOrEqual => currentDate <= compareDate,
            _ => false
        };
    }

    private bool EvaluateComplexCondition(RuleCondition condition, Dictionary<string, object> context)
    {
        if (condition.SubConditions == null || !condition.SubConditions.Any())
            return false;

        if (!condition.LogicalOperator.HasValue)
            return false;

        return condition.LogicalOperator.Value switch
        {
            Models.Rules.LogicalOperator.And => condition.SubConditions.All(c => EvaluateCondition(c, context)),
            Models.Rules.LogicalOperator.Or => condition.SubConditions.Any(c => EvaluateCondition(c, context)),
            _ => false
        };
    }

    private static bool IsNumericType(object value)
    {
        return value is int || value is long || value is float || value is double || value is decimal;
    }
}
