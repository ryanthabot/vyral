using VyralBot.Workflows.Models;
using VyralBot.Workflows.Nodes;

namespace VyralBot.Workflows.Abstractions;

/// <summary>Parameter field kinds rendered by the flow editor's settings panel.</summary>
public static class ParamKind
{
    public const string Text = "text";
    public const string Multiline = "multiline";
    public const string Number = "number";
    public const string Boolean = "boolean";
    public const string Select = "select";
    public const string Role = "role";         // dropdown fed by IDiscordWorkflowBridge.GetRoleNamesAsync
    public const string Channel = "channel";   // dropdown fed by IDiscordWorkflowBridge.GetChannelNamesAsync
    public const string Color = "color";       // hex color picker
    public const string Embed = "embed";       // embed builder with live preview
    public const string Rule = "rule";         // structured condition rule builder (JSON)
    public const string Comparisons = "comparisons"; // multi-rule comparison rules list (JSON array of {left, operator, right})
}

/// <summary>One configurable field of a block, rendered by the editor's settings panel.</summary>
public class ParamField
{
    /// <summary>WorkflowNode.Config key.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Label shown in the settings panel.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>One of <see cref="ParamKind"/>.</summary>
    public string Kind { get; set; } = ParamKind.Text;

    /// <summary>Watermark/hint text (also documents {variable} substitution support).</summary>
    public string? Hint { get; set; }

    /// <summary>Choices for Select fields ("Value|Label" per entry, label optional).</summary>
    public string[]? Choices { get; set; }

    /// <summary>Default config value for newly placed nodes.</summary>
    public string? DefaultValue { get; set; }

    public ParamField(string key, string label, string kind, string? hint = null, string[]? choices = null, string? defaultValue = null)
    {
        Key = key;
        Label = label;
        Kind = kind;
        Hint = hint;
        Choices = choices;
        DefaultValue = defaultValue;
    }
}

/// <summary>Palette groups shown as editor tabs.</summary>
public static class BlockGroup
{
    public const string Action = "action";         // blue
    public const string Condition = "condition";   // green (BotGhost colors the whole condition family green)
}

/// <summary>
/// Palette section headers rendered above a block inside its tab (BotGhost-style
/// group headers such as "Message Actions" / "Role Actions"). The conditions tab
/// is a flat list in BotGhost, so condition blocks use an empty section (no header).
/// </summary>
public static class BlockSections
{
    public const string Messaging = "Messaging";
    public const string ThreadsAndChannels = "Threads & Channels";
    public const string Roles = "Roles";
    public const string Moderation = "Moderation";
    public const string VariablesAndMath = "Variables & Math";
    public const string Economy = "Economy";
    public const string Loops = "Loops";
    public const string Utility = "Utility";
}

/// <summary>
/// Static catalog describing every placeable block: the palette (icon, title,
/// description, group, section header, color), the settings panel (parameter
/// fields), the canvas node visuals (ports) and the purple Options-tab entries.
/// Single source of truth shared by the canvas control, the palette and the
/// settings panel. Runtime behavior lives in the matching INode implementations
/// (Nodes/).
/// </summary>
public static class BlockCatalog
{
    /// <summary>Category accent colors (visual-editor dark theme, BotGhost coding).</summary>
    public const string ActionColor = "#3B82F6";     // actions (blue)
    public const string ConditionColor = "#10B981";  // conditions AND their True/False ports (green)
    public const string LogicColor = "#8B5CF6";      // options (purple)

    /// <summary>
    /// Grey used for main-flow "next" wires/ports (per the reference screenshots:
    /// ports stay drawn but main-flow wires go grey; conditions stay green,
    /// error/default stay amber).
    /// </summary>
    public const string FlowWireColor = "#9CA3AF";

