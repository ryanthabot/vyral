# Audio Settings

> Choose where event sounds play — locally in the app for you, or through the OBS browser source for your viewers — and set the master volume everything scales from.

## Overview

The Audio Settings page is a small page with an outsized job: it decides **where sounds go**. Two toggles split every event sound into two independent outputs:

- **Streamer Sounds** — play locally inside the VyralBot app, so *you* hear them (works for `.wav` files only).
- **Stream Sounds** — push the sound to the OBS browser source, so *viewers* hear it in the stream (currently broken by a known bug — see below).

Everything else on the page is a master volume slider, a read-mostly server status panel (the port is pinned to 5000), and the browser-source URL panel with OBS setup steps. There is no audio device selection, no per-app routing, and no ducking — the page is only about *where* sounds play and *how loud*.

One honest caveat before you rely on the viewer-side path: the **Stream Sounds** output currently cannot fire because of a JSON contract mismatch between the app and the browser-source page. The toggle is saved and respected by the code, but the receiving page never plays anything. Local playback works. Details in [Browser Sources](browser-sources.md).

## User Guide

Open **Stream Tools → Audio Settings** in the sidebar (also reachable from the Tools hub card "Audio Settings").

### Sound Output card

- **Streamer Sounds** (toggle, on by default) — "Play sounds locally in the app (you hear them)." When on, every event sound also plays on your desktop through VyralBot itself. Local playback uses Windows' built-in `.wav` player, so **only `.wav` files actually make sound here** — an `.mp3`/`.ogg`/`.m4a` assignment logs a warning and plays nothing locally.
- **Stream Sounds** (toggle, on by default) — "Send sounds to browser source (viewers hear them)." When on, event sounds are also broadcast over WebSocket to the OBS browser-source page. See [Browser Sources](browser-sources.md) for the OBS setup — and for why this path is currently broken.

At least one of the two toggles must be on for any sound to play at all; if both are off, sound playback is skipped entirely (SoundService.cs:114-118).

### Volume card

- **Master Volume** slider, 0–100 in steps of 10, shown as a percentage. This scales *all* event sounds. The final volume of an event sound is `MasterVolume% × per-event volume` — so a 50% master with a 100% event volume plays at half volume.
- Changes here are not live-saved: press **Save Settings** (bottom right) to persist. The next sound played after saving uses the new values — the settings are re-read from the database on every play.

### WebSocket Server card

- **Status / Connected Clients** — shows "Running" with a green dot and a client count. Treat both as decorative: status is hard-coded to Running (AudioSettingsViewModel.cs:66) and the client count is a stub that always shows 0 (AudioSettingsViewModel.cs:67) — it never reflects actual OBS connections.
- **Port** — you can type into this box, but any value other than 5000 is immediately reverted with a warning logged: "Audio browser source now always uses port 5000" (AudioSettingsViewModel.cs:195-204). The audio browser source is effectively fixed to port 5000; the underlying server port lives in [Web Server & ngrok](../settings/web-server-ngrok.md) settings.
- **Restart Server** button — vestigial. It only shows a message box saying the audio server "is now always running on port 5000" and restarts nothing (AudioSettingsViewModel.cs:169-179).

### Browser Source (OBS) card

- Shows the read-only URL `http://localhost:5000/audio.html` with a **Copy URL** button (copies to clipboard and confirms with a dialog).
- Lists the four OBS setup steps (add a Browser Source, paste the URL, set 1920x1080, sounds play automatically). The full walkthrough — including prerequisites and troubleshooting — is in [Browser Sources](browser-sources.md).

### Test Sound and Save

- **Test Sound** — plays the *first* file (alphabetically) from your sound library at the current master volume, through the same pipeline as real events (so it respects both output toggles, and is likewise subject to the `.wav`-only local limitation and the broken browser path). If your library is empty it tells you to upload sounds first on the [Sounds Management](sounds.md) page.
- **Save Settings** — persists the two toggles, master volume, and (always-5000) port as a single JSON blob, then confirms with a "saved successfully" dialog.

## How It Works (Developer)

All state lives in one `AudioSettings` JSON blob stored under the Configs key `AudioSettings` in the per-profile database. The settings page and the playback engine each read/write that same blob:

