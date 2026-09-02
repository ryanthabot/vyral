# Chat Settings

> Where chat behavior is configured: the Chat Hub page with its five platform tabs, the quick-settings popups in the chat window header, and the per-event notification/auto-reply editors — plus the honest differences in what each platform actually persists.

## Overview

Chat settings live in two places that share the same editor views:

1. **The Chat Hub page** — a full settings page with platform tabs (Home → **Chat** card). This is *not* the chat feed; the sidebar **Chat** button opens the [chat window](chat-window.md) instead.
2. **Quick-settings popups** — the per-platform icon buttons in the chat window header open the same editors inline. Handy for flipping an auto-reply template mid-stream without leaving chat.

What you can configure per platform differs sharply: **Twitch and Kick** get per-event editors (show in chat, auto-reply on/off, reply template) stored as `ChatEventSetting` rows; **YouTube, TikTok, and Discord** get only a show/hide messages toggle stored as a flag on the platform row. The event editors themselves are described in detail in [Event Reactions](event-reactions.md).

## User Guide

### Opening the Chat Hub

From the Home page, click the **Chat** card ("Complete chat management for all platforms"). The page titled **Chat Management** appears with five tabs across the top: **Twitch, Kick, YouTube, TikTok, Discord** (Twitch is selected by default).

### Connection status gating

Each tab shows a status bar — "{Platform} Chat Settings" with a green or red dot and "Connected"/"Not Connected". Status comes from the platform's database row, not a live socket check.

If the platform is not connected:

- a red warning appears — "Connect this platform in Connections to manage chat settings" — and
- the whole settings area is dimmed to 40% opacity and disabled.

Connect the platform first on the [Platforms](../platforms/overview.md) page. (Kick's tab is reachable even when only the channel is configured, via the chat header's Kick quick-settings button.)

### The General card (every tab)

Three toggles shown for every platform:

| Toggle | Intended meaning | Actually persisted? |
|---|---|---|
| **Chat Enabled** | Show messages from this platform in the unified chat | YouTube, TikTok, Discord: yes (platform flag, live effect). Kick: via the *Platforms → Kick* page, not here. Twitch: **no — cosmetic**. |
| **Auto-Start Chat** | Connect this platform's chat on app launch | No (Twitch/Kick hub VMs do not persist it) |
| **Auto-Replies** | Send automatic responses to events | No (Twitch/Kick hub VMs do not persist it; the working Kick auto-replies toggle lives in Platforms → Kick settings) |