    /// <summary>All placeable blocks in palette order.</summary>
    public static IReadOnlyList<BlockDefinition> Blocks { get; } = new List<BlockDefinition>
    {
        // ============================== ACTIONS ==============================
        new("send_message", BlockGroup.Action, "📨", "Send Message",
            "Send a text message to the origin channel (or an override channel). Supports {variables}.",
            ActionColor, false, BlockSections.Messaging,
            new ParamField("text", "Message", ParamKind.Multiline, "Supports {username}, {channel}, {args}, ...")),
        new("send_embed", BlockGroup.Action, "🖼️", "Send Embed",
            "Send a rich embed (title, description, color, fields, footer) with a live Discord preview.",
            ActionColor, false, BlockSections.Messaging,
            new ParamField("Embed", "Embed", ParamKind.Embed),
            new ParamField("ChannelOverride", "Channel override", ParamKind.Channel, "Optional — empty sends to the origin channel")),
        new("send_dm", BlockGroup.Action, "✉️", "Send DM",
            "Direct-message the triggering user (Discord).",
            ActionColor, false, BlockSections.Messaging,
            new ParamField("Content", "Message", ParamKind.Multiline, "Supports {variables}"),
            new ParamField("Username", "Recipient", ParamKind.Text, "Defaults to {username}")),
        new("edit_message", BlockGroup.Action, "✏️", "Edit Bot Message",
            "Edit the bot's most recent sent message in the origin channel.",
            ActionColor, false, BlockSections.Messaging,
            new ParamField("NewContent", "New content", ParamKind.Multiline, "Supports {variables}")),
        new("delete_message", BlockGroup.Action, "🗑️", "Delete Message",
            "Delete the triggering message or the bot's last sent message.",
            ActionColor, false, BlockSections.Messaging,
            new ParamField("Scope", "Scope", ParamKind.Select, null,
                new[] { "trigger|Triggering message", "last_bot|Bot's last message" }, "last_bot")),
        new("react", BlockGroup.Action, "➕", "React to Message",
            "Add an emoji reaction to the triggering or bot's last message (Discord).",
            ActionColor, false, BlockSections.Messaging,
            new ParamField("Emoji", "Emoji", ParamKind.Text, "e.g. ✅ or a custom emoji"),
            new ParamField("Scope", "Scope", ParamKind.Select, null,
                new[] { "trigger|Triggering message", "last_bot|Bot's last message" }, "trigger")),
        new("add_role", BlockGroup.Action, "🏷️", "Add Role",
            "Give the triggering user a role (Discord).",
            ActionColor, false, BlockSections.Roles,
            new ParamField("Role", "Role", ParamKind.Role),
            new ParamField("Username", "User", ParamKind.Text, "Defaults to {username}"),
            new ParamField("Reason", "Reason", ParamKind.Text)),
        new("remove_role", BlockGroup.Action, "🏷️", "Remove Role",
            "Take a role from the triggering user (Discord).",
            ActionColor, false, BlockSections.Roles,
            new ParamField("Role", "Role", ParamKind.Role),
            new ParamField("Username", "User", ParamKind.Text, "Defaults to {username}"),
            new ParamField("Reason", "Reason", ParamKind.Text)),
        new("create_channel", BlockGroup.Action, "📁", "Create Channel",
            "Create a text/voice/forum channel in the origin guild (Discord).",
            ActionColor, false, BlockSections.ThreadsAndChannels,
            new ParamField("Name", "Channel name", ParamKind.Text, "Supports {variables}"),
            new ParamField("Type", "Type", ParamKind.Select, null,
                new[] { "Text", "Voice", "Forum" }, "Text"),
            new ParamField("Category", "Category", ParamKind.Text, "Optional category name")),
        new("create_thread", BlockGroup.Action, "🧵", "Create Thread",
            "Create a thread from the bot's last message (Discord).",
            ActionColor, false, BlockSections.ThreadsAndChannels,
            new ParamField("Name", "Thread name", ParamKind.Text, "Supports {variables}"),
            new ParamField("AutoArchiveMinutes", "Auto-archive (minutes)", ParamKind.Number, null, null, "60")),
        new("lock_channel", BlockGroup.Action, "🔒", "Lock / Unlock Channel",
            "Lock (deny @everyone SendMessages) or unlock the origin channel (Discord).",
            ActionColor, false, BlockSections.ThreadsAndChannels,
            new ParamField("Mode", "Mode", ParamKind.Select, null,
                new[] { "Lock", "Unlock" }, "Lock"),
            new ParamField("Reason", "Reason", ParamKind.Text)),
        new("wait_for_response", BlockGroup.Action, "⏳", "Wait for Response",
            "Pause until the triggering user posts another message (same channel). Stored as {response}; timeout takes the error port.",
            ActionColor, true, BlockSections.Messaging,
            new ParamField("TimeoutSeconds", "Timeout (seconds)", ParamKind.Number, null, null, "30")),
        new("http_request", BlockGroup.Action, "🌐", "HTTP Request",
            "Call an external API. Response body stored as {http_response}, status as {http_status}.",
            ActionColor, false, BlockSections.Utility,
            new ParamField("Method", "Method", ParamKind.Select, null,
                new[] { "GET", "POST", "PUT", "DELETE" }, "GET"),
            new ParamField("Url", "URL", ParamKind.Text, "https://api.example.com/... — supports {variables}"),
            new ParamField("Headers", "Headers", ParamKind.Multiline, "One per line: Key: Value"),
            new ParamField("Body", "Body", ParamKind.Multiline, "Optional request body"),
            new ParamField("SaveAs", "Save response to", ParamKind.Text, "Variable name", null, "http_response")),
        new("set_variable", BlockGroup.Action, "💾", "Set Variable",
            "Store a value in an execution variable for later nodes ({name}).",
            ActionColor, false, BlockSections.VariablesAndMath,
            new ParamField("name", "Variable name", ParamKind.Text),
            new ParamField("value", "Value", ParamKind.Text, "Supports {variables}")),
        new("random_number", BlockGroup.Action, "🎲", "Random Number",
            "Roll a random integer between min and max; stored as {random} (or custom name).",
            ActionColor, false, BlockSections.VariablesAndMath,
            new ParamField("min", "Min", ParamKind.Number, null, null, "1"),
            new ParamField("max", "Max", ParamKind.Number, null, null, "100"),
            new ParamField("store", "Save as", ParamKind.Text, "Variable name", null, "random")),
        new("add_currency", BlockGroup.Action, "🪙", "Add Currency",
            "Award currency to a user via the existing economy system.",
            ActionColor, false, BlockSections.Economy,
            new ParamField("username", "User", ParamKind.Text, "Defaults to {username}"),
            new ParamField("amount", "Amount", ParamKind.Number, null, null, "10")),
        new("remove_currency", BlockGroup.Action, "🪙", "Remove Currency",
            "Deduct currency from a user via the existing economy system.",
            ActionColor, false, BlockSections.Economy,
            new ParamField("username", "User", ParamKind.Text, "Defaults to {username}"),
            new ParamField("amount", "Amount", ParamKind.Number, null, null, "10")),
        new("wait", BlockGroup.Action, "⏱️", "Delay",
            "Pause the flow for a fixed duration.",
            ActionColor, false, BlockSections.Utility,
            new ParamField("seconds", "Seconds", ParamKind.Number, null, null, "5")),
        new("builtin", BlockGroup.Action, "🧩", "Run Built-in Command",
            "Run a legacy built-in command (Commands table engine) and store its reply: cooldowns, permissions and variables all apply.",
            ActionColor, false, BlockSections.Moderation,
            new ParamField("Name", "Command", ParamKind.Text, "Built-in command name without prefix, e.g. uptime"),
            new ParamField("Args", "Arguments", ParamKind.Text, "Supports {variables}", null, "{args}"),
            new ParamField("OutputVariable", "Save reply to", ParamKind.Text, "Variable name", null, "builtin_response")),
        new("moderate", BlockGroup.Action, "🔨", "Moderate Member",
            "Discord moderation action: kick, ban, unban, timeout, remove timeout, purge or slowmode. Success flows to the next node (add a confirmation there if wanted).",
            ActionColor, false, BlockSections.Moderation,
            new ParamField("Action", "Action", ParamKind.Select, null,
                new[]
                {
                    "Kick|Kick",
                    "Ban|Ban",
                    "Unban|Unban",
                    "Timeout|Timeout",
                    "Remove Timeout|Remove Timeout",
                    "Purge|Purge",
                    "Slowmode|Slowmode"
                }, "Kick"),
            new ParamField("User", "User", ParamKind.Text, "Target member — supports {variables}; unused by Purge/Slowmode", null, "{username}"),
            new ParamField("Minutes", "Timeout minutes", ParamKind.Number, null, null, "10"),
            new ParamField("Count", "Purge count", ParamKind.Number, null, null, "10"),
            new ParamField("Seconds", "Slowmode seconds", ParamKind.Number, null, null, "10"),
            new ParamField("Reason", "Reason", ParamKind.Text)),
        new("log", BlockGroup.Action, "📝", "Log",
            "Write a message to the engine log (debugging).",
            ActionColor, false, BlockSections.Utility,
            new ParamField("text", "Message", ParamKind.Multiline, "Supports {variables}")),
        new("note", BlockGroup.Action, "🗒️", "Note",
            "Write a note in the tree (annotation only — does nothing when the flow runs).",
            ActionColor, false, BlockSections.Utility,
            new ParamField("Text", "Note text", ParamKind.Multiline)),
        new("loop", BlockGroup.Action, "🔁", "Run a Loop",
            "Loop through actions and conditions a set amount of times. The body hangs off the 'Loop times' port.",
            ActionColor, false, BlockSections.Loops,
            new ParamField("Count", "Count", ParamKind.Number, "Iterations (1-25); supports {variables}", null, "5")),
        new("stop_loop", BlockGroup.Action, "⏹️", "Stop a Loop",
            "Break out of and stop any active loop.",
            ActionColor, false, BlockSections.Loops),

        // ============================ CONDITIONS ============================
        new("if", BlockGroup.Condition, "❓", "If (Expression)",
            "Branch on a guard expression: {coins} > 100, {message} contains 'hello', and/or chains.",
            ConditionColor, true, "",
            new ParamField("Expression", "Expression", ParamKind.Rule)),
        new("if_user_has_role", BlockGroup.Condition, "❓", "If User Has Role",
            "Branch on Discord role membership of the triggering user.",
            ConditionColor, true, "",
            new ParamField("Role", "Role", ParamKind.Role),
            new ParamField("Username", "User", ParamKind.Text, "Defaults to {username}")),
        new("if_message_contains", BlockGroup.Condition, "❓", "If Message Contains",
            "Branch on the trigger message text.",
            ConditionColor, true, "",
            new ParamField("Text", "Contains", ParamKind.Text),
            new ParamField("CaseSensitive", "Case sensitive", ParamKind.Boolean)),
        new("if_variable_equals", BlockGroup.Condition, "❓", "If Variable Equals",
            "Branch on a stored variable's value.",
            ConditionColor, true, "",
            new ParamField("Name", "Variable", ParamKind.Text, "e.g. response"),
            new ParamField("Value", "Equals", ParamKind.Text, "Supports {variables}")),
        new("if_time_between", BlockGroup.Condition, "❓", "If Time Between",
            "Branch on the current local hour (e.g. night mode).",
            ConditionColor, true, "",
            new ParamField("StartHour", "Start hour (0-23)", ParamKind.Number, null, null, "22"),
            new ParamField("EndHour", "End hour (0-23)", ParamKind.Number, null, null, "6")),
        new("if_user_is_bot", BlockGroup.Condition, "❓", "If User Is Bot",
            "Branch on whether the triggering user is a bot account.",
            ConditionColor, true, ""),
        // NOTE: "switch" is intentionally not placeable anymore — condition blocks
        // are the only branching blocks in the palette. The runtime keeps full
        // switch support (SwitchNode/WorkflowRuntime/Workflow.Branches) so
        // previously saved flows still execute and render on canvas.
        new("chance", BlockGroup.Condition, "🎲", "Chance Condition",
            "Run actions based on a random chance.",
            ConditionColor, true, "",
            new ParamField("Percent", "Chance %", ParamKind.Number, "0-100", null, "50")),
        new("comparison", BlockGroup.Condition, "≥", "Comparison Condition",
            "Run actions based on the difference between two values.",
            ConditionColor, true, "",
            new ParamField("Comparisons", "Comparisons", ParamKind.Comparisons)),
        new("if_user_permission", BlockGroup.Condition, "🔒", "Permissions Condition",
            "Run actions based on the server permissions of a user.",
            ConditionColor, true, "",
            new ParamField("Permission", "Permission", ParamKind.Text, "e.g. Administrator or ManageMessages"),
            new ParamField("Username", "User", ParamKind.Text, "Defaults to {username}")),
        new("if_channel", BlockGroup.Condition, "#", "Channel Condition",
            "Run actions based on the channel the command was used in.",
            ConditionColor, true, "",
            new ParamField("Channels", "Channels", ParamKind.Text, "Comma-separated channel names/ids, e.g. general, #support")),
        new("if_user", BlockGroup.Condition, "👤", "User Condition",
            "Run actions based on who used the command.",
            ConditionColor, true, "",
            new ParamField("Users", "Users", ParamKind.Text, "Comma-separated usernames")),
    };

