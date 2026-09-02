# Event Testing

> A developer console with 12 buttons that publish synthetic Kick and Twitch events onto the real event bus — the full reaction pipeline (chat message, sound, alert, currency bonus, auto-reply) runs exactly as for live events, making this the best way to demo and verify the app.

## Overview

Event Testing is a diagnostic console. Each button fabricates a realistic platform event — a follow, a subscription, a raid with 200 viewers — and publishes it onto the same [Event Bus](../architecture/event-bus.md) that real Twitch/Kick connections publish to. Every downstream handler reacts as if the event were genuine: you get the gold system message in the chat feed, the configured sound plays, the visual alert window opens, the viewer earns their currency bonus, and the auto-reply template is sent (rate limiter permitting). Nothing is mocked after the event itself.

This page is **fully functional** — and because several other features are currently dead (see [Commands](commands.md) and [Event History](event-history.md)), it is effectively the best way to demonstrate the app working end-to-end without going live.

Two things to know before you click:

- **Auto-replies are rate-limited.** The same user + event + platform combination only auto-replies once every 30 seconds, so spamming one button produces one reply (see [Auto-Replies](auto-replies.md)).
- **Currency bonuses are real.** Each test event awards currency to the test user through the normal [Currency](../stream-tools/currency.md) bonus system, writing to your profile's database.

## User Guide

Open **Event Testing** in the sidebar — it is a **standalone entry below Settings**, not part of the Chat Features group.

### Inputs

- **Test Username** — the viewer name embedded in every test event (default `TestUser123`).
- **Test Message** — chat text embedded where the event carries one (default `This is a test message`).
- **Output Log** — a timestamped log of every publish and any errors; **Clear Log** wipes it.

### The 12 test buttons

**Kick events** (6 buttons), each publishing the corresponding integration event with a fixed realistic payload:

| Button | Event payload |
|---|---|
| **Follow Event** | `Username` = test username, `UserId` = "12345", `FollowerCount` = **42** |
| **Subscribe Event** | `MonthsSubscribed` = **3**, `Message` = test message |
| **Gift Sub Event** | `GifterUsername` = test username, `Count` = **5** subs to `Recipient1`–`Recipient5` |
| **Host Event** | `HostUsername` = test username, `ViewerCount` = **150** |
| **Kicks Gifted Event** | `SenderUsername` = test username, `Amount` = **1000** kicks, `Message` = test message |
| **Reward Redeemed Event** | `RewardName` = **"Test Reward"**, `Cost` = **500**, `UserInput` = test message |

**Twitch events** (6 buttons):

| Button | Event payload |
|---|---|
| **Follow Event** | `Username`/`DisplayName` = test username, `UserId` = "12345" |
| **Subscribe Event** | `Tier` = **"1000"**, `MonthsTotal` = **1**, `StreakMonths` = **1**, `IsGift` = false |
| **Resub Event** | `CumulativeMonths` = **6**, `StreakMonths` = **4**, `Tier` = "1000", `Message` = test message |
| **Gift Sub Event** | `GifterUsername` = test username, `Tier` = "1000", `Total` = **5**, `CumulativeTotal` = **25**, `IsAnonymous` = false |
| **Raid Event** | `FromBroadcasterUsername` = test username, `Viewers` = **200**, target channel "YourChannel" |
| **Cheer Event** | `Bits` = **100**, `Message` = test message, `IsAnonymous` = false |

### What you should see when you click one

For example, clicking **Kick Follow Event** (with default settings):

1. A gold system line in the chat window: "TestUser123 just followed! Total followers: 42" (if *Show in chat* is on for Kick/Follow — see [Chat Settings](../chat/chat-settings.md)).
2. The sound assigned to Kick follows in [Sounds](../stream-tools/sounds.md) (if any).
3. The [Visual Alerts](../stream-tools/visual-alerts.md) overlay window.
4. TestUser123 earning the follow bonus (default 100) in [Currency](../stream-tools/currency.md).
5. The auto-reply template sent to Kick — but only if auto-reply is enabled for Kick/Follow **and** no reply was sent for this user/event in the last 30 s. The log line says the event was published; a suppressed reply is visible only in the app logs, not the page log.

### The 30-second rate limiter caveat

Repeated clicks of the **same** button within 30 seconds still publish the event (chat message, alert, sound, and currency all re-fire) — only the **auto-reply** is suppressed after the first. To see auto-replies repeatedly, alternate between different event buttons, change the Test Username between clicks, or wait 30 seconds.

## Test matrix — which button exercises what

Because every button runs the full pipeline, any of them exercises all enabled reactions. Use this matrix when you want to verify one specific subsystem:

| To verify... | Click | What confirms it works |
|---|---|---|
| Chat feed system messages | any button | Gold line appears in the [Chat Window](../chat/chat-window.md) (needs *Show in chat* on for that platform/event in [Chat Settings](../chat/chat-settings.md)) |
| [Sounds](../stream-tools/sounds.md) | any button for an event with a sound assigned | Sound plays locally; browser-source broadcast is sent (see [Browser Sources](../stream-tools/browser-sources.md) for the known JSON mismatch) |
| [Visual Alerts](../stream-tools/visual-alerts.md) | **Twitch Raid Event** (has viewers count) or **Kick Host Event** | Alert overlay window slides in with title/username/message |
| [Currency](../stream-tools/currency.md) event bonuses | **Twitch Cheer Event** (100 bits) or **Kick Gift Sub Event** (5 subs) | TestUser123's balance grows in Currency & Rewards |
| [Auto-Replies](auto-replies.md) templates | any button for an event with auto-reply enabled | The processed template is posted to the connected platform's chat (once per 30 s) |
| Multi-event ordering | **Kick Gift Sub Event** then **Kick Subscribe Event** | Two independent rate-limiter keys — both replies send back-to-back |
| Rate limiter | the **same** button twice quickly | Second click fires everything except the auto-reply |

