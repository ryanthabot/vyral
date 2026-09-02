# Sounds Management

> Upload a library of sound files and map each of the ten supported Kick/Twitch events to a sound, with its own volume — the page where "what plays when someone follows" is decided.

## Overview

Sounds Management is two panels side by side:

- **Sound Library** (left) — upload sound files, preview them, delete them. Files are stored in the app's web folder so the embedded server can serve them to OBS.
- **Event Sound Assignments** (right) — a fixed table of ten platform events. For each one you can assign a sound from your library, set a per-event volume, and test it.

The page only manages the *mappings*. The actual "when" is elsewhere: sounds fire from the chat event handlers in [ChatViewModel](#files--code) when a real (or [tested](../chat-features/event-testing.md)) follow/sub/raid/cheer/host arrives. Where the sound physically plays — locally for you, in the browser source for viewers — is controlled by the two toggles on [Audio Settings](audio.md).

Two things to know before uploading:

- **The app ships with no sounds.** Nothing is bundled; a startup check only logs warnings for missing defaults like `follow.wav` (SoundService.cs:202-227). Upload your own.
- **Only `.wav` files make sound locally today.** Upload accepts `.wav`, `.mp3`, `.ogg`, and `.m4a`, but local playback uses the Windows `.wav`-only player, and the browser-source path is currently broken — so an mp3 assignment plays nowhere right now. Prefer `.wav`.

## User Guide

Open **Stream Tools → Sounds Management** in the sidebar (also the "Sounds" card on the Tools hub).

### Uploading sounds

1. Click **Upload Sound** under the Sound Library list.
2. Pick a file — the dialog accepts `.wav`, `.mp3`, `.ogg`, and `.m4a`.
3. The file is copied into the app's sounds folder and appears in the list. If a file with the same name already exists, the new copy is renamed with a suffix (`sound_1.wav`, `sound_2.wav`, ...) rather than overwriting it.

To hear a file, select it in the list and click **Play Selected**. To remove one, select it and click **Delete Selected** — after a confirmation, the file is deleted **and any event assignments that used it are cleared automatically** (the affected events go back to "No sound assigned").

### Assigning sounds to events

The right panel lists the ten supported events:

| Event row | Platform | Event type |
|---|---|---|
| Kick - Follow | Kick | follow |
| Kick - Subscribe | Kick | subscribe |
| Kick - Gift Sub | Kick | giftsub |
| Kick - Host | Kick | host |
| Twitch - Follow | Twitch | follow |
| Twitch - Subscribe | Twitch | subscribe |
| Twitch - Re-sub | Twitch | resub |
| Twitch - Gift Sub | Twitch | giftsub |
| Twitch - Raid | Twitch | raid |
| Twitch - Cheer | Twitch | cheer |

To assign:

1. Select the sound you want in the **Sound Library** list (this is the "selected sound" all assignments use).
2. On the event's row, click **Assign**. The sound file name appears in the row's Sound File column.
3. To remove an assignment, click **Unassign** on that row.
4. To hear it with that event's volume, click the row's **Test** button.

Assignments save to the database immediately — there is no Save button on this page.

### Per-event volume

Each row has a **Volume** slider from 0% to 100% (steps of 10%). The final loudness of an event sound is this slider times the **Master Volume** from [Audio Settings](audio.md) — e.g. 100% event volume with 50% master plays at half volume. Slider changes save immediately, like assignments.

One honest caveat: a format mismatch means the saved per-event volume is not actually applied when a *real event* fires — only **Test** honors it. See the last item in [Status & Known Issues](#status--known-issues).

### What plays where

- **You hear it** when the Streamer Sounds toggle is on and the file is a `.wav` (Audio Settings).
- **Viewers would hear it** through the OBS browser source when the Stream Sounds toggle is on — but that path is currently broken; see [Browser Sources](browser-sources.md).
- If both toggles are off, nothing plays even with assignments configured.

There are no soundboard/hotkey sounds, no command-triggered sounds, and no YouTube/TikTok/Discord event rows — sounds trigger only from the ten events above and from this page's test buttons.

## How It Works (Developer)

### Storage: files plus config keys

Uploaded files are copied to `src/VyralBot.Api/wwwroot/sounds/` (resolved at runtime relative to the executable: `<exe>\..\VyralBot.Api\wwwroot\sounds`, SoundService.cs:24). That folder is the Kestrel web root, so every uploaded file is also addressable as `http://localhost:5000/sounds/<file>` — which is how the browser-source page fetches them.

Mappings are not an entity; they are 20 Configs rows using two key patterns:

- `EventSound_<Platform>_<eventType>` → the assigned file name (empty string = unassigned)
- `EventVolume_<Platform>_<eventType>` → the slider value

`SoundsViewModel.LoadEventMappingsAsync` builds the fixed ten-row table by reading those keys (SoundsViewModel.cs:87-125); every Assign/Unassign/slider change writes one key immediately.

### Playback flow, end to end

```
platform event → EventBus → ChatViewModel handler (e.g. OnKickFollowAsync, ChatViewModel.cs:1719)
  → SoundService.PlayEventSoundAsync(eventType, platform)        (SoundService.cs:108-139)
      reads AudioSettings blob  → skip if both output toggles off
      reads EventSound_<platform>_<eventType> → skip if unmapped
      volume = MasterVolume/100 × EventVolume_<platform>_<eventType>
  → PlaySoundFileAsync(soundFile, volume)                        (SoundService.cs:141-189)
      StreamerSoundsEnabled → PlayLocalSoundAsync (.wav SoundPlayer; else warn)
      StreamSoundsEnabled  → serialize AudioEvent JSON → AudioWebSocketBroadcaster.BroadcastAsync
                             → Kestrel /audio-ws → OBS browser source (see browser-sources.md)
```

The trigger call sites (verified line numbers): Kick follow/subscribe/giftsub/host at ChatViewModel.cs:1719, 1807, 1878, 1944; Twitch follow/subscribe/resub/giftsub/raid/cheer at ChatViewModel.cs:2107, 2186, 2267, 2329, 2372, 2443. These sit inside the same event handlers that produce the chat announcement, the [visual alert](visual-alerts.md), the currency bonus, and the auto-reply — see [Event Reactions](../chat/event-reactions.md). You can fire any of them on demand from the [Event Testing](../chat-features/event-testing.md) page, which publishes synthetic events onto the real [event bus](../architecture/event-bus.md).

Note that Kick's "Kicks Gifted" and "Reward Redeemed" events show alerts but never play sounds — no `PlayEventSoundAsync` call exists for them.

### Uploads and de-duplication

`SoundService.UploadSoundAsync` validates the extension (.wav/.mp3/.ogg/.m4a), then copies the file into the sounds folder, renaming on collision (SoundService.cs:49-83):

```csharp
// If file exists, append number to avoid overwrite
if (File.Exists(destPath))
{
    var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
    var extension = Path.GetExtension(fileName);
    var counter = 1;
    do
    {
        fileName = $"{nameWithoutExt}_{counter}{extension}";
        destPath = Path.Combine(_soundsDirectory, fileName);
        counter++;
    } while (File.Exists(destPath));
}
await Task.Run(() => File.Copy(sourceFilePath, destPath));
```

`DeleteSoundAsync` only removes the file; the *ViewModel* is what walks the event table and clears any `EventSound_` key that referenced the deleted file (SoundsViewModel.cs:191-197).

### Default sounds: checked, not shipped

At profile load, `MainViewModel` calls `SoundService.InitializeDefaultSoundsAsync()` (MainViewModel.cs:233), which merely checks for five conventional file names and logs a warning for each missing one (SoundService.cs:202-227):

```csharp
var defaultSounds = new Dictionary<string, string>
{
    { "follow.wav",     "Default follow sound" },
    { "subscribe.wav",  "Default subscribe sound" },
    { "raid.wav",       "Default raid sound" },
    { "cheer.wav",      "Default cheer sound" },
    { "giftsub.wav",    "Default gift sub sound" }
};
// ... only logs "⚠️ Default sound missing" — nothing is created or downloaded
```

No audio files ship with the app; nothing is ever auto-assigned. If you want those names to mean something, upload files with those names and assign them yourself.

## Files & Code

| File | Role |
|---|---|
| [SoundsViewModel.cs](../../src/VyralBot.UI/ViewModels/SoundsViewModel.cs) | Page logic: library list, upload/delete/preview, the fixed 10-event mapping table, assign/unassign, per-event volume saves |
| [SoundsView.xaml](../../src/VyralBot.UI/Views/SoundsView.xaml) | The two-panel layout: Sound Library (list + Upload/Play/Delete buttons) and the Event Sound Assignments DataGrid |
| [SoundService.cs](../../src/VyralBot.Core/Services/Audio/SoundService.cs) | File storage (upload/delete/list), `PlayEventSoundAsync` / `PlaySoundFileAsync` pipeline, config-key lookups, default-sound check |
| [ISoundService.cs](../../src/VyralBot.Core/Services/Audio/ISoundService.cs) | Service interface used by the page and by ChatViewModel |
| [IAudioWebSocketServer.cs](../../src/VyralBot.Core/Services/Audio/IAudioWebSocketServer.cs) | Defines the `AudioEvent` message shape broadcast to browser sources |
| [AudioWebSocketBroadcaster.cs](../../src/VyralBot.Core/Services/Audio/AudioWebSocketBroadcaster.cs) | Static delegate carrying broadcasts from SoundService to the Kestrel server's WebSocket clients |
| [WebServerService.cs](../../src/VyralBot.Api/Services/WebServerService.cs) | Kestrel server that serves `/sounds/*` from wwwroot and hosts `/audio-ws` |
| [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) | The ten event handlers that actually trigger sounds at runtime |
| [MainViewModel.cs](../../src/VyralBot.UI/ViewModels/MainViewModel.cs) | Calls `InitializeDefaultSoundsAsync()` at profile load (:233) |
| [wwwroot/sounds](../../src/VyralBot.Api/wwwroot/sounds) | Where uploaded files live on disk (empty in a fresh install) |

### The fixed event list

```csharp
// SoundsViewModel.cs:91-103 — the mapping table is hard-coded, not extensible
var eventTypes = new[]
{
    ("Kick",   "follow",    "Follow"),
    ("Kick",   "subscribe", "Subscribe"),
    ("Kick",   "giftsub",   "Gift Sub"),
    ("Kick",   "host",      "Host"),
    ("Twitch", "follow",    "Follow"),
    ("Twitch", "subscribe", "Subscribe"),
    ("Twitch", "resub",     "Re-sub"),
    ("Twitch", "giftsub",   "Gift Sub"),
    ("Twitch", "raid",      "Raid"),
    ("Twitch", "cheer",     "Cheer")
};
```

### The event-play pipeline

```csharp
// SoundService.cs:108-139 (excerpt)
public async Task PlayEventSoundAsync(string eventType, string platform)
{
    var settings = await GetAudioSettingsAsync();
    if (!settings.StreamerSoundsEnabled && !settings.StreamSoundsEnabled)
        return;                                          // both outputs off → silence

    var soundFile = await GetEventSoundFileAsync(eventType, platform);
    if (string.IsNullOrEmpty(soundFile))
        return;                                          // nothing mapped → silence

    var volume = settings.MasterVolume / 100f;           // master × per-event
    var eventVolume = await GetEventVolumeAsync(eventType, platform);
    volume *= eventVolume;

    await PlaySoundFileAsync(soundFile, volume);         // local SoundPlayer + WS broadcast
}
```

## Data & Settings

| Key | Store | Example | Meaning |
|---|---|---|---|
| `EventSound_Kick_follow` | Configs (per-profile SQLite) | `horn.wav` | Sound file played on a Kick follow |
| `EventSound_Twitch_raid` | Configs | `raid_horn.wav` | Sound file played on a Twitch raid |
| `EventVolume_Kick_follow` | Configs | `0.70` | Per-event volume written by the slider |
| `EventVolume_Twitch_cheer` | Configs | `1.00` | Same pattern for every event row |

The full key set is those two patterns across the ten `<Platform>_<eventType>` combinations in the mapping table above (20 keys total). Related settings owned elsewhere: the `AudioSettings` blob ([Audio Settings](audio.md)) and `WebServerSettings` ([Web Server & ngrok](../settings/web-server-ngrok.md)). Sound files themselves live on disk in [wwwroot/sounds](../../src/VyralBot.Api/wwwroot/sounds), not in the database.

## Status & Known Issues

Every item below is verified against the current code. See the [known issues ledger](../reference/known-issues.md) for app-wide context.

1. **No sounds are bundled** — a fresh install has an empty library, and `InitializeDefaultSoundsAsync` only logs warnings for missing conventional files (SoundService.cs:202-227). Nothing plays until you upload and assign. [known issues ledger](../reference/known-issues.md)
2. **Viewer-side playback is broken** — the browser-source path never fires due to the PascalCase-vs-camelCase JSON mismatch documented in [Browser Sources](browser-sources.md). [known issues ledger](../reference/known-issues.md)
3. **Local playback is `.wav` only** — mp3/ogg/m4a upload fine and can be assigned, but local playback logs "Local playback only supports .wav files" and is silent (SoundService.cs:292-315); combined with issue 2, non-wav files currently make no sound anywhere. [known issues ledger](../reference/known-issues.md)
4. **Per-event volume does not apply to real events** — the slider saves values formatted as `"0.70"`, but `SoundService.GetEventVolumeAsync` parses with `int.TryParse` (SoundService.cs:285), which fails on the decimal string and falls back to full volume. Only this page's **Test** button (which passes the in-memory value directly, SoundsViewModel.cs:312) honors the slider; real events always play at full event volume. [known issues ledger](../reference/known-issues.md)
5. **Only Kick and Twitch events exist** — no YouTube/TikTok/Discord rows (SoundsViewModel.cs:91-103), and no soundboard, hotkeys, or command-triggered sounds anywhere. [known issues ledger](../reference/known-issues.md)
6. **Delete clears assignments but not volumes** — deleting a file nulls out `EventSound_` keys that referenced it (SoundsViewModel.cs:191-197), while the orphaned `EventVolume_` keys are left behind (harmless, but they persist). [known issues ledger](../reference/known-issues.md)
7. **Sound triggers depend on the wider event pipeline** — Twitch events additionally depend on the EventSub connection caveat described under [Twitch](../platforms/twitch.md); without events arriving, no sounds fire. [known issues ledger](../reference/known-issues.md)
