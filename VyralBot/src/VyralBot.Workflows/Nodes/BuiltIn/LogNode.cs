using VyralBot.Workflows.Models;
using VyralBot.Workflows.Runtime;
using Microsoft.Extensions.Logging;
using ExecutionContext = VyralBot.Workflows.Runtime.ExecutionContext;

namespace VyralBot.Workflows.Nodes;

/// <summary>
/// Writes a line to the execution logger (useful for debugging workflows).
/// Config: "text" (variable-substituted), optional "level": "info" (default),
/// "warning" or "error".
/// </summary>
public class LogNode : INode
{
    public string Type => "log";
    public string Category => "Logic";
    public string Title => "Log";

    public Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx)
    {
        var text = ctx.Substitute(NodeConfig.Get(node, "text"));

        switch (NodeConfig.Get(node, "level").Trim().ToLowerInvariant())
        {
            case "warning":
            case "warn":
                ctx.Logger.LogWarning("[workflow] {Text}", text);
                break;
            case "error":
                ctx.Logger.LogError("[workflow] {Text}", text);
                break;
            default:
                ctx.Logger.LogInformation("[workflow] {Text}", text);
                break;
        }

        return Task.FromResult(NodeResult.Success(text));
    }
}
