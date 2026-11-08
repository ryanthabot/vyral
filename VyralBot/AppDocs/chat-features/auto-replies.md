# Auto-Replies

> Automatic chat responses to stream events — one live, working mechanism (per-event templates behind a 30-second rate limiter) plus a conditional-rules engine that overrides those templates when its conditions match (Kick/Twitch, gated on a default message).

## Overview

When someone follows, subscribes, raids, cheers, or triggers other supported events, Vyral can automatically send a message to that platform's chat — "Thanks for the follow {username}!" and anything else you write. You enable it per event and per platform, write a template with placeholders like `{username}` or `{bits}`, and the app sends it when the event fires, at most **once per user, per event, per platform every 30 seconds**.

There are two code paths that share the "auto-reply" name, and only one of them is live:

1. **Per-event auto-replies (live).** One row per (platform, event type) storing Show-in-chat, Auto-reply enabled, and the template. The real event handlers in the chat engine read it, run the rate limiter, substitute variables, and send.
2. **Conditional auto-reply rules (live, gated).** An engine that picks a reply by evaluating conditions — "only on the 100th follower", "only after 20:00" — stored as rules. When a Kick or Twitch auto-reply is about to send, a matching rule **replaces** the default template; the catch is the rules lookup only runs when the event has auto-reply enabled **and a default message configured**, and Discord events never consult rules. That system has its own page: [Conditional Rules](conditional-rules.md).

## User Guide

### Where auto-replies are configured

Auto-reply settings are part of each platform's **chat settings** — open the Chat Hub (Home → Chat, see [Chat Settings](../chat/chat-settings.md)) and go to the **Twitch** or **Kick** tab, or use the per-platform quick-settings popups in the chat window header. Each supported event gets a card with three controls:

- **Show in chat** — print a gold system message in your chat feed when the event happens (this part is the [Event Reactions](../chat/event-reactions.md) system).
- **Auto-reply** — actually send a message to the platform's chat.
- **Template** — the message text. The small hint under each box lists the variables that event provides.

Kick events: Follow, Subscribe, Gift Sub, Host, Kicks Gifted, Reward Redeemed (Stream Status has show-in-chat only). Twitch events: Follow, Subscribe, Resub, Gift Sub, Raid, Cheer.

Every platform tab also has a General-card **Auto-Replies** master toggle, and all of them persist (`Chat.<Platform>.AutoRepliesEnabled`; Kick uses its own key) — but only Twitch and Kick have per-event templates behind the toggle, so YouTube, TikTok, and Discord still have no event auto-replies that actually fire.

### Writing a template

Type any text and insert placeholders. **Only the variables listed for that event work** — this path does a simple text substitution of exactly those keys, so database variables like `{twitter}` or `{twitch-followercount}` will *not* be replaced here (they work in [commands](commands.md) and manual sends instead — see [Variables](variables.md)). That limitation is tracked as KI-28 in the [known issues ledger](../reference/known-issues.md).

A note on the `@` character: earlier builds shipped default templates like `Thanks for the follow @{username}!`, which produced a stray `@` because the substitution replaces only the `{...}` part. The seeds and the Twitch/Kick default templates were fixed on 2026-08-15 to use plain `{username}` — but if you wrote your own template with an `@` in front of a variable that the event does not supply, the literal `@{...}` text is still sent to chat. Prefer `@{username}` only where the event's hint lists `username`, or write the template without the `@`.

### The 30-second rate limiter

Auto-replies are limited to one per user + event + platform per 30 seconds. If the same viewer triggers the same event twice within that window, the second reply is silently skipped (you will see "Auto-reply blocked by rate limiter" in the logs). The easiest way to see this is with the [Event Testing](event-testing.md) console — clicking the same test button repeatedly only produces one reply per 30 seconds.

## How It Works (Developer)

### The live path

**Settings storage** — [ChatEventSetting.cs](../../src/VyralBot.Data/Entities/ChatEventSetting.cs): one row per `(Platform, EventType)` with `ShowInChat`, `AutoReplyEnabled`, `AutoReplyMessage`, plus sound columns. **[ChatEventSettingsService.cs](../../src/VyralBot.Core/Services/Chat/ChatEventSettingsService.cs)** wraps them in a read-through cache (dictionary keyed `"{Platform}:{EventType}"`, lazy-loaded once under a `SemaphoreSlim`; saves write through; `InvalidateCache()` forces a reload). Helpers: `IsEventEnabledAsync` (ShowInChat), `IsAutoReplyEnabledAsync`, `GetAutoReplyMessageAsync`, `GetSoundSettingsAsync`.

