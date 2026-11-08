# Conditional Rules

> A complete CRUD page for conditional auto-reply rules backed by a fully-implemented condition evaluator (values, booleans, time-of-day, AND/OR trees, modulo milestones) — rules fire as conditional overrides of per-event auto-replies on Kick and Twitch with **no default message required**, and Discord member-join/boost events evaluate rules too (KI-57 resolved, 1.1.0).

## Overview

Conditional Rules lets you write *smart* auto-replies: instead of one fixed message per event, you write rules with conditions — "reply only when the follower count is a multiple of 100", "only after 20:00", "only when the user is a subscriber" — and a priority-ordered reply template for each. The page (whose header literally reads "Auto-Reply Rules") gives you a condition builder, a raw-JSON editor, and rule management, all persisted to the `AutoReplyRules` table.

The honest status: **rules are live and un-gated (since 1.1.0).** When a Kick or Twitch event's reply moment arrives, the pipeline asks the rules engine for a matching rule first (priority order) — a match **replaces** the default message with the rule's reply template and bumps its trigger count. Rules fire standalone: the default-message settings are no longer a gate — the handler always consults the rules and passes an empty default when none is configured, sending only when the combined result is non-empty (so a rule with the default message left empty fires on its own). **Discord evaluates rules too**: member joins map to the `Follow` vocabulary and server boosts to `Subscribe`, so Platform `Discord` and `All` rules match those events (joins/boosts still ride the Server Members intent gate — [Discord](../platforms/discord.md)). Saves are validated (wave 10, 2026-11-07): a rule needs a name, an event type and a reply, and invalid condition JSON is rejected with the parse error instead of saving a rule that could never evaluate. The page also documents the related `TriggerEngine`/`ActionDispatcher` scaffolding — an aspirational trigger→action pipeline that is still stubbed out.

The simple, working alternative is per-event auto-reply templates — see [Auto-Replies](auto-replies.md).

## User Guide

Open **Stream Tools → Conditional Rules** in the sidebar. (Despite living under Stream Tools, the page edits auto-reply behavior; there is no separate entry under Chat Features.)

### Creating a rule

