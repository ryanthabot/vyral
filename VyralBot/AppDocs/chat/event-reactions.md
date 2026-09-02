# Event Reactions

> What happens when someone follows, subscribes, raids, cheers, or hosts: each event can trigger up to five effects — a system chat message, a sound, a visual alert, a currency bonus, and a rate-limited auto-reply — all orchestrated from the chat view model.

## Overview

Vyral listens for stream events on Twitch and Kick. When one arrives, a single handler in the [chat window](chat-window.md)'s view model fires the same five-part reaction chain, with each part individually configurable:

1. **System chat message** — a gold "System" entry in the unified feed (for example "🎉 Alice just followed!").
2. **Sound** — plays your assigned event sound locally and to the OBS browser source.
3. **Visual alert** — an in-app alert overlay window slides in for the configured duration, position, and animation.
4. **Currency bonus** — awards points to the viewer (follow = 100, sub = 500, etc.), including cheer and host bonuses.
5. **Auto-reply** — sends your template message to that platform's chat, at most once per viewer/event/platform every 30 seconds.
6. **Event statistic** — records a row in `EventStatistics` so [Event History](../chat-features/event-history.md) and [Statistics](../stream-tools/statistics.md) can show it.

YouTube and Discord have no event ingestion (chat messages only), and TikTok has no integration at all.

## User Guide

### Which events react

| Platform | Events with reactions |
|---|---|
| **Twitch** | Follow, Subscribe, Resub, Gift Sub, Raid, Cheer |
| **Kick** | Follow, Subscribe, Gift Sub, Host, Kicks Gifted, Reward Redeemed, Stream Status (chat message only) |
| **YouTube / Discord** | None (chat messages only — no event ingestion) |
| **TikTok** | None (no integration) |

### Where to configure each effect

The five effects are configured in *different* places — that is the most confusing part of the system:

| Effect | Where to configure it |
|---|---|
| System chat message | **Show in chat** toggle per event — [Chat Settings](chat-settings.md) (Chat Hub tab or chat-header popup) |
| Auto-reply | **Auto-reply** toggle + template per event — same place |
| Sound | Per-event sound file and volume — [Sounds Management](../stream-tools/sounds.md) |
| Visual alert | Master toggle, duration, animation, position, and per-event switches — [Visual Alerts](../stream-tools/visual-alerts.md) settings (applied to real alerts since the 2026-08-15 fix) |
| Currency bonus | Amounts and on/off per event type — [Currency & Rewards](../stream-tools/currency.md) settings |

### Editing an auto-reply template

1. Open the Chat Hub (Home → Chat card) or the platform's popup in the chat window header.
2. Expand the event you want (for example **Raid**).
3. Turn on **Show in chat** if you also want the gold system message, and **Auto-reply** for the reply.
4. Write the template. Only the variables listed below resolve — anything else in braces is left untouched. Note that DB variables like `{twitter}` do **not** resolve in auto-replies.
5. Click **Save Settings**.

Available variables per event (exactly what the handler passes):

| Platform | Event | Variables |
|---|---|---|
| Kick | Follow | `{username}`, `{platform}` |
| Kick | Subscribe | `{username}`, `{months}`, `{message}`, `{platform}` |
| Kick | Gift Sub | `{gifter}`, `{count}`, `{platform}` |
| Kick | Host | `{hoster}`, `{username}`, `{viewers}`, `{platform}` |
| Kick | Kicks Gifted | `{sender}`, `{username}`, `{amount}`, `{message}`, `{platform}` |
| Kick | Reward Redeemed | `{username}`, `{reward}`, `{cost}`, `{input}`, `{platform}` |
| Kick | Stream Status | — (no auto-reply) |
| Twitch | Follow | `{username}`, `{displayname}`, `{platform}` |
| Twitch | Subscribe | `{username}`, `{displayname}`, `{tier}`, `{platform}` |
| Twitch | Resub | `{username}`, `{tier}`, `{months}`, `{streak}`, `{platform}` |
| Twitch | Gift Sub | `{gifter}`, `{count}`, `{username}`, `{platform}` |
| Twitch | Raid | `{username}`, `{raider}`, `{viewers}`, `{platform}` |
| Twitch | Cheer | `{username}`, `{bits}`, `{platform}` |

Watch the spelling all the same: the processor replaces only the `{...}` part, so an `@` in front of a variable (in a template you wrote yourself) is sent literally if the event does not supply that key. The shipped defaults now use plain `{username}`-style tokens and `{raider}` is supplied by the raid handler.

### Rate limiting

Auto-replies are limited to **one per viewer, per event type, per platform, per 30 seconds**. If the same viewer triggers the same event twice within the window, the second reply is skipped (everything else — sound, alert, currency — still fires). This also applies when testing: repeatedly clicking the same test button is suppressed after the first click.

