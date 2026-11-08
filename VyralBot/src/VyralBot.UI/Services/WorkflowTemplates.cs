using VyralBot.Workflows.Models;
using VyralBot.Workflows.Persistence;

namespace VyralBot.UI.Services;

/// <summary>
/// One embedded starter workflow template for the Automation page's TEMPLATES
/// panel (community onboarding): display metadata plus the two persisted JSON
/// shapes of a Workflows table row — <see cref="TriggerJson"/>
/// (<see cref="WorkflowJson.ToTrigger"/> shape) and <see cref="GraphJson"/>
/// (v2 <see cref="WorkflowJson.FromGraphData"/> document shape). Pure data;
/// no IO, so it needs no DI registration.
/// </summary>
public sealed class WorkflowTemplate
{
    /// <summary>Stable template key (logging + tests).</summary>
    public string Key { get; }

    /// <summary>Display name (the imported workflow becomes "Template: {Name}").</summary>
    public string Name { get; }

    /// <summary>One-line description shown in the panel and copied into the imported workflow.</summary>
    public string Description { get; }

    /// <summary>Display-only chip text naming the platforms the trigger fires on.</summary>
    public string Platforms { get; }

    /// <summary>WorkflowTrigger serialized in the TriggerJson column shape.</summary>
    public string TriggerJson { get; }

    /// <summary>Node graph serialized in the v2 GraphJson column shape.</summary>
    public string GraphJson { get; }

    public WorkflowTemplate(string key, string name, string description, string platforms, string triggerJson, string graphJson)
    {
        Key = key;
        Name = name;
        Description = description;
        Platforms = platforms;
        TriggerJson = triggerJson;
        GraphJson = graphJson;
    }
}

/// <summary>
/// Static catalog of one-click starter workflow templates (Automation page →
/// TEMPLATES). Every template is embedded C# data authored against the CURRENT
/// <see cref="WorkflowJson"/> schema and validated by
/// <c>VyralBot.UI.Tests.WorkflowTemplatesTests</c> (parses, node types exist in
/// <see cref="VyralBot.Workflows.Abstractions.BlockCatalog"/>, wiring resolves).
/// Node types used: <c>send_message</c> and <c>wait</c> only — the catalog has
/// no sound node, so the sub template uses two messages with a delay instead.
/// </summary>
public static class WorkflowTemplates
{
    /// <summary>Prefix of an imported workflow's name ("Template: X").</summary>
    public const string ImportedNamePrefix = "Template: ";

    /// <summary>Suffix appended to an imported workflow's description (docs/tests grep for it).</summary>
    public const string ImportedMarker = "[imported from template]";

