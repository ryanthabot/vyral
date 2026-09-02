using VyralBot.Workflows.Models;
using VyralBot.Workflows.Runtime;
using ExecutionContext = VyralBot.Workflows.Runtime.ExecutionContext;

namespace VyralBot.Workflows.Nodes;

/// <summary>
/// Pauses workflow execution. Config: "seconds" (variable-substituted, clamped to
/// 0..60 — longer waits belong to the future scheduler, phase 11).
/// </summary>
public class WaitNode : INode
{
    public const double MaxSeconds = 60;

    public string Type => "wait";
    public string Category => "Timers";
    public string Title => "Delay";

    public async Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx)
    {
        var raw = ctx.Substitute(NodeConfig.Get(node, "seconds", "1"));

        if (!double.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
            return NodeResult.Fail($"Cannot parse 'seconds' value '{raw}' as a number");

        seconds = Math.Clamp(seconds, 0, MaxSeconds);

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds), ctx.CancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return NodeResult.Fail("Wait was cancelled");
        }

        return NodeResult.Success(seconds.ToString("0.###"));
    }
}
