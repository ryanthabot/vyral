# Variables & Conditional Auto-Reply Rules

## Overview
Two related subsystems: the **variable substitution engine** (custom
`{kebab-case}` variables stored per profile, plus context variables injected
per surface) and the **conditional auto-reply rules engine** (per-platform,
per-event rules with a JSON condition tree that can override the default
auto-reply message).

## Source Files
| File | Description |
|------|-------------|
| `src/VyralBot.Core/Services/Variables/IVariableReplacementService.cs` | Engine interface |
| `src/VyralBot.Core/Services/Variables/VariableReplacementService.cs` | Substitution engine (nested vars, two-lane cache) |
| `src/VyralBot.Data/Entities/Variable.cs` | Entity (Name, Value, Category, Platforms, IsSystem, IsAutoUpdating...) |
| `src/VyralBot.Data/DapperDataService.cs` | `Variables` table DDL, CRUD, seeding, `UpdateVariableValueAsync` |
| `src/VyralBot.UI/ViewModels/VariablesViewModel.cs` | Variables page VM (CRUD, filter, search) |
| `src/VyralBot.UI/Views/VariablesView.xaml` | Variables page UI (categories sidebar, cards, add/edit dialogs) |
| `src/VyralBot.Core/Services/Chat/AutoReplyService.cs` | Rule lookup + template processing + 30s rate limiter |
| `src/VyralBot.Core/Services/Rules/RuleEvaluator.cs` | Condition evaluation (Value/Status/Time/Complex) |
| `src/VyralBot.Core/Models/Rules/RuleCondition.cs` | Condition model, operators, logical operators |
| `src/VyralBot.Data/Entities/AutoReplyRule.cs` | Rule entity |
| `src/VyralBot.UI/ViewModels/ConditionalRulesViewModel.cs` | Rules page VM (list, editor, condition builder, test) |
| `src/VyralBot.UI/Views/ConditionalRulesView.xaml` | Rules page UI |

## Variable substitution

### Behavior
- Placeholders match `{kebab-case}` names (`^[a-z0-9]+(-[a-z0-9]+)*$`); names
  with underscores or uppercase are NOT matched.
- Context variables (provided by the calling surface) take priority over
  database variables.
- Unknown or empty-valued placeholders are **left as-is** — never blanked.
  Errors also return the original template.
- Nested variables (`{a}` whose value contains `{b}`) resolve recursively up
  to depth 5; genuine cycles stop at the cap. A placeholder repeated inside
  one template is legitimate (`string.Replace` replaces all occurrences).
- Cache: static DB variables 5 minutes, **auto-updating variables 15 seconds**
  (they are refreshed by integrations via `UpdateVariableValueAsync`, which
  cannot invalidate the cache). Cache is cleared on save/delete from the
  Variables page (`InvalidateCacheAsync`).

### Surfaces that substitute
| Surface | Context variables |
|---------|-------------------|
| Chat commands (`CommandService.ParseVariables`) | username, platform, channel, balance, role (+custom) |
| Command action steps (`CommandActionRunner`) | same as commands |
| Command timers / scheduled messages (`CommandTimerService`) | username="Vyral", platform, channel |
| Auto-replies (`ChatViewModel.BuildAutoReplyTextAsync`) | username, platform + event fields (bits, months, ...) |
| Manual chat send box (`ChatViewModel`) | bot-username, channel, platform |
| Discord welcome message (`DiscordBotWelcomeSettings.FormatMessage`) | own `{user}`/`{username}`/`{server}` replacement, NOT this engine |

Alerts take values as parameters (no `{variable}` templates). The Variables
page cannot define the per-surface context variables above — it only manages
database-backed custom variables.

### Auto-updating variables
Seeded by `DapperDataService` (Category = platform, `IsAutoUpdating=1`,
non-editable): `kick-followercount`, `kick-subscribercount`, `kick-viewcount`,
`twitch-followercount`, `twitch-subscribercount`, `twitch-viewcount` — the
"Auto-Updating" badge on the Variables page marks exactly these rows.
`kick-followercount` is written with the real count from the Kick stream
event; the Twitch counters only increment when a follow/sub event is seen
(no Helix count query); the `*-viewcount` rows are currently never updated.

## Conditional auto-reply rules

### Pipeline
`ChatViewModel` event handlers (Twitch: Follow/Subscribe/Resub/GiftSub/Raid/
Cheer/RewardRedeemed; Kick: Follow/Subscribe/GiftSub/Host/KicksGifted/
RewardRedeemed) gate on the platform auto-reply toggle + a non-empty default
auto-reply message + `ShouldSendReply` (30s per username+platform+eventType
rate limit), then call `BuildAutoReplyTextAsync`, which asks
`AutoReplyService.GetAutoReplyAsync(platform, eventType, context)`.

- Rules are fetched `WHERE IsEnabled=1 AND (Platform=@p OR Platform='All')
  AND EventType=@e ORDER BY Priority ASC, Id ASC` — **first match wins**.
- A matching rule's `ReplyTemplate` **replaces** the default auto-reply
  message; no match keeps the default. `{}`/empty condition = always match.
- Operators: Equals/NotEquals/GreaterThan/LessThan/GreaterOrEqual/LessOrEqual/
  Contains/StartsWith/EndsWith/Modulo (Value is the divisor) for Value
  conditions; boolean checks for Status; time_of_day/day_of_week/hour/date
  for Time; nested AND/OR for Complex. No regex operator. No mod-exemption:
  the only gate is the per-user rate limiter.
- `TriggerCount`/`LastTriggeredAt` increment on each match. Saving a rule from
  the UI rewrites `LastTriggeredAt` from the loaded row, but `TriggerCount` is
  excluded from the UPDATE so the live count survives edits (the INSERT for
  new rules still seeds it).
- Not wired: "ChatMessage" and "StreamStatus" event types and the "Discord"
  platform never reach the engine in the current pipeline; the Rules page
  event list omits the dead types but keeps legacy stored values editable.

### Rules page
List (priority/trigger count/enabled state), editor (name, platform, event,
priority, enabled, condition builder that wraps conditions into AND trees,
raw condition JSON, reply template), delete with confirm, and "Test Rule"
which evaluates the stored condition against a sample context and explains
the verdict.
