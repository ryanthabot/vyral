# Browser Sources

> The OBS browser-source pages Vyral serves on `http://localhost:5000` (the web-server port, 5000 by default): **`audio.html`** (stream sounds — working end to end, volume honored, since the 2026-08-15 JSON-contract fix), **`songplayer.html`** (song-request audio player), and the five 1.1.0 overlay widgets — **`deathcounter.html`** (live death counter), **`leaderboard.html`** (points leaderboard), **`chatfeed.html`** (face-cam chat captions), **`goals.html`** (Vyral-goals progress bars) and **`polloverlay.html`** (live poll results).

## Overview

Vyral serves **seven** browser-source pages from the app's embedded web server (Kestrel, localhost — the configured web-server port, 5000 by default), each meant to be added to OBS as a **Browser Source**:

- **`audio.html`** — invisible; when a stream event fires (follow, sub, raid, and so on), Vyral pushes a WebSocket message to every open copy of the page and it plays the matching sound file at the sent volume — OBS captures it as stream audio, so **viewers** hear it.
- **`songplayer.html`** — invisible audio surface for [song requests](sounds.md): plays each queued song, auto-advances, and reports titles/durations back to the queue (added with song requests, 1.1.0).
- **`deathcounter.html`** (1.1.0) — the big number overlay for the shared death counter (Tools page card + `!death` chat commands). Polls `GET /counters/death` every 3 s; styling via URL params.
- **`leaderboard.html`** (1.1.0) — the top-points overlay. Polls `GET /leaderboard/data` every 10 s; rows, title, style and platform filter via URL params. The Currency page builds the URL for you.
- **`chatfeed.html`** (1.1.0, wave 16) — face-cam chat captions: the last few chat messages with platform chips, sized for a corner near your face cam. Polls `GET /chatfeed/data` every 2 s; count, font size, truncation, platform filter and theme via URL params. The Audio Settings page builds the URL for you.
- **`goals.html`** (1.1.0, wave 16) — one progress bar per enabled [Vyral goal](vyral-goals.md). Polls `GET /goals/data` every 15 s; theme and size via URL params. The Viewer Hub's Vyral Goals card builds the URL for you.
- **`polloverlay.html`** (1.1.0, wave 16) — live poll results as growing bars with a countdown while a poll runs, and the winner bars for 5 minutes after it ends. Polls `GET /poll/data` every 3 s; title and theme via URL params.

Things worth knowing up front:

