using System.Text.Json;
using VyralBot.UI.ViewModels;
using VyralBot.Workflows.Abstractions;
using VyralBot.Workflows.Models;

namespace VyralBot.UI.Services;

/// <summary>
/// Pure (de)serialization between the editor's <see cref="FlowEditorSnapshotDto"/>
/// and the portable <c>.flow.json</c> document shared by file export/import and
/// the command marketplace (sharecodes). One implementation so a shared command
/// and a file-exported flow behave identically on import.
/// </summary>
/// <remarks>
/// The file document stores canvas nodes as <c>{ id, blockType, label,
/// position{x,y}, parameters{...} }</c> plus a <c>connections</c> array, while
/// the snapshot DTO keeps <c>x/y/config</c> and routing fields directly — the
/// parse side maps between both shapes.
/// </remarks>
public static class FlowSnapshotService
{
    /// <summary>Serializes an editor snapshot into the portable document (spec shape).</summary>
    public static string BuildDocument(FlowEditorSnapshotDto snapshot, string? author = null)
    {
        var byId = new HashSet<string>(
            snapshot.Nodes?.Select(n => n.Id) ?? Enumerable.Empty<string>(),
            StringComparer.Ordinal);
        string? Clean(string? id) => id != null && byId.Contains(id) ? id : null;

        var document = new Dictionary<string, object?>
        {
            ["id"] = snapshot.Id,
            ["name"] = snapshot.Name,
            ["trigger"] = snapshot.Trigger == null ? null : new Dictionary<string, object?>
            {
                ["type"] = snapshot.Trigger.Type,
                ["commandName"] = snapshot.Trigger.CommandName,
                ["commandPrefix"] = snapshot.Trigger.CommandPrefix,
                ["intervalMinutes"] = snapshot.Trigger.IntervalMinutes,
                ["eventType"] = snapshot.Trigger.EventType,
                ["platform"] = snapshot.Trigger.Platform,
                // Command-trigger platform filter (null = all platforms) so
                // exported/shared commands keep their scope; the parse side
                // maps it back onto FlowTriggerDto.Platforms case-insensitively.
                ["platforms"] = snapshot.Trigger.Platforms is { Count: > 0 } platforms ? platforms : null,
                ["description"] = string.IsNullOrWhiteSpace(snapshot.Trigger.Description) ? null : snapshot.Trigger.Description.Trim()
            },
            ["options"] = (snapshot.Options ?? new List<FlowOptionDto>()).Select(o => new Dictionary<string, object?>
            {
                ["type"] = o.Type,
                // Flag-only rows (DM only / guild only / NSFW) carry no value on
                // the canvas; the document normalizes them to "true" like the
                // save path does.
                ["value"] = IsFlagOnlyOption(o.Type) ? "true" : o.Value
            }).ToList(),
            ["parameters"] = (snapshot.Parameters ?? new List<FlowParameterDto>()).Select(p => new Dictionary<string, object?>
            {
                ["name"] = p.Name,
                ["type"] = p.Type,
                ["description"] = p.Description,
                ["required"] = p.Required
            }).ToList(),
            ["entryNodeId"] = snapshot.EntryNodeId,
            ["errorHandlerEntryId"] = snapshot.ErrorHandlerEntryId,
            ["nodes"] = (snapshot.Nodes ?? new List<FlowNodeDto>()).Select(n => new Dictionary<string, object?>
            {
                ["id"] = n.Id,
                ["blockType"] = n.BlockType,
                ["label"] = n.Label,
                ["position"] = new Dictionary<string, object?> { ["x"] = n.X, ["y"] = n.Y },
                ["parameters"] = new Dictionary<string, string>(n.Config, StringComparer.Ordinal)
            }).ToList(),
            ["connections"] = BuildConnections(snapshot.Nodes ?? new List<FlowNodeDto>(), Clean),
            ["metadata"] = new Dictionary<string, object?>
            {
                ["version"] = "1.0",
                ["lastSaved"] = DateTime.UtcNow.ToString("o"),
                ["author"] = string.IsNullOrWhiteSpace(author) ? "Vyral" : author!.Trim()
            }
        };

        return JsonSerializer.Serialize(document, JsonOpts.Indented);
    }

    /// <summary>
    /// Parses a portable document (or a raw draft-shaped snapshot) into a full
    /// snapshot DTO — canvas positions, node parameters and port routing
    /// included. Returns null when the payload is not a flow document.
    /// </summary>
    public static FlowEditorSnapshotDto? ParseDocument(string json)
    {
        FlowEditorSnapshotDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<FlowEditorSnapshotDto>(json, JsonOpts.Indented);
        }
        catch (JsonException)
        {
            return null;
        }

        if (dto == null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return dto;
            }

