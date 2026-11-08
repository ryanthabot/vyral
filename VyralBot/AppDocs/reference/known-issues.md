# Known Issues Ledger

The numbered, severity-tagged inventory of every known defect, wiring gap, dead surface, and doc-drift item in the current build — verified against code by the six-pass review that produced this documentation, re-verified after the 2026-08-15 fix pass, and reconciled again against the 2026-11-07 production-marathon waves (0–17, including the release-wave, wave-12 documentation, wave-14 stream-tools, wave-15 verification, wave-16 overlay/chat-modes and wave-17 fun-pack/live-trigger/quick-action passes).

**Every feature page in these docs references this ledger** rather than repeating caveats inline. If a page says "see the Known Issues Ledger", the relevant entry is here. Severity meanings:

- **Critical** — silences or corrupts a shipped feature; the feature cannot work as built.
- **Wiring gap** — the feature runs but a connection, persistence, or plumbing step is missing or wrong.
- **Dead surface** — UI or code that renders/exists but does nothing.
- **Security** — hardening or trust concern, not necessarily a functional break.
- **Limitation** — an honest current-state boundary (an API that does not exist, a deliberate degradation), documented rather than promised.
- **Doc drift** — shipped root documents that disagree with the code.

Counts (as of 2026-11-07, after the final wave-17 recount): 64 entries — **30 resolved**, **7 partially resolved** (KI-5, KI-18, KI-21, KI-24, KI-31, KI-36, KI-53), **2 intended behavior / documented behavior change** (KI-14, KI-30), and **25 open** (KI-26, KI-27, KI-32–KI-34, KI-37, KI-38, KI-40–KI-42, KI-44–KI-47, KI-50–KI-52, KI-56, KI-58–KI-64). The wave-17 pass verified the fun command pack (`!8ball`/`!dice`/`!roll`/`!coinflip`/`!flip`/`!lurk`/`!hug`/`!rng`), the stream live/offline workflow triggers (`streamlive`/`streamoffline`) and the Home Quick Actions bar against code as they landed: **all three shipped in this build** and are documented (Command Catalog → Fun commands, Automation → stream live/offline triggers, Dashboard → Quick actions). None of the three resolves an open ledger entry or adds a new one — the counts above are unchanged. Original severity split of the 25 pre-fix entries: 7 Critical, 9 Wiring gaps, 2 Dead surface, 6 Security, 1 Doc drift. Wave-6 features (quotes, event-testing upgrade, control API, timer activity gate, OBS auto-reconnect) were re-checked against code and contributed no new entries; the wave-9 pass added KI-54–KI-56 (statistics charts, soundboard hotkeys, TikTok chat) and moved KI-31 (Kick recorded-status follower fallback) and KI-53 (Wave-9 Kick/YouTube moderation adapters) to partially resolved; the wave-10/11 pass moved KI-55 to partially resolved (the `play_sound` workflow block adds command/timer/event-triggered sound playback — global hotkeys and a soundboard UI remain) and added KI-57 (conditional rules — since resolved). The wave-12 docs pass (which saw the wave-12 feature code land mid-verification — charts and hotkeys initially absent, then shipped) moved **six entries to resolved**: **KI-16** (the audio port now follows Web Server settings, and Restart Server genuinely restarts the embedded server), **KI-57** (detail body aligned with its resolved summary row — rules un-gated from the default message, Discord join/boost evaluate rules), **KI-12/KI-28** (event auto-replies now run the full variable-replacement engine after the context-key pass, so DB/social variables resolve — verified at `ChatViewModel.BuildAutoReplyTextAsync`, ChatViewModel.cs:4334-4348), and **KI-54/KI-55** (the wave-12 code landed mid-pass: Statistics gained platform-colored overview + contributor `SimpleBarChart`s, and `GlobalHotkeyService` + the Sounds page Hotkey column shipped global soundboard hotkeys — 10 slots, conflict-checked, persisted, fire while gaming). The wave-14 pass verified the mod/stream-tools, TTS, death-counter, leaderboard-overlay, Streaming-block and deck-chip features against code and added three honest-limitation entries: **KI-58** (stream-tools command degradations), **KI-59** (RESOLVED: the seeded built-in now defers (returns null) and the placeholder method was removed — StreamToolsCommandService answers), **KI-60** (the OBS/clip workflow blocks need the OBS remote bridge actually running). The wave-15 pass verified the command import, loyalty seasons, first-time-chatter and 12-template features against code as they landed (mid-pass, as in wave 12): all four shipped and are documented on their pages; the pass added **KI-61** (import translation scope + first-chatter detection semantics) and updated every template/test-event count. The wave-16 pass verified the chat-mode commands, the chat-feed/goals/poll overlays, ad-hoc `!poll start`, the `!warn` family and session summaries against code as they landed: all five features shipped and are documented on their pages; the pass added three honest-limitation entries — **KI-62** (chat-mode auto-revert accuracy/restart), **KI-63** (overlay poll cadences + goals-bar semantics), **KI-64** (in-memory warning strikes and session chat counts).

Resolved entries are kept below (never deleted) with a ✅ marker and a one-line description of the fix; partially resolved entries carry ◐ with what remains.

---

## Summary

