# Chat Settings

> Where chat behavior is configured: the Chat Hub page with its five platform tabs, the quick-settings popups in the chat window header, and the per-event notification/auto-reply editors — plus the honest differences in what each platform actually persists.

## Overview

Chat settings live in two places that share the same editor views:

1. **The Chat Hub page** — a full settings page with platform tabs (Home → **Chat** card). This is *not* the chat feed; the sidebar **Chat** button opens the [chat window](chat-window.md) instead.
2. **Quick-settings popups** — the per-platform icon buttons in the chat window header open the same editors inline. Handy for flipping an auto-reply template mid-stream without leaving chat.

Since the 1.1.0 ChatHub parity pass, every platform's tab follows the same shape: a **General** card (Chat Enabled / Auto-Start / Auto-Replies), a **Display Options** card (badges/timestamps/compact preferences on YouTube/TikTok/Discord), and — where the platform has live events — per-event editors (show in chat, auto-reply on/off, reply template) stored as `ChatEventSetting` rows. **Twitch** (7 events) and **Kick** (7 events) have full editors; **YouTube** (Membership, Super Chat) and **Discord** (Member Join, Server Boost) gained auto-reply editors for their live events; **TikTok** has preferences only (no chat pipeline exists). The event editors themselves are described in detail in [Event Reactions](event-reactions.md).

## User Guide

### Opening the Chat Hub

From the Home page, click the **Chat** card ("Complete chat management for all platforms"). The page titled **Chat Management** appears with five tabs across the top: **Twitch, Kick, YouTube, TikTok, Discord** (Twitch is selected by default).

### Connection status gating

Each tab shows a status bar — "{Platform} Chat Settings" with a green or red dot and "Connected"/"Not Connected", plus a **Refresh Tokens** button (enabled where the platform supports token refresh). Status comes from the platform's database row, not a live socket check. The page header also carries a **Moderator** shortcut chip that jumps straight to the [Moderator dashboard](../architecture/moderator-guide.md).

If the platform is not connected:

- a red warning appears — "Connect this platform in Connections to manage chat settings" — and
- the whole settings area is dimmed to 40% opacity and disabled.

TikTok gets honest gating instead of the generic advice: its Connect/Disconnect buttons are hidden entirely (the commands would no-op), the warning reads "TikTok chat is not available in this version", a bordered **coming-soon banner** explains that VyralBot cannot read or send TikTok LIVE chat yet, and its settings area is dimmed like any unconnected platform.

Connect the platform first on the [Platforms](../platforms/overview.md) page. (Kick's tab is reachable even when only the channel is configured, via the chat header's Kick quick-settings button.)

### The General card (every tab)

Three toggles shown for every platform:

| Toggle | Intended meaning | Actually persisted? |
|---|---|---|
| **Chat Enabled** | Show messages from this platform in the unified chat | Yes for every platform, with live effect. YouTube/TikTok/Discord: platform flag + routing event. Kick: via the *Platforms → Kick* page. Twitch: platform flag + routing event (fixed 2026-08-15). Since the parity pass this General-card toggle is the *only* show/hide switch — the old per-tab "Show messages" rows on YouTube/TikTok/Discord are gone. |
| **Auto-Start Chat** | Connect this platform's chat on app launch | Yes on every platform — Twitch/Kick/YouTube/TikTok/Discord all persist a `<Platform>ChatAutoStart` flag (or per-platform config key) when the toggle flips. |
| **Auto-Replies** | Master switch for automatic responses to events | Yes on every platform — each VM persists `Chat.<Platform>.AutoRepliesEnabled` (or the Kick config key) immediately. The Twitch, Kick, YouTube and Discord master toggles are all consulted live by the event handlers in the chat engine (YouTube/Discord re-load on routing changes); TikTok's persists with no pipeline to consume it. |

