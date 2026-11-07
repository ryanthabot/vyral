# Moderation System

## Overview
Two cooperating layers under `src/VyralBot.Core/Services/Moderation/`:

1. **Auto-moderation** (chat content filter) — `ChatModerationEngine` evaluates
   every incoming chat message against six detection rules (banned words, links,
   caps, emojis, repeated messages, ASCII art) with per-rule thresholds,
   exemptions and an escalation ladder; `ModerationActionService` executes the
   verdicts per platform with graceful degradation and persists every action.
2. **Mod commands** — `ModCommandEngine` runs the `!mod <action>` Discord mod
   channel command set gated by the `ModTiers` ladder (TrialMod → Admin), and
   `ModRoleService`/`IModTierResolver` resolve who holds which tier.

The Moderator dashboard (`ModeratorViewModel` + `ModeratorView`) edits both:
mod channel, mod roles, role→tier mapping, the action log, the Auto-Moderation
card and the mod_command workflows (visual Flow editor).

## Auto-moderation

### ChatModerationEngine (`ChatModerationEngine.cs`)
- **Hot path:** synchronous, no I/O; the only regex is a static compiled link
  detector; banned words / allowlists are precomputed `HashSet`/`List` caches
  rebuilt only when `ApplyRules` swaps the (frozen) rule snapshot; caps / emoji
  / ASCII-art checks are single linear char scans. Per-viewer state (repeat
  streaks + strikes) is a bounded dictionary (`MaxTrackedViewers = 5000`) with
  stale-sweep + wholesale-clear overflow handling, guarded by one lock held for
  O(1) work.
- **Process-wide instance:** `ChatModerationEngine.Shared` — the chat pipelines
  (ChatViewModel message handlers — wiring pending, see Wave 1 audit), the
  Moderator dashboard and tests all see one rule set / escalation state. DI may
  construct separate instances for tests.
- **Rules** (priority order): BannedWords → Links → Caps → Emojis →
  RepeatedMessage → AsciiArt. Broadcaster is always exempt; per-rule exemptions
  for moderators / VIPs / subscribers; per-user exemption list.
- **Escalation:** per viewer per platform strike count with decay window
  (default 15 min); Delete/Warn upgrade to Timeout at `StrikesToTimeout`
  (doubling duration per extra strike), to Ban at `StrikesToBan`.
- **Verdict:** `ModerationVerdict(Rule, Action, TimeoutSeconds, Reason, Strikes)`;
  `null` = clean.

### ModerationActionService (`ModerationActionService.cs`)
- `IModerationPlatformAdapter` — the per-platform seam (delete/timeout/ban/
  notice + capability flags). Adapters are registered by the composition root
  or a chat pipeline; **none are wired yet** (Wave 1 built the engine + router).
- `ExecuteAsync(message, verdict)` — degrades Ban → Timeout → Delete → Warn,
  Timeout → Delete → Warn; a platform with no adapter is logged-only. Never
  throws (per-call try/catch). Every action — executed, degraded or logged-only
  — is written via `IDataService.SaveModActionAsync` (actor `AutoMod`), so the
  log survives restarts and renders in the dashboard Action Log.
- `ModerationPlatformParity.Table` — the audit-truth for per-platform
  capability: Discord full; Twitch/Kick/YouTube/TikTok have no per-viewer
  moderation API wired today (Twitch Helix automod scopes are requested but
  unused; Kick webhooks are inbound-only) — verdicts degrade to warn there.

### ModerationRulesRepository (`ModerationRulesRepository.cs`)
Persists the `ModerationRuleSet` as JSON under config key
`Moderation.AutoModRules` (category `Moderation`, per-profile via
`IDataService`) and applies every load/save to the engine. Corrupt JSON falls
back to `ModerationRuleSet.CreateDefault()` (master switch OFF — opt-in).

### ModeratorViewModel Auto-Moderation card
`LoadAutoModConfigCommand` (page init) loads + applies the rules and
configures `ModerationActionService.Shared` with the profile `IDataService`;
`SaveAutoModConfigCommand` normalizes + persists + applies; exemption toggles
are uniform across rules in the UI (config stores them per rule);
`TestAutoModMessageCommand` evaluates a sample message without executing
anything; `ResetAutoModEscalationCommand` clears all viewer state. The parity
list renders `ModerationPlatformParity.Describe`.

## Mod commands (!mod)

- `ModCommandEngine` — gates the configured Discord mod channel, resolves the
  caller tier, enforces `ModTiers`, executes built-ins (currency, giveaways,
  polls, assignmod, stream effects) and delegates kick/ban/mute/unban/timeout/
  unmute/purge/slowmode to the Discord command handler; unknown verbs fall
  through to `IModWorkflowInvoker` (custom `!mod` workflows). Every executed
  action is audited via `SaveModActionAsync`.
- `ModTiers` — cumulative ladder TrialMod ⊂ Mod ⊂ SeniorMod ⊂ Admin.
- `ModRoleService` — persisted `ModRoleAssignment` upsert + best-effort
  Discord role grant.
- `IModTierResolver` — explicit assignment vs highest Discord role→tier
  mapping, whichever ranks higher.

## Wiring status (Wave 1, 2026-11-07)
- Done: engine, action router + degradation, rule persistence, dashboard card.
- Pending (next waves): evaluate `ChatModerationEngine.Shared` in the
  ChatViewModel message handlers (Twitch/Kick/YouTube/Discord/generic paths —
  currently ALL platforms bypass auto-mod) and register platform adapters
  (Discord first via `IDiscordService`).
