# Warning Command Service

## Overview
Manual moderator warnings that feed the auto-mod escalation ladder
(`!warn` / `!warnings` / `!clearwarn`). One service, registered
`IWarningCommandService` → `WarningCommandService` (both in
`src/VyralBot.Core/Services/Moderation/WarningCommandService.cs`).
ChatViewModel calls `ProcessCommandAsync` in its command fallback chain; a
`null` return means "not mine" so other handlers can take the message.

## Contract
```csharp
Task<string?> ProcessCommandAsync(string username, string platform, bool isModerator, string message)
```
Returns the chat reply, or `null` when the message is not a warning command.
Never throws — unexpected exceptions are caught, error-logged and answered
with short error text.

## Constructor
`IChatModerationEngine` (the Shared engine instance via DI),
`IDataService` (audit rows), optional `ModerationActionService`
(defaults to `ModerationActionService.Shared` — the instance the chat
pipelines execute verdicts on), optional `ILogger<WarningCommandService>`.

## Commands (all moderator-only)
| Command | Reply shape |
|---|---|
| `!warn <user> [reason]` | `⚠ {user} warned ({N} total) — next: {ladder action}`; when the warn tips onto a Timeout/Ban rung: `… — escalated: timeout {s}s` / `… — escalated: ban — next: …` |
| `!warnings <user>` | `⚠ {user}: {N} warning(s) — next: {action} (timeout at {T}, ban at {B}, {W}m decay window)` or `✅ {user} has no active warnings.` |
| `!clearwarn <user>` | `✅ Warnings cleared for {user} (had {N}).` |

Non-mods get `❌ Only moderators can manage warnings.` Usernames are stripped
of a leading `@`. The `@`-less reason text (capped at 200 chars, default
"no reason given") is info-logged and stored in the audit row — no secrets.

## Shared escalation ladder (the core design)
A manual warn advances the SAME per-viewer strike store as rule violations —
`IChatModerationEngine.RegisterManualWarning(username, platform, reason)`
applies the identical decay window + increment as a rule strike and returns
the ladder's answer (`LadderAction(Strikes, Action, TimeoutSeconds)`) from
the single resolver `ResolveLadderAction` that `Strike()` also uses (one
ladder, no drift). `GetStrikeCount(platform, username)` is the read-only
decay-aware count; `!clearwarn` maps to the engine's existing `ResetViewer`.
Manual warns work even while the auto-mod master switch is off — they are a
moderator decision, not a detection.

Consequences:
- The viewer's NEXT rule violation escalates per the shared rungs and fires
  through the normal `ModerationActionService.ExecuteAsync` pipeline.
- A manual warn that itself tips onto a Timeout/Ban rung fires that action
  IMMEDIATELY through the same pipeline (adapter enforcement, graceful
  degradation, AutoMod audit row) — one punishment per strike, never doubled.
- A warning-level strike only records + audits (running a Warn verdict through
  the action pipeline would double-post a notice next to the command's reply).

## Audit (ModActions log)
- Warning-level `!warn` and `!clearwarn` persist via
  `IDataService.SaveModActionAsync` — the same save path
  `ModerationActionService` uses for rule-violation actions — with the
  issuing mod as actor (the `ModCommandEngine` precedent) and platform
  prefixed into `Details` (`"Twitch: manual warning (strike 2) — reason"`).
- Escalated timeout/ban actions are audited by `ModerationActionService`
  itself (actor `AutoMod`, rule type `ManualWarning`).

## Persistence (accepted trade-off)
Warning counts are the engine's in-memory escalation state (5k-viewer cap,
strike-decay window) — **restarting VyralBot clears all warning counts**.
Only the ModActions audit rows survive restarts (visible in the Moderator
dashboard's Action Log).

## Tests
`tests/VyralBot.UI.Tests/Wave16WarningCommandTests.cs` — gating/prefix
disambiguation, both shared-ladder orders (warn → next violation escalates;
rule strike → next warn escalates through the pipeline), doubling rungs →
ban, ladder-disabled mode, audit actors, per-platform state, clearwarn
ladder restart.
