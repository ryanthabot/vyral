# Platform Events Reference

**Complete documentation of all event records used by the event bus**

---

## Table of Contents

1. [Overview](#overview)
2. [Base Types](#base-types)
3. [Twitch Events](#twitch-events)
4. [Kick Events](#kick-events)
5. [System Events](#system-events)

---

## Overview

All platform events are immutable `record` types that inherit from `EventBase`, which implements `IEvent`. Events are published to the `IEventBus` and consumed by the trigger engine, chat UI, statistics tracker, and other subscribers.

**Total event files:** 20 (9 Twitch + 8 Kick + 3 System)

---

## Base Types

**File:** `src/VyralBot.Core/Services/EventBus/IEvent.cs`
**Namespace:** `VyralBot.Core.Services.EventBus`

### IEvent interface

| Property | Type | Description |
|----------|------|-------------|
| EventId | `Guid` | Unique identifier for this event instance |
| Timestamp | `DateTime` | When the event was created |
| Source | `string` | Origin (e.g. "Twitch", "Kick", "System") |
| Metadata | `Dictionary<string, object>?` | Optional metadata |

### EventBase abstract record

All events inherit from this. Provides default implementations for `IEvent`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| EventId | `Guid` | `Guid.NewGuid()` | Auto-generated event ID |
| Timestamp | `DateTime` | `DateTime.UtcNow` | Auto-set creation time |
| Source | `string` | **required** | Must be set by each event |
| Metadata | `Dictionary<string, object>?` | `null` | Optional metadata |

---

## Twitch Events

**Directory:** `src/VyralBot.Integrations/Twitch/Events/`
**Namespace:** `VyralBot.Integrations.Twitch.Events`

All Twitch events are `record : EventBase` with `Source` set to `"Twitch"`.

---

### TwitchChatMessageEvent

**File:** `TwitchChatMessageEvent.cs`

Fired when a chat message is received from Twitch.

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| Channel | `string` | **required** | — | Channel name |
| Username | `string` | **required** | — | Username |
| UserId | `string` | **required** | — | Platform user ID |
| Message | `string` | **required** | — | Message text |
| DisplayName | `string` | **required** | — | Display name |
| Color | `string?` | — | `null` | Chat color hex |
| IsSubscriber | `bool` | — | `false` | Is subscriber |
| IsModerator | `bool` | — | `false` | Is moderator |
| IsBroadcaster | `bool` | — | `false` | Is broadcaster |
| IsVip | `bool` | — | `false` | Is VIP |
| SubscriberMonths | `int` | — | `0` | Subscriber months |
| Badges | `Dictionary<string, string>` | — | `new()` | Badge dictionary |
| Emotes | `List<string>` | — | `new()` | Emote IDs |

---

### TwitchFollowEvent

**File:** `TwitchFollowEvent.cs`

Fired when a user follows the channel.

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| Username | `string` | **required** | Username |
| UserId | `string` | **required** | Platform user ID |
| DisplayName | `string` | **required** | Display name |

---

### TwitchSubscribeEvent

**File:** `TwitchSubscribeEvent.cs`

Fired when a user subscribes to the channel.

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| Username | `string` | **required** | — | Username |
| UserId | `string` | **required** | — | Platform user ID |
| DisplayName | `string` | **required** | — | Display name |
| Tier | `string` | **required** | — | Sub tier: "1000", "2000", "3000" |
| IsGift | `bool` | — | `false` | Whether this sub was gifted |
| MonthsTotal | `int` | — | `0` | Total months subscribed |
| StreakMonths | `int` | — | `0` | Consecutive months |
| Message | `string?` | — | `null` | Sub message |

---

### TwitchResubEvent

**File:** `TwitchResubEvent.cs`

Fired when a user resubscribes with a message.

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| Username | `string` | **required** | Username |
| UserId | `string` | **required** | Platform user ID |
| Tier | `string` | **required** | Sub tier: "1000", "2000", "3000", "Prime" |
| CumulativeMonths | `int` | **required** | Total months subscribed |
| StreakMonths | `int` | **required** | Consecutive months |
| DurationMonths | `int` | **required** | Duration of this resub |
| Message | `string?` | — | Resub message |
| ResubscribedAt | `DateTime` | **required** | When re-subscribed |

---

### TwitchGiftSubEvent

**File:** `TwitchGiftSubEvent.cs`

Fired when a user gifts subscriptions to the community.

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| GifterUsername | `string` | **required** | Gifter's username |
| GifterUserId | `string` | **required** | Gifter's platform user ID |
| Tier | `string` | **required** | Sub tier: "1000", "2000", "3000" |
| Total | `int` | **required** | Number gifted in this event |
| CumulativeTotal | `int` | **required** | All-time total gifted |
| IsAnonymous | `bool` | **required** | Whether gifter is anonymous |
| GiftedAt | `DateTime` | **required** | When gifted |

---

### TwitchRaidEvent

**File:** `TwitchRaidEvent.cs`

Fired when a channel receives a raid from another broadcaster.

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| FromBroadcasterUsername | `string` | **required** | Raiding broadcaster's username |
| FromBroadcasterUserId | `string` | **required** | Raiding broadcaster's user ID |
| ToBroadcasterUsername | `string` | **required** | Target broadcaster's username |
| ToBroadcasterUserId | `string` | **required** | Target broadcaster's user ID |
| Viewers | `int` | **required** | Number of viewers in the raid |
| RaidedAt | `DateTime` | **required** | When the raid occurred |

---

### TwitchCheerEvent

**File:** `TwitchCheerEvent.cs`

Fired when a user cheers bits in chat.

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| Username | `string` | **required** | — | Username |
| UserId | `string` | **required** | — | Platform user ID |
| Bits | `int` | **required** | — | Number of bits cheered |
| IsAnonymous | `bool` | **required** | — | Whether cheerer is anonymous |
| Message | `string?` | — | `null` | Cheer message |
| CheeredAt | `DateTime` | **required** | — | When the cheer occurred |

---

### TwitchChannelPointsEvent

**File:** `TwitchChannelPointsEvent.cs`

Fired when a user redeems channel points.

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| RewardId | `string` | **required** | — | Reward unique ID |
| RewardTitle | `string` | **required** | — | Reward display title |
| RewardCost | `int` | **required** | — | Point cost |
| Username | `string` | **required** | — | Username |
| UserId | `string` | **required** | — | Platform user ID |
| DisplayName | `string` | **required** | — | Display name |
| UserInput | `string?` | — | `null` | User-provided text |
| Status | `string?` | — | `null` | "unfulfilled", "fulfilled", "canceled" |

---

### TwitchChannelPointsRedeemEvent

**File:** `TwitchChannelPointsRedeemEvent.cs`

Fired when a viewer redeems a channel points reward (webhook variant).

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| Username | `string` | **required** | Username |
| UserId | `string` | **required** | Platform user ID |
| RewardId | `string` | **required** | Reward unique ID |
| RewardTitle | `string` | **required** | Reward display title |
| RewardCost | `int` | **required** | Point cost |
| UserInput | `string?` | — | User-provided text |
| Status | `string` | **required** | "unfulfilled", "fulfilled", "canceled" |
| RedeemedAt | `DateTime` | **required** | When redeemed |

---

## Kick Events

**Directory:** `src/VyralBot.Integrations/Kick/Events/`
**Namespace:** `VyralBot.Integrations.Kick.Events`

All Kick events are `record : EventBase` with `Source` set to `"Kick"`.

---

### KickChatMessageEvent

**File:** `KickChatMessageEvent.cs`

Fired when a chat message is received from Kick.

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| Channel | `string` | **required** | — | Channel name |
| Username | `string` | **required** | — | Username |
| UserId | `string` | **required** | — | Platform user ID |
| Message | `string` | **required** | — | Message text |
| DisplayName | `string` | **required** | — | Display name |
| Color | `string?` | — | `null` | Chat color hex |
| IsSubscriber | `bool` | — | `false` | Is subscriber |
| IsModerator | `bool` | — | `false` | Is moderator |
| IsOwner | `bool` | — | `false` | Is channel owner |
| IsVerified | `bool` | — | `false` | Is verified user |
| SubscriberMonths | `int?` | — | `null` | Subscriber months |
| Badges | `Dictionary<string, string>` | — | `new()` | Badge dictionary |
| MessageId | `string?` | — | `null` | Platform message ID |
| ProfilePicture | `string?` | — | `null` | User's profile picture URL |
| ChannelSlug | `string?` | — | `null` | Channel slug |
| CreatedAt | `DateTime?` | — | `null` | Message creation timestamp |
| Emotes | `List<KickEmote>` | — | `new()` | Emote data (see helper records below) |
| ReplyTo | `KickReplyInfo?` | — | `null` | Reply context (see helper records below) |

**Helper records:**

- **KickEmote** — `required string EmoteId`, `List<EmotePosition> Positions = new()`
- **EmotePosition** — `int Start`, `int End`
- **KickReplyInfo** — `required string MessageId`, `required string Content`, `required string Username`, `required string UserId`

---

### KickFollowEvent

**File:** `KickFollowEvent.cs`

Fired when a user follows the Kick channel.

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| Username | `string` | **required** | — | Username |
| UserId | `string` | **required** | — | Platform user ID |
| FollowerCount | `int` | **required** | — | Current follower count |
| FollowedAt | `DateTime` | — | `DateTime.UtcNow` | When the follow occurred |

---

### KickSubscribeEvent

**File:** `KickSubscribeEvent.cs`

Fired when a user subscribes to the Kick channel.

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| Username | `string` | **required** | — | Username |
| UserId | `string` | **required** | — | Platform user ID |
| MonthsSubscribed | `int` | **required** | — | Total months subscribed |
| Message | `string?` | — | `null` | Sub message |
| SubscribedAt | `DateTime` | — | `DateTime.UtcNow` | When subscribed |

---

### KickGiftSubEvent

**File:** `KickGiftSubEvent.cs`

Fired when a user gifts subscriptions to other users.

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| GifterUsername | `string` | **required** | — | Gifter's username |
| GifterUserId | `string` | **required** | — | Gifter's platform user ID |
| Count | `int` | **required** | — | Number of subs gifted |
| RecipientUsernames | `List<string>` | — | `new()` | Recipients |
| GiftedAt | `DateTime` | — | `DateTime.UtcNow` | When gifted |

---

### KickHostEvent

**File:** `KickHostEvent.cs`

Fired when a user hosts the Kick channel.

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| HostUsername | `string` | **required** | — | Hosting user's username |
| HostUserId | `string` | **required** | — | Hosting user's ID |
| ViewerCount | `int` | — | `0` | Number of viewers |
| HostedAt | `DateTime` | — | `DateTime.UtcNow` | When hosted |

---

### KickKicksGiftedEvent

**File:** `KickKicksGiftedEvent.cs`

Fired when a user gifts Kicks (Kick's virtual currency) to the streamer.

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| SenderUsername | `string` | **required** | — | Sender's username |
| SenderUserId | `string` | **required** | — | Sender's platform user ID |
| Amount | `int` | **required** | — | Number of Kicks gifted |
| Message | `string?` | — | `null` | Optional message |
| GiftedAt | `DateTime` | — | `DateTime.UtcNow` | When gifted |

---

### KickRewardRedeemedEvent

**File:** `KickRewardRedeemedEvent.cs`

Fired when a user redeems a channel reward.

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| Username | `string` | **required** | — | Username |
| UserId | `string` | **required** | — | Platform user ID |
| RewardName | `string` | **required** | — | Reward name |
| Cost | `int` | **required** | — | Point cost |
| UserInput | `string?` | — | `null` | User-provided text |
| RedeemedAt | `DateTime` | — | `DateTime.UtcNow` | When redeemed |

---

### KickStreamStatusEvent

**File:** `KickStreamStatusEvent.cs`

Fired when stream status changes (goes live or offline).

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| IsLive | `bool` | **required** | — | Whether stream is live |
| Title | `string` | **required** | — | Stream title |
| Category | `string?` | — | `null` | Stream category |
| ViewerCount | `int?` | — | `null` | Current viewer count |
| StatusChangedAt | `DateTime` | — | `DateTime.UtcNow` | When status changed |

---

## System Events

**File:** `src/VyralBot.Core/Services/EventBus/ProfileSwitchedEvent.cs`
**Namespace:** `VyralBot.Core.Services.EventBus`

Internal events for profile and platform state changes. All inherit from `EventBase`.

---

### ProfileSwitchedEvent

Fired when the active profile is switched. Sets `Source = "System"` in constructor.

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| OldProfileName | `string` | **required** | Previous profile name |
| NewProfileName | `string` | **required** | New profile name |

---

### PlatformChatRoutingChangedEvent

Fired when a platform's chat routing is enabled or disabled.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| Platform | `string` | `""` | Platform name |
| Enabled | `bool` | `false` | Whether chat routing is enabled |

---

### PlatformConnectionChangedEvent

Fired when a platform connects or disconnects.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| Platform | `string` | `""` | Platform name |
| Connected | `bool` | `false` | Whether platform is connected |
| Channel | `string?` | `null` | Channel name |
| BotUsername | `string?` | `null` | Bot username on the platform |
