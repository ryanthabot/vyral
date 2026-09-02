# Visual Alerts

> The pop-up overlay Vyral draws over your screen when someone follows, subscribes, raids, and more — a WPF window, not a browser source — plus the Alert Settings page, which persists and drives real alerts.

## Overview

When a stream event fires, Vyral shows an alert: a borderless, always-on-top window that slides or fades in over your primary screen with a title ("New Follower!"), a username, an optional message line, and an optional image. After the configured duration it fades out and closes by itself.

Two things make this feature unusual compared to tools like Stream Elements or Alertbox:

- **It is an in-app WPF window, not a browser source.** There is no overlay URL to add to OBS ([Browser Sources](browser-sources.md) covers the one HTML page that does exist — audio only). The alert appears over the streamer's own screen, so viewers see it only if your OBS setup captures that screen (display capture).
- **The Alert Settings page is real.** Since the 2026-08-15 fix it persists to the `AlertSettings` Configs key, and `AlertService` applies the saved master toggle, duration, animation, position, and per-event switches to real alerts (`applySettings = true` by default). The page's **Test Alert** button previews your *unsaved* selections, which is handy for trying animations before committing.

## User Guide

Open **Stream Tools → Visual Alerts** in the sidebar. (The "Events & Alerts" card on the Tools hub page goes to the same place — ToolsView.xaml:59 and :174 both route here.)

### Trying it out

Click **Test Alert**. An overlay window appears with "Test Alert!" for user "TestUser123", using whatever animation, position, and duration are currently selected on the page — a preview of unsaved selections. This is the best way to try the four animations and nine positions before pressing Save.

### The General Settings card

- **Enable Visual Alerts** — master on/off. With it off, real alerts are suppressed (Test Alert still previews).
- **Alert Duration (seconds)** — slider for how long the alert stays on screen (1–15). Applies to real alerts and the test alike; the default is 5.
- **Animation Style** — dropdown with **SlideInFromTop**, **SlideInFromBottom**, **FadeIn**, and **Bounce**.
- **Screen Position** — dropdown with nine positions across a 3x3 grid (Top/Center/Bottom x Left/Center/Right).

### The Alert Events card

Six toggles — **New Followers, New Subscribers, Gifted Subscriptions, Raids, Hosts, Bits/Cheers** — choose which event categories produce real alerts. Events outside a enabled category are skipped.

### Save and Reset

- **Save Settings** — persists everything on the page to the `AlertSettings` Configs key (JSON) and confirms with "Alert settings saved successfully!". Real alerts use the saved values from the next event on.
- **Reset to Defaults** — returns the on-screen controls to their defaults (on, 5s, SlideInFromTop, TopCenter, all events on). Save afterwards to persist the reset.

### What actually triggers alerts

Real alerts come from the chat event handlers (the same handlers that play [sounds](sounds.md) — see [Event Reactions](../chat/event-reactions.md)). Verified call sites in [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs):

| Event | Alert title | Call site |
|---|---|---|
| Kick follow | New Follower! | ChatViewModel.cs:1728 |
| Kick subscribe | New Subscriber! | ChatViewModel.cs:1817 |
| Kick gift sub | Gift Subs! | ChatViewModel.cs:1887 |
| Kick host | New Host! | ChatViewModel.cs:1953 |
| Kick kicks gifted | Kicks Gifted! | ChatViewModel.cs:1997 |
| Kick reward redeemed | Reward Redeemed! | ChatViewModel.cs:2042 |
| Twitch follow | New Follower! | ChatViewModel.cs:2116 |
| Twitch subscribe | New Subscriber! | ChatViewModel.cs:2196 |
| Twitch resub | Resubscribed! | ChatViewModel.cs:2276 |
| Twitch gift sub | Gift Subs! | ChatViewModel.cs:2338 |
| Twitch raid | Raid! | ChatViewModel.cs:2381 |
| Twitch cheer (bits) | Bits! | ChatViewModel.cs:2452 |

You can fire most of these on demand from the [Event Testing](../chat-features/event-testing.md) page, which pushes synthetic events through the real pipeline.

### Behavior details worth knowing

- **One alert at a time.** A new alert immediately replaces the one currently showing — there is no queue. A raid arriving during a sub alert cuts the sub alert short.
- **Auto-close.** The alert closes itself after its duration via a fade-out; you never dismiss it manually.
- **Always on top.** The window is `Topmost` with no taskbar entry, sized and positioned against your **primary** monitor. If you run OBS on a second monitor, position alerts accordingly (or expect them on the primary screen).

## How It Works (Developer)

### The service

