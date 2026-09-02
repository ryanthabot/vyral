using VyralBot.Workflows.Models;
using VyralBot.Workflows.Runtime;
using ExecutionContext = VyralBot.Workflows.Runtime.ExecutionContext;

namespace VyralBot.Workflows.Nodes;

/// <summary>
/// Pass-through "attachment card" nodes rendered by the flow editor BELOW loop
/// and comparison blocks (BotGhost-style attached cards). Each card is a real
/// node in the graph, wired through the loop's/comparison's routing fields so
/// graph walkers route through it; executing one is an instant no-op success
/// and the walk simply continues via <see cref="WorkflowNode.Next"/>:
/// <list type="bullet">
/// <item><c>loop_after</c> — the after-loop continuation (sits on the loop's
/// Next edge; "After loop").</item>
/// <item><c>loop_body</c> — the loop body entry (the Branches["body"] target;
/// "Loop # times"). Loop walking starts at Branches["body"] and follows Next,
/// so a card at the head passes straight into the body chain.</item>
/// <item><c>comparison_case</c> — one matching rule's branch (the
/// Branches["case:{i}"] target; "Comparison branch").</item>
/// <item><c>comparison_else</c> — the else branch (the Branches["else"] target;
/// "Else").</item>
/// </list>
/// The cards are NOT in <see cref="VyralBot.Workflows.Abstractions.BlockCatalog.Blocks"/>
/// (never placeable from the palette): the canvas renders these types with
/// hardcoded visuals, so the <see cref="INode.Title"/> strings below are
/// cosmetic. The runtime has no special cases for these types — they flow
/// through the generic "execute, then follow Next" path.
/// </summary>
public class LoopAfterNode : INode
{
    public string Type => "loop_after";
    public string Category => "Loops";
    public string Title => "After loop";

    public Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx) =>
        Task.FromResult(NodeResult.Success());
}

/// <summary>Pass-through card at the head of a loop body chain ("Loop # times"). See <see cref="LoopAfterNode"/>.</summary>
public class LoopBodyNode : INode
{
    public string Type => "loop_body";
    public string Category => "Loops";
    public string Title => "Loop # times";

    public Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx) =>
        Task.FromResult(NodeResult.Success());
}

/// <summary>Pass-through card heading one comparison rule's branch ("Comparison branch"). See <see cref="LoopAfterNode"/>.</summary>
public class ComparisonCaseNode : INode
{
    public string Type => "comparison_case";
    public string Category => "Logic";
    public string Title => "Comparison branch";

    public Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx) =>
        Task.FromResult(NodeResult.Success());
}

/// <summary>Pass-through card heading a comparison's else branch ("Else"). See <see cref="LoopAfterNode"/>.</summary>
public class ComparisonElseNode : INode
{
    public string Type => "comparison_else";
    public string Category => "Logic";
    public string Title => "Else";

    public Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx) =>
        Task.FromResult(NodeResult.Success());
}
