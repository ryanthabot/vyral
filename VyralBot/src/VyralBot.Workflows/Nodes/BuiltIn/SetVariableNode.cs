using VyralBot.Workflows.Models;
using VyralBot.Workflows.Runtime;
using ExecutionContext = VyralBot.Workflows.Runtime.ExecutionContext;

namespace VyralBot.Workflows.Nodes;

/// <summary>
/// Sets an execution-scoped variable. Config: "name", "value" (variable-substituted,
/// so values can reference {username}, {message}, other variables, ...).
/// </summary>
public class SetVariableNode : INode
{
    public string Type => "set_variable";
    public string Category => "Variables";
    public string Title => "Set Variable";

    public Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx)
    {
        var name = NodeConfig.Get(node, "name").Trim();

        if (string.IsNullOrWhiteSpace(name))
            return Task.FromResult(NodeResult.Fail("No variable name configured"));

        var value = ctx.Substitute(NodeConfig.Get(node, "value"));
        ctx.SetVariable(name, value);

        return Task.FromResult(NodeResult.Success(value));
    }
}
