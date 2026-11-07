using VyralBot.Workflows.Models;
using VyralBot.Workflows.Persistence;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Marathon wave-2 persistence hardening for <see cref="WorkflowJson"/>:
/// old-JSON tolerance and corrupt-row tolerance.
///
/// NOTE ON "Platforms": WorkflowJson / Workflow / WorkflowTrigger /
/// WorkflowGraphData have NO "Platforms" field in the current tree — the
/// closest shipped fields are the SINGULAR WorkflowTrigger.Platform (a
/// nullable string, round-tripped below) and ModerationRuleSet.Platforms
/// (moderation subsystem, covered by ModerationRulesRepositoryTests). A
/// "Platforms round-trip" fix described against WorkflowJson has NOT landed;
/// these tests pin the tolerance contract that actually ships.
///
///   1. Round-trip: ToGraph → FromGraphData preserves nodes, options,
///      parameters and every graph-root id; ToRow → FromRow preserves the
///      whole workflow; ToTrigger → FromTrigger preserves the trigger
///      (including the singular Platform filter).
///   2. Old-JSON tolerance: a v2 document missing optional sections loads
///      with nulls/empty lists; a v1 bare node-array row loads nodes only.
///   3. Corrupt tolerance: malformed GraphJson yields EMPTY results, never
///      an exception (the v1/v2 detection itself must not throw either).
/// </summary>
public class WorkflowJsonToleranceTests
{
    private static WorkflowNode Node(string type, string? next = null) => new()
    {
        Type = type,
        Config = new Dictionary<string, string> { ["message"] = "hi" },
        Next = next
    };

    // ------------------------------------------------------------------
    // Full round-trip: GraphJson document
    // ------------------------------------------------------------------

    [Fact]
    public void GraphJson_FullDocument_RoundTrips_EverySection()
    {
        var node = Node("send_message");
        var options = new[] { new WorkflowOption { Type = WorkflowOptionTypes.Cooldown, Value = "30" } };
        var parameters = new[] { new WorkflowParameter { Name = "amount", Type = WorkflowParameterTypes.Number, Required = true } };

        var json = WorkflowJson.ToGraph(
            new[] { node }, options, "cmd-42", parameters,
            entryNodeId: node.Id, errorHandlerEntryId: "err1", componentKey: "vyralwf:p:n:btn0");

        var graph = WorkflowJson.FromGraphData(json);

        Assert.Equal(node.Id, Assert.Single(graph.Nodes).Id);
        Assert.Equal("send_message", graph.Nodes[0].Type);
        Assert.Equal("hi", graph.Nodes[0].Config["message"]);
        var option = Assert.Single(graph.Options);
        Assert.Equal(WorkflowOptionTypes.Cooldown, option.Type);
        Assert.Equal("30", option.Value);
        Assert.Equal("cmd-42", graph.SourceCommandId);
        var parameter = Assert.Single(graph.Parameters);
        Assert.Equal("amount", parameter.Name);
        Assert.Equal(node.Id, graph.EntryNodeId);
        Assert.Equal("err1", graph.ErrorHandlerEntryId);
        Assert.Equal("vyralwf:p:n:btn0", graph.ComponentKey);
    }

    // ------------------------------------------------------------------
    // Old-JSON tolerance: missing sections
    // ------------------------------------------------------------------

    [Fact]
    public void GraphJson_MinimalV2Document_MissingSections_AreNull()
    {
        // Hand-authored old v2 row: nodes only — the options/parameters/id
        // sections are entirely absent (unknown properties are ignored).
        const string json = """
            { "nodes": [ { "id": "n1", "type": "wait", "config": {}, "unknownFutureField": true } ] }
            """;

        var graph = WorkflowJson.FromGraphData(json);

        var node = Assert.Single(graph.Nodes);
        Assert.Equal("n1", node.Id);
        Assert.Equal("wait", node.Type);
        Assert.Empty(graph.Options);
        Assert.Empty(graph.Parameters);
        Assert.Null(graph.SourceCommandId);
        Assert.Null(graph.EntryNodeId);
        Assert.Null(graph.ErrorHandlerEntryId);
        Assert.Null(graph.ComponentKey);
    }

    [Fact]
    public void GraphJson_V1BareArray_LoadsNodesOnly()
    {
        var node = Node("send_message");
        var json = WorkflowJson.ToGraph(new[] { node }); // v1 shape: bare array

        var (nodes, options, sourceCommandId) = WorkflowJson.FromGraphFull(json);

        Assert.Equal(node.Id, Assert.Single(nodes).Id);
        Assert.Empty(options);
        Assert.Null(sourceCommandId); // v1 rows carry no source command id
    }