**Execution** — the per-event handlers in `ChatViewModel` (e.g. `OnKickFollowAsync` at `ChatViewModel.cs:5841`). For every event, gated by the setting:

1. Optional system message in the chat feed (`ShowInChat`).
2. `SoundService.PlayEventSoundAsync` (see [Sounds](../stream-tools/sounds.md)).
3. Visual alert overlay (see [Visual Alerts](../stream-tools/visual-alerts.md)).
4. `CurrencyService.AwardEventBonusAsync` (see [Currency](../stream-tools/currency.md)).
5. If `AutoReplyEnabled` and a message exists: rate-limit check via `AutoReplyService.ShouldSendReply`, then `ProcessTemplate` with the event's variable dictionary, then send through the platform chat service.

**Template processing** — `AutoReplyService.ProcessTemplate` ([AutoReplyService.cs:37](../../src/VyralBot.Core/Services/Chat/AutoReplyService.cs)) is a plain case-insensitive `string.Replace` of `{key}` for each supplied key. It deliberately does **not** use the [VariableReplacementService](../../src/VyralBot.Core/Services/Variables/VariableReplacementService.cs), so database/social variables never resolve here (KI-28), and unknown keys are left verbatim.

**Rate limiting** — [AutoReplyRateLimiter.cs](../../src/VyralBot.Core/Services/Chat/AutoReplyRateLimiter.cs): a `ConcurrentDictionary` keyed `"{platform}:{eventType}:{username}"`, 30 s minimum delay (constructed at `AutoReplyService.cs:26`). `CleanupExpired()` prunes entries older than 10 minutes, but the periodic `CleanupExpiredEntries` call it was written for is never scheduled — stale keys accumulate for the session (they still expire functionally because timestamps are compared).

### Template variables per event (exactly what each handler supplies)

| Platform | Event | Variables supplied |
|---|---|---|
| Kick | Follow | `username`, `platform` |
| Kick | Subscribe | `username`, `months`, `message`, `platform` |
| Kick | Gift Sub | `gifter`, `count`, `platform` |
| Kick | Host | `hoster`, `username`, `viewers`, `platform` |
| Kick | Kicks Gifted | `sender`, `username`, `amount`, `message`, `platform` |
| Kick | Reward Redeemed | `username`, `reward`, `cost`, `input`, `platform` |
| Kick | Stream Status | *(show-in-chat only — no auto-reply)* |
| Twitch | Follow | `username`, `displayname`, `platform` |
| Twitch | Subscribe | `username`, `displayname`, `tier`, `platform` |
| Twitch | Resub | `username`, `tier`, `months`, `streak`, `platform` |
| Twitch | Gift Sub | `gifter`, `count`, `username`, `platform` (full auto-reply block added 2026-08-15) |
| Twitch | Raid | `username`, `raider`, `viewers`, `platform` — `{raider}` is now actually supplied (fixed 2026-08-15) |
| Twitch | Cheer | `username`, `bits`, `platform` |

### The conditional path

`AutoReplyService.GetAutoReplyAsync` (`AutoReplyService.cs:63`) is the conditional engine: load `AutoReplyRule` rows for the platform/event ordered by `Priority ASC, Id ASC` (`Platform = @Platform OR Platform = 'All'`) → deserialize each rule's `ConditionJson` → evaluate through `IRuleEvaluator` → on first match increment `TriggerCount` and return the processed `ReplyTemplate`. Its production caller is `ChatViewModel.BuildAutoReplyTextAsync` (ChatViewModel.cs:4260): every Kick and Twitch event handler builds its reply through it, so a matching rule's template **replaces** the event's default message (the rule reply then passes through full variable replacement). Boundary: the handlers only call it when auto-reply is enabled **and the default message is non-empty**, and there is no Discord call site — the full story is on the [Conditional Rules](conditional-rules.md) page.

## Files & Code

