namespace VyralBot.Workflows.Models;

/// <summary>Well-known workflow trigger type strings.</summary>
public static class WorkflowTriggerTypes
{
    public const string Command = "command";
    public const string Timer = "timer";
    public const string Event = "event";
    public const string Manual = "manual";

    // Discord interaction triggers (visual command editor).
    public const string SlashCommand = "slash_command";
    public const string ButtonClick = "button_click";
    public const string SelectMenu = "select_menu";
}

/// <summary>Well-known workflow command-option type strings (command-level constraints).</summary>
public static class WorkflowOptionTypes
{
    /// <summary>Per-user cooldown in seconds. Value = "30".</summary>
    public const string Cooldown = "cooldown";

    /// <summary>Required access. Value = "Everyone", "Moderator" or a Discord/Twitch role name.</summary>
    public const string Permission = "permission";

    /// <summary>Restrict to channels. Value = comma-separated channel names.</summary>
    public const string ChannelRestriction = "channel_restriction";

    /// <summary>Only usable in DMs. Value = "true".</summary>
    public const string DmOnly = "dm_only";

    /// <summary>Never usable in DMs. Value = "true".</summary>
    public const string GuildOnly = "guild_only";

    /// <summary>Marked NSFW (v1: informational). Value = "true".</summary>
    public const string Nsfw = "nsfw";
}

/// <summary>
/// A command-level constraint authored in the flow editor's Options panel
/// (cooldowns, permissions, channel restrictions, DM/guild scoping).
/// Serialized inside the workflow graph JSON — no schema changes required.
/// </summary>
public class WorkflowOption
{
    /// <summary>One of <see cref="WorkflowOptionTypes"/>.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Option value ("30", "Moderator", "#general,#support", "true", ...).</summary>
    public string Value { get; set; } = string.Empty;
}

/// <summary>Well-known workflow parameter type strings (typed command parameters).</summary>
public static class WorkflowParameterTypes
{
    public const string Text = "text";
    public const string Number = "number";
    public const string User = "user";
    public const string Channel = "channel";
    public const string Role = "role";
    public const string Choice = "choice";
    public const string Attachment = "attachment";
}

/// <summary>
/// A typed command parameter authored in the flow editor (BotGhost-style purple
/// "Options" entries): one slash-command argument the workflow accepts.
/// Serialized inside the workflow graph JSON — no schema changes required.
/// </summary>
public class WorkflowParameter
{
    /// <summary>Parameter name (single word, e.g. "amount"); exposed as a {variable}.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>One of <see cref="WorkflowParameterTypes"/>.</summary>
    public string Type { get; set; } = WorkflowParameterTypes.Text;

    /// <summary>Optional user-facing description.</summary>
    public string? Description { get; set; }

    /// <summary>Whether the parameter must be supplied for the command to run.</summary>
    public bool Required { get; set; } = true;
}

/// <summary>
/// A visual automation workflow (BotGhost/Node-RED style).
/// Persisted as JSON via <see cref="VyralBot.Workflows.Persistence.WorkflowJson"/>
/// (TriggerJson + GraphJson columns of the Workflows table).
/// Everything is data — no generated code.
/// </summary>
public class Workflow
{
    /// <summary>Stable ID (GUID "N" string), primary key of the Workflows table.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional user-facing description.</summary>
    public string? Description { get; set; }

    /// <summary>Whether the workflow's trigger is active.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Optional grouping category (e.g. "Moderation", "Fun").</summary>
    public string? Category { get; set; }

    /// <summary>What starts this workflow (command, timer, event or manual run).</summary>
    public WorkflowTrigger Trigger { get; set; } = new();

    /// <summary>
    /// Node graph. The entry node is <see cref="EntryNodeId"/> when set, else the
    /// first node that no other node routes to (see
    /// <see cref="VyralBot.Workflows.Runtime.WorkflowRuntime"/>); the visual
    /// editor keeps it first in the list as well.
    /// </summary>
    public List<WorkflowNode> Nodes { get; set; } = new();

    /// <summary>Command-level constraints (cooldowns, permissions, channel scoping).</summary>
    public List<WorkflowOption> Options { get; set; } = new();