    // ------------------------------------------------------------------
    // Corrupt tolerance: malformed JSON → empty, not throw
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("{ not json at all")]
    [InlineData("{\"nodes\":")]                       // truncated mid-value
    [InlineData("[{\"type\": \"send_message\"")]      // truncated array
    public void GraphJson_Malformed_YieldsEmpty_NeverThrows(string corrupt)
    {
        var graph = WorkflowJson.FromGraphData(corrupt);

        Assert.NotNull(graph);
        Assert.Empty(graph.Nodes);
        Assert.Empty(graph.Options);
        Assert.Empty(graph.Parameters);
        Assert.Null(graph.SourceCommandId);
        Assert.Null(graph.EntryNodeId);
        Assert.Null(graph.ErrorHandlerEntryId);
        Assert.Null(graph.ComponentKey);
    }

    [Fact]
    public void GraphJson_NullOrWhitespace_YieldsEmpty()
    {
        foreach (var empty in new[] { null, "", "   " })
        {
            Assert.Empty(WorkflowJson.FromGraph(empty));
            Assert.Empty(WorkflowJson.FromGraphData(empty).Nodes);
        }
    }

    // ------------------------------------------------------------------
    // Trigger round-trip incl. the (singular) Platform filter
    // ------------------------------------------------------------------

    [Fact]
    public void TriggerJson_RoundTrips_PreservingThePlatformFilter()
    {
        var trigger = new WorkflowTrigger
        {
            Type = WorkflowTriggerTypes.Event,
            EventType = "Subscribe",
            Platform = "Kick"
        };

        var loaded = WorkflowJson.FromTrigger(WorkflowJson.ToTrigger(trigger));

        Assert.Equal(WorkflowTriggerTypes.Event, loaded.Type);
        Assert.Equal("Subscribe", loaded.EventType);
        Assert.Equal("Kick", loaded.Platform);
    }

    [Fact]
    public void TriggerJson_AbsentPlatformField_LoadsAsNull()
    {
        var loaded = WorkflowJson.FromTrigger("{\"type\":\"command\",\"commandName\":\"hello\"}");

        Assert.Equal(WorkflowTriggerTypes.Command, loaded.Type);
        Assert.Equal("hello", loaded.CommandName);
        // Old trigger rows without a platform filter stay unfiltered (null = any).
        Assert.Null(loaded.Platform);
    }

    [Fact]
    public void TriggerJson_Empty_YieldsFreshManualTrigger()
    {
        var loaded = WorkflowJson.FromTrigger(null);

        Assert.Equal(WorkflowTriggerTypes.Manual, loaded.Type);
        Assert.Null(loaded.Platform);
    }

    // ------------------------------------------------------------------
    // Whole-row round-trip (Workflows table row)
    // ------------------------------------------------------------------

    [Fact]
    public void Row_RoundTrip_PreservesWorkflowVerbatim()
    {
        var node = Node("send_message", next: null);
        var workflow = new Workflow
        {
            Id = "abc123",
            Name = "Test flow",
            Category = "Fun",
            Trigger = new WorkflowTrigger { Type = WorkflowTriggerTypes.Command, CommandName = "hello", Platform = "Twitch" },
            Nodes = new List<WorkflowNode> { node },
            Options = new List<WorkflowOption> { new() { Type = WorkflowOptionTypes.Permission, Value = "Moderator" } },
            Parameters = new List<WorkflowParameter> { new() { Name = "target", Type = WorkflowParameterTypes.User } },
            EntryNodeId = node.Id,
            SourceCommandId = "cmd-7"
        };

        var row = WorkflowJson.ToRow(workflow);
        var loaded = WorkflowJson.FromRow(row);

        Assert.Equal(workflow.Id, loaded.Id);
        Assert.Equal(workflow.Name, loaded.Name);
        Assert.Equal("Twitch", loaded.Trigger.Platform);
        Assert.Equal(node.Id, Assert.Single(loaded.Nodes).Id);
        Assert.Equal("Moderator", Assert.Single(loaded.Options).Value);
        Assert.Equal("target", Assert.Single(loaded.Parameters).Name);
        Assert.Equal(node.Id, loaded.EntryNodeId);
        Assert.Equal("cmd-7", loaded.SourceCommandId);
    }
}