| File | Role |
|---|---|
| [AutoReplyService.cs](../../src/VyralBot.Core/Services/Chat/AutoReplyService.cs) | Template processing (`ProcessTemplate`), rate-limit check, and the conditional engine (`GetAutoReplyAsync`, called from the Kick/Twitch reply path) |
| [IAutoReplyService.cs](../../src/VyralBot.Core/Services/Chat/IAutoReplyService.cs) | Service interface |
| [AutoReplyRateLimiter.cs](../../src/VyralBot.Core/Services/Chat/AutoReplyRateLimiter.cs) | 30 s per-user/event/platform cooldown dictionary |
| [ChatEventSettingsService.cs](../../src/VyralBot.Core/Services/Chat/ChatEventSettingsService.cs) | Cached read/write of per-event settings |
| [IChatEventSettingsService.cs](../../src/VyralBot.Core/Services/Chat/IChatEventSettingsService.cs) | Settings service interface |
| [ChatEventSetting.cs](../../src/VyralBot.Data/Entities/ChatEventSetting.cs) | Entity: per (Platform, EventType) row |
| [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) | The live executors — 11 per-event handlers with auto-reply steps, lines 1700-2480 |
| [TwitchChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/TwitchChatSettingsViewModel.cs) | Chat Hub Twitch tab: per-event toggles + templates |
| [KickChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/KickChatSettingsViewModel.cs) | Chat Hub Kick tab: same for Kick events |
| [TwitchChatSettingsView.xaml](../../src/VyralBot.UI/Views/TwitchChatSettingsView.xaml) / [TwitchChatSettingsView.xaml.cs](../../src/VyralBot.UI/Views/TwitchChatSettingsView.xaml.cs) | Twitch per-event cards UI |
| [KickChatSettingsView.xaml](../../src/VyralBot.UI/Views/KickChatSettingsView.xaml) / [KickChatSettingsView.xaml.cs](../../src/VyralBot.UI/Views/KickChatSettingsView.xaml.cs) | Kick per-event cards UI |
| [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs) | `ChatEventSettings` table + seeding of the 13 default rows (lines 2973-3013) |

The template processor — a simple replace, not the variable engine:

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

A live auto-reply step, exactly as the Kick follow handler runs it:

```csharp
if (settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage))
{
    if (_autoReplyService.ShouldSendReply(evt.Username, "Kick", "Follow"))
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
```

## Data & Settings

- **ChatEventSettings** table (per-profile SQLite, see [Database Reference](../reference/database-reference.md)): `Platform`, `EventType`, `ShowInChat`, `AutoReplyEnabled`, `AutoReplyMessage`, `SoundEnabled`, `SoundFile`, `SoundVolume`. Seeded once with 13 default rows (7 Kick + 6 Twitch event types) when the table is empty — all with `AutoReplyEnabled = false`; the seeded templates now use plain `{username}`-style tokens (the `@{...}` defaults were fixed on 2026-08-15).
- The service-level cache is in-memory only; the settings-service cache is invalidated on profile switch via `ProfileSwitchedEvent` (fixed 2026-08-15).
- Rate-limiter state is in-memory per app session and resets on restart.

## Status & Known Issues

- **Auto-replies work today** — this is one of the live event-driven paths (alongside sounds, alerts, and currency bonuses). Test it safely with [Event Testing](event-testing.md).
- **DB/social variables never resolve in auto-replies** — `ProcessTemplate` is a simple replace, unlike command responses which use the full [Variables](variables.md) engine (KI-28).
- **Twitch Gift Sub now has an auto-reply step** — the handler gained a full auto-reply block on 2026-08-15 (it previously only did message/sound/alert/currency).
- **`{raider}` is now supplied by the Twitch Raid handler** — templates using `{raider}` resolve correctly (fixed 2026-08-15); older documentation claiming otherwise is stale.
- **User-written stray-`@` templates** — the processor replaces only `{key}`, so an `@` in front of a variable the event does not supply is still sent verbatim; the shipped defaults no longer do this. — [known issues ledger](../reference/known-issues.md)
- **Rate-limiter cleanup is never scheduled** — `CleanupExpiredEntries` exists but no timer calls it; stale keys accumulate for the session (functionally harmless).
- Conditional rules **are consulted** on the Kick/Twitch reply path — a matching rule replaces the default message — but only when a default message exists, and never on Discord (KI-57); see [Conditional Rules](conditional-rules.md) for the full story.
- Auto-replies only send to **Twitch and Kick**; there are no YouTube/TikTok/Discord auto-replies.
- Twitch events depend on EventSub connecting — `Platform.UserId` is now set on authorization (fixed 2026-08-15), so Twitch auto-replies fire once the broadcaster has (re)authorized. See [Twitch](../platforms/twitch.md).
