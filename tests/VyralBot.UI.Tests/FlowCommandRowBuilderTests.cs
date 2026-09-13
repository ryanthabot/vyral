using VyralBot.Data.Entities;
using VyralBot.UI.Services;
using VyralBot.Workflows.Models;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Saving a command-triggered flow must materialize a (disabled, flow-owned)
/// Command row so it shows in the Commands page CUSTOM list — the regression
/// guard for "saved commands don't show up".
/// </summary>
public class FlowCommandRowBuilderTests
{
    private static Workflow CommandFlow(string commandName = "sayno", string? sourceCommandId = null, string? description = null)
    {
        var workflow = new Workflow
        {
            Id = "flow1",
            Name = "Say No",
            Description = description,
            Trigger = new WorkflowTrigger
            {
                Type = WorkflowTriggerTypes.Command,
                CommandName = commandName,
                CommandPrefix = "!"
            },
            SourceCommandId = sourceCommandId
        };
        workflow.Nodes.Add(new WorkflowNode
        {
            Id = "n1",
            Type = "send_message",
            Config = new Dictionary<string, string> { ["text"] = "no way {username}" }
        });
        return workflow;
    }

    [Fact]
    public void Command_trigger_without_source_row_needs_one()
    {
        Assert.True(FlowCommandRowBuilder.NeedsCommandRow(CommandFlow()));
    }

    [Theory]
    [InlineData(WorkflowTriggerTypes.SlashCommand)]
    [InlineData(WorkflowTriggerTypes.ModCommand)]
    public void Slash_and_mod_command_triggers_also_need_a_row(string triggerType)
    {
        var workflow = CommandFlow();
        workflow.Trigger!.Type = triggerType;
        Assert.True(FlowCommandRowBuilder.NeedsCommandRow(workflow));
    }

    [Fact]
    public void Manual_trigger_needs_no_row()
    {
        var workflow = CommandFlow();
        workflow.Trigger!.Type = WorkflowTriggerTypes.Manual;
        Assert.False(FlowCommandRowBuilder.NeedsCommandRow(workflow));
    }

    [Fact]
    public void Already_linked_flow_needs_no_row()
    {
        Assert.False(FlowCommandRowBuilder.NeedsCommandRow(CommandFlow(sourceCommandId: "row123")));
    }

    [Fact]
    public void Missing_command_name_needs_no_row()
    {
        var workflow = CommandFlow(commandName: "");
        Assert.False(FlowCommandRowBuilder.NeedsCommandRow(workflow));
    }

    [Fact]
    public void Built_row_is_a_disabled_custom_entry_with_response_text()
    {
        Command row = FlowCommandRowBuilder.BuildRow(CommandFlow());

        Assert.Equal("sayno", row.Name);
        Assert.Equal("no way {username}", row.Response);
        Assert.False(row.IsEnabled); // the flow runs the command — the row is the directory entry
        Assert.False(row.IsBuiltIn);
        Assert.Equal("Everyone", row.Permission);
    }

    [Fact]
    public void Row_without_message_blocks_falls_back_to_description()
    {
        var workflow = CommandFlow(description: "says no");
        workflow.Nodes.Clear();

        Command row = FlowCommandRowBuilder.BuildRow(workflow);
        Assert.Equal("says no", row.Response);
    }

    [Fact]
    public void Permission_option_is_carried_into_the_row()
    {
        var workflow = CommandFlow();
        workflow.Options.Add(new WorkflowOption { Type = WorkflowOptionTypes.Permission, Value = "Moderator" });

        Assert.Equal("Moderator", FlowCommandRowBuilder.BuildRow(workflow).Permission);
    }
}
