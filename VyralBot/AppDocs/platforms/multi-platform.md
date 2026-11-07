# Multi-Platform Parity

> What actually works on each platform **today** — an honest parity sheet across Twitch, Kick, YouTube, Discord and TikTok: chat, events, commands, timers, and which surfaces a cross-platform announcement actually reaches. Read this before promising a feature "everywhere".

## Overview

Vyral connects platforms with very different depths, and the differences matter when you build on them. The parity table below is verified against the current code (sources cited per row); the short version:

- **Twitch and Kick are the full-fat platforms** — chat in/out, the whole event pipeline (follows, subs, raids, cheers/hosts), and everything built on top of it: [sounds](../stream-tools/sounds.md), [alerts](../stream-tools/visual-alerts.md), [currency](../stream-tools/currency.md) bonuses, auto-replies, dashboards.
- **Discord is a full bot platform** — multiple bots, the whole command pipeline, the moderation engine — but it has no "events" in the follow/sub sense (there is nothing to follow).
- **YouTube is chat-centric** — live chat in/out while live, memberships and Super Chats (as decorated chat messages), but **not** command timers.
- **TikTok is not built.** Clicking its card says exactly that: "TikTok integration is coming soon — chat is not available yet" (PlatformsViewModel.cs:1772).

| Capability | Twitch | Kick | YouTube | Discord | TikTok |
|---|---|---|---|---|---|
| Chat in | ✅ WebSocket IRC | ✅ Pusher WebSocket | ✅ polling, **only while live** | ✅ gateway, every guild channel + DMs, multi-bot | ❌ |
| Chat out | ✅ | ✅ as broadcaster | ✅ only while live | ✅ per bot, to its selected channel | ❌ |
| Events (follow/sub/raid/…) | ✅ EventSub WebSocket | ✅ Pusher + webhooks via ngrok | ⚠️ memberships + Super Chat/Super Sticker **as chat messages**; no follow/raid events | — (n/a) | ❌ |
| Command pipeline | ✅ | ✅ | ✅ (chat commands while live) | ✅ + native slash commands + `!mod` verbs | ❌ |
| Command timers | ✅ | ✅ | ❌ (timer targets are Twitch/Kick/Discord only) | ✅ incl. a pinned bot | ❌ |
| Moderation engine | ✅ | ✅ | — | ✅ tier-gated mod commands + Discord mod actions | ❌ |
| Announcement broadcast | ✅ | ✅ | ✅ | ✅ | ❌ |
| Dashboard w/ stream preview | ✅ | ✅ | ❌ | per-bot dashboard, no preview | ❌ |

Sources: transports and events per the [Platforms overview](overview.md) table; timer platform list `CommandsViewModel.cs:156` and the sender's platform switch `CommandTimerService.cs:245-291` (Twitch/Kick/Discord cases only, unknown platform logs a skip); YouTube special-event synthesis `YouTubeChatService.cs:770-792` and membership handling `ChatViewModel.cs:4341-4371`; announcement fan-out `ChatViewModel.cs:4275-4339`.

## User Guide

### Where each platform's features live

- **Twitch / Kick** — everything in [Chat Features](../chat-features/commands.md) and [Stream Tools](../stream-tools/sounds.md) works from their chat and events; both get [dashboards](dashboards.md) with an embedded stream preview.
- **YouTube** — chat appears in the unified [Chat Window](../chat/chat-window.md) and commands run from it while you're live. Memberships fire the subscriber alert/stats (the "New Subscriber!" alert family), and Super Chats / Super Stickers arrive **as chat messages** carrying the amount and the user's comment text — they are chat text with decoration, not first-class event types (YouTubeChatService.cs:770-792). What you *cannot* have: a [command timer](../chat-features/commands.md) posting to YouTube — the timer editor simply doesn't offer it, and the timer engine would skip it as an unknown platform (CommandsViewModel.cs:156; CommandTimerService.cs:285-289).
- **Discord** — bots are managed on the [Discord page](discord.md); each bot gets the full command pipeline (chat commands, native `/` slash commands authored in the [flow editor](../chat-features/automation.md), `!mod` custom verbs gated by the moderator tier ladder) plus Discord-native moderation actions (kick/ban/timeout/purge/slowmode) from the same [Moderator](../architecture/moderator-guide.md) engine. Timers can target Discord and can be pinned to a specific bot (CommandTimerService.cs:269-284).
- **TikTok** — nothing. The card is a shell; the card click tells you chat is not available, and no TikTok chat service exists anywhere in the codebase (PlatformsViewModel.cs:1763-1773; TiktokChatSettingsViewModel.cs:18-22). See [TikTok & OBS](tiktok-discord-obs.md) for history.

### Cross-platform announcements

One broadcast primitive fans out to **Twitch, Kick, YouTube and Discord** — always and only those four: `SendAnnouncementToConnectedPlatformsAsync` iterates exactly that array and sends to each **connected** service, skipping (with a log line) any that isn't (ChatViewModel.cs:4275-4339). It powers:

