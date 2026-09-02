# Conditional Rules

> A complete CRUD page for conditional auto-reply rules backed by a fully-implemented condition evaluator (values, booleans, time-of-day, AND/OR trees, modulo milestones) — plus the fact that nothing at runtime ever evaluates the rules, so they never fire.

## Overview

Conditional Rules lets you write *smart* auto-replies: instead of one fixed message per event, you write rules with conditions — "reply only when the follower count is a multiple of 100", "only after 20:00", "only when the user is a subscriber" — and a priority-ordered reply template for each. The page (whose header literally reads "Auto-Reply Rules") gives you a condition builder, a raw-JSON editor, and rule management, all persisted to the `AutoReplyRules` table.

The honest status: **the page is write-only in the current build.** The evaluator that would run these rules (`RuleEvaluator`) is complete and correct, but its only consumer (`AutoReplyService.GetAutoReplyAsync`) is never called by production code, so rules you create here are saved and never evaluated. This page also documents the related `TriggerEngine`/`ActionDispatcher` scaffolding — an aspirational trigger→action pipeline that is stubbed out.

The simple, working alternative is per-event auto-reply templates — see [Auto-Replies](auto-replies.md).

## User Guide

Open **Stream Tools → Conditional Rules** in the sidebar. (Despite living under Stream Tools, the page edits auto-reply behavior; there is no separate entry under Chat Features.)

### Creating a rule

1. Click **New Rule**. Defaults: name "New Rule", platform "All", event "Follow", enabled, priority 0, empty condition, reply "Thank you, {username}!".
2. **Name** — anything, for your own reference.
3. **Platform** — `All`, `Twitch`, `Kick`, or `Discord`. (The whole rules engine has no production caller in the current build — see below — so no platform choice here has any effect yet. Discord itself is a real platform now; see [Discord](../platforms/discord.md).)
4. **Event Type** — one of `Follow`, `Subscription`, `Raid`, `Host`, `Bits`, `Donation`, `SubscriptionGift`, `ChatMessage`, `ChannelPointRedemption`.
5. **Enabled** — on/off switch.
6. **Priority** — lower number = higher priority; when several rules match, the first by priority wins.
7. **Condition** — see the builder below, or edit the JSON directly in the Condition JSON box.
8. **Reply Template** — the message to send, with variables like `{username}`, `{platform}`, `{count}`, `{amount}` (whatever the event context would supply).
9. **Save**. The rule appears in the list with its trigger count (always 0 in the current build — see below).

### Building conditions

Choose a **Type**, **Field**, **Operator**, and **Value**, then click **Add Condition**:

| Condition type | Meaning | Example fields |
|---|---|---|
| **Value** | Compare a field from the event context | `count`, `username`, `amount` |
| **Status** | Boolean field check | `is_subscriber`, `is_moderator` |
| **Time** | Compare against the current local time | `time_of_day` (HH:mm), `day_of_week`, `hour`, `date` |
| **Complex** | A group of nested conditions (created automatically when you add a second condition) | — |

Operators: `Equals`, `NotEquals`, `GreaterThan`, `LessThan`, `GreaterOrEqual`, `LessOrEqual`, `Contains`, `StartsWith`, `EndsWith`, `Modulo`.

Examples shown on the page: a milestone `count % 100 == 0` (Value type, `Modulo` operator — set the divisor to 100 and the value to 0), an `is_subscriber` status check, and `time_of_day >= 20:00`.

**Multiple conditions:** each time you add one, the previous root condition is wrapped into a `Complex` condition combining the old and new with **AND** logic (the builder cannot produce OR — hand-edit the JSON and set `"logicalOperator": "Or"` if you need it). **Clear Conditions** resets the JSON to `{}`. The raw JSON box is always editable, so you can craft trees by hand.

### Test Rule

**Test Rule** on a selected rule shows a summary MessageBox (name, platform, event, enabled, priority, trigger count, condition JSON, reply). It does **not** evaluate the rule against sample data — it is a viewer, not a tester.

## How It Works (Developer)

### Data model

A rule is an [AutoReplyRule.cs](../../src/VyralBot.Data/Entities/AutoReplyRule.cs) row: `Name`, `Platform` ("Twitch"/"Kick"/"Discord"/"All"), `EventType`, `IsEnabled`, `Priority`, `ConditionJson`, `ReplyTemplate`, `TriggerCount`, `LastTriggeredAt`. `ConditionJson` deserializes into [RuleCondition.cs](../../src/VyralBot.Core/Models/Rules/RuleCondition.cs) — a recursive model with `Type` (`Value|Status|Time|Complex`), `Field`, `Operator`, `Value`, `ModuloDivisor`, `SubConditions`, `LogicalOperator` (`And|Or`). Enums serialize as JSON strings via `JsonStringEnumConverter`.

