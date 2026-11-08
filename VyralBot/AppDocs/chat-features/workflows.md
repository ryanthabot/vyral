# Workflow Engine (Deep Guide)

> What actually runs when a trigger fires: the block catalog, the eight trigger types, the command-level options, the runtime's hard safety caps, and the execution history — the engine under the [flow editor](automation.md). Every claim here is verified against `VyralBot.Workflows/` and `WorkflowTriggerService` as of 2026-11-07.

## Overview

A workflow is **data, not code**: a trigger plus a node graph, persisted as JSON (`TriggerJson` + `GraphJson` columns of the Workflows table) and executed by `WorkflowRuntime`. The [visual flow editor](automation.md) is one authoring surface; the Discord bot command builder and the Commands page EDIT buttons are others — all editing the same storage.

The numbers, straight from the code:

- **40 placeable blocks** — 29 **Actions** (blue) across Messaging / Threads & Channels / Roles / Moderation / Variables & Math / Economy / Streaming / Loops / Utility sections, and 11 **Conditions** (green, flat list) (BlockCatalog.cs:101-301).
- **8 trigger types** — `command`, `slash_command`, `button_click`, `select_menu`, `mod_command`, `event`, `timer`, `manual` (WorkflowTriggerTypes, Workflow.cs:4-18).
- **6 command options** — Cooldown, Require Moderator, Channel Restriction, DM Only, Guild Only, NSFW (BlockCatalog.cs:361-369).
- **Runtime caps** — a run executes at most **200 steps** (cycle guard) and a loop block iterates at most **25 times**; loop body nodes count toward the step budget too (WorkflowRuntime.cs:55-58, 263-268, 405-411).

## User Guide

### Triggers

| Trigger | Fires when | Key settings |
|---|---|---|
| **Command** | A chat message `!name`/`?name`/`.name` arrives (prefix set synced with Settings → Commands) | Command name + prefix; consumes the message |
| **Slash command** | A native Discord `/name` is invoked | Registered per bot on each connected bot; 5 s dedup per bot\|command\|user\|channel |
| **Button click / Select menu** | A workflow-sent Discord component is used | Component child flows (`ComponentKey`) |
| **Mod command** | `!mod <verb>` with a custom verb | Minimum tier on the TrialMod < Mod < SeniorMod < Admin ladder |
| **Event** | A platform event publishes (follow/subscribe/giftsub/host/raid/cheer/**rewardredeemed**/YouTube **membership** (own key — see below — `isMilestone` filterable)/**firstmessage**/discord member+boost aliases normalized) | Event type + **platform filter** + optional guard expression |
| **Timer** | Every N minutes (60 s internal tick) | Interval minutes |
| **Manual** | The RUN button / API | — |

Platform filtering on the **event** trigger: `Trigger.Platform` null/empty/"Any"/"All" matches every platform, otherwise an exact case-insensitive match is required (WorkflowTriggerService.cs:61-64). The same filter is **enforced by the runtime on command, slash and mod-command matches too** (WorkflowTriggerService.cs:492-496, 600, 773-775) — and command triggers honor an additional platform **allow-list**, `Trigger.Platforms`: a non-empty list restricts the command to the named publishing platforms (case-insensitive; null/empty = all platforms). The list lives inside `TriggerJson` (omitted when null, so older rows round-trip unchanged). Note the flow editor's Platform picker currently only appears on event triggers (FlowEditorViewModel.cs:4999), so a platform-scoped *command* trigger is a runtime capability the editor UI does not yet expose per-trigger.