This table is the single most important thing to understand about this page — see [Status & Known Issues](#status--known-issues).

### Twitch tab — event editors

Below the General card, "Event Notifications & Auto-Replies" contains one collapsible expander per event: **Follow, Subscribe, Resub, Gift Sub, Raid, Cheer**. Each has:

- **Show in chat** — post a gold system message when the event happens.
- **Auto-reply** — send a chat message automatically.
- **Template** — the reply text with variables. Advertised variables per event:

| Event | Variables |
|---|---|
| Follow | `{username}` |
| Subscribe | `{username}`, `{tier}` |
| Resub | `{username}`, `{months}` |
| Gift Sub | `{gifter}`, `{count}` |
| Raid | `{raider}`, `{viewers}` |
| Cheer | `{username}`, `{bits}` |

### Kick tab — event editors

Same pattern for **Follow, Subscribe, Gift Sub, Host, Kicks Gifted, Reward Redeemed**, plus **Stream Status** (show-in-chat only — no auto-reply):

| Event | Variables |
|---|---|
| Follow | `{username}` |
| Subscribe | `{username}`, `{months}` |
| Gift Sub | `{gifter}`, `{count}` |
| Host | `{hoster}` |
| Kicks Gifted | `{sender}`, `{amount}` |
| Reward Redeemed | `{username}`, `{reward}` |
| Stream Status | — |

### YouTube / TikTok / Discord tabs

Display options only: a single **"Show messages ... in the unified chat"** toggle, with a note that event-based auto-replies will arrive in a future update. The toggle takes effect immediately — messages stop or resume in the feed without a restart. (TikTok/Discord have no chat integration at all yet, so their toggle only writes a flag nothing consumes; see [TikTok, Discord & OBS](../platforms/tiktok-discord-obs.md).)

### Saving

- **Twitch / Kick tabs**: click **Save Settings** at the bottom. This persists the event editors (per-event rows in the database). The status line confirms.
- **YouTube / TikTok / Discord tabs**: the Save button is a no-op that just says "Saved" — the show-messages toggle already saved itself the moment you flipped it.

### Quick-settings popups in the chat window

Click a platform's icon button in the [chat window](chat-window.md) header. Twitch and Kick popups show the same event editors as their hub tabs (with their own Save button). YouTube/TikTok/Discord popups show the show-messages toggle plus three decorative checkboxes — "Show Badges", "Show Timestamps", "Compact Mode" — which have **no bindings and do nothing**.

## How It Works (Developer)

- The hub page is [ChatHubView.xaml](../../src/VyralBot.UI/Views/ChatHubView.xaml) (733 lines) + [ChatHubViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatHubViewModel.cs). It is *not* a feed — it is a tab switcher that hosts one of five child settings VMs as `CurrentPlatformSettings` and resolves/initializes all five on hub init.
- Connection gating reads `IDataService.GetPlatformByNameAsync(...)` and compares `Status == "Connected"`; `IsCurrentPlatformConnected` drives the dimming via XAML opacity.
- **Twitch/Kick persistence** ([TwitchChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/TwitchChatSettingsViewModel.cs), [KickChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/KickChatSettingsViewModel.cs)): `InitializeAsync` loads 6 (Twitch) / 7 (Kick) `ChatEventSetting` rows through `IChatEventSettingsService`; `SaveSettingsAsync` writes them back (get-or-new per event type). Kick's StreamStatus event forces `AutoReplyEnabled` off on save. Neither VM persists `IsChatEnabled` / `AutoStartChat` / `AutoRepliesEnabled` — they are observable properties with no writer to storage.
- **YouTube/TikTok/Discord persistence** ([YoutubeChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/YoutubeChatSettingsViewModel.cs) and twins): `OnIsChatEnabledChanged` fires `PersistPlatformFlagAsync`, which saves `Platform.<Platform>ChatEnabled` and publishes `PlatformChatRoutingChangedEvent` on the EventBus. The chat window's `OnPlatformRoutingChangedAsync` flips its per-platform display filter live. `InitializeAsync` force-enables the flag if the platform is connected but the flag is false.
- **Twitch has no publisher**: nothing in the solution publishes `PlatformChatRoutingChangedEvent` for Twitch, and `ChatViewModel.TwitchFilterEnabled` defaults to `true`. The hub's Twitch "Chat Enabled" toggle binds to an unpersisted observable — flipping it changes nothing at runtime.
- **The settings cache**: [ChatEventSettingsService.cs](../../src/VyralBot.Core/Services/Chat/ChatEventSettingsService.cs) lazy-loads all `ChatEventSetting` rows once into a dictionary keyed `"{Platform}:{EventType}"` (guarded by a `SemaphoreSlim`), then serves reads from cache and writes through on save. `InvalidateCache()` exists but is not called on profile switch — see known issues.
- **Quick-settings popups**: [LazyPopupContent.cs](../../src/VyralBot.UI/Controls/LazyPopupContent.cs) defers creating the popup's view until opened. Lifetime inconsistency: `TwitchChatSettingsView`/`KickChatSettingsView` code-behind constructs a **new** ViewModel from `App.ServiceProvider` services each time (fresh-from-DB state), while `YoutubeChatSettingsView` resolves the DI-registered singleton VM.
- Kick's *working* "Enable Kick Chat" and "Enable auto-replies" toggles live on the Platforms → Kick settings page ([KickSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/KickSettingsViewModel.cs)), which persists `Platform.KickChatEnabled` and the `Kick.AutoRepliesEnabled` config key; `ChatViewModel` bootstraps `KickFilterEnabled` from that VM at init.

## Files & Code

| File | Role |
|---|---|
| [ChatHubView.xaml](../../src/VyralBot.UI/Views/ChatHubView.xaml) | Hub page: tabs, status bar, general card, event expanders, Save button |
| [ChatHubViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatHubViewModel.cs) | Tab switching, DB connection status, child VM hosting |
| [TwitchChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/TwitchChatSettingsViewModel.cs) | Twitch: 6 `ChatEventSetting` rows load/save |
| [KickChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/KickChatSettingsViewModel.cs) | Kick: 7 rows load/save (StreamStatus auto-reply forced off) |
| [YoutubeChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/YoutubeChatSettingsViewModel.cs) | YouTube: platform-flag persistence + routing event |
| [TiktokChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/TiktokChatSettingsViewModel.cs) | TikTok: same pattern as YouTube |
| [DiscordChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/DiscordChatSettingsViewModel.cs) | Discord: same pattern as YouTube |
| [TwitchChatSettingsView.xaml](../../src/VyralBot.UI/Views/TwitchChatSettingsView.xaml) | Twitch event-editor UI (hub tab and popup share it) |
| [KickChatSettingsView.xaml](../../src/VyralBot.UI/Views/KickChatSettingsView.xaml) | Kick event-editor UI |
| [YoutubeChatSettingsView.xaml](../../src/VyralBot.UI/Views/YoutubeChatSettingsView.xaml) | YouTube quick-settings (toggle + decorative checkboxes) |
| [TiktokChatSettingsView.xaml](../../src/VyralBot.UI/Views/TiktokChatSettingsView.xaml) | TikTok quick-settings |
| [DiscordChatSettingsView.xaml](../../src/VyralBot.UI/Views/DiscordChatSettingsView.xaml) | Discord quick-settings |
| [ChatEventSettingsService.cs](../../src/VyralBot.Core/Services/Chat/ChatEventSettingsService.cs) | Cached `ChatEventSetting` access (the `IChatEventSettingsService`) |
| [ChatEventSetting.cs](../../src/VyralBot.Data/Entities/ChatEventSetting.cs) | Entity: per (Platform, EventType) row |
| [Platform.cs](../../src/VyralBot.Data/Entities/Platform.cs) | Entity carrying the `<Platform>ChatEnabled` flags |
| [KickSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/KickSettingsViewModel.cs) | The working Kick chat/auto-reply toggles (Platforms page) |
| [LazyPopupContent.cs](../../src/VyralBot.UI/Controls/LazyPopupContent.cs) | Deferred popup host |

The YouTube-family persistence pattern — save the platform flag, then publish the routing event the chat feed listens for (YoutubeChatSettingsViewModel.cs:78):

```csharp
private async Task PersistPlatformFlagAsync(string platformName, bool enabled)
{
    var platform = await _dataService.GetPlatformByNameAsync(platformName);
    if (platform != null)
    {
        platform.YoutubeChatEnabled = enabled;
        platform.UpdatedAt = DateTime.UtcNow;
        await _dataService.SavePlatformAsync(platform);
    }

    // The chat window subscribes to this and flips its display filter live
    var evt = new PlatformChatRoutingChangedEvent { Source = "UI", Platform = platformName, Enabled = enabled };
    await _eventBus.PublishAsync(evt);
}
```

The read-through cache every event editor sits on (ChatEventSettingsService.cs:32) — loaded once, keyed by platform and event type:

```csharp
private async Task EnsureCacheLoadedAsync()
{
    if (_isCacheLoaded) return;

    await _cacheLock.WaitAsync();
    try
    {
        if (_isCacheLoaded) return;

        var allSettings = await _dataService.GetAllChatEventSettingsAsync();
        _settingsCache.Clear();

        foreach (var setting in allSettings)
        {
            var key = GetCacheKey(setting.Platform, setting.EventType); // "{Platform}:{EventType}"
            _settingsCache[key] = setting;
        }
        _isCacheLoaded = true;
    }
    finally
    {
        _cacheLock.Release();
    }
}
```

## Data & Settings

| Data | Where | Written by |
|---|---|---|
| Per-event settings | `ChatEventSettings` table: `Platform` + `EventType` keyed rows with `ShowInChat`, `AutoReplyEnabled`, `AutoReplyMessage` | Twitch/Kick hub tabs and popups (Save Settings) |
| `ChatEventSetting` sound columns | `SoundEnabled`, `SoundFile`, `SoundVolume` (defaults true/null/100) | **Nobody** — orphaned columns with no UI and no callers; real event sounds come from [Sounds](../stream-tools/sounds.md) |
| Platform display flags | `Platform` entity: `TwitchChatEnabled`, `KickChatEnabled`, `YoutubeChatEnabled`, `TiktokChatEnabled`, `DiscordChatEnabled` | YouTube/TikTok/Discord toggles; Kick settings page; auto-connect force-enable |
| Kick auto-replies toggle | Configs key `Kick.AutoRepliesEnabled` | Platforms → Kick settings page |
| General toggles (`AutoStartChat`, `AutoRepliesEnabled` on Twitch/Kick hub VMs) | Nothing | Not persisted anywhere |

Event types with settings rows: Twitch — Follow, Subscribe, Resub, GiftSub, Raid, Cheer. Kick — Follow, Subscribe, GiftSub, Host, KicksGifted, RewardRedeemed, StreamStatus. Everything is per-profile (see [Data Layer](../architecture/data-layer.md)).

## Status & Known Issues

- **Works**: Twitch/Kick per-event editors (load, edit, save, take effect on the next event), YouTube show-messages toggle with live effect, connection-status gating, popup editors.
- **The Twitch "Chat Enabled" toggle is cosmetic**: no code publishes `PlatformChatRoutingChangedEvent` for Twitch and the chat window's Twitch filter defaults to on, so Twitch messages can never be filtered out and the toggle persists nothing. [Known issues](../reference/known-issues.md).
- **`@{username}` template bug**: the default Twitch templates ("Thanks for the follow @{username}!" — TwitchChatSettingsViewModel.cs:34 etc.) use `@{username}`, but the template processor only replaces `{username}`, so replies go out with a stray `@`. Use `{username}` without the `@`, or write `@` before the placeholder explicitly. [Known issues](../reference/known-issues.md).
- **General-card toggles for Twitch/Kick are not persisted**: "Auto-Start Chat" and "Auto-Replies" on the hub write nothing to storage; only the Kick settings page under Platforms persists real equivalents. [Known issues](../reference/known-issues.md).
- **Popup lifetimes are inconsistent**: Twitch/Kick popups build fresh VMs from the database each open; the YouTube popup reuses the DI singleton. Functionally fine, but hub edits and popup edits can show different states. [Known issues](../reference/known-issues.md).
- **Decorative checkboxes**: the YouTube/TikTok/Discord popups' "Show Badges"/"Show Timestamps"/"Compact Mode" checkboxes have no bindings. [Known issues](../reference/known-issues.md).
- **The settings cache is not profile-aware**: switching profiles does not invalidate the `ChatEventSetting` cache, so stale rows from the previous profile can be served until restart. [Known issues](../reference/known-issues.md).
- **First auto-connect force-enables chat flags**: connecting a platform silently flips its `<Platform>ChatEnabled` to true. [Known issues](../reference/known-issues.md).
- See also [Event Reactions](event-reactions.md) for issues that affect the effects these settings control (Twitch events not arriving, the `{raider}` variable never being replaced, Twitch Gift Sub auto-replies not executing).
