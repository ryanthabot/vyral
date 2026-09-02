# VyralBot — STREAM TOOLS Review (Agent 5)

Scope: the "Stream Tools" expandable sidebar group and the tool pages behind it.
(Other agents cover Home, Chat UI, Platforms, Chat Features, Settings.)

## Sidebar structure (src/VyralBot.UI/MainWindow.xaml:298-435)

The sidebar group "Stream Tools" (toggled via `ToggleStreamToolsCommand` / `IsStreamToolsExpanded` in
src/VyralBot.UI/ViewModels/MainViewModel.cs:89,630) contains these entries:

| Sidebar item | Command | Target ViewModel / View |
|---|---|---|
| Browser Sources | **none — button has no Command binding (dead button)** (MainWindow.xaml:324-332) | — |
| Audio Settings | NavigateToAudioSettingsCommand | AudioSettingsViewModel / AudioSettingsView |
| Sounds Management | NavigateToSoundsCommand | SoundsViewModel / SoundsView |
| Statistics | NavigateToStatisticsCommand | StatisticsViewModel / StatisticsView |
| Visual Alerts | NavigateToAlertSettingsCommand | AlertSettingsViewModel / AlertSettingsView |
| Conditional Rules | NavigateToConditionalRulesCommand | ConditionalRulesViewModel / ConditionalRulesView |
| Giveaways | NavigateToGiveawaysCommand | GiveawayViewModel / GiveawayView |
| Polls | NavigateToPollsCommand | PollViewModel / PollView |
| Watchtime | NavigateToWatchtimeCommand | WatchtimeViewModel / WatchtimeView |

All navigations in MainViewModel follow the same pattern: null `CurrentViewModel` first, dispose all
other non-essential ViewModels (`DisposeNonEssentialViewModelsAsync`, MainViewModel.cs:1638), create a
fresh DI scope + ViewModel, call `InitializeAsync()`, then force a blocking compacting Gen2 GC
(MainViewModel.cs:1864).

There is also a "Tools" hub page (NavigateToToolsCommand → ToolsView.xaml + ToolsViewModel.cs).
ToolsViewModel is an **empty stub** (no code beyond class declaration). ToolsView.xaml renders card
grids: a "Chat Features" row (Events & Alerts, Currency & Rewards, Commands, Event History) and a
"Stream Tools" section (Audio Settings, Sounds, Statistics, Visual Alerts, Conditional Rules,
Giveaways, Polls, Watchtime) — all cards bind to MainViewModel navigation commands via
`RelativeSource AncestorType=Window`. Visual Alerts and Events & Alerts both route to the same
`NavigateToAlertSettingsCommand` (ToolsView.xaml:59 and :174).

Currency lives at top level (`NavigateToCurrencyAsync`, MainViewModel.cs:797), not inside Stream
Tools, but is listed in this doc's scope per task description.

---

## 1. Browser Sources

### Where it lives
Sidebar "Stream Tools" → "Browser Sources" — **the button has NO Command binding**
(src/VyralBot.UI/MainWindow.xaml:324-332), so clicking it does nothing. There is no dedicated
Browser Sources page/ViewModel. The actual browser-source functionality surfaces inside the
Audio Settings page (URL display + copy) and the embedded web server.

### What it does
Serves a single HTML page, `audio.html`, meant to be added as an OBS Browser Source. It connects
back to VyralBot over WebSocket and plays sound files over HTTP when events happen. That is the
only browser source page in the app — there are NO overlay/alert/poll/giveaway browser-source HTML
pages (alerts use an in-app WPF window instead; see section 5).

### How it works
Two copies of the page exist:
- `BrowserSource/audio.html` (project root) — legacy copy, hard-coded to `ws://localhost:8080` /
  `http://localhost:8080/sounds/` (BrowserSource/audio.html:57-58). Used by the legacy
  Core `AudioWebSocketServer` (see below).
- `src/VyralBot.Api/wwwroot/audio.html` — current copy, hard-coded to
  `ws://localhost:5000/audio-ws` and `http://localhost:5000/sounds/`
  (src/VyralBot.Api/wwwroot/audio.html:51-52).

Page behavior (both copies): connects WebSocket, shows a status pill (connecting/connected/
disconnected) that auto-hides after 5s, auto-reconnects every 3s, and on JSON message
`{ type: 'play', sound: '...' }` plays `http://<host>/sounds/<file>` via a cached `Audio` element.
Note the page ignores any `volume` field in the message — volume is not applied browser-side.