### Testing your setup

Use **Event Testing** (standalone sidebar item) to publish synthetic Kick/Twitch events with realistic payloads. Everything downstream — chat message, sound, alert, currency, auto-reply — reacts exactly as for live events. See [Event Testing](../chat-features/event-testing.md).

## How It Works (Developer)

### How events arrive

```
Twitch EventSub WebSocket ──► TwitchService ──► typed TwitchXxxEvent ──┐
Kick webhooks (ngrok → Kestrel :5000) ──► WebhooksController ─────────┼─► EventBus ─► ChatViewModel handler
Kick Pusher WebSocket ──► KickLibChatService ──► typed KickXxxEvent ───┘                    │
                                                                      ┌──────────────────────────┤
                                                                      │ 1. system chat message   │
                                                                      │ 2. sound                 │
                                                                      │ 3. visual alert          │
                                                                      │ 4. currency bonus        │
                                                                      │ 5. rate-limited auto-    │
                                                                      │    reply                 │
                                                                      └──────────────────────────┘
```

- **Twitch**: TwitchLib EventSub over WebSocket (no inbound webhooks needed) — subscribes to `channel.follow` v2, subscribe, subscription.message, subscription.gift, raid, cheer. The EventSub connection is gated on `Platform.UserId` + `ClientId`, and `UserId` is now populated by both Twitch auth flows (Helix `GET /users` after token exchange — fixed 2026-08-15), so events flow after (re)authorization.
- **Kick**: channel events arrive as **webhooks** through the ngrok tunnel into [WebhooksController.cs](../../src/VyralBot.Api/Controllers/WebhooksController.cs), which validates RSA-SHA256 signatures and publishes typed events onto the shared EventBus. Chat *messages* come from the Pusher WebSocket instead — a deliberate split to avoid duplicates. Some Pusher handlers additionally emit synthetic bot-authored display messages ("🎉 X just subscribed!") alongside the typed events.
- **YouTube**: only `YouTubeChatMessageEvent` exists; there is no event ingestion.

One shared EventBus serves both the WPF UI and the embedded Kestrel server, so a webhook-sourced Kick follow and a WebSocket-sourced Twitch cheer hit the exact same handlers. See [Event Bus](../architecture/event-bus.md).

### The handler pattern

`ChatViewModel` subscribes to all 13 event types during `InitializeAsync` (ChatViewModel.cs:207-270; Kick handlers from :1700, Twitch handlers from :2089). Every handler follows the same shape:

1. Check the platform display-filter flag (`KickFilterEnabled` / `TwitchFilterEnabled`) — the whole reaction chain is skipped if the platform's chat is hidden.
2. `IChatEventSettingsService.GetSettingAsync(platform, eventType)` — load the `ChatEventSetting` row; if none exists, stop.
3. If `ShowInChat` → `AddSystemMessageAsync(platform, text)` (ChatViewModel.cs:2481) — gold `#FFD700` "System" entry.
4. `ISoundService.PlayEventSoundAsync(eventType, platform)` — resolves the sound from its own audio settings and event-sound mapping, not from the `ChatEventSetting` row.
5. `ShowEventAlertAsync(...)` (ChatViewModel.cs:2628) → `IAlertService.ShowAlertAsync` with the persisted Alert Settings applied (animation, position, duration from the `AlertSettings` Configs key; `applySettings = true` by default).
6. `ICurrencyService.AwardEventBonusAsync(username, platform, type, count?)` — bonus multiplied for gift-sub counts and raid viewer counts; skipped for Twitch gift-sub *recipients* (the gifter gets the bonus). The Twitch cheer and Kick host handlers now call this too (wired 2026-08-15), with the full multiplier stack (platform × boost × tier × role, `Math.Floor`) applied.
7. If `AutoReplyEnabled` and a template exists and `_autoReplyService.ShouldSendReply(user, platform, eventType)` passes the rate limiter → `ProcessTemplate(template, variables)` → send via the platform's chat service.
8. `SaveEventStatisticAsync` — persist the event row with the handler's variable dict as `EventData` and the accurate `WasAutoReplied` (feeds [Event History](../chat-features/event-history.md) and [Statistics](../stream-tools/statistics.md)).

Two gating variants exist in the wild: most handlers use `if (settings == null) return;` and then gate only the chat message on `ShowInChat` — so sound/alert/currency fire even with "Show in chat" off. A few (`OnTwitchGiftSubAsync`, `OnKickGiftSubAsync`, `OnKickHostAsync`, `OnKickKicksGiftedAsync`, `OnKickRewardRedeemedAsync`, `OnKickStreamStatusAsync`) use `if (settings?.ShowInChat != true) return;`, which gates the *entire* handler on ShowInChat — including the new statistics write (KI-26). `OnTwitchGiftSubAsync` now has a full auto-reply block (added 2026-08-15).

