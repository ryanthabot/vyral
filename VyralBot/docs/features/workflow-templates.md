# Starter workflow templates (Automation page onboarding)

`src/VyralBot.UI/Services/WorkflowTemplates.cs` — **static catalog** (no DI
registration, same pattern as `BlockCatalog`/`FlowSnapshotService`) of eight
one-click starter workflows for the Automation page's TEMPLATES panel, so a
new user's automation page is not empty. Community-onboarding feature
(marathon wave 8, 2026-11-07).

## Catalog

`WorkflowTemplates.Templates` — `IReadOnlyList<WorkflowTemplate>` (pure data:
`Key`, `Name`, `Description`, `Platforms` display chip, `TriggerJson`,
`GraphJson`). The JSON columns are hand-authored against the CURRENT
`WorkflowJson` schema (`WorkflowTrigger` shape + v2 GraphDocument shape with
`nodes`/`options`/`entryNodeId`) and guarded by tests:

| Key | Trigger | Nodes | Platforms |
|---|---|---|---|
| `follower-thanks` | event `follow` | send_message `{username}` | Twitch, Kick |
| `sub-appreciation` | event `subscribe` | play_sound `subscribe.wav` (vol 80) → send_message `{username}` (wave 10: sound block replaced the wait+second-message fallback) | Twitch, Kick |
| `raid-welcome` | event `raid` | send_message `{username}` `{viewers}` | Twitch |
| `first-hello` | command `!hello` | send_message `{username}` `{channel}` (no first-message variable exists — spec fallback) | Any chat |
| `schedule-announcement` | timer 60m | send_message (static text) | Any |
| `reward-thanks` | event `rewardredeemed` | send_message `{username}` `{reward}` `{cost}` | Twitch, Kick |
| `superchat-shoutout` | event `superchat` (YouTube) | send_message `{username}` `{amount}` | YouTube |
| `mod-warn` | command `!warn` + option `permission=Moderator` | send_message `{username}` `{args}` | Any chat |

Event keys/variables match what `WorkflowTriggerService` actually dispatches
(`follow`/`subscribe`/`raid`/`rewardredeemed`/`superchat` canonical keys; the
permission option is enforced by `TryAllowPermission`). Node types used are
`send_message`, `play_sound` and `wait` — all in `BlockCatalog`, so imported
templates also open cleanly in the structured builder and the canvas editor.

## Import path (disabled by default)

`WorkflowTemplates.CreateWorkflow(template)` → `Workflow?`:

- fresh `Guid("N")` id, name **`"Template: {Name}"`**,
- description = template description + **`[imported from template]`** marker,
- **`Enabled = false`** — nothing fires until the user edits it and flips the
  toggle,
- trigger/nodes/options/parameters/entryNodeId parsed via
  `WorkflowJson.FromTrigger`/`FromGraphData`; a parse failure yields `null`
  (guarded, never throws).

UI: `AutomationView.xaml` header **TEMPLATES** button → panel card listing the
templates (name, description, trigger + platform chips, per-row IMPORT, per-row
IMPORTED badge). `AutomationViewModel` owns `Templates`
(`WorkflowTemplateItemViewModel` rows), `IsTemplatesVisible`,
`ShowTemplates`/`CloseTemplates`/`ImportTemplateAsync` commands; import saves
via `IDataService.SaveWorkflowAsync(WorkflowJson.ToRow(...))`, refreshes
`WorkflowTriggerService.ReloadAsync()` and the workflow list, and logs at
info level.

## Tests

`tests/VyralBot.UI.Tests/WorkflowTemplatesTests.cs` — each template parses
through `WorkflowJson` (theory per key), node types verified against
`BlockCatalog`, wiring/config validated, trigger completeness, disabled
"Template: X" import with marker, `ToRow`/`FromRow` round-trip (keeps the
Moderator permission option), and pinned trigger-key facts.
