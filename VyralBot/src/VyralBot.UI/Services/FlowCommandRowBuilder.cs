using VyralBot.Data.Entities;
using VyralBot.Workflows.Models;

namespace VyralBot.UI.Services;

/// <summary>
/// The Commands page lists COMMAND ROWS — a saved flow with a chat-command
/// trigger IS a custom command, so first-save materializes a (disabled,
/// flow-owned) row for it; the row is linked back to the flow via
/// <c>Workflow.SourceCommandId</c> so EDIT re-opens the flow.
/// </summary>
public static class FlowCommandRowBuilder
{
    private static readonly HashSet<string> CommandTriggerTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        WorkflowTriggerTypes.Command,
        WorkflowTriggerTypes.SlashCommand,
        WorkflowTriggerTypes.ModCommand
    };

    /// <summary>True when this flow needs a command row created on save.</summary>
    public static bool NeedsCommandRow(Workflow workflow) =>
        workflow.Trigger != null &&
        CommandTriggerTypes.Contains(workflow.Trigger.Type) &&
        string.IsNullOrWhiteSpace(workflow.SourceCommandId) &&
        !string.IsNullOrWhiteSpace(workflow.Trigger.CommandName);

    /// <summary>
    /// Builds the directory row for a command-triggered flow: disabled (the
    /// flow itself runs the command), never built-in, response text lifted
    /// from the first send-message block for the list display. An optional
    /// <paramref name="botId"/> pins the row to one Discord bot (editor opened
    /// from that bot's page); null keeps the "runs for every bot" scope.
    /// </summary>
    public static Command BuildRow(Workflow workflow, string? botId = null)
    {
        var now = DateTime.UtcNow;
        return new Command
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = workflow.Trigger!.CommandName!.Trim(),
            Response = FirstResponseText(workflow),
            Permission = PermissionOption(workflow),
            IsEnabled = false, // the flow owns the command — the row is the directory entry
            IsBuiltIn = false,
            BotId = string.IsNullOrWhiteSpace(botId) ? null : botId.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>Response shown in the list: first send-message block text, else the description.</summary>
    public static string FirstResponseText(Workflow workflow)
    {
        string? text = (workflow.Nodes ?? new List<WorkflowNode>())
            .FirstOrDefault(n => string.Equals(n.Type, "send_message", StringComparison.OrdinalIgnoreCase))
            ?.Config.GetValueOrDefault("text");
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text.Trim();
        }

        return workflow.Description ?? string.Empty;
    }

    private static string PermissionOption(Workflow workflow) =>
        (workflow.Options ?? new List<WorkflowOption>())
            .FirstOrDefault(o => string.Equals(o.Type, WorkflowOptionTypes.Permission, StringComparison.OrdinalIgnoreCase))
            ?.Value is { Length: > 0 } permission
            ? permission.Trim()
            : "Everyone";
}
