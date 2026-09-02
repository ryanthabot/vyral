using VyralBot.Workflows.Models;
using VyralBot.Workflows.Runtime;
using ExecutionContext = VyralBot.Workflows.Runtime.ExecutionContext;

namespace VyralBot.Workflows.Nodes;

/// <summary>
/// Pure canvas annotation (BotGhost "Note" block): carries author notes inside
/// the node tree and does nothing when the flow runs — it executes instantly,
/// succeeds unconditionally and never touches variables, services or output
/// routing (the run continues via Next). Config: "Text" (free-form note text;
/// intentionally NOT variable-substituted — it is documentation, not data).
/// </summary>
public class NoteNode : INode
{
    public string Type => "note";
    public string Category => "Utility";
    public string Title => "Note";

    public Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx) =>
        Task.FromResult(NodeResult.Success());
}
