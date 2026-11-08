# Platform Dashboards

> The connected-only Twitch and Kick sidebar entries open a stream-manager layout with a live preview of your own channel, an event-driven activity feed, and working quick-action buttons (rewired in the marathon waves — see Status below), while Discord's per-bot dashboards (reached from the always-visible Discord entry) are fully working server/channel managers with no stream preview at all.

## Overview

When Twitch or Kick is connected, the sidebar's Platforms group grows a **Twitch** or **Kick** entry; clicking it opens that platform's dashboard, a stream-manager-style layout with a stream preview, an activity feed, and two panels of quick-action buttons. The pages are near-identical twins (`TwitchDashboardView.xaml` and `KickDashboardView.xaml` share the same skeleton): the **stream preview is real** — it embeds the public web player for your channel in an on-demand WebView2 — the **Activity Feed is live** (follows/subs/raids/cheers and stream-action results stream in via the EventBus), and the **quick-action buttons are wired** (Twitch: Edit Stream Info + APPLY, Add Marker, Clip That, Raid…/Unraid, Clear Chat, Emote Only, Follower Only, Shield Mode; Kick: Clear Chat, Emote Only, Follower Only, Slow Mode) — action outcomes surface in a bound status line and the feed.

**Discord** is a different animal and does not share that skeleton: the sidebar's Discord entry is **always visible** (not connected-only) and opens the [Discord Bots page](discord.md#multiple-bots) — the multi-bot manager. Clicking **OPEN** on a bot card opens that bot's own dashboard: a connection card (status, start/stop/reconnect) and a servers card listing every guild that bot is in with a per-guild text-channel drop-down and **SET ACTIVE CHANNEL**. Everything on it works: the status string is actually displayed (unlike the Twitch/Kick twins), and the channel picker persists that bot's active channel. There is no stream preview — Discord has none to embed.

Reach all of these via the sidebar only. There is no Home card, and the Platforms page does not link here. The Twitch/Kick entries vanish the moment the platform is disconnected; the Discord entry stays because it is also where bots are managed (see [App Shell & Navigation](../home/app-shell.md)).

## User Guide

### Opening a dashboard

Connect Twitch or Kick on the [Platforms](overview.md) page; a matching button appears in the sidebar's Platforms group (logo + name). Click it. There is a naming quirk under the hood — the Twitch/Kick buttons invoke `NavigateToTwitchSettingsCommand` / `NavigateToKickSettingsCommand`, which load the *dashboard* ViewModels, not any settings page (`MainViewModel.cs:2150`, `:2216`). Nothing called "Twitch Settings" exists. The Discord entry uses the honestly named `NavigateToDiscordBotsCommand` (`MainViewModel.cs:2286`); per-bot dashboards open through it via each bot card's **OPEN** button (`MainViewModel.cs:2330`).

### The layout

| Panel | What you see | What works |
|---|---|---|
| **Stream Preview** (top-left, ~2/3 width) | Username + LIVE/OFFLINE badge in the header; a big "SHOW PREVIEW" play button | **Works** — see below. The badge follows the platform's stream-status events (`IsLive`) |
| **Activity Feed** (bottom-left) | Follows, subs, raids, cheers (and stream-action results), newest first | **Works** — populated live from the EventBus; capped at 50 rows |
| **Stream Management** (top-right) | Twitch: Edit Stream Info (+ APPLY expander), Add Marker, Clip That, Raid… (+ GO expander), Unraid; Kick: the same-titled panel holds its chat actions (see below) | **Works** (Twitch) — real Twitch API actions; outcomes surface in the status line and the feed |
| **Chat Moderation** (bottom-right) | Twitch: Clear Chat, Emote Only, Follower Only, Shield Mode; Kick's actions live in its single Stream Management panel: Clear Chat, Emote Only, Follower Only, Slow Mode | **Works** — toggle the chat modes via the platform API |

Panels are separated by `GridSplitter`s you can drag to resize. The preview panel can be hidden with the × in its header.

### The quick actions (what each button actually calls)