- The sidebar button labeled **Browser Sources** (under Stream Tools) navigates to the [Audio Settings](audio.md) page — that is where the audio and chat-feed browser-source setup lives (the tooltip says so). Wired on 2026-08-15.
- **Alerts and giveaways still have no overlay HTML** — visual alerts are an in-app WPF window ([Visual Alerts](visual-alerts.md)), and giveaway winners are only posted to chat. Polls **do** have an overlay now (`polloverlay.html`).
- **Viewer-side sound playback works** since the 2026-08-15 fix: the server broadcasts the camelCase `{"type":"play","sound":...,"volume":0..1}` shape the page understands, the page honors `volume`, and it derives its WebSocket/sounds URLs from `window.location` (port-agnostic). See [Status & Known Issues](#status--known-issues).

## User Guide

The sidebar **Browser Sources** button opens the Audio Settings page; the actual setup card is **Stream Tools → Audio Settings → Browser Source (OBS)**. That card shows the URL, a Copy button, and short instructions.

### Prerequisites

1. The embedded web server is running (it listens on `http://localhost:5000`). It normally starts with the app when web-server auto-start is enabled — see [Web Server & ngrok](../settings/web-server-ngrok.md).
2. You have uploaded at least one sound and assigned it to an event on the [Sounds Management](sounds.md) page.
3. The **Stream Sounds** toggle on the [Audio Settings](audio.md) page is on (it is on by default).

### Adding the browser source in OBS

1. In OBS, right-click the **Sources** box (or click the **+**) and choose **Add → Browser**.
2. Give it a name (for example "Vyral Audio") and confirm.
3. In the properties dialog, paste this URL into the **URL** field:

   ```
   http://localhost:5000/audio.html
   ```

4. Set **Width** to `1920` and **Height** to `1080` (or your stream resolution — the page itself is transparent and invisible, the size only matters for OBS bookkeeping).
5. Confirm, and leave the source in your scene.

That is the same URL the Audio Settings page displays with its **Copy URL** button, and the same steps listed on that page (AudioSettingsView.xaml:233-254).

### What you should see

When OBS loads the page, a small status pill appears in the top-left corner of the source:

- **"Connecting to Vyral..."** (yellow) while it connects.
- **"Connected to Vyral"** (green) once the WebSocket is open. The pill fades out after 5 seconds.
- **"Disconnected from Vyral"** (red) if the app is closed. The page silently retries every 3 seconds, so if you restart Vyral it reconnects on its own.

You can force the pill to reappear at any time by right-clicking the source in OBS and choosing **Refresh**.

### Troubleshooting

- **The pill never turns green** — Vyral's web server is not running. Start it from [Web Server & ngrok](../settings/web-server-ngrok.md). The port comes from Web Server settings (default 5000); the Audio Settings page only mirrors it read-only, so change it there and restart the app.
- **The pill is green but no sounds play on stream** — check that an event sound is assigned on [Sounds Management](sounds.md) and the **Stream Sounds** toggle is on; if OBS shows the page as muted in the audio mixer, unmute it.
- **Sounds play for you but not viewers** — check that the OBS browser source is not muted in the OBS audio mixer.
- **You run OBS on a different machine than Vyral** — the page connects back to the host it was loaded from (`window.location`), which is `localhost:5000` as configured — so OBS must run on the same computer as Vyral unless you rewrite the URL.

## The overlay widgets (1.1.0)

Five visible overlays join the invisible audio page (the song player is audio-only too). Same trust model as `/songs`: the embedded server binds **loopback only**, the pages are served from the same origin, and their endpoints are bare read-only GETs, so they sit outside the token-gated Control API and need no Origin gate.

### Death counter (`deathcounter.html`) {#death-counter-deathcounterhtml}

Shows the shared death counter as a huge number with a label under it. It polls `GET /counters/death` (returns `{ "count": 12 }`, `no-store`) **every 3 seconds** — no WebSocket, no restart needed when the app-side count changes via the [Tools card](../home/tools-hub.md) or a chat `!death+`.

Add it to OBS as a Browser Source with:

```
http://localhost:5000/deathcounter.html?label=Deaths&size=120&color=%23ffffff
```

| Param | Default | Notes |
|---|---|---|
| `label` | `Deaths` | Text under the number. Plain text only (never HTML), capped at 40 chars. The Tools page card builds the URL with your saved label. |
| `size` | `120` | Number font size in px, clamped 20–480. |
| `color` | white | Any CSS hex color — URL-encoded (`%23` = `#`); strict `#rgb`/`#rrggbb`/`#rrggbbaa` pattern, anything else falls back to white. |

A status pill (same look as the audio page's) appears while connecting and hides after 5 s; the page auto-reconnects if the app restarts. The Tools page's **death counter card** shows and copies this URL with the label pre-baked.

### Points leaderboard (`leaderboard.html`) {#points-leaderboard-leaderboardhtml}

Shows the top balances with rank, username, platform chip and amount, headed by a title. It polls `GET /leaderboard/data?count=&platform=` (top N by balance via the shared `ICurrencyService.GetLeaderboardAsync` — the same query the Currency page and `!leaderboard` use) **every 10 seconds**.

```
http://localhost:5000/leaderboard.html?count=10&title=Top%20Points&style=dark
```

| Param | Default | Notes |
|---|---|---|
| `count` | `10` | Rows to show, clamped 1–25 (a hand-edited URL is clamped server-side, not rejected). |
| `title` | `Top Points` | Heading text, capped at 60 chars. |
| `style` | `dark` | `dark` or `light` — light flips the card for bright stream scenes. |
| `platform` | — (all) | Optional single-platform filter: `twitch`, `kick`, `youtube`, `tiktok`, `discord`, `facebook`, `vyral` (case-insensitive; unknown values are a 400). |

The response carries `currencyName`/`currencySymbol` from your Currency settings, so the overlay labels points with your currency's name. Platform chips use the app's platform palette. The **Currency page's LEADERBOARD tab** has the URL card that builds this for you — see [Currency & Rewards](currency.md#leaderboard-overlay).

### Chat feed (`chatfeed.html`) {#chat-feed-chatfeedhtml}

Face-cam captions: the last few chat messages as compact caption lines — username, platform chip, message — meant for a box near your face cam so single-viewer scenes can read chat without a full chat window. It polls `GET /chatfeed/data?count=&platform=&channel=` (the last N rows from the same per-profile `ChatMessages` table the platform chat services write, newest first) **every 2 seconds**.

```
http://localhost:5000/chatfeed.html?count=5&size=16&maxlength=120&fade=true
```

| Param | Default | Notes |
|---|---|---|
| `count` | `5` | Messages to show, clamped 1–15 (client- and server-side). |
| `size` | `16` | Font size in px, clamped 10–48. |
| `maxlength` | `120` | Message truncation length, clamped 20–500. |
| `fade` | off | `fade=true` dims older messages. |
| `platform` | — (all) | Optional single-platform filter: `twitch`, `kick`, `youtube`, `discord` (case-insensitive; an unknown value is a 400). TikTok has no chat service and is never in the feed. |
| `channel` | — (all) | Optional channel key when you want one channel only; an empty value selects the most recent messages across the platform's channels. |

The **Audio Settings page** (the "Browser Sources" sidebar destination) has a Chat Feed overlay URL card with a Copy button, rebuilt from the live web-server port. Platform chips use the app's palette (Twitch purple, Kick green, YouTube red, Discord blurple); everything is rendered via `textContent`, never `innerHTML`.

### Goals progress (`goals.html`) {#goals-progress-goalshtml}

One progress bar per **enabled** [Vyral goal](vyral-goals.md) — the viewer challenges configured in the Viewer Hub. It polls `GET /goals/data` (every enabled goal from the same `ISocialGoalService` the Viewer Hub editor uses, ordered by `SortOrder`) **every 15 seconds**. Each bar shows completions / target (capped at 100 %) with the goal's title, kind chip (linked / follows / subs / sub tier) and description; the bar animates as it fills.

```
http://localhost:5000/goals.html?style=dark&size=m
```

| Param | Default | Notes |
|---|---|---|
| `style` | `dark` | `dark` or `light` — light flips the card for bright stream scenes. |
| `size` | `m` | `m` or `l` — larger title/subtitle rows. |

The **Viewer Hub's Vyral Goals card** has an "Overlay URL" row that shows the port-correct URL and copies it to the clipboard. Honest semantics: a Vyral goal's `TargetCount` is the per-viewer threshold ("link 2 platforms") while the bar's numerator is how many **viewers completed it** — the overlay shows "completions / target" as an aggregate progress display ([Known Issues](../reference/known-issues.md) KI-63).

### Poll results (`polloverlay.html`) {#poll-results-polloverlayhtml}

Live poll bars: while a poll is active it shows the question, one bar per option with votes and percent, the total vote count, and a live countdown when the poll was started with a duration (`!poll start 5m …`). After the poll ends the final bars — with the winner labeled — linger for **5 minutes**, then the widget hides itself until the next poll. It polls `GET /poll/data` **every 3 seconds** (the countdown ticks client-side every second between fetches).

```
http://localhost:5000/polloverlay.html?title=Poll&style=dark
```

| Param | Default | Notes |
|---|---|---|
| `title` | *(the poll question)* | Optional heading override, capped at 60 chars. |
| `style` | `dark` | `dark` or `light`. |

Votes are the same per-profile `PollVotes` rows `!vote`/`!poll vote` write — the overlay is a read-only view of the [Polls](polls.md) engine. Ties show no winner label; a zero-vote poll claims no winner (same honesty as the chat announcement). Bars are keyed by option index (not label), so duplicate option texts never merge.

### Widget prerequisites

Both widgets need the embedded web server running (same prerequisite as the audio page) — they are served and polled from the same origin and port. All five widgets render nothing useful while their backing service is not registered (the endpoints return a 503 JSON error instead of data); the currency service returns 503 only if currency itself is disabled.

## How It Works (Developer)

### The embedded web server

One in-process ASP.NET Core (Kestrel) app, [WebServerService.cs](../../src/VyralBot.Api/Services/WebServerService.cs), hosts everything browser-facing:

- `StartAsync(port = 5000)` builds a `WebApplication` bound to **localhost only** via `ListenLocalhost` (WebServerService.cs:55) — the server is never directly reachable from the network.
- It pulls shared services (`IEventBus`, `ILoggerFactory`) from the parent WPF DI container (WebServerService.cs:62-63) so web and UI see the same event stream.
- `UseStaticFiles()` serves everything under `wwwroot` — that is `audio.html`, `deathcounter.html`, `leaderboard.html` and the `/sounds/*` directory (WebServerService.cs:84).
- A minimal-API endpoint maps `/audio-ws` (WebServerService.cs:91-122): it accepts WebSocket upgrades, tracks connected pages in a `ConcurrentDictionary`, and parks each socket in a receive loop until it closes. Nothing is ever read from the client.
- The server is started by `WebhookCoordinatorService` (and the ngrok setup wizards) using the port from `WebServerSettings` (default 5000). See [Web Server architecture](../architecture/web-server.md).

The overlay widgets' read endpoints are MVC controllers on the same host, all loopback-only and read-only (hence outside the Bearer-gated Control API):

- [CountersController.cs](../../src/VyralBot.Api/Controllers/CountersController.cs) — `GET /counters/death` returns the current `{ count }` from `IDeathCounterService` (the shared Tools-card/`!death` config rows), `no-store`, 503 when the counter service is not registered.
- [LeaderboardController.cs](../../src/VyralBot.Api/Controllers/LeaderboardController.cs) — `GET /leaderboard/data?count=&platform=` returns `currencyName`/`currencySymbol` plus ranked `entries` from `ICurrencyService.GetLeaderboardAsync` (deterministic tiebreakers); `count` is clamped 1–25, `platform` normalized against the known platform set (400 on an unknown name), `no-store`, 503 when currency is disabled.
- [ChatFeedController.cs](../../src/VyralBot.Api/Controllers/ChatFeedController.cs) — `GET /chatfeed/data?count=&platform=&channel=` merges the newest rows from `IChatMessageStore` across the four persisted chat platforms (Twitch/Kick/YouTube/Discord), newest first; `count` clamped 1–15, `platform` normalized (400 on unknown), 503 when the store is not registered.
- [GoalsController.cs](../../src/VyralBot.Api/Controllers/GoalsController.cs) — `GET /goals/data` returns every enabled goal from `ISocialGoalService` with its aggregate completion count (no usernames/PII leave the process); 503 when the goal service is not registered.
- [PollController.cs](../../src/VyralBot.Api/Controllers/PollController.cs) — `GET /poll/data` returns `{ active, lastResults }` from `PollService` — the running poll with live per-option votes and the optional scheduled end, plus the most recently completed poll when it ended within the 5-minute linger window; 503 when the poll service is not registered.

### The broadcast bridge

Core's `SoundService` must not depend on the API project, so the fan-out is bridged through a static delegate:

```csharp
// src/VyralBot.Core/Services/Audio/AudioWebSocketBroadcaster.cs
public static class AudioWebSocketBroadcaster
{
    public static Func<string, Task>? BroadcastAsync;
}
```

When Kestrel starts, `WebServerService` assigns that delegate a lambda that sends the string as a text frame to every connected `/audio-ws` socket (WebServerService.cs:125-135). When `SoundService.PlaySoundFileAsync` wants to play a sound browser-side, it serializes an `AudioEvent` to JSON and invokes the delegate (SoundService.cs:162-180). If the web server is not running, the delegate is null and a warning is logged.

### The page itself

[audio.html](../../src/VyralBot.Api/wwwroot/audio.html) is plain hand-written JavaScript with no dependencies:

- Derives its WebSocket and sound-file base URLs from `window.location` (port-agnostic — the page works no matter which port serves it) and auto-reconnects every 3 seconds on close (audio.html:53, 75).
- Shows the status pill and hides it 5 seconds after connecting (audio.html:54, 108-113).
- On each message, parses it as JSON and — only if it looks like `{ type: 'play', sound: '...' }` — plays `<base>/sounds/<file>` through a cached `Audio` element (audio.html:81-103), applying any `volume` field (0..1) to the element. `.play()` rejections are caught and logged.

Sound files themselves are just static files: whatever you upload on the [Sounds Management](sounds.md) page lands in the **current profile's `sounds` folder** (migrated there automatically from the old app-global location), and `/sounds/<filename>` is served from that directory — the wwwroot static provider remains as a fallback for files still living in the legacy path.

### The JSON contract (fixed 2026-08-15)

The only producer of WebSocket messages is `SoundService`, which serializes the play request with a camelCase naming policy — exactly the shape the page handles:

```json
{"type":"play","sound":"follow.wav","volume":0.8}
```

(The pre-fix build serialized the `AudioEvent` class with PascalCase keys the page discarded, so browser-source sounds never fired; the `AudioEvent` class shape remains the in-code model, but the wire format is camelCase and volume is honored.)

### Two copies of the page, and the legacy server

- `src/VyralBot.Api/wwwroot/audio.html` — the **current** copy served by Kestrel (derives URLs from `window.location`; served on the configured port, default 5000).
- [BrowserSource/audio.html](../../BrowserSource/audio.html) (repo root) — a **legacy** copy that hard-codes `ws://localhost:8080` and `http://localhost:8080/sounds/` (BrowserSource/audio.html:57-58). It belonged to the older standalone `AudioWebSocketServer` (HttpListener, port 8080), which has since been **deleted from the codebase** — only the unreferenced HTML copy remains in the repo.
- The legacy server is gone: it is no longer registered in DI ("AudioWebSocketServer removed; now handled by Kestrel server", App.xaml.cs:306), and MainViewModel notes "Audio WebSocket server now handled by Kestrel (port 5000)" (MainViewModel.cs:361). Only the Kestrel path runs today.

### ngrok's actual role

The same Kestrel server is also tunneled publicly by ngrok ([NgrokService.cs](../../src/VyralBot.Api/Services/NgrokService.cs) spawns `ngrok http <port> --region <region> --authtoken <token>`, NgrokService.cs:60). That public URL exists **for receiving Kick webhooks** — it is *not* how OBS consumes the audio page. The server binds to localhost only, and the audio URL points at `localhost:5000`, so the tunnel does not serve the browser source without rewriting the URL. Details in [Web Server & ngrok](../settings/web-server-ngrok.md). A `WebServerDependencyTracker` ([WebServerDependencyTracker.cs](../../src/VyralBot.Core/Services/WebServer/WebServerDependencyTracker.cs)) prevents stopping the server while features depend on it.

## Files & Code

| File | Role |
|---|---|
| [audio.html](../../src/VyralBot.Api/wwwroot/audio.html) | The audio browser-source page: WebSocket client, status pill, volume-honoring sound playback (current copy) |
| [songplayer.html](../../src/VyralBot.Api/wwwroot/songplayer.html) | Song-request audio player page (plays the queue, reports titles back) — see [Sounds](sounds.md) |
| [deathcounter.html](../../src/VyralBot.Api/wwwroot/deathcounter.html) | Death-counter overlay: 3 s polling of `/counters/death`, `label`/`size`/`color` URL params (1.1.0) |
| [leaderboard.html](../../src/VyralBot.Api/wwwroot/leaderboard.html) | Points-leaderboard overlay: 10 s polling of `/leaderboard/data`, `count`/`title`/`style`/`platform` URL params, platform chips (1.1.0) |
| [chatfeed.html](../../src/VyralBot.Api/wwwroot/chatfeed.html) | Chat-captions overlay: 2 s polling of `/chatfeed/data`, `count`/`size`/`maxlength`/`fade`/`platform`/`style` URL params, platform chips (1.1.0, wave 16) |
| [goals.html](../../src/VyralBot.Api/wwwroot/goals.html) | Vyral-goals overlay: 15 s polling of `/goals/data`, `style`/`size` URL params, animated progress bars (1.1.0, wave 16) |
| [polloverlay.html](../../src/VyralBot.Api/wwwroot/polloverlay.html) | Poll-results overlay: 3 s polling of `/poll/data`, live bars + countdown, winner bars linger 5 min, `title`/`style` URL params (1.1.0, wave 16) |
| [CountersController.cs](../../src/VyralBot.Api/Controllers/CountersController.cs) | `GET /counters/death` — the death-counter read endpoint behind the widget |
| [LeaderboardController.cs](../../src/VyralBot.Api/Controllers/LeaderboardController.cs) | `GET /leaderboard/data` — clamped, platform-normalized leaderboard reads for the widget |
| [ChatFeedController.cs](../../src/VyralBot.Api/Controllers/ChatFeedController.cs) | `GET /chatfeed/data` — newest chat rows for the captions widget |
| [GoalsController.cs](../../src/VyralBot.Api/Controllers/GoalsController.cs) | `GET /goals/data` — enabled goals + completion counts for the goals widget |
| [PollController.cs](../../src/VyralBot.Api/Controllers/PollController.cs) | `GET /poll/data` — active-poll snapshot + linger results for the poll widget |
| [BrowserSource/audio.html](../../BrowserSource/audio.html) | Legacy copy of the same page, hard-coded to port 8080 (unreferenced by the running app) |
| [WebServerService.cs](../../src/VyralBot.Api/Services/WebServerService.cs) | Embedded Kestrel server: static files (`audio.html`, `/sounds/*`), `/audio-ws` WebSocket endpoint, broadcast bridge wiring |
| [AudioWebSocketBroadcaster.cs](../../src/VyralBot.Core/Services/Audio/AudioWebSocketBroadcaster.cs) | Static delegate that lets Core's SoundService push JSON to connected browser pages without referencing the API project |
| [SoundService.cs](../../src/VyralBot.Core/Services/Audio/SoundService.cs) | Produces the play-sound broadcasts (`PlaySoundFileAsync`) and serves/uploads the sound files |
| ~~IAudioWebSocketServer.cs / AudioWebSocketServer.cs~~ | Deleted — the legacy standalone HttpListener server (port 8080) and its `AudioEvent` model were removed from the codebase; only the Kestrel path remains |
| [WebhookCoordinatorService.cs](../../src/VyralBot.Api/Services/WebhookCoordinatorService.cs) | Starts/stops the Kestrel server (port from `WebServerSettings`) alongside ngrok |
| [NgrokService.cs](../../src/VyralBot.Api/Services/NgrokService.cs) | Runs the ngrok tunnel used for webhooks (not for the audio page) |
| [AudioSettingsView.xaml](../../src/VyralBot.UI/Views/AudioSettingsView.xaml) | The Browser Source (OBS) card with the URL, copy button, and setup steps users actually see |
| [MainWindow.xaml](../../src/VyralBot.UI/MainWindow.xaml) | The "Browser Sources" sidebar button at MainWindow.xaml:324-332 (navigates to Audio Settings) |

### The contract, both sides side by side

What the page expects (audio.html:81-92):

```html
ws.onmessage = (event) => {
    // Example: { type: 'play', sound: 'alert.mp3' }
    try {
        const data = JSON.parse(event.data);
        if (data.type === 'play' && data.sound) {
            playSound(data.sound, data.volume);
        }
    } catch (e) {
        console.error('Invalid message:', event.data);
    }
};
```

What the server now sends (SoundService.cs:163-176):

```csharp
// Send to browser source for stream
if (settings.StreamSoundsEnabled)
{
    // Browser-source page contract (wwwroot/audio.html):
    //   {"type":"play","sound":"<file>","volume":<0..1>}
    var broadcastVolume = Math.Clamp(float.IsNaN(volume) ? 1f : volume, 0f, 1f);
    var payload = new
    {
        type = "play",
        sound = soundFile,
        volume = broadcastVolume
    };
    var json = System.Text.Json.JsonSerializer.Serialize(payload);
    if (AudioWebSocketBroadcaster.BroadcastAsync != null)
    {
        await AudioWebSocketBroadcaster.BroadcastAsync(json);
    }
}
```

And the payload SoundService broadcasts today (an anonymous camelCase object, SoundService.cs:287-293 — the old `AudioEvent` class was deleted with the legacy server):

```csharp
var payload = new
{
    type = "play",
    sound = soundFile,
    volume = broadcastVolume   // final loudness (master × per-event), clamped 0..1
};
```

### How Kestrel wires the broadcast

```csharp
// src/VyralBot.Api/Services/WebServerService.cs:90-135 (excerpt)
var audioClients = new ConcurrentDictionary<System.Net.WebSockets.WebSocket, bool>();
_app.Map("/audio-ws", async context =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        var ws = await context.WebSockets.AcceptWebSocketAsync();
        audioClients[ws] = true;
        // ... park in a receive loop until close ...
    }
});

// Expose a static method to broadcast to all audio clients
VyralBot.Core.Services.Audio.AudioWebSocketBroadcaster.BroadcastAsync = async (string message) =>
{
    var tasks = audioClients.Keys
        .Where(ws => ws.State == WebSocketState.Open)
        .Select(ws => ws.SendAsync(
            new ArraySegment<byte>(Encoding.UTF8.GetBytes(message)),
            WebSocketMessageType.Text, true, CancellationToken.None));
    await Task.WhenAll(tasks);
};
```

## Data & Settings

| Setting | Where | Notes |
|---|---|---|
| `WebServerSettings.Port` | Per-profile SQLite | Server port; default and effective value 5000 for audio (read by `WebhookCoordinatorService`) |
| `WebServerSettings.AutoStartOnLaunch` | Per-profile SQLite | Whether the Kestrel+ngrok stack starts with the app |
| `WebServerSettings.CurrentTunnelUrl` | Per-profile SQLite | The ngrok https URL — for webhooks, not for the audio page |
| `AudioSettings` (Configs key) | Per-profile SQLite | JSON blob with `StreamSoundsEnabled`, which gates whether broadcasts are sent at all — see [Audio Settings](audio.md) |
| `EventSound_<Platform>_<event>` (Configs keys) | Per-profile SQLite | Which sound file each event plays — see [Sounds Management](sounds.md) |
| Sound files | Current profile's `sounds` folder on disk (legacy app-global folder as fallback) | Served as static files at `/sounds/<filename>` |

The audio page itself stores nothing — it is stateless except for its sound cache.

## Status & Known Issues

Every item below is verified against the current code. See the [known issues ledger](../reference/known-issues.md) for app-wide context.

1. **The "Browser Sources" sidebar button works** — the button at MainWindow.xaml:324-332 navigates to the Audio Settings page (wired 2026-08-15), where the browser-source card lives. There is no dedicated Browser Sources page. [known issues ledger](../reference/known-issues.md) (KI-17 resolved)
2. **Browser-source playback works** — the server broadcasts the camelCase `{"type":"play",...}` shape and the page plays it (fixed 2026-08-15, KI-4 resolved). [known issues ledger](../reference/known-issues.md)
3. **Browser-side volume is applied** — the page sets the audio element's volume from the message's `volume` field (0..1, already master × per-event, clamped server-side). [known issues ledger](../reference/known-issues.md)
4. **"Connected Clients" on the Audio Settings page is live** — it polls `AudioWebSocketBroadcaster.ConnectedClientCount` every 2 seconds, so you can see whether OBS is attached (see [Audio](audio.md)). [known issues ledger](../reference/known-issues.md)
5. **The page is effectively localhost-only by configuration** — it derives URLs from `window.location` (port-agnostic), but the URL you configure in OBS is `http://localhost:5000/audio.html` and the server binds localhost, so OBS must run on the same machine; the ngrok public URL serves [Kick webhooks](../settings/web-server-ngrok.md), not the browser source. [known issues ledger](../reference/known-issues.md)
6. **Legacy leftovers are unreferenced only** — `BrowserSource/audio.html` (port 8080) remains in the repo but nothing serves it; the old `AudioWebSocketServer` HttpListener server was deleted outright, and only the Kestrel server on 5000 runs (App.xaml.cs:306). [known issues ledger](../reference/known-issues.md)
7. **Seven pages, one server** — audio, song player, death counter, leaderboard, chat feed, goals and poll overlay are served from the same embedded loopback server; visual alerts remain an in-app WPF window ([Visual Alerts](visual-alerts.md)) and giveaways have no overlay output. The five visible widgets are poll-based (2 s / 3 s / 3 s / 10 s / 15 s), not WebSocket-push like the audio page — fine for counts and top-lists, and they self-heal after an app restart. The goals widget's 15 s cadence and completion/target semantics are tracked as [KI-63](../reference/known-issues.md). [known issues ledger](../reference/known-issues.md)