This table is the single most important thing to understand about this page — see [Status & Known Issues](#status--known-issues).

### Twitch tab — event editors

Below the General card, "Event Notifications & Auto-Replies" contains one collapsible expander per event: **Follow, Subscribe, Resub, Gift Sub, Raid, Cheer, Channel Point Redemption** (added 1.1.0 — the redemption handler went live 2026-11-07 and now has its editor). Each has:

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
| Channel Point Redemption | `{username}`, `{reward}`, `{cost}`, `{input}` |

### Kick tab — event editors

Same pattern for **Follow, Subscribe, Gift Sub, Host, Kicks Gifted, Reward Redeemed**, plus **Stream Status** (show-in-chat only — no auto-reply). The variables each expander advertises on the hub tab:

| Event | Variables (hub tab hint) |
|---|---|
| Follow | `{username}` |
| Subscribe | `{username}` |
| Gift Sub | `{gifter}`, `{count}` |
| Host | `{hoster}` |
| Kicks Gifted | `{sender}`, `{amount}` |
| Reward Redeemed | `{username}`, `{reward}` |
| Stream Status | — |

The hints are conservative: the handlers actually pass more keys than the tab advertises (Kick Subscribe also receives `{months}` and `{message}`, Host also `{viewers}`, and every handler adds `{platform}` — the full per-event list is in [Event Reactions](event-reactions.md)). The chat-header popup's Kick editor shows the richer hints for Follow/Subscribe/Host than the hub tab does; the hub tab is the accurate minimum.

### YouTube tab — display preferences + event editors

**Display Options** holds three checkboxes — **Show Badges, Show Timestamps, Compact Mode** — persisted immediately to `Chat.YouTube.*` config keys (not yet consumed by the feed; the live appearance controls are the chat window header's font-size and timestamp toggles). Below it, **Event Notifications & Auto-Replies** covers YouTube's two live event families:

| Event | Controls | Variables |
|---|---|---|
| Membership (milestone renewals count as Resub) | Auto-reply + template only — the chat line always appears | `{username}`, `{tier}` |
| Super Chat | Auto-reply + template only — the chat line always appears | `{username}`, `{amount}`, `{message}` |

The handlers read the `YouTube/Subscribe` and `YouTube/Cheer` `ChatEventSetting` rows (shared vocabulary, so conditional rules match across platforms) and gate on the Auto-Replies master toggle. Alerts and sounds for these events are configured on the [Visual Alerts](../stream-tools/visual-alerts.md) and [Sounds](../stream-tools/sounds.md) pages.

### Discord tab — display preferences + event editors

Same shape: the three display checkboxes (`Chat.Discord.*` keys), then **Event Notifications & Auto-Replies**:

| Event | Controls | Variables |
|---|---|---|
| Member Join | Show in chat + auto-reply + template | `{username}`, `{guild}` |
| Server Boost | Show in chat + auto-reply + template | `{username}`, `{guild}` |

Rows persist as `Discord/Follow` (join) and `Discord/Subscribe` (boost) `ChatEventSetting` rows. The events themselves need the **Server Members intent** portal toggle ([KI-52](../reference/known-issues.md)) — until it is on, the settings save but joins/boosts never arrive.

### TikTok tab — preferences only, gated

The coming-soon banner stays (no TikTok chat pipeline exists), the whole card is dimmed by the same gate, and inside are the three display checkboxes — stored now under `Chat.TikTok.*`, taking effect only once TikTok chat ships. There is no dead toggle promising live behavior (see [TikTok & OBS](../platforms/tiktok-discord-obs.md)).

### Saving

- **Twitch / Kick / YouTube / Discord tabs**: click **Save Settings** at the bottom — it persists the event editors as `ChatEventSetting` rows (and the display checkboxes). The status line confirms.
- **TikTok tab**: Save persists the display preferences only (there are no event rows). The General-card toggles all save themselves the moment they flip.

### Quick-settings popups in the chat window

Click a platform's icon button in the [chat window](chat-window.md) header. Twitch and Kick popups show the same event editors as their hub tabs (with their own Save button). The YouTube/TikTok/Discord popups predate the parity pass and still show the old show-messages toggle plus the three display checkboxes; the hub tabs are now the richer, current surface (event editors, General-card Chat Enabled). The checkboxes **persist** to per-platform `Chat.<Platform>.*` config keys but are not yet consumed by the feed. The real, live appearance controls are in the chat window header itself: the font-size +/- buttons and the timestamp toggle (saved to `Chat.Appearance`).

## How It Works (Developer)

- The hub page is [ChatHubView.xaml](../../src/VyralBot.UI/Views/ChatHubView.xaml) (~960 lines after the parity pass) + [ChatHubViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatHubViewModel.cs). It is *not* a feed — it is a tab switcher that hosts one of five child settings VMs as `CurrentPlatformSettings` and resolves/initializes all five on hub init.
- Connection gating reads `IDataService.GetPlatformByNameAsync(...)` and compares `Status == "Connected"`; `IsCurrentPlatformConnected` drives the dimming via XAML opacity.
- **Twitch/Kick persistence** ([TwitchChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/TwitchChatSettingsViewModel.cs), [KickChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/KickChatSettingsViewModel.cs)): `InitializeAsync` loads 7 (Twitch) / 7 (Kick) `ChatEventSetting` rows through `IChatEventSettingsService` (Twitch gained `RewardRedeemed` in the parity pass); `SaveSettingsAsync` writes them back (get-or-new per event type). Both VMs also persist the badges/timestamps/compact display keys. The Twitch VM also persists `Platform.TwitchChatEnabled` and publishes `PlatformChatRoutingChangedEvent` (fixed 2026-08-15), matching the YouTube-family pattern. Both VMs now persist the General-card flags too: `AutoStartChat` → the platform row's `<Platform>ChatAutoStart` column, and `AutoRepliesEnabled` → the `Chat.<Platform>.AutoRepliesEnabled` config key (Twitch's publishes a routing event so `ChatViewModel` re-reads the master toggle live; Kick's writes the legacy `Kick.AutoRepliesEnabled` key). Kick's StreamStatus event forces `AutoReplyEnabled` off on save.
- **YouTube/TikTok/Discord persistence** ([YoutubeChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/YoutubeChatSettingsViewModel.cs) and twins): `OnIsChatEnabledChanged` fires `PersistPlatformFlagAsync`, which saves `Platform.<Platform>ChatEnabled` and publishes `PlatformChatRoutingChangedEvent` on the EventBus — the hub's General-card **Chat Enabled** is that same toggle for these tabs. The chat window's `OnPlatformRoutingChangedAsync` flips its per-platform display filter live (and now also re-loads the YouTube/Discord auto-replies master toggles). Since the parity pass the YouTube VM loads/saves `YouTube/Subscribe` + `YouTube/Cheer` event rows and the Discord VM `Discord/Follow` + `Discord/Subscribe` (joining the Twitch/Kick pattern); TikTok persists only the `Chat.TikTok.*` display keys. `ChatViewModel` consults the YouTube and Discord auto-replies master toggles live in the membership/Super Chat and join/boost handlers.
- **Twitch routing parity**: `TwitchChatSettingsViewModel` persists `Platform.TwitchChatEnabled` and publishes `PlatformChatRoutingChangedEvent`, and `ChatViewModel.TwitchFilterEnabled` updates from that event like the other platforms (fixed 2026-08-15).
- **The settings cache**: [ChatEventSettingsService.cs](../../src/VyralBot.Core/Services/Chat/ChatEventSettingsService.cs) lazy-loads all `ChatEventSetting` rows once into a dictionary keyed `"{Platform}:{EventType}"` (guarded by a `SemaphoreSlim`), then serves reads from cache and writes through on save. It subscribes to `ProfileSwitchedEvent` (constructor-injected `IEventBus`) and calls `InvalidateCache()` on every profile switch (fixed 2026-08-15).
- **Quick-settings popups**: [LazyPopupContent.cs](../../src/VyralBot.UI/Controls/LazyPopupContent.cs) defers creating the popup's view until opened. Lifetime inconsistency: `TwitchChatSettingsView`/`KickChatSettingsView` code-behind constructs a **new** ViewModel from `App.ServiceProvider` services each time (fresh-from-DB state), while `YoutubeChatSettingsView` resolves the DI-registered singleton VM.
- Kick's *working* "Enable Kick Chat" and "Enable auto-replies" toggles live on the Platforms → Kick settings page ([KickSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/KickSettingsViewModel.cs)), which persists `Platform.KickChatEnabled` and the `Kick.AutoRepliesEnabled` config key; `ChatViewModel` bootstraps `KickFilterEnabled` from that VM at init.

## Files & Code

| File | Role |
|---|---|
| [ChatHubView.xaml](../../src/VyralBot.UI/Views/ChatHubView.xaml) | Hub page: tabs, status bar, general card, event expanders, Save button |
| [ChatHubViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatHubViewModel.cs) | Tab switching, DB connection status, child VM hosting |
| [TwitchChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/TwitchChatSettingsViewModel.cs) | Twitch: 7 `ChatEventSetting` rows load/save (incl. Channel Point Redemption) |
| [KickChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/KickChatSettingsViewModel.cs) | Kick: 7 rows load/save (StreamStatus auto-reply forced off) |
| [YoutubeChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/YoutubeChatSettingsViewModel.cs) | YouTube: platform flag + routing event, `YouTube/Subscribe` + `YouTube/Cheer` event rows, display keys |
| [TiktokChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/TiktokChatSettingsViewModel.cs) | TikTok: platform flag + display keys (no event rows) |
| [DiscordChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/DiscordChatSettingsViewModel.cs) | Discord: platform flag + `Discord/Follow` + `Discord/Subscribe` event rows, display keys |
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
| Platform display flags | `Platform` entity: `TwitchChatEnabled`, `KickChatEnabled`, `YoutubeChatEnabled`, `TiktokChatEnabled`, `DiscordChatEnabled` | All five settings surfaces (Twitch/Kick/YouTube/TikTok/Discord); auto-connect force-enable (intended behavior — KI-14) |
| Auto-start flags | `Platform` entity `<Platform>ChatAutoStart` columns (+ YouTube-family config keys) | Every platform's chat-settings VM (immediate persist on toggle) |
| Auto-replies master toggles | Configs keys `Chat.Twitch.AutoRepliesEnabled`, `Chat.<Platform>.AutoRepliesEnabled` (YouTube/TikTok/Discord), `Kick.AutoRepliesEnabled` | Every platform's chat-settings VM; Twitch + Kick flags are the ones the chat engine's event handlers consult |
| Kick auto-replies toggle (legacy) | Configs key `Kick.AutoRepliesEnabled` | Platforms → Kick settings page and the Kick chat-settings tab (same key) |

Event types with settings rows: Twitch — Follow, Subscribe, Resub, GiftSub, Raid, Cheer, RewardRedeemed. Kick — Follow, Subscribe, GiftSub, Host, KicksGifted, RewardRedeemed, StreamStatus. YouTube — Subscribe (Membership), Cheer (Super Chat). Discord — Follow (Member Join), Subscribe (Server Boost). Everything is per-profile (see [Data Layer](../architecture/data-layer.md)).

## Status & Known Issues

- **Works**: Twitch/Kick per-event editors (load, edit, save, take effect on the next event), the YouTube Membership/Super Chat and Discord join/boost auto-reply editors (1.1.0 parity pass), show-messages toggles with live effect on every connected platform (General card Chat Enabled), connection-status gating, popup editors, and the General-card Auto-Start/Auto-Replies toggles persisting on every platform.
- **General-card toggles all persist now** — "Auto-Start Chat" and "Auto-Replies" write immediately on every platform (Twitch/Kick/YouTube/TikTok/Discord), replacing the old write-nothing behavior. Since the 1.1.0 parity pass the per-event auto-reply *editors* exist on Twitch, Kick, YouTube (Membership/Super Chat) and Discord (join/boost) — TikTok alone has none (no pipeline). [Known issues](../reference/known-issues.md).
- **Default templates no longer emit a stray `@`** — the Twitch/Kick defaults and seeds now use plain `{username}` tokens (fixed 2026-08-15). Templates *you* wrote with `@{var}` still send the `@` literally if the event does not supply `{var}`. [Known issues](../reference/known-issues.md).
- **Popup lifetimes are inconsistent**: Twitch/Kick popups build fresh VMs from the database each open; the YouTube popup reuses the DI singleton. Functionally fine, but hub edits and popup edits can show different states. [Known issues](../reference/known-issues.md).
- **Popup appearance checkboxes persist but are inert**: the YouTube/TikTok popups' "Show Badges"/"Show Timestamps"/"Compact Mode" checkboxes save to `Chat.<Platform>.*` config keys, but the feed does not read them yet — the working timestamp/font-size controls live in the chat window header. [Known issues](../reference/known-issues.md).
- **The settings cache is profile-aware now**: `ChatEventSettingsService` invalidates its cache on `ProfileSwitchedEvent` (fixed 2026-08-15), so hot switches load the new profile's rows immediately.
- **First auto-connect force-enables chat flags**: connecting a platform silently flips its `<Platform>ChatEnabled` to true. This is **intended behavior** (KI-14): a connected platform is expected to appear in the feed by default. [Known issues](../reference/known-issues.md).
- See also [Event Reactions](event-reactions.md) for issues that affect the effects these settings control (the ShowInChat gating inconsistency, KI-26).
