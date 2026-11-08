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
/// Node types used: <c>send_message</c>, <c>play_sound</c> (the sub template
/// plays the Sounds library's subscribe.wav instead of the old wait+second-message
/// workaround), plus — wave 15 — <c>twitch_clip</c> and <c>obs_scene</c>. The
/// wave-15 stream-action templates wire the failing node's error branch onto the
/// same follow-up node, because both blocks fail routinely (Twitch offline /
/// clip while not live, OBS remote control off) and a failed clip or scene
/// switch must not swallow the chat message after it.
/// </summary>
public static class WorkflowTemplates
{
    /// <summary>Prefix of an imported workflow's name ("Template: X").</summary>
    public const string ImportedNamePrefix = "Template: ";

    /// <summary>Suffix appended to an imported workflow's description (docs/tests grep for it).</summary>
    public const string ImportedMarker = "[imported from template]";

    /// <summary>The twelve starter templates in panel display order.</summary>
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
            "Plays the sub alert sound and thanks new subs by name in chat.",
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
                  "type": "play_sound",
                  "config": { "SoundName": "subscribe.wav", "Volume": "80" },
                  "next": "tpl-sub-2",
                  "x": 80,
                  "y": 120
                },
                {
                  "id": "tpl-sub-2",
                  "type": "send_message",
                  "config": { "text": "Thank you for subscribing {username}! You just made the stream better." },
                  "x": 340,
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

        // ======================= wave 15 additions =======================

        new(
            "sub-clip",
            "Clip The Sub",
            "Clips the Twitch stream the moment someone subscribes, plays the cheer sound and thanks them in chat. Needs Twitch connected and live — a failed clip still celebrates.",
            "Twitch",
            """
            {
              "type": "event",
              "eventType": "subscribe",
              "platform": "Twitch"
            }
            """,
            """
            {
              "nodes": [
                {
                  "id": "tpl-clip-1",
                  "type": "twitch_clip",
                  "config": {},
                  "next": "tpl-clip-2",
                  "branchError": "tpl-clip-2",
                  "x": 80,
                  "y": 120
                },
                {
                  "id": "tpl-clip-2",
                  "type": "play_sound",
                  "config": { "SoundName": "cheer.wav", "Volume": "80" },
                  "next": "tpl-clip-3",
                  "x": 340,
                  "y": 120
                },
                {
                  "id": "tpl-clip-3",
                  "type": "send_message",
                  "config": { "text": "Clipped it! {username}" },
                  "x": 600,
                  "y": 120
                }
              ],
              "options": [],
              "entryNodeId": "tpl-clip-1"
            }
            """),

        new(
            "raid-mode",
            "Raid Mode",
            "Switches OBS to your raid scene and hypes the raid in chat. Edit the obs_scene node's SceneName (\"Raid Mode\") to match the scene in YOUR OBS collection.",
            "Any",
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
                  "id": "tpl-raidmode-1",
                  "type": "obs_scene",
                  "config": { "SceneName": "Raid Mode" },
                  "next": "tpl-raidmode-2",
                  "branchError": "tpl-raidmode-2",
                  "x": 80,
                  "y": 120
                },
                {
                  "id": "tpl-raidmode-2",
                  "type": "send_message",
                  "config": { "text": "RAID MODE ENGAGED! Welcome {username} and all {viewers} raiders — hype crew, make some noise!" },
                  "x": 340,
                  "y": 120
                }
              ],
              "options": [],
              "entryNodeId": "tpl-raidmode-1"
            }
            """),

        new(
            "commercial-break",
            "Commercial Break Reminder",
            "Posts a quick break reminder in chat every 30 minutes. Note: the message goes to chat — it is visible to everyone, not just mods.",
            "Any",
            """
            {
              "type": "timer",
              "intervalMinutes": 30
            }
            """,
            """
            {
              "nodes": [
                {
                  "id": "tpl-break-1",
                  "type": "send_message",
                  "config": { "text": "Time for a quick break — thanks for supporting the channel!" },
                  "x": 80,
                  "y": 120
                }
              ],
              "options": [],
              "entryNodeId": "tpl-break-1"
            }
            """),

        new(
            "first-chatter",
            "Welcome First-Time Chatter",
            "Greets a viewer the first time they chat in the channel. Uses the new \"firstmessage\" event trigger key (wave 15).",
            "Any chat platform",
            """
            {
              "type": "event",
              "eventType": "firstmessage",
              "platform": "Any"
            }
            """,
            """
            {
              "nodes": [
                {
                  "id": "tpl-firstmsg-1",
                  "type": "send_message",
                  "config": { "text": "Welcome to the stream, {username}!" },
                  "x": 80,
                  "y": 120
                }
              ],
              "options": [],
              "entryNodeId": "tpl-firstmsg-1"
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
