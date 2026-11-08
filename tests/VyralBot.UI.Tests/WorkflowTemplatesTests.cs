using VyralBot.UI.Services;
using VyralBot.Workflows.Abstractions;
using VyralBot.Workflows.Models;
using VyralBot.Workflows.Persistence;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Schema regression coverage for the embedded starter workflow templates
/// (Automation page → TEMPLATES panel): every template's TriggerJson/GraphJson
/// must parse through the CURRENT <see cref="WorkflowJson"/> shapes, every node
/// type must exist in <see cref="BlockCatalog"/> (the same allow-list
/// <see cref="FlowSnapshotService"/> imports through), the wiring must resolve,
/// and <see cref="WorkflowTemplates.CreateWorkflow"/> must import DISABLED
/// under the "Template: X" name with the imported-from-template marker.
/// </summary>
public class WorkflowTemplatesTests
{
    private static WorkflowTemplate Get(string key) =>
        WorkflowTemplates.Templates.First(t => t.Key == key);

    [Fact]
    public void Catalog_HasEightUniqueTemplates_WithDisplayMetadata()
    {
        Assert.Equal(8, WorkflowTemplates.Templates.Count);
        Assert.Equal(WorkflowTemplates.Templates.Count,
            WorkflowTemplates.Templates.Select(t => t.Key).Distinct(StringComparer.Ordinal).Count());

        foreach (var template in WorkflowTemplates.Templates)
        {
            Assert.False(string.IsNullOrWhiteSpace(template.Key), $"{template.Key}: key");
            Assert.False(string.IsNullOrWhiteSpace(template.Name), $"{template.Key}: name");
            Assert.False(string.IsNullOrWhiteSpace(template.Description), $"{template.Key}: description");
            Assert.False(string.IsNullOrWhiteSpace(template.Platforms), $"{template.Key}: platforms");
        }
    }

    [Theory]
    [InlineData("follower-thanks")]
    [InlineData("sub-appreciation")]
    [InlineData("raid-welcome")]
    [InlineData("first-hello")]
    [InlineData("schedule-announcement")]
    [InlineData("reward-thanks")]
    [InlineData("superchat-shoutout")]
    [InlineData("mod-warn")]
    public void EachTemplate_ParsesViaWorkflowJson(string key)
    {
        var template = Get(key);

        var trigger = WorkflowJson.FromTrigger(template.TriggerJson);
        var graph = WorkflowJson.FromGraphData(template.GraphJson);

        Assert.NotEmpty(trigger.Type);
        Assert.NotEmpty(graph.Nodes);
    }

    [Theory]
    [InlineData("follower-thanks")]
    [InlineData("sub-appreciation")]
    [InlineData("raid-welcome")]
    [InlineData("first-hello")]
    [InlineData("schedule-announcement")]
    [InlineData("reward-thanks")]
    [InlineData("superchat-shoutout")]
    [InlineData("mod-warn")]
    public void EachTemplate_NodeTypes_ExistInBlockCatalog(string key)
    {
        var graph = WorkflowJson.FromGraphData(Get(key).GraphJson);

        foreach (var node in graph.Nodes)
        {
            Assert.False(string.IsNullOrWhiteSpace(node.Id), $"{key}: node id");
            var block = BlockCatalog.Find(node.Type);
            Assert.True(block != null, $"{key}: node type '{node.Type}' is not in BlockCatalog");
        }
    }

    [Theory]
    [InlineData("follower-thanks")]
    [InlineData("sub-appreciation")]
    [InlineData("raid-welcome")]
    [InlineData("first-hello")]
    [InlineData("schedule-announcement")]
    [InlineData("reward-thanks")]
    [InlineData("superchat-shoutout")]
    [InlineData("mod-warn")]
    public void EachTemplate_WiringResolves_AndNodesAreConfigured(string key)
    {
        var graph = WorkflowJson.FromGraphData(Get(key).GraphJson);
        var ids = graph.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);

        Assert.True(graph.EntryNodeId != null && ids.Contains(graph.EntryNodeId),
            $"{key}: entryNodeId must be set and resolve");

