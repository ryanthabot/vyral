# Event Testing

> A diagnostic console with **17 test-fires grouped by platform** (Twitch, Kick, YouTube, Discord) that publish the real event objects onto the live event bus — the full pipeline (chat line, sound, alert, currency bonus, auto-reply, statistics row, **and workflows**) runs exactly as for a live viewer. The page wears its own warning: **FIRES REAL EVENTS — WORKFLOWS REALLY EXECUTE.**

## Overview

Event Testing is a diagnostic console. Each button fabricates a realistic platform event — a follow, a subscription, a raid with 200 viewers, a **Twitch channel-points redeem**, a **YouTube Super Chat**, a **Discord server boost** — and publishes it onto the same [Event Bus](../architecture/event-bus.md) that real platform connections publish to. Every downstream handler reacts as if the event were genuine: you get the gold system message in the chat feed, the configured sound plays, the visual alert window opens, the viewer earns their currency bonus, the auto-reply template is sent (rate limiter permitting), a statistics row is written, and any [workflow](workflows.md) whose trigger matches **actually executes**. Nothing is mocked after the event itself.

This page is **fully functional** — the recommended way to exercise every event reaction without going live. Test events also land in [Event History](event-history.md) and [Statistics](../stream-tools/statistics.md) through the same handlers live events use.

Three things to know before you click:

- **The warning is literal.** Test events fire real workflows — a flow with a *raid* trigger will run its full action list (send messages, switch OBS scenes, grant roles...) when you press **Raid**. Point test-fires at a scratch [profile](../getting-started/profiles.md) if your flows have real side effects.
- **Auto-replies are rate-limited.** The same user + event + platform combination only auto-replies once every 30 seconds, so spamming one button produces one reply (see [Auto-Replies](auto-replies.md)).
- **Currency bonuses are real.** Each test event awards currency to the test user through the normal [Currency](../stream-tools/currency.md) bonus system, writing to your profile's database.

## User Guide

Open **Event Testing** in the sidebar — it is a **standalone entry below Settings**, not part of the Chat Features group.

### Inputs and feedback

- **Test Username** — the viewer name embedded in every test event (default `TestUser123`).
- **Test Message** — chat text embedded where the event carries one (default `This is a test message`).
- **Status line** — under the warning banner; updated on every fire: `FIRED Twitch Cheer (100 bits) for TestUser123 at 14:32:07 - alert, sound, statistics, currency and workflow handlers ran for real.` A failed publish turns the line into `FAILED <platform> <event> ...: <error>`.
- **Recent fires** — a newest-first log of the **last 10 fires** (platform, event, username, time); failed rows render in the error color. **CLEAR LOG** wipes both the list and the status line.

### The 17 test-fires

The buttons sit in **four platform-grouped cards**, each with the platform's icon and accent color. Payloads are fixed and realistic; the Test Username / Test Message inputs feed the user-carried fields.

**Twitch** (7 fires):

| Button | Event payload |
|---|---|
| **Follow** | `Username`/`DisplayName` = test username, `UserId` = "12345" |
| **Subscribe** | `Tier` = **"1000"**, `MonthsTotal` = **1**, `StreakMonths` = **1**, `IsGift` = false |
| **Resub** | `CumulativeMonths` = **6**, `StreakMonths` = **4**, `Tier` = "1000", `Message` = test message |
| **Gift Sub** | `GifterUsername` = test username, `Tier` = "1000", `Total` = **5**, `CumulativeTotal` = **25**, `IsAnonymous` = false |
| **Raid** | `FromBroadcasterUsername` = test username, `Viewers` = **200**, target channel "YourChannel" |
| **Cheer (100 bits)** | `Bits` = **100**, `Message` = test message, `IsAnonymous` = false |
| **Channel Points Redeem** | `RewardTitle` = **"Hydrate Reminder"**, `RewardCost` = **500**, `UserInput` = test message, status "unfulfilled" |

**Kick** (6 fires):

