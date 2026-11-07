# Audio Settings

> Choose where event sounds play — locally in the app for you, or through the OBS browser source for your viewers — and set the master volume everything scales from.

## Overview

The Audio Settings page is a small page with an outsized job: it decides **where sounds go**. Two toggles split every event sound into two independent outputs:

- **Streamer Sounds** — play locally inside the Vyral app, so *you* hear them (every supported format — fixed 2026-11-07, see below).
- **Stream Sounds** — push the sound to the OBS browser source, so *viewers* hear it in the stream. This path works: the broadcast is the camelCase shape the page understands and the volume is honored (fixed 2026-08-15 — see [Browser Sources](browser-sources.md)).

Everything else on the page is a master volume slider, a server status panel with a **live** connected-browser-source count (the port is fixed at 5000), and the browser-source URL panel with OBS setup steps. There is no audio device selection, no per-app routing, and no ducking — the page is only about *where* sounds play and *how loud*.

Local playback plays every supported format through WPF `MediaPlayer` (fixed 2026-11-07 — the old Windows `.wav`-only player is gone), and the volume sliders apply to it.

## User Guide

Open **Stream Tools → Audio Settings** in the sidebar (also reachable from the Tools hub card "Audio Settings").

### Sound Output card

- **Streamer Sounds** (toggle, on by default) — "Play sounds locally in the app (you hear them)." When on, every event sound also plays on your desktop through Vyral itself — every supported format (`.wav`/`.mp3`/`.ogg`/`.m4a`) plays locally with volume honored, one short-lived MediaPlayer instance per sound so simultaneous effects overlap.
- **Stream Sounds** (toggle, on by default) — "Send sounds to browser source (viewers hear them)." When on, event sounds are also broadcast over WebSocket to the OBS browser-source page, at the computed volume. See [Browser Sources](browser-sources.md) for the OBS setup.

At least one of the two toggles must be on for any sound to play at all; if both are off, sound playback is skipped entirely (SoundService.cs:114-118).

### Volume card

- **Master Volume** slider, 0–100 in steps of 10, shown as a percentage. This scales *all* event sounds. The final volume of an event sound is `MasterVolume% × per-event volume` — so a 50% master with a 100% event volume plays at half volume.
- Changes here are not live-saved: press **Save Settings** (bottom right) to persist. The next sound played after saving uses the new values — the settings are re-read from the database on every play.

### WebSocket Server card

- **Status / Connected Clients** — shows "Running" with a green dot and a **live** count of connected browser-source pages. The count polls `AudioWebSocketBroadcaster.ConnectedClientCount` every 2 seconds, so you can tell at a glance whether OBS (or any other open copy of the page) is attached.
- **Port** — the box is labeled "Port (fixed at 5000)" and any value other than 5000 is reverted with a warning logged ("Audio browser source port is fixed at 5000", AudioSettingsViewModel.cs:195-204). The underlying server port lives in [Web Server & ngrok](../settings/web-server-ngrok.md) settings; the browser-source page itself is port-agnostic (it derives its URLs from `window.location`), but the audio setup is pinned to 5000 for now.
- **Restart Server** button — vestigial. It only shows a message box and restarts nothing (AudioSettingsViewModel.cs:169-179).

### Browser Source (OBS) card

- Shows the read-only URL `http://localhost:5000/audio.html` with a **Copy URL** button (copies to clipboard and confirms with a dialog).
- Lists the four OBS setup steps (add a Browser Source, paste the URL, set 1920x1080, sounds play automatically). The full walkthrough — including prerequisites and troubleshooting — is in [Browser Sources](browser-sources.md).

### Test Sound and Save

- **Test Sound** — plays the *first* file (alphabetically) from your sound library at the current master volume, through the same pipeline as real events (so it respects both output toggles). If your library is empty it tells you to upload sounds first on the [Sounds Management](sounds.md) page.
- **Save Settings** — persists the two toggles, master volume, and (always-5000) port as a single JSON blob, then confirms with a "saved successfully" dialog.

## How It Works (Developer)

All state lives in one `AudioSettings` JSON blob stored under the Configs key `AudioSettings` in the per-profile database. The settings page and the playback engine each read/write that same blob:

