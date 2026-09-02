# Platform Dashboards

> The Twitch and Kick dashboard pages — connected-only sidebar entries that promise a stream-manager layout with a live preview of your own channel, of which exactly one panel (the on-demand stream preview) actually works.

## Overview

When Twitch or Kick is connected, the sidebar's Platforms group grows a **Twitch** or **Kick** entry; clicking it opens that platform's dashboard, a stream-manager-style layout with a stream preview, an activity feed, and two panels of quick-action buttons. The pages are near-identical twins (`TwitchDashboardView.xaml` and `KickDashboardView.xaml` are both 498 lines with the same skeleton), and the honest summary is: the **stream preview is real** — it embeds the public web player for your channel in an on-demand WebView2 — while the Activity Feed and all twelve quick-action buttons are static decoration.

Reach them via the sidebar only. There is no Home card, and the Platforms page does not link here. The entries vanish the moment the platform is disconnected (see [App Shell & Navigation](../home/app-shell.md)).

## User Guide

### Opening a dashboard

Connect Twitch or Kick on the [Platforms](overview.md) page; a **Twitch**/**Kick** button appears in the sidebar's Platforms group (logo + name). Click it. There is a naming quirk under the hood — the buttons invoke `NavigateToTwitchSettingsCommand` / `NavigateToKickSettingsCommand`, which load the *dashboard* ViewModels, not any settings page (`MainViewModel.cs:1490-1553`). Nothing called "Twitch Settings" exists.

### The layout

| Panel | What you see | What works |
|---|---|---|
| **Stream Preview** (top-left, ~2/3 width) | Username + LIVE/OFFLINE badge in the header; a big "SHOW PREVIEW" play button | **Works** — see below. The badge always reads OFFLINE (nothing sets `IsLive`) |
| **Activity Feed** (bottom-left) | "It's quiet. Too quiet…" placeholder text | Nothing — static text, never populated |
| **Stream Management** (top-right) | 8 outlined buttons (Edit Stream Info, Add Stream Marker, Marker + Desc, Clip That, Stop Raids 1hr, Raid Channel, then Twitch: Manage Goals / Stream Together, Kick: Set Goals / Host Channel) | Nothing — no commands bound |
| **Chat Moderation** (bottom-right) | 4 outlined buttons (Clear Chat, Emote Only, Follower Only, then Twitch: Shield Mode, Kick: Subscriber Only) | Nothing — no commands bound |

Panels are separated by `GridSplitter`s you can drag to resize. The preview panel can be hidden with the × in its header.

### The stream preview

Click **SHOW PREVIEW** and the app creates a WebView2 on the spot and loads the public player for your own channel — `https://player.twitch.tv/?channel={name}&parent=localhost` or `https://player.kick.com/{name}?parent=localhost`. Audio plays (the URL is loaded with `muted=false`). Click the floating × over the preview to close it; the WebView2 is disposed immediately and its memory reclaimed. The preview is deliberately **not** created until you ask — an idle dashboard spawns zero browser processes.

### Connection status

Each ViewModel computes a status string at initialization: "Connected as {username}" for Twitch; for Kick, "Connected as {username}" when the chat service is live but **"Disconnected (reconnect on app restart)"** when the saved configuration exists yet the service dropped (Kick does not hot-reconnect — restart the app). **Neither string is displayed anywhere**: the views never bind `Status`; the only connection evidence on screen is your username in the preview header. The "Not connected" state is unreachable in practice, since the sidebar entry only exists while connected.

## How It Works (Developer)

`NavigateToTwitchSettingsAsync` / `NavigateToKickSettingsAsync` (`MainViewModel.cs:1490-1553`) follow the standard pipeline (guard, null, dispose sweep, scope ceremony) and call each dashboard VM's `InitializeAsync()`, which reads the platform's row from the current profile database: `Status == "Connected"` fills `Username`/`ChannelName` (from `Platform.Username`) and builds `StreamUrl`. The Twitch VM trusts the DB row alone; the Kick VM additionally consults `IKickService.IsConnected` for the connected/disconnected distinction. Both VMs expose `SaveSettingsAsync` (writes `Username` back to the row) — but no view ever binds it, so it is unreachable from the UI.

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

The view's code-behind handles `WebView2InitializeRequested` by constructing the `WebView2` programmatically into an empty `Border` (`TwitchDashboardView.xaml.cs:27-49`) — the XAML contains no WebView2 element at all — using a shared temp user-data folder (`%TEMP%\VyralBot_WebView2_Temp`). Closing the preview (`CloseStreamPreview`) disposes the control, nulls it, and calls `GC.Collect()`; the view also disposes on `Unloaded`. This is deliberate: a XAML-declared WebView2 would spawn Edge processes for every dashboard visit whether you previewed or not. (Startup/exit also kill WebView2 processes machine-wide — KI-23 in the [known issues ledger](../reference/known-issues.md).)

The status card that isn't: `Status` is an `[ObservableProperty]` diligently maintained by both VMs, and a `StatusBadgeStyle` in each view renders a LIVE/OFFLINE badge from `IsLive` — but no code path ever sets `IsLive`, and no element binds `Status`. The badges read OFFLINE forever.

## Files & Code

| File | Role |
|---|---|
| [TwitchDashboardViewModel.cs](../../src/VyralBot.UI/ViewModels/TwitchDashboardViewModel.cs) | Twitch dashboard state: status from DB row, on-demand preview lifecycle, unused save/live state |
| [KickDashboardViewModel.cs](../../src/VyralBot.UI/ViewModels/KickDashboardViewModel.cs) | Kick twin — adds `IKickService.IsConnected` check for the "reconnect on app restart" state |
| [TwitchDashboardView.xaml](../../src/VyralBot.UI/Views/TwitchDashboardView.xaml) / [KickDashboardView.xaml](../../src/VyralBot.UI/Views/KickDashboardView.xaml) | Identical 498-line stream-manager layouts; 12 decorative quick-action buttons, no commands |
| [TwitchDashboardView.xaml.cs](../../src/VyralBot.UI/Views/TwitchDashboardView.xaml.cs) / [KickDashboardView.xaml.cs](../../src/VyralBot.UI/Views/KickDashboardView.xaml.cs) | On-demand WebView2 creation into `%TEMP%\VyralBot_WebView2_Temp`, disposal on close/unload |
| [MainViewModel.cs](../../src/VyralBot.UI/ViewModels/MainViewModel.cs) | `NavigateToTwitchSettingsAsync` / `NavigateToKickSettingsAsync` (`1490-1553`) — misleadingly named |
| [MainWindow.xaml](../../src/VyralBot.UI/MainWindow.xaml) | Connected-only sidebar entries (`196-220`, bound to `IsTwitchConnected`/`IsKickConnected`); dashboard DataTemplates |

## Data & Settings

- Each dashboard reads one row from the current profile's `Platforms` table on every visit (`InitializeAsync` → `GetPlatformByNameAsync`) — `Status == "Connected"` gates everything the page shows. Nothing on the page writes back through the UI (`SaveSettingsAsync` is unbound); see [Data Layer](../architecture/data-layer.md).
- The preview WebView2 keeps its cache in `%TEMP%\VyralBot_WebView2_Temp` — a machine-level temp folder shared by both dashboards, not per-profile storage.
- Panel layout (splitter positions, hidden preview panel) resets on every visit; nothing is persisted.

## Status & Known Issues

- **Works**: connected-only sidebar entries; on-demand Twitch/Kick player preview with audio; closing the preview frees the WebView2 immediately; splitters/panel hide behave.
- **Status string never displayed** — "Connected as …" and Kick's "Disconnected (reconnect on app restart)" are computed and then bound to nothing; you cannot see connection state on the dashboard itself.
- **LIVE/OFFLINE badge is stuck on OFFLINE** — `IsLive` has no writer in either VM.
- **Twelve decorative buttons** — Stream Management and Chat Moderation panels have no command bindings (part of the dead-button inventory, KI-17 in the [known issues ledger](../reference/known-issues.md)); the Activity Feed is static placeholder text.
- **Misleading command names** — `NavigateToTwitchSettingsCommand` loads `TwitchDashboardViewModel` (`MainViewModel.cs:1490`), not settings; platform settings actually live on the [Platforms](overview.md) page.
- `SaveSettingsAsync` in both VMs is unreachable — no view binds its command.
- There is deliberately no YouTube dashboard, even though YouTube connections work (no sidebar entry either) — see [YouTube](youtube.md).