Embedded web server — src/VyralBot.Api/Services/WebServerService.cs:
- `StartAsync(port = 5000)` builds an in-process ASP.NET Core (Kestrel) app listening on localhost
  only (`ListenLocalhost`, WebServerService.cs:55), pulls shared services (IEventBus, ILoggerFactory)
  from the parent WPF DI container (WebServerService.cs:62-63), serves static files from wwwroot
  (`audio.html`, `sounds/`) and maps the WebhooksController.
- `/audio-ws` minimal-API endpoint (WebServerService.cs:91-122) tracks connected browser sources in
  a ConcurrentDictionary.
- It wires the broadcast bridge: `VyralBot.Core.Services.Audio.AudioWebSocketBroadcaster.BroadcastAsync`
  (a static delegate field, src/VyralBot.Core/Services/Audio/AudioWebSocketBroadcaster.cs:8) is set
  to a lambda that fans out a text frame to all connected sockets (WebServerService.cs:125-135).
  Core's SoundService calls this static to push "play sound" JSON to OBS.
- Started by WebhookCoordinatorService/Setup wizards on port 5000 (also directly via
  NgrokSetupWizardViewModel.cs:337 and NgrokSetupViewModel.cs:408). MainViewModel comments confirm
  "Audio WebSocket server now handled by Kestrel (port 5000)" (MainViewModel.cs:230-232).

Legacy path (still in tree, apparently unused): src/VyralBot.Core/Services/Audio/AudioWebSocketServer.cs
— a standalone HttpListener on port 8080 serving the root BrowserSource/audio.html and files from
`<exe>\Sounds`, with `SendAudioEventAsync(AudioEvent)`. The WPF DI registers ISoundService but the
comment in AudioSettingsViewModel.cs:14 says the IAudioWebSocketServer is "No longer needed".

### ngrok's role
src/VyralBot.Api/Services/NgrokService.cs: spawns `ngrok http <port> --region <region> --authtoken <token>`
(NgrokService.cs:60), polls ngrok's local API `http://127.0.0.1:4040/api/tunnels` for the public
https URL (NgrokService.cs:160-191), monitors for URL change every 30s and raises
`PublicUrlChanged`. Auth token persisted per-profile in `<profile>\credentials\ngrok_credentials.json`
(NgrokService.cs:261-285). Managed by WebhookCoordinatorService (src/VyralBot.Api/Services/
WebhookCoordinatorService.cs) and configured via WebServerSettingsViewModel (Settings page → Web
Server section; start/stop, port, ngrok region, auto-start; also NgrokSetupWizardViewModel).

IMPORTANT nuance: the web server binds to localhost only. ngrok's public URL exists primarily for
receiving Kick/YouTube webhooks; the OBS browser source itself is intended to use the localhost URL
(audio.html hard-codes localhost:5000). So "ngrok exposes browser source URLs" is only true
insofar as the same server is shared — the HTML as shipped cannot use the ngrok URL without edits
(host is hard-coded). WebServerDependencyTracker (src/VyralBot.Core/Services/WebServer/
WebServerDependencyTracker.cs) prevents stopping the web server while features depend on it.

### Settings that affect it
- WebServerSettings in DB (`GetWebServerSettingsAsync`): IsEnabled, Port (default 5000), NgrokRegion,
  AutoStartOnLaunch, CurrentTunnelUrl — saved by WebServerSettingsViewModel.SaveSettingsAsync
  (src/VyralBot.UI/ViewModels/WebServerSettingsViewModel.cs:436-452).
- The port is effectively fixed at 5000 for audio (AudioSettingsViewModel forces it,
  AudioSettingsViewModel.cs:195-204).

---

## 2. Audio (Audio Settings)

### Where it lives
Sidebar Stream Tools → "Audio Settings" (NavigateToAudioSettingsCommand, MainViewModel.cs:915) →
AudioSettingsView.xaml + AudioSettingsViewModel.cs. Also a card on the Tools hub.

### What it does (user-visible)
- Two output toggles: "Streamer Sounds" (play locally in-app, the streamer hears them) and
  "Stream Sounds" (send to browser source so viewers hear them) (AudioSettingsView.xaml:43-69).
