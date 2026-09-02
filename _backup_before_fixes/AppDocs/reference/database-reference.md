# Database Reference

The complete persistence map for VyralBot: every table in the per-profile SQLite database, every credential/JSON file on disk, and every key in the `Configs` store — with who writes each one.

Related pages: [Data Layer](../architecture/data-layer.md), [Profiles](../getting-started/profiles.md). Reference siblings: [Command Catalog](command-catalog.md), [Variables Catalog](variables-catalog.md), [Known Issues Ledger](known-issues.md).

---

## Storage layout

Everything is portable under `appdata/` in the install root — no registry, no %APPDATA%:

```
appdata/
├── current_profile.txt                  # active profile name (plain text)
└── profiles/<Name>/
    ├── profile.json                     # metadata, PIN hash, Settings dict (AutoLogin)
    ├── data/vyralbot.db                 # per-profile SQLite database (33 tables)
    ├── twitch_credentials.json          # OAuth client id/secret (plaintext)
    ├── kick_credentials.json            # read by PlatformsViewModel (no writer in code)
    └── ngrok_credentials.json           # ngrok auth token (plaintext JSON)
```

- The schema (33 tables) is created and migrated on demand by `EnsureDatabaseCreatedAsync` in [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs):2092-3740 — column additions use `pragma_table_info` checks + `ALTER TABLE`.
- Every database call goes through `IDataService` → [ProfileAwareDataService.cs](../../src/VyralBot.UI/Services/ProfileAwareDataService.cs) (resolves the current profile per call; falls back to profile `temp` when none is selected) → `DapperDataService` (SQLite + Dapper). This decorator is what makes every table per-profile.
- A handful of settings are **not** in the database at all — see [Non-database storage](#non-database-storage) below.

---

## Platform and configuration tables

| Table | Purpose | Key columns | Written by |
|---|---|---|---|
| `Platforms` | One row per platform (Twitch/Kick/YouTube/TikTok/Discord): tokens, connection status, per-platform chat flags, Kick channel IDs | `Name` (unique), `AccessToken`, `RefreshToken`, `UserId`, `ChannelId`, `ChatroomId`, `Status`, `KickChatEnabled`/`TwitchChatEnabled`/`YoutubeChatEnabled`/... and `*ChatAutoStart` per platform, `WebhookEnabled`, `WebhookEventTypes` | Web-setup wizards, settings pages, auto-connect (`MainViewModel.cs:437-447` also force-enables chat flags), [KickLibOAuthService.cs](../../src/VyralBot.Integrations/Kick/KickLibOAuthService.cs), disconnect paths |
| `Configs` | Generic key/value config store | `Key` (PK), `Value`, `Category`, `IsEncrypted` (unused by current callers) | See the [Configs keys](#configs-keys) table below |
| `WebServerSettings` | Single-row (Id=1) web server + ngrok configuration | `Port` (default 5000), `NgrokRegion` (default "us"), `AutoStartOnLaunch`, `CurrentTunnelUrl`, `LastStarted`, `LastStopped`, `IsEnabled` (read by nothing) | [WebServerSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/WebServerSettingsViewModel.cs) (`SaveSettingsAsync`), [WebhookCoordinatorService.cs](../../src/VyralBot.Api/Services/WebhookCoordinatorService.cs) |
| `WebhookSubscriptions` | Kick webhook event subscriptions | `Platform`, `EventType`, `WebhookUrl`, `IsActive`, `BroadcasterUserId` | [WebhookCoordinatorService.cs](../../src/VyralBot.Api/Services/WebhookCoordinatorService.cs) (`EnableKickWebhooksAsync`/disable cleanup) |
| `Triggers` | Automation trigger definitions (scaffolding — **no writers, no live readers**) | `EventType`, `Platform`, `Condition`, `ActionSequence` (JSON action-id array), `CooldownSeconds`, `FireCount` | Nobody (TriggerEngine is a stub — [Known Issues](known-issues.md) KI-18) |
| `Actions` | Bot action definitions for the trigger engine (scaffolding — **no writers**) | `ActionType` (SendMessage/Delay/HttpRequest/RunScript...), `Configuration` JSON | Nobody |
| `EventLogs` | Generic event log rows (**no writers found**) | `Platform`, `EventType`, `Data`, `TriggerId` | Nobody |

## Chat tables

| Table | Purpose | Key columns | Written by |
|---|---|---|---|
| `ChatMessages` | Persisted chat history (Kick and Twitch only — YouTube events are never stored) | `Platform`, `Channel`, `Username`, `Content`, `Timestamp`, `MessageId`, `BadgesJson`, `IsSystemMessage` | [DataChatMessageStore.cs](../../src/VyralBot.UI/Services/Chat/DataChatMessageStore.cs) (maps Kick/Twitch events only) |
| `ChatEventSettings` | Per (Platform, EventType) display and auto-reply configuration | `Platform`, `EventType` (unique pair), `ShowInChat`, `AutoReplyEnabled`, `AutoReplyMessage`, `SoundEnabled`/`SoundFile`/`SoundVolume` (columns exist but have no UI or callers) | [TwitchChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/TwitchChatSettingsViewModel.cs), [KickChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/KickChatSettingsViewModel.cs) via [ChatEventSettingsService.cs](../../src/VyralBot.Core/Services/Chat/ChatEventSettingsService.cs) |
| `AutoReplyRules` | Conditional auto-reply rules (edited in the Conditional Rules page; evaluated only by a code path with no production caller) | `Platform`, `EventType`, `Priority`, `ConditionJson`, `ReplyTemplate`, `TriggerCount` | [ConditionalRulesViewModel.cs](../../src/VyralBot.UI/ViewModels/ConditionalRulesViewModel.cs) |
| `EventStatistics` | Per-event history rows feeding Event History and Statistics | `Platform`, `EventType`, `Username`, `EventData` JSON, `Timestamp`, `WasAutoReplied` | **Nobody — `SaveEventStatisticAsync` has zero callers** (KI-3). Read by [StatisticsService.cs](../../src/VyralBot.Core/Services/Statistics/StatisticsService.cs) and [EventHistoryViewModel.cs](../../src/VyralBot.UI/ViewModels/EventHistoryViewModel.cs) |

## Command and variable tables

| Table | Purpose | Key columns | Written by |
|---|---|---|---|
| `Commands` | Command definitions: 21 seeded built-ins plus user-defined custom commands | `Name` (unique, case-insensitive), `Response`, `Permission`, `GlobalCooldown`, `UserCooldown`, `IsEnabled`, `IsBuiltIn`, `EnabledOnKick`/`EnabledOnTwitch`, `UsageCount` | Seeding at [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs):3172-3581; [CommandsViewModel.cs](../../src/VyralBot.UI/ViewModels/CommandsViewModel.cs) (CRUD/toggles); [CommandService.cs](../../src/VyralBot.Core/Services/Commands/CommandService.cs) (usage counts) |
| `CommandCooldowns` | Cooldown expiry rows per (CommandId, Username or null, Platform) | `CommandId`, `Username`, `Platform`, `ExpiresAt` | [CommandService.cs](../../src/VyralBot.Core/Services/Commands/CommandService.cs) (`ApplyCooldownsAsync`) |
| `Variables` | Template variable catalog: 19 system, 6 auto-updating, 6 social seeded | `Name` (unique), `Value`, `Category`, `IsEditable`, `IsSystem`, `IsAutoUpdating`, `ContainsNestedVariables`, `Platforms`, `LastSyncedAt` | Seeding at [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs):3583-3737; [VariablesViewModel.cs](../../src/VyralBot.UI/ViewModels/VariablesViewModel.cs) (editable rows); auto-updating values synced from webhook/API data |

## Currency family (14 tables)

| Table | Purpose | Key columns | Written by |
|---|---|---|---|
| `CurrencySettings` | Single-row economy configuration: name/symbol, watch-time and chat-activity earning, event bonus toggles/amounts, role and platform multipliers, decay, bank, transfer tax | `CurrencyName`, `CurrencySymbol`, `WatchTime*`, `ChatActivity*`, `*Bonus*`, `*Multiplier*`, `Bank*`, `TransferTaxPercent` | [CurrencyService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyService.cs) — **but the Currency page's Save button does not persist** (KI-7) |
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

Table count: 6 platform/config + 4 chat + 3 commands/variables + 14 currency + 5 giveaway/poll/watchtime = **33 tables**, matching the `CREATE TABLE IF NOT EXISTS` statements in `EnsureDatabaseCreatedAsync` (`DapperDataService.cs:2099-2948`). Older documentation sometimes says "36 tables" — the live database contains these 33 plus SQLite's internal `sqlite_sequence`.

---

## Non-database storage

### Files on disk

| File | Contents | Written by |
|---|---|---|
| `appdata/current_profile.txt` | Active profile name, plain text | [ProfileService.cs](../../src/VyralBot.Core/Services/Profiles/ProfileService.cs) (create/switch/delete) |
| `appdata/profiles/<Name>/profile.json` | Profile metadata: display name, created/last-used timestamps, **PIN hash (unsalted SHA256, base64)**, `Settings` dictionary (`"AutoLogin"` is the only key used) | [ProfileService.cs](../../src/VyralBot.Core/Services/Profiles/ProfileService.cs); AutoLogin by [SettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/SettingsViewModel.cs) |
| `appdata/profiles/<Name>/twitch_credentials.json` | Twitch OAuth client id/secret, plaintext JSON | [TwitchOAuthService.cs](../../src/VyralBot.Integrations/Twitch/TwitchOAuthService.cs) (`SaveCredentialsAsync`) |
| `appdata/profiles/<Name>/kick_credentials.json` | Kick credentials file — **read** by [PlatformsViewModel.cs](../../src/VyralBot.UI/ViewModels/PlatformsViewModel.cs) (`:549`), no code writes it | Nobody (Kick tokens live in the `Platforms` table instead) |
| `appdata/profiles/<Name>/ngrok_credentials.json` | ngrok auth token, plaintext JSON `{"AuthToken": ...}` | [NgrokService.cs](../../src/VyralBot.Api/Services/NgrokService.cs) (`SaveAuthTokenAsync`), called by the ngrok setup wizard — the token textbox in Settings does not persist it (KI-10) |
| `logs/vyralbot-*.txt` | Serilog rolling daily logs, 7-day retention | Serilog at startup |

Note: Twitch **user** tokens (access/refresh) are DPAPI-encrypted in the `Platforms` table; Kick and YouTube tokens are stored plaintext in the same table (see [Known Issues](known-issues.md) KI-23).

### Configs keys

Four key families are actually used in the `Configs` table (grep of `Get/SetConfigValueAsync` across `src/`); the `IsEncrypted` and `Category` columns are unused by current callers.

| Key | Contents | Written by |
|---|---|---|
| `AudioSettings` | JSON blob: `StreamerSoundsEnabled`, `StreamSoundsEnabled`, `MasterVolume`, `WebSocketPort` (port is pinned to 5000 by the VM — KI-16) | [AudioSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/AudioSettingsViewModel.cs) |
| `Kick.AutoRepliesEnabled` | Kick auto-replies toggle ("True"/"False") | [KickSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/KickSettingsViewModel.cs) |
| `EventSound_<Platform>_<eventType>` | Sound file assignment per event (10 events: Kick follow/subscribe/giftsub/host; Twitch follow/subscribe/resub/giftsub/raid/cheer) | [SoundsViewModel.cs](../../src/VyralBot.UI/ViewModels/SoundsViewModel.cs) |
| `EventVolume_<Platform>_<eventType>` | Per-event volume percent | [SoundsViewModel.cs](../../src/VyralBot.UI/ViewModels/SoundsViewModel.cs) |

### Not persisted anywhere

- **Alert settings** — the Visual Alerts page's master toggle, duration, animation, position, and per-event switches are never loaded or saved. `InitializeAsync` and `SaveSettingsAsync` in [AlertSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/AlertSettingsViewModel.cs) are `// TODO` stubs; the Save button shows success without writing anything, and real alerts use hard-coded settings (KI-6).
- **`EventStatistics` rows** — table and write API exist, nothing calls them (KI-3).

---

## See also

- [Data Layer](../architecture/data-layer.md) — how `ProfileAwareDataService` re-points every call per profile
- [Profiles](../getting-started/profiles.md) — the profile system end to end
- [Command Catalog](command-catalog.md) / [Variables Catalog](variables-catalog.md) — what lives in the `Commands` and `Variables` tables
- [Known Issues Ledger](known-issues.md) — every persistence gap flagged above (KI-3, KI-6, KI-7, KI-10)