    /// <summary>Looks a block up by its type key (case-insensitive). Null when unknown.</summary>
    public static BlockDefinition? Find(string blockType)
    {
        foreach (var block in Blocks)
        {
            if (string.Equals(block.Type, blockType, StringComparison.OrdinalIgnoreCase))
                return block;
        }
        return null;
    }

    /// <summary>
    /// The 14 canonical comparison operator keys in canonical (display) order —
    /// the choices for the comparison block's rules-list editor and the same set
    /// <see cref="Nodes.ComparisonNode.EvaluateRule"/> understands (legacy
    /// symbols normalize into this set).
    /// </summary>
    public static IReadOnlyList<string> ComparisonOperators { get; } = new[]
    {
        "lt", "lte", "gt", "gte", "eq", "neq",
        "contains", "not_contains",
        "starts_with", "ends_with", "not_starts_with", "not_ends_with",
        "collection_contains", "collection_not_contains"
    };

    /// <summary>Human phrase per canonical operator, inserted between left and right ("{left} … {right}").</summary>
    private static readonly Dictionary<string, string> ComparisonOperatorPhrases = new(StringComparer.Ordinal)
    {
        ["lt"] = "is less than",
        ["lte"] = "is less than or equal to",
        ["gt"] = "is greater than",
        ["gte"] = "is greater than or equal to",
        ["eq"] = "is equal to",
        ["neq"] = "is not equal to",
        ["contains"] = "contains",
        ["not_contains"] = "does not contain",
        ["starts_with"] = "starts with",
        ["ends_with"] = "ends with",
        ["not_starts_with"] = "does not start with",
        ["not_ends_with"] = "does not end with",
        ["collection_contains"] = "contains",
        ["collection_not_contains"] = "does not contain"
    };

