# Changelog

All notable changes to VyralBot are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[semantic versioning](https://semver.org/).

## [1.1.0] - 2026-11-07

A major stability, security and feature release: every page was audited,
the theme is now consistent across the whole app, moderation and currency got
real enforcement, platform events (Channel Points, Super Chat) went live, and
secrets are now encrypted on your disk. 676 automated tests.

### Added

- **Quotes** — `!quote`, `!quote add/del/search/count` commands (moderator
  gated) on Twitch, Kick, YouTube and Discord, plus a Quotes page with search
  and management.
- **Auto-moderation** — a built-in moderation engine with six rule types,
  escalation and exemptions, working across all live platforms. Twitch now
  enforces timeouts, bans and message deletions; Discord enforces natively.
- **Twitch Channel Points** — custom reward redemptions now trigger alerts,
  stats, auto-replies, chat lines and workflows.
- **YouTube Super Chats** — amounts and currency show in chat (cheer-style),
  fire alerts and sounds, count toward stats and currency, and can start
  workflows.
- **Discord welcome & auto-role** — per-bot welcome messages and automatic
  role assignment when someone joins your server, with `{user}` variables.
- **Discord dashboard** — live status for every bot you run: servers,
  commands, moderation actions and workflow runs, refreshed automatically.
- **Ephemeral replies** — Discord bots can answer slash commands visibly only
  to the user who invoked them.
- **Command timers** — schedule recurring commands, optionally gated to only
  fire when chat is active (minimum messages between runs).
- **Event Testing page** — fire any of 17 test events (follow, sub, raid,
  Super Chat, redemption and more) to try out alerts, sounds and workflows.
- **Control API** — read live status, today's stats and the app version, and
  fire test events over HTTP. Protected by a Bearer token.
- **Commands page upgrades** — duplicate a command, search, per-platform
  columns and sortable usage / last-used.
- **Chat appearance** — text size controls, timestamp toggle, membership
  sound, and a friendly empty state.
- **Workflow canvas tools** — rubber-band selection, multi-select delete and
  drag, copy/paste (Ctrl+C/V) and middle-drag panning.
- **Command platform filter** — a command or workflow can be limited to only
  the platforms you choose.
- **Crash reports** — if VyralBot crashes, it now writes a report (with
  secrets removed), shows a clear error dialog, and keeps logs size-capped.

### Fixed

- Alerts now fire every time — a stuck queue could silently skip them.
- A failed page navigation restores the previous page instead of a blank
  screen.
- **Restream**: hang on exit, duplicated stream-key URLs, per-destination
  status lights (previously stuck red), live destination validation, and a
  banner to re-enable destinations that were auto-disabled.
- **OBS**: crash on exit, stream-start desync, embedded-window zombies, and
  automatic reconnection for remote OBS connections (with re-authentication).
- **Currency**: double-spending, auction double refunds, watchtime over-awards
  after a restart, bank withdrawal cooldowns and account race conditions.
- **Discord**: connection cards now show the real status after a network
  loss; deleting a bot cleans up its commands, timers, roles and channels;
  stale connections are cleaned up.
- **Workflows**: Wait steps no longer fail when a run is cancelled; runs now
  cancel cleanly when a workflow is deleted or disabled, or when you switch
  profile; invalid canvas connections are rejected.
- **Memory** — eight leaks and log floods fixed, including capped chat
  history, so long streams no longer grow memory without bound.
- The Auto-Replies toggle is now respected on Kick, YouTube, TikTok and
  Discord (it previously only worked on Twitch).
- Sounds: volume is now honored for `.wav` files; each profile gets its own
  sounds folder.
- Marketplace imports can no longer corrupt command routing with dangling
  entries; imported settings round-trip without losing fields.
- Startup is 1–3 seconds faster.
- VyralDeck mobile companion: live status dots, unpairing now closes the
  connection, and connection limits for the pairing server.

### Changed

- **TikTok is now clearly marked "Coming Soon" everywhere.** The previous
  login path was a dead end; no other behavior changed.
- **One consistent dark theme** — roughly 35 pages had off-palette colors;
  every page now follows the same palette, and custom themes apply app-wide.
- **Twitch moderation now enforces** (timeout / ban / delete) instead of only
  posting a notice in chat.
- Stream keys, bot tokens, API keys and the ingest stream key are masked in
  the UI.
- Log volume during chat traffic was reduced to keep logs readable.

### Security

- Login secrets and Discord bot tokens are now **encrypted at rest** using
  Windows DPAPI (existing files are re-encrypted automatically).
- Crash reports automatically scrub tokens, keys and other secrets.
- Kick webhooks now have replay protection and a payload size cap.
- The Control API requires a Bearer token, rejects oversized bodies, and
  denies cross-origin requests by default.
- The audio-websocket endpoint checks origins and caps connections.
- SQLite driver updated to fix a published security advisory
  (GHSA-2m69-gcr7-jv3q).
- VyralDeck pairing accepts only allowlisted addresses.