- **Load** — `AudioSettingsViewModel.InitializeAsync` deserializes the blob into its observable properties, then force-sets `WebSocketPort = 5000` and `BrowserSourceUrl` (AudioSettingsViewModel.cs:48-75).
- **Save** — `SaveSettingsAsync` serializes the properties back with `SetConfigValueAsync("AudioSettings", json)` (AudioSettingsViewModel.cs:77-111).
- **Consume** — `SoundService.GetAudioSettingsAsync` reads the same Configs key *on every play* (SoundService.cs:237-272), so there is no caching and no in-memory sync problem; unsaved UI edits simply have no effect until saved.

Playback decisions in `SoundService.PlaySoundFileAsync` (SoundService.cs:141-189):

- If `StreamerSoundsEnabled` → `PlayLocalSoundAsync` on a background thread: `System.Media.SoundPlayer.PlaySync()` for `.wav`, otherwise a warning log and silence (SoundService.cs:292-315).
- If `StreamSoundsEnabled` → serialize an `AudioEvent` (PascalCase, no naming policy) and invoke the static `AudioWebSocketBroadcaster.BroadcastAsync` delegate, which the embedded Kestrel server points at all connected `/audio-ws` sockets. See [Browser Sources](browser-sources.md) for the full server side and the contract mismatch.

Volume math (SoundService.cs:129-131): final volume = `MasterVolume / 100f` × per-event volume (itself a 0–1 value read from `EventVolume_<Platform>_<event>`; see [Sounds Management](sounds.md)).

The page has no `IAudioWebSocketServer` dependency anymore — the commented-out field in AudioSettingsViewModel.cs:14 ("No longer needed") marks the transition from the legacy 8080 HttpListener server to Kestrel.

## Files & Code

| File | Role |
|---|---|
| [AudioSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/AudioSettingsViewModel.cs) | Page logic: load/save the `AudioSettings` blob, clamp volume, pin port to 5000, test sound, copy URL |
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
    var audioEvent = new AudioEvent { EventType = "manual", Platform = "Manual",
                                       SoundFile = soundFile, Volume = volume };
    var json = System.Text.Json.JsonSerializer.Serialize(audioEvent);
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
    // Port is now fixed at 5000
    if (value != 5000)
    {
        _logger.LogWarning("Audio browser source now always uses port 5000. Ignoring user port change.");
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

1. **Stream Sounds (viewer-side) cannot currently fire** — the toggle is saved and respected, but the browser-source page only understands `{"type":"play","sound":...}` while the app broadcasts PascalCase `AudioEvent` JSON. Full analysis in [Browser Sources](browser-sources.md). [known issues ledger](../reference/known-issues.md)
2. **Local playback supports `.wav` only** — `System.Media.SoundPlayer` cannot play mp3/ogg/m4a; non-wav assignments log "Local playback only supports .wav files" and are silent locally (SoundService.cs:292-315). Since the browser path is also broken, mp3 uploads effectively make no sound anywhere right now. [known issues ledger](../reference/known-issues.md)
3. **The port is pinned to 5000** — edits to the port box revert instantly (AudioSettingsViewModel.cs:195-204), and the DTO's 8080 default never surfaces; the stored `WebSocketPort` value is decorative. [known issues ledger](../reference/known-issues.md)
4. **"Connected Clients" is a stub showing 0** — it never reads the real WebSocket client count (AudioSettingsViewModel.cs:67, 181-186); you cannot tell from this page whether OBS is connected. [known issues ledger](../reference/known-issues.md)
5. **"Restart Server" is vestigial** — it only shows an informational message box and restarts nothing (AudioSettingsViewModel.cs:169-179). [known issues ledger](../reference/known-issues.md)
6. **No device selection, routing, or ducking** — "Audio" here means output destination and volume only; you cannot pick an output device (e.g. a VB-Cable) or duck music. [known issues ledger](../reference/known-issues.md)
7. **Test Sound depends on library contents** — it plays the alphabetically first file, whatever that is; it is not a dedicated test tone (AudioSettingsViewModel.cs:120-136). [known issues ledger](../reference/known-issues.md)
