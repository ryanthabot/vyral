# Browser Sources

> The single OBS browser-source page (`audio.html`) that VyralBot serves on `http://localhost:5000` so stream sounds can play on the stream — plus an honest account of why viewer-side playback is currently broken.

## Overview

VyralBot ships exactly **one** browser-source page: `audio.html`. It is served by the app's embedded web server (Kestrel, localhost port 5000) and is meant to be added to OBS as a **Browser Source**. When a stream event fires (follow, sub, raid, and so on), VyralBot pushes a WebSocket message to every open copy of the page, and the page plays the matching sound file — which OBS captures as part of your stream audio, so **viewers** hear it.

Things worth knowing up front:

- The sidebar button labeled **Browser Sources** (under Stream Tools) is a dead button — it has no command wired to it and does nothing when clicked. The real browser-source setup lives on the [Audio Settings](audio.md) page.
- There are **no other browser-source pages** in the app. Alerts, giveaways, and polls have no overlay HTML — visual alerts are an in-app WPF window ([Visual Alerts](visual-alerts.md)), and giveaway/poll winners are only posted to chat.
- **Viewer-side sound playback is currently broken by a JSON contract mismatch** between the server and the page. OBS will connect and show "Connected to VyralBot", but sounds will never actually fire in the browser source. Streamer-side (local) sounds still work. See [Status & Known Issues](#status--known-issues).

## User Guide

Because the sidebar button is dead, you reach the browser-source setup through **Stream Tools → Audio Settings** and scroll to the **Browser Source (OBS)** card. That card shows the URL, a Copy button, and short instructions.

### Prerequisites

1. The embedded web server is running (it listens on `http://localhost:5000`). It normally starts with the app when web-server auto-start is enabled — see [Web Server & ngrok](../settings/web-server-ngrok.md).
2. You have uploaded at least one sound and assigned it to an event on the [Sounds Management](sounds.md) page.
3. The **Stream Sounds** toggle on the [Audio Settings](audio.md) page is on (it is on by default).

### Adding the browser source in OBS

1. In OBS, right-click the **Sources** box (or click the **+**) and choose **Add → Browser**.
2. Give it a name (for example "VyralBot Audio") and confirm.
3. In the properties dialog, paste this URL into the **URL** field:

   ```
   http://localhost:5000/audio.html
   ```

4. Set **Width** to `1920` and **Height** to `1080` (or your stream resolution — the page itself is transparent and invisible, the size only matters for OBS bookkeeping).
5. Confirm, and leave the source in your scene.

That is the same URL the Audio Settings page displays with its **Copy URL** button, and the same steps listed on that page (AudioSettingsView.xaml:233-254).

### What you should see

When OBS loads the page, a small status pill appears in the top-left corner of the source:

- **"Connecting to VyralBot..."** (yellow) while it connects.
- **"Connected to VyralBot"** (green) once the WebSocket is open. The pill fades out after 5 seconds.
- **"Disconnected from VyralBot"** (red) if the app is closed. The page silently retries every 3 seconds, so if you restart VyralBot it reconnects on its own.

You can force the pill to reappear at any time by right-clicking the source in OBS and choosing **Refresh**.

### Troubleshooting

- **The pill never turns green** — VyralBot's web server is not running on port 5000. Start it from [Web Server & ngrok](../settings/web-server-ngrok.md). The port is fixed at 5000 for audio; it cannot be changed from the Audio Settings page.
- **The pill is green but no sounds play on stream** — that is the current known JSON contract mismatch (see below); as shipped, browser-source playback never fires. Local playback (the **Streamer Sounds** toggle) is unaffected.
- **Sounds play for you but not viewers** — check that the OBS browser source is not muted in the OBS audio mixer.
- **You run OBS on a different machine than VyralBot** — the page hard-codes `localhost:5000`, so it only works when OBS runs on the same computer as VyralBot.

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

- Connects a WebSocket to the hard-coded `WS_URL = 'ws://localhost:5000/audio-ws'` (audio.html:51) and auto-reconnects every 3 seconds on close (audio.html:53, 75).
- Shows the status pill and hides it 5 seconds after connecting (audio.html:54, 108-113).
- On each message, parses it as JSON and — only if it looks like `{ type: 'play', sound: '...' }` — plays `http://localhost:5000/sounds/<file>` through a cached `Audio` element (audio.html:81-103). Any `volume` field in the message is **ignored**; browser-side volume is not implemented.

Sound files themselves are just static files: whatever you upload on the Sounds Management page lands in `src/VyralBot.Api/wwwroot/sounds/` and is served at `http://localhost:5000/sounds/<filename>`.

### The JSON contract mismatch (why playback is broken)

The only producer of WebSocket messages is `SoundService`, which serializes the `AudioEvent` class with `System.Text.Json.JsonSerializer.Serialize` and **no naming policy** — so the JSON on the wire is PascalCase:

```json
{"EventType":"manual","Platform":"Manual","SoundFile":"follow.wav","Volume":0.8}
```

The page, however, only reacts to `data.type === 'play' && data.sound` (camelCase `type`/`sound` keys that the server never sends). Nothing in the app ever sends `{type:'play'}`. The result: the page connects happily, receives messages, and silently discards every one of them. Browser-source sound playback has never been able to fire as shipped. Fixing it means either serializing with `JsonNamingPolicy.CamelCase` into `{type, sound, volume}` on the server, or teaching the page to read the PascalCase `AudioEvent` shape — and ideally honoring `Volume` while there.

### Two copies of the page, and the legacy server

- `src/VyralBot.Api/wwwroot/audio.html` — the **current** copy served by Kestrel (hard-codes port 5000, path `/audio-ws`).
- [BrowserSource/audio.html](../../BrowserSource/audio.html) (repo root) — a **legacy** copy that hard-codes `ws://localhost:8080` and `http://localhost:8080/sounds/` (BrowserSource/audio.html:57-58). It belongs to the older [AudioWebSocketServer.cs](../../src/VyralBot.Core/Services/Audio/AudioWebSocketServer.cs), a standalone `HttpListener` implementation that served `<exe>\BrowserSource\audio.html` and sound files from `<exe>\Sounds` on port 8080.
- The legacy server is dead code: it is no longer registered in DI ("AudioWebSocketServer removed; now handled by Kestrel server", App.xaml.cs:173), and MainViewModel notes "Audio WebSocket server now handled by Kestrel (port 5000)" (MainViewModel.cs:231-232). Only the Kestrel path runs today.

### ngrok's actual role

The same Kestrel server is also tunneled publicly by ngrok ([NgrokService.cs](../../src/VyralBot.Api/Services/NgrokService.cs) spawns `ngrok http <port> --region <region> --authtoken <token>`, NgrokService.cs:60). That public URL exists **for receiving Kick webhooks** — it is *not* how OBS consumes the audio page. The server binds to localhost only, and `audio.html` hard-codes `localhost:5000`, so the tunnel cannot serve the browser source without editing the page. Details in [Web Server & ngrok](../settings/web-server-ngrok.md). A `WebServerDependencyTracker` ([WebServerDependencyTracker.cs](../../src/VyralBot.Core/Services/WebServer/WebServerDependencyTracker.cs)) prevents stopping the server while features depend on it.

## Files & Code

| File | Role |
|---|---|
| [audio.html](../../src/VyralBot.Api/wwwroot/audio.html) | The browser-source page: WebSocket client, status pill, sound playback (current copy, port 5000) |
| [BrowserSource/audio.html](../../BrowserSource/audio.html) | Legacy copy of the same page, hard-coded to port 8080 (unreferenced by the running app) |
| [WebServerService.cs](../../src/VyralBot.Api/Services/WebServerService.cs) | Embedded Kestrel server: static files (`audio.html`, `/sounds/*`), `/audio-ws` WebSocket endpoint, broadcast bridge wiring |
| [AudioWebSocketBroadcaster.cs](../../src/VyralBot.Core/Services/Audio/AudioWebSocketBroadcaster.cs) | Static delegate that lets Core's SoundService push JSON to connected browser pages without referencing the API project |
| [SoundService.cs](../../src/VyralBot.Core/Services/Audio/SoundService.cs) | Produces the play-sound broadcasts (`PlaySoundFileAsync`) and serves/uploads the sound files |
| [IAudioWebSocketServer.cs](../../src/VyralBot.Core/Services/Audio/IAudioWebSocketServer.cs) | Defines the `AudioEvent` message class (the PascalCase shape actually sent) plus the legacy server interface |
| [AudioWebSocketServer.cs](../../src/VyralBot.Core/Services/Audio/AudioWebSocketServer.cs) | Legacy standalone HttpListener WebSocket+HTTP server on port 8080 — dead code, not registered in DI |
| [WebhookCoordinatorService.cs](../../src/VyralBot.Api/Services/WebhookCoordinatorService.cs) | Starts/stops the Kestrel server (port from `WebServerSettings`) alongside ngrok |
| [NgrokService.cs](../../src/VyralBot.Api/Services/NgrokService.cs) | Runs the ngrok tunnel used for webhooks (not for the audio page) |
| [AudioSettingsView.xaml](../../src/VyralBot.UI/Views/AudioSettingsView.xaml) | The Browser Source (OBS) card with the URL, copy button, and setup steps users actually see |
| [MainWindow.xaml](../../src/VyralBot.UI/MainWindow.xaml) | The (dead) "Browser Sources" sidebar button at MainWindow.xaml:324-332 |

### The mismatch, both sides side by side

What the page expects (audio.html:81-92):

```html
ws.onmessage = (event) => {
    // Example: { type: 'play', sound: 'alert.mp3' }
    try {
        const data = JSON.parse(event.data);
        if (data.type === 'play' && data.sound) {
            playSound(data.sound);
        }
    } catch (e) {
        console.error('Invalid message:', event.data);
    }
};
```

What the server actually sends (SoundService.cs:162-175):

```csharp
// Send to browser source for stream
if (settings.StreamSoundsEnabled)
{
    var audioEvent = new AudioEvent
    {
        EventType = "manual",
        Platform = "Manual",
        SoundFile = soundFile,
        Volume = volume
    };
    var json = System.Text.Json.JsonSerializer.Serialize(audioEvent); // PascalCase: {"EventType":...}
    if (AudioWebSocketBroadcaster.BroadcastAsync != null)
    {
        await AudioWebSocketBroadcaster.BroadcastAsync(json);
    }
```

And the shape both derive from (IAudioWebSocketServer.cs:37-63):

```csharp
public class AudioEvent
{
    public required string EventType { get; set; }   // follow, subscribe, raid, ...
    public required string Platform { get; set; }    // Kick or Twitch
    public required string SoundFile { get; set; }   // e.g. "follow.wav"
    public float Volume { get; set; } = 1.0f;        // 0.0 - 1.0 (ignored by the page)
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
| Sound files | `src/VyralBot.Api/wwwroot/sounds/` on disk | Served as static files at `/sounds/<filename>` |

The audio page itself stores nothing — it is stateless except for its sound cache.

## Status & Known Issues

Every item below is verified against the current code. See the [known issues ledger](../reference/known-issues.md) for app-wide context.

1. **The "Browser Sources" sidebar button is dead** — the button at MainWindow.xaml:324-332 has no `Command` binding, so clicking it does nothing. There is no dedicated Browser Sources page; the functionality surfaces on [Audio Settings](audio.md). [known issues ledger](../reference/known-issues.md)
2. **Browser-source playback can never fire as shipped** — the server broadcasts PascalCase `AudioEvent` JSON while the page only handles `{"type":"play","sound":...}` (SoundService.cs:162-175 vs audio.html:86). The page connects and then discards every message. [known issues ledger](../reference/known-issues.md)
3. **Browser-side volume is not applied** — even once the contract mismatch is fixed, the page ignores any `volume` field in the message (audio.html:81-92); browser sounds would always play at full element volume. [known issues ledger](../reference/known-issues.md)
4. **"Connected Clients" on the Audio Settings page is a stub** — it is hard-coded to 0 and never reads the Kestrel server's real client count (AudioSettingsViewModel.cs:67, 181-186), so you cannot tell from the app whether OBS is attached. [known issues ledger](../reference/known-issues.md)
5. **The page is localhost-only by construction** — the URL is hard-coded in `audio.html` (`ws://localhost:5000/audio-ws`), so OBS must run on the same machine, and the ngrok public URL cannot be used for the browser source without editing the HTML. ngrok exists for [Kick webhooks](../settings/web-server-ngrok.md). [known issues ledger](../reference/known-issues.md)
6. **Legacy dead code ships alongside the current path** — `BrowserSource/audio.html` (port 8080) and the old `AudioWebSocketServer` HttpListener server are unused; only the Kestrel server on 5000 runs (App.xaml.cs:173). [known issues ledger](../reference/known-issues.md)
7. **No overlay pages exist for anything except audio** — visual alerts are an in-app WPF window ([Visual Alerts](visual-alerts.md)); giveaways and polls have no overlay output at all. [known issues ledger](../reference/known-issues.md)
