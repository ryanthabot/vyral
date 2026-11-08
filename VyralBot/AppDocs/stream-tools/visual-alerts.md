# Visual Alerts

> The pop-up overlay Vyral draws over your screen when someone follows, subscribes, raids, and more — a WPF window, not a browser source — plus the Alert Settings page, which persists and drives real alerts, and (since 1.1.0) the app's Text-to-Speech settings: spoken alerts, the `!tts` chat command, and SAPI voice/rate/volume configuration all live on this page.

## Overview

When a stream event fires, Vyral shows an alert: a borderless, always-on-top window that slides or fades in over your primary screen with a title ("New Follower!"), a username, an optional message line, and an optional image. After the configured duration it fades out and closes by itself.

Two things make this feature unusual compared to tools like Stream Elements or Alertbox:

- **It is an in-app WPF window, not a browser source.** There is no overlay URL to add to OBS ([Browser Sources](browser-sources.md) covers the HTML pages that do exist — audio, the song player and the five overlay widgets). The alert appears over the streamer's own screen, so viewers see it only if your OBS setup captures that screen (display capture).
- **The Alert Settings page is real.** Since the 2026-08-15 fix it persists to the `AlertSettings` Configs key, and `AlertService` applies the saved master toggle, duration, animation, position, and per-event switches to real alerts (`applySettings = true` by default). Since 1.1.0 the page previews two ways: **Preview Current Settings** shows your *unsaved* general selections, and each event row's **TEST** button fires the exact live title for that event (suppressed + explained when the row's toggle is off).

## User Guide

Open **Stream Tools → Visual Alerts** in the sidebar. (The "Events & Alerts" card on the Tools hub page goes to the same place — ToolsView.xaml:59 and :174 both route here.)

### Trying it out

Two ways to preview (1.1.0):

- **Preview Current Settings** (General Settings card) — an overlay with "Test Alert!" for user "TestUser123", using the duration, animation and position currently selected on the page. It previews unsaved selections, and the caption under the button says so: changes apply to the preview immediately, before saving. Best way to try the four animations and nine positions before pressing Save.
- **TEST buttons on each Alert Events row** (follow, subscribe, gift sub, raid, host, cheer) — fires the alert with the **exact title the live event uses** (e.g. "Bits!"), so the popup matches what viewers see on stream. If that row's toggle is off, the preview is suppressed and a status line above the Notes explains why — mirroring the real per-event gate.

### The General Settings card

- **Enable Visual Alerts** — master on/off. With it off, real alerts are suppressed (Test Alert still previews).
- **Alert Duration (seconds)** — slider for how long the alert stays on screen (1–15). Applies to real alerts and the test alike; the default is 5.
- **Animation Style** — dropdown with **SlideInFromTop**, **SlideInFromBottom**, **FadeIn**, and **Bounce**.
- **Screen Position** — dropdown with nine positions across a 3x3 grid (Top/Center/Bottom x Left/Center/Right).

### The Alert Events card

Six toggles — **New Followers, New Subscribers, Gifted Subscriptions, Raids, Hosts, Bits/Cheers** — choose which event categories produce real alerts. Events outside a enabled category are skipped.

### The Text-to-Speech card {#text-to-speech-card}

Added in 1.1.0. The card configures the app's single TTS engine — used both for **spoken alerts** ("New Follower, Bob" spoken as the popup appears) and the **`!tts` chat command** (see the [command catalog](../reference/command-catalog.md#tts-command)):

