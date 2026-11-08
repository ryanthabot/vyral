# SoundsViewModel + SoundsView

Sounds Management page (`Views/SoundsView.xaml`): sound library (per-profile
files) plus per-platform event-sound mappings. Backed by
`ISoundService` (`VyralBot.Core/Services/Audio/SoundService.cs`) and
`IDataService` config keys.

## Persistence keys

- `EventSound_{Platform}_{EventType}` — assigned sound file name (empty = none).
- `EventVolume_{Platform}_{EventType}` — per-event volume, invariant `"F2"`
  string in 0..1 ("0.00".."1.00"). Legacy whole-percent rows ("70") are
  normalized on read (both VM and `SoundService.GetEventVolumeAsync`).
- `AudioSettings` — shared JSON blob (also written by the Audio Settings page):
  master volume, streamer/stream sound toggles, web port. The Sounds page
  read-modify-writes it so it never clobbers the other fields.
- `Sounds.Hotkeys` (category `Sounds`) — JSON map
  `{ "sound file name": "Ctrl+Alt+1" }` of global soundboard hotkeys (KI-55),
  owned by `GlobalHotkeyService` (`docs/services/global-hotkey-service.md`).

## Master volume

`MasterVolumePercent` (0-100 slider in the page header) is the global
multiplier: live playback computes `master * per-event volume`
(`SoundService.PlayEventSoundAsync`), and every preview on this page
(Play Selected, per-file play, event Test) multiplies by it so previews match
live loudness. Persist is debounced 600ms after the drag settles. During
`InitializeAsync` the loaded value is assigned with `_suppressMasterVolumePersist`
so reading storage doesn't schedule a write-back.

## Per-event rows (DataGrid)

Every mapping row has a 0-100% volume slider. `UpdateEventVolumeCommand`
debounces the persist (400ms, latest value wins via `CancellationTokenSource`).
Sound-file assignment uses the selected library item (Assign/Clear/Test).

## Sound library

- Files live in the profile sounds dir (`ISoundService.GetSoundsDirectory()`);
  supported: .wav, .mp3, .ogg, .m4a.
- Per-row play button previews at master volume (`PlayFileCommand`).
- Per-row **Hotkey** column (KI-55): "Set Hotkey" starts click-to-capture; the
  next modifier+key press registers the combo immediately via
  `GlobalHotkeyService.SetSoundHotkeyAsync` (conflicts with another sound and
  combos claimed by other applications are rejected with a message; Esc
  cancels; modifier-only or bare keys keep the capture alive with a hint).
  The chip shows the assigned gesture ("Ctrl+Alt+1"); a "✕" button clears it.
  Key capture tunnels through the page's `PreviewKeyDown` (Alt combos arrive
  as `Key.System` and are resolved to `SystemKey`). Deleting a sound releases
  its hotkey slot. The header documents the constraints: hotkeys are OS-global
  (fire in-game), avoid game/Windows combos, max 10 slots.
- Duration is shown for .wav only — parsed from the RIFF header
  (`TryGetWavDuration`: fmt byte-rate + data chunk size) on a background
  thread. Other formats would need a decoder; they show no duration.
- Drag-drop import: `AllowDrop` on the ListBox + `DragOverSoundFilesCommand`
  (copy cursor for file drops) + `DropSoundFilesCommand` (copies each file via
  `UploadSoundAsync`, which dedupes names). The empty-state overlay is
  `IsHitTestVisible=False` so drops pass through to the ListBox.
- Upload button supports multiselect; Delete confirms and clears every
  `EventSound_` mapping that referenced the file.

## Browser source

`VyralBot.Api/wwwroot/audio.html` (dependency-free single file) connects to
`/audio-ws`, shows a Connecting/Connected/Disconnected badge (only the
Connected badge auto-hides), reconnects after 3s, and plays
`{type:'play',sound,volume}` messages where `volume` is already the final
loudness (master x per-event) — see `docs/services/embedded-web-server.md`.

## Song Requests card

Collapsible card at the bottom of the page (collapsed by default) for the
chat-driven song queue (`!sr` family — `docs/features/song-requests.md`).
Backed by `ISongRequestService` + `IDataService` song-request rows; a 3s
`DispatcherTimer` refreshes the list while the page is open so player-side
advances (`POST /songs/played` from `songplayer.html`) show up without
cross-thread events. Rows show title (bare video id until the player reports
the real one), requester, platform, duration when known; the Playing row gets
a NOW PLAYING highlight + primary border. Right panel: OBS browser-source URL
(`http://localhost:<port>/songplayer.html?volume=N`, built from the persisted
web-server port) with Copy button, volume box (0-100, feeds the URL), and
max-per-user / max-queue-total boxes. Persistence keys (debounced 600ms):

- `Songs.MaxPerUser` (default 2) and `Songs.MaxQueue` (default 30) — the caps
  the `!sr` command family enforces, stored as plain invariant ints.
- `Songs.PlayerVolume` (default 50) — only feeds the browser-source URL.

Buttons: Skip (`SkipCurrentAsync`; the player picks the next song up within
5s) and Clear Queue (`ClearAsync`, with confirmation). Limits are clamped
(1-50 per user, 1-200 total, 0-100 volume) on change.

## Disposal

`IDisposable` stops the song-queue timer, cancels the three debounce CTSs
(event volume, master volume, song settings) and aborts any in-progress
hotkey capture; live hotkey registrations are owned by the
`GlobalHotkeyService` singleton and keep firing. `MainViewModel` scope
disposal invokes it on page switch.