### The evaluator (fully implemented)

[RuleEvaluator.cs](../../src/VyralBot.Core/Services/Rules/RuleEvaluator.cs) (`IRuleEvaluator`, DI-registered) evaluates a `RuleCondition` against a `Dictionary<string, object>` context:

- **Value** — numeric comparison first (int/long/float/double/decimal; `Equals`/`NotEquals` use an epsilon of 0.0001), falling back to case-insensitive string comparison (`Equals`, `NotEquals`, `Contains`, `StartsWith`, `EndsWith`). A missing context field yields `false`.
- **Modulo** — for int fields with `ModuloDivisor` set, computes `field % divisor` and compares the remainder to `Value` — milestone detection (`count % 100 == 0`). Returns `false` for non-int fields.
- **Status** — boolean field parsed against `Value` ("true"/"false") with Equals/NotEquals semantics.
- **Time** — evaluates against the **local** clock: `time_of_day` ("HH:mm"; `Equals` means within 1 minute), `day_of_week` (day name), `hour` (int), `date` (date comparison).
- **Complex** — recursive `All` (And) / `Any` (Or) over `SubConditions`.

### Why rules never fire

The only code that calls `IRuleEvaluator.EvaluateCondition` is `AutoReplyService.GetAutoReplyAsync` ([AutoReplyService.cs:61](../../src/VyralBot.Core/Services/Chat/AutoReplyService.cs)): rate-limit check → load rules by platform/event ordered by `Priority ASC` → evaluate → first match increments `TriggerCount` and returns the processed template. A solution-wide search finds **no production caller** of `GetAutoReplyAsync` — the live per-event auto-reply path (see [Auto-Replies](auto-replies.md)) uses only `ShouldSendReply` + `ProcessTemplate`. So the chain is: `ConditionalRulesView` writes rules → `AutoReplyRules` table → (nothing reads them at runtime). Wiring `GetAutoReplyAsync` into the `ChatViewModel` event handlers is the obvious revival path.

### TriggerEngine & ActionDispatcher (scaffolding)

A second, more ambitious automation layer exists as entities and services, largely as stubs:

- **[Trigger.cs](../../src/VyralBot.Data/Entities/Trigger.cs)** — an event matcher (`EventType`, `Platform`, `Condition`, `CooldownSeconds`, `Permission`, `ActionSequence` = JSON array of action IDs, `FireCount`). **[BotAction.cs](../../src/VyralBot.Data/Entities/BotAction.cs)** — an executable action (`ActionType` e.g. "SendMessage", "Delay", "HttpRequest", "RunScript", `Configuration` JSON, `TimeoutSeconds`, `RetryOnFailure`, `MaxRetries`). No UI edits these entities and no service reads them.
- **[TriggerEngine.cs](../../src/VyralBot.Core/Services/TriggerEngine/TriggerEngine.cs)** — started at app boot, subscribes to all `IEvent` on the [Event Bus](../architecture/event-bus.md) — but `OnEventReceivedAsync` is a TODO stub that only logs, `ReloadTriggersAsync` hard-sets `ActiveTriggerCount = 0`, and `FireTriggerAsync` dispatches a placeholder action GUID.
- **[ActionDispatcher.cs](../../src/VyralBot.Core/Services/ActionDispatcher/ActionDispatcher.cs)** — `ExecuteActionAsync` simulates work with `Task.Delay(100)` inside a 30 s linked-CTS timeout (TODO: load the action from the database); `ExecuteSequenceAsync` runs actions in order, stopping on the first failure; `CancelTriggerActions` cancels all running actions (the trigger→action mapping is not tracked yet). [ActionContext.cs](../../src/VyralBot.Core/Models/ActionContext.cs) carries `TriggerId`, `Platform`, `Username`, a variable dictionary, and a cancellation token.

## Files & Code