- **Enable Text-to-Speech** — the master switch (`Tts.Enabled`). Off = neither alerts nor `!tts` speak anything; `!tts` is silently ignored, exactly like other disabled commands.
- **Speak Alert TTS** — speaks "{title}, {username}" (trailing `!`/`.` dropped) through the speakers whenever an alert popup appears (`AlertSettings.TtsAlertsEnabled`, on by default). This toggle saves with the alert settings; everything else on the card saves as individual `Tts.*` keys.
- **Voice** — dropdown of the installed Windows SAPI voices (`ITtsService.GetVoices` enumerates them once and caches). Empty selection = the OS default voice. A configured voice that has since been uninstalled falls back to the default voice (logged).
- **Rate (%)** — −50 to +50 (SAPI's −10..10 scale under the hood).
- **Volume (%)** — 0–100.
- **Allow viewers to use !tts** — when off, only moderators can use `!tts` (viewers are silently ignored; mods always pass and also bypass the cooldown).
- **Max message length** — 10–1000 characters; longer `!tts` messages (and longer programmatic speaks) are truncated to it.
- **Test TTS** — speaks a sample line with the current (unsaved) selections; suppressed with an explanation when the master toggle is off, and reports when no SAPI voices are installed.

**Do Not Disturb gates everything**: with `Notifications.DoNotDisturb` on (the same key [Sounds](sounds.md) and alerts respect), no TTS plays at all — `!tts` is ignored and alert speech is skipped before it even queues. Speech is serialized on a single-consumer queue (max 10 pending, drop-oldest) so overlapping utterances can never dogpile the audio device; the queue drop is logged at info.

### Save and Reset

- **Save Settings** — persists the alert settings to the `AlertSettings` Configs key (JSON) and the Text-to-Speech card to its individual `Tts.*` keys, then confirms with "Alert settings saved successfully!". Real alerts use the saved values from the next event on.
- **Reset to Defaults** — returns the on-screen controls to their defaults (on, 5s, SlideInFromTop, TopCenter, all events on, TTS defaults). Save afterwards to persist the reset.

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
| Twitch channel-points redemption | Reward Redeemed! | `OnTwitchChannelPointsRedeemAsync` (2026-11-07) |
| YouTube Super Chat / Super Sticker | Super Chat! | `OnYouTubeSuperChatAsync` (:6973) |
| YouTube membership / milestone | New Subscriber! | membership handler (:4500) |
| Discord member join | New Member! | `OnDiscordMemberJoinedAsync` (:7021, intent-gated — see [Discord](../platforms/discord.md#member-joins--boosts-intent-gated)) |
| Discord boost | Server Boosted! | `OnDiscordBoostAsync` (:7053, intent-gated) |

Titles without a dedicated settings toggle — "Kicks Gifted!", "Reward Redeemed!", "Test Alert!" — always show when the master toggle is on; the rest map onto the six per-event switches below (Super Chat → cheer, New Member → follow, Server Boosted → subscribe).

You can fire most of these on demand from the [Event Testing](../chat-features/event-testing.md) page, which pushes synthetic events through the real pipeline.

### Behavior details worth knowing

- **Alerts queue (bounded FIFO).** One alert is on screen at a time; an alert arriving while another is showing waits in a **queue capped at 10** (drop-oldest when exceeded) and plays when the current one closes. A burst no longer truncates itself — beyond ten deep, the oldest waiting alerts are dropped so the queue can never grow unbounded.
- **Auto-close.** The alert closes itself after its duration via a fade-out — a real `Close()`, which is what drains the queue (an earlier build only *hid* the window, so the service never learned it finished and every alert after the first stalled; fixed 2026-11-07, pre-marathon).
- **Alerts can speak.** With **Speak Alert TTS** on (and the TTS master switch on, and DND off), each popup is followed by "{title}, {username}" spoken through the configured voice — fire-and-forget on the same bounded speech queue `!tts` uses, so a burst of alerts serializes instead of talking over itself.
- **Always on top.** The window is `Topmost` with no taskbar entry, sized and positioned against your **primary** monitor. If you run OBS on a second monitor, position alerts accordingly (or expect them on the primary screen).

## How It Works (Developer)

### The service

[IAlertService](../../src/VyralBot.UI/Services/AlertService.cs) (registered as a singleton, App.xaml.cs:180) owns the current alert window plus the pending queue. `ShowAlertAsync` marshals to the UI dispatcher, applies the saved settings (when `applySettings`), and then either shows immediately or enqueues: under a lock, a request arriving while `_isShowing` is true joins a `Queue<QueuedAlert>` capped at **10** (drop-oldest while full — `MaxPendingAlerts`, AlertService.cs:67). `ShowCore` creates the window and hooks its `Closed` event; `OnAlertWindowClosed` dequeues the next request or clears `_isShowing` when the queue is empty. Auto-close therefore has to *close* the window (not hide it) for the chain to advance — that distinction was the whole stall bug. A `TitleToEventMap` (AlertService.cs:87) routes each title to its per-event settings toggle, including the wave-4 additions ("Super Chat!" → cheer, "New Member!" → follow, "Server Boosted!" → subscribe).

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

Note it still passes `imageUrl: null` — even the (supported) image slot is unused by real events. The callers that bypass the saved settings are the settings page's own preview commands: **Preview Current Settings** parses the dropdown strings into the enums and passes them straight to the service with `applySettings: false` so you preview unsaved selections, and the per-event **TEST** buttons send the live title through the normal settings path (title→toggle routing included) so a disabled row is suppressed exactly like a real event (AlertSettingsViewModel.cs, `TestAlertCommand`/`TestEventAlertCommand`).

### The TTS engine (1.1.0)

[TextToSpeechService.cs](../../src/VyralBot.Core/Services/Audio/TextToSpeechService.cs) (`ITtsService`, Core — Windows SAPI via `System.Speech.Synthesis`) is a DI singleton with two faces:

- **`SpeakAsync(text, voice?, ratePercent, volumePercent)`** — the programmatic speak (used by alert TTS). Gates on `Tts.Enabled` + Do Not Disturb, resolves defaults from the saved settings (explicit arguments override per-utterance), truncates to `Tts.MaxLength`, and enqueues onto a **bounded channel (capacity 10, DropOldest, single consumer)** — SAPI's `Speak()` is a blocking call, so the worker loop is the only thing that ever touches the synthesizer; overlapping callers only enqueue. `StopSpeaking` drains the queue and cancels the in-flight prompt; `Dispose` cuts off speech at shutdown.
- **`ProcessCommandAsync`** — the `!tts` chat command (full rules in the [command catalog](../reference/command-catalog.md#tts-command)): master toggle → viewer gate → DND → 30-second per-user cooldown (mods exempt) → truncate → speak → "🔊 {user} says: {text}".

Alert TTS itself lives in `AlertService.TrySpeakAlertTtsAsync` (AlertService.cs:308-345): after a popup shows, it checks `AlertSettings.TtsAlertsEnabled`, strips trailing punctuation, and fire-and-forgets `SpeakAsync("{title}, {username}")` — so the inner gates (master toggle, DND) still apply. SAPI-unavailable machines (no voices / COM failure) disable the engine for the session after one attempt (logged, status surfaces on the card's Test).

### What does not exist

- No GIF/video support, or customizable templates/themes — the alert layout is fixed XAML.
- No per-event alert asset customization (image/GIF/sound pairing per event) — the title/message strings are hard-coded at each call site, and the page's Notes say so plainly: `{variables}` like `{username}` are not substituted in alert text (they apply to commands, auto-replies and chat responses), and per-event *sounds* live on the [Sounds](sounds.md) page, not here.
- `src/VyralBot.Core/Services/Alerts/` exists in the tree but is an **empty folder** — no files. All alert logic lives in the UI project; the folder is presumably reserved for a future Core-level alert service.
- No browser-source/overlay HTML for alerts (see [Browser Sources](browser-sources.md)).
- TTS has no output-device routing — SAPI speaks to the system default audio device (same boundary as local sound playback, KI-50).

## Files & Code

| File | Role |
|---|---|
| [AlertService.cs](../../src/VyralBot.UI/Services/AlertService.cs) | `IAlertService` + implementation: dispatcher marshaling, settings overlay, bounded FIFO queue (cap 10, drop-oldest) with close-driven drain, title→toggle routing |
| [AlertOverlayWindow.xaml](../../src/VyralBot.UI/Views/AlertOverlayWindow.xaml) | The overlay window: transparent/topless window chrome, alert layout, the 4 in-animations + FadeOut storyboards |
| [AlertOverlayWindow.xaml.cs](../../src/VyralBot.UI/Views/AlertOverlayWindow.xaml.cs) | Show/hide logic, storyboard selection, 9-position screen math, auto-close DispatcherTimer, `AlertAnimation`/`AlertPosition` enums |
| [AlertSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/AlertSettingsViewModel.cs) | The settings page state: load/save/reset against the `AlertSettings` Configs key, the Test Alert previewer, and the Text-to-Speech card (voice list, clamped rate/volume/length, Test TTS) |
| [TextToSpeechService.cs](../../src/VyralBot.Core/Services/Audio/TextToSpeechService.cs) | `ITtsService` — SAPI synthesis behind a single-consumer bounded queue, the `Tts.*` settings store, and the `!tts` command handler (1.1.0) |
| [AlertSettingsView.xaml](../../src/VyralBot.UI/Views/AlertSettingsView.xaml) | The settings page: General Settings card (Preview Current Settings), six per-event toggles each with a TEST button, scope Notes, Save/Reset |
| [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) | The 12 real-event call sites and the `ShowEventAlertAsync` funnel |
| [App.xaml.cs](../../src/VyralBot.UI/App.xaml.cs) | DI registration of `IAlertService → AlertService` (:180) |
| [MainWindow.xaml](../../src/VyralBot.UI/MainWindow.xaml) | The "Visual Alerts" sidebar entry (:373-384) |
| `src/VyralBot.Core/Services/Alerts/` | Empty placeholder folder — no files, nothing linked (not a link: empty directories are not tracked by git) |

### The queue-and-drain lifecycle

```csharp
// AlertService.ShowAlertAsync (excerpt) — enqueue while an alert is on screen
lock (_lock)
{
    if (_isShowing)
    {
        var queued = new QueuedAlert(title, username, message, imageUrl,
                                     effectiveAnimation, effectivePosition, effectiveDuration);

        // Drop-oldest so the queue stays bounded.
        while (_pendingAlerts.Count >= MaxPendingAlerts)   // cap 10
            _pendingAlerts.Dequeue();

        _pendingAlerts.Enqueue(queued);
        return;
    }

    _isShowing = true;
}
await ShowCore(new QueuedAlert(/* the request just shown */));

// ShowCore hooks the window; when it CLOSES (auto-close calls Close(), not Hide()),
// OnAlertWindowClosed dequeues the next request or clears _isShowing.
alert.Closed += OnAlertWindowClosed;
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
| `Tts.Enabled` | Configs (category `Tts`) | TTS master switch (default **off** until first saved). Gates alert speech and `!tts` alike. |
| `Tts.Voice` | Configs | Installed SAPI voice name; empty = OS default. Uninstalled names fall back to the default voice. |
| `Tts.RatePercent` | Configs | −50..50 (default 0); maps to SAPI's −10..10. |
| `Tts.VolumePercent` | Configs | 0..100 (default 100). |
| `Tts.AllowViewers` | Configs | Whether non-moderators may use `!tts` (default true). |
| `Tts.MaxLength` | Configs | 10..1000 chars (default 300); longer messages are truncated. |
| `AlertSettings.TtsAlertsEnabled` | inside the `AlertSettings` JSON | The **Speak Alert TTS** toggle (default on) — speaks "{title}, {username}" when a popup appears. |
| `Notifications.DoNotDisturb` | Configs | Shared DND key (owned by Settings → Notifications): on = no TTS at all, plus no sounds/alerts. |

Everything on the settings page persists; real-alert styling comes from the saved values, and only the page's Test Alert previews unsaved selections.

## Status & Known Issues

Every item below is verified against the current code. See the [known issues ledger](../reference/known-issues.md) for app-wide context.

1. **The Alert Settings page is fully wired** — load/save/reset persist to the `AlertSettings` Configs key; real alerts apply the saved master toggle, duration, animation, position, and per-event switches (fixed 2026-08-15, KI-6 resolved). [known issues ledger](../reference/known-issues.md)
2. **Test Alert previews unsaved selections** — it passes `applySettings: false`, so you can experiment freely; nothing is persisted until Save. [known issues ledger](../reference/known-issues.md)
3. **Alerts queue — bounded, drop-oldest (fixed 2026-11-07)** — one alert at a time with up to 10 waiting; beyond that the oldest waiting alert is dropped. An earlier build had no queue *and* auto-close only hid the window, so the service never advanced and every alert after the first in a session stalled on screen — both are fixed (KI-48 resolved). [known issues ledger](../reference/known-issues.md)
4. **No GIFs or templates** — static XAML layout, static or URL image only, hard-coded title/message strings per call site; real alerts always pass `imageUrl: null` anyway. (TTS arrived in 1.1.0 — see [The Text-to-Speech card](#text-to-speech-card).) [known issues ledger](../reference/known-issues.md)
5. **WPF window, not a browser source** — alerts render over the streamer's primary screen; there is no overlay URL for OBS (see [Browser Sources](browser-sources.md)), so capturing alerts requires display capture. [known issues ledger](../reference/known-issues.md)
6. **`src/VyralBot.Core/Services/Alerts/` is an empty folder** — reserved, unused; all alert code lives in VyralBot.UI. [known issues ledger](../reference/known-issues.md)
7. **Alert delivery rides on the event pipeline** — Twitch alerts arrive now that EventSub connects after the auth fix (see [Twitch](../platforms/twitch.md)). [known issues ledger](../reference/known-issues.md)
