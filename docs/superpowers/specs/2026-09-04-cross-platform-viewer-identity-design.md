# Cross-Platform Viewer Identity, Multi-Platform Rewards & Live Giveaways — Design

Date: 2026-09-04
Status: Approved for implementation (autonomous session; user delegated all decisions)
Supersedes: none. Builds on the existing `LinkedAccounts` / `LinkedAccountService` / `!link` foundation.

## Problem statement

Viewers watch from multiple platforms but the bot treats each `(Username, Platform)` pair as a
stranger. We need:

1. **Account linking by code** — a viewer DMs the Discord bot, receives a one-time code, and sends
   that code to the bot on Twitch/Kick/YouTube (chat command fallback where DMs don't exist). All
   their platform accounts then map to one unified viewer identity. Must extend to TikTok, Facebook
   Gaming, Rumble (and later social accounts) without schema changes.
2. **Multi-platform rewards** — viewers watching from 2+ linked platforms earn bonus points
   (manual per-viewer opt-in toggle OR auto-apply), and get giveaway odds boosts based on their
   follower/subscriber status, including each platform's 3-tier sub system.
3. **Social goals ("Vyral Status")** — a special promotion system: viewers who complete
   multi-platform goals (link N platforms, follow on N platforms, sub on any platform, later:
   social-media goals) earn rewards and a chat-announced special status.
4. **Giveaway fixes** — deletable old giveaways, live UI (no manual refresh when starting/drawing/
   entering), and adaptive intake that auto-switches from live to batched timed processing under
   heavy traffic to protect CPU/bandwidth.

## Non-goals (explicit)

- No live TikTok / Facebook Gaming / Rumble chat integrations in this pass (external API access
  infeasible autonomously). We ship: platform-agnostic identity/rewards/giveaway support for any
  platform string, UI platform cards (TikTok card already stubbed; Facebook/Rumble added as
  "coming soon"), and the exact integration seam documented.
- No Instagram support.
- No per-viewer OAuth for social-media comment verification (future; the goals framework accepts
  new goal types later).
- No Twitch whisper receive (IRC parser lacks WHISPER; deprecated by Twitch). Discord DM is the
  DM path; all other platforms use the chat-command flow.

## Architecture decisions

1. **Unified identity stays `ViewerId`** (GUID string) in the existing `LinkedAccounts` table.
   No new identity registry. Every linked `(Username, Platform)` row points at one ViewerId.
2. **Platform names are strings everywhere** ("Twitch", "Kick", "YouTube", "Discord", "TikTok",
   "Facebook", "Rumble") — matching the existing convention. No enum refactor.
3. **Link codes become persistent** (`LinkCodes` table) instead of the in-memory `_pendingLinks`
   dictionary, so codes survive restarts and can be audited/deleted from the UI.
4. **Codes are delivered back over the channel they arrived on**: Discord DMs reply in-DM
   (new `SendDmToChannelAsync`); every other platform replies in chat (5-minute single-use code —
   acceptable to be visible, matching today's `!link` behavior).
5. **Per-viewer platform status is persisted** (`ViewerPlatformStatus`: sub tier 0/1/2/3, follower
   flag, months) from platform events, so giveaway odds and goals don't depend on transient event
   flags. Best tier across a viewer's linked platforms wins.
6. **Multi-platform points bonus is one new factor** inside
   `CurrencyService.ApplyEarnMultipliersAsync`, computed by a new
   `IMultiPlatformBonusService` (own service = testable, no CurrencyService bloat).
7. **Giveaway live updates use the existing EventBus** (push), with debounced UI reloads.
8. **Adaptive intake lives in `GiveawayService`**: a rolling entries-per-minute rate switches
   between Live (immediate DB write per entry) and Timed (concurrent queue flushed every N
   seconds) modes with hysteresis; mode changes publish an event that both the UI and chat
   announcements consume.
9. **Settings use dotted `Config` keys** (`Linking.*`, `MultiPlatform.*`, `Odds.*`,
   `Giveaway.*`) — no new settings tables. Admin UI is a new Settings category "Viewer Hub".
10. **Old giveaway deletion** reuses the existing (currently caller-less)
    `IDataService.DeleteGiveawayAsync`; blocked for Active giveaways (cancel first).

## Detailed design

### A. Link codes & unified identity

New entity `LinkCode` (VyralBot.Data\Entities): `Id`, `Code` (6-digit string), `ViewerId`,
`CreatedByUsername`, `CreatedByPlatform`, `CreatedAt`, `ExpiresAt`, `RedeemedByUsername`,
`RedeemedByPlatform`, `RedeemedAt`. Table `LinkCodes` (UNIQUE(Code), index on ViewerId).

New service `ILinkCodeService` / `LinkCodeService` (VyralBot.Core\Services\Linking\):
- `Task<(string Code, DateTime ExpiresAt)> GenerateCodeAsync(string username, string platform)` —
  resolves/creates the caller's ViewerId (`ILinkedAccountService.GetOrCreateViewerIdAsync`),
  invalidates prior unredeemed codes for that viewer, generates a cryptographically-random 6-digit
  code (collision-checked against the table), 5-minute expiry (config `Linking.CodeExpiryMinutes`).
- `Task<LinkRedeemResult> RedeemCodeAsync(string code, string username, string platform)` —
  validates (exists, unredeemed, unexpired), then merges the redeemer's identity into the code
  owner's ViewerId: if the redeemer has no ViewerId → insert `LinkedAccount` row with the code's
  ViewerId; if the redeemer has a different ViewerId → **merge** (re-point all their LinkedAccount
  rows to the code owner's ViewerId; balances stay per-platform, unified via
  `UnifiedBalanceEnabled`). Result record: `Success`, `Message`, `ViewerId`, `LinkedPlatforms`.
- `Task CleanupExpiredAsync()` — deletes codes expired > 24h (called from the scheduler service).
- Publishes `AccountsLinkedEvent { ViewerId, Username, Platform, LinkedPlatformNames, Source }`.

Linking intake (chat surface, handled by extending `CurrencyCommandService`):
- `!link` → generates a code for the caller and replies with it + instructions.
- `!link <6-digit code>` (or `!linkcode <code>`) → redeems.
- `!linked` → lists the caller's linked platforms. `!unlink <platform>` → removes that account.
- Discord DMs: a DM whose text is `link`, `!link`, or `connect` (no code) triggers code
  generation with the reply sent back to the DM channel; a DM containing a bare 6-digit code
  redeems it. Handled in `ChatViewModel` before the normal send-routing (DM replies must not go
  to the selected channel).

Discord DM plumbing: new `Task<bool> SendDmToChannelAsync(string channelId, string content,
string? embedJson = null)` on `IDiscordService` (resolves `IMessageChannel` by snowflake).
`ChatViewModel` records the DM `ChannelId` + `BotId` from `DiscordChatMessageEvent` when
`Channel == "DirectMessage"` and routes Discord replies for that origin through the new method.

Supported linking platforms (accepted in commands, validated case-insensitively): Twitch, Kick,
YouTube, Discord, TikTok, Facebook, Rumble. Old `InitiateLinkAsync`/`VerifyLinkAsync` remain for
back-compat but `!link` now uses the new service.

### B. Multi-platform points bonus

New entity `ViewerPlatformStatus`: `Id`, `Username`, `Platform`, `ViewerId?`, `IsSubscriber`,
`SubTier` (0=none,1,2,3), `SubscriberMonths`, `IsFollower`, `LastUpdatedAt`,
UNIQUE(Username, Platform). New entity `ViewerPreferences`: `Id`, `ViewerId` UNIQUE,
`MultiPlatformBonusOptIn`, `UpdatedAt`. New entity rows recorded by a new
`IViewerStatusService` / `ViewerStatusService`:
- `Task RecordSubscriptionAsync(username, platform, tier, months)` — Twitch events pass their real
  tier ("1000"→1, "2000"→2, "3000"→3, "Prime"→1); Kick/YouTube default tier 1; upsert keeps the
  highest values.
- `Task RecordFollowAsync(username, platform)`.
- `Task<int> GetBestSubTierAcrossLinkedAsync(viewerId)` (0 if none);
  `Task<bool> IsFollowerAnywhereAsync(viewerId)`; `Task<int> GetLinkedPlatformCountAsync(viewerId)`.

Recording hooks (in `ChatViewModel` event handlers): Twitch subscribe/resub (`evt.Tier`), Kick
subscribe (tier 1), YouTube membership synthetic event (tier 1; `YouTubeChatService` additionally
publishes a new `YouTubeMembershipEvent { Username, UserId, Level, IsMilestone }` instead of only
synthetic text), Twitch/Kick follow events. Chat `IsSubscriber` flags still apply per-entry as
today (belt and braces). Every upsert also publishes
`ViewerStatusUpdatedEvent { Username, Platform, SubTier, IsSubscriber, IsFollower, Source }`
(declared in `Core\Services\EventBus\Events\ViewerEvents.cs` alongside
`AccountsLinkedEvent` and `YouTubeMembershipEvent`).

New `IMultiPlatformBonusService` / `MultiPlatformBonusService`:
- `Task<double> GetMultiplierAsync(string username, string platform)` — resolves the caller's
  ViewerId (short-TTL 5-min cache of viewerId + count), counts distinct verified platforms; if
  `MultiPlatform.Mode` = "Off" → 1.0; "Auto" → apply to all linked viewers; "OptIn" → require
  `ViewerPreferences.MultiPlatformBonusOptIn`. Multiplier from configured thresholds:
  2 platforms → `MultiPlatform.Bonus2` (default 1.25), 3 → 1.5, 4 → 2.0, 5+ → 2.5
  (config keys `MultiPlatform.Bonus2/3/4/5`).
- Hooked as one extra factor in `CurrencyService.ApplyEarnMultipliersAsync`.
- `Task SetOptInAsync(username, platform, bool optedIn)`; chat commands `!multiplatform on/off`
  (and `!mp on/off`) surface it; `!mystatus` shows linked platforms + current bonus.

### C. Giveaway odds boosts (3-tier aware)

Global settings (Config keys, defaults): `Odds.Tier1BonusEntries=1`, `Odds.Tier2BonusEntries=2`,
`Odds.Tier3BonusEntries=3`, `Odds.FollowerBonusEntries=1`,
`Odds.LinkedPlatformBonusEntries=1` per extra linked platform beyond the first (cap
`Odds.LinkedPlatformBonusCap=3`).

`Giveaway` gains a per-giveaway `OddsBoostEnabled` column (default false; UI toggle in the create
form). When enabled, `GiveawayService.EnterGiveawayAsync` computes:
`EntryCount = 1 + (existing sub bonus) + tierBonus(best linked tier) + followerBonus +
linkedPlatformBonus`, and stores a human-readable breakdown in the entry's `Message` field
(e.g. `"T3 sub (+3), follower (+1), 3 linked platforms (+2)"`). `DrawWinnersAsync` is unchanged
(already a weighted ticket pool over `EntryCount`). When `OddsBoostEnabled` is false the legacy
subscriber-only behavior is preserved exactly.

### D. Giveaway: delete, live UI, adaptive intake

**Delete**: `GiveawayService.DeleteGiveawayAsync(id)` (refuses Active — returns false with
message; callers cancel first). `GiveawayViewModel.DeleteSelectedCommand` with a
`ConfirmDialog` ("Delete '<name>' and its N entries?"), then reload; Delete button visible for
Completed/Cancelled/Pending via the existing DataTrigger pattern.

**Live events** (new records in `Core\Services\EventBus\Events\GiveawayEvents.cs`):
`GiveawayStartedEvent { GiveawayId, Title, EntryKeyword }`,
`GiveawayEntryEvent { GiveawayId, Username, Platform, TotalEntries, UniqueParticipants, IsTimed }`,
`GiveawayWinnersDrawnEvent { GiveawayId, Title, WinnersText }`,
`GiveawayCancelledEvent { GiveawayId, Title }`,
`GiveawayIntakeModeChangedEvent { GiveawayId, Mode, EntriesPerMinute }`.
`GiveawayService` publishes them (constructor now also takes `IEventBus`).

**Live UI**: `GiveawayViewModel` subscribes to all five in `InitializeAsync` (unsubscribes in
cleanup), coalesces reloads with a 1-second debounce (DispatcherTimer) so entry storms cause at
most one reload/second, and calls the existing `LoadGiveawaysAsync()` /
`LoadEntriesForSelectedGiveawayAsync()`. The view gains a live "intake mode" chip (Live/Timed)
and an entries-per-minute indicator. Winners-draw announcements to chat stay as-is.

**Adaptive intake**: `GiveawayService` keeps a rolling 60-second window of entry timestamps.
Config: `Giveaway.LiveMaxEntriesPerMinute` (default 120) switches Live→Timed;
`Giveaway.TimedResumeEntriesPerMinute` (default 60) switches back (hysteresis);
`Giveaway.TimedFlushSeconds` (default 10). Timed mode: entries go into a
`ConcurrentQueue<EntryRequest>`, deduplicated per (username,platform) at flush, written in one
batched pass, counters recomputed once, per-entry event replaced by one aggregated
`GiveawayEntryEvent`; chat confirmations suppressed. Mode transitions publish
`GiveawayIntakeModeChangedEvent`; `ChatViewModel` announces switches in chat ("⏱ High traffic —
entries are being batched every 10s"). Entries accepted while Timed are durably queued in memory
only (documented trade-off; flush timer runs on a dedicated `PeriodicTimer` loop started lazily).
The metrics registry records `Giveaway` source counters (`entries`, `entries_batched`,
`mode_switches`).

### E. Social goals — "Vyral Status" promotions

New entities: `SocialGoal { Id, Name, Description, GoalType, TargetCount, RequiredPlatforms,
RewardType, RewardValue, IsEnabled, SortOrder, CreatedAt, UpdatedAt }` and
`ViewerGoalCompletion { Id, GoalId, ViewerId, Username, Platform, CompletedAt, RewardApplied }`.
GoalType: `LinkedPlatforms` (link ≥ TargetCount distinct platforms), `FollowPlatforms` (follow on
≥ TargetCount of RequiredPlatforms), `SubAnyPlatform` (sub on ≥ TargetCount platforms),
`SubTier` (reach SubTier ≥ TargetCount on any platform). RewardType: `Currency` (RewardValue
points, awarded once via `ICurrencyService.AddCurrencyAsync`), `Multiplier` (RewardValue earned
multiplier while status held — consumed by `MultiPlatformBonusService` as an extra factor),
`Badge` (chat title only, v1).

`ISocialGoalService` / `SocialGoalService`: seeds four default goals on first run (Link 2
platforms → 500 points; Link 4 platforms → Vyral badge; Follow 3 platforms → 250 points; Sub
tier 2+ anywhere → 1000 points). Evaluates a viewer after `AccountsLinkedEvent`,
`ViewerStatusUpdatedEvent` (new record published by `ViewerStatusService` on sub/follow), and
periodically (60s) for pending viewers; idempotent completions (unique GoalId+ViewerId).
Publishes `ViewerGoalCompletedEvent { ViewerId, Username, Platform, GoalName, RewardDescription }`.
Chat: `!goals` lists enabled goals with the caller's progress; completions announced in chat by
`ChatViewModel` (once per completion).

Admin UI: Viewer Hub settings section hosts the goals editor (list + add/edit/delete, enable
toggle) and a completions count per goal.

### F. Settings — new "Viewer Hub" category (SettingsView)

Sidebar entry `ViewerHub` between Commands and Variables. ContentControl + child VM
`ViewerHubSettingsViewModel` (+ `ViewerHubView.xaml`, DataTemplate in MainWindow.xaml), sections:
1. **Account Linking** — master enable, code expiry minutes, per-platform enable list (7
   platforms), pending-code table (code, created, expires, redeem/delete buttons).
2. **Multi-Platform Bonus** — mode (Off/OptIn/Auto), bonus multipliers 2/3/4/5 platforms, live
   preview table.
3. **Giveaway Odds** — odds boost defaults (tier 1/2/3, follower, per-linked-platform, cap).
4. **Vyral Goals** — goals CRUD (name, type, target, platforms, reward type/value, enabled) with
   completion counts.
5. **Linked Viewers** — searchable table of ViewerIds → platform accounts with unlink buttons.
All keys auto-save on change (existing `_isInitialized`-guarded `OnXChanged` pattern);
keys: `Linking.Enabled`, `Linking.CodeExpiryMinutes`, `Linking.Platforms.<name>`,
`MultiPlatform.Mode`, `MultiPlatform.Bonus2/3/4/5`, `Odds.*`, `Goals.Enabled`.

### G. New platform cards (stubs)

Facebook Gaming ("Facebook") and Rumble cards added to PlatformsView + Settings → Platforms grid
using the TikTok card pattern with `isImplemented: false` in `PlatformStartupSettingsViewModel`
(TikTok already stubbed this way). `Platform` entity gains `FacebookChatEnabled` /
`FacebookChatAutoStart` / `RumbleChatEnabled` / `RumbleChatAutoStart` columns (schema-ready, no
integration service yet). Docs pages added for all three platforms.

### H. Docs & tests

AppDocs: `platforms/tiktok.md`, `platforms/facebook.md`, `platforms/rumble.md` (status: coming
soon), `chat-features/account-linking.md`, `stream-tools/vyral-goals.md`, updates to giveaway +
currency docs; all registered in `appdocs.json`.
Tests (xUnit, `tests/VyralBot.UI.Tests`): `LinkCodeServiceTests` (generation/expiry/merge),
`MultiPlatformBonusServiceTests` (mode matrix, threshold ladder),
`GiveawayOddsTests` (entry-count breakdowns), `GiveawayIntakeTests` (rate-based mode switch,
batched flush dedup) — pure-logic tests with fake data services where needed.

## Data flow summary

Discord DM "link" → `ChatMessageReceivedEvent(Platform=Discord, Channel=DirectMessage)` →
`LinkCodeService.GenerateCodeAsync` → reply via `SendDmToChannelAsync` → viewer types
`!link 123456` in Twitch chat → `CurrencyCommandService` → `RedeemCodeAsync` → `LinkedAccounts`
rows share a ViewerId → `AccountsLinkedEvent` → `SocialGoalService` evaluates goals +
`ChatViewModel` announces → next point award picks up the multi-platform multiplier;
next giveaway entry with odds boost reads `ViewerPlatformStatus` for tier/follower bonuses.

## Error handling

- Redeem failures (expired/used/unknown code) reply with a clear chat/DM message; codes are
  single-use and validated atomically (UPDATE ... WHERE RedeemedAt IS NULL guard).
- Merge conflicts (both viewers own the same platform account) are impossible —
  `UNIQUE(Username, Platform)`; re-linking an account that's already in another identity moves it
  (documented) — the most recent link wins.
- Timed-mode queue is bounded (10,000); overflow entries are rejected with a chat notice.
- All EventBus publishing stays fire-and-forget with try/catch (existing `SafePublishAsync`
  pattern in integrations; `PublishAsync` in services wrapped).
- Every new DB write keeps the existing per-call connection + upsert conventions.

## Rollout

All changes compile against the existing schema (additive `CREATE TABLE IF NOT EXISTS` /
`AddColumnIfNotExistsAsync`); first run migrates automatically. Features default to safe states:
linking enabled, multi-platform bonus Off, odds boosts off per giveaway, goals enabled with the
4 seeds, intake thresholds conservative (120/min).

---

# v2 Amendment (2026-09-05) — Linking hardening + viewer profiles

## Motivation

Codes generated in public chat are copyable: a stranger who sees a victim's code can redeem it on
their own platform account and graft themselves into the victim's identity. Removal today is also
one-sided. Additionally the host has no way to inspect a viewer from the chat window.

## Hardened linking rules

1. **One account per platform per viewer identity.** `RedeemCodeAsync` rejects any redemption
   (and any merge) that would leave the target identity with two accounts on the same platform.
   Error copy: "Your link group already has a {platform} account ({name}). Remove it first with
   !delink."
2. **Two-sided removal (`!delink`)** — removing a platform account requires proof from another
   account in the same identity plus action on the account being removed:
   - From any linked account that is NOT the one being removed (Discord DM is the natural
     channel; chat works too): `!delink <platform>` → generates a one-time Delink code
     (same 5-min expiry, `LinkCodes.Purpose='Delink'`, `TargetPlatform=<platform>`).
   - On the platform being removed: `!delink <code>` → validates same identity, target platform
     match, unexpired/unredeemed → deletes that `LinkedAccount` row and nulls its
     `ViewerPlatformStatus.ViewerId` (history preserved, aggregation stops).
   - A single-account identity cannot delink itself ("ask the streamer"); the streamer removes
     accounts from the Viewer Hub / viewer profile UI (banned-user path).
3. **Admin removal** — Viewer Hub Linked Viewers rows and the new viewer profile window both get
   per-account unlink buttons (banned users who can't type commands).

## v2.1 ruling (post-review, 2026-09-05)

The one-per-platform guard is absolute: **no account row in the target identity is exempt** (the
drafted creator-row exemption was removed — it allowed a chat-copying stranger on the same
platform as the code's creator to graft into the identity). A group member re-typing a code gets
an explicit "already part of this link group" no-op. Delink codes are rejected by the link
redemption path (Purpose check both ways), the old one-sided `!unlink` chat command was replaced
by a `!delink` pointer, and every admin unlink path now detaches `ViewerPlatformStatus.ViewerId`
so removed/banned accounts stop contributing tier/follower boosts and goal aggregates.

---

# v3 Amendment (2026-09-05) — Discord bot roles & auto-detected identity

## Motivation

With multiple Discord bots connected, every bot currently answers link DMs and there is no way to
say "this bot handles linking, that one handles giveaways". Bot name/ID must also be typed by the
user today; Discord already tells us both at connect time.

## Design

1. **Auto-detected identity** — when a Discord bot connects, the app captures
   `CurrentUser.Id` (snowflake) and `CurrentUser.Username` (+ avatar URL) and persists them on
   that bot's `DiscordBots` row (new nullable columns `ApplicationId`, `BotUsername`, `AvatarUrl`).
   UI displays them read-only, labelled "auto-detected", refreshing on `BotsChanged`.
2. **Bot roles (single-select per role)** — new `RoleLinking` / `RoleGiveaway` flags on the bot
   rows. Assigning a role to one bot clears it from all others. Unset roles resolve to the
   Primary bot (existing behavior). Role constants live in `DiscordBotRoles` (extensible list).
3. **Role consumption**:
   - *Account Linking*: Discord DM link/delink intake and replies only run on the linking bot.
     `Linking.Enabled` unchanged.
   - *Giveaway announcements*: Discord legs of giveaway announcements (start/winners/intake-mode/
     auto-end) go through the giveaway bot when set.
4. **UI** — Discord bot detail: "Identity (auto-detected)" read-only fields + "Bot roles"
   checkboxes; bots list cards show detected username + ID; Viewer Hub → Account Linking gains an
   "Account linking Discord bot" selector (same backing flags). Defaults need zero configuration:
   with no roles assigned everything behaves as today via Primary.

## Viewer profile (click any username in chat)

Clicking a username in the chat window opens a **Viewer Profile** window showing everything the
host can see: unified identity (ViewerId + all linked accounts, each with platform, name, verified
state, and an unlink button), per-platform status (sub tier, follower, months), economy (balance,
total earned/spent per platform + recent transactions), activity (watchtime, streaks, first/last
seen), Vyral goals completed, giveaway entries with results, perks, and recent event statistics.
Implementation: `IViewerProfileService` aggregate query + `ViewerProfileViewModel` +
`ViewerProfileWindow` opened from a click command in `ChatViewModel` (ChatView username becomes a
clickable button). New data access: `GetViewerGoalCompletionsByViewerIdAsync(viewerId)`,
`GetGiveawayEntriesForUserAsync(username, platform)`.
