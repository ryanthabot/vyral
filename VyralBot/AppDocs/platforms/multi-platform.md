# Multi-Platform Parity

> What actually works on each platform **today** — an honest parity sheet across Twitch, Kick, YouTube, Discord and TikTok: chat, events, commands, timers, and which surfaces a cross-platform announcement actually reaches. Read this before promising a feature "everywhere".

## Overview

Vyral connects platforms with very different depths, and the differences matter when you build on them. The parity table below is verified against the current code (sources cited per row); the short version:

- **Twitch and Kick are the full-fat platforms** — chat in/out, the whole event pipeline (follows, subs, raids, cheers/hosts, and — on Twitch — Channel Points redemptions), and everything built on top of it: [sounds](../stream-tools/sounds.md), [alerts](../stream-tools/visual-alerts.md), [currency](../stream-tools/currency.md) bonuses, auto-replies, dashboards. Twitch additionally has **full native moderation enforcement** (Helix timeout/ban/delete).
- **Discord is a full bot platform** — multiple bots, the whole command pipeline, ban+timeout enforcement in the moderation engine — and member-join/boost events exist but are intent-gated off by default ([Discord](discord.md)).
- **YouTube is chat-centric** — live chat in/out while live, memberships, and **structured Super Chats** (alert, cheer-sound family, currency like bits, statistics), plus command timers while live.
- **TikTok is not built.** Clicking its card says exactly that: "TikTok integration is coming soon — chat is not available yet" (PlatformsViewModel.cs:1772).

| Capability | Twitch | Kick | YouTube | Discord | TikTok |
|---|---|---|---|---|---|
| Chat in | ✅ WebSocket IRC | ✅ Pusher WebSocket | ✅ polling, **only while live** | ✅ gateway, every guild channel + DMs, multi-bot | ❌ |
| Chat out | ✅ | ✅ as broadcaster | ✅ only while live | ✅ per bot, to its selected channel | ❌ |
| Events (follow/sub/raid/…) | ✅ EventSub WebSocket incl. **Channel Points redemptions** | ✅ Pusher + webhooks via ngrok | ⚠️ memberships + **structured Super Chat/Super Sticker events** (alert/sound/currency/stats); no follow/raid events | ⚠️ member-join + boost events wired but **intent-gated off** | ❌ |
| Command pipeline | ✅ | ✅ | ✅ (chat commands while live) | ✅ + native slash commands + `!mod` verbs | ❌ |
| Command timers | ✅ | ✅ | ✅ while live (skipped + retried when offline) | ✅ incl. a pinned bot | ❌ |
| Moderation enforcement | ✅ **full** (Helix timeout/ban/delete) | ⚠️ notice-only | ⚠️ notice-only | ✅ ban/timeout native (delete degrades to notice) | ❌ none |
| Announcement broadcast | ✅ | ✅ | ✅ | ✅ | ❌ |
| Dashboard w/ stream preview | ✅ | ✅ | ✅ (live-page preview) | fleet DASHBOARD tab + per-bot dashboards, no preview | ❌ |

Sources: transports and events per the [Platforms overview](overview.md) table; timer platform list `CommandsViewModel.cs:190` (Twitch/Kick/Discord/YouTube) and the sender's platform switch `CommandTimerService.cs` (YouTube case sends via `IYouTubeService`, skips unstamped while not live); YouTube Super Chat publishing `YouTubeChatService.cs` + the reaction handler `ChatViewModel.OnYouTubeSuperChatAsync`; Twitch redemption pipeline `TwitchEventSubService` → `ChatViewModel.OnTwitchChannelPointsRedeemAsync` → `WorkflowTriggerService` key `rewardredeemed`; moderation levels from `ModerationPlatformParity.Table` (ModerationActionService.cs) rendered on the Moderator dashboard; announcement fan-out `ChatViewModel.cs:4484-4548`.

## User Guide

### Where each platform's features live