    /// <summary>
    /// Human one-line phrase for one comparison rule, used as the editor's
    /// comparison case-card title, e.g. "10 is equal to 5". Legacy operator
    /// symbols ("==", "&gt;=", "equal", ...) normalize through the SAME mapping
    /// the evaluator uses (<see cref="Nodes.ComparisonNode.NormalizeOperator"/>);
    /// unknown operators read as "is equal to".
    /// </summary>
    public static string ComparisonRuleSummary(string left, string op, string right)
    {
        var canonical = Nodes.ComparisonNode.NormalizeOperator(op ?? string.Empty);
        var phrase = ComparisonOperatorPhrases.TryGetValue(canonical, out var known) ? known : "is equal to";
        return $"{left} {phrase} {right}";
    }

    /// <summary>
    /// The purple Options-tab entries (BotGhost-style), mapped onto the real
    /// command-level constraints the runtime enforces (see
    /// <see cref="WorkflowOptionTypes"/>). Each entry carries the label and value
    /// hint the editor's Options panel renders.
    /// </summary>
    public static IReadOnlyList<FlowOptionDefinition> OptionDefinitions { get; } = new List<FlowOptionDefinition>
    {
        new(WorkflowOptionTypes.Cooldown, "Cooldown", "Rate-limit the command per user.", "seconds, e.g. 30"),
        new(WorkflowOptionTypes.Permission, "Require Moderator", "Only allow moderators (or a specific role) to run the command.", "Moderator or role name"),
        new(WorkflowOptionTypes.ChannelRestriction, "Channel Restriction", "Only allow the command in specific channels.", "channel names, comma-separated"),
        new(WorkflowOptionTypes.DmOnly, "DM Only", "Only allow the command in DMs.", "true"),
        new(WorkflowOptionTypes.GuildOnly, "Guild Only", "Disable the command in DMs.", "true"),
        new(WorkflowOptionTypes.Nsfw, "NSFW Flag", "Mark the command as NSFW.", "true"),
    };

