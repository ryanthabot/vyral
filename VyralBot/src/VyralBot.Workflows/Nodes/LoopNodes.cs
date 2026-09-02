using VyralBot.Workflows.Models;
using VyralBot.Workflows.Runtime;
using ExecutionContext = VyralBot.Workflows.Runtime.ExecutionContext;

namespace VyralBot.Workflows.Nodes;

/// <summary>
/// Action node (BotGhost "Run a Loop"): marks the loop point of a flow. The
/// runtime interprets "loop" nodes natively — it runs the body chain wired to
/// the Branches["body"] port ("Loop times" output) once per iteration
/// (Config "Count", 1-25) and then routes via Next ("After loop"), so this
/// implementation only exists for registry completeness
/// (<see cref="NodeRegistry.Get"/>); the runtime special-cases the type before
/// calling it. Executed directly (e.g. inside a loop body) it is a no-op success.
/// </summary>
public class LoopNode : INode
{
    public string Type => "loop";
    public string Category => "Loops";
    public string Title => "Run a Loop";

    public Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx) =>
        Task.FromResult(NodeResult.Success());
}

/// <summary>
/// Action node (BotGhost "Stop a Loop"): requests breaking out of the active
/// loop by setting the "__stop_loop" variable, which the runtime's loop
/// interpreter consumes at the end of the current body pass (ending every
/// remaining iteration). Outside a loop the flag is inert (the next loop start
/// clears it). Always succeeds.
/// </summary>
public class StopLoopNode : INode
{
    public string Type => "stop_loop";
    public string Category => "Loops";
    public string Title => "Stop a Loop";

    public Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx)
    {
        ctx.SetVariable(WorkflowRuntime.StopLoopVariable, "true");
        return Task.FromResult(NodeResult.Success());
    }
}