Every button routes through one service — `StreamActionsService` (`VyralBot.UI/Services/StreamActionsService.cs`) — which guards on the platform connection, runs the call, and maps the outcome (or exception) to a result surfaced in the status line and the Activity Feed. Nothing ever throws to the UI. The Twitch buttons are real **Helix API** calls on the connected bot/broadcaster account:

| Button (panel) | Twitch Helix action | Notes |
|---|---|---|
| **APPLY** — Edit Stream Info (Stream Management) | `EditChannelInfoAsync` (PATCH `channels`) | Needs a new title or category first; blank fields are left unchanged |
| **Add Marker** (Stream Management) | `CreateStreamMarkerAsync` (`POST /streams/markers`) | Optional description not exposed — sends none |
| **Clip That** (Stream Management) | `CreateClipAsync` (`POST /clips`) | Creates the clip at the current moment |
| **Raid… → GO** (Stream Management) | `RaidChannelAsync` (`POST /raids`) | Requires a target channel name |
| **Unraid** (Stream Management) | `StopRaidAsync` (`DELETE /raids`) | Cancels an in-flight raid |
| **Clear Chat** (Chat Moderation) | `ClearChatAsync` (`DELETE /moderation/chat`) | Purges the whole chat window |
| **Emote Only** (Chat Moderation, toggle) | `SetChatModeAsync(emoteOnly: …)` (`PATCH /chat/settings`) | Toggle reverts on failure so the switch shows the real state |
| **Follower Only** (Chat Moderation, toggle) | `SetChatModeAsync(followerOnly: …)` (`PATCH /chat/settings`) | Same failure-revert behavior |
| **Shield Mode** (Chat Moderation, toggle) | `SetShieldModeAsync` (`PATCH /shield_mode`) | Same failure-revert behavior |

The three moderation toggles are **read back** after a successful flip (`GetChatModeStateAsync` → `RefreshChatSettingsAsync`), so the switches settle on what Twitch actually reports. The Kick panel's buttons (Clear Chat, Emote Only, Follower Only, Slow Mode) go through the same service as documented mod chat commands sent by the bot account, and the YouTube dashboard's stream-info/chat actions go through the `liveBroadcasts` API — per-platform details in [Kick](kick.md) and [YouTube](youtube.md). The auto-moderator's per-platform *enforcement* capability (a different system) is the parity table in the [Moderator Guide](../architecture/moderator-guide.md#auto-mod-platform-parity).

### The stream preview

Click **SHOW PREVIEW** and the app creates a WebView2 on the spot and loads the public player for your own channel — `https://player.twitch.tv/?channel={name}&parent=localhost` or `https://player.kick.com/{name}?parent=localhost`. Audio plays (the URL is loaded with `muted=false`). Click the floating × over the preview to close it; the WebView2 is disposed immediately and its memory reclaimed. The preview is deliberately **not** created until you ask — an idle dashboard spawns zero browser processes.

### Connection status

Each ViewModel computes a status string at initialization: "Connected as {username}" for Twitch; for Kick, "Connected as {username}" when the chat service is live but **"Disconnected (reconnect on app restart)"** when the saved configuration exists yet the service dropped (Kick does not hot-reconnect — restart the app). **Neither string is displayed anywhere**: the views never bind `Status`; the only connection evidence on screen is your username in the preview header. The "Not connected" state is unreachable in practice, since the sidebar entry only exists while connected. The Discord per-bot dashboards are the exception — their view binds `Status` directly, so "Connected as {bot}" is visible on the page.

### The Discord per-bot dashboards

No stream preview, no decorative buttons — two cards per bot, opened with **OPEN** on the bots page:

| Card | What you see | What works |
|---|---|---|
| **Connection** | "Connected as {bot}" status, START / STOP / RECONNECT | **Everything** — the status string is actually displayed here |
| **Servers** | One row per guild (name, member count, text-channel drop-down), SET ACTIVE CHANNEL, owner-ping Test Message | **Everything** — the picker persists that bot's active channel |

The channel selection, permission model, and what each bot does with its active channel are documented in [Discord](discord.md); limitations (the per-bot RECONNECT token quirk, single channel per bot, chat-only events) are in the [known issues ledger](../reference/known-issues.md) (KI-34, KI-36). The single-bot dashboard view (`DiscordDashboardView`) is back and reachable: the Discord Bots page header has a **DASHBOARD** toggle that lazily hosts it as a tab (see below).