- **Load** — `AudioSettingsViewModel.InitializeAsync` deserializes the blob into its observable properties, force-sets `WebSocketPort = 5000` and `BrowserSourceUrl`, and starts the 2-second client-count polling timer (AudioSettingsViewModel.cs:48-75).
- **Save** — `SaveSettingsAsync` serializes the properties back with `SetConfigValueAsync("AudioSettings", json)` (AudioSettingsViewModel.cs:77-111).
- **Consume** — `SoundService.GetAudioSettingsAsync` reads the same Configs key *on every play* (SoundService.cs:237-272), so there is no caching and no in-memory sync problem; unsaved UI edits simply have no effect until saved.
- **Client count** — a `DispatcherTimer` polls `AudioWebSocketBroadcaster.ConnectedClientCount` (the Kestrel `/audio-ws` client set) every 2 seconds and updates `ConnectedClients` (AudioSettingsViewModel.cs:217-228). Because the VM is a DI singleton, the timer is created once and disposed only at shutdown — negligible cost, but it ticks even while the page is not visible (KI-32).

Playback decisions in `SoundService.PlaySoundFileAsync` (SoundService.cs:141-189):

- If `StreamerSoundsEnabled` → `PlayLocalSoundAsync` on a background thread → `PlayWithMediaPlayer`: a per-playback WPF `MediaPlayer` on its own short-lived STA thread, every format, volume applied (SoundService.cs:402-455).
- If `StreamSoundsEnabled` → serialize the camelCase play payload (`{"type":"play","sound":...,"volume":0..1}`) and invoke the static `AudioWebSocketBroadcaster.BroadcastAsync` delegate, which the embedded Kestrel server points at all connected `/audio-ws` sockets. See [Browser Sources](browser-sources.md) for the full server side.

Volume math (SoundService.cs:129-131): final volume = `MasterVolume / 100f` × per-event volume (itself a 0–1 value read from `EventVolume_<Platform>_<event>`; see [Sounds Management](sounds.md)).

The page has no `IAudioWebSocketServer` dependency anymore — the commented-out field in AudioSettingsViewModel.cs:14 ("No longer needed") marks the transition from the legacy 8080 HttpListener server to Kestrel.

## Files & Code

| File | Role |
|---|---|
| [AudioSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/AudioSettingsViewModel.cs) | Page logic: load/save the `AudioSettings` blob, clamp volume, pin port to 5000, live client-count poll, test sound, copy URL |
| [AudioSettingsView.xaml](../../src/VyralBot.UI/Views/AudioSettingsView.xaml) | The page: Sound Output toggles, Volume card, WebSocket Server card, Browser Source (OBS) card, Test/Save buttons |
| [SoundService.cs](../../src/VyralBot.Core/Services/Audio/SoundService.cs) | Reads the blob on every play; gates local vs browser output; defines the `AudioSettings` DTO (SoundService.cs:321-327) |
| [ISoundService.cs](../../src/VyralBot.Core/Services/Audio/ISoundService.cs) | Service interface consumed by the page's Test Sound and by event triggers |
| [AudioWebSocketBroadcaster.cs](../../src/VyralBot.Core/Services/Audio/AudioWebSocketBroadcaster.cs) | Static delegate bridging SoundService broadcasts to the Kestrel server's WebSocket clients |
| [WebServerService.cs](../../src/VyralBot.Api/Services/WebServerService.cs) | The embedded Kestrel server (localhost:5000) that serves the browser-source page and `/audio-ws` |

### The settings blob

The DTO is defined alongside SoundService (SoundService.cs:321-327) — note the DTO's port default of 8080, a leftover that never surfaces because the ViewModel pins 5000 on every init and save:

```csharp
public class AudioSettings
{
    public bool StreamerSoundsEnabled { get; set; } = true;
    public bool StreamSoundsEnabled { get; set; } = true;
    public int MasterVolume { get; set; } = 100; // 0-100
    public int WebSocketPort { get; set; } = 8080;
}
```

Saved as:

```csharp
// AudioSettingsViewModel.cs:82-91 (excerpt)
var settings = new AudioSettings
{
    StreamerSoundsEnabled = StreamerSoundsEnabled,
    StreamSoundsEnabled = StreamSoundsEnabled,
    MasterVolume = MasterVolume,
    WebSocketPort = WebSocketPort // always 5000 by the time it gets here
};
var settingsJson = JsonSerializer.Serialize(settings);
await _dataService.SetConfigValueAsync("AudioSettings", settingsJson);
```

