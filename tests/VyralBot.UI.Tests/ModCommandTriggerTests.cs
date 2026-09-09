using VyralBot.Data;
using VyralBot.Workflows.Models;
using VyralBot.Workflows.Persistence;
using Xunit;

namespace VyralBot.UI.Tests;

/// <summary>
/// Task 1 of the "moderator commands as workflows" plan: the
/// <see cref="WorkflowTriggerTypes.ModCommand"/> trigger type plus the
/// <see cref="WorkflowTrigger.MinimumTier"/> field (canonical ladder
/// TrialMod &lt; Mod &lt; SeniorMod &lt; Admin — see ModTierTests).
///
/// Covers the persistence contract (TriggerJson round-trip survives Type +
/// CommandName + MinimumTier) and the workflow-list row label that surfaces
/// "!mod &lt;verb&gt; (&lt;tier&gt;)". The flow-editor form-side normalization
/// (unknown/empty MinimumTier -> "Mod") lives in FlowEditorViewModel.BuildTriggerDto,
/// which is private on a pinned-DI view model — verified live.
/// </summary>
public class ModCommandTriggerTests
{
    [Fact]
    public void ModCommand_TriggerTypeConstant_IsStableString()
    {
        // WorkflowTriggerService (runtime dispatch, plan task 2) keys on this exact string.
        Assert.Equal("mod_command", WorkflowTriggerTypes.ModCommand);
    }

    [Fact]
    public void WorkflowTrigger_DefaultMinimumTier_IsMod()
    {
        Assert.Equal("Mod", new WorkflowTrigger().MinimumTier);
    }

    [Fact]
    public void WorkflowJson_RoundTrips_ModCommandTrigger()
    {
        var workflow = new Workflow
        {
            Name = "Hype",
            Trigger = new WorkflowTrigger
            {
                Type = WorkflowTriggerTypes.ModCommand,
                CommandName = "hype",
                CommandPrefix = "!",
                MinimumTier = "SeniorMod"
            }
        };

        WorkflowEntity row = WorkflowJson.ToRow(workflow);
        Workflow reloaded = WorkflowJson.FromRow(row);

        Assert.Equal(WorkflowTriggerTypes.ModCommand, reloaded.Trigger.Type);
        Assert.Equal("hype", reloaded.Trigger.CommandName);
        Assert.Equal("SeniorMod", reloaded.Trigger.MinimumTier);
    }

    [Fact]
    public void TriggerLabel_ShowsVerbAndTier_ForModCommand()
    {
        var flow = new Workflow
        {
            Name = "Hype",
            Trigger = new WorkflowTrigger
            {
                Type = WorkflowTriggerTypes.ModCommand,
                CommandName = "hype",
                MinimumTier = "SeniorMod"
            }
        };
        var row = new VyralBot.UI.ViewModels.FlowWorkflowRowViewModel(
            new WorkflowEntity { Id = flow.Id, Name = flow.Name }, flow);

        Assert.Equal("!mod hype (SeniorMod)", row.TriggerLabel);
    }

    [Fact]
    public void TriggerLabel_UnknownOrEmptyTier_FallsBackToMod()
    {
        var flow = new Workflow
        {
            Name = "Hype",
            Trigger = new WorkflowTrigger
            {
                Type = WorkflowTriggerTypes.ModCommand,
                CommandName = "hype",
                MinimumTier = "banana"
            }
        };
        var row = new VyralBot.UI.ViewModels.FlowWorkflowRowViewModel(
            new WorkflowEntity { Id = flow.Id, Name = flow.Name }, flow);

        Assert.Equal("!mod hype (Mod)", row.TriggerLabel);
    }
}