- **Twitch / Kick** — everything in [Chat Features](../chat-features/commands.md) and [Stream Tools](../stream-tools/sounds.md) works from their chat and events; both get [dashboards](dashboards.md) with an embedded stream preview.
- **YouTube** — chat appears in the unified [Chat Window](../chat/chat-window.md) and commands run from it while you're live. Memberships fire the subscriber alert/stats (the "New Subscriber!" alert family), and Super Chats / Super Stickers are **structured events** (`YouTubeSuperChatEvent`: amount, currency, comment) on top of their 💎 chat line — they fire the Super Chat alert, the cheer sound family, a currency bonus computed like Twitch bits (cents = micros ÷ 10 000), and a statistics row ([YouTube](youtube.md#super-chats--super-stickers)). [Command timers](../chat-features/commands.md) can now target YouTube; a tick while you're offline is skipped and retried once the broadcast starts.
- **Discord** — bots are managed on the [Discord page](discord.md); each bot gets the full command pipeline (chat commands, native `/` slash commands authored in the [flow editor](../chat-features/automation.md), `!mod` custom verbs gated by the moderator tier ladder) plus Discord-native moderation actions (kick/ban/timeout/purge/slowmode) from the same [Moderator](../architecture/moderator-guide.md) engine. Timers can target Discord and can be pinned to a specific bot. Member-join and boost events (reactions + workflow triggers `discordmemberjoined` / `discordboost`, and the per-bot [Welcome & Auto-Role](discord.md#welcome--auto-role) card) are wired but dormant until the Server Members Intent is enabled — [Discord](discord.md#member-joins--boosts-intent-gated).
- **TikTok** — nothing. The card is a shell; the card click tells you chat is not available, and no TikTok chat service exists anywhere in the codebase (PlatformsViewModel.cs:1763-1773; TiktokChatSettingsViewModel.cs:18-22). See [TikTok & OBS](tiktok-discord-obs.md) for history.

### Cross-platform announcements

One broadcast primitive fans out to **Twitch, Kick, YouTube and Discord** — always and only those four: `SendAnnouncementToConnectedPlatformsAsync` iterates exactly that array and sends to each **connected** service, skipping (with a log line) any that isn't (ChatViewModel.cs:4484-4548). It powers:

- the chat box's **announcement** send,
- account-link celebrations ("🔗 X just linked Twitch - now on 3 platforms!"),
- viewer-goal completions,
- giveaway intake-mode traffic notices,
- giveaway/poll scheduler announcements (those can be limited to chosen platforms via their allowed-platforms setting, `SendSchedulerAnnouncementAsync` at ChatViewModel.cs:4393).

The Discord leg can be routed through a specific bot — the one holding the **GiveawayAnnouncements** role — when the announcement is giveaway-related; otherwise it uses the normal send target (inside the same fan-out method). TikTok is never a target because there is no service to send through.

## How It Works (Developer)

- Every platform publishes typed events plus the platform-agnostic `ChatMessageReceivedEvent` onto the [Event Bus](../architecture/event-bus.md), so the command/auto-reply pipeline runs everywhere chat exists; per-platform event handlers in `ChatViewModel` then fire the shared reactions (sounds/alerts/currency/statistics) — implemented for the Twitch and Kick event families (redemptions included), YouTube memberships, and YouTube Super Chats.
- **The timer platform list is the contract**: the editor's `TimerPlatforms` list and the sender's platform switch stay in sync (`"Twitch", "Kick", "Discord", "YouTube"`, CommandsViewModel.cs:190); an unknown platform still lands in the default case that logs a skip.
- **Announcement routing** resolves the Discord send target through `IDiscordBotManager` (role-resolved bot → pinned → primary), which is the same routing manual chat sends use.
- **TikTok's honesty is load-bearing**: the Platforms card never opens its (dead-end) wizard; the TikTok quick-settings card is behind a coming-soon gate with an explicit comment that no chat pipeline exists (PlatformsViewModel.cs:1765-1769; TiktokChatSettingsViewModel.cs:18-22).

## Files & Code

| File | Role |
|---|---|
| [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) | Announcement fan-out (:4484-4548), YouTube membership handling (:4550+), per-platform event handlers |
| [CommandsViewModel.cs](../../src/VyralBot.UI/ViewModels/CommandsViewModel.cs) | Timer platform choices (:190) and the Discord bot scope picker |
| [CommandTimerService.cs](../../src/VyralBot.UI/Services/CommandTimerService.cs) | Timer firing + per-platform send switch (:350-383) |
| [YouTubeChatService.cs](../../src/VyralBot.Integrations/YouTube/YouTubeChatService.cs) | Polling chat + Super Chat / Super Sticker synthesis (:785+) |
| [PlatformsViewModel.cs](../../src/VyralBot.UI/ViewModels/PlatformsViewModel.cs) | TikTok coming-soon gate (:1755-1785) |
| [WorkflowTriggerService.cs](../../src/VyralBot.UI/Services/WorkflowTriggerService.cs) | Per-platform trigger matching incl. platform filters |
| [overview.md](overview.md) + per-platform pages | Transport/auth details per platform |

## Data & Settings

Per-platform connection state is one `Platforms` row each (see [Platforms overview](overview.md) and the [database reference](../reference/database-reference.md)); announcement behavior itself has no settings — it always targets the connected subset of the four platforms. Giveaway scheduler announcements honor each giveaway's allowed-platforms configuration.

## Status & Known Issues

1. **YouTube Super Chats stop short of workflow triggers** — memberships DO have a workflow trigger (their own canonical `membership` key, distinct from the follow/sub family, with `isMilestone` filterable), but Super Chats feed alerts, sounds, currency and statistics only; no `superchat`-style workflow trigger key exists yet.
2. **Twitch redemption scope is opt-in by re-auth** — connections authorized before the redemption scope was added need one re-authorize for `channel.channel_points_custom_reward_redemption.add` to be subscribed ([Twitch](twitch.md)).
3. **TikTok overclaims nowhere anymore** — the card, its click message and the chat-settings gate all say coming soon with no chat backend (PlatformsViewModel.cs:1772). The older description of a clickable four-step TikTok wizard on [TikTok & OBS](tiktok-discord-obs.md) predates this gate.
4. **Announcements skip silently-by-design** — a disconnected platform is skipped with a log warning, never queued (ChatViewModel.cs:4419, 4430, 4441, 4472 for the scheduler legs; the fan-out itself checks each service's `IsConnected`).

### Verified against (2026-11-07)

`ChatViewModel.cs` (announcement fan-out :4484-4548; YouTube membership + `OnYouTubeSuperChatAsync`; `OnTwitchChannelPointsRedeemAsync`), `CommandsViewModel.cs` (TimerPlatforms :190 incl. "YouTube"), `CommandTimerService.cs` (per-platform send switch with the YouTube case), `YouTubeChatService.cs` (special-event mapping incl. `PublishSuperChatEvent`), `TwitchEventSubService.cs` (redemption subscription + handler), `WorkflowTriggerService.cs` (`rewardredeemed` dispatch for Twitch + Kick; `discordmemberjoined` / `discordboost`), `ModerationActionService.cs` (`ModerationPlatformParity.Table` + adapter registrations in App.xaml.cs), `PlatformsViewModel.cs` (TikTok selection gate), plus the existing verified [Platforms overview](overview.md) table for transports.