### How the toggles gate playback

```csharp
// SoundService.cs:153-180 (excerpt) — read on every single play
var settings = await GetAudioSettingsAsync();

// Play locally for streamer
if (settings.StreamerSoundsEnabled)
{
    _ = Task.Run(() => PlayLocalSoundAsync(filePath, volume));
}

// Send to browser source for stream
if (settings.StreamSoundsEnabled)
{
    var broadcastVolume = Math.Clamp(volume, 0f, 1f);
    var payload = new { type = "play", sound = soundFile, volume = broadcastVolume };
    var json = System.Text.Json.JsonSerializer.Serialize(payload);   // camelCase by construction
    if (AudioWebSocketBroadcaster.BroadcastAsync != null)
    {
        await AudioWebSocketBroadcaster.BroadcastAsync(json);
    }
}
```

### The port pin

```csharp
// AudioSettingsViewModel.cs:195-204 — the port box is editable but not changeable
partial void OnWebSocketPortChanged(int value)
{
    // The port is fixed at 5000. The browser-source page derives its WebSocket
    // URL from window.location, so it follows whatever port serves it — but
    // the audio setup stays pinned to 5000 for now.
    if (value != 5000)
    {
        _logger.LogWarning("Audio browser source port is fixed at 5000. Ignoring user port change ({Port}).", value);
        WebSocketPort = 5000;
    }
    BrowserSourceUrl = "http://localhost:5000/audio.html";
}
```

## Data & Settings

| Key | Store | Type | Meaning |
|---|---|---|---|
| `AudioSettings` | Configs table (per-profile SQLite) | JSON string | The whole page's state: `StreamerSoundsEnabled`, `StreamSoundsEnabled` (bool, default true), `MasterVolume` (0–100, default 100), `WebSocketPort` (int; DTO default 8080, effectively always 5000) |

Related settings that this page depends on but does not own:

- `WebServerSettings` (port, auto-start) — see [Web Server & ngrok](../settings/web-server-ngrok.md).
- `EventSound_<Platform>_<event>` / `EventVolume_<Platform>_<event>` Configs keys — see [Sounds Management](sounds.md).

## Status & Known Issues

Every item below is verified against the current code. See the [known issues ledger](../reference/known-issues.md) for app-wide context.

1. **Stream Sounds (viewer-side) works** — the broadcast is the camelCase `{"type":"play",...}` shape with volume honored. Full analysis in [Browser Sources](browser-sources.md). [known issues ledger](../reference/known-issues.md) (KI-4 resolved)
2. **Local playback supports every format** — fixed 2026-11-07: all formats (`.wav`, `.mp3`, `.ogg`, `.m4a`) play through WPF `MediaPlayer` on a short-lived STA thread, so the volume sliders apply locally too; the old wav-only `System.Media.SoundPlayer` path is gone (SoundService.cs:402-407). [known issues ledger](../reference/known-issues.md)
3. **The port is pinned to 5000** — edits to the port box revert instantly (AudioSettingsViewModel.cs:195-204), and the DTO's 8080 default never surfaces; the browser-source page is port-agnostic now, but making the server port configurable for audio is future work. [known issues ledger](../reference/known-issues.md)
4. **"Connected Clients" is live** — a 2-second poll of `AudioWebSocketBroadcaster.ConnectedClientCount` (AudioSettingsViewModel.cs:217-228); you can see whether OBS is attached from this page. [known issues ledger](../reference/known-issues.md)
5. **The client-count timer only stops at shutdown** — the VM is a DI singleton, so its polling timer runs for the whole process lifetime, visible page or not (negligible cost — KI-32). [known issues ledger](../reference/known-issues.md)
6. **"Restart Server" is vestigial** — it only shows an informational message box and restarts nothing (AudioSettingsViewModel.cs:169-179). [known issues ledger](../reference/known-issues.md)
7. **No device selection, routing, or ducking** — "Audio" here means output destination and volume only; you cannot pick an output device (e.g. a VB-Cable) or duck music. Tracked as KI-50. [known issues ledger](../reference/known-issues.md)
8. **Test Sound depends on library contents** — it plays the alphabetically first file, whatever that is; it is not a dedicated test tone (AudioSettingsViewModel.cs:120-136). [known issues ledger](../reference/known-issues.md)
