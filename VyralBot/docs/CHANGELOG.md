# Changelog

All notable changes to VyralBot are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[semantic versioning](https://semver.org/).

## [1.1.0] - 2026-11-07

A major stability, security and feature release: every page was audited,
the theme is now consistent across the whole app, moderation and currency got
real enforcement, platform events (Channel Points, Super Chat) went live, and
secrets are now encrypted on your disk. New users get a guided "Get started"
strip on Home and twelve starter workflow templates.

### Added

- **Quotes** — `!quote`, `!quote add/del/search/count` commands (moderator
  gated) on Twitch, Kick, YouTube and Discord, plus a Quotes page with search
  and management.
- **Auto-moderation** — a built-in moderation engine with six rule types,
  escalation and exemptions, working across all live platforms. Twitch now
  enforces timeouts, bans and message deletions; Discord enforces natively.
- **Auto-mod on Kick and YouTube** — Kick now enforces timeouts, bans and
  message deletions too (one re-auth to grant the new moderation permission);
  YouTube deletes messages (its API offers no public ban, so bans degrade to
  a warning).
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
- **Discord scheduled messages** — recurring announcements each bot posts on
  a timer while connected, with `{time}`/`{date}` variables and a 5-minute
  minimum interval (up to 25 per bot).
- **Discord command channels** — limit a bot's commands to the channels you
  pick; messages anywhere else are ignored (moderators are never restricted).
- **Event Testing page** — fire any of 18 test events (follow, sub, raid,
  Super Chat, redemption, first message and more) to try out alerts, sounds
  and workflows.
- **Get started strip** — a dismissible three-step guide on Home (connect a
  platform → set up alerts → import a workflow template) that hides itself
  once any platform connects.
- **Workflow templates** — twelve ready-made starter workflows (follow, sub,
  raid, reward redemption, Super Chat, hello command, timer, warn, sub clip,
  raid mode, commercial-break reminder, first-time-chatter welcome) on the
  Automation page's TEMPLATES panel; each imports disabled so you can edit
  it before it goes live.
- **Control API** — read live status, today's stats and the app version, and
  fire test events over HTTP. Protected by a Bearer token.
- **Commands page upgrades** — duplicate a command, search, per-platform
  columns and sortable usage / last-used.
- **Chat appearance** — text size controls, timestamp toggle, membership
  sound, and a friendly empty state.
- **Sounds upgrades** — a master-volume slider (applies to every event sound,
  including the browser source), per-sound preview play, durations in the
  list, and drag-and-drop import for `.wav` / `.mp3` / `.ogg` / `.m4a`.
- **CSV export** — Statistics and Event History can export the current
  filtered view to an Excel-friendly CSV file.
- **Restream bandwidth guardrail** — a warning when your destinations'
  combined bitrate exceeds 80% of your configured uplink budget, plus a
  dropped-frames column per destination.
- **Profile backup import** — an Import Profile Backup button next to export
  (it never overwrites an existing profile).
- **Workflow canvas tools** — rubber-band selection, multi-select delete and
  drag, copy/paste (Ctrl+C/V) and middle-drag panning.
- **Command platform filter** — a command or workflow can be limited to only
  the platforms you choose.
- **Crash reports** — if VyralBot crashes, it now writes a report (with
  secrets removed), shows a clear error dialog, and keeps logs size-capped.
- **Mod & stream tools** — the classic mod commands in chat: `!so <user>`
  (all platforms), `!clip`, `!raid`/`!raid cancel`, `!title`, `!game`,
  `!commercial`, `!followage` (Twitch), and a Helix-backed `!uptime` (all
  platforms; the seeded built-in still answers first — disable it on the
  Commands page for the live-registry version).
- **Workflow Streaming blocks** — four new action blocks: Switch OBS Scene,
  OBS Stream Start/Stop, Save OBS Replay Buffer, and Create Twitch Clip.
  They ride the OBS remote-control bridge (obs-websocket) and route failures
  to the error branch. The block catalog is now 40 blocks (29 actions).
- **Text-to-speech** — SAPI voice synthesis with a settings card on the Alert
  Settings page (voice, rate, volume, max length, viewer toggle), spoken
  alerts ("New Follower, Bob"), and a `!tts` chat command with a 30-second
  per-viewer cooldown; Do Not Disturb mutes all of it.
- **Death counter** — a Tools-page card (+/−/set/reset, editable label,
  change history) sharing state with the `!death` chat command family and an
  OBS browser-source widget (`deathcounter.html`, polls every 3 s).
- **Leaderboard overlay** — an OBS browser-source widget for the top balances
  (`leaderboard.html`, polls every 10 s, shows your currency name/symbol,
  dark/light styles, optional platform filter) with a URL-builder card on the
  Currency page's Leaderboard tab.
- **VyralDeck status chips** — the deck's live-state payload now includes
  now-playing, uptime and death-count context strings the phone app renders
  as status chips (older clients safely ignore them).
- **Command import** — switchers can bring their commands along: the Commands
  page's CUSTOM tab has an IMPORT button that reads a Nightbot CSV export, a
  StreamElements JSON export or a plain `!name=response` text file, shows a
  dry-run preview grid (duplicates, Nightbot built-ins and Vyral built-ins
  skipped with reasons), translates `$(user)`-style variables to Vyral's
  `{username}` tokens (untranslatable tokens are flagged, not lost) and
  creates only the rows you confirm — existing commands are never
  overwritten.
- **Loyalty seasons** — a Seasons card on the Currency page's Leaderboard tab
  starts competitive seasons: the closing top-20 leaderboard is snapshotted
  into append-only season history, balances can optionally be zeroed in the
  same transaction (earned history, tiers and the audit trail are kept), and
  viewers ask `!season` for the live status and previous winner.
- **First-time chatter welcome** — a viewer's very first chat message (on any
  live platform) fires a "First Message!" alert by default, an optional
  welcome reply (`Chat.FirstChatterWelcome`, with `{user}` variables), and a
  `firstmessage` workflow trigger; detection is once-per-viewer against the
  profile's activity records, and Event Testing can fire it without a
  newcomer.