        foreach (var node in graph.Nodes)
        {
            Assert.True(node.Next == null || ids.Contains(node.Next), $"{key}: dangling Next on {node.Id}");

            // The node types the templates use must carry their load-bearing config.
            if (node.Type == "send_message")
            {
                Assert.True(node.Config.TryGetValue("text", out var text) && !string.IsNullOrWhiteSpace(text),
                    $"{key}: send_message {node.Id} has no text");
            }
            else if (node.Type == "wait")
            {
                Assert.True(node.Config.TryGetValue("seconds", out var seconds) && int.TryParse(seconds, out _),
                    $"{key}: wait {node.Id} has non-numeric seconds");
            }
            else if (node.Type == "play_sound")
            {
                Assert.True(node.Config.TryGetValue("SoundName", out var sound) && !string.IsNullOrWhiteSpace(sound),
                    $"{key}: play_sound {node.Id} has no SoundName");
            }
        }
    }

    [Theory]
    [InlineData("follower-thanks")]
    [InlineData("sub-appreciation")]
    [InlineData("raid-welcome")]
    [InlineData("first-hello")]
    [InlineData("schedule-announcement")]
    [InlineData("reward-thanks")]
    [InlineData("superchat-shoutout")]
    [InlineData("mod-warn")]
    public void EachTemplate_TriggerIsComplete(string key)
    {
        var trigger = WorkflowJson.FromTrigger(Get(key).TriggerJson);

        switch (trigger.Type)
        {
            case WorkflowTriggerTypes.Command:
                Assert.False(string.IsNullOrWhiteSpace(trigger.CommandName), $"{key}: command trigger needs a name");
                break;
            case WorkflowTriggerTypes.Timer:
                Assert.True(trigger.IntervalMinutes is >= 1, $"{key}: timer trigger needs an interval >= 1 minute");
                break;
            case WorkflowTriggerTypes.Event:
                Assert.False(string.IsNullOrWhiteSpace(trigger.EventType), $"{key}: event trigger needs an event type");
                Assert.False(string.IsNullOrWhiteSpace(trigger.Platform), $"{key}: event trigger needs a platform");
                break;
            default:
                Assert.Fail($"{key}: unexpected trigger type '{trigger.Type}'");
                break;
        }
    }

    [Theory]
    [InlineData("follower-thanks")]
    [InlineData("sub-appreciation")]
    [InlineData("raid-welcome")]
    [InlineData("first-hello")]
    [InlineData("schedule-announcement")]
    [InlineData("reward-thanks")]
    [InlineData("superchat-shoutout")]
    [InlineData("mod-warn")]
    public void CreateWorkflow_ImportsDisabled_UnderTemplateName_WithMarker(string key)
    {
        var workflow = WorkflowTemplates.CreateWorkflow(Get(key));

        Assert.NotNull(workflow);
        Assert.False(workflow!.Enabled); // disabled by default — user arms it after editing
        Assert.Equal(WorkflowTemplates.ImportedNamePrefix + Get(key).Name, workflow.Name);
        Assert.Contains(WorkflowTemplates.ImportedMarker, workflow.Description, StringComparison.Ordinal);
        Assert.True(Guid.TryParse(workflow.Id, out _), $"{key}: imported workflow needs a fresh guid id");
        Assert.NotEmpty(workflow.Nodes);
    }

    [Fact]
    public void CreateWorkflow_RoundTripsThroughWorkflowRow()
    {
        // The import path saves via WorkflowJson.ToRow — prove the row shape
        // preserves trigger, graph, options and (for !warn) the Moderator gate.
        var warn = WorkflowTemplates.CreateWorkflow(Get("mod-warn"));
        Assert.NotNull(warn);

        var row = WorkflowJson.ToRow(warn!);
        var reloaded = WorkflowJson.FromRow(row);

        Assert.Equal(warn!.Name, reloaded.Name);
        Assert.Equal(warn.Enabled, reloaded.Enabled);
        Assert.Equal(WorkflowTriggerTypes.Command, reloaded.Trigger.Type);
        Assert.Equal("warn", reloaded.Trigger.CommandName, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(reloaded.Options, o =>
            string.Equals(o.Type, WorkflowOptionTypes.Permission, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(o.Value, "Moderator", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(reloaded.Nodes, n => n.Type == "send_message");
    }

    [Fact]
    public void Templates_PinTheSpecd_TriggerKeyFacts()
    {
        var byKey = WorkflowTemplates.Templates.ToDictionary(t => t.Key, StringComparer.Ordinal);

        WorkflowTrigger Trigger(string key) => WorkflowJson.FromTrigger(byKey[key].TriggerJson);

        Assert.Equal("follow", Trigger("follower-thanks").EventType);
        Assert.Equal("subscribe", Trigger("sub-appreciation").EventType);
        Assert.Equal("raid", Trigger("raid-welcome").EventType);
        Assert.Equal(WorkflowTriggerTypes.Command, Trigger("first-hello").Type);
        Assert.Equal("hello", Trigger("first-hello").CommandName);
        Assert.Equal(WorkflowTriggerTypes.Timer, Trigger("schedule-announcement").Type);
        Assert.Equal(60, Trigger("schedule-announcement").IntervalMinutes);
        Assert.Equal("rewardredeemed", Trigger("reward-thanks").EventType);
        Assert.Equal("superchat", Trigger("superchat-shoutout").EventType);
        Assert.Equal("YouTube", Trigger("superchat-shoutout").Platform);
        Assert.Equal(WorkflowTriggerTypes.Command, Trigger("mod-warn").Type);
        Assert.Equal("warn", Trigger("mod-warn").CommandName);
    }

    [Fact]
    public void Wave10_SubAppreciation_PlaysSound_InsteadOfDelayedSecondMessage()
    {
        // Wave 10 shipped the play_sound block, so the sub template now plays
        // the Sounds library's subscribe.wav followed by one thank-you message —
        // no more wait+second-message workaround.
        Assert.NotNull(BlockCatalog.Find("play_sound"));

        var graph = WorkflowJson.FromGraphData(Get("sub-appreciation").GraphJson);

        Assert.Single(graph.Nodes, n => n.Type == "play_sound");
        Assert.Single(graph.Nodes, n => n.Type == "send_message");
        Assert.Equal(2, graph.Nodes.Count);

        var sound = graph.Nodes.Single(n => n.Type == "play_sound");
        Assert.Equal("subscribe.wav", sound.Config["SoundName"]);
        Assert.True(sound.Next != null && graph.Nodes.Any(n => n.Id == sound.Next),
            "sub-appreciation: play_sound must wire into the thank-you message");
    }
}