[IAlertService](../../src/VyralBot.UI/Services/AlertService.cs) (registered as a singleton, App.xaml.cs:180) owns the one "current" alert window. `ShowAlertAsync` marshals to the UI dispatcher, and under a lock closes any visible current alert before creating a fresh `AlertOverlayWindow` (AlertService.cs:37-75). That replace-don't-queue behavior is the entire "queueing" model. Its default parameters are themselves the hard-coded production styling: `SlideInFromTop`, `TopCenter`, `durationSeconds: 5`.

### The overlay window

[AlertOverlayWindow.xaml](../../src/VyralBot.UI/Views/AlertOverlayWindow.xaml) is a transparent, borderless, topmost window (`WindowStyle="None"`, `AllowsTransparency="True"`, `Topmost="True"`, `ShowInTaskbar="False"`) containing title/username/message `TextBlock`s, an optional `Image`, and five XAML `Storyboard` resources: `SlideInFromTop`, `SlideInFromBottom`, `FadeIn`, `Bounce`, and `FadeOut`.

[AlertOverlayWindow.xaml.cs](../../src/VyralBot.UI/Views/AlertOverlayWindow.xaml.cs) drives it:

- `ShowAlertAsync` (:23-87) sets the text, loads the optional image as a `BitmapImage` from a URL (collapse-on-failure), calls `PositionWindow`, shows the window, plays the selected storyboard, then arms a `DispatcherTimer` for `durationSeconds`; on tick it calls `HideAlertAsync`.
- `HideAlertAsync` (:92-99) begins the `FadeOut` storyboard and waits 500 ms; `FadeOut_Completed` hides the window and resets opacity.
- `PositionWindow` (:124-176) computes `Left`/`Top` from `SystemParameters.PrimaryScreenWidth/Height` for the nine positions — 20 px margins, 60 px reserved at the bottom for the taskbar.
- The `AlertAnimation` and `AlertPosition` enums (:186-205) map 1:1 to the storyboard names and position cases.

### The settings page (real since 2026-08-15)

[AlertSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/AlertSettingsViewModel.cs) loads its state from the `AlertSettings` Configs key in `InitializeAsync` and persists it in `SaveSettingsAsync` (JSON: `AlertsEnabled`, `DurationSeconds` 1–15, `Animation`, `Position`, and the six `*AlertsEnabled` per-event switches). `ResetSettingsAsync` restores defaults on the page; saving persists them.

The production path consults those settings: `AlertService.ShowAlertAsync` accepts `applySettings = true` (the default), in which case it overlays the saved master toggle, duration, animation, position, and per-event switches onto the call's parameters. `ChatViewModel.ShowEventAlertAsync` — the funnel for all twelve real-event call sites below — relies on that:

```csharp
// ChatViewModel.cs (excerpt) — every real alert goes through here
private async Task ShowEventAlertAsync(string title, string username, string? message = null, string platform = "")
{
    // AlertService applies the persisted AlertSettings (toggle/duration/animation/
    // position/per-event switches) because applySettings defaults to true.
    await _alertService.ShowAlertAsync(
        title: title,
        username: username,
        message: message,
        imageUrl: null,
        applySettings: true);
}
```

Note it still passes `imageUrl: null` — even the (supported) image slot is unused by real events. The only caller that bypasses the saved settings is the settings page's own **Test Alert** command, which parses the dropdown strings into the enums and passes them straight to the service with `applySettings: false` so you preview unsaved selections (AlertSettingsViewModel.cs:87-110).

### What does not exist

- No text-to-speech, GIF/video support, or customizable templates/themes — the alert layout is fixed XAML.
- No per-event alert customization (text, image, sound pairing) — the title/message strings are hard-coded at each call site.
- `src/VyralBot.Core/Services/Alerts/` exists in the tree but is an **empty folder** — no files. All alert logic lives in the UI project; the folder is presumably reserved for a future Core-level alert service.
- No browser-source/overlay HTML for alerts (see [Browser Sources](browser-sources.md)).

## Files & Code

| File | Role |
|---|---|
| [AlertService.cs](../../src/VyralBot.UI/Services/AlertService.cs) | `IAlertService` + implementation: dispatcher marshaling, replace-current-alert lifecycle |
| [AlertOverlayWindow.xaml](../../src/VyralBot.UI/Views/AlertOverlayWindow.xaml) | The overlay window: transparent/topless window chrome, alert layout, the 4 in-animations + FadeOut storyboards |
| [AlertOverlayWindow.xaml.cs](../../src/VyralBot.UI/Views/AlertOverlayWindow.xaml.cs) | Show/hide logic, storyboard selection, 9-position screen math, auto-close DispatcherTimer, `AlertAnimation`/`AlertPosition` enums |
| [AlertSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/AlertSettingsViewModel.cs) | The settings page state: load/save/reset against the `AlertSettings` Configs key, the Test Alert previewer |
| [AlertSettingsView.xaml](../../src/VyralBot.UI/Views/AlertSettingsView.xaml) | The settings page: General Settings card, six per-event toggles, Test/Save/Reset buttons |
| [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) | The 12 real-event call sites and the `ShowEventAlertAsync` funnel |
| [App.xaml.cs](../../src/VyralBot.UI/App.xaml.cs) | DI registration of `IAlertService → AlertService` (:180) |
| [MainWindow.xaml](../../src/VyralBot.UI/MainWindow.xaml) | The "Visual Alerts" sidebar entry (:373-384) |
| `src/VyralBot.Core/Services/Alerts/` | Empty placeholder folder — no files, nothing linked (not a link: empty directories are not tracked by git) |