- **Twitch chat-mode commands with auto-revert** — `!emoteonly`,
  `!subonly`, `!followers` (optional follow age, e.g. `!followers 2h`) and
  `!slow <seconds>` enable Twitch's chat protections from any mod's chat,
  each arming a timed auto-revert (default 10 minutes, capped at 2 hours) so
  a raid-shielded chat always opens up again; `<mode> off` disables
  immediately. Moderator-only and Twitch-only; reverts fire within ~30 s of
  due and do not survive an app restart (see known issues KI-62).
- **Chat feed overlay** — `chatfeed.html`, face-cam captions for OBS: the
  last few chat messages with platform chips, configurable count, font size,
  truncation, fading and single-platform filter (2 s refresh), with a URL
  card on the Audio Settings page.
- **Goals overlay** — `goals.html`, one animated progress bar per enabled
  Vyral goal (Viewer Hub), completions/target with kind chips, dark/light and
  two sizes (15 s refresh), with a copyable URL row on the Viewer Hub's
  Vyral Goals card.
- **Poll overlay** — `polloverlay.html`, live poll results as growing bars
  with vote counts and percentages, a countdown for timed polls, and the
  winner bars lingering 5 minutes after the end (3 s refresh).
- **Chat-driven polls** — `!poll start [5m] Question | Option A | Option B`
  creates and starts a poll straight from chat (2–6 pipe-separated options,
  optional duration token), timed polls auto-end with the results announced
  on every connected platform, and everyone gets a short `!vote <n>` alias.
- **Warning commands** — `!warn <user> [reason]`, `!warnings <user>` and
  `!clearwarn <user>`: manual warns climb the same escalation ladder as
  auto-mod rule violations (shared strikes, decay window, timeout/ban rungs —
  escalating immediately when a warn tips the rung), all audited to the mod
  log. Strike counts are in-memory: restarting the app clears them
  (KI-64).
- **Session summaries** — the Statistics page's new Sessions tab shows a
  post-stream report per session: duration, platforms, new followers/subs,
  raids and viewers brought, cheers and bits, chat messages, currency
  awarded, top chatters and a per-event breakdown (last 20 sessions kept;
  chat counts cover what the running app saw — KI-64).

### Fixed

- Alerts now fire every time — a stuck queue could silently skip them.
- **Profile switching** no longer carries the old profile with you: currency
  caches are invalidated, the sounds folder is swapped, and a running restream
  is stopped (previously the old profile's data could leak into the new one).
- A failed page navigation restores the previous page instead of a blank
  screen.
- **Restream**: hang on exit, duplicated stream-key URLs, per-destination
  status lights (previously stuck red), live destination validation, and a
  banner to re-enable destinations that were auto-disabled.
- **OBS**: crash on exit, stream-start desync, embedded-window zombies, and
  automatic reconnection for remote OBS connections (with re-authentication).
- **Currency**: double-spending, auction double refunds, watchtime over-awards
  after a restart, bank withdrawal cooldowns and account race conditions.
- **Giveaways**: a timed flush could briefly resurrect an already-closed
  giveaway.
- **Discord**: connection cards now show the real status after a network
  loss; deleting a bot cleans up its commands, timers, roles and channels;
  stale connections are cleaned up; the scheduled-message scheduler can no
  longer post a stale message that was being edited.
- **Workflows**: Wait steps no longer fail when a run is cancelled; runs now
  cancel cleanly when a workflow is deleted or disabled, or when you switch
  profile; invalid canvas connections are rejected.
- **Memory** — eight leaks and log floods fixed, including capped chat
  history, so long streams no longer grow memory without bound; final
  review caught one more (a YouTube live-state subscription that survived
  disconnects).
- The Auto-Replies toggle is now respected on Kick, YouTube, TikTok and
  Discord (it previously only worked on Twitch).
- Sounds: volume is now honored for `.wav` files; each profile gets its own
  sounds folder; the browser-source audio page reconnects reliably after a
  disconnect.
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
- Exporting a profile now warns that its encrypted secrets only restore on
  the machine that created them.
- Log volume during chat traffic was reduced to keep logs readable.

### Security

- Login secrets and Discord bot tokens are now **encrypted at rest** using
  Windows DPAPI (existing files are re-encrypted automatically).
- Crash reports automatically scrub tokens, keys and other secrets.
- Kick webhooks now have replay protection and a payload size cap.
- **Restream's bundled FFmpeg download** is pinned to a versioned artifact
  and verified with SHA256 before use, with a disk-space preflight and
  automatic retries.
- Deleting a PIN-protected profile now asks for the PIN first (masked
  prompt, both deletion paths).
- The Control API requires a Bearer token, rejects oversized bodies, and
  denies cross-origin requests by default.
- The audio-websocket endpoint checks origins and caps connections.
- SQLite driver updated to fix a published security advisory
  (GHSA-2m69-gcr7-jv3q).
- VyralDeck pairing accepts only allowlisted addresses.