| File | Role |
|---|---|
| [ConditionalRulesViewModel.cs](../../src/VyralBot.UI/ViewModels/ConditionalRulesViewModel.cs) | Page ViewModel: rule CRUD, condition builder (AND wrapping), raw JSON editing, Test Rule summary |
| [ConditionalRulesView.xaml](../../src/VyralBot.UI/Views/ConditionalRulesView.xaml) | Page layout (header "Auto-Reply Rules": list, editor, builder) |
| [ConditionalRulesView.xaml.cs](../../src/VyralBot.UI/Views/ConditionalRulesView.xaml.cs) | Code-behind |
| [AutoReplyRule.cs](../../src/VyralBot.Data/Entities/AutoReplyRule.cs) | Rule entity |
| [RuleCondition.cs](../../src/VyralBot.Core/Models/Rules/RuleCondition.cs) | Condition model: types, operators, modulo divisor, recursive sub-conditions |
| [RuleEvaluator.cs](../../src/VyralBot.Core/Services/Rules/RuleEvaluator.cs) | The complete condition evaluator (value/status/time/complex) |
| [AutoReplyService.cs](../../src/VyralBot.Core/Services/Chat/AutoReplyService.cs) | `GetAutoReplyAsync` — the (uncalled) rules executor |
| [TriggerEngine.cs](../../src/VyralBot.Core/Services/TriggerEngine/TriggerEngine.cs) / [ITriggerEngine.cs](../../src/VyralBot.Core/Services/TriggerEngine/ITriggerEngine.cs) | Stubbed event→trigger engine (started at boot, does nothing) |
| [ActionDispatcher.cs](../../src/VyralBot.Core/Services/ActionDispatcher/ActionDispatcher.cs) / [IActionDispatcher.cs](../../src/VyralBot.Core/Services/ActionDispatcher/IActionDispatcher.cs) | Stubbed action executor (simulated work) |
| [ActionContext.cs](../../src/VyralBot.Core/Models/ActionContext.cs) | Execution context for actions |
| [Trigger.cs](../../src/VyralBot.Data/Entities/Trigger.cs) / [BotAction.cs](../../src/VyralBot.Data/Entities/BotAction.cs) | Trigger/action entities (no UI, no readers) |
| [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs) | `AutoReplyRules` table + indexes (lines 2374-2392) and CRUD (lines 1205-1294) |

Modulo milestone detection, the evaluator's most distinctive branch:

```csharp
if (condition.Operator == ComparisonOperator.Modulo && condition.ModuloDivisor.HasValue)
{
    if (fieldValue is int intValue)
    {
        var remainder = intValue % condition.ModuloDivisor.Value;
        return int.TryParse(condition.Value, out var expectedRemainder) && remainder == expectedRemainder;
    }
    return false;
}
```

The condition builder's AND-wrapping behavior when adding a second condition:

```csharp
// If we already have a root condition, wrap both in a Complex condition with AND logic
if (rootCondition != null)
{
    var complexCondition = new RuleCondition
    {
        Type = ConditionType.Complex,
        LogicalOperator = LogicalOperator.And,
        SubConditions = new() { rootCondition, newCondition }
    };
    EditConditionJson = JsonSerializer.Serialize(complexCondition, ...);
}
```

## Data & Settings

- **AutoReplyRules** table (per-profile SQLite, see [Database Reference](../reference/database-reference.md)): `Name`, `Platform`, `EventType`, `IsEnabled`, `Priority`, `ConditionJson`, `ReplyTemplate`, `TriggerCount`, `LastTriggeredAt`. Indexed on `(Platform, EventType)`, `IsEnabled`, and `Priority`. Rows are only ever written by this page — no seeder, no runtime writer.
- `TriggerCount`/`LastTriggeredAt` are updated only by the uncalled `IncrementRuleTriggerCountAsync` path, so they stay at 0/`null`.
- `Triggers`/`BotActions` tables exist in the schema but nothing reads or writes them (no seeded rows).

## Status & Known Issues

- **Rules never fire.** `GetAutoReplyAsync` (the only evaluator consumer) has zero production callers; the Conditional Rules page is a working editor for rows nothing reads. — [known issues ledger](../reference/known-issues.md)
- **"Test Rule" does not evaluate** — it shows a MessageBox summary of the rule without running the condition against sample data.
- The condition builder can only produce **AND** combinations; OR requires hand-editing the JSON to `"logicalOperator": "Or"`.
- Time conditions use the **local** machine clock, not the channel's timezone.
- The event-type list (`Donation`, `ChatMessage`, ...) does not exactly match the live event vocabulary used by the working per-event auto-replies (`Cheer`, `GiftSub`, ...) — even if the engine were wired, names would have to be reconciled.
- **TriggerEngine/ActionDispatcher are stubs** despite starting at boot: the event handler is a TODO that only logs, `ActiveTriggerCount` is hard-set to 0, and the dispatcher simulates actions with `Task.Delay(100)`. `Trigger`/`BotAction` entities have no UI and no readers. — [known issues ledger](../reference/known-issues.md)
- The working alternative for condition-less replies is [Auto-Replies](auto-replies.md); for firing test events see [Event Testing](event-testing.md).
