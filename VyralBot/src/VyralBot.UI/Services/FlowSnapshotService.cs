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
            // Top-level description/enabled round-trip with the snapshot DTO
            // (the DTO deserializer reads both keys case-insensitively).
            ["description"] = string.IsNullOrWhiteSpace(snapshot.Description) ? null : snapshot.Description.Trim(),
            ["enabled"] = snapshot.Enabled,
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
                // Mod-command minimum tier — without it a shared !mod command
                // silently falls back to "Mod" on import.
                ["minimumTier"] = string.IsNullOrWhiteSpace(snapshot.Trigger.MinimumTier) ? null : snapshot.Trigger.MinimumTier.Trim(),
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

    /// <summary>Hard import ceilings — anything larger is rejected, not truncated.</summary>
    public const int MaxImportNodes = 500;
    public const int MaxImportOptions = 100;
    public const int MaxImportParameters = 100;
    public const int MaxImportConfigValueLength = 100_000;
    public const int MaxImportNodeIdLength = 256;
    public const int MaxConfigKeysPerNode = 64;

    /// <summary>
    /// Block types that are valid in an import but NOT in <see cref="BlockCatalog.Blocks"/>:
    /// "switch" (legacy, runtime-supported but no longer placeable), the four
    /// loop/comparison attachment-card types and the component (button/menu)
    /// sub-node cards the canvas spawns next to message blocks.
    /// </summary>
    private static readonly HashSet<string> ExtraImportableTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "switch", "loop_after", "loop_body", "comparison_case", "comparison_else",
        "menu_start", "button_start"
    };

    /// <summary>True when a node type is known to this build (palette, legacy switch or canvas card).</summary>
    private static bool IsKnownNodeType(string? blockType) =>
        !string.IsNullOrWhiteSpace(blockType) &&
        (BlockCatalog.Find(blockType!) != null || ExtraImportableTypes.Contains(blockType!));

    /// <summary>Clamps a node config to the import ceilings (key count + value length).</summary>
    private static Dictionary<string, string> SanitizeConfig(Dictionary<string, string> config, int maxKeys)
    {
        var safe = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in config)
        {
            if (safe.Count >= maxKeys)
            {
                break;
            }

            safe[entry.Key] = entry.Value is { Length: > MaxImportConfigValueLength }
                ? entry.Value[..MaxImportConfigValueLength]
                : entry.Value ?? string.Empty;
        }

        return safe;
    }

    /// <summary>
    /// Parses a portable document (or a raw draft-shaped snapshot) into a full
    /// snapshot DTO — canvas positions, node parameters and port routing
    /// included. Returns null when the payload is not a flow document.
    /// Shared/imported documents are sanitized first: unknown node types,
    /// duplicate/empty ids, oversized node lists and routing references to
    /// dropped nodes are removed so a hostile or newer-version document cannot
    /// inject nodes this build cannot render or execute.
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

        if (!SanitizeImportedSnapshot(dto))
        {
            return null; // oversized beyond the import ceilings — treat as invalid
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
    /// Clamps an imported snapshot to the safe shape: drops unknown node types,
    /// duplicate/oversized ids and pathological config payloads, then clears
    /// every routing reference that no longer resolves (draft-shaped payloads
    /// carry routing on the DTO; file-shaped ones are wired later from the
    /// <c>connections</c> array, which drops orphans on its own). Returns false
    /// when the document exceeds the node ceiling and must be rejected whole.
    /// </summary>
    private static bool SanitizeImportedSnapshot(FlowEditorSnapshotDto dto)
    {
        if (dto.Options is { Count: > MaxImportOptions })
        {
            dto.Options = dto.Options.Take(MaxImportOptions).ToList();
        }

        if (dto.Parameters is { Count: > MaxImportParameters })
        {
            dto.Parameters = dto.Parameters.Take(MaxImportParameters).ToList();
        }

        if (dto.Nodes is not { Count: > 0 })
        {
            return true;
        }

        if (dto.Nodes.Count > MaxImportNodes)
        {
            return false;
        }

        const int maxNodeIdLength = MaxImportNodeIdLength;

        var kept = new List<FlowNodeDto>(dto.Nodes.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (FlowNodeDto node in dto.Nodes)
        {
            if (node == null ||
                string.IsNullOrWhiteSpace(node.Id) ||
                node.Id.Length > maxNodeIdLength ||
                !IsKnownNodeType(node.BlockType) ||
                !ids.Add(node.Id))
            {
                // Unknown/injected node type, duplicate or malformed id — drop.
                // ids.Add stays LAST so a dropped id never masquerades as a
                // resolvable routing target in the dangling-wire sweep below.
                continue;
            }

            if (node.Config is { Count: > 0 })
            {
                node.Config = SanitizeConfig(node.Config, MaxConfigKeysPerNode);
            }

            kept.Add(node);
        }

        if (kept.Count != dto.Nodes.Count)
        {
            foreach (FlowNodeDto node in kept)
            {
                if (node.Next != null && !ids.Contains(node.Next)) node.Next = null;
                if (node.BranchTrue != null && !ids.Contains(node.BranchTrue)) node.BranchTrue = null;
                if (node.BranchFalse != null && !ids.Contains(node.BranchFalse)) node.BranchFalse = null;
                if (node.BranchError != null && !ids.Contains(node.BranchError)) node.BranchError = null;
                if (node.Branches is { Count: > 0 })
                {
                    foreach (string caseKey in node.Branches.Where(b => !ids.Contains(b.Value)).Select(b => b.Key).ToList())
                    {
                        node.Branches.Remove(caseKey);
                    }

                    if (node.Branches.Count == 0)
                    {
                        node.Branches = null;
                    }
                }
            }
        }

        if (dto.EntryNodeId != null && !ids.Contains(dto.EntryNodeId))
        {
            dto.EntryNodeId = null;
        }

        if (dto.ErrorHandlerEntryId != null && !ids.Contains(dto.ErrorHandlerEntryId))
        {
            dto.ErrorHandlerEntryId = null;
        }

        dto.Nodes = kept;
        return true;
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
                var config = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var property in parameters.EnumerateObject())
                {
                    config[property.Name] = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? string.Empty
                        : property.Value.GetRawText();
                }

                // Same ceilings as the sanitize pass — the file shape REPLACES
                // the DTO config, so pathological values must be clamped here too.
                node.Config = SanitizeConfig(config, MaxConfigKeysPerNode);
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