### Template processing and rate limiting

- `ProcessTemplate` ([AutoReplyService.cs](../../src/VyralBot.Core/Services/Chat/AutoReplyService.cs):37) is a plain case-insensitive `string.Replace` of `{key}` placeholders from the handler's variable dictionary. It does **not** use `IVariableReplacementService`, so DB/social variables never resolve in auto-replies.
- `ShouldSendReply` (:136) delegates to [AutoReplyRateLimiter.cs](../../src/VyralBot.Core/Services/Chat/AutoReplyRateLimiter.cs) — a `ConcurrentDictionary` keyed `"{platform}:{eventType}:{username-lowercase}"` with a 30-second cooldown (constructed at AutoReplyService.cs:26). Expired-entry cleanup exists (`CleanupExpired`, 10 minutes) but no timer schedules it.
- A richer conditional-rules engine (`AutoReplyService.GetAutoReplyAsync` — loads `AutoReplyRule` rows, evaluates `RuleCondition` JSON) is fully implemented but has **no production caller**; the live path is the simple one above. See [Auto-Replies](../chat-features/auto-replies.md).

## Files & Code

| File | Role |
|---|---|
| [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) | The 13 event handlers, `AddSystemMessageAsync` (:2481), `ShowEventAlertAsync` (:2628), subscriptions (:207-270) |
| [AutoReplyService.cs](../../src/VyralBot.Core/Services/Chat/AutoReplyService.cs) | `ProcessTemplate`, `ShouldSendReply`, dormant rules engine `GetAutoReplyAsync` |
| [AutoReplyRateLimiter.cs](../../src/VyralBot.Core/Services/Chat/AutoReplyRateLimiter.cs) | 30 s per-user/event/platform cooldown |
| [ChatEventSettingsService.cs](../../src/VyralBot.Core/Services/Chat/ChatEventSettingsService.cs) | Cached `ChatEventSetting` access |
| [ChatEventSetting.cs](../../src/VyralBot.Data/Entities/ChatEventSetting.cs) | Per-event settings entity |
| [TwitchService.cs](../../src/VyralBot.Integrations/Twitch/TwitchService.cs) | Twitch EventSub ingestion |
| [KickLibChatService.cs](../../src/VyralBot.Integrations/Kick/KickLibChatService.cs) | Kick Pusher ingestion (chat + some events) |
| [WebhooksController.cs](../../src/VyralBot.Api/Controllers/WebhooksController.cs) | Kick webhook ingestion (follows, subs, gifts, stream status, moderation) |
| [YouTubeChatService.cs](../../src/VyralBot.Integrations/YouTube/YouTubeChatService.cs) | YouTube — chat messages only, no events |
| Kick event payloads | `src/VyralBot.Integrations/Kick/Events/` — [KickFollowEvent.cs](../../src/VyralBot.Integrations/Kick/Events/KickFollowEvent.cs) + Subscribe, GiftSub, Host, KicksGifted, RewardRedeemed, StreamStatus |
| Twitch event payloads | `src/VyralBot.Integrations/Twitch/Events/` — [TwitchRaidEvent.cs](../../src/VyralBot.Integrations/Twitch/Events/TwitchRaidEvent.cs) + Follow, Subscribe, Resub, GiftSub, Cheer |
| [SoundService.cs](../../src/VyralBot.Core/Services/Audio/SoundService.cs) | `PlayEventSoundAsync` — event → sound resolution |
| [AlertService.cs](../../src/VyralBot.UI/Services/AlertService.cs) | Alert overlay window |
| [CurrencyService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyService.cs) | `AwardEventBonusAsync` — event bonuses |

The handler pattern, trimmed from `OnKickFollowAsync` (ChatViewModel.cs:1700) — all five effects in order, each individually gated:

