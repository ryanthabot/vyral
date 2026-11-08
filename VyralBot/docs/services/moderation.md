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
  (ChatViewModel message handlers — wired in Wave 2: each platform handler calls
  `TryAutoModerateAsync` → `Evaluate` before display/processing and drops the
  message on a verdict; the generic handler drops already-moderated messages and
  evaluates only platforms without a dedicated handler, e.g. TikTok; own-account
  senders and Discord DMs are exempt), the
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
  notice + capability flags). The composition root (`App.OnStartup`) registers
  four adapters: `TwitchModerationAdapter` (full), `DiscordModerationAdapter`
  (full), `KickModerationAdapter` + `YouTubeModerationAdapter` (notice-only).
- `ExecuteAsync(message, verdict)` — degrades Ban → Timeout → Delete → Warn,
  Timeout → Delete → Warn; a platform with no adapter is logged-only. Never
  throws (per-call try/catch). Every action — executed, degraded or logged-only
  — is written via `IDataService.SaveModActionAsync` (actor `AutoMod`), so the
  log survives restarts and renders in the dashboard Action Log.
- `ModerationPlatformParity.Table` — the audit-truth for per-platform
  enforcement (Wave 3): **Twitch full** (Helix ban/timeout/delete via
  `ITwitchService.TimeoutUserAsync`/`BanUserAsync`/`DeleteMessageAsync`; the
  chat pipeline does not parse IRC message ids, so a Delete without one
  degrades to a 1s purge-timeout — timeouts purge the sender's messages);
  **Discord full** (ban/timeout by username, minute-granular; no delete-by-id
  API, so Delete verdicts degrade to warn); **Kick/YouTube notice-only**
  (adapters post warn notices; Kick webhooks are inbound-only); **TikTok none**
  (no chat integration — verdicts logged only). The Moderator dashboard's
  parity card renders `ModerationPlatformParity.Snapshot` rows (the Table
  merged with live adapter registration), so it can never drift from the
  Table.

### ModerationRulesRepository (`ModerationRulesRepository.cs`)
Persists the `ModerationRuleSet` as JSON under config key
`Moderation.AutoModRules` (category `Moderation`, per-profile via
`IDataService`) and applies every load/save to the engine. Corrupt JSON falls
back to `ModerationRuleSet.CreateDefault()` (master switch OFF — opt-in).

### ModeratorViewModel Auto-Moderation card
The action service is configured with the profile `IDataService` + logging
ONCE at app startup (`App.OnStartup`) — never on page init (that would drop
its logger). `LoadAutoModConfigCommand` (page init) loads + applies the rules;
`SaveAutoModConfigCommand` normalizes + persists + applies; exemption toggles
are uniform across rules in the UI (config stores them per rule);
`TestAutoModMessageCommand` evaluates a sample message without executing
anything; `ResetAutoModEscalationCommand` clears all viewer state. The card's
text areas cover every engine list surface (Wave 7 audit): link allowlist,
banned words, **word allowlist** (words that never count as banned — editor
added in Wave 7, previously engine-configurable but not exposed) and exempt
users, one entry per line. The parity
card renders `ModerationPlatformParity.Snapshot` rows — platform name,
enforcement-level badge (Full / Notice-only / None) and a detail line — so the
display always mirrors the audit Table.

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

## Wiring status (Wave 3, 2026-11-07)
- Done: engine, action router + degradation, rule persistence, dashboard card
  (Wave 1); auto-mod evaluated on every platform in the ChatViewModel message
  handlers — Twitch/Kick/YouTube/Discord dedicated paths plus the generic
  handler for platforms without one (Wave 2); all four platform adapters
  registered at startup with Twitch upgraded to full Helix enforcement
  (timeout/ban/delete, delete-without-id → 1s purge-timeout) (Wave 3).
- Remaining seams: parse IRC message ids in the Twitch pipeline so Delete
  verdicts call `DELETE /helix/moderation/chat` directly; Kick/YouTube
  per-viewer action APIs when their services gain them; TikTok has no chat
  integration to enforce through.
