using VyralBot.Workflows.Models;
using VyralBot.Workflows.Runtime;
using ExecutionContext = VyralBot.Workflows.Runtime.ExecutionContext;

namespace VyralBot.Workflows.Nodes;

/// <summary>
/// Rolls a random integer. Config: "min" (default 0), "max" (default 100),
/// optional "store" (variable name to store the result into). The result is also
/// exposed as the node's OutputValue.
/// </summary>
public class RandomNumberNode : INode
{
    private static readonly Random Random = new();

    public string Type => "random_number";
    public string Category => "Random";
    public string Title => "Random Number";

    public Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx)
    {
        var minRaw = ctx.Substitute(NodeConfig.Get(node, "min", "0"));
        var maxRaw = ctx.Substitute(NodeConfig.Get(node, "max", "100"));

        if (!int.TryParse(minRaw, out var min))
            return Task.FromResult(NodeResult.Fail($"Cannot parse 'min' value '{minRaw}' as an integer"));

        if (!int.TryParse(maxRaw, out var max))
            return Task.FromResult(NodeResult.Fail($"Cannot parse 'max' value '{maxRaw}' as an integer"));

        if (max < min)
            (min, max) = (max, min);

        int roll;
        lock (Random)
        {
            roll = Random.Next(min, max + 1);
        }

        var output = roll.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var storeInto = NodeConfig.Get(node, "store").Trim();
        if (!string.IsNullOrWhiteSpace(storeInto))
            ctx.SetVariable(storeInto, output);

        return Task.FromResult(NodeResult.Success(output));
    }
}