| Button | Event payload |
|---|---|
| **Follow** | `Username` = test username, `FollowerCount` = **42** |
| **Subscribe** | `MonthsSubscribed` = **3**, `Message` = test message |
| **Gift Sub** | `GifterUsername` = test username, `Count` = **5** subs to `Recipient1`–`Recipient5` |
| **Host** | `HostUsername` = test username, `ViewerCount` = **150** |
| **Kicks Gifted (1000)** | `SenderUsername` = test username, `Amount` = **1000** kicks, `Message` = test message |
| **Reward Redeemed** | `RewardName` = **"Test Reward"**, `Cost` = **500**, `UserInput` = test message |

**YouTube** (2 fires):

| Button | Event payload |
|---|---|
| **Super Chat ($5.00)** | `AmountMicros` = 5,000,000 (a real **$5.00**), `AmountDisplayString` = "$5.00", `Currency` = "USD", `Message` = test message |
| **Membership** | `Level` = **"Tier 1"**, `IsMilestone` = false |

**Discord** (2 fires):

| Button | Event payload |
|---|---|
| **Member Joined** | `GuildName` = "Vyral Test Server", member = test username |
| **Server Boost** | `GuildName` = "Vyral Test Server", booster = test username |

### What the five newer fires run downstream

- **Twitch Channel Points Redeem** — the "Reward Redeemed!" alert with the redemption sound, a `Twitch/RewardRedeemed` statistics row, and the **`rewardredeemed`** workflow key (the same key Kick redemptions dispatch, so one flow covers both platforms).
- **YouTube Super Chat** — the "cheer" sound key, a currency bonus of **500** (cents, from `amountMicros`), a `YouTube/Cheer` statistics row, and the dedicated **`superchat`** workflow key (separate from Twitch bits' `cheer`).
- **YouTube Membership** — the "New Subscriber!" alert, a viewer-status subscription record, and a `YouTube/Subscribe` statistics row. No workflow key dispatches for memberships (the trigger service does not subscribe to membership records) — this fire exercises reactions, not flows.
- **Discord Member Joined** — the "follow"/Discord sound, the "New Member!" alert, a `Discord/MemberJoined` statistics row, and the **`discordmemberjoined`** workflow key. This is also the only way to exercise the join/boost reactions today: **live** member-join and boost events are gated behind the Server Members Intent (KI-52), but the test-fire publishes straight onto the bus and runs the whole reaction pipeline regardless.
- **Discord Server Boost** — the "subscribe"/Discord sound, the "Server Boosted!" alert, a `Discord/Boost` statistics row, and the **`discordboost`** workflow key.

### What you should see when you click one

For example, clicking **Twitch → Follow** (with default settings):

1. A gold system line in the chat window (if *Show in chat* is on for Twitch/Follow — see [Chat Settings](../chat/chat-settings.md)).
2. The sound assigned to Twitch follows in [Sounds](../stream-tools/sounds.md) (if any).
3. The [Visual Alerts](../stream-tools/visual-alerts.md) overlay window.
4. TestUser123 earning the follow bonus (default 100) in [Currency](../stream-tools/currency.md).
5. The status line flips to `FIRED Twitch Follow for TestUser123 at <time>` and the fire lands at the top of the recent-fires log.

### The 30-second rate limiter caveat

Repeated clicks of the **same** button within 30 seconds still publish the event (chat message, alert, sound, currency, workflows, log row all re-fire) — only the **auto-reply** is suppressed after the first. To see auto-replies repeatedly, alternate between different buttons, change the Test Username between clicks, or wait 30 seconds.

## Test matrix — which button exercises what

Because every button runs the full pipeline, any of them exercises all enabled reactions. Use this matrix when you want to verify one specific subsystem:

| To verify... | Click | What confirms it works |
|---|---|---|
| Chat feed system messages | any button | Gold line appears in the [Chat Window](../chat/chat-window.md) (needs *Show in chat* on for that platform/event in [Chat Settings](../chat/chat-settings.md)) |
| [Sounds](../stream-tools/sounds.md) | any button for an event with a sound assigned | Sound plays locally; browser-source broadcast is sent (see [Browser Sources](../stream-tools/browser-sources.md) for the known JSON mismatch) |
| [Visual Alerts](../stream-tools/visual-alerts.md) | **Raid** (has viewers count) or **Host** | Alert overlay window slides in with title/username/message |
| [Currency](../stream-tools/currency.md) event bonuses | **Cheer (100 bits)**, **Super Chat ($5.00)**, or **Gift Sub** | TestUser123's balance grows in Currency & Rewards |
| [Workflows](workflows.md) triggers | the button matching a flow's trigger | The flow's actions genuinely execute — this is why the warning banner exists |
| [Auto-Replies](auto-replies.md) templates | any button for an event with auto-reply enabled | The processed template is posted to the connected platform's chat (once per 30 s) |
| [Statistics](../stream-tools/statistics.md) / [Event History](event-history.md) rows | **Channel Points Redeem**, **Super Chat**, **Server Boost** ... | New rows through the same writers live events use |
| Multi-event ordering | **Kick Gift Sub** then **Kick Subscribe** | Two independent rate-limiter keys — both replies send back-to-back |
| Rate limiter | the **same** button twice quickly | Second click fires everything except the auto-reply |

## Troubleshooting

- **Nothing happens at all** — the chat window's per-platform display filter may be off (the handlers early-return on it), or *Show in chat* is disabled for that event in Chat Settings. Check the status line: a `FIRED ...` line means the publish succeeded and the reactions are configured off; a `FAILED ...` line carries the exception.
- **No auto-reply** — auto-reply is off for that platform/event (check Chat Settings), the platform isn't connected (replies are sent through the real chat service), or the 30-second rate limiter already fired for this user/event/platform.
- **No sound** — no sound file is assigned to that event in Sounds Management, or the file is not `.wav` (local playback is wav-only).
- **Balance unchanged** — check the currency event-bonus toggles in Currency & Rewards settings; bonuses are gated per event type there.

## How It Works (Developer)

[EventTestingViewModel.cs](../../src/VyralBot.UI/ViewModels/EventTestingViewModel.cs) injects one dependency: the shared **`TestEventPublisher`** ([VyralBot.Integrations/Testing](../../src/VyralBot.Integrations/Testing/TestEventPublisher.cs)). Each of the 17 buttons is a `[RelayCommand]` that calls `TriggerAsync(platform, eventType, eventName)`, which awaits `TestEventPublisher.PublishTestEventAsync(type, TestUsername, TestMessage)`, then records the fire into the status line and the 10-entry `RecentFires` collection (`FiredEventEntry` rows render failed fires in the error brush).

`TestEventPublisher` — extracted from the ViewModel precisely so the page and the HTTP **control API** fire byte-identical payloads — constructs the real integration event class (`TwitchFollowEvent`, `YouTubeSuperChatEvent`, `DiscordBoostEvent`, ... from `VyralBot.Integrations.{Twitch,Kick,YouTube,Discord}.Events`), fills the fixed realistic payload plus the two input fields, and `await _eventBus.PublishAsync(evt)`. `SupportedEventTypes` is the canonical list of the 17 type ids (`twitch.follow`, `youtube.superchat`, `discord.boost`, ...), and unknown ids throw `ArgumentException` — which is also what `POST /api/events/test` reports back (see [API, Web Server & ngrok](../settings/web-server-ngrok.md#the-control-api)).

Because the bus routes by exact CLR type, the published test events are indistinguishable from live ones: the same `ChatViewModel` handlers run the full [Event Reactions](../chat/event-reactions.md) pipeline, including the 30-second `AutoReplyRateLimiter` (keyed on `{platform}:{eventType}:{username}`). The five newer events ride the same bus: the channel-points redeem mirrors the `TwitchEventSubService` payload shape, the Super Chat carries `amountMicros` = 5,000,000, and the two Discord events are published directly — which is why they exercise reactions even though **live** join/boost delivery needs the Server Members Intent (KI-52).

Two downstream notes:

- Twitch events fired from here run the full reaction chain, as do live Twitch events now that EventSub connects after the auth fix — the test bypasses the transport either way (see [Twitch](../platforms/twitch.md)).
- Test events **are** recorded into `EventStatistics` (the writers live in the same `ChatViewModel` handlers), so they show up in [Event History](event-history.md) and [Statistics](../stream-tools/statistics.md) — except for the six ShowInChat-gated event types when their "Show in chat" toggle is off (KI-26).

## Files & Code

| File | Role |
|---|---|
| [EventTestingViewModel.cs](../../src/VyralBot.UI/ViewModels/EventTestingViewModel.cs) | The 17 trigger commands, status line, 10-entry recent-fires log |
| [TestEventPublisher.cs](../../src/VyralBot.Integrations/Testing/TestEventPublisher.cs) | One source of truth for test-event payloads — shared with `POST /api/events/test`; the 17 type ids in `SupportedEventTypes` |
| [EventTestingView.xaml](../../src/VyralBot.UI/Views/EventTestingView.xaml) | Page layout: warning banner, inputs, the four platform-grouped button cards, status line + log |
| [EventTestingView.xaml.cs](../../src/VyralBot.UI/Views/EventTestingView.xaml.cs) | Code-behind |
| [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) | The handlers these events wake (sounds, alerts, statistics, currency) |
| [ApiController.cs](../../src/VyralBot.Api/Controllers/ApiController.cs) | `POST /api/events/test` — the control API's window into the same publisher |
| [EventBus.cs](../../src/VyralBot.Core/Services/EventBus/EventBus.cs) | The shared bus both test and live events travel on |

A representative trigger — real event class, real bus, no shortcuts:

```csharp
[RelayCommand]
private Task TriggerTwitchCheer() =>
    TriggerAsync("Twitch", TestEventPublisher.TwitchCheer, "Cheer (100 bits)");
// TriggerAsync → await _testEventPublisher.PublishTestEventAsync(type, TestUsername, TestMessage);
//              → record FIRED/FAILED into LastFireStatus + RecentFires (cap 10, newest first)
```

## Data & Settings

- The page itself persists nothing — no settings, no database rows of its own.
- **Side effects do persist**: each test event's currency bonus is a real [Currency](../stream-tools/currency.md) transaction for the test user in the profile database, statistics rows land in `EventStatistics`, and workflows really run their actions.
- Auto-reply sends (when not rate-limited) go to the **real platform chat** if the platform is connected — the message is actually posted.

## Status & Known Issues

- **This page is fully functional** — 17 fires across all four platforms, and the recommended way to demo event reactions and workflow triggers.
- **Test-fires execute real workflows** — the FIRES REAL EVENTS warning is a design statement, not decoration; use a scratch profile around flows with live side effects.
- The 30 s rate limiter suppresses repeated auto-replies for the same user/event/platform; everything else (chat line, sound, alert, currency, workflow, statistics row) re-fires on every click. — [known issues ledger](../reference/known-issues.md)
- Test events create Event History rows through the same handlers as live events (subject to the KI-26 ShowInChat caveat for the six gated types). — [known issues ledger](../reference/known-issues.md)
- **Discord member-join/boost fires bypass the intent gate** — live delivery of those events needs the Server Members Intent portal toggle (KI-52), but the test publishes straight onto the bus, so it is the way to verify join/boost reactions (and the Welcome & Auto-Role flow) today.
- Currency side effects are real; use a scratch [profile](../getting-started/profiles.md) if you care about your live data.
- There are still no test buttons for plain chat **messages** — the command pipeline cannot be exercised from here; type a `!command` in a real connected chat instead. The control API's `/api/events/test` fires the same 17 events over HTTP.
