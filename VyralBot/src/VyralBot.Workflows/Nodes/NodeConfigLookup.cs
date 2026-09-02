using VyralBot.Workflows.Models;

namespace VyralBot.Workflows.Nodes;

/// <summary>
/// Lenient Config reader shared by the newer built-in node families: reads the
/// first matching key (exact ordinal match first, then a case-insensitive scan),
/// so hand-authored graph JSON ("role") and editor-authored JSON ("Role") both
/// load. Missing keys (or a missing config) yield the empty string.
/// </summary>
internal static class NodeConfigLookup
{
    /// <summary>Reads the first matching config key; empty string when none match.</summary>
    public static string Get(WorkflowNode node, params string[] keys)
    {
        if (node.Config is null || keys is null || keys.Length == 0)
            return string.Empty;

        foreach (var key in keys)
        {
            if (!string.IsNullOrEmpty(key) && node.Config.TryGetValue(key, out var value) && value is not null)
                return value;
        }

        foreach (var pair in node.Config)
        {
            foreach (var key in keys)
            {
                if (!string.IsNullOrEmpty(key) &&
                    string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase) &&
                    pair.Value is not null)
                {
                    return pair.Value;
                }
            }
        }

        return string.Empty;
    }

    /// <summary>Reads the first matching config key; <paramref name="fallback"/> when none match (or the value is blank).</summary>
    public static string GetOrDefault(WorkflowNode node, string fallback, params string[] keys)
    {
        var value = Get(node, keys);
        return value.Trim().Length > 0 ? value : fallback;
    }
}
