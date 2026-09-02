# Entity & Model Reference

**Complete documentation of all data entities and core models**

---

## Table of Contents

1. [Overview](#overview)
2. [Core Models (VyralBot.Core.Models)](#core-models)
3. [Data Entities (VyralBot.Data.Entities)](#data-entities)
4. [Rule Models (VyralBot.Core.Models.Rules)](#rule-models)

---

## Overview

VyralBot uses two layers of models:

- **Core Models** (`VyralBot.Core.Models`) — runtime objects that never touch the database directly (ActionContext, Profile, RuleCondition).
- **Data Entities** (`VyralBot.Data.Entities`) — SQLite-backed POCOs managed via Dapper. One database per profile.

**Total files documented:** 26 (2 core models + 1 rules file + 23 data entities)

---

## Core Models

### ActionContext

**File:** `src/VyralBot.Core/Models/ActionContext.cs`
**Namespace:** `VyralBot.Core.Models`

Provides execution context for trigger actions. Pure data class with no external dependencies.

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| ContextId | `Guid` | — | `Guid.NewGuid()` | Unique execution instance ID |
| TriggerId | `Guid` | **required** | — | ID of the trigger that initiated execution |
| Variables | `Dictionary<string, object>` | — | `new()` | Variables shared between actions |
| CancellationToken | `CancellationToken` | — | `default` | Token for graceful termination |
| Platform | `string?` | — | `null` | Source platform (e.g. "Twitch", "Kick") |
| Username | `string?` | — | `null` | User who triggered the event |

**Methods:**

| Method | Signature | Description |
|--------|-----------|-------------|
| GetVariable | `T? GetVariable<T>(string key)` | Type-safe variable lookup; returns `default(T)` if missing |
| SetVariable | `void SetVariable(string key, object value)` | Sets/overwrites a variable in the dictionary |

> Full deep-dive: [action-context-model.md](action-context-model.md)

---

### Profile

**File:** `src/VyralBot.Core/Models/Profile.cs`
**Namespace:** `VyralBot.Core.Models`

Represents a user profile with isolated settings and connections.

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| Name | `string` | **required** | — | Profile name (directory name) |
| DisplayName | `string?` | — | `null` | Human-readable display name |
| CreatedAt | `DateTime` | — | `DateTime.UtcNow` | When the profile was created |
| LastUsedAt | `DateTime` | — | `DateTime.UtcNow` | Last time the profile was activated |
| Description | `string?` | — | `null` | Optional description |
| PinCode | `string?` | — | `null` | Optional PIN for profile protection (4-6 digits) |
| Settings | `Dictionary<string, string>` | — | `new()` | Profile-specific settings (JSON serialized) |

---

## Data Entities

All entities below live in `src/VyralBot.Data/Entities/` under the namespace `VyralBot.Data.Entities`.

---

### Platform

**File:** `Platform.cs`

Represents a connected streaming platform (Twitch, YouTube, Kick, TikTok, Discord).

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| Id | `Guid` | — | `Guid.NewGuid()` | Primary key |
| Name | `string` | **required** | — | Platform name |
| Enabled | `bool` | — | `true` | Whether platform is active |
| AccessToken | `string?` | — | `null` | Encrypted access token |
| RefreshToken | `string?` | — | `null` | Encrypted refresh token |
| TokenExpiresAt | `DateTime?` | — | `null` | Token expiration (UTC) |
| Username | `string?` | — | `null` | Connected username |
| UserId | `string?` | — | `null` | Platform user ID |
| ChannelId | `string?` | — | `null` | Channel ID (Kick WebSocket) |
| ChatroomId | `string?` | — | `null` | Chatroom ID (Kick WebSocket chat) |
| ClientId | `string?` | — | `null` | OAuth client ID |
| ClientSecret | `string?` | — | `null` | Encrypted client secret |
| Status | `string` | — | `"Disconnected"` | Connection status |
| LastError | `string?` | — | `null` | Last error message |
| ConnectedAt | `DateTime?` | — | `null` | When platform connected |
| KickChatEnabled | `bool` | — | `false` | Enable Kick chat receiving |
| KickChatAutoStart | `bool` | — | `false` | Auto-start Kick chat on launch |
| TwitchChatEnabled | `bool` | — | `false` | Enable Twitch chat receiving |
| TwitchChatAutoStart | `bool` | — | `false` | Auto-start Twitch chat on launch |
| YoutubeChatEnabled | `bool` | — | `false` | Enable YouTube chat receiving |
| YoutubeChatAutoStart | `bool` | — | `false` | Auto-start YouTube chat on launch |
| TiktokChatEnabled | `bool` | — | `false` | Enable TikTok chat receiving |
| TiktokChatAutoStart | `bool` | — | `false` | Auto-start TikTok chat on launch |
| DiscordChatEnabled | `bool` | — | `false` | Enable Discord chat receiving |
| DiscordChatAutoStart | `bool` | — | `false` | Auto-start Discord chat on launch |
| CreatedAt | `DateTime` | — | `DateTime.UtcNow` | Record creation timestamp |
| UpdatedAt | `DateTime` | — | `DateTime.UtcNow` | Last update timestamp |

---

### Trigger

**File:** `Trigger.cs`

Defines when to execute actions — event matching, conditions, cooldowns, and permissions.

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| Id | `Guid` | — | `Guid.NewGuid()` | Primary key |
| Name | `string` | **required** | — | Display name |
| Description | `string?` | — | `null` | Help text |
| Enabled | `bool` | — | `true` | Active flag |
| EventType | `string` | **required** | — | Event discriminator (e.g. "ChatMessage", "Follow") |
| Platform | `string?` | — | `null` | Platform filter (null = all) |
| Condition | `string?` | — | `null` | JSON/regex condition expression |
| CooldownSeconds | `int` | — | `0` | Cooldown between fires (0 = none) |
| LastFiredAt | `DateTime?` | — | `null` | Last execution timestamp |
| FireCount | `int` | — | `0` | Total times fired |
| Permission | `string` | — | `"Everyone"` | Required permission level |
| ActionSequence | `string` | **required** | `"[]"` | JSON array of action IDs |
| CreatedAt | `DateTime` | — | `DateTime.UtcNow` | Record creation |
| UpdatedAt | `DateTime` | — | `DateTime.UtcNow` | Last update |

> Full deep-dive: [trigger-and-action-entities.md](trigger-and-action-entities.md)

---

### BotAction

**File:** `BotAction.cs`

Defines what to execute — action type, JSON configuration, timeout, and retry settings. Named `BotAction` to avoid conflict with `System.Action`.

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| Id | `Guid` | — | `Guid.NewGuid()` | Primary key |
| Name | `string` | **required** | — | Display name |
| Description | `string?` | — | `null` | Help text |
| ActionType | `string` | **required** | — | Action discriminator (e.g. "SendMessage", "Delay") |
| Configuration | `string` | **required** | `"{}"` | JSON configuration object |
| TimeoutSeconds | `int` | — | `30` | Max execution time (0 = default) |
| RetryOnFailure | `bool` | — | `false` | Auto-retry on transient failure |
| MaxRetries | `int` | — | `3` | Maximum retry attempts |
| CreatedAt | `DateTime` | — | `DateTime.UtcNow` | Record creation |
| UpdatedAt | `DateTime` | — | `DateTime.UtcNow` | Last update |

> Full deep-dive: [trigger-and-action-entities.md](trigger-and-action-entities.md)

---

### Config

**File:** `Config.cs`

Key-value configuration storage. **No Id column** — `Key` is the primary identifier.

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| Key | `string` | **required** | — | Configuration key (primary identifier) |
| Value | `string` | **required** | — | Configuration value |
| Category | `string?` | — | `null` | Grouping category |
| IsEncrypted | `bool` | — | `false` | Whether value is encrypted |
| UpdatedAt | `DateTime` | — | `DateTime.UtcNow` | Last update timestamp |

---

### EventLog

**File:** `EventLog.cs`

Audit log of all events received by the system.

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| Id | `Guid` | — | `Guid.NewGuid()` | Primary key |
| Platform | `string` | **required** | — | Source platform |
| EventType | `string` | **required** | — | Event discriminator |
| Username | `string?` | — | `null` | User who triggered the event |
| UserId | `string?` | — | `null` | Platform user ID |
| Data | `string?` | — | `null` | JSON payload of the event |
| TriggerId | `Guid?` | — | `null` | Matched trigger ID (if any) |
| TriggerFired | `bool` | — | `false` | Whether a trigger was fired |
| Timestamp | `DateTime` | — | `DateTime.UtcNow` | When the event occurred |

> Full deep-dive: [trigger-and-action-entities.md](trigger-and-action-entities.md)

---

### ChatMessage

**File:** `ChatMessage.cs`

Persisted chat message from any platform. Uses `int` for boolean-like fields (SQLite convention).

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| Id | `Guid` | — | Primary key |
| Platform | `string` | `""` | Source platform |
| Channel | `string` | `""` | Channel name |
| MessageId | `string?` | `null` | Platform message ID |
| UserId | `string?` | `null` | Platform user ID |
| Username | `string?` | `null` | Username |
| DisplayName | `string?` | `null` | Display name |
| Content | `string?` | `null` | Message text |
| Timestamp | `DateTime` | — | When the message was sent |
| Color | `string?` | `null` | User's chat color |
| IsModerator | `int` | `0` | 1 if moderator |
| IsSubscriber | `int` | `0` | 1 if subscriber |
| IsOwner | `int` | `0` | 1 if channel owner |
| BadgesJson | `string?` | `null` | JSON badges array |
| IsSystemMessage | `int` | `0` | 1 if system-generated event (follow, sub, raid, etc.) |
| CreatedAt | `DateTime` | — | Record creation |

---

### ChatEventSetting

**File:** `ChatEventSetting.cs`

Per-platform, per-event-type settings controlling visibility, auto-reply, and sound notifications.

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| Id | `Guid` | — | `Guid.NewGuid()` | Primary key |
| Platform | `string` | **required** | — | "Kick" or "Twitch" |
| EventType | `string` | **required** | — | "Follow", "Subscribe", "GiftSub", "Raid", etc. |
| ShowInChat | `bool` | — | `true` | Show event in chat window |
| AutoReplyEnabled | `bool` | — | `false` | Send auto-reply when event occurs |
| AutoReplyMessage | `string?` | — | `null` | Reply template (supports {username}, {count}, etc.) |
| SoundEnabled | `bool` | — | `true` | Play sound on event |
| SoundFile | `string?` | — | `null` | Sound file name (e.g. "follow.wav") |
| SoundVolume | `int` | — | `100` | Volume 0-100 |
| CreatedAt | `DateTime` | — | `DateTime.UtcNow` | Record creation |
| UpdatedAt | `DateTime` | — | `DateTime.UtcNow` | Last update |

---

### Command

**File:** `Command.cs`

Chat command definition with per-platform enablement and cooldowns.

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| Id | `string` | **required** | — | Primary key (GUID string) |
| Name | `string` | **required** | — | Command name (e.g. "!hello") |
| Response | `string` | **required** | — | Response template |
| Permission | `string` | **required** | `"Everyone"` | Required permission level |
| GlobalCooldown | `int` | — | `0` | Global cooldown in seconds |
| UserCooldown | `int` | — | `0` | Per-user cooldown in seconds |
| IsEnabled | `bool` | — | `true` | Whether command is active |
| IsBuiltIn | `bool` | — | `false` | System-provided command |
| EnabledOnKick | `bool` | — | `true` | Active on Kick |
| EnabledOnTwitch | `bool` | — | `true` | Active on Twitch |
| UsageCount | `int` | — | `0` | Total times used |
| LastUsedAt | `DateTime?` | — | `null` | Last execution timestamp |
| CreatedAt | `DateTime` | — | `DateTime.UtcNow` | Record creation |
| UpdatedAt | `DateTime` | — | `DateTime.UtcNow` | Last update |

---

### CommandCooldown

**File:** `CommandCooldown.cs`

Tracks active cooldowns for commands (global or per-user).

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| Id | `string` | **required** | — | Primary key |
| CommandId | `string` | **required** | — | Foreign key to Command |
| CommandName | `string` | **required** | — | Command name |
| Username | `string?` | — | `null` | Null for global cooldowns |
| Platform | `string` | **required** | — | Platform name |
| ExpiresAt | `DateTime` | — | — | When cooldown expires |
| CreatedAt | `DateTime` | — | `DateTime.UtcNow` | Record creation |

---

### AutoReplyRule

**File:** `AutoReplyRule.cs`
**Table:** `AutoReplyRules`

Conditional auto-reply rule with JSON conditions, priority ordering, and trigger tracking.

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| Id | `int` | **[Key]** | auto | Primary key (auto-increment) |
| Name | `string` | **[Required]** | `""` | Rule name (max 100 chars) |
| Platform | `string` | **[Required]** | `"All"` | Target platform (max 20 chars) |
| EventType | `string` | **[Required]** | `""` | Event type (max 50 chars) |
| IsEnabled | `bool` | — | `true` | Whether rule is active |
| Priority | `int` | — | `0` | Lower = higher priority |
| ConditionJson | `string` | **[Required]** | `"{}"` | JSON condition configuration |
| ReplyTemplate | `string` | **[Required]** | `""` | Reply template (supports variables) |
| CreatedAt | `DateTime` | — | `DateTime.UtcNow` | Record creation |
| UpdatedAt | `DateTime` | — | `DateTime.UtcNow` | Last update |
| TriggerCount | `int` | — | `0` | Times this rule has fired |
| LastTriggeredAt | `DateTime?` | — | `null` | Last fire timestamp |

---

### CurrencySetting

**File:** `CurrencySetting.cs`

Currency system configuration (single row per profile).

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| Id | `string` | `Guid.NewGuid().ToString()` | Primary key |
| CurrencyName | `string` | `"Points"` | Display name |
| CurrencySymbol | `string` | `"⭐"` | Display symbol |
| **Watch Time Rewards** | | | |
| WatchTimeEnabled | `bool` | `true` | Enable watch-time earning |
| WatchTimeInterval | `int` | `60` | Seconds between payouts |
| WatchTimeAmount | `int` | `10` | Amount per interval |
| **Chat Activity Rewards** | | | |
| ChatActivityEnabled | `bool` | `true` | Enable chat-activity earning |
| ChatActivityCooldown | `int` | `120` | Seconds between chat payouts |
| ChatActivityAmount | `int` | `5` | Amount per chat activity |
| **Event Bonuses** | | | |
| FollowBonusEnabled | `bool` | `true` | Bonus on follow |
| FollowBonusAmount | `int` | `100` | Follow bonus amount |
| SubBonusEnabled | `bool` | `true` | Bonus on subscription |
| SubBonusAmount | `int` | `500` | Sub bonus amount |
| GiftSubBonusEnabled | `bool` | `true` | Bonus on gifted sub |
| GiftSubBonusAmountPerSub | `int` | `50` | Amount per gifted sub |
| RaidBonusEnabled | `bool` | `true` | Bonus on raid |
| RaidBonusAmountPerViewer | `int` | `10` | Amount per raid viewer |
| CreatedAt | `DateTime` | `DateTime.UtcNow` | Record creation |
| UpdatedAt | `DateTime` | `DateTime.UtcNow` | Last update |

---

### UserCurrency

**File:** `UserCurrency.cs`

Per-user currency balance tracked by username and platform.

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| Id | `string` | — | `Guid.NewGuid().ToString()` | Primary key |
| Username | `string` | **required** | — | Username |
| Platform | `string` | **required** | — | Platform name |
| Balance | `int` | — | `0` | Current balance |
| TotalEarned | `int` | — | `0` | Lifetime earned |
| TotalSpent | `int` | — | `0` | Lifetime spent |
| LastWatchTimeUpdate | `DateTime?` | — | `null` | Last watch-time payout |
| LastChatActivityUpdate | `DateTime?` | — | `null` | Last chat-activity payout |
| CreatedAt | `DateTime` | — | `DateTime.UtcNow` | Record creation |
| UpdatedAt | `DateTime` | — | `DateTime.UtcNow` | Last update |

---

### CurrencyTransaction

**File:** `CurrencyTransaction.cs`

Audit log for currency balance changes.

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| Id | `string` | — | `Guid.NewGuid().ToString()` | Primary key |
| Username | `string` | **required** | — | Username |
| Platform | `string` | **required** | — | Platform name |
| Amount | `int` | — | `0` | Transaction amount |
| TransactionType | `string` | **required** | — | "Earn", "Spend", "Transfer", "Admin" |
| Reason | `string?` | — | `null` | Human-readable reason |
| BalanceBefore | `int` | — | `0` | Balance before transaction |
| BalanceAfter | `int` | — | `0` | Balance after transaction |
| Timestamp | `DateTime` | — | `DateTime.UtcNow` | When the transaction occurred |
| CreatedAt | `DateTime` | — | `DateTime.UtcNow` | Record creation |

---

### CurrencyReward

**File:** `CurrencyReward.cs`

Redeemable reward that costs currency.

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| Id | `string` | — | `Guid.NewGuid().ToString()` | Primary key |
| Name | `string` | **required** | — | Reward name |
| Description | `string?` | — | `null` | Reward description |
| Cost | `int` | — | `0` | Currency cost to redeem |
| IsEnabled | `bool` | — | `true` | Whether reward is available |
| RequiresApproval | `bool` | — | `false` | Needs manual approval |
| Cooldown | `int` | — | `0` | Cooldown in seconds |
| MaxRedemptionsPerUser | `int` | — | `0` | Per-user max (0 = unlimited) |
| MaxRedemptionsTotal | `int` | — | `0` | Global max (0 = unlimited) |
| CurrentRedemptions | `int` | — | `0` | Current total redemptions |
| Category | `string?` | — | `null` | Grouping category |
| Prompt | `string?` | — | `null` | User input prompt |
| CreatedAt | `DateTime` | — | `DateTime.UtcNow` | Record creation |
| UpdatedAt | `DateTime` | — | `DateTime.UtcNow` | Last update |

---

### CurrencyRedemption

**File:** `CurrencyRedemption.cs`

Queue record for when a user redeems a reward.

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| Id | `string` | — | `Guid.NewGuid().ToString()` | Primary key |
| RewardId | `string` | **required** | — | Foreign key to CurrencyReward |
| RewardName | `string` | **required** | — | Reward name (denormalized) |
| Username | `string` | **required** | — | Redeemer username |
| Platform | `string` | **required** | — | Platform name |
| Cost | `int` | — | `0` | Points spent |
| UserInput | `string?` | — | `null` | User-provided text |
| Status | `string` | — | `"Pending"` | "Pending", "Approved", "Rejected", "Completed" |
| RedeemedAt | `DateTime` | — | `DateTime.UtcNow` | When redeemed |
| ProcessedAt | `DateTime?` | — | `null` | When processed |
| ProcessedBy | `string?` | — | `null` | Who processed it |
| Notes | `string?` | — | `null` | Processing notes |
| CreatedAt | `DateTime` | — | `DateTime.UtcNow` | Record creation |

---

### EventStatistic

**File:** `EventStatistic.cs`

Individual event occurrence for statistics and history.

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| Id | `Guid` | — | `Guid.NewGuid()` | Primary key |
| Platform | `string` | **required** | — | "Kick" or "Twitch" |
| EventType | `string` | **required** | — | "Follow", "Subscribe", "Raid", etc. |
| Username | `string` | **required** | — | User who triggered the event |
| EventData | `string?` | — | `null` | JSON data (tier, amount, count, etc.) |
| Timestamp | `DateTime` | — | `DateTime.UtcNow` | When the event occurred |
| WasAutoReplied | `bool` | — | `false` | Whether an auto-reply was sent |
| CreatedAt | `DateTime` | — | `DateTime.UtcNow` | Record creation |

---

### Giveaway

**File:** `Giveaway.cs`

Giveaway event with cross-platform participation support.

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| Id | `Guid` | — | `Guid.NewGuid()` | Primary key |
| Name | `string` | **required** | — | Display name |
| Description | `string?` | — | `null` | What's being given away |
| EntryKeyword | `string` | **required** | — | Keyword to enter (e.g. "!enter") |
| Status | `GiveawayStatus` | — | `Pending` | Current lifecycle status |
| CreatedAt | `DateTime` | — | `DateTime.UtcNow` | Record creation |
| StartedAt | `DateTime?` | — | `null` | When entries opened |
| EndedAt | `DateTime?` | — | `null` | When winner was drawn |
| ScheduledEndTime | `DateTime?` | — | `null` | Auto-end time |
| MaxEntries | `int?` | — | `null` | Max entries (null = unlimited) |
| WinnerCount | `int` | — | `1` | Number of winners |
| SubscriberBonusEnabled | `bool` | — | `false` | Subs get bonus entries |
| SubscriberBonusEntries | `int` | — | `1` | Extra entries for subs |
| MinWatchtimeMinutes | `int` | — | `0` | Min watchtime to enter (0 = none) |
| AllowedPlatforms | `string` | — | `"All"` | Comma-separated platforms |
| FollowersOnly | `bool` | — | `false` | Restrict to followers |
| SubscribersOnly | `bool` | — | `false` | Restrict to subscribers |
| Winners | `string?` | — | `null` | Comma-separated winner usernames |
| TotalEntries | `int` | — | `0` | Total entries received |
| UniqueParticipants | `int` | — | `0` | Unique participant count |
| Metadata | `string?` | — | `null` | JSON custom settings |
| UpdatedAt | `DateTime` | — | `DateTime.UtcNow` | Last update |

**GiveawayStatus enum:**

| Value | Int | Description |
|-------|-----|-------------|
| Pending | 0 | Created but not started |
| Active | 1 | Accepting entries |
| Completed | 2 | Ended, winner(s) drawn |
| Cancelled | 3 | Cancelled before completion |

---

### GiveawayEntry

**File:** `GiveawayEntry.cs`

Single entry in a giveaway by a user.

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| Id | `Guid` | — | `Guid.NewGuid()` | Primary key |
| GiveawayId | `Guid` | — | — | Foreign key to Giveaway |
| Username | `string` | **required** | — | Participant username |
| Platform | `string` | **required** | — | Entry platform |
| PlatformUserId | `string?` | — | `null` | Platform user ID |
| EnteredAt | `DateTime` | — | `DateTime.UtcNow` | Entry timestamp |
| EntryCount | `int` | — | `1` | Number of entries (1 + bonus) |
| IsSubscriber | `bool` | — | `false` | Was subscriber at entry time |
| IsFollower | `bool` | — | `false` | Was follower at entry time |
| WatchtimeMinutes | `int` | — | `0` | User's watchtime at entry |
| Message | `string?` | — | `null` | Optional entry message |
| IsWinner | `bool` | — | `false` | Selected as winner |
| WonAt | `DateTime?` | — | `null` | When winner was announced |

---

### Poll

**File:** `Poll.cs`

Multiple-choice poll with cross-platform voting support.

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| Id | `Guid` | — | `Guid.NewGuid()` | Primary key |
| Question | `string` | **required** | — | Poll question |
| Options | `string` | **required** | — | JSON array of option strings |
| Status | `PollStatus` | — | `Pending` | Current lifecycle status |
| CreatedAt | `DateTime` | — | `DateTime.UtcNow` | Record creation |
| StartedAt | `DateTime?` | — | `null` | When voting opened |
| EndedAt | `DateTime?` | — | `null` | When voting ended |
| ScheduledEndTime | `DateTime?` | — | `null` | Auto-end time |
| CreatorUsername | `string` | **required** | — | Who created the poll |
| TotalVotes | `int` | — | `0` | Total votes cast |
| AllowMultipleVotes | `bool` | — | `false` | Users can change their vote |
| AllowedPlatforms | `string` | — | `"All"` | Comma-separated platforms |
| FollowersOnly | `bool` | — | `false` | Restrict to followers |
| SubscribersOnly | `bool` | — | `false` | Restrict to subscribers |
| MinWatchtimeMinutes | `int` | — | `0` | Min watchtime to vote (0 = none) |
| VoteResults | `string?` | — | `null` | JSON vote counts per option |
| Metadata | `string?` | — | `null` | JSON custom settings |
| UpdatedAt | `DateTime` | — | `DateTime.UtcNow` | Last update |

**PollStatus enum:**

| Value | Int | Description |
|-------|-----|-------------|
| Pending | 0 | Created but not started |
| Active | 1 | Accepting votes |
| Completed | 2 | Ended |
| Cancelled | 3 | Cancelled before completion |

---

### PollVote

**File:** `PollVote.cs`

Single vote in a poll by a user.

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| Id | `Guid` | — | `Guid.NewGuid()` | Primary key |
| PollId | `Guid` | — | — | Foreign key to Poll |
| Username | `string` | **required** | — | Voter username |
| Platform | `string` | **required** | — | Voting platform |
| PlatformUserId | `string?` | — | `null` | Platform user ID |
| OptionIndex | `int` | — | `0` | Chosen option (0-based) |
| VotedAt | `DateTime` | — | `DateTime.UtcNow` | When vote was cast |
| IsSubscriber | `bool` | — | `false` | Was subscriber at vote time |
| IsFollower | `bool` | — | `false` | Was follower at vote time |
| WatchtimeMinutes | `int` | — | `0` | User's watchtime at vote |

---

### UserWatchtime

**File:** `UserWatchtime.cs`

Per-user watchtime tracking across platforms.

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| Id | `Guid` | — | `Guid.NewGuid()` | Primary key |
| Username | `string` | **required** | — | Viewer username |
| Platform | `string` | **required** | — | Platform name |
| TotalMinutes | `int` | — | `0` | All-time watchtime in minutes |
| LastSeenAt | `DateTime` | — | `DateTime.UtcNow` | Last activity timestamp |
| FirstSeenAt | `DateTime` | — | `DateTime.UtcNow` | First tracked timestamp |
| StreamSessionMinutes | `int` | — | `0` | Current session minutes (resets per stream) |
| LastStreamId | `string?` | — | `null` | Current stream session ID |
| IsActive | `bool` | — | `false` | Currently active (seen recently) |
| UpdatedAt | `DateTime` | — | `DateTime.UtcNow` | Last update |

---

### Variable

**File:** `Variable.cs`

Template variable referenced via `{variablename}` syntax in commands, messages, and templates.

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| Id | `string` | **required** | — | Primary key (GUID string) |
| Name | `string` | **required** | — | Variable name (lowercase, kebab-case) |
| DisplayName | `string` | **required** | — | Human-readable display name |
| Category | `string` | **required** | — | Grouping: Global, Twitch, Kick, Social, Custom |
| Prefix | `string?` | — | `null` | Platform prefix (e.g. "kick-", "twitch-") |
| Value | `string?` | — | `null` | Current value (null for runtime-only vars) |
| MaxLength | `int` | — | `2000` | Max character length |
| IsEditable | `bool` | — | `false` | User can edit in UI |
| IsSystem | `bool` | — | `false` | System-generated runtime variable |
| IsAutoUpdating | `bool` | — | `false` | Auto-updates from webhooks/API |
| ContainsNestedVariables | `bool` | — | `false` | Value contains nested {variable} references |
| Platforms | `string?` | — | `null` | Comma-separated platforms (null = all) |
| Description | `string?` | — | `null` | Help text |
| CreatedAt | `DateTime` | — | `DateTime.UtcNow` | Record creation |
| UpdatedAt | `DateTime` | — | `DateTime.UtcNow` | Last update |
| LastSyncedAt | `DateTime?` | — | `null` | Last auto-sync timestamp |

---

### WebServerSettings

**File:** `WebServerEntities.cs`

Embedded web server configuration (single-row table, `Id = 1`).

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| Id | `int` | `1` | Single row identifier |
| IsEnabled | `bool` | `false` | Whether web server is active |
| Port | `int` | `5000` | Listening port |
| NgrokRegion | `string` | `"us"` | Ngrok tunnel region |
| CurrentTunnelUrl | `string?` | `null` | Active ngrok tunnel URL |
| AutoStartOnLaunch | `bool` | `false` | Start server on app launch |
| LastStarted | `DateTime?` | `null` | Last start timestamp |
| LastStopped | `DateTime?` | `null` | Last stop timestamp |

---

### WebhookSubscription

**File:** `WebServerEntities.cs`

Tracks active webhook subscriptions for external platforms.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| Id | `string` | `""` | Primary key |
| Platform | `string` | `""` | Platform name |
| EventType | `string` | `""` | Event type |
| EventVersion | `int` | `1` | Event version |
| BroadcasterUserId | `string?` | `null` | Broadcaster user ID |
| WebhookUrl | `string` | `""` | Webhook callback URL |
| IsActive | `bool` | `true` | Whether subscription is active |
| LastEventReceived | `DateTime?` | `null` | Last received event timestamp |
| CreatedAt | `DateTime` | `DateTime.UtcNow` | Record creation |

---

## Rule Models

### RuleCondition

**File:** `src/VyralBot.Core/Models/Rules/RuleCondition.cs`
**Namespace:** `VyralBot.Core.Models.Rules`

Used for conditional auto-reply rule evaluation. Contains three enums and one class.

#### ConditionType enum

| Value | Description |
|-------|-------------|
| Value | Numeric/string comparisons (count == 100, username contains "test") |
| Status | Boolean checks (is_subscriber, is_moderator, is_vip) |
| Time | Time-based conditions (time_of_day, day_of_week) |
| Complex | Multiple conditions with AND/OR logic |

#### ComparisonOperator enum

| Value | Symbol | Description |
|-------|--------|-------------|
| Equals | `==` | Equality |
| NotEquals | `!=` | Inequality |
| GreaterThan | `>` | Greater than |
| LessThan | `<` | Less than |
| GreaterOrEqual | `>=` | Greater or equal |
| LessOrEqual | `<=` | Less or equal |
| Contains | — | String contains |
| StartsWith | — | String starts with |
| EndsWith | — | String ends with |
| Modulo | `%` | Modulo (for milestone checks like count % 100 == 0) |

#### LogicalOperator enum

| Value | Description |
|-------|-------------|
| And | All sub-conditions must be true |
| Or | At least one sub-condition must be true |

#### RuleCondition class

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| Type | `ConditionType` | — | Type of condition |
| Field | `string` | `""` | Field to evaluate (count, username, is_subscriber, etc.) |
| Operator | `ComparisonOperator` | — | Comparison operator |
| Value | `string` | `""` | Value to compare against |
| ModuloDivisor | `int?` | `null` | Divisor for Modulo operator |
| SubConditions | `List<RuleCondition>?` | `null` | Nested conditions (for Complex type) |
| LogicalOperator | `LogicalOperator?` | `null` | How to combine sub-conditions |

All three enums use `[JsonConverter(typeof(JsonStringEnumConverter))]` for JSON string serialization.