## Troubleshooting

- **Nothing happens at all** — the chat window's per-platform display filter may be off (the handlers early-return on it), or *Show in chat* is disabled for that event in Chat Settings.
- **No auto-reply** — auto-reply is off for that platform/event (check Chat Settings), the platform isn't connected (replies are sent through the real chat service), or the 30-second rate limiter already fired for this user/event/platform.
- **No sound** — no sound file is assigned to that event in Sounds Management, or the file is not `.wav` (local playback is wav-only).
- **Balance unchanged** — check the currency event-bonus toggles in Currency & Rewards settings; bonuses are gated per event type there.

## How It Works (Developer)

[EventTestingViewModel.cs](../../src/VyralBot.UI/ViewModels/EventTestingViewModel.cs) injects the shared `IEventBus` and each button is a `[RelayCommand]` that constructs the real integration event class — `KickFollowEvent`, `KickSubscribeEvent`, `KickGiftSubEvent`, `KickHostEvent`, `KickKicksGiftedEvent`, `KickRewardRedeemedEvent`, `TwitchFollowEvent`, `TwitchSubscribeEvent`, `TwitchResubEvent`, `TwitchGiftSubEvent`, `TwitchRaidEvent`, `TwitchCheerEvent` (from `VyralBot.Integrations.Kick.Events` / `VyralBot.Integrations.Twitch.Events`) — fills in the payload from the tables above plus the two input fields, and `await _eventBus.PublishAsync(evt)`.

Because the bus routes by exact CLR type, the published test events are indistinguishable from live ones: the same `ChatViewModel` handlers (`OnKickFollowAsync`, `OnTwitchCheerAsync`, ...) run the full [Event Reactions](../chat/event-reactions.md) pipeline. This is also why the page exercises the 30-second `AutoReplyRateLimiter` (see [Auto-Replies](auto-replies.md)) — `ShouldSendReply` keys on `{platform}:{eventType}:{username}`, so identical clicks within 30 s are rate-limited exactly like real repeat events.

Two downstream notes:

- Twitch events fired from here work even though *live* Twitch events currently depend on EventSub connecting (see [Twitch](../platforms/twitch.md)) — the test bypasses the transport entirely.
- The events are **not** recorded into `EventStatistics`, so [Event History](event-history.md) stays empty even after testing (that table has no writer at all).

## Files & Code

| File | Role |
|---|---|
| [EventTestingViewModel.cs](../../src/VyralBot.UI/ViewModels/EventTestingViewModel.cs) | The 12 trigger commands, payload construction, bus publishing, output log |
| [EventTestingView.xaml](../../src/VyralBot.UI/Views/EventTestingView.xaml) | Page layout: inputs, Kick/Twitch button groups, log |
| [EventTestingView.xaml.cs](../../src/VyralBot.UI/Views/EventTestingView.xaml.cs) | Code-behind |
| [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) | The handlers these events wake (lines 1700-2480) |
| [EventBus.cs](../../src/VyralBot.Core/Services/EventBus/EventBus.cs) | The shared bus both test and live events travel on |

A representative trigger — real event class, real bus, no shortcuts:

```csharp
[RelayCommand]
private async Task TriggerKickFollow()
{
    var evt = new KickFollowEvent
    {
        Source = "Kick",
        Username = TestUsername,
        UserId = "12345",
        FollowerCount = 42,
        Timestamp = DateTime.UtcNow
    };
    await _eventBus.PublishAsync(evt);
    AddLog("✅ Kick Follow Event Published");
}
```

## Data & Settings

- The page itself persists nothing — no settings, no database rows of its own.
- **Side effects do persist**: each test event's currency bonus is a real [Currency](../stream-tools/currency.md) transaction for the test user in the profile database. If you click cheer tests fifty times, TestUser123 genuinely accumulates bits-scaled bonuses.
- Auto-reply sends (when not rate-limited) go to the **real platform chat** if the platform is connected — the message is actually posted.

## Status & Known Issues

- **This page is fully functional** — the one Chat Features tool that works end-to-end today, and the recommended way to demo event reactions.
- The 30 s rate limiter suppresses repeated auto-replies for the same user/event/platform; everything else (chat line, sound, alert, currency) re-fires on every click. — [known issues ledger](../reference/known-issues.md)
- Auto-reply output only goes to Kick/Twitch and depends on per-event auto-replies being enabled — check [Chat Settings](../chat/chat-settings.md) if nothing replies.
- Test events do **not** create Event History rows — that table has no writer regardless of source (see [Event History](event-history.md)). — [known issues ledger](../reference/known-issues.md)
- Currency side effects are real; use a scratch [profile](../getting-started/profiles.md) if you care about your live data.
- There are no test buttons for chat **messages** (so the dead command pipeline cannot be exercised from here) and none for YouTube events.