    /// <summary>Typed command parameters (slash-command argument definitions).</summary>
    public List<WorkflowParameter> Parameters { get; set; } = new();

    /// <summary>
    /// Explicit main-flow entry node (the trigger wire target). Null = legacy
    /// detection (the first node no other node routes to — see
    /// <see cref="VyralBot.Workflows.Runtime.WorkflowRuntime"/>).
    /// </summary>
    public string? EntryNodeId { get; set; }

    /// <summary>First node of the Error Handler chain, run when a failing node has no error branch of its own.</summary>
    public string? ErrorHandlerEntryId { get; set; }

    /// <summary>
    /// Component child workflow: the component custom_id this workflow answers
    /// ("vyralwf:{parent}:{node}:btn0" / ":menu"). Null for non-component flows.
    /// </summary>
    public string? ComponentKey { get; set; }

    /// <summary>Command row this flow was created from / manages; null for pure workflows.</summary>
    public string? SourceCommandId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// The trigger definition of a workflow: what causes it to run.
/// Only the fields relevant to <see cref="Type"/> are populated.
/// </summary>
public class WorkflowTrigger
{
    /// <summary>One of <see cref="WorkflowTriggerTypes"/>: "command", "timer", "event" or "manual".</summary>
    public string Type { get; set; } = WorkflowTriggerTypes.Manual;

    /// <summary>Command name without prefix (command triggers), e.g. "hello".</summary>
    public string? CommandName { get; set; }

    /// <summary>Command prefix (command triggers), e.g. "!". Defaults to "!".</summary>
    public string? CommandPrefix { get; set; }

    /// <summary>Firing interval (timer triggers).</summary>
    public int? IntervalMinutes { get; set; }

    /// <summary>Event type (event triggers), e.g. "Follow", "Subscribe", "Raid", "ChatMessage".</summary>
    public string? EventType { get; set; }

    /// <summary>Platform filter: "Twitch", "Kick", "Discord", "YouTube" or "Any".</summary>
    public string? Platform { get; set; }

    /// <summary>
    /// Optional guard expression for event triggers, evaluated with
    /// <see cref="VyralBot.Workflows.Expressions.ExpressionEvaluator"/>
    /// (e.g. "{viewers} > 50").
    /// </summary>
    public string? Conditions { get; set; }
}

/// <summary>
/// A single node in the workflow graph. <see cref="Type"/> selects the
/// registered <see cref="VyralBot.Workflows.Nodes.INode"/> implementation;
/// <see cref="Config"/> carries its settings as simple string key/values.
/// Flow: non-branching nodes use <see cref="Next"/>; "if" nodes use
/// <see cref="BranchTrue"/>/<see cref="BranchFalse"/>. A null next id ends the run.
/// </summary>
public class WorkflowNode
{
    /// <summary>Node ID; referenced by Next/BranchTrue/BranchFalse of other nodes.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Node type, matching an INode.Type in the registry (e.g. "send_message").</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Node settings as string key/values (variable substitution applies per node).</summary>
    public Dictionary<string, string> Config { get; set; } = new();

    /// <summary>ID of the next node (null = end of workflow).</summary>
    public string? Next { get; set; }

    /// <summary>ID taken when the condition is true ("if" nodes).</summary>
    public string? BranchTrue { get; set; }

    /// <summary>ID taken when the condition is false ("if" nodes).</summary>
    public string? BranchFalse { get; set; }

    /// <summary>
    /// ID taken when the node fails (implicit "error" output). When unset, a
    /// failed node ends the run as today.
    /// </summary>
    public string? BranchError { get; set; }

    /// <summary>
    /// Per-case routing for "switch" nodes: matched case value → next node id.
    /// An unmatched value falls back to <see cref="BranchFalse"/> (default port).
    /// </summary>
    public Dictionary<string, string>? Branches { get; set; }

    /// <summary>Canvas X position (visual editor; unused by the runtime).</summary>
    public double X { get; set; }

    /// <summary>Canvas Y position (visual editor; unused by the runtime).</summary>
    public double Y { get; set; }
}
