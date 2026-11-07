# Browser Sources

> The single OBS browser-source page (`audio.html`) that Vyral serves on `http://localhost:5000` so stream sounds can play on the stream — working end to end, with volume honored, since the 2026-08-15 JSON-contract fix.

## Overview

Vyral ships exactly **one** browser-source page: `audio.html`. It is served by the app's embedded web server (Kestrel, localhost port 5000) and is meant to be added to OBS as a **Browser Source**. When a stream event fires (follow, sub, raid, and so on), Vyral pushes a WebSocket message to every open copy of the page, and the page plays the matching sound file at the sent volume — which OBS captures as part of your stream audio, so **viewers** hear it.

Things worth knowing up front:

- The sidebar button labeled **Browser Sources** (under Stream Tools) navigates to the [Audio Settings](audio.md) page — that is where browser-source setup lives (the tooltip says so). Wired on 2026-08-15.
- There are **no other browser-source pages** in the app. Alerts, giveaways, and polls have no overlay HTML — visual alerts are an in-app WPF window ([Visual Alerts](visual-alerts.md)), and giveaway/poll winners are only posted to chat.
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

- **The pill never turns green** — Vyral's web server is not running on port 5000. Start it from [Web Server & ngrok](../settings/web-server-ngrok.md). The port is fixed at 5000 for audio; it cannot be changed from the Audio Settings page.
- **The pill is green but no sounds play on stream** — check that an event sound is assigned on [Sounds Management](sounds.md) and the **Stream Sounds** toggle is on; if OBS shows the page as muted in the audio mixer, unmute it.
- **Sounds play for you but not viewers** — check that the OBS browser source is not muted in the OBS audio mixer.
- **You run OBS on a different machine than Vyral** — the page connects back to the host it was loaded from (`window.location`), which is `localhost:5000` as configured — so OBS must run on the same computer as Vyral unless you rewrite the URL.

## How It Works (Developer)

### The embedded web server

One in-process ASP.NET Core (Kestrel) app, [WebServerService.cs](../../src/VyralBot.Api/Services/WebServerService.cs), hosts everything browser-facing:

- `StartAsync(port = 5000)` builds a `WebApplication` bound to **localhost only** via `ListenLocalhost` (WebServerService.cs:55) — the server is never directly reachable from the network.
- It pulls shared services (`IEventBus`, `ILoggerFactory`) from the parent WPF DI container (WebServerService.cs:62-63) so web and UI see the same event stream.
- `UseStaticFiles()` serves everything under `wwwroot` — that is `audio.html` and the `/sounds/*` directory (WebServerService.cs:84).
- A minimal-API endpoint maps `/audio-ws` (WebServerService.cs:91-122): it accepts WebSocket upgrades, tracks connected pages in a `ConcurrentDictionary`, and parks each socket in a receive loop until it closes. Nothing is ever read from the client.
- The server is started by `WebhookCoordinatorService` (and the ngrok setup wizards) using the port from `WebServerSettings` (default 5000). See [Web Server architecture](../architecture/web-server.md).

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
- [BrowserSource/audio.html](../../BrowserSource/audio.html) (repo root) — a **legacy** copy that hard-codes `ws://localhost:8080` and `http://localhost:8080/sounds/` (BrowserSource/audio.html:57-58). It belongs to the older [AudioWebSocketServer.cs](../../src/VyralBot.Core/Services/Audio/AudioWebSocketServer.cs), a standalone `HttpListener` implementation that served `<exe>\BrowserSource\audio.html` and sound files from `<exe>\Sounds` on port 8080.
- The legacy server is dead code: it is no longer registered in DI ("AudioWebSocketServer removed; now handled by Kestrel server", App.xaml.cs:173), and MainViewModel notes "Audio WebSocket server now handled by Kestrel (port 5000)" (MainViewModel.cs:231-232). Only the Kestrel path runs today.

### ngrok's actual role

The same Kestrel server is also tunneled publicly by ngrok ([NgrokService.cs](../../src/VyralBot.Api/Services/NgrokService.cs) spawns `ngrok http <port> --region <region> --authtoken <token>`, NgrokService.cs:60). That public URL exists **for receiving Kick webhooks** — it is *not* how OBS consumes the audio page. The server binds to localhost only, and the audio URL points at `localhost:5000`, so the tunnel does not serve the browser source without rewriting the URL. Details in [Web Server & ngrok](../settings/web-server-ngrok.md). A `WebServerDependencyTracker` ([WebServerDependencyTracker.cs](../../src/VyralBot.Core/Services/WebServer/WebServerDependencyTracker.cs)) prevents stopping the server while features depend on it.

## Files & Code

| File | Role |
|---|---|
| [audio.html](../../src/VyralBot.Api/wwwroot/audio.html) | The browser-source page: WebSocket client, status pill, volume-honoring sound playback (current copy) |
| [BrowserSource/audio.html](../../BrowserSource/audio.html) | Legacy copy of the same page, hard-coded to port 8080 (unreferenced by the running app) |
| [WebServerService.cs](../../src/VyralBot.Api/Services/WebServerService.cs) | Embedded Kestrel server: static files (`audio.html`, `/sounds/*`), `/audio-ws` WebSocket endpoint, broadcast bridge wiring |
| [AudioWebSocketBroadcaster.cs](../../src/VyralBot.Core/Services/Audio/AudioWebSocketBroadcaster.cs) | Static delegate that lets Core's SoundService push JSON to connected browser pages without referencing the API project |
| [SoundService.cs](../../src/VyralBot.Core/Services/Audio/SoundService.cs) | Produces the play-sound broadcasts (`PlaySoundFileAsync`) and serves/uploads the sound files |
| [IAudioWebSocketServer.cs](../../src/VyralBot.Core/Services/Audio/IAudioWebSocketServer.cs) | Defines the `AudioEvent` message class (serialized camelCase on the wire) plus the legacy server interface |
| [AudioWebSocketServer.cs](../../src/VyralBot.Core/Services/Audio/AudioWebSocketServer.cs) | Legacy standalone HttpListener WebSocket+HTTP server on port 8080 — dead code, not registered in DI |
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

And the model both derive from (IAudioWebSocketServer.cs:37-63):

```csharp
public class AudioEvent
{
    public required string EventType { get; set; }   // follow, subscribe, raid, ...
    public required string Platform { get; set; }    // Kick or Twitch
    public required string SoundFile { get; set; }   // e.g. "follow.wav"
    public float Volume { get; set; } = 1.0f;        // 0.0 - 1.0
    public Dictionary<string, string>? Data { get; set; }
}
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
6. **Legacy dead code ships alongside the current path** — `BrowserSource/audio.html` (port 8080) and the old `AudioWebSocketServer` HttpListener server are unused; only the Kestrel server on 5000 runs (App.xaml.cs:173). [known issues ledger](../reference/known-issues.md)
7. **No overlay pages exist for anything except audio** — visual alerts are an in-app WPF window ([Visual Alerts](visual-alerts.md)); giveaways and polls have no overlay output at all. [known issues ledger](../reference/known-issues.md)
