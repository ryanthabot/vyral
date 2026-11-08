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

## Disposal

`IDisposable` cancels the two debounce CTSs; `MainViewModel` scope disposal
invokes it on page switch.
