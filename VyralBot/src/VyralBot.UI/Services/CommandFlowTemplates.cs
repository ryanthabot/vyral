using System.Globalization;
using System.Text.Json;
using VyralBot.Core.Services.Commands;
using VyralBot.Data;
using VyralBot.Data.Entities;
using VyralBot.Workflows.Models;

namespace VyralBot.UI.Services;

/// <summary>
/// Bridge between the legacy Commands table and the visual flow editor:
/// <see cref="FromCommand"/> converts a <see cref="Command"/> row into a
/// ready-to-edit <see cref="Workflow"/> template (command trigger, options and
/// a node graph reproducing what the command engine used to do).
///
/// The editor stamps the saved flow back to <see cref="Workflow.SourceCommandId"/>
/// and the adopting UI disables the Command row, so the flow — not the row —
/// answers from then on. Built-in Discord moderation commands convert to a
/// "moderate" node, other built-ins to a "builtin" node (the legacy engine is
/// invoked, so its permission/cooldown/variable behavior is preserved), and
/// custom commands are expanded: random replies become a
/// random_number → switch fan-out of send_message nodes, and the action
/// sequence (Command.ActionsJson) becomes the matching node chain.
///
/// Conversion never throws — a malformed row degrades to a minimal valid
/// command-triggered flow.
/// </summary>
public static class CommandFlowTemplates
{
    /// <summary>Trigger prefix used for every converted flow (the engine's default).</summary>
    private const string CommandPrefix = "!";

    /// <summary>Default confirmation for converted moderation flows without a stored response.</summary>
    private const string DefaultModerationConfirmation = "✅ {username}: done.";

