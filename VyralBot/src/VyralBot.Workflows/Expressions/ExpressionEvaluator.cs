using System.Globalization;
using System.Text.RegularExpressions;
using VyralBot.Workflows.Runtime;
using ExecutionContext = VyralBot.Workflows.Runtime.ExecutionContext;

namespace VyralBot.Workflows.Expressions;

/// <summary>
/// Tiny boolean expression evaluator for workflow conditions ("if" nodes and
/// event-trigger Conditions).
///
/// GRAMMAR (deliberately minimal — phase 10 expands this):
///   expression := comparison ( ' and ' comparison )*        // OR/NOT/parens are NOT supported
///   comparison := operand OP operand
///   OP         := ==  !=  &gt;  &gt;=  &lt;  &lt;=  contains
///   operand    := {variable}  |  number  |  true  |  false  |  'single-quoted string'  |  bareword
///
/// Semantics:
///   - {variable} resolves against the execution context (execution variables
///     plus {username}, {channel}, {platform}, {message}, {botid}, {guild}).
///     Unknown variables resolve to the empty string.
///   - == / != compare numerically when BOTH sides parse as numbers (invariant
///     culture), otherwise as case-insensitive strings.
///   - &gt; &gt;= &lt; &lt;= are numeric only — if either side is not a number
///     the comparison is an error (TryEvaluate returns false with a message).
///   - contains is a case-insensitive substring test on the string forms.
///   - ' and ' inside a quoted string literal is not supported (naive split).
///   - Single-quoted strings cannot contain single quotes and support no escapes.
///
/// Examples:
///   {coins} &gt; 100
///   {message} contains 'hello'
///   {platform} == 'Twitch' and {viewers} &gt;= 50
///   {stream.live} == true
/// </summary>
public static partial class ExpressionEvaluator
{
    private static readonly string[] OperatorsDescending =
    {
        "==", "!=", ">=", "<=", "contains", ">", "<"
    };

    private static readonly Regex AndSplitRegex = CreateAndSplitRegex();

    /// <summary>
    /// Convenience wrapper: evaluates and returns the boolean result.
    /// Invalid expressions evaluate to false (use TryEvaluate for the reason).
    /// </summary>
    public static bool Evaluate(string? expression, ExecutionContext ctx) =>
        TryEvaluate(expression, ctx, out var result, out _) && result;

    /// <summary>
    /// Evaluates an expression. Returns false (with an <paramref name="error"/>) when the
    /// expression cannot be parsed; returns true with the boolean <paramref name="result"/> otherwise.
    /// Never throws.
    /// </summary>
    public static bool TryEvaluate(string? expression, ExecutionContext ctx, out bool result, out string? error)
    {
        result = false;
        error = null;

        if (ctx is null)
        {
            error = "No execution context";
            return false;
        }

        if (string.IsNullOrWhiteSpace(expression))
        {
            error = "Expression is empty";
            return false;
        }

        // AND of comparisons (naive split: ' and ' inside string literals is not supported).
        var parts = AndSplitRegex.Split(expression.Trim());

        foreach (var part in parts)
        {
            if (!TryEvaluateComparison(part.Trim(), ctx, out var comparison, out error))
                return false;

            if (!comparison)
            {
                result = false; // short-circuit AND
                return true;
            }
        }

        result = true;
        return true;
    }

    private static bool TryEvaluateComparison(string comparison, ExecutionContext ctx, out bool result, out string? error)
    {
        result = false;
        error = null;

        if (string.IsNullOrWhiteSpace(comparison))
        {
            error = "Empty comparison";
            return false;
        }

        // Locate the earliest operator (longest wins on ties, so ">=" beats ">").
        var opIndex = -1;
        var op = string.Empty;
        foreach (var candidate in OperatorsDescending)
        {
            var index = comparison.IndexOf(candidate, StringComparison.Ordinal);
            if (index < 0)
                continue;

            if (opIndex < 0 || index < opIndex || (index == opIndex && candidate.Length > op.Length))
            {
                opIndex = index;
                op = candidate;
            }
        }

        if (opIndex < 0)
        {
            error = $"No comparison operator (==, !=, >, >=, <, <=, contains) in '{comparison}'";
            return false;
        }

        var leftToken = comparison[..opIndex].Trim();
        var rightToken = comparison[(opIndex + op.Length)..].Trim();

        if (leftToken.Length == 0 || rightToken.Length == 0)
        {
            error = $"Missing operand around '{op}' in '{comparison}'";
            return false;
        }

        var left = ResolveOperand(leftToken, ctx);
        var right = ResolveOperand(rightToken, ctx);

        switch (op)
        {
            case "==":
                result = Equals(left, right);
                return true;

            case "!=":
                result = !Equals(left, right);
                return true;

            case ">":
            case ">=":
            case "<":
            case "<=":
            {
                if (!double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out var leftNumber) ||
                    !double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out var rightNumber))
                {
                    error = $"'{op}' requires numeric operands, got '{leftToken}' and '{rightToken}'";
                    return false;
                }

                result = op switch
                {
                    ">" => leftNumber > rightNumber,
                    ">=" => leftNumber >= rightNumber,
                    "<" => leftNumber < rightNumber,
                    _ => leftNumber <= rightNumber
                };
                return true;
            }

            case "contains":
                result = left.Contains(right, StringComparison.OrdinalIgnoreCase);
                return true;

            default:
                error = $"Unknown operator '{op}'";
                return false;
        }
    }

    /// <summary>Case-insensitive equality; numeric when both sides are numbers.</summary>
    private static bool Equals(string left, string right)
    {
        if (double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out var leftNumber) &&
            double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out var rightNumber))
        {
            return Math.Abs(leftNumber - rightNumber) < double.Epsilon;
        }

        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Resolves an operand token to its string value.</summary>
    private static string ResolveOperand(string token, ExecutionContext ctx)
    {
        token = token.Trim();

        // {variable} lookup
        if (token.Length >= 2 && token.StartsWith('{') && token.EndsWith('}'))
            return ctx.GetVariable(token[1..^1].Trim()) ?? string.Empty;

        // 'single-quoted string literal'
        if (token.Length >= 2 && token.StartsWith('\'') && token.EndsWith('\''))
            return token[1..^1];

        // Boolean literals normalize to lowercase so {flag} == true works with "True" values.
        if (string.Equals(token, "true", StringComparison.OrdinalIgnoreCase))
            return "true";
        if (string.Equals(token, "false", StringComparison.OrdinalIgnoreCase))
            return "false";

        // Numbers pass through; anything else is treated as a bareword literal (lenient).
        return token;
    }

    [GeneratedRegex(@"\s+and\s+", RegexOptions.IgnoreCase)]
    private static partial Regex CreateAndSplitRegex();
}