1. Click **New Rule**. Defaults: name "New Rule", platform "All", event "Follow", enabled, priority 0, empty condition, reply "Thank you, {username}!".
2. **Name** — anything, for your own reference.
3. **Platform** — `All`, `Twitch`, `Kick`, or `Discord`. `All` and the event's own platform match (`Platform = @Platform OR Platform = 'All'`). `Discord` rules match member-join (`Follow`) and server-boost (`Subscribe`) events — intent-gated on Discord's side (see [How rules fire](#how-rules-fire)).
4. **Event Type** — one of `Follow`, `Subscribe`, `Resub`, `GiftSub`, `Raid`, `Cheer`, `Host`, `RewardRedeemed`, `KicksGifted` — the exact vocabulary the live pipeline feeds the engine (reconciled 2026-11-07; dead types like `ChatMessage` and `Donation` were removed from the list). `Host`/`KicksGifted` are Kick-only and `Raid`/`Cheer`/`Resub` are Twitch-only in the current pipeline.
5. **Enabled** — on/off switch.
6. **Priority** — lower number = higher priority; when several rules match, the first by priority wins.
7. **Condition** — see the builder below, or edit the JSON directly in the Condition JSON box.
8. **Reply Template** — the message to send, with variables like `{username}`, `{platform}`, `{count}`, `{amount}` (whatever the event context would supply).
9. **Save** — validated: name, event type and reply template are required, and invalid condition JSON is rejected with the parse error (a rule that could never evaluate cannot be saved). The rule appears in the list with its trigger count, which now increments every time the rule matches a live event.

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

**Test Rule** on a selected rule **actually evaluates** it (since 2026-11-07): the condition is parsed and run by the real `RuleEvaluator` against a sample context (`username`, `count`, `amount`, `bits`, `viewers`, `is_subscriber`, …), and the MessageBox reports **MATCHED / NOT MATCHED** plus the reason — a missing sample field, an empty condition ("matches every event"), an empty complex group ("never matches"), or a genuine compare. Invalid condition JSON is called out as un-savable dead weight. Time conditions evaluate against the clock at the moment you click.

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

### How rules fire

The only code that calls `IRuleEvaluator.EvaluateCondition` in production is `AutoReplyService.GetAutoReplyAsync` ([AutoReplyService.cs:63](../../src/VyralBot.Core/Services/Chat/AutoReplyService.cs)): load rules by platform/event ordered by `Priority ASC, Id ASC` (`Platform = @Platform OR Platform = 'All'`) → evaluate → first match increments `TriggerCount`/`LastTriggeredAt` and returns the processed template. Its caller is `ChatViewModel.BuildAutoReplyTextAsync` (ChatViewModel.cs:4315): **every** Kick and Twitch event handler builds its auto-reply through this method, and a matching rule's template **replaces** the event's default message before variables are resolved (the rule reply then also passes through the full variable-replacement service).

Two behaviors to know:

- **No default-message gate (un-gated since 1.1.0)** — each call site passes the event's default template **or an empty string** when the auto-reply toggle is off or the default message box is empty (`settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage) ? settings.AutoReplyMessage : ""`). The rules lookup runs either way; the reply is sent only when the combined result is non-empty — so rules fire standalone with no default message configured, and an event with no rule and no default sends nothing.
- **Discord call sites exist** — the pipeline invokes the rules engine for Kick (`Follow`, `Subscribe`, `GiftSub`, `Host`, `KicksGifted`, `RewardRedeemed`), Twitch (`Follow`, `Subscribe`, `Resub`, `GiftSub`, `Raid`, `Cheer`, `RewardRedeemed`), and Discord (`Follow` = member join, `Subscribe` = server boost; ChatViewModel.cs:7165/7216, replies sent through the connected Discord service). Discord joins/boosts themselves require the Server Members intent (KI-52) — until the portal toggle is on, no Discord events arrive to match against.

### TriggerEngine & ActionDispatcher (scaffolding)

A second, more ambitious automation layer exists as entities and services, largely as stubs:

- **[Trigger.cs](../../src/VyralBot.Data/Entities/Trigger.cs)** — an event matcher (`EventType`, `Platform`, `Condition`, `CooldownSeconds`, `Permission`, `ActionSequence` = JSON array of action IDs, `FireCount`). **[BotAction.cs](../../src/VyralBot.Data/Entities/BotAction.cs)** — an executable action (`ActionType` e.g. "SendMessage", "Delay", "HttpRequest", "RunScript", `Configuration` JSON, `TimeoutSeconds`, `RetryOnFailure`, `MaxRetries`). No UI edits these entities and no service reads them.
- **[TriggerEngine.cs](../../src/VyralBot.Core/Services/TriggerEngine/TriggerEngine.cs)** — started at app boot, subscribes to all `IEvent` on the [Event Bus](../architecture/event-bus.md) — but `OnEventReceivedAsync` is a TODO stub that only logs, `ReloadTriggersAsync` hard-sets `ActiveTriggerCount = 0`, and `FireTriggerAsync` dispatches a placeholder action GUID.
- **[ActionDispatcher.cs](../../src/VyralBot.Core/Services/ActionDispatcher/ActionDispatcher.cs)** — `ExecuteActionAsync` simulates work with `Task.Delay(100)` inside a 30 s linked-CTS timeout (TODO: load the action from the database); `ExecuteSequenceAsync` runs actions in order, stopping on the first failure; `CancelTriggerActions` cancels all running actions (the trigger→action mapping is not tracked yet). [ActionContext.cs](../../src/VyralBot.Core/Models/ActionContext.cs) carries `TriggerId`, `Platform`, `Username`, a variable dictionary, and a cancellation token.

## Files & Code

| File | Role |
|---|---|
| [ConditionalRulesViewModel.cs](../../src/VyralBot.UI/ViewModels/ConditionalRulesViewModel.cs) | Page ViewModel: rule CRUD with save validation, condition builder (AND wrapping), raw JSON editing, evaluating Test Rule |
| [ConditionalRulesView.xaml](../../src/VyralBot.UI/Views/ConditionalRulesView.xaml) | Page layout (header "Auto-Reply Rules": list, editor, builder) |
| [ConditionalRulesView.xaml.cs](../../src/VyralBot.UI/Views/ConditionalRulesView.xaml.cs) | Code-behind |
| [AutoReplyRule.cs](../../src/VyralBot.Data/Entities/AutoReplyRule.cs) | Rule entity |
| [RuleCondition.cs](../../src/VyralBot.Core/Models/Rules/RuleCondition.cs) | Condition model: types, operators, modulo divisor, recursive sub-conditions |
| [RuleEvaluator.cs](../../src/VyralBot.Core/Services/Rules/RuleEvaluator.cs) | The complete condition evaluator (value/status/time/complex) |
| [AutoReplyService.cs](../../src/VyralBot.Core/Services/Chat/AutoReplyService.cs) | `GetAutoReplyAsync` — the rules executor, called from the Kick/Twitch auto-reply path |
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
- `TriggerCount`/`LastTriggeredAt` increment via `IncrementRuleTriggerCountAsync` every time a rule matches a live Kick/Twitch event (and an edit/save no longer clobbers the counter — the settings UPDATE deliberately excludes `TriggerCount`).
- `Triggers`/`BotActions` tables exist in the schema but nothing reads or writes them (no seeded rows).

## Status & Known Issues

- **Rules fire with no default message required (KI-57 resolved)** — a matching rule replaces the event's default auto-reply text on Kick/Twitch; with no default message configured, rules for that event still fire on their own. — [known issues ledger](../reference/known-issues.md)
- **Discord rules live on join/boost** — member joins evaluate `Follow` rules and server boosts `Subscribe` rules (Platform `Discord` and `All`); the events themselves are intent-gated (KI-52). — [known issues ledger](../reference/known-issues.md)
- The condition builder can only produce **AND** combinations; OR requires hand-editing the JSON to `"logicalOperator": "Or"`.
- Time conditions use the **local** machine clock, not the channel's timezone.
- **TriggerEngine/ActionDispatcher are stubs** despite starting at boot: the event handler is a TODO that only logs, `ActiveTriggerCount` is hard-set to 0, and the dispatcher simulates actions with `Task.Delay(100)`. `Trigger`/`BotAction` entities have no UI and no readers. — [known issues ledger](../reference/known-issues.md)
- The rule-less alternative is plain [Auto-Replies](auto-replies.md); for firing test events see [Event Testing](event-testing.md).
