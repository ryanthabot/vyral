using VyralBot.Workflows.Expressions;
using VyralBot.Workflows.Models;
using VyralBot.Workflows.Runtime;
using ExecutionContext = VyralBot.Workflows.Runtime.ExecutionContext;

namespace VyralBot.Workflows.Nodes;

/// <summary>
/// Condition node: when the Config carries a non-empty "ConditionRule" key, the
/// structured rule is evaluated via <see cref="ConditionRuleEvaluator"/>; a JSON
/// object stored in the "Expression" key is treated as a rule too (rule builders
/// that reuse the expression field). Otherwise the plain expression engine
/// (see <see cref="ExpressionEvaluator"/>) evaluates "expression". No side
/// effects; outputs "true"/"false" for the runtime's branch routing.
/// </summary>
public class IfNode : INode
{
    public string Type => "if";
    public string Category => "Logic";
    public string Title => "If (Expression)";

    public Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx)
    {
        // Structured rule builder output wins over the raw expression.
        var ruleJson = NodeConfigLookup.Get(node, "ConditionRule");
        if (!string.IsNullOrWhiteSpace(ruleJson))
        {
            var ruleResult = ConditionRuleEvaluator.Evaluate(ruleJson, ctx);
            return Task.FromResult(NodeResult.Success(ruleResult ? "true" : "false"));
        }

        var expression = NodeConfigLookup.Get(node, "expression", "Expression");

        // A JSON object in the expression field is treated as a rule as well;
        // plain expressions (e.g. "{coins} > 100") never parse as rule JSON.
        if (expression.TrimStart().StartsWith('{') &&
            ConditionRuleEvaluator.TryParse(expression, out var parsedRule) &&
            parsedRule is not null)
        {
            var parsedResult = ConditionRuleEvaluator.Evaluate(parsedRule, ctx);
            return Task.FromResult(NodeResult.Success(parsedResult ? "true" : "false"));
        }

        if (string.IsNullOrWhiteSpace(expression))
            return Task.FromResult(NodeResult.Fail("No expression configured"));

        if (!ExpressionEvaluator.TryEvaluate(expression, ctx, out var result, out var error))
            return Task.FromResult(NodeResult.Fail($"Invalid expression '{expression}': {error}"));

        return Task.FromResult(NodeResult.Success(result ? "true" : "false"));
    }
}
