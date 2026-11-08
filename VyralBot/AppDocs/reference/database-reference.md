# Database Reference

The complete persistence map for Vyral: every table in the per-profile SQLite database, every credential/JSON file on disk, and every key in the `Configs` store — with who writes each one.

Related pages: [Data Layer](../architecture/data-layer.md), [Profiles](../getting-started/profiles.md). Reference siblings: [Command Catalog](command-catalog.md), [Variables Catalog](variables-catalog.md), [Known Issues Ledger](known-issues.md).

---

## Storage layout

Everything is portable under `appdata/` in the install root — no registry, no %APPDATA%:

```
appdata/
├── current_profile.txt                  # active profile name (plain text)
└── profiles/<Name>/
    ├── profile.json                     # metadata, PIN hash, Settings dict (AutoLogin)
    ├── data/vyralbot.db                 # per-profile SQLite database (34 tables)
    ├── twitch_credentials.json          # OAuth client id/secret (plaintext)
    ├── kick_credentials.json            # read by PlatformsViewModel (no writer in code)
    └── ngrok_credentials.json           # ngrok auth token (plaintext JSON)
```

- The schema (50 tables, 88 indexes) is created and migrated on demand by `EnsureDatabaseCreatedAsync` in [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs):3390+ — column additions use `pragma_table_info` checks + `ALTER TABLE` (for example `Commands.BotId`, added 2026-08-16).
- Every database call goes through `IDataService` → [ProfileAwareDataService.cs](../../src/VyralBot.UI/Services/ProfileAwareDataService.cs) (resolves the current profile per call; falls back to profile `temp` when none is selected) → `DapperDataService` (SQLite + Dapper). This decorator is what makes every table per-profile.
- A handful of settings are **not** in the database at all — see [Non-database storage](#non-database-storage) below.

---

## Platform and configuration tables

| Table | Purpose | Key columns | Written by |
|---|---|---|---|
| `Platforms` | One row per platform (Twitch/Kick/YouTube/TikTok/Discord): tokens, connection status, per-platform chat flags, Kick channel IDs. The Discord row reuses fields bot-token-style: `AccessToken` = bot token, `ChannelId` = the selected active channel, `Username` = bot name, `ClientId` = derived application ID — this row belongs to the **setup wizard's bot**; additional bots live in `DiscordBots` (see [Discord](../platforms/discord.md)) | `Name` (unique), `AccessToken`, `RefreshToken`, `UserId`, `ChannelId`, `ChatroomId`, `Status`, `KickChatEnabled`/`TwitchChatEnabled`/`YoutubeChatEnabled`/... and `*ChatAutoStart` per platform, `WebhookEnabled`, `WebhookEventTypes` | Web-setup wizards, settings pages, auto-connect (`PlatformConnectionService.cs:123/201/262` also force-enables chat flags on every connect), [KickLibOAuthService.cs](../../src/VyralBot.Integrations/Kick/KickLibOAuthService.cs), [DiscordService.cs](../../src/VyralBot.Integrations/Discord/DiscordService.cs), disconnect paths |
| `DiscordBots` | One row per managed Discord bot (multi-bot support, added 2026-08-16): per-bot token, last-known status, and per-bot selected channel. The first load of an empty table migrates the legacy Discord `Platforms` row into a bot named "Primary" | `Id` (PK, GUID "N" string), `Name` (unique via `IX_DiscordBots_Name`), `Token` (plaintext, matching the legacy row), `Status` ("Connected"/"Disconnected"/"Error"), `SelectedChannelId`, `CreatedAt`, `UpdatedAt` | [DiscordBotManager.cs](../../src/VyralBot.Integrations/Discord/DiscordBotManager.cs) (CRUD, start/stop status writes, migration); read by the Discord Bots page and the Commands bot-scope bar |
| `Configs` | Generic key/value config store | `Key` (PK), `Value`, `Category`, `IsEncrypted` (unused by current callers) | See the [Configs keys](#configs-keys) table below |
| `WebServerSettings` | Single-row (Id=1) web server + ngrok configuration | `Port` (default 5000), `NgrokRegion` (default "us"), `AutoStartOnLaunch`, `CurrentTunnelUrl`, `LastStarted`, `LastStopped`, `IsEnabled` (legacy column — no longer read or written by the ViewModel; unread) | [WebServerSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/WebServerSettingsViewModel.cs) (`SaveSettingsAsync`), [WebhookCoordinatorService.cs](../../src/VyralBot.Api/Services/WebhookCoordinatorService.cs) |
| `WebhookSubscriptions` | Kick webhook event subscriptions | `Platform`, `EventType`, `WebhookUrl`, `IsActive`, `BroadcasterUserId` | [WebhookCoordinatorService.cs](../../src/VyralBot.Api/Services/WebhookCoordinatorService.cs) (`EnableKickWebhooksAsync`/disable cleanup) |
| `Triggers` | Automation trigger definitions (scaffolding — **no writers, no live readers**) | `EventType`, `Platform`, `Condition`, `ActionSequence` (JSON action-id array), `CooldownSeconds`, `FireCount` | Nobody (TriggerEngine is a stub — [Known Issues](known-issues.md) KI-18) |
| `Actions` | Bot action definitions for the trigger engine (scaffolding — **no writers**) | `ActionType` (SendMessage/Delay/HttpRequest/RunScript...), `Configuration` JSON | Nobody |
| `EventLogs` | Generic event log rows (**no writers found**) | `Platform`, `EventType`, `Data`, `TriggerId` | Nobody |

## Chat tables

| Table | Purpose | Key columns | Written by |
|---|---|---|---|
| `ChatMessages` | Persisted chat history (Kick, Twitch, YouTube, and Discord) | `Platform`, `Channel`, `Username`, `Content`, `Timestamp`, `MessageId`, `BadgesJson`, `IsSystemMessage` | [DataChatMessageStore.cs](../../src/VyralBot.UI/Services/Chat/DataChatMessageStore.cs) (maps Kick, Twitch, YouTube, and Discord events both directions). Caveat: the Chat Window's Load History button reloads only Kick/Twitch/YouTube — Discord rows persist but are not merged back yet (KI-34) |
| `ChatEventSettings` | Per (Platform, EventType) display and auto-reply configuration | `Platform`, `EventType` (unique pair), `ShowInChat`, `AutoReplyEnabled`, `AutoReplyMessage`, `SoundEnabled`/`SoundFile`/`SoundVolume` (columns exist but have no UI or callers) | [TwitchChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/TwitchChatSettingsViewModel.cs), [KickChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/KickChatSettingsViewModel.cs) via [ChatEventSettingsService.cs](../../src/VyralBot.Core/Services/Chat/ChatEventSettingsService.cs) |
| `AutoReplyRules` | Conditional auto-reply rules (edited in the Conditional Rules page; evaluated only by a code path with no production caller) | `Platform`, `EventType`, `Priority`, `ConditionJson`, `ReplyTemplate`, `TriggerCount` | [ConditionalRulesViewModel.cs](../../src/VyralBot.UI/ViewModels/ConditionalRulesViewModel.cs) |
| `EventStatistics` | Per-event history rows feeding Event History and Statistics | `Platform`, `EventType`, `Username`, `EventData` JSON, `Timestamp`, `WasAutoReplied` | The 13 event handlers in [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) via `SaveEventStatisticAsync` (`EventData` = handler variable dict JSON; accurate `WasAutoReplied`). Read by [StatisticsService.cs](../../src/VyralBot.Core/Services/Statistics/StatisticsService.cs) and [EventHistoryViewModel.cs](../../src/VyralBot.UI/ViewModels/EventHistoryViewModel.cs). Caveat: 6 ShowInChat-gated handlers skip recording when Show-in-chat is off — [Known Issues](known-issues.md) KI-26 |
| `Quotes` | The `!quote` database (added 2026-11-07, marathon wave 6) | `Number` (SQLite autoincrement PK — the viewer-facing stable number), `Text`, `AddedBy` (chat username or "Manual"), `Platform` (Twitch/Kick/YouTube/Discord or "Manual"), `CreatedAt` | [QuoteService.cs](../../src/VyralBot.Core/Services/Quotes/QuoteService.cs) (`!quote add`/`del`, chat reads) and the Commands page QUOTES tab ([CommandsViewModel.cs](../../src/VyralBot.UI/ViewModels/CommandsViewModel.cs)). Deletes do not renumber — gaps are deliberate. See [Quotes](../chat-features/quotes.md) |

## Command and variable tables

| Table | Purpose | Key columns | Written by |
|---|---|---|---|
| `Commands` | Command definitions: 21 seeded built-ins plus user-defined custom commands | `Name` (unique, case-insensitive), `Response`, `PlatformResponsesJson` (per-platform response overrides, marathon wave 7), `Permission`, `GlobalCooldown`, `UserCooldown`, `IsEnabled`, `IsBuiltIn`, `EnabledOnKick`/`EnabledOnTwitch`, `BotId` (null = every Discord bot; a `DiscordBots` row id pins the command to that bot — added 2026-08-16 via `ALTER TABLE`), `UsageCount` | Seeding at [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs):4799+; [CommandsViewModel.cs](../../src/VyralBot.UI/ViewModels/CommandsViewModel.cs) (CRUD/toggles/scope); [CommandService.cs](../../src/VyralBot.Core/Services/Commands/CommandService.cs) (usage counts, bot-scope filtering, per-platform response resolution). `GetCommandsByBotAsync` (BotId-scoped read) exists in the data layer but currently has no domain caller |
| `CommandCooldowns` | Cooldown expiry rows per (CommandId, Username or null, Platform) | `CommandId`, `Username`, `Platform`, `ExpiresAt` | [CommandService.cs](../../src/VyralBot.Core/Services/Commands/CommandService.cs) (`ApplyCooldownsAsync`) |
| `CommandTimers` | Scheduled command timers (Commands page → TIMERS tab) | `Id`, `Name`, `CommandName`, `IntervalMinutes`, `MinimumMessages` (chat-activity gate; 0 = always fire), `TargetPlatform` ("Twitch"/"Kick"/"Discord"), `BotId` (Discord destination bot; null = Primary), `IsEnabled`, `LastRunAt` | [CommandTimerService.cs](../../src/VyralBot.UI/Services/CommandTimerService.cs) (30 s tick, fire + `LastRunAt` stamp) via [CommandsViewModel.cs](../../src/VyralBot.UI/ViewModels/CommandsViewModel.cs) (CRUD). The `MinimumMessages` counters themselves are in-memory, reset per platform after each fire |
| `Variables` | Template variable catalog: 19 system, 6 auto-updating, 6 social seeded | `Name` (unique), `Value`, `Category`, `IsEditable`, `IsSystem`, `IsAutoUpdating`, `ContainsNestedVariables`, `Platforms`, `LastSyncedAt` | Seeding at [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs):3583-3737; [VariablesViewModel.cs](../../src/VyralBot.UI/ViewModels/VariablesViewModel.cs) (editable rows); auto-updating values synced from webhook/API data |

## Currency family (14 tables)

| Table | Purpose | Key columns | Written by |
|---|---|---|---|
| `CurrencySettings` | Single-row economy configuration: name/symbol, watch-time and chat-activity earning, event bonus toggles/amounts, role and platform multipliers, decay, bank, transfer tax | `CurrencyName`, `CurrencySymbol`, `WatchTime*`, `ChatActivity*`, `*Bonus*`, `*Multiplier*`, `Bank*`, `TransferTaxPercent` | [CurrencyService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyService.cs) + the Currency page's Save (`SaveSettingsAsync`, cache refreshed; writes the full row incl. bank/decay — KI-29 resolved) |
| `UserCurrency` | Balances per (Username, Platform) | `Username`, `Platform` (unique pair), `Balance`, `TotalEarned`, `TotalSpent` | [CurrencyService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyService.cs) |
| `CurrencyTransactions` | Full audit ledger of every currency movement | `Username`, `Platform`, `Amount`, `TransactionType`, `Reason`, `BalanceBefore`, `BalanceAfter` | [CurrencyService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyService.cs) |
| `CurrencyRewards` | Redeemable reward store items | `Name`, `Cost`, `RequiresApproval`, `Cooldown`, `MaxRedemptions*`, `RewardTier`, `AvailableFrom`/`AvailableUntil` | [CurrencyViewModel.cs](../../src/VyralBot.UI/ViewModels/CurrencyViewModel.cs) (Rewards tab CRUD); seeded defaults |
| `CurrencyRedemptions` | Redemption queue with approval workflow | `RewardId`, `Username`, `Platform`, `Status` (Pending/Approved/Rejected/Completed), `ProcessedBy`, `UserInput` | [CurrencyRewardsService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyRewardsService.cs) |
| `CurrencyGameSettings` | Per-game limits: Slots, Duel, Heist, Roulette | `GameType` (unique), `MinBet`, `MaxBet`, `CooldownSeconds`, `HouseEdgePercent` | Seeded defaults at [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs):2749-2765 |
| `CurrencyBoosts` | Timed earn multipliers (mod `!boost`) | `Multiplier`, `DurationMinutes`, `Reason`, `Source`, `IsActive`, `Platform` | [CurrencyBoostService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyBoostService.cs) |
| `CurrencyAuctions` | Auction items and live bid state | `ItemName`, `StartingBid`, `MinBidIncrement`, `CurrentBid`, `CurrentBidder`, `EndsAt`, `Status`, `AntiSnipe*` | [CurrencyAuctionService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyAuctionService.cs) |
| `CurrencyAuctionBids` | Bid history per auction | `AuctionId`, `Username`, `Platform`, `Amount`, `BidAt` | [CurrencyAuctionService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyAuctionService.cs) |
| `UserBankAccounts` | Bank balances with interest tracking per (Username, Platform) | `BankBalance`, `TotalDeposited`, `TotalWithdrawn`, `TotalInterestEarned`, `LastInterestPayout` | [CurrencyBankService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyBankService.cs) (interest payouts run hourly via [CurrencyDecayBackgroundService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyDecayBackgroundService.cs)) |
| `ViewerStreaks` | Daily-bonus and attendance streaks per (Username, Platform) | `CurrentStreak`, `LongestStreak`, `LastDailyClaimDate`, `HasChattedThisStream` | [CurrencyService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyService.cs) (`ClaimDailyBonusAsync`) |
| `LoyaltyTiers` | Rank definitions (Newcomer through Legend, seeded) | `Name`, `MinPoints`, `EarnMultiplier`, `Icon`, `SortOrder` | Seeded defaults at [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs):2728-2747 |
| `LinkedAccounts` | Cross-platform identity links with verification | `ViewerId`, `Username`, `Platform` (unique pair), `IsVerified`, `VerifiedAt` | [LinkedAccountService.cs](../../src/VyralBot.Core/Services/Currency/LinkedAccountService.cs) |
| `ViewerPerks` | Temporary purchased perks (**no writers found**) | `Username`, `Platform`, `PerkType`, `ExpiresAt`, `IsActive` | Nobody |

## Giveaway, poll, and watchtime tables

| Table | Purpose | Key columns | Written by |
|---|---|---|---|
| `Giveaways` | Giveaway definitions and lifecycle (Pending → Active → Completed/Cancelled) | `EntryKeyword`, `Status`, `WinnerCount`, `MaxEntries`, `SubscriberBonus*`, `MinWatchtimeMinutes`, `FollowersOnly`, `SubscribersOnly`, `AllowedPlatforms`, `Winners` | [GiveawayService.cs](../../src/VyralBot.Core/Services/GiveawayService.cs) (create/start/draw/cancel) |
| `GiveawayEntries` | One row per participant per (giveaway, username, platform) | `GiveawayId`, `EntryCount`, `IsSubscriber`, `IsWinner`, `WonAt` | [GiveawayService.cs](../../src/VyralBot.Core/Services/GiveawayService.cs) (`EnterGiveawayAsync`) |
| `Polls` | Poll definitions and lifecycle; options stored as a JSON string array | `Question`, `Options` (JSON), `Status`, `AllowMultipleVotes`, `VoteResults` (JSON), `MinWatchtimeMinutes`, `AllowedPlatforms` | [PollService.cs](../../src/VyralBot.Core/Services/PollService.cs) |
| `PollVotes` | One row per (poll, username, platform) — votes move rather than duplicate when multiple-votes is on | `PollId`, `OptionIndex`, `IsSubscriber`, `IsFollower`, `WatchtimeMinutes` | [PollService.cs](../../src/VyralBot.Core/Services/PollService.cs) (`VoteAsync`) |
| `UserWatchtime` | Viewer watchtime per (Username, Platform) | `TotalMinutes`, `StreamSessionMinutes`, `LastStreamId`, `IsActive`, `LastSeenAt` | [WatchtimeService.cs](../../src/VyralBot.Core/Services/WatchtimeService.cs), incremented per-minute by [WatchtimeBackgroundService.cs](../../src/VyralBot.Core/Services/WatchtimeBackgroundService.cs) |

Table count: 7 platform/config (now including `DiscordBots`) + 4 chat + 3 commands/variables + 14 currency + 5 giveaway/poll/watchtime = **34 tables**, matching the `CREATE TABLE IF NOT EXISTS` statements in `EnsureDatabaseCreatedAsync` (`DapperDataService.cs:2099-3030`). Older documentation sometimes says "33 tables" (pre-multi-bot) or "36 tables" — the live database contains these 34 plus SQLite's internal `sqlite_sequence`.

---

## Non-database storage

### Files on disk

| File | Contents | Written by |
|---|---|---|
| `appdata/current_profile.txt` | Active profile name, plain text | [ProfileService.cs](../../src/VyralBot.Core/Services/Profiles/ProfileService.cs) (create/switch/delete) |
| `appdata/profiles/<Name>/profile.json` | Profile metadata: display name, created/last-used timestamps, **PIN hash (salted PBKDF2-SHA256, 100k iterations, `v2.salt.hash` format; legacy unsalted hashes verify constant-time and transparently re-hash on success)**, `Settings` dictionary (`"AutoLogin"` is the only key used) | [ProfileService.cs](../../src/VyralBot.Core/Services/Profiles/ProfileService.cs); AutoLogin by [SettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/SettingsViewModel.cs) |
| `appdata/profiles/<Name>/twitch_credentials.json` | Twitch OAuth client id/secret, plaintext JSON | [TwitchOAuthService.cs](../../src/VyralBot.Integrations/Twitch/TwitchOAuthService.cs) (`SaveCredentialsAsync`) |
| `appdata/profiles/<Name>/kick_credentials.json` | Kick credentials file — **read** by [PlatformsViewModel.cs](../../src/VyralBot.UI/ViewModels/PlatformsViewModel.cs) (`:549`), no code writes it | Nobody (Kick tokens live in the `Platforms` table instead) |
| `appdata/profiles/<Name>/ngrok_credentials.json` | ngrok auth token, plaintext JSON `{"AuthToken": ...}` | [NgrokService.cs](../../src/VyralBot.Api/Services/NgrokService.cs) (`SaveAuthTokenAsync`), called by both the ngrok setup wizard **and** the Settings → API & Webhooks token editor (KI-10 resolved) |
| `logs/vyralbot-*.txt` | Serilog rolling daily logs, 7-day retention | Serilog at startup |

Note: Twitch **user** tokens (access/refresh) are DPAPI-encrypted in the `Platforms` table; Kick and YouTube tokens are stored plaintext in the same table (see [Known Issues](known-issues.md) KI-21).

### Configs keys

Key families actually used in the `Configs` table (grep of `Get/SetConfigValueAsync` across `src/`); the `IsEncrypted` column is unused by current callers, and `Category` is only set by the Support keys ("Support").

| Key | Contents | Written by |
|---|---|---|
| `AudioSettings` | JSON blob: `StreamerSoundsEnabled`, `StreamSoundsEnabled`, `MasterVolume`, `WebSocketPort` (port is pinned to 5000 by the VM — KI-16) | [AudioSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/AudioSettingsViewModel.cs) |
| `AlertSettings` | JSON blob: `AlertsEnabled`, `DurationSeconds` (1–15), `Animation` (SlideInFromTop/SlideInFromBottom/FadeIn/Bounce), `Position` (9-grid), `FollowAlertsEnabled`, `SubscribeAlertsEnabled`, `GiftSubAlertsEnabled`, `RaidAlertsEnabled`, `HostAlertsEnabled`, `CheerAlertsEnabled` | [AlertSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/AlertSettingsViewModel.cs); read by [AlertService.cs](../../src/VyralBot.UI/Services/AlertService.cs) when real alerts fire |
| `Kick.AutoRepliesEnabled` | Kick auto-replies toggle ("True"/"False") | [KickSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/KickSettingsViewModel.cs) |
| `Support.DiscordUrl` | Discord invite URL opened by the Home page's Discord support row (category "Support") | Settings → Profile → **Support Links** card ([SettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/SettingsViewModel.cs)); read by `MainViewModel.OpenDiscordLinkCommand` |
| `Support.IdeasUrl` | Ideas/suggestions board URL opened by the Home page's Ideas & Suggestions row (category "Support") | Same card (`SaveSupportLinksCommand` persists both keys) |
| `Kick.AllowTestWebhooks` | Opt-in flag (default absent/false = fail-closed) allowing the Kick webhook test-payload signature bypass; cached 60 s | Set manually in the database by operators who need Kick's test webhooks (KI-19) |
| `EventSound_<Platform>_<eventType>` | Sound file assignment per event (10 events: Kick follow/subscribe/giftsub/host; Twitch follow/subscribe/resub/giftsub/raid/cheer) | [SoundsViewModel.cs](../../src/VyralBot.UI/ViewModels/SoundsViewModel.cs) |
| `EventVolume_<Platform>_<eventType>` | Per-event volume percent | [SoundsViewModel.cs](../../src/VyralBot.UI/ViewModels/SoundsViewModel.cs) |

### Partially persisted

*(This section previously listed the `CurrencySetting` subset-save caveat — KI-29 — which is resolved: `SaveCurrencySettingsAsync` now persists every `CurrencySetting` column.)*

---

## See also

- [Data Layer](../architecture/data-layer.md) — how `ProfileAwareDataService` re-points every call per profile
- [Profiles](../getting-started/profiles.md) — the profile system end to end
- [Command Catalog](command-catalog.md) / [Variables Catalog](variables-catalog.md) — what lives in the `Commands` and `Variables` tables
- [Known Issues Ledger](known-issues.md) — every persistence gap flagged above (KI-26, KI-29)