## How It Works (Developer)

`NavigateToTwitchSettingsAsync` / `NavigateToKickSettingsAsync` (`MainViewModel.cs:2150`, `:2216`) follow the standard pipeline (guard, null, dispose sweep, scope ceremony) and call each dashboard VM's `InitializeAsync()`, which reads the platform's row from the current profile database: `Status == "Connected"` fills `Username`/`ChannelName` (from `Platform.Username`) and builds `StreamUrl`. The Twitch VM trusts the DB row alone; the Kick VM additionally consults `IKickService.IsConnected` for the connected/disconnected distinction. Both VMs expose `SaveSettingsAsync` (writes `Username` back to the row) — but no view ever binds it, so it is unreachable from the UI.

The preview's on-demand WebView2 (Twitch shown; Kick identical):

```csharp
// TwitchDashboardViewModel.cs:91-103 — browser process exists only while previewing
WebView2InitializeRequested?.Invoke(this, EventArgs.Empty);  // view creates the control
await Task.Delay(500);                                       // let it initialize

if (StreamPreviewWebView?.CoreWebView2 != null)
{
    ShowStreamPreview = true;
    var embedUrl = $"https://player.twitch.tv/?channel={ChannelName}&parent=localhost&muted=false";
    StreamPreviewWebView.CoreWebView2.Navigate(embedUrl);
}
```

The view's code-behind handles `WebView2InitializeRequested` by constructing the `WebView2` programmatically into an empty `Border` (`TwitchDashboardView.xaml.cs:27-49`) — the XAML contains no WebView2 element at all — using a shared temp user-data folder (`%TEMP%\VyralBot_WebView2_Temp`). Closing the preview (`CloseStreamPreview`) disposes the control, nulls it, and calls `GC.Collect()`; the view also disposes on `Unloaded`. This is deliberate: a XAML-declared WebView2 would spawn Edge processes for every dashboard visit whether you previewed or not. (Startup/exit cleanup is now scoped to `msedgewebview2` processes whose command line references `VyralBot_WebView2_Temp` — KI-23 resolved in the [known issues ledger](../reference/known-issues.md).)

The status card that isn't: `Status` is an `[ObservableProperty]` diligently maintained by both VMs, and a `StatusBadgeStyle` in each view renders a LIVE/OFFLINE badge from `IsLive` — `IsLive` is now fed by the platform's stream-status events (`IsLive = e.IsLive`), so the badge tracks real live state, but no element binds `Status` itself (the visible status line binds `StatusMessage`, the action-outcome text).

## Files & Code