```csharp
private async Task OnKickFollowAsync(KickFollowEvent evt)
{
    if (!KickFilterEnabled) return;

    var settings = await _eventSettingsService.GetSettingAsync("Kick", "Follow");
    if (settings == null) return;

    if (settings.ShowInChat)                                        // 1. system chat message
    {
        var message = $"🎉 {evt.Username} just followed! Total followers: {evt.FollowerCount}";
        await AddSystemMessageAsync("Kick", message);
    }

    if (_soundService != null)                                      // 2. sound
        await _soundService.PlayEventSoundAsync("follow", "Kick");

    await ShowEventAlertAsync("New Follower!", evt.Username,        // 3. visual alert
        $"Total followers: {evt.FollowerCount}", "Kick");

    if (_currencyService != null)                                   // 4. currency bonus
        await _currencyService.AwardEventBonusAsync(evt.Username, "Kick", "follow");

    if (settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage))
    {
        if (_autoReplyService.ShouldSendReply(evt.Username, "Kick", "Follow"))  // 5. rate-limited auto-reply
        {
            var variables = new Dictionary<string, string>
            {
                { "username", evt.Username },
                { "platform", "Kick" }
            };
            var reply = _autoReplyService.ProcessTemplate(settings.AutoReplyMessage, variables);
            await _kickService.SendMessageAsync(reply);
        }
    }
}
```

The template processor (AutoReplyService.cs:37) — deliberately simple; note it only ever replaces the keys the handler passed:

```csharp
public string ProcessTemplate(string template, Dictionary<string, string> variables)
{
    if (string.IsNullOrEmpty(template))
        return string.Empty;

    var result = template;

    foreach (var (key, value) in variables)
    {
        var placeholder = $"{{{key}}}";
        result = result.Replace(placeholder, value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    return result;
}
```

## Data & Settings

| Data | Where | Notes |
|---|---|---|
| Per-event reaction settings | `ChatEventSettings` rows: `ShowInChat`, `AutoReplyEnabled`, `AutoReplyMessage` per (Platform, EventType) | Edited in [Chat Settings](chat-settings.md); its `SoundEnabled`/`SoundFile`/`SoundVolume` columns are orphaned — nothing reads them |
| Event → sound mapping | Configs keys `EventSound_<Platform>_<event>` / `EventVolume_<Platform>_<event>` | Edited in [Sounds Management](../stream-tools/sounds.md) |
| Currency event bonuses | `CurrencySetting` flags and amounts: follow 100, subscribe 500, giftsub 50/sub, raid 10/viewer, cheer 1/bit, host 200 | See [Currency & Rewards](../stream-tools/currency.md) |
| Alert styling | Configs key `AlertSettings` (master toggle, duration, animation, position, per-event switches) | Applied by `AlertService` on real alerts — see [Visual Alerts](../stream-tools/visual-alerts.md) |
| Auto-reply rate-limit state | In-memory `ConcurrentDictionary` | Resets on app restart |
| Event statistics | `EventStatistics` table | Written by the 13 handlers (handler variable dict as `EventData`, accurate `WasAutoReplied`); the six ShowInChat-gated handlers skip recording when Show-in-chat is off (KI-26) |

## Status & Known Issues

- **Works live**: all five effects plus statistics recording for Kick events (webhook + Pusher sourced), auto-reply templates, rate limiting, currency bonuses, test-event triggering. Together with the command pipeline (live since 2026-08-15), this is the automation backbone of the app.
- **Twitch events arrive after (re)authorization** — `Platform.UserId` is now set by both Twitch auth flows, so EventSub connects (fixed 2026-08-15). Tokens saved before the fix need one re-auth.
- **Alerts respect the Alert Settings page** — animation, position, duration, master toggle, and per-event switches are loaded from the `AlertSettings` Configs key (fixed 2026-08-15). [Known issues](../reference/known-issues.md).
- **Browser-source sounds work** — the WebSocket broadcast now sends the camelCase shape the page understands, with volume honored (fixed 2026-08-15). [Known issues](../reference/known-issues.md).
- **`{raider}` is supplied** by the Twitch Raid handler; **Twitch Gift Sub has a full auto-reply block** (both fixed 2026-08-15).
- **Show-in-chat gating is inconsistent**: for six events, turning off "Show in chat" also silences sound, alert, currency, auto-reply, *and* the statistics write; for most it only hides the message. [Known issues](../reference/known-issues.md) (KI-26).
- **DB/social variables still do not resolve in auto-reply templates** — see [Auto-Replies](../chat-features/auto-replies.md) (KI-28).
- **Kick Pusher follow events carry `Username="Unknown"`**; the webhook path is the one that knows the real name. [Known issues](../reference/known-issues.md).
- **The dormant rules engine**: conditional auto-reply rules ([Conditional Rules](../chat-features/conditional-rules.md)) are stored but never fire — `GetAutoReplyAsync` has no callers. [Known issues](../reference/known-issues.md).
- **YouTube/Discord/TikTok have no event reactions** — only Twitch and Kick event types exist (Discord and YouTube carry chat only; TikTok has no integration). [Known issues](../reference/known-issues.md).
- The legacy `USER_GUIDE_CHAT_EVENTS.md` (repo root) also lists a Twitch "Channel Points" event; no handler for it exists — only six Twitch event handlers are implemented.
