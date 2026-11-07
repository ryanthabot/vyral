# Sounds Management

> Upload a library of sound files and map each of the fourteen supported Kick/Twitch events to a sound, with its own volume — the page where "what plays when someone follows" is decided.

## Overview

Sounds Management is two panels side by side:

- **Sound Library** (left) — upload sound files, preview them, delete them. Files live in the **current profile's `sounds` folder** (migrated there automatically from the old app-global location), and the embedded server serves them to OBS at `/sounds/<file>`.
- **Event Sound Assignments** (right) — a fixed table of fourteen platform events. For each one you can assign a sound from your library, set a per-event volume, and test it.

The page only manages the *mappings*. The actual "when" is elsewhere: sounds fire from the chat event handlers in [ChatViewModel](#files--code) when a real (or [tested](../chat-features/event-testing.md)) follow/sub/raid/cheer/host/redemption arrives. Where the sound physically plays — locally for you, in the browser source for viewers — is controlled by the two toggles on [Audio Settings](audio.md).

Two things to know before uploading:

- **The app ships with no sounds.** Nothing is bundled; a startup check only logs warnings for missing defaults like `follow.wav` (SoundService.cs). Upload your own.
- **Volume now applies to every format, `.wav` included.** All supported formats (`.wav`, `.mp3`, `.ogg`, `.m4a`) play locally through WPF `MediaPlayer` — which honors master and per-event volume. (Older builds used the Windows `.wav`-only `SoundPlayer`, which had no volume API and made wav event sounds ignore the sliders; that path is gone. Each playback runs on its own short-lived STA thread with its own MediaPlayer instance, so simultaneous sounds still overlap.)

## User Guide

Open **Stream Tools → Sounds Management** in the sidebar (also the "Sounds" card on the Tools hub).

### Where your files live (and the automatic migration)

Uploaded sounds are stored in **`<profile data folder>\sounds`** — the profile is the app's user-data boundary, so sounds survive alongside the profile's database and travel with profile exports. On first run after the change, SoundService performs a **one-time best-effort migration**: files from the legacy app-global sounds folder are copied into the profile folder (never overwritten, originals never deleted), and once the profile folder has any sound file it is authoritative — later launches don't re-migrate. The web server serves `/sounds/*` from the profile folder first, with the legacy app-global location kept as a fallback.

### Uploading sounds

1. Click **Upload Sound** under the Sound Library list.
2. Pick a file — the dialog accepts `.wav`, `.mp3`, `.ogg`, and `.m4a`.
3. The file is copied into the profile's sounds folder and appears in the list. If a file with the same name already exists, the new copy is renamed with a suffix (`sound_1.wav`, `sound_2.wav`, ...) rather than overwriting it.

To hear a file, select it in the list and click **Play Selected**. To remove one, select it and click **Delete Selected** — after a confirmation, the file is deleted **and any event assignments that used it are cleared automatically** (the affected events go back to "No sound assigned").

### Assigning sounds to events

The right panel lists the fourteen supported events:

| Event row | Platform | Event type |
|---|---|---|
| Kick - Follow | Kick | follow |
| Kick - Subscribe | Kick | subscribe |
| Kick - Gift Sub | Kick | giftsub |
| Kick - Host | Kick | host |
| Kick - Kicks Gifted | Kick | kicks_gifted |
| Kick - Reward Redeemed | Kick | redemption |
| Twitch - Follow | Twitch | follow |
| Twitch - Subscribe | Twitch | subscribe |
| Twitch - Re-sub | Twitch | resub |
| Twitch - Gift Sub | Twitch | giftsub |
| Twitch - Raid | Twitch | raid |
| Twitch - Cheer | Twitch | cheer |
| Twitch - Reward Redeemed | Twitch | redemption |

To assign:

1. Select the sound you want in the **Sound Library** list (this is the "selected sound" all assignments use).
2. On the event's row, click **Assign**. The sound file name appears in the row's Sound File column.
3. To remove an assignment, click **Unassign** on that row.
4. To hear it with that event's volume, click the row's **Test** button.

Assignments save to the database immediately — there is no Save button on this page.

### Per-event volume

Each row has a **Volume** slider from 0% to 100% (steps of 10%). The final loudness of an event sound is this slider times the **Master Volume** from [Audio Settings](audio.md) — e.g. 100% event volume with 50% master plays at half volume — and since the MediaPlayer rework it applies to **every format including `.wav`**, both locally and in the browser source (the broadcast carries the final clamped 0..1 loudness). Slider changes save immediately, like assignments, and the saved value applies to real events as well as the **Test** button.

### What plays where

- **You hear it** when the Streamer Sounds toggle is on — in any supported format (`.wav`, `.mp3`, `.ogg`, `.m4a`).
- **Viewers hear it** through the OBS browser source when the Stream Sounds toggle is on — same formats, volume honored (the broadcast is the camelCase shape the page understands). See [Browser Sources](browser-sources.md).
- If both toggles are off, nothing plays even with assignments configured.

There are no soundboard/hotkey sounds, no command-triggered sounds, and no YouTube/TikTok/Discord event rows in the UI — sounds trigger only from the fourteen events above and from this page's test buttons. (YouTube Super Chats do call the cheer sound key — `PlayEventSoundAsync("cheer", "YouTube")` — but with no YouTube row here there is no way to map it from the UI yet; see [YouTube](../platforms/youtube.md#super-chats--super-stickers).)

## How It Works (Developer)

### Storage: files plus config keys

Uploaded files are copied into the **current profile's sounds directory** — `ProfileService.GetProfileDataPath(profile)/sounds` — chosen at SoundService construction (a profile-less first run keeps the legacy app-global location). A one-time `MigrateLegacySounds` copies app-global files in (no overwrites, originals kept, never re-run once the profile folder has a file). The Kestrel server maps `/sounds` to that directory first, with the static wwwroot provider as the fallback for files left in the legacy path.

Mappings are not an entity; they are 28 Configs rows using two key patterns:

- `EventSound_<Platform>_<eventType>` → the assigned file name (empty string = unassigned)
- `EventVolume_<Platform>_<eventType>` → the slider value

`SoundsViewModel.LoadEventMappingsAsync` builds the fixed fourteen-row table by reading those keys; every Assign/Unassign/slider change writes one key immediately.

### Playback flow, end to end

```
platform event → EventBus → ChatViewModel handler (e.g. OnKickFollowAsync)
  → SoundService.PlayEventSoundAsync(eventType, platform)
      reads AudioSettings blob  → skip if both output toggles off
      reads EventSound_<platform>_<eventType> → skip if unmapped
      volume = MasterVolume/100 × EventVolume_<platform>_<eventType>   (invariant-parse + clamp)
  → PlaySoundFileAsync(soundFile, volume)
      StreamerSoundsEnabled → PlayLocalSoundAsync → PlayWithMediaPlayer (ALL formats, volume applied,
                              one short-lived STA thread + MediaPlayer per playback so sounds overlap)
      StreamSoundsEnabled  → serialize camelCase {"type":"play","sound":...,"volume":0..1}
                             → AudioWebSocketBroadcaster.BroadcastAsync
                             → Kestrel /audio-ws → OBS browser source (see browser-sources.md)
```

The trigger call sites sit inside the same event handlers that produce the chat announcement, the [visual alert](visual-alerts.md), the currency bonus, and the auto-reply — see [Event Reactions](../chat/event-reactions.md). They now include the redemption handlers: Kick `OnKickKicksGiftedAsync` / `OnKickRewardRedeemedAsync` (the `kicks_gifted` / `redemption` rows) and Twitch `OnTwitchChannelPointsRedeemAsync` (the `redemption` row). You can fire any of them on demand from the [Event Testing](../chat-features/event-testing.md) page, which publishes synthetic events onto the real [event bus](../architecture/event-bus.md).

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
| [SoundsViewModel.cs](../../src/VyralBot.UI/ViewModels/SoundsViewModel.cs) | Page logic: library list, upload/delete/preview, the fixed 14-event mapping table, assign/unassign, per-event volume saves |
| [SoundsView.xaml](../../src/VyralBot.UI/Views/SoundsView.xaml) | The two-panel layout: Sound Library (list + Upload/Play/Delete buttons) and the Event Sound Assignments DataGrid |
| [SoundService.cs](../../src/VyralBot.Core/Services/Audio/SoundService.cs) | Per-profile sounds directory + one-time legacy migration, file storage (upload/delete/list), `PlayEventSoundAsync` / `PlaySoundFileAsync` pipeline (MediaPlayer local playback), config-key lookups, default-sound check |
| [ISoundService.cs](../../src/VyralBot.Core/Services/Audio/ISoundService.cs) | Service interface used by the page and by ChatViewModel (incl. `GetSoundsDirectory`) |
| [IAudioWebSocketServer.cs](../../src/VyralBot.Core/Services/Audio/IAudioWebSocketServer.cs) | Defines the `AudioEvent` message class (serialized camelCase on the wire) plus the legacy server interface |
| [AudioWebSocketBroadcaster.cs](../../src/VyralBot.Core/Services/Audio/AudioWebSocketBroadcaster.cs) | Static delegate carrying broadcasts from SoundService to the Kestrel server's WebSocket clients |
| [WebServerService.cs](../../src/VyralBot.Api/Services/WebServerService.cs) | Kestrel server that serves `/sounds/*` from the profile sounds directory (wwwroot fallback) and hosts `/audio-ws` |
| [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) | The fourteen event handlers that actually trigger sounds at runtime |
| [MainViewModel.cs](../../src/VyralBot.UI/ViewModels/MainViewModel.cs) | Calls `InitializeDefaultSoundsAsync()` at profile load |

### The fixed event list

```csharp
// SoundsViewModel.LoadEventMappingsAsync — the mapping table is hard-coded, not extensible
var eventTypes = new[]
{
    ("Kick",   "follow",      "Follow"),
    ("Kick",   "subscribe",   "Subscribe"),
    ("Kick",   "giftsub",     "Gift Sub"),
    ("Kick",   "host",        "Host"),
    ("Kick",   "kicks_gifted","Kicks Gifted"),
    ("Kick",   "redemption",  "Reward Redeemed"),
    ("Twitch", "follow",      "Follow"),
    ("Twitch", "subscribe",   "Subscribe"),
    ("Twitch", "resub",       "Re-sub"),
    ("Twitch", "giftsub",     "Gift Sub"),
    ("Twitch", "raid",        "Raid"),
    ("Twitch", "cheer",       "Cheer"),
    ("Twitch", "redemption",  "Reward Redeemed")
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

The full key set is those two patterns across the fourteen `<Platform>_<eventType>` combinations in the mapping table above (28 keys total). Related settings owned elsewhere: the `AudioSettings` blob ([Audio Settings](audio.md)) and `WebServerSettings` ([Web Server & ngrok](../settings/web-server-ngrok.md)). Sound files themselves live on disk in the current profile's `sounds` folder (legacy app-global location served as fallback), not in the database.

## Status & Known Issues

Every item below is verified against the current code. See the [known issues ledger](../reference/known-issues.md) for app-wide context.

1. **No sounds are bundled** — a fresh install has an empty library, and `InitializeDefaultSoundsAsync` only logs warnings for missing conventional files (SoundService.cs). Nothing plays until you upload and assign. [known issues ledger](../reference/known-issues.md)
2. **Viewer-side playback works** — the broadcast is camelCase `{"type":"play","sound":...,"volume":0..1}` and the page honors the volume field. [known issues ledger](../reference/known-issues.md) (KI-4 resolved)
3. **Local playback honors volume for every format** — fixed 2026-11-07: all formats (wav included) play through WPF `MediaPlayer` per playback on its own STA thread; the old `System.Media.SoundPlayer` wav path (which ignored volume and left non-wav files silent locally) is gone.
4. **Per-event volume applies to real events** — `GetEventVolumeAsync` parses the saved invariant string with `double.TryParse` (normalizing legacy whole-percent rows and clamping to 0..1), so the slider is honored everywhere, not just Test.
5. **Only Kick and Twitch rows exist** — no YouTube/TikTok/Discord rows (YouTube Super Chats call the cheer key programmatically but it is not mappable from this page), and no soundboard, hotkeys, or command-triggered sounds anywhere. [known issues ledger](../reference/known-issues.md)
6. **Delete clears assignments but not volumes** — deleting a file nulls out `EventSound_` keys that referenced it (SoundsViewModel.cs:191-197), while the orphaned `EventVolume_` keys are left behind (harmless, but they persist). [known issues ledger](../reference/known-issues.md)
7. **Sound triggers depend on the wider event pipeline** — Twitch events arrive once EventSub is connected (it is, after the 2026-08-15 auth fix — see [Twitch](../platforms/twitch.md)). [known issues ledger](../reference/known-issues.md)
