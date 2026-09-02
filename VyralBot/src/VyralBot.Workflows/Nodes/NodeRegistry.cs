namespace VyralBot.Workflows.Nodes;

/// <summary>
/// Singleton catalog of node implementations. Built-in nodes are registered via
/// <see cref="BuiltInNodes.RegisterAll"/>; plugins register additional nodes the
/// same way. Lookups by type are case-insensitive.
/// </summary>
public class NodeRegistry
{
    private readonly Dictionary<string, INode> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    /// <summary>Registers a node implementation; a later registration replaces an earlier one with the same type.</summary>
    public void Register(INode node)
    {
        if (node is null)
            throw new ArgumentNullException(nameof(node));
        if (string.IsNullOrWhiteSpace(node.Type))
            throw new ArgumentException("Node type cannot be empty", nameof(node));

        lock (_lock)
        {
            _nodes[node.Type] = node;
        }
    }

    /// <summary>Gets the implementation for a node type, or null when unregistered.</summary>
    public INode? Get(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return null;

        lock (_lock)
        {
            return _nodes.TryGetValue(type, out var node) ? node : null;
        }
    }

    /// <summary>All registered node implementations (for the editor palette).</summary>
    public IReadOnlyList<INode> GetAll()
    {
        lock (_lock)
        {
            return _nodes.Values.ToList();
        }
    }
}