### The replace-current-alert lifecycle

```csharp
// AlertService.cs:48-69 (excerpt)
await Application.Current.Dispatcher.InvokeAsync(async () =>
{
    lock (_lock)
    {
        // Hide current alert if one is showing
        if (_currentAlert?.IsVisible == true)
        {
            _currentAlert.Hide();
            _currentAlert.Close();
            _currentAlert = null;
        }

        // Create new alert window
        _currentAlert = new Views.AlertOverlayWindow();
    }

    if (_currentAlert != null)
    {
        await _currentAlert.ShowAlertAsync(title, username, message, imageUrl,
                                           animation, position, durationSeconds);
    }
});
```

### The auto-close and fade

```csharp
// AlertOverlayWindow.xaml.cs:73-84 — arm the timer
_autoCloseTimer?.Stop();
_autoCloseTimer = new DispatcherTimer
{
    Interval = TimeSpan.FromSeconds(durationSeconds)
};
_autoCloseTimer.Tick += (s, e) =>
{
    _autoCloseTimer.Stop();
    _ = HideAlertAsync();   // begins the FadeOut storyboard, waits 500 ms
};
_autoCloseTimer.Start();
```

### The save

```csharp
// AlertSettingsViewModel.cs — SaveSettingsAsync (excerpt)
var settings = new AlertSettings
{
    AlertsEnabled = AlertsEnabled,
    DurationSeconds = DurationSeconds,       // clamped 1-15
    Animation = Animation,                   // SlideInFromTop/Bottom | FadeIn | Bounce
    Position = Position,                     // 9-grid
    FollowAlertsEnabled = FollowAlertsEnabled,
    SubscribeAlertsEnabled = SubscribeAlertsEnabled,
    GiftSubAlertsEnabled = GiftSubAlertsEnabled,
    RaidAlertsEnabled = RaidAlertsEnabled,
    HostAlertsEnabled = HostAlertsEnabled,
    CheerAlertsEnabled = CheerAlertsEnabled
};
await _dataService.SetConfigValueAsync("AlertSettings", JsonSerializer.Serialize(settings));
```

## Data & Settings

| Data | Where | Notes |
|---|---|---|
| Alert settings | Configs key `AlertSettings` (per-profile SQLite) | JSON blob (shape above). Written by the Alert Settings page; read by `AlertService` when real alerts fire. See [Database Reference](../reference/database-reference.md). |

Everything on the settings page persists; real-alert styling comes from the saved values, and only the page's Test Alert previews unsaved selections.

## Status & Known Issues

Every item below is verified against the current code. See the [known issues ledger](../reference/known-issues.md) for app-wide context.

1. **The Alert Settings page is fully wired** — load/save/reset persist to the `AlertSettings` Configs key; real alerts apply the saved master toggle, duration, animation, position, and per-event switches (fixed 2026-08-15, KI-6 resolved). [known issues ledger](../reference/known-issues.md)
2. **Test Alert previews unsaved selections** — it passes `applySettings: false`, so you can experiment freely; nothing is persisted until Save. [known issues ledger](../reference/known-issues.md)
3. **Single-alert, no queue** — a new alert replaces the current one mid-display (AlertService.cs:50-62); back-to-back events truncate each other. [known issues ledger](../reference/known-issues.md)
4. **No TTS, GIFs, or templates** — static XAML layout, static or URL image only, hard-coded title/message strings per call site; real alerts always pass `imageUrl: null` anyway. [known issues ledger](../reference/known-issues.md)
5. **WPF window, not a browser source** — alerts render over the streamer's primary screen; there is no overlay URL for OBS (see [Browser Sources](browser-sources.md)), so capturing alerts requires display capture. [known issues ledger](../reference/known-issues.md)
6. **`src/VyralBot.Core/Services/Alerts/` is an empty folder** — reserved, unused; all alert code lives in VyralBot.UI. [known issues ledger](../reference/known-issues.md)
7. **Alert delivery rides on the event pipeline** — Twitch alerts arrive now that EventSub connects after the auth fix (see [Twitch](../platforms/twitch.md)). [known issues ledger](../reference/known-issues.md)
