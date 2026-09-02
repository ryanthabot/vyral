using System.Text.Json;
using System.Text.Json.Serialization;
using VyralBot.Data;
using VyralBot.Workflows.Models;

namespace VyralBot.Workflows.Persistence;

/// <summary>
/// Maps between <see cref="Workflow"/> objects and the Workflows table row shape
/// (TriggerJson + GraphJson columns). Round-trip safe: saving via ToRow and
/// reloading via FromRow preserves trigger, nodes, options and config verbatim
/// (including the node BranchError/BranchError case/switch wiring and the
/// workflow's command options).
///
/// GraphJson shapes (both load): v1 rows hold a bare JSON node array; v2+ rows
/// hold a document object ("nodes": [...], "options": [{"type","value"}, ...],
/// "parameters": [{"name","type","description","required"}, ...], "entryNodeId",
/// "errorHandlerEntryId", "componentKey", "sourceCommandId").
/// JSON is written camelCase and parsed case-insensitively, so older PascalCase
/// rows and hand-authored JSON load unchanged; unknown properties are ignored
/// on load (forward compatible).
/// </summary>
public static class WorkflowJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Root shape of the v2+ GraphJson column: nodes, options and graph metadata.</summary>
    private sealed class GraphDocument
    {
        /// <summary>The node graph (entry node first).</summary>
        public List<WorkflowNode> Nodes { get; set; } = new();

        /// <summary>Command-level constraints (cooldowns, permissions, channel scoping).</summary>
        public List<WorkflowOption> Options { get; set; } = new();

        /// <summary>Typed command parameters (Workflow.Parameters). Omitted when empty.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<WorkflowParameter>? Parameters { get; set; }

        /// <summary>Explicit main-flow entry node (Workflow.EntryNodeId). Omitted when null.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? EntryNodeId { get; set; }

        /// <summary>First node of the Error Handler chain (Workflow.ErrorHandlerEntryId). Omitted when null.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ErrorHandlerEntryId { get; set; }

        /// <summary>Component child workflow key (Workflow.ComponentKey). Omitted when null.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ComponentKey { get; set; }

        /// <summary>
        /// Command row this flow was created from / manages (Workflow.SourceCommandId).
        /// Omitted when null so pre-existing documents stay byte-identical.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SourceCommandId { get; set; }
    }

    /// <summary>Serializes a trigger definition to the TriggerJson column value.</summary>
    public static string ToTrigger(WorkflowTrigger trigger) =>
        JsonSerializer.Serialize(trigger ?? new WorkflowTrigger(), Options);

    /// <summary>Deserializes a TriggerJson column value; null/empty yields a fresh manual trigger.</summary>
    public static WorkflowTrigger FromTrigger(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? new WorkflowTrigger()
            : JsonSerializer.Deserialize<WorkflowTrigger>(json, Options) ?? new WorkflowTrigger();

    /// <summary>Serializes the node list (v1 shape: bare array) to a GraphJson column value.</summary>
    public static string ToGraph(IEnumerable<WorkflowNode> nodes) =>
        JsonSerializer.Serialize(nodes ?? Array.Empty<WorkflowNode>(), Options);

    /// <summary>Serializes nodes plus command options (v2 shape: document object) to a GraphJson column value.</summary>
    public static string ToGraph(IEnumerable<WorkflowNode> nodes, IEnumerable<WorkflowOption>? options) =>
        JsonSerializer.Serialize(ToDocument(nodes, options, null), Options);

    /// <summary>
    /// Serializes nodes, command options and the source command id (v2 shape:
    /// document object) to a GraphJson column value. A null source command id is
    /// omitted from the document.
    /// </summary>
    public static string ToGraph(IEnumerable<WorkflowNode> nodes, IEnumerable<WorkflowOption>? options, string? sourceCommandId) =>
        JsonSerializer.Serialize(ToDocument(nodes, options, sourceCommandId), Options);

    /// <summary>
    /// Serializes a complete workflow graph (v2 shape: document object) to a
    /// GraphJson column value, including typed parameters, the explicit entry
    /// node, the Error Handler entry and the component key. Empty/null sections
    /// are omitted from the document so pre-existing documents stay byte-identical.
    /// </summary>
    public static string ToGraph(
        IEnumerable<WorkflowNode> nodes,
        IEnumerable<WorkflowOption>? options,
        string? sourceCommandId,
        IEnumerable<WorkflowParameter>? parameters,
        string? entryNodeId,
        string? errorHandlerEntryId,
        string? componentKey) =>
        JsonSerializer.Serialize(
            ToDocument(nodes, options, sourceCommandId, parameters, entryNodeId, errorHandlerEntryId, componentKey),
            Options);

    /// <summary>
    /// Deserializes a GraphJson column value (v1 array or v2 document; command
    /// options, when present, are dropped); null/empty yields an empty node list.
    /// </summary>
    public static List<WorkflowNode> FromGraph(string? json) =>
        FromGraphDocument(json).Nodes;

    /// <summary>
    /// Deserializes a GraphJson column value including command options. Missing
    /// sections (old rows) default to empty lists; malformed JSON yields empty
    /// lists instead of throwing.
    /// </summary>
    public static (List<WorkflowNode> Nodes, List<WorkflowOption> Options) FromGraphDocument(string? json)
    {
        var graph = FromGraphFull(json);
        return (graph.Nodes, graph.Options);
    }

    /// <summary>
    /// Deserializes a GraphJson column value including command options and the
    /// source command id. Missing sections (old rows) default to empty lists /
    /// null; malformed JSON yields empty lists instead of throwing. The v1 bare
    /// node-array shape carries no source command id.
    /// </summary>
    public static (List<WorkflowNode> Nodes, List<WorkflowOption> Options, string? SourceCommandId) FromGraphFull(string? json)
    {
        var graph = FromGraphData(json);
        return (graph.Nodes, graph.Options, graph.SourceCommandId);
    }

    /// <summary>
    /// Deserializes a GraphJson column value including every graph-root section:
    /// nodes, command options, source command id, typed parameters, entry node,
    /// Error Handler entry and component key. Missing sections (old rows) default
    /// to empty lists / null; malformed JSON yields empty lists instead of
    /// throwing. The v1 bare node-array shape carries only nodes.
    /// </summary>
    public static WorkflowGraphData FromGraphData(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new WorkflowGraphData(new List<WorkflowNode>(), new List<WorkflowOption>(), null, new List<WorkflowParameter>(), null, null, null);

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            // v1 shape: bare node array (no command options or graph metadata).
            if (root.ValueKind == JsonValueKind.Array)
                return new WorkflowGraphData(FromGraphCore(json), new List<WorkflowOption>(), null, new List<WorkflowParameter>(), null, null, null);

            // v2 shape: { "nodes", "options", "parameters", "entryNodeId",
            //             "errorHandlerEntryId", "componentKey", "sourceCommandId" }.
            var parsed = JsonSerializer.Deserialize<GraphDocument>(json, Options);
            return new WorkflowGraphData(
                parsed?.Nodes ?? new List<WorkflowNode>(),
                parsed?.Options ?? new List<WorkflowOption>(),
                parsed?.SourceCommandId,
                parsed?.Parameters ?? new List<WorkflowParameter>(),
                parsed?.EntryNodeId,
                parsed?.ErrorHandlerEntryId,
                parsed?.ComponentKey);
        }
        catch (JsonException)
        {
            return new WorkflowGraphData(new List<WorkflowNode>(), new List<WorkflowOption>(), null, new List<WorkflowParameter>(), null, null, null);
        }
    }

    /// <summary>Builds a full <see cref="Workflow"/> from a Workflows table row.</summary>
    public static Workflow FromRow(WorkflowEntity row)
    {
        if (row is null)
            throw new ArgumentNullException(nameof(row));

        var graph = FromGraphData(row.GraphJson);

        return new Workflow
        {
            Id = row.Id,
            Name = row.Name,
            Description = row.Description,
            Enabled = row.Enabled,
            Category = row.Category,
            Trigger = FromTrigger(row.TriggerJson),
            Nodes = graph.Nodes,
            Options = graph.Options,
            Parameters = graph.Parameters,
            EntryNodeId = graph.EntryNodeId,
            ErrorHandlerEntryId = graph.ErrorHandlerEntryId,
            ComponentKey = graph.ComponentKey,
            SourceCommandId = graph.SourceCommandId,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt
        };
    }

    /// <summary>Builds a saveable Workflows table row from a <see cref="Workflow"/>.</summary>
    public static WorkflowEntity ToRow(Workflow workflow)
    {
        if (workflow is null)
            throw new ArgumentNullException(nameof(workflow));

        return new WorkflowEntity
        {
            Id = workflow.Id,
            Name = string.IsNullOrWhiteSpace(workflow.Name) ? "Untitled workflow" : workflow.Name,
            Description = workflow.Description,
            Enabled = workflow.Enabled,
            TriggerJson = ToTrigger(workflow.Trigger),
            GraphJson = ToGraph(
                workflow.Nodes,
                workflow.Options,
                workflow.SourceCommandId,
                workflow.Parameters,
                workflow.EntryNodeId,
                workflow.ErrorHandlerEntryId,
                workflow.ComponentKey),
            Category = workflow.Category,
            CreatedAt = workflow.CreatedAt,
            UpdatedAt = workflow.UpdatedAt
        };
    }

    private static List<WorkflowNode> FromGraphCore(string json) =>
        JsonSerializer.Deserialize<List<WorkflowNode>>(json, Options) ?? new List<WorkflowNode>();

    private static GraphDocument ToDocument(
        IEnumerable<WorkflowNode>? nodes,
        IEnumerable<WorkflowOption>? options,
        string? sourceCommandId) =>
        ToDocument(nodes, options, sourceCommandId, null, null, null, null);

    private static GraphDocument ToDocument(
        IEnumerable<WorkflowNode>? nodes,
        IEnumerable<WorkflowOption>? options,
        string? sourceCommandId,
        IEnumerable<WorkflowParameter>? parameters,
        string? entryNodeId,
        string? errorHandlerEntryId,
        string? componentKey)
    {
        var parameterList = parameters?.ToList();

        return new GraphDocument
        {
            Nodes = nodes?.ToList() ?? new List<WorkflowNode>(),
            Options = options?.ToList() ?? new List<WorkflowOption>(),
            Parameters = parameterList is { Count: > 0 } ? parameterList : null,
            EntryNodeId = entryNodeId,
            ErrorHandlerEntryId = errorHandlerEntryId,
            ComponentKey = componentKey,
            SourceCommandId = sourceCommandId
        };
    }
}

/// <summary>
/// Everything deserialized from one GraphJson column value: the v2 document
/// sections plus the v1-compatible node list. Missing sections (old rows)
/// default to empty lists / null.
/// </summary>
/// <param name="Nodes">The node graph (entry node first).</param>
/// <param name="Options">Command-level constraints (cooldowns, permissions, channel scoping).</param>
/// <param name="SourceCommandId">Command row this flow was created from / manages; null on v1 rows.</param>
/// <param name="Parameters">Typed command parameters; empty when absent.</param>
/// <param name="EntryNodeId">Explicit main-flow entry node; null = legacy detection.</param>
/// <param name="ErrorHandlerEntryId">First node of the Error Handler chain; null when none.</param>
/// <param name="ComponentKey">Component child workflow key; null for non-component flows.</param>
public sealed record WorkflowGraphData(
    List<WorkflowNode> Nodes,
    List<WorkflowOption> Options,
    string? SourceCommandId,
    List<WorkflowParameter> Parameters,
    string? EntryNodeId,
    string? ErrorHandlerEntryId,
    string? ComponentKey);