    /// <summary>Well-known output port keys used on canvas nodes and wires.</summary>
    public const string PortNext = "next";
    public const string PortTrue = "true";
    public const string PortFalse = "false";
    public const string PortError = "error";
    public const string PortDefault = "default";
    public const string PortCasePrefix = "case:";

    /// <summary>
    /// True for condition-family blocks whose False branch routes through the
    /// auto-attached "comparison_else" card: every <see cref="BlockDefinition.IsConditional"/>
    /// block in the <see cref="BlockGroup.Condition"/> group EXCEPT "comparison"
    /// (which manages its own case/Else ports — legacy True/False stay green,
    /// multi-rule mode replaces the ports entirely). "wait_for_response" is
    /// IsConditional but sits in the action group, so it is excluded too — its
    /// False branch is a normal green branch and its extra port is Error.
    /// Drives both the False port color below and the flow editor's
    /// Else-card auto-attach (FlowEditorViewModel.EnsureAttachmentCards).
    /// </summary>
    public static bool UsesElseAttachment(string blockType) =>
        Find(blockType) is { IsConditional: true } found &&
        string.Equals(found.Group, BlockGroup.Condition, StringComparison.Ordinal) &&
        !string.Equals(found.Type, "comparison", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Static output ports for a block type: ("next", grey) for actions (main-flow
    /// wires are grey per the reference screenshots); condition-family True stays
    /// in the condition green while False uses the attachment grey (#9CA3AF — the
    /// False wire routes through the auto-attached Else card and renders as the
    /// grey attachment elbow; the port labels differentiate); "comparison" keeps
    /// legacy True AND False both green (BotGhost wires every legacy comparison
    /// branch green), "wait_for_response" keeps True/False green plus Error
    /// (amber); Default
    /// (amber) for switch (no longer placeable in the palette, but kept so switch
    /// nodes from previously saved workflows still render) — the canvas derives
    /// per-case ports dynamically from the
    /// node's Cases config. "loop" exposes the grey "After loop" port plus the
    /// "case:body" port ("Loop times") that maps into BranchTargets/Branches["body"]
    /// via the canvas's case-port mechanics.
    /// </summary>
    public static IReadOnlyList<PortDescriptor> GetOutputs(string blockType)
    {
        if (string.Equals(blockType, "switch", StringComparison.OrdinalIgnoreCase))
        {
            return new List<PortDescriptor> { new(PortDefault, "Default", "#F59E0B") };
        }

        if (string.Equals(blockType, "loop", StringComparison.OrdinalIgnoreCase))
        {
            return new List<PortDescriptor>
            {
                new(PortNext, "After loop", FlowWireColor),
                new(PortCasePrefix + "body", "Loop times", FlowWireColor)
            };
        }

        var block = Find(blockType);
        if (block is { IsConditional: true })
        {
            var ports = new List<PortDescriptor>
            {
                new(PortTrue, "True", ConditionColor),
                // Condition-family False ports are the attachment grey (#9CA3AF):
                // the False branch routes through the auto-attached "comparison_else"
                // card, and a grey source port is what switches the canvas wire to
                // the BotGhost right-angle attachment elbow (see
                // FlowCanvasPalette.IsAttachmentColor). The "comparison" block keeps
                // its legacy green/green True/False (it manages its own case/else
                // ports in multi-rule mode) and "wait_for_response" keeps a normal
                // green False branch (its extra Error port stays amber) — both fall
                // out of UsesElseAttachment below.
                new(PortFalse, "False", UsesElseAttachment(blockType) ? FlowWireColor : ConditionColor)
            };
            if (string.Equals(blockType, "wait_for_response", StringComparison.OrdinalIgnoreCase))
            {
                ports.Add(new PortDescriptor(PortError, "Error", "#F59E0B"));
            }
            return ports;
        }

        return new List<PortDescriptor> { new(PortNext, "Next", FlowWireColor) };
    }
}

/// <summary>
/// One entry of the flow editor's purple Options tab: a command-level constraint
/// (cooldown, permission, channel scoping, ...) the user can attach to a
/// workflow. <see cref="Type"/> matches <see cref="WorkflowOptionTypes"/> and is
/// what gets persisted into WorkflowOption.Type.
/// </summary>
public sealed record FlowOptionDefinition(string Type, string Label, string Description, string ValueHint);

/// <summary>A placeable block in the visual editor palette.</summary>
public class BlockDefinition
{
    /// <summary>Node type key (matches an INode.Type in the registry).</summary>
    public string Type { get; }

    /// <summary>Palette tab: <see cref="BlockGroup.Action"/> or <see cref="BlockGroup.Condition"/>.</summary>
    public string Group { get; }

    /// <summary>Emoji icon shown on the palette entry and canvas node.</summary>
    public string Icon { get; }

    /// <summary>Short title.</summary>
    public string Title { get; }

    /// <summary>One-line description.</summary>
    public string Description { get; }

    /// <summary>Accent color (hex string) for the node's left border and palette chip.</summary>
    public string Color { get; }

    /// <summary>True when the block exposes True/False output ports (condition family).</summary>
    public bool IsConditional { get; }

    /// <summary>
    /// Palette section header rendered above this block inside its tab
    /// (BotGhost-style grouping, e.g. "Messaging"). Empty string = no header
    /// (the conditions tab is a flat list).
    /// </summary>
    public string Section { get; }

    /// <summary>Settings-panel fields in display order.</summary>
    public IReadOnlyList<ParamField> Params { get; }

    public BlockDefinition(
        string type, string group, string icon, string title, string description,
        string color, bool isConditional, string section, params ParamField[] parameters)
    {
        Type = type;
        Group = group;
        Icon = icon;
        Title = title;
        Description = description;
        Color = color;
        IsConditional = isConditional;
        Section = section ?? string.Empty;
        Params = parameters;
    }
}

/// <summary>Describes one output port of a canvas node.</summary>
public readonly record struct PortDescriptor(string Key, string Label, string Color);