    /// <summary>The eight starter templates in panel display order.</summary>
    public static IReadOnlyList<WorkflowTemplate> Templates { get; } = new WorkflowTemplate[]
    {
        new(
            "follower-thanks",
            "New Follower Thanks",
            "Thanks every new follower by name the moment they follow.",
            "Twitch, Kick",
            """
            {
              "type": "event",
              "eventType": "follow",
              "platform": "Any"
            }
            """,
            """
            {
              "nodes": [
                {
                  "id": "tpl-follow-1",
                  "type": "send_message",
                  "config": { "text": "Thanks for the follow {username}! Welcome to the community." },
                  "x": 80,
                  "y": 120
                }
              ],
              "options": [],
              "entryNodeId": "tpl-follow-1"
            }
            """),

        new(
            "sub-appreciation",
            "Sub Appreciation",
            "Two-message thank-you for new subs with a short pause between (no sound node in the block catalog yet).",
            "Twitch, Kick",
            """
            {
              "type": "event",
              "eventType": "subscribe",
              "platform": "Any"
            }
            """,
            """
            {
              "nodes": [
                {
                  "id": "tpl-sub-1",
                  "type": "send_message",
                  "config": { "text": "Thank you for subscribing {username}! You just made the stream better." },
                  "next": "tpl-sub-2",
                  "x": 80,
                  "y": 120
                },
                {
                  "id": "tpl-sub-2",
                  "type": "wait",
                  "config": { "seconds": "3" },
                  "next": "tpl-sub-3",
                  "x": 340,
                  "y": 120
                },
                {
                  "id": "tpl-sub-3",
                  "type": "send_message",
                  "config": { "text": "Welcome to the sub club {username} — your support keeps the lights on." },
                  "x": 600,
                  "y": 120
                }
              ],
              "options": [],
              "entryNodeId": "tpl-sub-1"
            }
            """),

        new(
            "raid-welcome",
            "Raid Welcome",
            "Greets the raider and their viewers by name the moment a raid lands.",
            "Twitch",
            """
            {
              "type": "event",
              "eventType": "raid",
              "platform": "Any"
            }
            """,
            """
            {
              "nodes": [
                {
                  "id": "tpl-raid-1",
                  "type": "send_message",
                  "config": { "text": "RAID! Welcome {username} and all {viewers} raiders — pull up a chair and make yourselves at home!" },
                  "x": 80,
                  "y": 120
                }
              ],
              "options": [],
              "entryNodeId": "tpl-raid-1"
            }
            """),

        new(
            "first-hello",
            "First-Time Chatter Greeting",
            "Greets chatters who type !hello — a stand-in until a first-message trigger exists.",
            "Any chat platform",
            """
            {
              "type": "command",
              "commandName": "hello",
              "commandPrefix": "!"
            }
            """,
            """
            {
              "nodes": [
                {
                  "id": "tpl-hello-1",
                  "type": "send_message",
                  "config": { "text": "Hey {username}, great to see you chatting in {channel}! Stick around — good stuff coming." },
                  "x": 80,
                  "y": 120
                }
              ],
              "options": [],
              "entryNodeId": "tpl-hello-1"
            }
            """),

        new(
            "schedule-announcement",
            "Daily Schedule Announcement",
            "Repeats your schedule message in chat every hour while Vyral runs — edit the text and interval to taste.",
            "Any",
            """
            {
              "type": "timer",
              "intervalMinutes": 60
            }
            """,
            """
            {
              "nodes": [
                {
                  "id": "tpl-sched-1",
                  "type": "send_message",
                  "config": { "text": "Schedule reminder: we go live every day at 7pm — follow the channel so you catch the start!" },
                  "x": 80,
                  "y": 120
                }
              ],
              "options": [],
              "entryNodeId": "tpl-sched-1"
            }
            """),

        new(
            "reward-thanks",
            "Channel Points Reward Thanks",
            "Thanks viewers by name when they redeem a channel-points reward (Twitch or Kick).",
            "Twitch, Kick",
            """
            {
              "type": "event",
              "eventType": "rewardredeemed",
              "platform": "Any"
            }
            """,
            """
            {
              "nodes": [
                {
                  "id": "tpl-reward-1",
                  "type": "send_message",
                  "config": { "text": "{username} just redeemed {reward} for {cost} points — thank you for the support!" },
                  "x": 80,
                  "y": 120
                }
              ],
              "options": [],
              "entryNodeId": "tpl-reward-1"
            }
            """),

        new(
            "superchat-shoutout",
            "Super Chat Shoutout",
            "Shouts out YouTube Super Chats with the sender and amount.",
            "YouTube",
            """
            {
              "type": "event",
              "eventType": "superchat",
              "platform": "YouTube"
            }
            """,
            """
            {
              "nodes": [
                {
                  "id": "tpl-super-1",
                  "type": "send_message",
                  "config": { "text": "HUGE thanks to {username} for the {amount} Super Chat!" },
                  "x": 80,
                  "y": 120
                }
              ],
              "options": [],
              "entryNodeId": "tpl-super-1"
            }
            """),

        new(
            "mod-warn",
            "Moderation Warning",
            "Mod-only !warn <user> command that posts a public warning message.",
            "Any chat platform",
            """
            {
              "type": "command",
              "commandName": "warn",
              "commandPrefix": "!"
            }
            """,
            """
            {
              "nodes": [
                {
                  "id": "tpl-warn-1",
                  "type": "send_message",
                  "config": { "text": "{username} has issued a warning to {args}. Please follow the chat rules." },
                  "x": 80,
                  "y": 120
                }
              ],
              "options": [
                { "type": "permission", "value": "Moderator" }
              ],
              "entryNodeId": "tpl-warn-1"
            }
            """),
    };

    /// <summary>
    /// Materializes a template into a saveable <see cref="Workflow"/>:
    /// fresh id, name "Template: X", description tagged with
    /// <see cref="ImportedMarker"/>, and — deliberately — <see cref="Workflow.Enabled"/>
    /// false so nothing fires until the user has reviewed it and flipped the
    /// toggle. Returns null when the embedded JSON does not parse into a node
    /// graph (guarded import path — never throws).
    /// </summary>
    public static Workflow? CreateWorkflow(WorkflowTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var graph = WorkflowJson.FromGraphData(template.GraphJson);
        if (graph.Nodes.Count == 0)
        {
            return null;
        }

        var description = template.Description.Trim();
        return new Workflow
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = ImportedNamePrefix + template.Name,
            Description = $"{description} {ImportedMarker}",
            Enabled = false,
            Trigger = WorkflowJson.FromTrigger(template.TriggerJson),
            Nodes = graph.Nodes,
            Options = graph.Options,
            Parameters = graph.Parameters,
            EntryNodeId = graph.EntryNodeId,
            ErrorHandlerEntryId = graph.ErrorHandlerEntryId,
            ComponentKey = graph.ComponentKey,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}