**Event trigger keys worth knowing**: `rewardredeemed` fires for both Twitch Channel Points redemptions and Kick reward redemptions (variables `userId`, `reward`, `cost`; a Twitch redemption's user-input text arrives as the message) — an event flow with no platform filter serves both. `discordmemberjoined` / `discordboost` fire for Discord member joins and boost starts (variables `userId`, `displayName`, `guildId`, `guildName`, plus `premiumSince` on boosts) — only while the gateway runs the privileged GuildMembers intent, which ships off by default ([Discord](../platforms/discord.md#member-joins--boosts-intent-gated)). **`firstmessage`** (1.1.0) fires the first time a viewer ever chats on a platform — Core's `FirstTimeChatterService` publishes the event after a read-only "no prior watchtime or currency row" check (variables `username`, `platform`, `message`; the burst dedup means one welcome per newcomer per 60 s window). Authored spellings `firstchatter` / `firsttimechatter` normalize onto the key; the welcome alert/reply side of the feature is documented in [Event Reactions → First-time chatters](../chat/event-reactions.md#first-time-chatters), and its detection semantics carry the KI-61 caveat. YouTube Super Chats have **no** workflow trigger key yet (reactions only — see [YouTube](../platforms/youtube.md#super-chats--super-stickers)).

**YouTube memberships — the `membership` key.** A new YouTube member (`newSponsorEvent`) and a member milestone (`memberMilestoneChatEvent`) both dispatch the canonical **`membership`** key — deliberately **not** folded into `subscribe`, because that key already carries Twitch/Kick subs: separate keys keep Twitch subs and YouTube memberships independently targetable (the trigger's Platform filter separates them further). Variables on every membership dispatch: `username` (the context), **`level`** (the membership level name) and **`tier`** (a mirror of `level`, so conditions authored against Twitch's `tier` read naturally), `userId`, and **`isMilestone`** (`true` for milestone renewals, `false` for a new member) — filter one or the other with a Conditions guard such as `{isMilestone} = false`. Authored spellings normalize onto the key: `youtubemembership`, `ytmembership`, `sponsor`, `newsponsor`, `newsponsorevent`, `membermilestone`, `membermilestonechatevent` (WorkflowTriggerService.cs:2492-2499).

**Command triggers and per-platform responses.** The editor's command-trigger panel carries a **Per-platform responses** expander (four boxes — Twitch/Kick/YouTube/Discord) whose entries are persisted on the linked `Commands` row and applied wherever that row's reply is picked: the Core command engine's prefix-command path and **timers** (the override for the timer's target platform wins, then random replies, then the default). The precedence and pick sites are documented in [Commands → Per-platform responses](commands.md#per-platform-responses-one-command-four-replies).

**Discord bot scoping**: commands executed from Discord carry the receiving bot's id — `TryHandleCommand` accepts a `discordBotId`, seeds the execution context's `BotId`, and every Discord node send routes back through that origin bot (WorkflowTriggerService.cs:428, 451, 529; NodeBridges.cs — every bridge method takes `botId?`). Slash commands are registered on **each** connected bot separately (WorkflowTriggerService.cs:1654-1727).

### Options (the purple tab)

Cooldown (per-user seconds, enforced via an in-memory `workflowId|username` map), Require Moderator (Everyone/Moderator/role name — checks the "userRoles"/"isModerator" context variables seeded from badges/Discord roles; allows when roles are unavailable, a documented V1 behavior), Channel Restriction, DM Only, Guild Only, NSFW (informational in V1) (Workflow.cs:21-40; WorkflowTriggerService.cs:85-89, 171-172, 435-445). The Options tab also holds typed **command parameters** — the slash-command argument definitions exposed to nodes as `{variables}` (Workflow.cs:56-86).

### Blocks

**Actions** (29): Send Message (Discord slash/button runs can reply **ephemerally** — the interaction is deferred at trigger entry so slow flows still beat Discord's 3-second window, and the reply lands as a followup visible only to the invoker), Send Embed (live Discord preview), Send DM, Edit Bot Message, Delete Message, React, Add/Remove Role, Create Channel/Thread, Lock/Unlock Channel, Wait for Response (stores `{response}`, timeout → error port), HTTP Request (stores `{http_response}`/`{http_status}`), Set Variable, Random Number, Add/Remove Currency, Delay, **Play Sound** (added 1.1.0 — plays a named file from your [Sounds](../stream-tools/sounds.md) library, e.g. `subscribe.wav`, `{variables}` allowed, optional Volume 0-100 clamped to range; honors the streamer/stream sound settings, and a missing file **fails the node** so an error branch can route around it), the four **Streaming** blocks (added 1.1.0) — **Switch OBS Scene** (`obs_scene`, scene name supports `{variables}`), **OBS Stream Start/Stop** (`obs_stream`, Start/Stop select), **Save OBS Replay Buffer** (`obs_replay`, parameterless — errors with a hint when the replay buffer isn't running in OBS), and **Create Twitch Clip** (`twitch_clip`, parameterless — errors when Twitch is disconnected or the stream is offline); they run through the OBS remote-control seam the VyralDeck OBS buttons use (obs-websocket), so OBS must be running with its websocket enabled (KI-60), and every failure takes the node's **error branch** instead of throwing — Run Built-in Command (cooldowns/permissions/variables of the legacy engine all apply), Moderate Member (kick/ban/unban/timeout/purge/slowmode), Log, Note, Run a Loop (1-25 iterations), Stop a Loop.

**Conditions** (11): If (Expression) — rule builder with and/or chains; If User Has Role; If Message Contains; If Variable Equals; If Time Between; If User Is Bot; Chance (%); Comparison (multi-rule with 14 canonical operators, `collection_contains` included); Permissions Condition; Channel Condition; User Condition.

Branching: conditions expose True/False ports (False routes through the auto-attached Else card; Comparison manages its own case/Else ports). Any node can wire an **error branch** (`BranchError`), and the flow-level **Error Handler** chain runs when a failing node has none of its own (Workflow.cs:219-229, 135-136). The legacy `switch` block is no longer placeable but still opens and runs from saved flows (BlockCatalog.cs:278-280).

### Runtime behavior and history

- Variables resolve per node — context tokens (`{username}`, `{channel}`, `{platform}`, `{message}`, `{args}`, `{botid}`, …) plus flow-stored names (ExpressionEvaluator.cs:20; ExecutionContext.cs:84-86).
- A run that exceeds 200 steps fails with "Maximum step count (200) exceeded — possible node cycle" (WorkflowRuntime.cs:263-268); loop counts clamp to 25 (WorkflowRuntime.cs:405-411).
- **Every execution persists a history row** (WorkflowExecutions table — best effort, never fails the run) with the trigger summary, platform, duration and outcome; the Automation page shows a per-workflow HISTORY card and the editor's ⚠ Error Logs panel lists recent failures (WorkflowRuntime.cs:97, 175-198; IDataService.cs:96-99, 161-162).
- **Deleting (or disabling) a workflow cancels its in-flight runs** — the runtime keeps an active-run registry per workflow id; on every workflow-cache reload (which every delete path and the disable toggle trigger), ids that vanished across the reload get `CancelWorkflowRuns` signalled, each run stops at the next between-node check, and its history row records Status **"Cancelled"**. No-op when nothing is in flight, and a disabled-then-re-enabled id is unaffected (cancellation only touches runs in flight at that moment).
- Timer caveat (V1): last-run times are in-memory — a timer whose interval elapsed while the app was down fires on the first tick after start (WorkflowTriggerService.cs:66-71).
- Command triggers are one hook into the shared chat pipeline and **consume the message** (CommandService, currency and auto-replies don't also fire for it) (WorkflowTriggerService.cs:28-33).

## How It Works (Developer)

- `NodeRegistry` maps each block type to its `INode` implementation (Nodes/ — DiscordNodes, CommandNodes, CurrencyNodes, ConditionNodes, LoopNodes, HttpNode, WaitResponseNode, SwitchNode, BuiltIn/*); `BlockCatalog` is the single source of truth for palette + settings + ports shared by desktop editor, Discord builder and canvas rendering (BlockCatalog.cs:78-100).
- `WorkflowRuntime.ExecuteAsync` walks the graph from the pinned `EntryNodeId` (else the first node nobody routes to), threading an `ExecutionContext` (platform, channel, username, BotId, variables, origin reply callback); per-node config values get `{variable}` substitution before execution (Workflow.cs:113-133; FlowEditorViewModel.cs:5044-5059).
- Discord surface area lives behind `IDiscordWorkflowBridge` (NodeBridges.cs) — the UI registers a bridge over `IDiscordBotManager`/`IDiscordService`; every method is `botId?`-parameterized so nodes target the origin bot or a pinned one.
- `WorkflowTriggerService` owns the live trigger indexes (command-name map, slash map, single mod-verb map, event subscriptions, timer set), reloads them when workflows change, registers slash commands on every connected bot (deduplicated by BotId, including the setup wizard's bot), and gates mod verbs through the tier resolver (WorkflowTriggerService.cs:154-172, 1654-1727, 597-620).
- Sends go through the runtime's MessageSender platform hook (installed by the UI at startup) plus the optional per-trigger origin reply callback, so nodes have a guaranteed reply path even outside Discord (WorkflowRuntime.cs:82; WorkflowTriggerService.cs:90-93, 425-434).

## Files & Code

| File | Role |
|---|---|
| [BlockCatalog.cs](../../src/VyralBot.Workflows/Abstractions/BlockCatalog.cs) | The 40 blocks, param-field kinds, option definitions, port colors/keys |
| [Workflow.cs](../../src/VyralBot.Workflows/Models/Workflow.cs) | Trigger/node/option/parameter models + the 8 trigger and 6 option type keys |
| [WorkflowRuntime.cs](../../src/VyralBot.Workflows/Runtime/WorkflowRuntime.cs) | Graph execution, 200-step / 25-iteration caps, error chains, history persistence |
| [ExecutionContext.cs](../../src/VyralBot.Workflows/Runtime/ExecutionContext.cs) | Per-run state incl. Platform, BotId, variable resolution |
| [WorkflowTriggerService.cs](../../src/VyralBot.UI/Services/WorkflowTriggerService.cs) | Live trigger matching: command/slash/component/mod/event/timer/manual + options enforcement |
| [NodeBridges.cs](../../src/VyralBot.Workflows/Abstractions/NodeBridges.cs) | IDiscordWorkflowBridge — bot-scoped Discord operations for nodes |
| [Nodes/](../../src/VyralBot.Workflows/Nodes) | The INode implementations behind each block |
| [WorkflowJson.cs](../../src/VyralBot.Workflows/Persistence/WorkflowJson.cs) | Row ⇄ model (de)serialization |
| [FlowEditorViewModel.cs](../../src/VyralBot.UI/ViewModels/FlowEditorViewModel.cs) | The visual editor (UI covered in [Automation](automation.md)) |
| [AutomationViewModel.cs](../../src/VyralBot.UI/ViewModels/AutomationViewModel.cs) | Workflow list + per-workflow execution-history cards |

## Data & Settings

| Key / table | Store | Meaning |
|---|---|---|
| `Workflows` table | Per-profile SQLite | Id, name, enabled, category, TriggerJson, GraphJson, timestamps |
| `WorkflowExecutions` table | Per-profile SQLite | One row per run: workflow, status, trigger summary, started/duration (IDataService.cs:96-99) |
| `Automation.CommandTriggersEnabled` | Configs | Master switch for command triggers (default true) (WorkflowTriggerService.cs:120, 351) |
| `Commands.Prefixes` | Configs | Shared prefix set ("!", "?", ".") kept in sync with the Core command engine (WorkflowTriggerService.cs:111-114) |

## Status & Known Issues

1. **Block count is 40** — 29 actions + 11 conditions in the catalog today (`play_sound` joined as the 25th action and the four Streaming blocks — `obs_scene`/`obs_stream`/`obs_replay`/`twitch_clip` — as the 26th–29th in the 1.1.0 marathon); the "switch" block is runtime-supported but no longer placeable (BlockCatalog.cs:101-301, 278-280).
2. **Command-trigger platform filtering is runtime-only for now** — `Trigger.Platform` (and the `Trigger.Platforms` allow-list) are honored when matching commands, but the editor saves them only for event triggers (FlowEditorViewModel.cs:4999).
3. **Timer state is volatile** — restarts forget last-run times; an elapsed interval fires on the first tick (WorkflowTriggerService.cs:66-71).
4. **Permission option allows when roles are unavailable** — documented V1 behavior where a platform can't supply roles (WorkflowTriggerService.cs:435-445).
5. **Cooldown map is in-memory** — a restart clears per-user cooldowns (WorkflowTriggerService.cs:171-172).

### Verified against (2026-11-07)

`BlockCatalog.cs` (block list :101-301 — 25 action + 11 condition entries counted; `play_sound` :203 with SoundName/Volume params; send_message Ephemeral param; options :361-369; ports :411-452; comparison operators :314-320), `PlaySoundNode.cs` (volume 0-100 clamp, sound-library existence check → routable failure), `WorkflowTemplates.cs` (the 12 starter templates — four added in 1.1.0's wave 15, incl. the `firstmessage`-triggered welcome and the Streaming-block clip/raid-mode templates; the sub-appreciation template uses `play_sound`), `Workflow.cs` (trigger types :4-18, option types :21-40, parameters :56-86, trigger fields incl. Platform/Platforms/MinimumTier :155-200, node branches), `WorkflowRuntime.cs` (MaxSteps/MaxLoopIterations, cycle guard, loop clamp, active-run registry + `CancelWorkflowRuns`, history persistence, SummarizeTrigger), `WorkflowTriggerService.cs` (header doc, command match + platform filters incl. `CommandPlatformListed`, delete-mid-run cancellation sweep on ReloadAsync, event dispatch keys `rewardredeemed`/`discordmemberjoined`/`discordboost`/`firstmessage` (+ alias normalization), YouTube `membership` dispatch + alias table :1677-1692, :2492-2499, slash per-bot registration + dedup, prefixes), `ExecutionContext.cs`, `NodeBridges.cs` (botId-parameterized bridge), `SendMessageNode.cs` (Ephemeral interaction-followup path), `DiscordService.cs` (`DeferInteractionAsync` at trigger entry, `RespondEphemeralAsync`), `FlowEditorViewModel.cs` (BuildTriggerDto, palette platforms, per-platform response boxes :2080-2100), `CommandService.cs` (`PickResponse`/`StoredResponse` per-platform precedence :363-393), `CommandTimerService.cs` (:281 timer-platform override), `YouTubeChatService.cs` (`PublishMembershipEvent` :634, :735-769), `AutomationViewModel.cs` (history fields).