    /// <summary>
    /// Discord moderation command names (as claimed by the built-in mod
    /// handler, prefix-stripped) mapped onto "moderate" node actions.
    /// </summary>
    private static readonly Dictionary<string, string> ModerationActions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["kick"] = "Kick",
        ["ban"] = "Ban",
        ["unban"] = "Unban",
        ["mute"] = "Timeout",
        ["timeout"] = "Timeout",
        ["unmute"] = "Remove Timeout",
        ["removetimeout"] = "Remove Timeout",
        ["purge"] = "Purge",
        ["clear"] = "Purge",
        ["slowmode"] = "Slowmode"
    };

    /// <summary>
    /// Converts a Command row into an editable workflow template. Never throws.
    /// </summary>
    public static Workflow FromCommand(Command command)
    {
        try
        {
            return Convert(command);
        }
        catch (Exception)
        {
            // Defensive: a malformed row (null fields, broken JSON, ...) must
            // never break the adopting UI — fall back to a minimal valid flow.
            var fallbackName = command is null || string.IsNullOrWhiteSpace(command.Name)
                ? "Imported command"
                : command.Name;

            return new Workflow
            {
                Name = fallbackName,
                Category = "Commands",
                Enabled = command?.IsEnabled ?? true,
                Trigger = BuildTrigger(command),
                SourceCommandId = command?.Id
            };
        }
    }

    // ----------------------------------------------------------------------
    // Conversion
    // ----------------------------------------------------------------------

    private static Workflow Convert(Command command)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        return new Workflow
        {
            Name = command.Name,
            Category = "Commands",
            Enabled = command.IsEnabled,
            Trigger = BuildTrigger(command),
            Options = BuildOptions(command),
            SourceCommandId = command.Id,
            Nodes = command.IsBuiltIn ? BuildBuiltInGraph(command) : BuildCustomGraph(command)
        };
    }

    /// <summary>
    /// The command trigger: bare name + "!" prefix. Platform filter — commands
    /// pinned to a Discord bot run on Discord only; otherwise the disabled
    /// platform flags decide (the enabled one wins), else any platform.
    /// </summary>
    private static WorkflowTrigger BuildTrigger(Command? command)
    {
        string? platform = null;
        if (!string.IsNullOrWhiteSpace(command?.BotId))
        {
            platform = "Discord";
        }
        else if (command is { EnabledOnTwitch: false })
        {
            platform = "Kick";
        }
        else if (command is { EnabledOnKick: false })
        {
            platform = "Twitch";
        }

        return new WorkflowTrigger
        {
            Type = WorkflowTriggerTypes.Command,
            CommandName = StripPrefix(command?.Name),
            CommandPrefix = CommandPrefix,
            Platform = platform
        };
    }

    /// <summary>
    /// Command-level options: ONE cooldown (per-user wins over global, matching
    /// how often it matters in practice) and the permission (Moderator/Mod/Mods
    /// normalize to "Moderator"; anything beyond Everyone stays as-is so role
    /// checks keep working).
    /// </summary>
    private static List<WorkflowOption> BuildOptions(Command command)
    {
        var options = new List<WorkflowOption>();

        var cooldown = command.UserCooldown > 0 ? command.UserCooldown : command.GlobalCooldown;
        if (cooldown > 0)
        {
            options.Add(new WorkflowOption
            {
                Type = WorkflowOptionTypes.Cooldown,
                Value = cooldown.ToString(CultureInfo.InvariantCulture)
            });
        }

        var permission = (command.Permission ?? string.Empty).Trim();
        if (permission.Length > 0 && !permission.Equals("Everyone", StringComparison.OrdinalIgnoreCase))
        {
            options.Add(new WorkflowOption
            {
                Type = WorkflowOptionTypes.Permission,
                Value = permission.ToLowerInvariant() is "moderator" or "mod" or "mods"
                    ? "Moderator"
                    : permission
            });
        }

        return options;
    }

    /// <summary>
    /// Built-in commands: Discord moderation names map onto a "moderate" node
    /// (plus a confirmation message), everything else onto a "builtin" node
    /// that invokes the legacy engine and optionally sends its stored response.
    /// </summary>
    private static List<WorkflowNode> BuildBuiltInGraph(Command command)
    {
        var nodes = new List<WorkflowNode>();
        var bareName = StripPrefix(command.Name);
        var response = command.Response ?? string.Empty;

        if (ModerationActions.TryGetValue(bareName, out var action))
        {
            var moderate = Node("moderate", 240, 160);
            moderate.Config["Action"] = action;
            if (ModerationActionNeedsUser(action))
            {
                moderate.Config["User"] = "{args}";
            }

            nodes.Add(moderate);

            // Confirmation is the author's next node; the stored response text,
            // when present, is used as the content instead (it may reference
            // variables — substituted at send time).
            var confirmation = response.Trim().Length > 0 ? response : DefaultModerationConfirmation;
            var send = Node("send_message", 240, 280);
            send.Config["text"] = confirmation;
            nodes.Add(send);
            moderate.Next = send.Id;

            return nodes;
        }

        var builtin = Node("builtin", 240, 160);
        builtin.Config["Name"] = bareName;
        builtin.Config["Args"] = "{args}";
        builtin.Config["OutputVariable"] = "builtin_response";
        nodes.Add(builtin);

        if (response.Trim().Length > 0)
        {
            var send = Node("send_message", 240, 280);
            send.Config["text"] = response;
            nodes.Add(send);
            builtin.Next = send.Id;
        }

        return nodes;
    }

    /// <summary>
    /// Custom commands: random replies (ResponsesJson with 2+ entries) become
    /// random_number → switch → one send per case; a single reply (or the plain
    /// Response) becomes one send. The action sequence (ActionsJson) is then
    /// appended as a linear node chain — from the response node(s) when present,
    /// else as the entry chain.
    /// </summary>
    private static List<WorkflowNode> BuildCustomGraph(Command command)
    {
        var nodes = new List<WorkflowNode>();
        var responseTails = new List<WorkflowNode>();

        var replies = CommandService.ParseResponses(command.ResponsesJson);
        if (replies.Count > 1)
        {
            var random = Node("random_number", 120, 200);
            random.Config["min"] = "1";
            random.Config["max"] = replies.Count.ToString(CultureInfo.InvariantCulture);
            random.Config["store"] = "reply_index";

            var @switch = Node("switch", 300, 200);
            @switch.Config["Value"] = "{reply_index}";
            @switch.Config["Cases"] = string.Join(",", Enumerable.Range(1, replies.Count));

            random.Next = @switch.Id;
            nodes.Add(random);
            nodes.Add(@switch);

            // One terminal send per case port, stacked below the switch.
            for (var i = 0; i < replies.Count; i++)
            {
                var branch = Node("send_message", 560, 120 + 80 * i);
                branch.Config["text"] = replies[i];
                nodes.Add(branch);
                responseTails.Add(branch);

                @switch.Branches ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                @switch.Branches[(i + 1).ToString(CultureInfo.InvariantCulture)] = branch.Id;
            }
        }
        else
        {
            var content = replies.Count == 1 ? replies[0] : (command.Response ?? string.Empty);
            if (content.Trim().Length > 0)
            {
                var send = Node("send_message", 240, 160);
                send.Config["text"] = content;
                nodes.Add(send);
                responseTails.Add(send);
            }
        }

        var actions = BuildActionNodes(command.ActionsJson);
        if (actions.Count > 0)
        {
            nodes.AddRange(actions);

            // Route every response tail into the action chain so nothing is
            // unreachable; with no response nodes the first action is the entry.
            foreach (var tail in responseTails)
            {
                tail.Next = actions[0].Id;
            }
        }

        return nodes;
    }

    /// <summary>
    /// Maps the ordered action sequence onto nodes: send→send_message, wait→wait,
    /// grant_role/remove_role→add_role/remove_role, add_currency/remove_currency→
    /// the economy nodes (targeting the triggering user). Steps the legacy
    /// runner would have skipped anyway (empty text/role, non-positive wait or
    /// amount, unknown types) are not converted. Nodes chain linearly via Next.
    /// </summary>
    private static List<WorkflowNode> BuildActionNodes(string? actionsJson)
    {
        var nodes = new List<WorkflowNode>();

        foreach (var step in ParseSteps(actionsJson))
        {
            var node = MapActionNode(step, nodes.Count);
            if (node is null)
            {
                continue; // Unknown or would-be-skipped step: not converted.
            }

            if (nodes.Count > 0)
            {
                nodes[nodes.Count - 1].Next = node.Id;
            }

            nodes.Add(node);
        }

        return nodes;
    }

    /// <summary>
    /// Maps one action step onto its node at the cascading canvas position,
    /// or null when the legacy runner would have skipped the step anyway
    /// (unknown type, empty text/role, non-positive wait or amount).
    /// </summary>
    private static WorkflowNode? MapActionNode(ActionStep step, int index)
    {
        const double x = 240;
        var y = 160 + 120 * index;

        switch (step.Type)
        {
            case "send" when !string.IsNullOrWhiteSpace(step.Text):
                var send = Node("send_message", x, y);
                send.Config["text"] = step.Text;
                return send;

            case "wait" when step.Seconds > 0:
                var wait = Node("wait", x, y);
                wait.Config["seconds"] = step.Seconds.ToString(CultureInfo.InvariantCulture);
                return wait;

            case "grant_role" when !string.IsNullOrWhiteSpace(step.RoleId):
            case "remove_role" when !string.IsNullOrWhiteSpace(step.RoleId):
                var role = Node(step.Type == "grant_role" ? "add_role" : "remove_role", x, y);
                role.Config["Role"] = step.RoleId;
                role.Config["Username"] = "{username}";
                return role;

            case "add_currency" when step.Amount > 0:
            case "remove_currency" when step.Amount > 0:
                var currency = Node(step.Type == "add_currency" ? "add_currency" : "remove_currency", x, y);
                currency.Config["username"] = "{username}";
                currency.Config["amount"] = step.Amount.ToString(CultureInfo.InvariantCulture);
                return currency;

            default:
                return null;
        }
    }

    // ----------------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------------

    /// <summary>Whether the moderation action targets a member (Purge/Slowmode do not).</summary>
    private static bool ModerationActionNeedsUser(string action) =>
        !action.Equals("Purge", StringComparison.OrdinalIgnoreCase)
        && !action.Equals("Slowmode", StringComparison.OrdinalIgnoreCase);

    /// <summary>Strips an optional command prefix ('!', '?' or '.'), mirroring the engines.</summary>
    private static string StripPrefix(string? name) =>
        (name ?? string.Empty).Trim().TrimStart('!', '?', '.');

    /// <summary>Creates a node with deterministic ids (GUID "N") at a canvas position.</summary>
    private static WorkflowNode Node(string type, double x, double y) => new()
    {
        Type = type,
        X = x,
        Y = y
    };

    // ----------------------------------------------------------------------
    // ActionsJson parsing — mirrors CommandActionRunner's tolerant step
    // shapes: {"type":"send","text":..}, {"type":"wait","seconds":..},
    // {"type":"grant_role"|"remove_role","roleId"|"role":..},
    // {"type":"add_currency"|"remove_currency","amount":..}.
    // ----------------------------------------------------------------------

    private sealed record ActionStep(string Type, string? Text, int Seconds, string? RoleId, int Amount);

    private static List<ActionStep> ParseSteps(string? actionsJson)
    {
        var steps = new List<ActionStep>();

        if (string.IsNullOrWhiteSpace(actionsJson))
        {
            return steps;
        }

        try
        {
            using var document = JsonDocument.Parse(actionsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return steps;
            }

            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object
                    || !element.TryGetProperty("type", out var typeProperty)
                    || typeProperty.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var type = typeProperty.GetString();
                if (string.IsNullOrWhiteSpace(type))
                {
                    continue;
                }

                var seconds = 0;
                if (element.TryGetProperty("seconds", out var secondsProperty)
                    && (secondsProperty.ValueKind == JsonValueKind.Number || secondsProperty.ValueKind == JsonValueKind.String)
                    && secondsProperty.TryGetInt32(out var parsedSeconds))
                {
                    seconds = parsedSeconds;
                }

                var amount = 0;
                if (element.TryGetProperty("amount", out var amountProperty)
                    && (amountProperty.ValueKind == JsonValueKind.Number || amountProperty.ValueKind == JsonValueKind.String)
                    && amountProperty.TryGetInt32(out var parsedAmount))
                {
                    amount = parsedAmount;
                }

                steps.Add(new ActionStep(
                    type,
                    GetString(element, "text"),
                    seconds,
                    GetString(element, "roleId") ?? GetString(element, "role"),
                    amount));
            }
        }
        catch (JsonException)
        {
            // Malformed sequence JSON: no action nodes.
        }

        return steps;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
