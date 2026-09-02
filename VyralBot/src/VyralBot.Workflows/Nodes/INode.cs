using VyralBot.Workflows.Models;
using VyralBot.Workflows.Runtime;
using ExecutionContext = VyralBot.Workflows.Runtime.ExecutionContext;

namespace VyralBot.Workflows.Nodes;

/// <summary>Outcome of executing a single node.</summary>
public class NodeResult
{
    /// <summary>Whether the node did its job. A failed node stops the workflow.</summary>
    public bool Succeeded { get; private set; }

    /// <summary>
    /// Optional output value ("true"/"false" for "if" nodes, the rolled number for
    /// "random_number", the stored value for "set_variable", ...).
    /// </summary>
    public string? OutputValue { get; private set; }

    /// <summary>Diagnostic message when <see cref="Succeeded"/> is false.</summary>
    public string? Error { get; private set; }

    public static NodeResult Success(string? outputValue = null) => new() { Succeeded = true, OutputValue = outputValue };

    public static NodeResult Fail(string? error = null) => new() { Succeeded = false, Error = error };
}

/// <summary>
/// A workflow node implementation. Register instances into the
/// <see cref="NodeRegistry"/>; the runtime resolves them by <see cref="Type"/>.
/// Implementations must resolve Core services lazily from
/// <see cref="ExecutionContext"/>.Services and tolerate missing services by
/// returning a failed result (never throw).
/// </summary>
public interface INode
{
    /// <summary>Unique node type key, e.g. "send_message" (matched against WorkflowNode.Type).</summary>
    string Type { get; }

    /// <summary>Palette category, e.g. "Logic", "Variables", "Economy", "Random", "Timers", "Messaging".</summary>
    string Category { get; }

    /// <summary>Human-readable title for the editor palette.</summary>
    string Title { get; }

    /// <summary>Executes the node. Should not throw; return Fail() on errors.</summary>
    Task<NodeResult> ExecuteAsync(WorkflowNode node, ExecutionContext ctx);
}

/// <summary>Small helpers for reading node Config dictionaries safely.</summary>
public static class NodeConfig
{
    /// <summary>Reads a config value with a fallback when the key (or the config itself) is missing.</summary>
    public static string Get(WorkflowNode node, string key, string fallback = "")
    {
        if (node.Config is null)
            return fallback;
        return node.Config.TryGetValue(key, out var value) ? value : fallback;
    }
}