            ApplyNodeFileShape(document.RootElement, dto.Nodes);
            if (document.RootElement.TryGetProperty("connections", out var connections) &&
                connections.ValueKind == JsonValueKind.Array)
            {
                ApplyConnections(connections, dto.Nodes ?? new List<FlowNodeDto>());
            }
        }
        catch (JsonException)
        {
            // The DTO shell already parsed — tolerate a malformed raw section
            // (same tolerance as the pre-service import path).
        }

        return dto;
    }

    /// <summary>
    /// Patches DTO nodes from the file shape: <c>position{x,y}</c> and
    /// <c>parameters{...}</c>. Draft-shaped payloads (direct <c>x/y/config</c>)
    /// already deserialize correctly and pass through untouched.
    /// </summary>
    private static void ApplyNodeFileShape(JsonElement root, List<FlowNodeDto>? nodes)
    {
        if (nodes == null || nodes.Count == 0 ||
            !root.TryGetProperty("nodes", out var fileNodes) ||
            fileNodes.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var byId = nodes.GroupBy(n => n.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var element in fileNodes.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string id = GetString(element, "id");
            if (string.IsNullOrWhiteSpace(id) || !byId.TryGetValue(id, out FlowNodeDto? node))
            {
                continue;
            }

            if (element.TryGetProperty("position", out var position) &&
                position.ValueKind == JsonValueKind.Object)
            {
                node.X = GetNumber(position, "x", node.X);
                node.Y = GetNumber(position, "y", node.Y);
            }

            if (element.TryGetProperty("parameters", out var parameters) &&
                parameters.ValueKind == JsonValueKind.Object)
            {
                node.Config = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var property in parameters.EnumerateObject())
                {
                    node.Config[property.Name] = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? string.Empty
                        : property.Value.GetRawText();
                }
            }
        }
    }

    /// <summary>
    /// Wires the ports from the <c>connections</c> array
    /// ("{nodeId}:{portKey}" → "{targetNodeId}:in") onto DTO routing fields.
    /// </summary>
    public static void ApplyConnections(JsonElement connections, List<FlowNodeDto> nodes)
    {
        var byId = nodes.GroupBy(n => n.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var connection in connections.EnumerateArray())
        {
            string source = GetString(connection, "source");
            string target = GetString(connection, "target");
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            int sourceColon = source.IndexOf(':');
            if (sourceColon <= 0)
            {
                continue;
            }
            string sourceId = source[..sourceColon];
            string portKey = source[(sourceColon + 1)..];

            int targetColon = target.IndexOf(':');
            string targetId = targetColon > 0 ? target[..targetColon] : target;

            if (!byId.TryGetValue(sourceId, out FlowNodeDto? sourceNode) || !byId.ContainsKey(targetId))
            {
                continue; // orphan reference — drop
            }

            switch (portKey)
            {
                case BlockCatalog.PortNext: sourceNode.Next = targetId; break;
                case BlockCatalog.PortTrue: sourceNode.BranchTrue = targetId; break;
                case BlockCatalog.PortFalse:
                case BlockCatalog.PortDefault: sourceNode.BranchFalse = targetId; break;
                case BlockCatalog.PortError: sourceNode.BranchError = targetId; break;
                default:
                    if (portKey.StartsWith(BlockCatalog.PortCasePrefix, StringComparison.Ordinal))
                    {
                        sourceNode.Branches ??= new Dictionary<string, string>(StringComparer.Ordinal);
                        sourceNode.Branches[portKey[BlockCatalog.PortCasePrefix.Length..]] = targetId;
                    }
                    break;
            }
        }
    }

    private static bool IsFlagOnlyOption(string? type) =>
        string.Equals(type, WorkflowOptionTypes.DmOnly, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, WorkflowOptionTypes.GuildOnly, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, WorkflowOptionTypes.Nsfw, StringComparison.OrdinalIgnoreCase);

    private static List<Dictionary<string, object?>> BuildConnections(List<FlowNodeDto> nodes, Func<string?, string?> clean)
    {
        var connections = new List<Dictionary<string, object?>>();
        foreach (FlowNodeDto node in nodes)
        {
            void Wire(string portKey, string? targetId)
            {
                string? target = clean(targetId);
                if (string.IsNullOrEmpty(target))
                {
                    return;
                }

                connections.Add(new Dictionary<string, object?>
                {
                    ["id"] = $"{node.Id}:{portKey}->{target}",
                    ["source"] = $"{node.Id}:{portKey}",
                    ["target"] = $"{target}:in"
                });
            }

            Wire(BlockCatalog.PortNext, node.Next);
            Wire(BlockCatalog.PortTrue, node.BranchTrue);
            Wire(BlockCatalog.PortFalse, node.BranchFalse);
            Wire(BlockCatalog.PortError, node.BranchError);

            foreach (KeyValuePair<string, string> branch in node.Branches ?? new Dictionary<string, string>())
            {
                Wire(BlockCatalog.PortCasePrefix + branch.Key, branch.Value);
            }
        }
        return connections;
    }

    private static string GetString(JsonElement element, string property)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(property, out var value) &&
            value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    private static double GetNumber(JsonElement element, string property, double fallback)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(property, out var value) &&
            value.ValueKind == JsonValueKind.Number)
        {
            return value.GetDouble();
        }
        return fallback;
    }
}