- the chat box's **announcement** send,
- account-link celebrations ("🔗 X just linked Twitch - now on 3 platforms!"),
- viewer-goal completions,
- giveaway intake-mode traffic notices,
- giveaway/poll scheduler announcements (those can be limited to chosen platforms via their allowed-platforms setting, ChatViewModel.cs:4184-4273).

The Discord leg can be routed through a specific bot — the one holding the **GiveawayAnnouncements** role — when the announcement is giveaway-related; otherwise it uses the normal send target (ChatViewModel.cs:4311-4327). TikTok is never a target because there is no service to send through.

## How It Works (Developer)

- Every platform publishes typed events plus the platform-agnostic `ChatMessageReceivedEvent` onto the [Event Bus](../architecture/event-bus.md), so the command/auto-reply pipeline runs everywhere chat exists; per-platform event handlers in `ChatViewModel` then fire the shared reactions (sounds/alerts/currency/statistics) — currently implemented for the Twitch and Kick event families and YouTube memberships.
- **Parity gaps are real code paths, not bugs**: the command-timer sender's `switch` has Twitch/Kick/Discord cases and a default that logs "unknown platform" — a `TargetPlatform` of "YouTube" would land there (CommandTimerService.cs:245-291). The editor's `TimerPlatforms` list matches (`"Twitch", "Kick", "Discord"`, CommandsViewModel.cs:156).
- **Announcement routing** resolves the Discord send target through `IDiscordBotManager` (role-resolved bot → pinned → primary), which is the same routing manual chat sends use.
- **TikTok's honesty is load-bearing**: the Platforms card never opens its (dead-end) wizard; the TikTok quick-settings card is behind a coming-soon gate with an explicit comment that no chat pipeline exists (PlatformsViewModel.cs:1765-1769; TiktokChatSettingsViewModel.cs:18-22).

## Files & Code

| File | Role |
|---|---|
| [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) | Announcement fan-out (:4275-4339), YouTube membership handling (:4341-4371), per-platform event handlers |
| [CommandsViewModel.cs](../../src/VyralBot.UI/ViewModels/CommandsViewModel.cs) | Timer platform choices (:156) and the Discord bot scope picker |
| [CommandTimerService.cs](../../src/VyralBot.UI/Services/CommandTimerService.cs) | Timer firing + per-platform send switch (:245-291) |
| [YouTubeChatService.cs](../../src/VyralBot.Integrations/YouTube/YouTubeChatService.cs) | Polling chat + Super Chat / Super Sticker synthesis (:770-792) |
| [PlatformsViewModel.cs](../../src/VyralBot.UI/ViewModels/PlatformsViewModel.cs) | TikTok coming-soon gate (:1754-1785) |
| [WorkflowTriggerService.cs](../../src/VyralBot.UI/Services/WorkflowTriggerService.cs) | Per-platform trigger matching incl. platform filters |
| [overview.md](overview.md) + per-platform pages | Transport/auth details per platform |

## Data & Settings

Per-platform connection state is one `Platforms` row each (see [Platforms overview](overview.md) and the [database reference](../reference/database-reference.md)); announcement behavior itself has no settings — it always targets the connected subset of the four platforms. Giveaway scheduler announcements honor each giveaway's allowed-platforms configuration.

## Status & Known Issues

1. **YouTube command timers do not exist** — despite YouTube being a connected chat platform, timers are Twitch/Kick/Discord only; don't configure a "post every 30m" flow expecting YouTube delivery (CommandsViewModel.cs:156; CommandTimerService.cs:245-291).
2. **YouTube events are chat-shaped** — memberships alert and Super Chats arrive as text; there are no YouTube follow/raid/cheer event types (YouTubeChatService.cs:770-792; ChatViewModel.cs:4341-4371).
3. **TikTok overclaims nowhere anymore** — the card, its click message and the chat-settings gate all say coming soon with no chat backend (PlatformsViewModel.cs:1772). The older description of a clickable four-step TikTok wizard on [TikTok & OBS](tiktok-discord-obs.md) predates this gate.
4. **Announcements skip silently-by-design** — a disconnected platform is skipped with a log warning, never queued (ChatViewModel.cs:4210, 4221, 4232, 4263).

### Verified against (2026-11-07)

`ChatViewModel.cs` (announcement fan-out :4275-4339 incl. the four-platform array :4277 and Discord role routing :4311-4327; scheduler variant :4184-4273; YouTube membership :4341-4371; link/goal/intake callers :4373-4402), `CommandsViewModel.cs` (TimerPlatforms :156), `CommandTimerService.cs` (send switch :245-291, Discord BotId routing :269-284), `YouTubeChatService.cs` (special-event synthesis :770-792), `PlatformsViewModel.cs` (TikTok selection gate :1754-1785), `TiktokChatSettingsViewModel.cs` (coming-soon gate :18-22), plus the existing verified [Platforms overview](overview.md) table for transports.
