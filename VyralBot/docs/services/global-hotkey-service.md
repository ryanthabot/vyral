# GlobalHotkeyService

`VyralBot.UI/Services/Hotkey/GlobalHotkeyService.cs` — global soundboard
hotkeys (KI-55): Win32 `RegisterHotKey`/`UnregisterHotKey` slots bound to the
main window's HWND, so streamers can fire sounds while a game has focus.

Singleton that **outlives the Sounds page VM** (RestreamEngine/DeckServer
precedent): the registration table, WM_HOTKEY dispatch and playback all live in
the service; the Sounds page only edits assignments.

## Lifecycle

| Step | Where |
|---|---|
| DI | `App.xaml.cs` → `services.AddSingleton<UI.Services.Hotkey.GlobalHotkeyService>()` |
| Attach | `MainWindow.OnSourceInitialized` → `AttachToWindow(this)` (HWND exists there; window is created once per run and never recreated, so the HWND is stable) |
| WM_HOTKEY | `MainWindow.WndProc` forwards `wParam` (slot id) to `HandleWmHotkey` |
| Restore | `MainViewModel.OnProfileLoadedAsync` → `RestoreSavedHotkeysAsync()` (app start + every profile switch; previous profile's slots are unregistered first, entries whose sound file is missing are skipped with an info log) |
| Shutdown | `App.OnExit` → `Dispose()` unregisters every slot (idempotent; `Window.Closed` is a backstop) |

`RegisterHotKey` is owned by the thread that calls it, so all register calls
run on the attached window's UI thread — public APIs marshal themselves via the
captured `Dispatcher` (`InvokeAsync`, never blocking). The table (`id → entry`,
plus gesture/sound reverse indexes) is guarded by a lock; ids are app-private,
capped at `MaxSlots = 10`.

## Persistence

Config key `Sounds.Hotkeys` (category `Sounds`) via `IDataService`, JSON map
`{ "sound file name": "Ctrl+Alt+1" }`. Gestures are stored normalized; on read
an unparsable map starts empty (info log).

## Playback

A fired hotkey plays through `ISoundService.PlaySoundFileAsync(soundName,
master/100f)` — the same master-volume path as the Sounds page test-play
button: the shared `AudioSettings` blob's `MasterVolume` is read at fire time,
identical to `SoundService.PlayEventSoundAsync`'s live math. Playback is
fire-and-forget so the message pump never blocks.

## Gestures

`HotkeyGesture` (pure, unit-testable): parse/format round-trips for
"Ctrl+Alt+1"-style strings. Accepts `Ctrl`/`Control`, `Win`/`Windows`/`Meta`,
`D1`/`Numpad3` aliases, any casing/order; requires ≥1 modifier + exactly one
supported key (0-9, A-Z, F1-F12, Num0-9, Space, navigation keys). Canonical
display order: `Ctrl+Alt+Shift+Win+Key`. `ToRegisterModifiers` emits the
`MOD_*` bits plus `MOD_NOREPEAT`.

`HotkeyConflict.Resolve` (pure) checks a candidate against the persisted map
by parsed gesture value, distinguishing same-sound reassignment from another
sound's combo.

`FromWpfKey` converts a WPF capture key press; callers must resolve
`Key.System` → `SystemKey` first (WPF reports Alt-held keys as System).

## Assignment outcomes (`SoundHotkeyResult`)

`Registered`, `ReplacedExisting` (same sound re-keyed: old slot freed first),
`InvalidGesture`, `ConflictOtherSound` (rejected with the owning sound's name),
`OsRejected` (combo claimed by another app — info log), `LimitReached`
(10 slots), `NoWindowAttached`.

## UI

The Sounds page library rows have a Hotkey column (see
`docs/ui/sounds-view.md`): click-to-capture chip + clear button; the header
documents that hotkeys are OS-global and should avoid game/Windows combos.
