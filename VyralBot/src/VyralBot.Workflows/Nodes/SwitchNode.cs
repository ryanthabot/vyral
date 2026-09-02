using VyralBot.Workflows.Models;
using VyralBot.Workflows.Runtime;
using ExecutionContext = VyralBot.Workflows.Runtime.ExecutionContext;

namespace VyralBot.Workflows.Nodes;

/// <summary>
/// Multi-way branch: evaluates a ({variable}-substituted) value against a
/// comma-separated case list and outputs the MATCHED case string — the runtime
/// then routes through the node's Branches[matched-case] entry. When nothing
/// matches, the raw value is output and the runtime falls back to BranchFalse
/// (the "default" port) or Next. Config: "Value", "Cases".
/// </summary>
public class SwitchNode : INode
{
    public string Type => "switch";
    public string Category => "Logic";
    public string Title => "Switch";

    public Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx)
    {
        var value = ctx.Substitute(NodeConfigLookup.Get(node, "Value")).Trim();

        var cases = NodeConfigLookup.Get(node, "Cases")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (cases.Length == 0)
            return Task.FromResult(NodeResult.Fail("No cases configured"));

        foreach (var testCase in cases)
        {
            if (string.Equals(testCase, value, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(NodeResult.Success(testCase)); // matched case → Branches[case]
        }

        // No match: output the raw value; the runtime falls back to BranchFalse/default.
        return Task.FromResult(NodeResult.Success(value));
    }
}