- Master volume slider 0-100 (clamped, AudioSettingsViewModel.cs:188-193).
- WebSocket Server panel showing status/connected clients/port (port hard-forced to 5000; the port
  TextBox's changes are reverted with a warning — AudioSettingsViewModel.cs:195-204).
- Browser Source (OBS) panel showing `http://localhost:5000/audio.html` with Copy button and
  step-by-step OBS instructions (add Browser Source, paste URL, set 1920x1080;
  AudioSettingsView.xaml:207-252).
- Test Sound button (plays first available sound at master volume),
  Save, and a vestigial "Restart Server" button that just shows a message box ("server is always
  running on port 5000", AudioSettingsViewModel.cs:169-179).

### How it works
- Settings persisted as JSON in the app config store under key `AudioSettings` via IDataService
  `Get/SetConfigValueAsync` (AudioSettingsViewModel.cs:53-63, 82-91). Model `AudioSettings`
  {StreamerSoundsEnabled, StreamSoundsEnabled, MasterVolume, WebSocketPort} defined in
  src/VyralBot.Core/Services/Audio/SoundService.cs:321-327.
- No audio device selection, no routing, no ducking — the "Audio" tool is only about where sounds
  play (local vs browser) and volume. Local playback is `System.Media.SoundPlayer` and .wav only;
  mp3/ogg/m4a log a warning "Local playback only supports .wav files" and play nothing locally
  (SoundService.cs:292-315).

### Interactions
- SoundService.PlaySoundFileAsync reads these settings on every play: local play gated by
  StreamerSoundsEnabled, browser broadcast gated by StreamSoundsEnabled; volume = MasterVolume/100
  × per-event volume (SoundService.cs:141-189).

---

## 3. Sounds (Sounds Management)

### Where it lives
Sidebar Stream Tools → "Sounds Management" (NavigateToSoundsCommand, MainViewModel.cs:950) →
SoundsView.xaml + SoundsViewModel.cs.

### What it does (user-visible)
- Sound library: upload (.wav/.mp3/.ogg/.m4a via OpenFileDialog; duplicates get `_1`, `_2` suffixes),
  delete (with confirm; also clears event assignments referencing the file), preview-play any file.
- Event mapping table: fixed list of 10 platform events — Kick: follow, subscribe, giftsub, host;
  Twitch: follow, subscribe, resub, giftsub, raid, cheer (SoundsViewModel.cs:91-103). For each:
  assign/unassign a sound file, per-event volume slider (saved as percent), and Test Event Sound.
- No YouTube/TikTok/Discord event mappings, no sound-board (manual hotkey) feature, no command
  triggered sounds — sounds trigger only from platform events and manual test clicks.

### How it works
- Files live in `src/VyralBot.Api/wwwroot/sounds` (resolved relative to the exe:
  `<exe>\..\VyralBot.Api\wwwroot\sounds`, SoundService.cs:24) so the Kestrel static file middleware
  serves them at `http://localhost:5000/sounds/<file>`.
- Mappings persisted in the config store with string keys `EventSound_<Platform>_<eventType>` and
  `EventVolume_<Platform>_<eventType>` (SoundsViewModel.cs:107-108, 250-251, 331).
- Playback pipeline (SoundService.PlaySoundFileAsync): local SoundPlayer if StreamerSoundsEnabled;
  if StreamSoundsEnabled, serializes `AudioEvent {EventType, Platform, SoundFile, Volume}` JSON and
  invokes static `AudioWebSocketBroadcaster.BroadcastAsync` which pushes it to every `/audio-ws`
  client (SoundService.cs:161-180).
- BUG (verified): the served wwwroot/audio.html only reacts to messages shaped
  `{"type":"play","sound":"..."}` (audio.html:85-88), but the only producer is SoundService
  serializing `AudioEvent` (PascalCase `EventType/Platform/SoundFile/Volume`, no camelCase policy,
  class at src/VyralBot.Core/Services/Audio/IAudioWebSocketServer.cs:37-63). Nothing in the app
  ever sends `{type:'play'}` — so **browser-source (viewer-side) sound playback never actually
  fires** despite the UI advertising it.

### Interactions
- Event triggers live in ChatViewModel event handlers (not in this tool): e.g.
  `OnKickFollowAsync` → `_soundService.PlayEventSoundAsync("follow","Kick")`
  (src/VyralBot.UI/ViewModels/ChatViewModel.cs:1719); similarly Kick subscribe/giftsub/host at
  1807/1878/1944 and Twitch follow/subscribe/resub/giftsub/raid/cheer at
  2107/2186/2267/2329/2372/2443. Whether the event also shows in chat is controlled by
  EventSettingsService ("Kick"/"Follow" settings) — separate from sounds.
- PlayEventSoundAsync applies master volume × event volume and no-ops if both output toggles are off
  (SoundService.cs:108-139).
- MainViewModel calls `InitializeDefaultSoundsAsync()` at profile load which only logs warnings for
  missing defaults (follow.wav, subscribe.wav, raid.wav, cheer.wav, giftsub.wav) — it ships no
  bundled sounds (SoundService.cs:202-227, MainViewModel.cs:232-233).

---

## 4. Visual Alerts

### Where it lives
Sidebar Stream Tools → "Visual Alerts" (NavigateToAlertSettingsCommand, MainViewModel.cs:1055) →
AlertSettingsView.xaml + AlertSettingsViewModel.cs. Also appears as "Events & Alerts" card in the
Tools hub (same destination).

### What it does (user-visible)
Alert Settings page offers: master AlertsEnabled toggle, alert duration (seconds), animation choice
(SlideInFromTop, SlideInFromBottom, FadeIn, Bounce), screen position (9 positions), per-event
toggles (Follow, Subscribe, GiftSub, Raid, Host, Cheer), Test Alert button, Save, Reset to Defaults.

The actual alert is an in-app WPF overlay window — NOT a browser source.

### How it works
- IAlertService/AlertService (src/VyralBot.UI/Services/AlertService.cs) creates a
  `AlertOverlayWindow` on the UI dispatcher; a second alert replaces the current one
  (single-alert queue, AlertService.cs:50-62).
- AlertOverlayWindow (src/VyralBot.UI/Views/AlertOverlayWindow.xaml.cs) shows title, username,
  optional message, optional image (BitmapImage from URL), plays one of four XAML Storyboards, and
  auto-closes after `durationSeconds` via DispatcherTimer, then fades out
  (AlertOverlayWindow.xaml.cs:23-99). Positions computed against PrimaryScreen bounds
  (AlertOverlayWindow.xaml.cs:124-176). Enums AlertAnimation/AlertPosition at
  AlertOverlayWindow.xaml.cs:186-205.
- No TTS, no GIF/video, no templates, no per-event customization wired in.
- src/VyralBot.Core/Services/Alerts/ exists but is EMPTY (no files).

### Interactions + gap (notable)
- Real events route through ChatViewModel handlers: after playing the sound they call
  `ShowEventAlertAsync("New Follower!", username, ...)` (ChatViewModel.cs:2629-2650), which calls
  IAlertService with hard-coded SlideInFromTop/TopCenter/5s — it does NOT read any of the settings
  on the Alert Settings page.
- AlertSettingsViewModel.InitializeAsync/SaveSettingsAsync are TODOs — settings are never loaded
  from or saved to the database (AlertSettingsViewModel.cs:82, 119). The Save button shows
  "saved successfully" without persisting anything. Per-event toggles and position/animation/duration
  selections have no effect on real alerts.

---

---

## 5. Giveaways

### Where it lives
Sidebar Stream Tools → "Giveaways" (NavigateToGiveawaysCommand, MainViewModel.cs:1125) →
GiveawayView.xaml + GiveawayViewModel.cs.

### What it does (user-visible)
- Create form: name, description, entry keyword (default "enter" — becomes a chat command like
  `!enter`), winner count, max entries cap, subscriber bonus (toggle + extra entries, default 5),
  min watchtime (minutes), followers-only, subscribers-only, allowed platforms (default
  "Twitch,Kick"), optional scheduled end time (GiveawayViewModel.cs:33-67).
- List of giveaways with status; selecting one loads its entries + stats (total entries, unique
  participants). Buttons: Start, Draw Winners, Cancel, Refresh (GiveawayViewModel.cs:231-396).
- Chat side (GiveawayCommandService): `!giveaway start|enter|winner|draw|cancel|status` (mod-only
  for start/draw/cancel) plus the giveaway's custom entry keyword (e.g. `!enter`) while a giveaway
  is active (src/VyralBot.Core/Services/GiveawayCommandService.cs:26-93).

### How it works
- src/VyralBot.Core/Services/GiveawayService.cs — lifecycle Pending → Active → Completed/Cancelled
  (only one Active giveaway allowed at a time, enforced in StartGiveawayAsync:82-87).
  `EnterGiveawayAsync` (:100-202) validates status, platform allow-list (comma-separated or "All"),
  follower/subscriber requirement, min watchtime, duplicate entry (per username+platform), max
  entry cap; computes EntryCount = 1 + subscriber bonus. `DrawWinnersAsync` (:207-286) builds a
  weighted pool (each entry contributes EntryCount tickets), draws up to WinnerCount unique winners
  (retry cap 100 attempts per winner), marks winning rows (IsWinner, WonAt), sets Completed status
  and stores winner names as a comma string on the Giveaway entity.
- Persistence: Giveaway / GiveawayEntry entities via IDataService (GetGiveawaysAsync,
  GetGiveawayEntriesAsync, GetGiveawayEntryCountAsync, GetGiveawayUniqueParticipantsAsync,
  SaveGiveawayAsync...) — SQLite through Dapper/ProfileAwareDataService.
- DI: registered singleton in src/VyralBot.UI/App.xaml.cs:130.

### Interactions
- Entry path: ChatViewModel.OnChatMessageReceivedAsync routes any `!`-prefixed message to
  GiveawayCommandService (ChatViewModel.cs:692-695) — after the unified CommandService and currency
  commands. Response is sent back only to Twitch or Kick (`_twitchService/_kickService
  SendMessageAsync`); a message on YouTube/TikTok/Discord would be evaluated but its reply is
  dropped.
- IMPORTANT wiring gap: ChatViewModel passes `false` for isFollower and `0` for watchtimeMinutes
  ("can be tracked in future", ChatViewModel.cs:703-704). Therefore a FollowersOnly giveaway always
  rejects everyone, and MinWatchtime > 0 always rejects everyone (watchtime data exists in
  WatchtimeService but is not plumbed in). SubscribersOnly works (IsSubscriber is passed).
- No overlay/browser-source output for winners; winners are only posted to chat + shown in the
  Giveaway page's StatusMessage.

---

## 6. Polls

### Where it lives
Sidebar Stream Tools → "Polls" (NavigateToPollsCommand, MainViewModel.cs:1160) → PollView.xaml +
PollViewModel.cs.

### What it does (user-visible)
- Create form: question, options (one per line, min 2), allow multiple votes (vote changing),
  min watchtime, followers-only, subscribers-only, allowed platforms (default "All"), scheduled end
  (PollViewModel.cs:34-56).
- List + selection with votes; buttons: Start, End (computes results), Cancel, Delete, Refresh.
- Chat side: `!poll start|vote <n>|results|end|cancel|status` (PollCommandService.cs:26-76);
  results show per-option counts and percentages; falls back to the most recent completed poll for
  `!poll results`.

### How it works
- src/VyralBot.Core/Services/PollService.cs — same lifecycle pattern as giveaways (Pending →
  Active → Completed/Cancelled, single active poll enforced :81-86). `VoteAsync` (:99-198)
  validates option index, platform allow-list, follower/sub/watchtime requirements, duplicate vote
  (per username+platform) — if AllowMultipleVotes, an existing vote is moved rather than rejected.
  `EndPollAsync` (:203-240) aggregates per-option counts into a dictionary, stores them as JSON in
  Poll.VoteResults, announces the winning option. Options themselves stored as a JSON string array
  on the Poll entity.
- Persistence: Poll / PollVote entities via IDataService (SavePollAsync, GetActivePollAsync,
  GetPollVoteResultsAsync, DeletePollAsync...).
- DI singleton: App.xaml.cs:134.

### Interactions
- Same ChatViewModel routing as giveaways but gated on message starting with `!poll`
  (ChatViewModel.cs:727-730), same isFollower=false / watchtimeMinutes=0 gap
  (ChatViewModel.cs:735-736) and Twitch/Kick-only response delivery. No overlay output for live
  poll results (no browser source page exists).

---

## 7. Watchtime

### Where it lives
Sidebar Stream Tools → "Watchtime" (NavigateToWatchtimeCommand, MainViewModel.cs:1195) →
WatchtimeView.xaml + WatchtimeViewModel.cs.

### What it does (user-visible)
- Read-only dashboard: All viewers table (sorted by total minutes), Top viewers (configurable
  limit 1-100), Active viewers list + count, selected-viewer detail; Refresh; manual
  "Reset stream session" button (zeroes per-session counters for everyone)
  (WatchtimeViewModel.cs:118-160).
- Chat side (`!watchtime`): own total + current-stream time, `!watchtime <user>` for another
  user's total, `!watchtime top [n]` / `leaderboard` / `lb` leaderboard (default 5, max 10)
  (WatchtimeCommandService.cs:26-118).

### How it works
- src/VyralBot.Core/Services/WatchtimeService.cs:
  - `RecordUserActivityAsync(username, platform, streamId)` creates/updates a UserWatchtime row
    (last-seen, IsActive, session reset when streamId changes).
  - `IncrementActiveWatchtimeAsync(minutes)` adds minutes to TotalMinutes and
    StreamSessionMinutes for every active viewer AND awards currency: 1 currency per minute via
    ICurrencyService.AddCurrencyAsync("Watchtime reward") (WatchtimeService.cs:87-99) — the main
    watchtime → currency integration.
  - `DeactivateInactiveViewersAsync` marks viewers inactive after a 5-minute threshold
    (WatchtimeService.cs:17,113-118).
  - `FormatWatchtime` renders minutes as "5m / 3h 20m / 2d 5h".
- src/VyralBot.Core/Services/WatchtimeBackgroundService.cs — hosted service ticking every 1 minute
  to increment active viewers, and every 2 minutes to deactivate stale viewers
  (WatchtimeBackgroundService.cs:14-15,31-46). Registered via AddHostedService in App.xaml.cs:139.
- Persistence: UserWatchtime entity (Username, Platform, TotalMinutes, StreamSessionMinutes,
  FirstSeenAt, LastSeenAt, LastStreamId, IsActive) via IDataService.

### Interactions
- Activity source: every incoming chat message calls RecordUserActivityAsync — i.e. "watchtime"
  is really chat-activity time, not actual viewing (ChatViewModel.cs:790-794). Viewers who never
  chat are never tracked.
- Feeds currency earning (above), and is intended to feed giveaway/poll eligibility, but that is
  not wired (see sections 5/6).

---

## 8. Statistics

### Where it lives
Sidebar Stream Tools → "Statistics" (NavigateToStatisticsCommand, MainViewModel.cs:985) →
StatisticsView.xaml + StatisticsViewModel.cs.

### What it does (user-visible)
- Date-range filter (defaults last 30 days), platform filter (null/"Kick"/"Twitch"), event type
  filter (All, follow, subscribe, giftsub, raid, cheer, host).
- Event count summary grid (platform × event type) + total events; Top contributors for the
  selected event type (top 20); Top users overall (top 20); Export to CSV via SaveFileDialog
  (StatisticsViewModel.cs:48-51, 76-207).

### How it works
- src/VyralBot.Core/Services/Statistics/StatisticsService.cs (implements IStatisticsService
  defined in the same file, :10-41):
  - `GetEventCountsByTypeAsync` pulls up to 10,000 EventStatistic rows and groups in memory by
    platform → event type (:54-81).
  - `GetTopContributorsAsync` groups by user+platform, sums a numeric value extracted from the
    row's EventData JSON (looks for `count`, `amount`, `viewers`, `bits`, `value` keys —
    ExtractValueFromEventData :218-248), sorts by value-or-count (:83-105).
  - `GetTopUsersAsync` (:107-128), `GetUserStatisticsAsync` (:153-177),
    `GetEventCountsByDateAsync` (chart-ready, :179-202 — not surfaced in the UI),
    `ExportToCsvAsync` (:130-150).
- Reads only EventStatistics table via IDataService.GetEventStatisticsAsync.
- DI singleton: App.xaml.cs:177.

### CRITICAL finding (verified)
`IDataService.SaveEventStatisticAsync` (src/VyralBot.Data/IDataService.cs:62, implemented in
DapperDataService.cs:264, table created at DapperDataService.cs:2395-2408) has **zero callers**
anywhere in the solution — no service or ViewModel ever inserts EventStatistic rows. The
Statistics page (and the Event History page, which reads the same table) will therefore always be
empty in the running app unless rows are inserted externally. The Write path exists in the data
layer only.

---

---

## 9. Currency & Rewards

### Where it lives
NOT in the Stream Tools sidebar group — top-level "Currency" navigation
(NavigateToCurrencyAsync, MainViewModel.cs:797) plus "Currency & Rewards" card in the Tools hub
(ToolsView.xaml:71-82) → CurrencyView.xaml + CurrencyViewModel.cs (4 tabs: Settings, Rewards,
Redemptions, Leaderboard; also a username-filtered Transactions lookup, CurrencyView.xaml:53-352).
View rendered via implicit DataTemplate in MainWindow.xaml:802.

### What it does (user-visible)
- Settings tab: currency name/symbol, watch-time rewards (enable, points per interval, interval
  seconds), chat-activity rewards (enable, points per message, cooldown), event bonuses
  (follow/subscribe/gift-sub per sub/raid per viewer amounts + enables). **The "Save Settings"
  button does NOT persist anything** — see Issues.
- Rewards tab: CRUD reward store items (name, description, cost, requires-approval, cooldown
  seconds), enable/disable toggle, redemption counts.
- Redemptions tab: approve / reject (refund) / complete redemptions ("Admin" is hard-coded as the
  approver, CurrencyViewModel.cs:320-338).
- Leaderboard tab: top 10 balances, refresh.
- Transactions: per-user history lookup (last 100).
- Chat commands (src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs:55-76):
  `!points|!balance|!currency`, `!leaderboard|!top`, `!give`/`!take` (mod), `!redeem`,
  `!rewards`, `!gamble|!bet`, `!daily`, `!rank|!tier`, `!store|!shop`, `!slots`, `!duel`+`!accept`,
  `!heist`, `!roulette`, `!link` (cross-platform account linking), `!boost` (mod), `!bid`,
  `!auction` (mod: start/end/cancel/info), `!deposit`, `!withdraw`, `!bank`.

### How it works — services in src/VyralBot.Core/Services/Currency/
- **CurrencyService.cs** (core ledger): per-(username,platform) balances with a global
  `_balanceLock`, settings cached 5 min (creates defaults "Points"/"⭐"); Add/Remove/Transfer with
  full CurrencyTransaction audit trail; earning methods:
  - `AwardChatActivityAsync` (:229) — points per message with per-user cooldown.
  - `AwardWatchTimeAsync` (:262) — points per interval (a second, independent watchtime-earning
    path in addition to WatchtimeService's 1-per-minute reward).
  - `AwardEventBonusAsync` (:295) — follow/sub/giftsub×count/raid×viewers/cheer×bits/host bonuses
    × platform multiplier (Twitch/Kick/YouTube earn multipliers, :496-503).
  - `ClaimDailyBonusAsync` (:356) — daily streak (ViewerStreak entity) with day-1/5/10 streak
    bonuses.
  - `AwardFirstChatBonusAsync` (:417), `GetEffectiveMultiplierAsync` (:432, role multipliers),
  `GiveCurrencyToAllAsync` (:452), `GetEconomyStatsAsync` (:469 — totalInCirculation etc.; not
  surfaced in UI).
- **CurrencyRewardsService.cs**: `RedeemRewardAsync` (:48-105) validates via CanRedeemAsync
  (enabled, cost, per-user/global limits, cooldown, availability window, loyalty tier), deducts
  currency, creates CurrencyRedemption (Pending if RequiresApproval), increments counters;
  Approve/Reject (refunds) /Complete (:118+).
- **CurrencyGameService.cs** (gambling): slots (:81), duels (initiate :134 / accept :154), heist
  (join :199 / resolve :235), roulette (:290), with `CanPlayAsync` limits (:36).
- **CurrencyBankService.cs**: deposit/withdraw to a separate bank account + daily interest
  payouts `ProcessInterestPayoutsAsync` (:118).
- **CurrencyAuctionService.cs**: mod-created auctions (item, starting bid, duration, min
  increment), `!bid` placements, resolve/cancel, history (:26-188).
- **CurrencyBoostService.cs**: timed global/platform earn multipliers (`StartBoostAsync`,
  `GetActiveMultiplierAsync`, `ExpireBoostsAsync`).
- **LoyaltyTierService.cs**: tier from TotalEarned (10-min cache), progress to next tier.
- **LinkedAccountService.cs**: cross-platform identity linking with verification codes
  (InitiateLinkAsync :65, VerifyLinkAsync :90, ResolveIdentitiesAsync :140).
- **CurrencyDecayBackgroundService.cs** (hosted, hourly, App.xaml.cs:126): applies inactivity
  decay (percent after N days, ProcessDecayAsync :63-93), bank interest, boost expiry.

### Interactions
- ChatViewModel is the earning trigger hub: every message → `AwardChatActivityAsync`
  (ChatViewModel.cs:807); Kick follow/sub/giftsub events and Twitch follow/sub/giftsub/raid
  events → `AwardEventBonusAsync` (ChatViewModel.cs:1735, 1824, 1894, 2123, 2203, 2345, 2388).
- WatchtimeService.IncrementActiveWatchtimeAsync also grants 1 currency/minute
  (WatchtimeService.cs:87-99).
- `!`-commands are routed from ChatViewModel.OnChatMessageReceivedAsync: unified CommandService
  first, then CurrencyCommandService (ChatViewModel.cs:664), then giveaway/poll/watchtime
  services. Responses only sent to Twitch/Kick.
- Currency balance is injected into CommandContext for the variable/command system
  (ChatViewModel.cs:615-636).
- Persistence: UserCurrency, CurrencyTransaction, CurrencySetting, CurrencyReward,
  CurrencyRedemption, CurrencyAuction(+Bids), UserBankAccount, CurrencyBoost, LoyaltyTier,
  ViewerStreak, LinkedAccount entities via IDataService/ProfileAwareDataService (per-profile DBs).
- Design doc: currency-and-reward-system.md (at project root, next to the .sln) describes the
  phased plan; the "missing" services it lists (games/loyalty/linked accounts) now exist.

### Issue
- CurrencyViewModel.SaveSettingsAsync (src/VyralBot.UI/ViewModels/CurrencyViewModel.cs:105-122)
  logs "Settings saved successfully!" but never calls any save API — the entire Settings tab is
  non-persistent in the current UI.

---

## Cross-cutting interactions & shared infrastructure

- **Navigation/memory**: every Stream Tools page is created in a fresh DI scope, and navigating
  anywhere disposes all other tool ViewModels then forces a blocking compacting Gen2 GC
  (MainViewModel.cs:1638-1868). Views are instantiated lazily via implicit DataTemplates in
  MainWindow.xaml:505-902 (e.g. CurrencyViewModel → CurrencyView at :802).
- **Chat command pipeline order** (ChatViewModel.OnChatMessageReceivedAsync, ~:601-810):
  1. unified CommandService (custom commands, returns → stops processing)
  2. CurrencyCommandService (any `!`)
  3. GiveawayCommandService (any `!`, incl. custom entry keyword)
  4. PollCommandService (`!poll*`)
  5. WatchtimeCommandService (`!watchtime*`)
  then watchtime tracking + chat-activity currency for the message. Bot replies are only delivered
  to Twitch/Kick even though entries/votes/balances are recorded for any platform.
- **Event handler fan-out** (ChatViewModel event handlers, e.g. OnKickFollowAsync :1700): for each
  Kick/Twitch follow/sub/giftsub/host/raid/cheer event the app (a) optionally prints a system chat
  message (EventSettingsService), (b) plays the mapped event sound (SoundService → local +
  browser WS), (c) shows the WPF alert overlay (AlertService), (d) awards currency
  (AwardEventBonusAsync). No parallel YouTube/TikTok/Discord event handlers exist.
- **Database**: SQLite via DapperDataService wrapped by ProfileAwareDataService (per-profile DB
  switch). Tool data (giveaways, polls, watchtime, currency, events) all flows through
  IDataService.
- **Web server**: one embedded Kestrel server on localhost:5000 (WebServerService) serves the OBS
  audio browser source (wwwroot/audio.html + /sounds/*), the /audio-ws WebSocket, and the Kick
  webhook controller; ngrok (NgrokService) tunnels it publicly for webhooks. WebServerDependency
  Tracker gates stopping it.

## Issues / surprises found (all verified in code)

1. Sidebar "Browser Sources" button is dead — no Command binding (MainWindow.xaml:324-332).
2. Browser-source sound playback can never fire: server broadcasts PascalCase `AudioEvent` JSON
   while audio.html only handles `{"type":"play","sound":...}`
   (SoundService.cs:164-175 vs wwwroot/audio.html:85-88).
3. EventStatistics table is never written by any code — Statistics (and Event History) pages read
   an always-empty table (IDataService.cs:62 has no callers).
4. Alert Settings page is a façade: load/save are TODOs; real alerts use hard-coded animation/
   position/duration (AlertSettingsViewModel.cs:82,119; ChatViewModel.cs:2635-2643).
5. Giveaways/Polls: `isFollower` is always false and `watchtimeMinutes` always 0 when invoked from
   chat, so FollowersOnly / MinWatchtime eligibility permanently rejects everyone
   (ChatViewModel.cs:703-704, 735-736).
6. Currency Settings tab Save button does not persist (CurrencyViewModel.cs:105-122).
7. Watchtime = chat activity only (RecordUserActivityAsync on messages, ChatViewModel.cs:790-794);
   silent viewers are never counted, and "connected clients" on the Audio page is a stub showing 0.
8. Local (streamer) sound playback supports .wav only (SoundPlayer), mp3/ogg/m4a only work via the
   (broken, see #2) browser path (SoundService.cs:292-315).
9. Root docs OBS-addition.md / OBS_INTEGRATION_PLAN.md are aspirational libobs-embedding plans,
   not implemented; the only real OBS integration is the audio browser source URL.
10. Tools hub "Chat Features" row and "Stream Tools" row both include cards routing to the same
    Alert page ("Events & Alerts" and "Visual Alerts", ToolsView.xaml:59 and :174).