| File | Role |
|---|---|
| [TwitchDashboardViewModel.cs](../../src/VyralBot.UI/ViewModels/TwitchDashboardViewModel.cs) | Twitch dashboard state: status from DB row, on-demand preview lifecycle, unused save/live state |
| [KickDashboardViewModel.cs](../../src/VyralBot.UI/ViewModels/KickDashboardViewModel.cs) | Kick twin — adds `IKickService.IsConnected` check for the "reconnect on app restart" state |
| [TwitchDashboardView.xaml](../../src/VyralBot.UI/Views/TwitchDashboardView.xaml) / [KickDashboardView.xaml](../../src/VyralBot.UI/Views/KickDashboardView.xaml) | Shared stream-manager skeleton; wired quick-action buttons, event-driven activity feed, on-demand WebView2 preview |
| [TwitchDashboardView.xaml.cs](../../src/VyralBot.UI/Views/TwitchDashboardView.xaml.cs) / [KickDashboardView.xaml.cs](../../src/VyralBot.UI/Views/KickDashboardView.xaml.cs) | On-demand WebView2 creation into `%TEMP%\VyralBot_WebView2_Temp`, disposal on close/unload |
| [DiscordDashboardViewModel.cs](../../src/VyralBot.UI/ViewModels/DiscordDashboardViewModel.cs) | Single-bot (setup bot) Discord dashboard: status, guild list with member counts, per-guild text-channel drop-downs, active-channel picker, test message — hosted as the **DASHBOARD** tab on the Discord Bots page |
| [DiscordBotDetailViewModel.cs](../../src/VyralBot.UI/ViewModels/DiscordBotDetailViewModel.cs) | Per-bot dashboard: start/stop/reconnect, guild snapshot, per-bot active-channel picker, owner-ping test — no WebView2 |
| [DiscordBotDetailView.xaml](../../src/VyralBot.UI/Views/DiscordBotDetailView.xaml) | Two-card layout per bot: connection status (bound, visible) + servers/channel list |
| [StreamActionsService.cs](../../src/VyralBot.UI/Services/StreamActionsService.cs) | The quick-action backend: Twitch via Helix, Kick via mod chat commands, YouTube via liveBroadcasts — every failure returns a result, never throws |
| [MainViewModel.cs](../../src/VyralBot.UI/ViewModels/MainViewModel.cs) | `NavigateToTwitchSettingsAsync` / `NavigateToKickSettingsAsync` (`2150`, `:2216`) — misleadingly named; `NavigateToDiscordBotsAsync` (`2286`) + `NavigateToDiscordBotDetailAsync` (`2330`) — honestly named |
| [MainWindow.xaml](../../src/VyralBot.UI/MainWindow.xaml) | Connected-only Twitch/Kick sidebar entries (`264-281`, bound to `IsTwitchConnected`/`IsKickConnected`); always-visible Discord entry (`301-311`); dashboard/bots DataTemplates |

## Data & Settings

- Each dashboard reads one row from the current profile's `Platforms` table on every visit (`InitializeAsync` → `GetPlatformByNameAsync`) — `Status == "Connected"` gates everything the page shows. Nothing on the Twitch/Kick pages writes back through the UI (`SaveSettingsAsync` is unbound); the Discord per-bot dashboards go through the `DiscordBots` table instead — SET ACTIVE CHANNEL persists that bot's `SelectedChannelId` and start/stop update its `Status` (see [Discord](discord.md) and [Data Layer](../architecture/data-layer.md)).
- The preview WebView2 keeps its cache in `%TEMP%\VyralBot_WebView2_Temp` — a machine-level temp folder shared by both dashboards, not per-profile storage.
- Panel layout (splitter positions, hidden preview panel) resets on every visit; nothing is persisted.

## Status & Known Issues

- **Works**: connected-only Twitch/Kick sidebar entries; on-demand Twitch/Kick player preview with audio; closing the preview frees the WebView2 immediately; splitters/panel hide behave. The quick-action buttons are wired (Twitch stream actions + chat modes on both platforms — outcomes surface in the status line and Activity Feed), the Activity Feed is live (EventBus-driven, capped at 50 rows), and the LIVE/OFFLINE badge follows stream-status events. The Discord bots page and every per-bot dashboard work end to end (status, start/stop/reconnect, servers, channel picker, test message — see [Discord](discord.md)).
- **Connection `Status` string never displayed** — "Connected as …" and Kick's "Disconnected (reconnect on app restart)" are computed and then bound to nothing in the Twitch/Kick views; the visible status line shows action outcomes (`StatusMessage`) instead. You cannot see connection state itself on those dashboards. (The Discord per-bot dashboards do display their status.)
- **Misleading command names** — `NavigateToTwitchSettingsCommand` loads `TwitchDashboardViewModel` (`MainViewModel.cs:2150`), not settings; platform settings actually live on the [Platforms](overview.md) page.
- `SaveSettingsAsync` in both VMs is unreachable — no view binds its command.
- **The single-bot Discord dashboard is back as a tab** — the Discord Bots page header's **DASHBOARD** toggle shows it (servers with member counts, per-guild channel drop-downs, active-channel picker, connection status, test message). The view is created lazily from DI on first open, so an unopened tab costs nothing.
- **YouTube has its own dashboard sibling** — connected-only sidebar entry, same skeleton (stream preview over the channel's live page, event-driven activity feed, stream-info/chat actions); see [YouTube](youtube.md). Discord has bot dashboards because each bot needs a place to pick its active channel — see [Discord](discord.md).