| ID | Severity | Area | Issue | Where documented |
|---|---|---|---|---|
| [KI-5](#ki-5-critical--giveawaypoll-follower-and-watchtime-eligibility-always-reject) | Critical ◐ | Giveaways / Polls | Watchtime eligibility now real; follower check real for Twitch, recorded-status fallback for Kick (webhook-seen followers), YouTube still `false` | [Giveaways](../stream-tools/giveaways.md), [Polls](../stream-tools/polls.md) |
| [KI-12](#ki-12-wiring-gap--auto-reply-templates-use-simple-replace) | Wiring gap ✅ | Auto-replies | RESOLVED: context keys substitute first, then the full variable engine runs on every event auto-reply (KI-28's fix closes this entry's remainder) | [Auto-Replies](../chat-features/auto-replies.md), [Variables Catalog](variables-catalog.md) |
| [KI-14](#ki-14-wiring-gap--auto-connect-silently-flips-chat-enabled-to-true) | Intended behavior (documented) | Chat settings | Auto-connect force-enabling `<Platform>ChatEnabled` stays as designed | [Chat Settings](../chat/chat-settings.md) |
| [KI-16](#ki-16-wiring-gap--audio-port-pinned-to-5000-connected-clients-stub) | Wiring gap ✅ | Audio | RESOLVED: client count live, port follows Web Server settings (read-only mirror), Restart Server genuinely restarts the embedded server | [Audio Settings](../stream-tools/audio.md) |
| [KI-18](#ki-18-dead-surface--dead-and-orphaned-code) | Dead surface ◐ | Code | `!watchtime` shadowing + dead wizard/env-var methods removed; other dead code remains | [Data Layer](../architecture/data-layer.md) |
| [KI-21](#ki-21-security--inconsistent-token-storage) | Security ◐ | Platforms | Wizards no longer write user env vars; Kick tokens unencrypted by design, YouTube plaintext | [Twitch](../platforms/twitch.md), [Kick](../platforms/kick.md) |
| [KI-24](#ki-24-security--robustness-notes) | Security ◐ | Event bus / Echo filter / Stub wizards | TikTok wizard saves "Coming Soon" (not fake Connected); the Discord wizard was replaced by a real bot-token integration; echo suppression now bot-account-scoped but still time-window based | [Event Bus](../architecture/event-bus.md), [TikTok & OBS](../platforms/tiktok-discord-obs.md) |
| [KI-26](#ki-26-wiring-gap--showin-chat-off-skips-eventstatistics-recording) | Wiring gap | Statistics / Event History | 6 handlers gated on ShowInChat skip recording EventStatistics when it is off | [Event History](../chat-features/event-history.md), [Statistics](../stream-tools/statistics.md) |
| [KI-27](#ki-27-wiring-gap--first-chat-bonus-means-first-currency-record) | Wiring gap | Currency | `AwardFirstChatBonusAsync` semantics = "first currency record", not true first chat | [Currency & Rewards](../stream-tools/currency.md) |
| [KI-28](#ki-28-wiring-gap--auto-reply-templates-still-do-not-resolve-db-or-social-variables) | Wiring gap ✅ | Auto-replies | `ProcessTemplate` remains `string.Replace` — DB/social variables do not resolve in auto-replies | [Auto-Replies](../chat-features/auto-replies.md), [Variables Catalog](variables-catalog.md) |
| [KI-29](#ki-29-wiring-gap--savecurrencysettingsasync-persists-only-a-field-subset) | Wiring gap ✅ | Currency | RESOLVED: the settings save now writes the full `CurrencySetting` row, and the Bank & Decay tab edits bank/decay in the UI (withdrawal cooldown enforced) | [Currency & Rewards](../stream-tools/currency.md) |
| [KI-30](#ki-30-behavior-change-documented--watchtime-earning-rate-is-now-configurable-defaults-to-10minute) | Behavior change (documented) | Currency / Watchtime | Watchtime earning uses `WatchTime*` settings — default 10/interval vs old hard-coded 1/min | [Watchtime](../stream-tools/watchtime.md), [Currency & Rewards](../stream-tools/currency.md) |
| [KI-31](#ki-31-wiring-gap--kick--youtube-follower-eligibility) | Wiring gap ◐ | Giveaways / Polls | Kick follower eligibility now honors the recorded viewer status (follow webhooks); YouTube has no follower concept — eligibility stays `false` there | [Giveaways](../stream-tools/giveaways.md), [Polls](../stream-tools/polls.md) |
| [KI-32](#ki-32-wiring-gap-minor--audio-settings-client-count-timer-only-stops-at-shutdown) | Wiring gap (minor) | Audio | `AudioSettingsViewModel` is a singleton — its 2s poll timer runs until shutdown | [Audio Settings](../stream-tools/audio.md) |
| [KI-33](#ki-33-wiring-gap--twitch-follower-check-requires-the-moderatorreadfollowers-scope) | Wiring gap | Giveaways / Polls / Twitch | Twitch follower eligibility needs `moderator:read:followers` on the stored user token; degrades to `false` | [Twitch](../platforms/twitch.md), [Giveaways](../stream-tools/giveaways.md) |
| [KI-34](#ki-34-wiring-gap--discord-integration-limitations) | Wiring gap | Discord | Message Content intent required; no slash commands; no non-chat Discord events; history rows not reloaded (single-channel bullet superseded per-bot by multi-bot, 2026-08-16) | [Discord](../platforms/discord.md), [Chat Window](../chat/chat-window.md) |
| [KI-36](#ki-36-wiring-gap--discord-multi-bot-transition-loose-ends) | Wiring gap ◐ | Discord | Wizard targets the setup bot only (no per-bot wizard); per-bot RECONNECT reads the shared legacy token; the legacy dashboard view is live again as the bots page's DASHBOARD tab | [Discord](../platforms/discord.md), [Platform Dashboards](../platforms/dashboards.md) |
| [KI-37](#ki-37-wiring-gap--commands-list-ignores-discord-bot-scope) | Wiring gap | Commands | The built-in `!commands` list does not filter by bot scope | [Commands](../chat-features/commands.md), [Command Catalog](command-catalog.md) |
| [KI-38](#ki-38-wiring-gap--discord-origin-race-can-fall-back-to-primary-routing) | Wiring gap | Discord / Event bus | The generic command event can beat the typed Discord event, so a reply routes through Primary instead of the origin bot | [Discord](../platforms/discord.md), [Event Bus](../architecture/event-bus.md) |
| [KI-40](#ki-40-wiring-gap--slash-command-registration-requires-the-bot-online) | Wiring gap | Workflows / Discord | Flow slash commands register only while a bot is online; offline saves register on the next save/reconnect | [Automation](../chat-features/automation.md), [Discord](../platforms/discord.md) |
| [KI-41](#ki-41-wiring-gap--converted-moderation-flows-use-fixed-numbers-not-args) | Wiring gap | Workflows / Commands | Converted moderation flows hard-code the Moderate block's default numbers (10 min / 10 s / 10 messages) — `{args}` can't feed its numeric fields | [Commands](../chat-features/commands.md), [Automation](../chat-features/automation.md) |
| [KI-42](#ki-42-wiring-gap--random-replies-convert-to-one-switch-case-per-reply) | Wiring gap | Workflows / Commands | Converted random-reply commands fan out one Send Message per stored reply — new replies need manual flow edits | [Commands](../chat-features/commands.md) |
| [KI-44](#ki-44-wiring-gap--prefix-commands-bypass-typed-command-parameters) | Wiring gap | Workflows / Discord | Typed parameters apply to Discord slash triggers only — prefix commands still pass everything through `{args}` | [Automation](../chat-features/automation.md), [Discord](../platforms/discord.md) |
| [KI-45](#ki-45-wiring-gap--botghost-musicvoicepremium-blocks-are-not-implemented) | Wiring gap | Workflows / Discord | The music/voice/premium BotGhost block suite (Create Music Player, voice actions, IFTTT, transcripts, Components V2, Premium Check) has no Vyral counterpart; "Music Smart Search" is a plain text parameter | [Automation](../chat-features/automation.md), [Discord](../platforms/discord.md) |
| [KI-46](#ki-46-wiring-gap--component-button-ids-are-positional-and-renumber-on-reorder) | Wiring gap | Workflows / Discord | Component button ids are positional (`btn0`, `btn1`, …) — reordering or removing buttons renumbers them, breaking `{buttonId}` comparisons until re-edited | [Automation](../chat-features/automation.md), [Discord](../platforms/discord.md) |
| [KI-47](#ki-47-wiring-gap--google-refresh-tokens-expire-in-testing-mode-youtube-needs-periodic-re-authorization) | Wiring gap | YouTube | Google Testing-mode refresh tokens expire after 7 days — YouTube auto-connect fails with `invalid_grant` until the user re-authorizes; the Platforms page now shows the live status and "Re-authorization required" instead of a false Connected | [YouTube](../platforms/youtube.md) |
| [KI-48](#ki-48-critical--alert-queue-stalled-after-the-first-alert) | Critical ✅ | Visual Alerts | RESOLVED: auto-close now `Close()`s (was `Hide()`) and alerts flow through a bounded FIFO (cap 10, drop-oldest) — alerts fire every time instead of stalling after the first | [Visual Alerts](../stream-tools/visual-alerts.md) |
| [KI-49](#ki-49-wiring-gap--auto-replies-master-toggle-was-dead-on-4-platforms) | Wiring gap ✅ | Chat settings / Auto-replies | RESOLVED: the Chat-Hub Auto-Replies master toggle loads + persists on Kick/YouTube/TikTok/Discord (Kick's gates the handlers, Twitch publishes the routing event) | [Chat Settings](../chat/chat-settings.md), [Auto-Replies](../chat-features/auto-replies.md) |
| [KI-50](#ki-50-limitation--no-output-device-selection-for-sounds) | Limitation | Sounds / Audio | Local sound playback always uses the system default output device — there is no device picker or per-output routing | [Sounds](../stream-tools/sounds.md), [Audio Settings](../stream-tools/audio.md) |
| [KI-51](#ki-51-limitation--twitch-delete-message-verdicts-degrade-to-a-purge-timeout) | Limitation | Moderation / Twitch | The auto-mod Delete verdict on Twitch degrades to a 1-second timeout (the IRC message id is not parsed) — it still removes the line via the purge side effect | [Twitch](../platforms/twitch.md), [Moderator Guide](../architecture/moderator-guide.md) |
| [KI-52](#ki-52-limitation--discord-joinboost-events-need-the-server-members-intent-portal-toggle) | Limitation | Discord | Member-join and boost events are fully wired but dormant: they require the privileged **Server Members Intent** toggle in the Discord Developer Portal, which the gateway deliberately does not request — the per-bot Welcome & Auto-Role card rides the same gate | [Discord](../platforms/discord.md) |
| [KI-53](#ki-53-partially-resolved--auto-mod-enforcement-per-platform-degradation) | Limitation ◐ | Moderation | Kick upgraded to full (Wave 9; delete needs one-time re-auth for pre-Wave-9 tokens), YouTube to delete-native (no public ban/timeout API exists); Discord's Delete verdict still degrades to a warn notice (ban/timeout are native) | [Moderator Guide](../architecture/moderator-guide.md), [Multi-Platform Parity](../platforms/multi-platform.md) |
| [KI-54](#ki-54-limitation--statistics-has-no-charts-everything-renders-as-datagrids) | Limitation ✅ | Statistics | RESOLVED (1.1.0): overview + top-contributor `SimpleBarChart` bar charts, platform-colored; only the per-day trend API stays UI-less | [Statistics](../stream-tools/statistics.md) |
| [KI-55](#ki-55-resolved--no-global-hotkey-soundboard) | Limitation ✅ | Sounds | RESOLVED (1.1.0): `GlobalHotkeyService` + the Sounds page Hotkey column — OS-global combos fire any library sound, incl. while gaming (10 slots, conflict-checked, persisted) | [Sounds](../stream-tools/sounds.md) |
| [KI-56](#ki-56-limitation--tiktok-has-no-chat-integration-identity-only) | Limitation | TikTok | TikTok is an identity only — the wizard is a stub, no chat service exists, nothing can be sent; linking/bonus/goals still count a TikTok identity | [TikTok](../platforms/tiktok.md), [TikTok & OBS](../platforms/tiktok-discord-obs.md) |
| [KI-57](#ki-57-wiring-gap--conditional-rules-fire-only-behind-the-default-message-gate-discord-rules-have-no-pipeline) | Wiring gap ✅ | Conditional Rules | ✅ RESOLVED (1.1.0): rules fire independently of the default-message gate on all platforms, and Discord evaluates rules (chat + join/boost) | [Conditional Rules](../chat-features/conditional-rules.md), [Auto-Replies](../chat-features/auto-replies.md) |
| [KI-58](#ki-58-limitation--stream-tools-command-degradations) | Limitation | Mod & stream tools | `!so` cannot read the target's channel info (URL + hype only; Discord plain hype); `!game` resolves via top-games search (first exact-name match); `!followage` null = "isn't following" even on lookup failure; non-Twitch `!uptime` reports live/offline only ("duration unknown") | [Command Catalog](command-catalog.md) |
| [KI-59](#ki-59-wiring-gap--the-seeded-uptime-built-in-shadows-the-new-helix-uptime) | Wiring gap | Commands / Stream tools | The seeded `!uptime` Commands-table row (config `Stream.StartedAtUtc`) answers first — the newer Helix-based `!uptime` only serves when the built-in is disabled or deleted | [Command Catalog](command-catalog.md) |
| [KI-60](#ki-60-limitation--the-obstwitch-streaming-workflow-blocks-need-their-backends-running) | Limitation | Workflows / OBS / Twitch | `obs_scene`/`obs_stream`/`obs_replay` need OBS running with its websocket enabled (replay buffer active for `obs_replay`); `twitch_clip` needs Twitch connected and live — otherwise the node takes the error branch | [Automation](../chat-features/automation.md), [Workflow Engine](../chat-features/workflows.md) |
| [KI-61](#ki-61-limitation--command-import-translates-common-variables-only-first-chatter-detection-uses-activity-records) | Limitation | Commands / Chat | Command import translates the common Nightbot/SE variables only (specials stay verbatim, flagged); first-time-chatter detection = "no watchtime/currency row", so pre-known silent viewers are never welcomed | [Commands](../chat-features/commands.md), [Event Reactions](../chat/event-reactions.md), [Workflow Engine](../chat-features/workflows.md) |
| [KI-62](#ki-62-limitation--chat-mode-auto-revert-accuracy-and-restart-behavior) | Limitation | Mod & stream tools / Twitch | Chat-mode auto-reverts fire within ~30 s of due (one-shot timer), are attempted once (a failed Twitch call drops the revert), and pending reverts are in-memory — an app restart leaves the modes enabled on Twitch | [Command Catalog](command-catalog.md) |
| [KI-63](#ki-63-limitation--overlay-widget-poll-cadences-and-the-goals-bar-semantics) | Limitation | Browser sources | The five overlay widgets poll (2–15 s) instead of pushing: goals lag up to 15 s and mix an aggregate completion count over the per-viewer target; the chat feed never shows TikTok (no chat service) | [Browser Sources](../stream-tools/browser-sources.md), [Vyral Goals](../stream-tools/vyral-goals.md) |
| [KI-64](#ki-64-limitation--warning-strikes-and-session-chat-counts-are-in-memory) | Limitation | Moderation / Statistics | Warning strikes live in the auto-mod engine's in-memory state — restarting VyralBot clears every viewer's warnings (audit rows survive); session summaries' chat-message counts only cover messages the running instance saw | [Command Catalog](command-catalog.md), [Statistics](../stream-tools/statistics.md), [Moderator Guide](../architecture/moderator-guide.md) |

### Resolved (2026-08-15 fix pass; KI-35 on 2026-08-16, KI-39/KI-43 on 2026-08-29)

| ID | Severity | Area | Issue (original) | Where documented |
|---|---|---|---|---|
| [KI-1](#ki-1-critical--chat-command-pipeline-is-dead) | Critical ✅ | Chat / Commands / Currency / Giveaways / Polls / Watchtime | `ChatMessageReceivedEvent` was never published — no chat command could execute | [Commands](../chat-features/commands.md), [Command Catalog](command-catalog.md) |
| [KI-2](#ki-2-critical--twitch-eventsub-likely-never-connects) | Critical ✅ | Twitch events | EventSub was gated on `Platform.UserId`, which no Twitch code path set | [Twitch](../platforms/twitch.md), [Event Reactions](../chat/event-reactions.md) |
| [KI-3](#ki-3-critical--eventstatistics-is-never-written) | Critical ✅ | Statistics / Event History | `SaveEventStatisticAsync` had zero callers — both pages read an always-empty table | [Statistics](../stream-tools/statistics.md), [Event History](../chat-features/event-history.md) |
| [KI-4](#ki-4-critical--browser-source-sounds-never-play) | Critical ✅ | Browser sources / Sounds | Server broadcast PascalCase `AudioEvent` JSON; audio.html only understood `{"type":"play"}` | [Browser Sources](../stream-tools/browser-sources.md), [Sounds](../stream-tools/sounds.md) |
| [KI-6](#ki-6-critical--alert-settings-page-is-a-facade) | Critical ✅ | Visual Alerts | Alert Settings never loaded or saved; real alerts used hard-coded animation/position/duration | [Visual Alerts](../stream-tools/visual-alerts.md) |
| [KI-7](#ki-7-critical--currency-settings-tab-does-not-persist) | Critical ✅ | Currency | Currency Settings "Save" logged success and called no save API | [Currency & Rewards](../stream-tools/currency.md) |
| [KI-8](#ki-8-wiring-gap--twitch-chat-enabled-toggle-is-cosmetic) | Wiring gap ✅ | Chat settings | Twitch had no routing-event publisher, so its chat filter could never turn off | [Chat Settings](../chat/chat-settings.md) |
| [KI-9](#ki-9-wiring-gap--youtube-chat-history-is-never-persisted) | Wiring gap ✅ | Chat history | `DataChatMessageStore` mapped Kick/Twitch events only | [Chat Window](../chat/chat-window.md) |
| [KI-10](#ki-10-wiring-gap--ngrok-token-edits-in-settings-dont-persist) | Wiring gap ✅ | Web server / ngrok | Settings' Save wrote only DB fields; only the wizard saved the token | [API, Web Server & ngrok](../settings/web-server-ngrok.md) |
| [KI-11](#ki-11-wiring-gap--chateventsettingsservice-cache-is-not-profile-aware) | Wiring gap ✅ | Chat settings / Profiles | Cache keyed platform:eventType only; stale rows served across profile switches until restart | [Chat Settings](../chat/chat-settings.md), [Profiles](../getting-started/profiles.md) |
| [KI-13](#ki-13-wiring-gap--ngrok-url-rotation-requires-manual-recovery) | Wiring gap ✅ | ngrok / Kick | On tunnel URL change, Kick webhook subscriptions were not repointed; user had to toggle off/on | [API, Web Server & ngrok](../settings/web-server-ngrok.md), [Kick](../platforms/kick.md) |
| [KI-15](#ki-15-wiring-gap--binding-mismatches) | Wiring gap ✅ | UI bindings | Kick settings `Channel` box bound a nonexistent property; profile-selector padlock bound nonexistent `HasPin` | [Kick](../platforms/kick.md), [Profiles](../getting-started/profiles.md) |
| [KI-17](#ki-17-dead-surface--dead-buttons) | Dead surface ✅ | Navigation | Sidebar Events & Alerts / Browser Sources; Home Discord / Ideas; Settings platform cards were dead buttons | [App Shell](../home/app-shell.md) |
| [KI-19](#ki-19-security--kick-webhook-test-payload-signature-bypass) | Security ✅ | Webhooks | `test-` ids / `"test":true` payloads bypassed RSA signature validation unconditionally | [Kick](../platforms/kick.md), [Web Server](../architecture/web-server.md) |
| [KI-20](#ki-20-security--unsalted-sha256-pin-hashes) | Security ✅ | Profiles | PINs hashed with unsalted SHA256; length validation only existed in dead code | [Profiles](../getting-started/profiles.md) |
| [KI-22](#ki-22-security--wizards-write-windows-user-environment-variables) | Security ✅ | Wizards | Demo/ngrok wizards wrote user env vars; ngrok wizard wrote machine-level ngrok config | [API, Web Server & ngrok](../settings/web-server-ngrok.md) |
| [KI-23](#ki-23-security--machine-wide-webview2-process-kills) | Security ✅ | Startup/exit | Startup and exit killed all `msedgewebview2` processes on the machine, not just its own | [Startup & Lifecycle](../architecture/startup-lifecycle.md) |
| [KI-25](#ki-25-doc-drift--root-markdown-vs-code) | Doc drift ✅ | Docs | README and root docs disagreed with code | [Overview](../index.md) |
| [KI-35](#ki-35-critical--discord-disconnect-crashed-the-app) | Critical ✅ | Discord | Disconnecting/disposing a live Discord gateway client abruptly killed the app | [Discord](../platforms/discord.md) |
| [KI-39](#ki-39-wiring-gap--no-block-attaches-buttons-or-menus-to-messages) | Wiring gap ✅ | Workflows / Discord | button_click/select_menu triggers dispatched, but no block attached buttons/menus to messages | [Automation](../chat-features/automation.md), [Discord](../platforms/discord.md) |
| [KI-43](#ki-43-wiring-gap--typed-slash-command-parameters-are-not-implemented) | Wiring gap ✅ | Workflows / Discord | The purple Options tab held only command constraints; slash commands took a single text `{args}` | [Automation](../chat-features/automation.md), [Discord](../platforms/discord.md) |

---

## Critical

### KI-1 (Critical) — Chat command pipeline is dead

**✅ RESOLVED (2026-08-15)** — a new Core model [ChatMessageReceivedEvent.cs](../../src/VyralBot.Core/Models/ChatMessageReceivedEvent.cs) (Platform, Channel, Username, Message, IsModerator, IsSubscriber, IsVip, IsBroadcaster, MessageId?, Timestamp) is now published fire-and-forget by `TwitchService.OnMessageReceived`, `KickLibChatService.OnChatMessage` (MessageId `"kick:{id}"`), and the `YouTubeChatService` polling loop (`"youtube:{id}"`). `ChatViewModel` consumes it: the full command pipeline (unified commands → currency → giveaway → poll → watchtime) is LIVE, with MessageId dedupe (safe against Kick Pusher + webhook double delivery) and bot-echo skip. Bot replies are still only sent to Twitch/Kick.

*Original finding (pre-fix):* `ChatViewModel.OnChatMessageReceivedAsync` (`ChatViewModel.cs:592`) was the orchestrator for the entire command system — unified [CommandService](../../src/VyralBot.Core/Services/Commands/CommandService.cs), currency, giveaway, poll, and watchtime command handling, watchtime activity recording, and chat-activity currency — subscribed to a `ChatMessageReceivedEvent` that nothing in the solution ever published. Every command in the [Command Catalog](command-catalog.md) was inert; watchtime was never recorded; chat-activity currency never awarded. Verified independently by three review passes.

### KI-2 (Critical) — Twitch EventSub likely never connects

**✅ RESOLVED (2026-08-15)** — both Twitch auth paths (wizard and system-browser flow) now fetch the authenticated user via Helix `GET /users` after token exchange and persist `Platform.UserId` (never clobbering an existing id); the reconnect flow no longer wipes `ClientId` (a latent bug found during the fix). EventSub connects after re-auth.

*Original finding (pre-fix):* `TwitchService.cs:115-123` only connected EventSub when the Twitch `Platform` row had a non-empty `UserId` **and** `ClientId`, and no Twitch flow ever populated `UserId`.

### KI-3 (Critical) — EventStatistics is never written

**✅ RESOLVED (2026-08-15)** — all 13 event handlers now persist `EventStatistic` rows (`EventData` = the handler's variable dictionary as JSON; `WasAutoReplied` accurate). Statistics and Event History have real data. One new limitation: the 6 handlers gated on ShowInChat skip recording when Show-in-chat is off — tracked as [KI-26](#ki-26-wiring-gap--showin-chat-off-skips-eventstatistics-recording).

*Original finding (pre-fix):* `SaveEventStatisticAsync` had zero domain callers, so both [Statistics](../stream-tools/statistics.md) and Event History read a permanently empty table and `WasAutoReplied` could never be set.

### KI-4 (Critical) — Browser-source sounds never play

**✅ RESOLVED (2026-08-15)** — `SoundService` now broadcasts camelCase `{"type":"play","sound":"...","volume":0..1}`; `audio.html` honors `volume`, derives its WebSocket/sounds URLs from `window.location` (port-agnostic), and logs `.play()` rejections. OBS browser-source sounds work.

*Original finding (pre-fix):* the only WebSocket producer serialized the `AudioEvent` class with default PascalCase JSON while [audio.html](../../src/VyralBot.Api/wwwroot/audio.html) only reacted to camelCase `{"type":"play",...}` messages and ignored the volume field.

### KI-5 (Critical) — Giveaway/poll follower and watchtime eligibility always reject

**◐ PARTIALLY RESOLVED (2026-08-15; follower fallback widened 2026-11-07)** — eligibility now passes **real watchtime** (resolved through `WatchtimeService`, all platforms) and a real follower check for **Twitch** (Helix `channels/followers`, 5-minute cache; requires the stored user token to have the `moderator:read:followers` scope — KI-33 — and degrades to `false` otherwise). **Non-Twitch platforms** no longer hard-code `false`: the eligibility lookup falls back to the **recorded viewer status**, and Kick follow webhooks (`KickFollowEvent`) mark a viewer followed — so Followers-only giveaways/polls are enforceable on **Kick** once a follow event has been recorded for that viewer (a viewer who followed before the webhooks were live, or while the app was down, is still rejected until seen). **Remains:** YouTube has no follow concept, so eligibility there stays `false` (KI-31). SubscribersOnly works everywhere.

*Original finding (pre-fix):* the chat-side entry paths passed `false` for `isFollower` and `0` for `watchtimeMinutes`, so any giveaway or poll with **FollowersOnly** or **MinWatchtime > 0** rejected every entry.

### KI-6 (Critical) — Alert Settings page is a facade

**✅ RESOLVED (2026-08-15)** — alert settings are now persisted to the `Configs` key `"AlertSettings"` (JSON: `AlertsEnabled`, `DurationSeconds` 1–15, `Animation` SlideInFromTop/Bottom | FadeIn | Bounce, `Position` 9-grid, and `FollowAlertsEnabled`/`SubscribeAlertsEnabled`/`GiftSubAlertsEnabled`/`RaidAlertsEnabled`/`HostAlertsEnabled`/`CheerAlertsEnabled`). `AlertService` applies the settings on real alerts (`applySettings = true` by default); the page's **Test Alert** previews unsaved selections. The page loads, saves, and resets genuinely.

*Original finding (pre-fix):* `InitializeAsync`/`SaveSettingsAsync` were `// TODO` stubs; Save showed "saved successfully" without persisting anything, and real alerts used hard-coded SlideInFromTop / TopCenter / 5 s.

### KI-7 (Critical) — Currency Settings tab does not persist

**✅ RESOLVED (2026-08-15)** — the Settings tab now persists via `ICurrencyService.SaveSettingsAsync` (with the service's cache refreshed). **New limitation:** the data-layer `SaveCurrencySettingsAsync` persists only a subset of fields (name/symbol, watch-time, chat-activity, follow/sub/giftsub/raid bonuses) — other `CurrencySetting` fields such as role multipliers are still not persisted; tracked as [KI-29](#ki-29-wiring-gap--savecurrencysettingsasync-persists-only-a-field-subset).

*Original finding (pre-fix):* `CurrencyViewModel.SaveSettingsAsync` logged "Settings saved successfully!" and called no save API.

### KI-35 (Critical) — Discord disconnect crashed the app

**✅ RESOLVED (2026-08-16)** — disconnecting a Discord bot abruptly killed the whole app: the teardown path unhooked only dead-code event handlers and then disposed the **live** gateway client while Discord.Net background work and other components' state queries were still in flight. `DiscordService.DisconnectAsync` is now deliberately staged ([DiscordService.cs](../../src/VyralBot.Integrations/Discord/DiscordService.cs):268-345): unhook **all** gateway handlers first (so the manual stop/logout cannot reenter them), then best-effort `StopAsync` + `LogoutAsync`, then *park* the fully-stopped client — it is disposed lazily by the next `ConnectAsync` or by `Dispose`, never while live. A matching `ReconnectAsync` (disconnect-then-connect from the stored token) was added for the per-bot dashboards. Documented on [Discord](../platforms/discord.md).

*Original finding (pre-fix):* stopping or disconnecting the Discord bot (dashboard Disconnect, profile switch, app shutdown paths) could take the process down with an unrecoverable gateway-client exception; the handler unhooking only covered handlers that were no longer attached.

---

## Wiring gaps

### KI-8 (Wiring gap) — Twitch "Chat Enabled" toggle is cosmetic

**✅ RESOLVED (2026-08-15)** — `TwitchChatSettingsViewModel` now persists `Platform.TwitchChatEnabled` and publishes `PlatformChatRoutingChangedEvent`; the toggle live-filters the chat feed with no force-enable side effect.

*Original finding (pre-fix):* Twitch had no routing-event publisher — its toggle wrote nothing, `TwitchFilterEnabled` was never fed from the DB, and Twitch messages could never be filtered out of the feed.

### KI-9 (Wiring gap) — YouTube chat history is never persisted

**✅ RESOLVED (2026-08-15)** — `DataChatMessageStore` maps `YouTubeChatMessageEvent` in both directions, and the Chat Window's Load History now loads YouTube too (its log line counts Kick/Twitch/YouTube). YouTube history works end to end.

*Original finding (pre-fix):* only `KickChatMessageEvent` and `TwitchChatMessageEvent` were mapped into the `ChatMessages` table, so YouTube messages rendered live but never loaded from history.

### KI-10 (Wiring gap) — ngrok token edits in Settings don't persist

**✅ RESOLVED (2026-08-15)** — token edits in the ngrok configuration screen now persist via `NgrokService.SaveAuthTokenAsync`. The dead `WebServerSettings.IsEnabled` flag was removed from the ViewModel (the entity column remains, unread).

*Original finding (pre-fix):* `SaveSettingsAsync` wrote only DB fields; the Authentication Token textbox was display-only, and `IsEnabled` was loaded/saved but read by nothing.

### KI-11 (Wiring gap) — ChatEventSettingsService cache is not profile-aware

**✅ RESOLVED (2026-08-15)** — `ChatEventSettingsService` now subscribes to `ProfileSwitchedEvent` (constructor-injected `IEventBus`) and calls `InvalidateCache()` on every profile switch; the new profile's chat-event settings load immediately.

*Original finding (pre-fix):* the cache was keyed only `"{Platform}:{EventType}"` with no invalidation on profile switch, so the new profile could be served the previous profile's chat-event settings until restart.

### KI-12 (Wiring gap) — Auto-reply templates use simple replace

**✅ RESOLVED (2026-11-07, 1.1.0)** — all `@{...}` tokens were fixed in the seeds (DapperDataService), the Kick defaults, and the Twitch defaults (plain `{username}` etc.); `{raider}` is passed by the Twitch raid handler; the Twitch GiftSub handler got a full auto-reply block; and the former remainder — DB/social variables not resolving — is closed by the KI-28 fix: every event auto-reply now passes through `IVariableReplacementService` after the context-key substitution (`ChatViewModel.BuildAutoReplyTextAsync`, ChatViewModel.cs:4334-4348).

*Original finding (pre-fix):* `string.Replace` over only handler context keys, plus default templates shipped as `@{username}` leaving a stray `@` in sent messages.

### KI-13 (Wiring gap) — ngrok URL rotation requires manual recovery

**✅ RESOLVED (2026-08-15)** — `OnNgrokUrlChanged` now auto-resubscribes Kick webhooks: it loads the Kick `Platform` row, refreshes the OAuth token if expired (a new `KickLibOAuthService` injection into `WebhookCoordinatorService` — DI auto-resolves), then unsubscribes-all and resubscribes to the new URL. On failure the previous manual-heal warning is kept.

*Original finding (pre-fix):* on tunnel URL change the coordinator only logged a warning — it lacked the OAuth token to re-subscribe, so free-plan URL rotation silently dead-ended Kick webhooks until the user toggled **Enable Webhooks** off/on.

### KI-14 (Wiring gap) — Auto-connect silently flips Chat Enabled to true

**Intended behavior (documented, 2026-08-15)** — after review this stays **as designed**: after any successful platform auto-connect at startup, `MainViewModel` force-sets `<Platform>ChatEnabled = true` in the DB when it was false. A connected platform is expected to show in the unified feed by default. Documented on [Chat Settings](../chat/chat-settings.md) and [Startup & Lifecycle](../architecture/startup-lifecycle.md); no longer treated as a defect.

### KI-15 (Wiring gap) — Binding mismatches

**✅ RESOLVED (2026-08-15)** — `KickSettingsView` now binds `ChannelName` (the read-only Channel box populates), and the `Profile` model gained a computed `HasPin` so the profile-selector padlock renders.

*Original finding (pre-fix):* the Kick settings Channel box bound a nonexistent `Channel` property, and the startup padlock bound a nonexistent `Profile.HasPin`.

### KI-16 (Wiring gap) — Audio port pinned to 5000, connected-clients stub

**✅ RESOLVED (2026-11-07, verified in the wave-12 docs pass)** — all three halves are closed. The **"Connected Clients" count is live**: `AudioSettingsViewModel` polls `AudioWebSocketBroadcaster.ConnectedClientCount` every 2 seconds. The **port is no longer pinned**: the audio browser source and `/audio-ws` ride the embedded Kestrel web server, so the page resolves its port from `WebServerSettings` (5000 fallback) and shows it as a read-only mirror labeled "Managed in Web Server settings" — change it in Web Server settings, restart the app, and the audio side follows. **Restart Server is real**: it stops/starts the embedded server through the webhook coordinator, reports success or a port-in-use failure, and refreshes the port mirror + browser-source URL (AudioSettingsViewModel.cs:237-283). The master volume also now auto-persists ~600 ms after the slider settles. Documented on [Audio Settings](../stream-tools/audio.md) and [Browser Sources](../stream-tools/browser-sources.md).

*Original finding (pre-fix):* the port edit was rejected and forced back to 5000 on every init, the restart button was a no-op message box, and the client count was a stub always showing 0.

---

## Dead surface

### KI-17 (Dead surface) — Dead buttons

**✅ RESOLVED (2026-08-15)** — all the navigation dead buttons were wired: sidebar **Events & Alerts** now navigates to Alert Settings; sidebar **Browser Sources** now navigates to Audio Settings (tooltip explains it); the Home **Discord** and **Ideas & Suggestions** support rows now open configurable links — `MainViewModel.OpenDiscordLinkCommand`/`OpenIdeasLinkCommand` read the `Support.DiscordUrl` / `Support.IdeasUrl` Configs keys and open the URL in the system browser, with a MessageBox pointing to Settings → Profile → **Support Links** when unset; and the Settings → Platforms TikTok/Discord/OBS/YouTube cards are disabled with explanatory tooltips (`ShowOnDisabled`) instead of silently doing nothing. Small decorative surfaces are unchanged and remain documented where they live: the chat-header "All Platforms" popup action buttons and the YouTube/TikTok/Discord decorative checkboxes ([Chat Window](../chat/chat-window.md)), plus the "Coming Soon" Settings categories ([Settings Overview](../settings/overview.md)).

*Original finding (pre-fix):* sidebar Events & Alerts / Browser Sources, Home Discord / Ideas, Settings platform cards, chat-header popup actions, and decorative checkboxes all rendered as clickable with no command behind them.

### KI-18 (Dead surface) — Dead and orphaned code

**◐ PARTIALLY RESOLVED (2026-08-15)** — the seeded, enabled `!watchtime` built-in no longer shadows the real service (`CommandService` now defers it — returns null so `WatchtimeCommandService` handles the message; the dead placeholder text is gone); dead env-var methods were removed from the TikTok wizard and the pre-rewrite Discord wizard; the dead `SaveCredentialsToEnvironment` was removed from `TwitchWebSetupViewModel`; and the conditional-rules `GetAutoReplyAsync` path — listed here as a caller-less engine when this entry was written — gained a production caller on 2026-09-06 (the Kick/Twitch auto-reply pipeline consults it; its remaining boundary is tracked as KI-57). **Remains:** `ProfileSelectorWindow`, `DemoSetupView`, the `NgrokSetupView` variants, and the `TriggerEngine`/`ActionDispatcher` stubs are still in the tree (see [UI Reference](ui-reference.md)).

*Original finding (pre-fix):* unreachable pages (`VariablesView` hosted nowhere reachable from the sidebar, `ProfileSelectorWindow`), caller-less engines (the conditional-rules `GetAutoReplyAsync` path, the `TriggerEngine`/`ActionDispatcher` TODO stubs, the `Triggers`/`Actions`/`EventLogs`/`ViewerPerks` tables with no writers), superseded/orphaned files (`NgrokSetupView` + variants, `DemoSetupView`, `App.AutoConnectTwitchAsync`, `InMemoryChatMessageStore`, the legacy port-8080 `AudioWebSocketServer`, `ImportProfileAsync` with no UI caller, `BadgesToImagesConverter`, the orphaned `ChatEventSetting` sound columns), and the `!watchtime` placeholder shadowing — plus `CommandContext.IsBroadcaster` hard-coded `false` and Kick/Twitch-only platform gating.

---

## Security

### KI-19 (Security) — Kick webhook test-payload signature bypass

**✅ RESOLVED (2026-08-15)** — the bypass is now gated behind the `Configs` key `"Kick.AllowTestWebhooks"` (default **false** = fail-closed; the value is cached for 60 s and resolved through the WPF host's `App.ServiceProvider` via reflection). Forged "test" payloads are rejected unless the operator explicitly opts in.

*Original finding (pre-fix):* any payload whose message id started with `test-` or whose body contained `"test":true` skipped RSA-SHA256 validation, allowing forged events from anyone who knew the public endpoint.

### KI-20 (Security) — Unsalted SHA256 PIN hashes

**✅ RESOLVED (2026-08-15)** — PINs are now hashed with PBKDF2-SHA256 (100,000 iterations, 16-byte salt, format `v2.salt.hash`); legacy unsalted hashes still verify via constant-time comparison and are transparently re-hashed to v2 on successful login; all comparisons are constant-time.

*Original finding (pre-fix):* plain SHA256 → base64 in `profile.json`, trivially brute-forceable for 4–6 digit PINs and comparable across profiles.

### KI-21 (Security) — Inconsistent token storage

**◐ PARTIALLY RESOLVED (2026-08-15)** — the demo/ngrok wizards no longer write **user** environment variables (see KI-22). **Remains by design / unchanged:** Kick tokens stay unencrypted in the `Platforms` table (deliberate, per code comments — now documented), and YouTube tokens stay plaintext. Twitch user tokens remain DPAPI-encrypted; client secrets remain plaintext JSON files.

*Original finding (pre-fix):* the inconsistency existed and was undocumented; wizards additionally leaked credentials into user env vars.

### KI-22 (Security) — Wizards write Windows user environment variables

**✅ RESOLVED (2026-08-15)** — the demo wizard's env writes are now **process-scope only** (nothing persists to the user/machine); the ngrok wizard's vestigial env writes were removed entirely. The machine-level `ngrok config add-authtoken` write is intentionally kept (the ngrok agent needs it to run).

*Original finding (pre-fix):* `VYRALBOT_NGROK_CLIENT_ID/SECRET` and dummy `VYRALBOT_*` vars were written as persistent user environment variables — hidden global state outside appdata.

### KI-23 (Security) — Machine-wide WebView2 process kills

**✅ RESOLVED (2026-08-15)** — the startup and exit cleanup is now scoped: a PowerShell `Get-CimInstance` query filters `msedgewebview2` processes whose command line contains `VyralBot_WebView2_Temp`, so only Vyral's own WebView2 processes are killed. Other applications' WebView2 processes are untouched.

*Original finding (pre-fix):* startup and exit killed every `msedgewebview2` process on the machine and deleted the WebView2 temp cache machine-wide.

### KI-24 (Security) — Robustness notes

**◐ PARTIALLY RESOLVED (2026-08-15)** — the TikTok wizard now saves `Status="Coming Soon"` (with `TokenExpiresAt` null), so nothing treats it as connected. **TikTok honesty completed (2026-11-07, marathon wave 2):** every remaining TikTok surface now tells the truth — the Platforms card never opens its dead-end wizard (the click says "TikTok integration is coming soon — chat is not available yet"), and the TikTok quick-settings card sits behind a coming-soon gate; an audit confirmed there is no TikTok chat pipeline anywhere in the codebase. **Discord superseded (2026-08-15):** the Discord half of this entry is gone entirely — the stub wizard was replaced by a real bot-token wizard backed by [DiscordService](../../src/VyralBot.Integrations/Discord/DiscordService.cs) (see [Discord](../platforms/discord.md)); the fake Connected row, the IRC-pointed-at-discord.com test message, and the Twitch copy/paste log remnants no longer exist. Discord's current limitations are tracked separately as [KI-34](#ki-34-wiring-gap--discord-integration-limitations). **Remains:** echo suppression is still time-window based, but it is now restricted to messages from the bot's own sending accounts — viewers typing identical text are no longer filtered. **Unchanged:** the EventBus DropOldest loss under burst and the single-instance mutex remain as documented design trade-offs.

*Original finding (pre-fix):* stub wizards wrote `Status="Connected"` rows with empty tokens, and content-based echo suppression ate duplicate viewer text.

---

## Doc drift

### KI-25 (Doc drift) — Root markdown vs code

**✅ RESOLVED (2026-08-15)** — `README.md` at the repo root was rewritten to reflect reality: features that are implemented and working (multi-platform chat Twitch/Kick/YouTube, the now-live commands+currency+giveaways+polls+watchtime pipeline, alerts/sounds with real persisted settings, profiles, webhooks+ngrok, the in-app AppDocs browser), TikTok/Discord marked as planned, .NET 10 (not 8), and no more "Planned (Future)" claims for implemented features. `AppDocs/` is called out as the documentation. Other root planning documents remain historical; `AppDocs/` stays authoritative.

*Original finding (pre-fix):* README listed implemented features as "planned" and described a .NET 8 / EF Core / scripting-plugin architecture the code never had.

---

## Newly-documented limitations (2026-08-15 fix pass)

### KI-26 (Wiring gap) — ShowIn-chat-off skips EventStatistics recording

Six of the 13 event handlers are gated on `ShowInChat == true` for their whole reaction chain (Kick GiftSub / Host / KicksGifted / RewardRedeemed / StreamStatus, plus Twitch GiftSub). With "Show in chat" off, those handlers return before the new `SaveEventStatisticAsync` call, so those event types leave no statistics/history rows. The other seven handlers record regardless of ShowInChat. Affects [Event History](../chat-features/event-history.md) and [Statistics](../stream-tools/statistics.md).

### KI-27 (Wiring gap) — First-chat bonus means "first currency record"

`AwardFirstChatBonusAsync` treats "has this user ever had a currency row" as "first chat". A viewer who earned currency through watchtime, an event bonus, or a mod `!give` before their first message will never receive the first-chat bonus; conversely the bonus fires on the first *earning* event of any kind that routes through the check. Documented on [Currency & Rewards](../stream-tools/currency.md).

### KI-28 (Wiring gap) — Auto-reply templates still do not resolve DB or social variables

**✅ RESOLVED (2026-11-07, 1.1.0 — verified in the wave-12 docs pass)** — the remaining half of KI-12 is closed. `AutoReplyService.ProcessTemplate` is still the plain case-insensitive `string.Replace` over handler-supplied keys (the first pass), but every event auto-reply is now built through `ChatViewModel.BuildAutoReplyTextAsync`, which runs the result through the full `IVariableReplacementService` engine (ChatViewModel.cs:4334-4348) — the same engine command responses use. `{twitter}`, `{twitch-followercount}`, and the rest of the DB/social catalog resolve in auto-replies; keys neither pass knows are still left verbatim. Applies to all 13 Kick/Twitch event handlers and the Discord join/boost rule paths. Details: [Auto-Replies](../chat-features/auto-replies.md), [Variables Catalog](variables-catalog.md).

### KI-29 (Wiring gap) — SaveCurrencySettingsAsync persists only a field subset

**✅ RESOLVED** — `DapperDataService.SaveCurrencySettingsAsync` now writes the **full** `CurrencySetting` row (role multipliers, cheer/host bonuses, decay, bank fields including the withdrawal cooldown, and transfer tax included), and the Currency page gained a **Bank & Decay** tab that edits the bank/decay fields in the UI. The withdrawal cooldown is also now **enforced** (rejections quote the remaining wait; the window is durable across restarts). Documented on [Currency & Rewards](../stream-tools/currency.md).

### KI-30 (Behavior change, documented) — Watchtime earning rate is now configurable, defaults to 10/minute

Watchtime currency earning previously hard-coded 1 point per tracked minute. It now goes through the configured `WatchTimeEnabled` / `WatchTimeAmount` / `WatchTimeInterval` settings and the full multiplier stack. **BEHAVIOR CHANGE:** the seeded defaults (`WatchTimeAmount = 10`, interval 60 s) mean new and existing databases award **10 per minute** where they used to award 1. Streamers who want the old rate should set Points Per Interval to 1 on the Currency Settings tab. Documented on [Watchtime](../stream-tools/watchtime.md) and [Currency & Rewards](../stream-tools/currency.md).

### KI-31 (Wiring gap ◐) — Kick / YouTube follower eligibility

**◐ PARTIALLY RESOLVED (2026-11-07)** — the eligibility lookup for non-Twitch platforms now falls back to the **recorded viewer status** (`IsFollowerForEligibilityAsync`, ChatViewModel.cs:4127-4151) instead of hard-coding `false`. Kick follow webhooks (`KickFollowEvent`) record `IsFollower = true` on a viewer's status row, so a **Kick** follower who has been seen by the webhooks passes the Followers-only gate on giveaways and polls. **Remains:** the recorded status only exists after a follow event was processed — a viewer who followed before the webhooks were configured (or while the app was down) is still rejected until one arrives — and **YouTube** has no follow concept at all, so eligibility there stays `false` (Twitch is a real-time Helix check — see KI-33 for its scope requirement). Documented on [Giveaways](../stream-tools/giveaways.md) and [Polls](../stream-tools/polls.md).

### KI-32 (Wiring gap, minor) — Audio settings client-count timer only stops at shutdown

`AudioSettingsViewModel` is a DI singleton, so the 2-second polling timer behind the live connected-clients count (KI-16 fix) is created once and disposed only at process shutdown. The cost is negligible, but the timer runs even while the Audio Settings page is not visible. Documented on [Audio Settings](../stream-tools/audio.md).

### KI-33 (Wiring gap) — Twitch follower check requires the moderator:read:followers scope

The real Twitch follower eligibility check (Helix `channels/followers`, 5-minute cache) requires the **stored user token** to carry the `moderator:read:followers` scope. Tokens saved before the fix pass may lack it; in that case the check degrades to `false` (Followers-only giveaways/polls reject everyone until the broadcaster re-authorizes). The app's Twitch authorization flow requests the scope for new connections. Documented on [Twitch](../platforms/twitch.md) and [Giveaways](../stream-tools/giveaways.md).

### KI-34 (Wiring gap) — Discord integration limitations

The Discord integration (added 2026-08-15 — see [Discord](../platforms/discord.md)) is chat-complete but deliberately narrow. All of these are current-state boundaries, not breakage:

- **One active channel at a time** — **superseded per-bot (2026-08-16)**: with multi-bot support each bot now has its own `SelectedChannelId` (the `DiscordBots` row), so two bots can talk in two different channels. Per *single* bot the boundary stands: it listens to every text channel of every guild (and DMs) but sends only to its one selected channel. Guild-wide listening with per-guild reply routing is still future work.
- **Message Content intent is mandatory** — the bot token's application must have the MESSAGE CONTENT privileged gateway intent enabled in the Discord Developer Portal (the wizard's step 2 instructs this; Quick Add on the bots page does not, so remember it for manually added bots). Without it Discord delivers no message content, so no messages or commands arrive.
- **No Discord-native slash commands** — the app's `!`/`?`/`.` prefix commands work on Discord; no `/`-commands are registered by the command system. (Workflow flows with a Slash Command trigger do register real `/` commands — see [Automation](../chat-features/automation.md); KI-40 tracks the online requirement.)
- **Non-chat Discord events: joins/boosts live (intent-gated), others absent** — member joins and server boosts are fully wired (sound, alert, statistics, chat line and auto-reply with per-event editors since the 1.1.0 ChatHub parity pass), but they stay dormant until the Server Members intent is enabled (KI-52); bans and other guild events are not ingested at all.
- **History rows are written but not reloaded** — Discord messages persist to the `ChatMessages` table via `DataChatMessageStore`, but the Chat Window's Load Chat History button merges only Kick/Twitch/YouTube rows back into the feed.

Documented on [Discord](../platforms/discord.md) (Status & Known Issues) and [Chat Window](../chat/chat-window.md).

---

## Newly-documented limitations (2026-08-16 multi-bot pass)

### KI-36 (Wiring gap) — Discord multi-bot transition loose ends

The multi-bot feature (2026-08-16) moved per-bot truth into the `DiscordBots` table, but three edges still point at the legacy single-bot world:

- **The full wizard configures the setup bot only** — the four-step wizard validates and stores its token on the legacy `Platform` row and drives the DI-singleton `DiscordService`; it does not create or edit `DiscordBots` rows. Adding a *second* bot means **Quick Add** on the bots page (name + token, no invite-link derivation or validation preview). A per-bot wizard is pending.
- **Per-bot RECONNECT reads the shared legacy token** — the dashboard's RECONNECT calls `DiscordService.ReconnectAsync`, which loads its token from the legacy `Platform` row, not from the bot's own `DiscordBots` row. For any bot whose token differs from the legacy row's (normally the migrated Primary or the wizard's bot), RECONNECT reconnects with the *wrong* token. STOP + START reads the correct per-bot token and is the reliable restart path.
- **The legacy single-bot dashboard view is reachable again** — the Discord Bots page header gained a **DASHBOARD** toggle that lazily hosts `DiscordDashboardView` (from DI) as a tab showing the setup bot's servers, member counts, channel drop-downs, active-channel picker, and test message. The per-bot `DiscordBotDetailView` remains the per-bot manager; the old "bound nowhere" orphan state no longer applies.

Documented on [Discord](../platforms/discord.md), [Platform Dashboards](../platforms/dashboards.md), and [UI Reference](ui-reference.md).

### KI-37 (Wiring gap) — !commands list ignores Discord bot scope

`CommandService.ExecuteCommandsListAsync` filters the `!commands` listing by platform flags and permission only — it does not consider `Commands.BotId`. A viewer asking one bot can be told about a command pinned to a different bot (which that bot would then not answer). Execution-time filtering works correctly; only the advertisement list is unscoped. Documented on [Commands](../chat-features/commands.md) and [Command Catalog](command-catalog.md).

### KI-38 (Wiring gap) — Discord origin race can fall back to Primary routing

`DiscordService` publishes the typed `DiscordChatMessageEvent` (which carries `BotId`) and the generic `ChatMessageReceivedEvent` (which does not) as two concurrent fire-and-forget tasks. `ChatViewModel` records message-id→bot-id in the typed handler and recovers the origin in the generic handler — but if the generic event wins the race (or the message id was evicted from the bounded map), the origin resolves to null and the reply routes through **Primary** (first connected bot, else the setup bot) instead of the receiving bot. The message still gets answered, and commands scoped to the actual origin bot can be skipped in that window — a safe degradation, not a crash or a lost reply. Documented on [Discord](../platforms/discord.md) and [Event Bus](../architecture/event-bus.md).

---

## Newly-documented limitations (2026-08-29 visual command editor pass)

### KI-39 (Wiring gap) — No block attaches buttons or menus to messages

**✅ RESOLVED (2026-08-29)** — **Send Message** and **Send Embed** blocks gained a **Buttons & Menus** section (up to 5 buttons; one select menu with up to 25 options). Wired sub-chains hanging off the component chips' sub-node cards materialize on SAVE as component child workflows keyed `vyralwf:{parentWorkflowId}:{nodeId}:btn{i}` / `:menu`, and a click dispatches by that key straight to the child flow. Documented on [Automation](../chat-features/automation.md) and [Discord](../platforms/discord.md).

*Original finding (pre-fix):* the visual flow editor's **Button click** and **Select menu** triggers were wired end to end — Discord interaction dispatch and workflow execution both worked — but no block *attached* a button or select menu to a message, so those triggers could only fire from interaction components already addressed as `vyralwf:{workflowId}`.

### KI-40 (Wiring gap) — Slash command registration requires the bot online

Flows with a Slash Command trigger register their real Discord `/` commands at save time and on reconnect — both of which only reach Discord while the bot is connected. A flow saved while the bot is offline registers later, on the next save or the next reconnect. Documented on [Automation](../chat-features/automation.md) and [Discord](../platforms/discord.md).

---

## Newly-documented limitations (2026-08-29 command-adoption pass)

### KI-41 (Wiring gap) — Converted moderation flows use fixed numbers, not {args}

Converting a Discord moderation command (edit on the Commands page or the bot detail Commands card) seeds a **🔨 Moderate Member** block whose number parameters are the block's fixed defaults — **10 timeout minutes / 10 slowmode seconds / 10 purge messages**. The block's numeric fields can't take `{args}`, so `!mute @user 30` (30 minutes from the arguments) does not carry over: until edited by hand — or until the Moderate block grows an args-parsing option — every converted timeout/slowmode/purge flow uses its fixed default. Kick/ban/unban targets are fine (they take `{args}` as text). Documented on [Commands](../chat-features/commands.md) and [Automation](../chat-features/automation.md).

### KI-42 (Wiring gap) — Random replies convert to one Switch case per reply

Converting a custom command with multiple random replies seeds a **Random Number → Switch** fan-out with exactly one **Send Message** branch per stored reply. Replies are not re-read at run time, so adding a 4th reply later means editing the flow by hand: raise the Random Number max, add the comma-separated case to the Switch, and wire a new Send Message node to it. Documented on [Commands](../chat-features/commands.md).

---

## Newly-documented limitations (2026-08-29 BotGhost restyle pass)

### KI-43 (Wiring gap) — Typed slash-command parameters are not implemented

**✅ RESOLVED (2026-08-29)** — the purple OPTIONS tab grew a **COMMAND PARAMETERS** group: typed parameter definitions (Text, Number, User, Channel, Role, Choice→Boolean, Attachment, Music Smart Search) with a Name/Type/Description/Required editor (the original violet chips row later became the purple parameter cards on the canvas and the right panel's Parameter editor — see [Automation](../chat-features/automation.md)). For Slash Command triggers they register as real typed Discord options (cap 25) and their values arrive as `{parameterName}` variables. The remaining edge — prefix commands still run on `{args}` alone — is tracked as [KI-44](#ki-44-wiring-gap--prefix-commands-bypass-typed-command-parameters). Documented on [Automation](../chat-features/automation.md) and [Discord](../platforms/discord.md).

*Original finding (pre-fix):* the flow editor's restyle copied BotGhost's command builder look, but BotGhost's purple **Options** tab means something different: its purple blocks (Text, Number, User, Channel, Role, Choice, Attachment) are **typed slash-command argument definitions** that feed per-parameter variables (`{username}`, `{number}`, …). Vyral's purple OPTIONS tab listed the six **command constraints** instead — Cooldown, Require Moderator, Channel Restriction, DM Only, Guild Only, NSFW Flag — and slash commands took a single text `{args}`.

---

## Newly-documented limitations (2026-08-29 flow-editor feature wave)

### KI-44 (Wiring gap) — Prefix commands bypass typed command parameters

The typed COMMAND PARAMETERS (KI-43's resolution) are Discord slash-command options by nature: they register with the Slash Command trigger and nowhere else. A **Prefix command** flow (`!mycmd`) still receives everything as the single `{args}` text variable — its authored parameters register no options and inject no `{parameterName}` variables, so `{price}` in a Send Message stays literal when the flow is invoked by prefix. Auto-conversion seeds prefix-command flows, so commands converted from the Commands page are the common hit. Documented on [Automation](../chat-features/automation.md) and [Discord](../platforms/discord.md).

### KI-45 (Wiring gap) — BotGhost music/voice/premium blocks are not implemented

The block catalog stops at Vyral's own engine surface. BotGhost's **Create Music Player** suite, voice-channel actions, IFTTT, message **transcripts**, **Components V2**, and **Premium Check** blocks have no Vyral counterparts — and Vyral's own music/voice features are not exposed as flow blocks either. The palette's typed **Music Smart Search** parameter is honest about this: it creates a plain **text** parameter named `search` (no player, no track selection). Documented on [Automation](../chat-features/automation.md) and [Discord](../platforms/discord.md).

---

## Newly-documented limitations (2026-08-29 side-rail pass)

### KI-46 (Wiring gap) — Component button ids are positional and renumber on reorder

A message's buttons are addressed `vyralwf:{parentWorkflowId}:{nodeId}:btn{i}` — the trailing index is the button's **position** in the Buttons & Menus settings (btn0 = first). Add, remove, or **reorder** buttons and every later button shifts down: `btn1` becomes `btn0`, its component child workflow gets the new key on the next save, and `{buttonId}` (the id tail a click seeds, e.g. `…:btn0`) changes with it. Any **Comparison Condition** branching on `{buttonId}` therefore points at the wrong button after a reorder until it is re-edited to the new index. Select menus are immune (the single `…:menu` key has no index). Documented on [Automation](../chat-features/automation.md) and [Discord](../platforms/discord.md).

---

## Cross-reference index

Which ledger entries affect which feature areas (✅ = resolved, ◐ = partially resolved):

| Feature | Entries |
|---|---|
| [Quick Start](../getting-started/quick-start.md) | none (router page — defers to the pages it links) |
| [Commands](../chat-features/commands.md) | KI-1 ✅, KI-37, KI-38, KI-41, KI-42, KI-61 (import translation scope) |
| [Command Catalog](command-catalog.md) | KI-1 ✅, KI-33 (scope caveat), KI-58, KI-59, KI-62 (auto-revert accuracy), KI-64 (warn strikes in-memory) |
| [Vyral Goals](../stream-tools/vyral-goals.md) | KI-63 (overlay cadence + completions/target semantics) |
| [Marketplace & Sharecodes](../chat-features/marketplace.md) | KI-45 (imported flows use the same block set) |
| [Variables](../chat-features/variables.md) | KI-12 ✅, KI-28 ✅ |
| [Auto-Replies](../chat-features/auto-replies.md) | KI-1 ✅, KI-12 ✅, KI-28 ✅, KI-49 ✅, KI-57 ✅ |
| [Conditional Rules](../chat-features/conditional-rules.md) | KI-18 ◐ (rules path live since 2026-09-06), KI-57 ✅ |
| [Event History](../chat-features/event-history.md) | KI-3 ✅, KI-26 |
| [Event Testing](../chat-features/event-testing.md) | none (fully functional) |
| [Automation](../chat-features/automation.md) | KI-39 ✅, KI-40, KI-41, KI-43 ✅, KI-44, KI-45, KI-46, KI-60 |
| [Workflow Engine](../chat-features/workflows.md) | KI-60 (Streaming blocks need OBS/Twitch backends), KI-61 (`firstmessage` detection semantics) |
| [Giveaways](../stream-tools/giveaways.md) | KI-1 ✅, KI-5 ◐, KI-31, KI-33 |
| [Polls](../stream-tools/polls.md) | KI-1 ✅, KI-5 ◐, KI-31, KI-33, KI-63 (overlay cadence) |
| [Watchtime](../stream-tools/watchtime.md) | KI-1 ✅, KI-30 |
| [Currency & Rewards](../stream-tools/currency.md) | KI-1 ✅, KI-7 ✅, KI-29 ✅ (full-row save + Bank & Decay tab), KI-27, KI-30 |
| [Statistics](../stream-tools/statistics.md) | KI-3 ✅, KI-26, KI-54 ✅, KI-64 (session chat counts in-memory) |
| [Browser Sources](../stream-tools/browser-sources.md) | KI-4 ✅, KI-16 ✅, KI-63 (widget cadences + goals semantics) |
| [Sounds](../stream-tools/sounds.md) | KI-4 ✅, KI-50, KI-55 ✅ |
| [Audio Settings](../stream-tools/audio.md) | KI-16 ✅ (port + restart), KI-32, KI-50 |
| [Visual Alerts](../stream-tools/visual-alerts.md) | KI-6 ✅, KI-48 ✅ |
| [Chat Window](../chat/chat-window.md) | KI-9 ✅, KI-24 ◐, KI-34, KI-38 |
| [Chat Settings](../chat/chat-settings.md) | KI-8 ✅, KI-11 ✅, KI-14 (intended), KI-49 ✅ |
| [Event Reactions](../chat/event-reactions.md) | KI-2 ✅, KI-26, KI-61 (first-chatter detection semantics) |
| [Twitch](../platforms/twitch.md) | KI-2 ✅, KI-21 ◐, KI-33, KI-51 |
| [Kick](../platforms/kick.md) | KI-13 ✅, KI-19 ✅, KI-21 ◐, KI-31, KI-53 ◐ |
| [YouTube](../platforms/youtube.md) | KI-9 ✅, KI-21 ◐, KI-31, KI-47, KI-53 ◐ |
| [Discord](../platforms/discord.md) | KI-24 ◐ (Discord half superseded), KI-34 ◐, KI-35 ✅, KI-36, KI-38, KI-39 ✅, KI-40, KI-41, KI-43 ✅, KI-44, KI-45, KI-46, KI-52, KI-53 |
| [Platform Dashboards](../platforms/dashboards.md) | KI-36 (per-bot RECONNECT token; dashboard-tab half resolved) |
| [Multi-Platform Parity](../platforms/multi-platform.md) | KI-31, KI-52, KI-53 |
| [TikTok & OBS](../platforms/tiktok-discord-obs.md) | KI-17 ✅, KI-24 ◐, KI-56 |
| [TikTok](../platforms/tiktok.md) | KI-24 ◐ (all surfaces honest coming-soon), KI-56 |
| [Profiles](../getting-started/profiles.md) | KI-11 ✅, KI-15 ✅, KI-20 ✅ |
| [API, Web Server & ngrok](../settings/web-server-ngrok.md) | KI-10 ✅, KI-13 ✅, KI-22 ✅ |
| [App Shell](../home/app-shell.md) | KI-17 ✅ |
| [Event Bus](../architecture/event-bus.md) | KI-1 ✅, KI-24 ◐, KI-38 |
| [Web Server](../architecture/web-server.md) | KI-19 ✅ |
| [Startup & Lifecycle](../architecture/startup-lifecycle.md) | KI-23 ✅ |
| [Data Layer](../architecture/data-layer.md) | KI-3 ✅, KI-18 ◐ |
| [Moderator Guide](../architecture/moderator-guide.md) | KI-51, KI-53 (auto-mod platform parity), KI-64 (warn strikes in-memory) |

---

## Newly-documented limitations (2026-09-01 YouTube re-auth pass)

### KI-47 (Wiring gap) — Google refresh tokens expire in Testing mode; YouTube needs periodic re-authorization

While the app's Google OAuth consent screen is in **Testing** status, Google expires its refresh tokens after **7 days**, so YouTube auto-connect fails with `invalid_grant` roughly weekly (revoking access in your Google account or changing the password does the same). The engine can't prevent this — only a fresh sign-in fixes it. As of this pass the failure is handled honestly: a failed auto-connect marks the saved platform row **Disconnected**, the Platforms page shows **"Re-authorization required"** (the live chat service — not the database row — is the source of truth, and a row that claims Connected while the service is down triggers one silent stored-credential reconnect first), and the fix is re-running **Home → Connections → YouTube**. Publishing the Google OAuth app removes the 7-day expiry. Documented on [YouTube](../platforms/youtube.md).

---

## Fixes and newly-documented limitations (2026-11-07 marathon reconciliation)

Everything below was verified against the code during the 24-hour production marathon (waves 0–6). The two resolved entries predate the marathon (fixed in the 2026-10-18 full-app audit pass, commit `7a341d8`) but had never been entered in this ledger; the four limitations are honest current-state boundaries surfaced by the marathon's parity and moderation work.

### KI-48 (Critical) — Alert queue stalled after the first alert

**✅ RESOLVED (2026-10-18)** — two defects stacked: the alert overlay's auto-close called `Hide()` instead of `Close()`, so `AlertService` never learned a slot had freed; and there was no queue at all — a new alert just replaced the visible one. Net effect in real streams: after the first alert in a session, subsequent alerts could stall forever. Now: auto-close genuinely **closes** the window, and alerts flow through a **bounded FIFO** — one on screen, at most **10** pending (`MaxPendingAlerts`, drop-oldest so the queue can never grow), drained window-close by window-close (`OnAlertWindowClosed`, [AlertService.cs](../../src/VyralBot.UI/Services/AlertService.cs)). Documented on [Visual Alerts](../stream-tools/visual-alerts.md).

### KI-49 (Wiring gap) — Auto-Replies master toggle was dead on 4 platforms

**✅ RESOLVED (2026-10-18)** — the **Auto-Replies** master toggle in the Chat Hub's per-platform settings loaded and persisted nothing on **Kick, YouTube, TikTok, and Discord** (Twitch worked). The four settings VMs now load and persist their `Chat.<Platform>.AutoRepliesEnabled` config keys (Kick keeps its legacy `Kick.AutoRepliesEnabled` key), `ChatViewModel` consults the Kick flag live before every auto-reply send, and the Twitch toggle publishes a routing event so the engine re-reads it without restart. Note the wider boundary this entry originally recorded — per-event auto-reply *editors* Twitch/Kick-only — was itself closed by the 1.1.0 ChatHub parity pass: YouTube (Membership, Super Chat) and Discord (join, boost) gained per-event editors and live auto-reply pipelines (TikTok still has none; no chat integration). Documented on [Chat Settings](../chat/chat-settings.md).

### KI-50 (Limitation) — No output device selection for sounds

Local sound playback (WPF `MediaPlayer`, one short-lived STA instance per playback) always goes to the **system default output device**. There is no device picker on the Sounds or Audio Settings pages and no routing of the local streamer feed to a specific device (e.g. headphones) separate from the OBS browser-source feed — the browser source itself plays in OBS and follows OBS's audio settings, but the streamer-side playback cannot be aimed. Documented on [Sounds](../stream-tools/sounds.md) and [Audio Settings](../stream-tools/audio.md).

### KI-51 (Limitation) — Twitch Delete-message verdicts degrade to a purge-timeout

The auto-moderator's **Delete** verdict on Twitch needs the offending message's id, but the chat pipeline does not parse IRC message ids, so `TwitchModerationAdapter` degrades a Delete to a **1-second timeout** — a timeout purges the sender's recent messages, which still removes the offending line. Functionally the line disappears; the side effect is the viewer is also muted for one second and the action logs as a timeout, not a delete. Twitch ban/timeout verdicts are enforced natively via Helix. Documented on [Twitch](../platforms/twitch.md) and the [Moderator Guide](../architecture/moderator-guide.md#auto-mod-platform-parity).

### KI-52 (Limitation) — Discord join/boost events need the Server Members Intent portal toggle

`DiscordMemberJoinedEvent` / `DiscordBoostEvent` and their reactions + `discordmemberjoined` / `discordboost` workflow triggers are wired end to end, but the gateway deliberately does **not** request the privileged **GuildMembers** intent: `GUILD_MEMBER_ADD` / `GUILD_MEMBER_UPDATE` only arrive with it, and IDENTIFYing an intent that is not toggled in the Discord Developer Portal ("Server Members Intent") is rejected with close code **4014 — which would drop all Discord connectivity for every bot**. Turning the events on therefore requires the portal toggle **and** the code-side intent flag; until then the feature is dormant by design, not broken. The per-bot **Welcome & Auto-Role** card (added 2026-11-07, wave 6 — message + role grant on join, settings persisted per bot under `DiscordBot.{botId}.WelcomeSettings`) is the first user-facing consumer of the join event, so it rides this gate too: the card configures and saves fine, but joins never arrive until the intent is enabled. Documented on [Discord](../platforms/discord.md#member-joins--boosts-intent-gated) and [Discord → Welcome & Auto-Role](../platforms/discord.md#welcome--auto-role).

### KI-53 (◐ partially resolved) — Auto-mod enforcement per-platform degradation

The cross-platform moderation engine runs everywhere, but enforcement degrades per platform (the Moderator dashboard renders this parity live): **Twitch** — full native enforcement (Helix timeout/ban; delete degrades per KI-51); **Discord** — ban and timeout native, but no delete-by-id/by-author API is exposed, so a Delete verdict degrades to a warning notice; **Kick** — ◐ upgraded to full native enforcement (Wave 9, 2026-11-07: Kick API v1 ban/timeout with the `moderation:ban` scope + message delete with `moderation:chat_message:manage`; Kick tokens granted before Wave 9 must re-run the Kick setup once or deletes 403 and degrade); **YouTube** — ◐ upgraded to delete-native (Wave 9: `liveChatMessages.delete`; the public Data API has no chat ban/timeout endpoint — `liveChatModerators.insert` appoints moderators, it does not ban — so Ban/Timeout verdicts degrade to a delete, then a warn notice); **TikTok** — no adapter at all (verdicts are logged only). All verdicts are logged and audited regardless of level. Documented in the [Moderator Guide](../architecture/moderator-guide.md#auto-mod-platform-parity) and [Multi-Platform Parity](../platforms/multi-platform.md).

---

## Newly-documented limitations (2026-11-07 release-wave pass)

Known-and-shipping boundaries confirmed against the code during the wave-9 documentation release pass — each was already documented on its feature page and now has a ledger entry so the cross-reference index is complete.

### KI-54 (Limitation) — Statistics has no charts; everything renders as DataGrids

**✅ RESOLVED (2026-11-07, 1.1.0 — wave 12)** — the Statistics page gained two bar charts backed by a new dependency-free `SimpleBarChart` control ([Controls/Charts/SimpleBarChart.cs](../../src/VyralBot.UI/Controls/Charts/SimpleBarChart.cs), themed in Themes/Generic.xaml): an **overview chart** ("Events by type (selected range)", top 8 event types, cross-platform sums, each bar colored by its dominant platform via TwitchBrush/KickBrush/YouTubeBrush/DiscordBrush/TikTokBrush DataTriggers) above the Event Counts grid, and a **top-contributors chart** (top 8 users for the selected event type, bars colored by the user's platform) on the Top Contributors tab. Aggregation is pure, testable `StatisticsChartBuilder` static code over the already-loaded collections. The per-day trend API `GetEventCountsByDateAsync` still has no UI consumer — a time-axis chart remains future work. Documented on [Statistics](../stream-tools/statistics.md).

### KI-55 (✅ resolved) — No global-hotkey soundboard

**✅ RESOLVED (2026-11-07, 1.1.0 — wave 12)** — the soundboard landed. `GlobalHotkeyService` ([Services/Hotkey/GlobalHotkeyService.cs](../../src/VyralBot.UI/Services/Hotkey/GlobalHotkeyService.cs), DI singleton) registers Win32 `RegisterHotKey` slots on the main window's HWND and dispatches WM_HOTKEY to playback, so a combo fires a library sound **while any app has focus — including fullscreen games**. The Sounds page's library rows grew a **Hotkey column**: click **Set Hotkey**, press a modifier+key combo (Ctrl/Alt/Shift/Win + one key, Esc cancels), and the gesture shows on the row with a clear button. Limits enforced in UI and service: **10 slots max**, one gesture per sound / one sound per gesture (conflicts are rejected and named), MOD_NOREPEAT so holding the key does not auto-repeat, and a page banner warns about OS-global collisions (Alt+Tab, Win+G, …). Assignments persist as the `Sounds.Hotkeys` config key (JSON sound→gesture map), are restored on startup and every profile switch, and are released when a sound is deleted. Playback rides the normal pipeline (master volume, Streamer/Stream output toggles). Together with the wave-10 `play_sound` workflow block, every on-demand path now exists: hotkey, workflow, page Test buttons, and [Event Testing](../chat-features/event-testing.md). Documented on [Sounds](../stream-tools/sounds.md).

*Earlier progress (wave 10):* the **Play Sound** block (`play_sound`) had already added command/timer/event-triggered playback — the half-step this entry recorded as partially resolved.

### KI-56 (Limitation) — TikTok has no chat integration (identity only)

TikTok is a first-class platform *identity* (account linking, the multi-platform bonus, Vyral Goals, giveaway platform lists) but has **no live chat integration**: the setup wizard is a cosmetic stub whose finish writes an honest `Status="Coming Soon"` row, no TikTok chat service exists to produce messages or events, and nothing can be sent to TikTok. Consequences: TikTok viewers cannot generate or redeem link codes, no commands/auto-replies/moderation run there, and auto-mod has no adapter (KI-53's "None" row). Documented on [TikTok](../platforms/tiktok.md) and [TikTok & OBS](../platforms/tiktok-discord-obs.md).

---

## Newly-documented limitations (2026-11-07 wave-10/11 pass)

### KI-57 (Wiring gap) — Conditional rules fire only behind the default-message gate; Discord rules have no pipeline

**✅ RESOLVED (2026-11-07, 1.1.0)** — both boundaries are closed, verified against `ChatViewModel` in the wave-12 docs pass:

- **Rules are un-gated from the default message** — every Kick/Twitch event handler now builds its reply through `BuildAutoReplyTextAsync` (ChatViewModel.cs:4315), which passes an **empty template** when the event's auto-reply is disabled or its default message is empty. The first matching conditional rule wins; the reply is non-empty only when a rule matched (or a default existed), and callers use that as the send gate — so rules fire standalone, with no default message configured.
- **Discord evaluates rules** — the Discord member-join and boost handlers call the same builder with platform `Discord` ("Follow" for joins, "Subscribe" for boosts, ChatViewModel.cs:7165/7216), so Discord-specific and `All` rules finally see Discord events (joins still ride the Server Members intent gate, KI-52).

Wave 10 also hardened the page itself (2026-11-07): saves validate name/event-type/reply and reject unparseable condition JSON, Test Rule genuinely evaluates against a sample context, and the event-type list was reconciled with the live vocabulary (dead types like `ChatMessage`/`Donation` removed). The builder additionally runs the matched reply through `IVariableReplacementService`, so DB/social variables resolve in event auto-replies too. Documented on [Conditional Rules](../chat-features/conditional-rules.md).

---

## Newly-documented limitations (2026-11-07 wave-14 pass)

Verified against `StreamToolsCommandService.cs`, `BlockCatalog.cs`/`StreamControlWorkflowBridge.cs` and the chat pipeline in `ChatViewModel.cs` while documenting the wave-14 features.

### KI-58 (Limitation) — Stream-tools command degradations

Honest current-state boundaries of the 1.1.0 mod & stream tools command set (all deliberate degradations over `ITwitchService`'s existing API surface, not breakage):

- **`!so` has no target-channel info** — `ITwitchService.GetChannelInfoAsync` reads only the broadcaster's **own** channel; there is no target-capable variant, so the shoutout cannot say what the target "was last playing". It degrades to the platform's channel URL plus generic hype text (twitch.tv/{user}, kick.com/{user}, youtube.com/@{user}, tiktok.com/@{user}); **Discord** has no channel-URL concept and gets plain hype ("Go check out {user} — they're awesome!").
- **`!game` name resolution is first-match over the top-games search** — the set path resolves the typed name through `GetTopGamesAsync(query)`'s "id:name;..." pairs and PATCHes with the **first** entry; an exact-name lookup only (no fuzzy/category-search API in the wrapper). An unusual spelling replies "Game "X" not on Twitch."
- **`!followage` cannot distinguish "not following" from "lookup failed"** — the API wrapper returns null for both, and the reply says "{target} isn't following the channel." either way.
- **Non-Twitch `!uptime` reports live/offline only** — `IStreamLiveRegistry` tracks a per-platform bool with no start times, so live platforms answer "Live now — duration unknown (no start time available)"; Discord is never tracked and reports offline.
- **Re-auth for old tokens** — the write commands need `clips:edit` / `channel:manage:raids` / `channel:manage:broadcast` / `channel:edit:commercial` on the stored broadcaster token. The scopes pre-exist in the authorization flow, but tokens issued before those scope lines were added don't carry them: a token that old needs one re-run of the Twitch connection (same pattern as KI-33).

Documented on [Command Catalog → Mod & stream tools commands](command-catalog.md#mod--stream-tools-commands).

### KI-59 (Wiring gap) — The seeded `!uptime` built-in shadows the new Helix `!uptime`

Wave 14 added a Helix-backed `!uptime` (real `started_at` uptime) to `StreamToolsCommandService`, but the chat pipeline runs the unified Commands table **first** — and the seeded, enabled `!uptime` row (DapperDataService seed; `Stream.StartedAtUtc`-based reply, `CommandService.ExecuteUptimeAsync`) matches and stops processing. On a default install the new handler is therefore unreachable dead-fallthrough: it only answers when the built-in row is disabled or deleted on the Commands page. Both replies are similar ("Stream uptime: Xh Ym"), so most users never notice which one answered. Fix would be either deferring the seed row (as `!watchtime`'s seed does — KI-18) or removing the built-in. Documented on [Command Catalog](command-catalog.md#mod--stream-tools-commands) (routing-precedence note).

### KI-60 (Limitation) — The OBS/Twitch Streaming workflow blocks need their backends running

The four 1.1.0 Streaming blocks (`obs_scene`, `obs_stream`, `obs_replay`, `twitch_clip`) execute through `IStreamControlWorkflowBridge` — the same obs-websocket remote-control seam the VyralDeck OBS buttons use, plus the Helix clip call. That means the **OBS side must actually be reachable**: OBS running with **Tools → WebSocket Server Settings** enabled (and, for `obs_replay`, the replay buffer enabled and active); `twitch_clip` needs Twitch connected and the stream live. When the backend isn't there, the node does not throw — it fails with a helpful message routed to the node's **error branch** (or the flow-level Error Handler when none is wired), which is by design, but a flow without an error branch simply ends Failed. Embedded-OBS users: the bridge targets the remote-control path, so the embedded engine's own outputs are not a fallback. Documented on [Automation → Actions — Streaming](../chat-features/automation.md) and [Workflow Engine](../chat-features/workflows.md).

## Newly-documented limitations (2026-11-07 wave-15 pass)

### KI-61 (Limitation) — Command import translates common variables only; first-chatter detection uses activity records

Both wave-15 headliners shipped; these are their honest boundaries, verified against code as it landed:

- **Command import — translation covers the common variables only.** `TranslateVariables` (`CommandImportService.cs`) maps exactly `$(user)`/`$(touser)`/`${user}` → `{username}`, `$(count)`, plain `$(time)` (no timezone argument), `$(uptime)`/`$(uptimeapi)`, `$(watchtime)` and `$(channel)`. Nightbot/SE specials — `$(eval)`, `$(query)`, `$(urlfetch …)`, `$(readapi …)`, `$(random.pick a, b)`, argumented `$(time TZ)` — have no Vyral equivalent: they stay **verbatim** in the imported response and are flagged in the preview row's note ("Untranslated tokens left as-is: …") for a hand-fix. Permission translation is likewise coarse: moderator-and-up imports as `Moderator`, everything else (subscriber, vip, regular) as `Everyone` — a Nightbot sub-only command loses its sub gate on import. Cooldowns carry over (per-user; StreamElements `cooldownAll` → global).
- **First-time chatter — detection means "no activity row", not "never spoken".** `FirstTimeChatterService` publishes the welcome event only when the profile database has **no UserWatchtime row and no UserCurrency row on any platform** (`HasAnyActivityAsync` — deliberately read-only, 60-second burst dedup). A viewer who quietly earned watchtime or an event bonus before their first message therefore counts as known and is **never welcomed** — the same "first record" semantics family as KI-27's first-chat bonus. Because the check is per-profile, a brand-new profile re-welcomes every existing viewer once. The event carries no display name (the alert shows the username), and TikTok never fires (no chat pipeline). The **Event Testing → First Message** button bypasses the check entirely.
- **Cosmetic**: the *First-Time Chatter Greeting* (`!hello`) starter template's description still calls itself "a stand-in until a first-message trigger exists" — the trigger now ships (`firstmessage`), and the template keeps working regardless.

Documented on [Commands → Import](../chat-features/commands.md#command-import), [Event Reactions → First-time chatters](../chat/event-reactions.md#first-time-chatters), and [Workflow Engine](../chat-features/workflows.md) (the `firstmessage` key).

---

## Newly-documented limitations (2026-11-07 wave-16 pass)

All five wave-16 features shipped (chat-mode commands, the three overlays, ad-hoc chat polls, the `!warn` family, session summaries) and are documented on their pages. These are their honest boundaries, verified against code as it landed:

### KI-62 (Limitation) — Chat-mode auto-revert accuracy and restart behavior

The `!emoteonly`/`!subonly`/`!followers`/`!slow` auto-revert (`ChatModeCommandService`) is a background timer, not an exact alarm clock:

- **Granularity** — the checker is a one-shot timer that ticks at most every 30 seconds, so a due revert fires up to ~30 s after its window expires. The enable reply's "for 10 minutes" is approximate on the revert side.
- **Single attempt** — a revert whose Twitch Helix call fails (token scope dropped mid-stream, network blip) is dropped with an info log; it is not retried. A moderator re-issuing `!<mode> off` is the documented recovery.
- **In-memory only** — pending reverts die with the process. Restarting VyralBot (or crashing) leaves whatever modes were enabled active on Twitch until a mod disables them; nothing resynchronizes on startup.
- **Twitch-only by design** — the commands reply "is Twitch-only." on every other platform (Kick/YouTube/Discord equivalents do not exist in this family); and as with the other Twitch-scope features, tokens issued before `moderator:manage:chat_settings` joined the authorization URL fail the update until the Twitch connection is re-run.

Documented on [Command Catalog → Chat-mode commands](command-catalog.md#chat-mode-commands-twitch).

### KI-63 (Limitation) — Overlay widget poll cadences and the goals bar semantics

The five visible overlay widgets are poll-based reads, not WebSocket pushes (only `audio.html` is pushed):

- **Goals lag up to 15 s** — `goals.html` polls `/goals/data` every 15 seconds, so a fresh completion moves the bar up to 15 s late; the bar also mixes units by design: the numerator is the **aggregate count of viewers who completed the goal** while the target is the **per-viewer threshold** ("completions / target"), so the percentage is a community-progress reading, not any one viewer's progress.
- **Chat feed reads the persisted table** — `chatfeed.html` shows the last N rows of the per-profile `ChatMessages` table, refreshed every 2 s: only Twitch, Kick, YouTube and Discord ever appear (TikTok has no chat service — KI-56), and messages older than the table's retention window are not replayable.
- **Poll overlay polls every 3 s and lingers 5 min** — `polloverlay.html` reflects votes up to 3 s late, shows the final winner bars for exactly 5 minutes after the poll ends, then hides; a hand-crafted `count` on the chat feed and an unknown `platform` value behave as documented (clamp / 400) on the other widgets too.

Documented on [Browser Sources](../stream-tools/browser-sources.md) (per-widget sections) and [Vyral Goals](../stream-tools/vyral-goals.md).

### KI-64 (Limitation) — Warning strikes and session chat counts are in-memory

Two wave-16 features deliberately trade durability for simplicity, and both reset on restart:

- **`!warn` strikes** — manual warns climb the auto-mod escalation ladder through the engine's **in-memory** viewer state (5,000-viewer cap, strike-decay window). Restarting VyralBot clears every viewer's warning count to zero; `!warnings` then reads 0 even for viewers a mod warned minutes before. Only the `ModActions` audit rows survive (the Moderator dashboard's Action Log shows the full warn history), and ladder escalation after a restart starts from scratch. Decay behaves the same way: strikes older than the configured window read as 0.
- **Session chat-message counts** — the Sessions tab's chat total and top-chatters are counted **in memory while the session is live**; messages sent before VyralBot started, or while it was restarted mid-stream, are not reconstructible (no durable per-message counter exists), so the number under-reports on restart-mid-stream days. Event and currency numbers in the same summary come from the database and are window-complete (minus KI-26's ShowInChat-off gap).

Documented on [Command Catalog → Warning commands](command-catalog.md#warning-commands), [Statistics → Session summaries](../stream-tools/statistics.md#session-summaries-sessions-tab), and the [Moderator Guide](../architecture/moderator-guide.md).
