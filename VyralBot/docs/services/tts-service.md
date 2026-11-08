# TextToSpeechService (TTS)

`VyralBot.Core/Services/Audio/TextToSpeechService.cs` — text-to-speech through
the in-box Windows SAPI voices (`System.Speech` NuGet, `SpeechSynthesizer`).
Interface: `ITtsService` (same folder). Registered as a singleton
(`App.xaml.cs`, next to the audio services). Consumers: the `!tts` chat command
(ChatViewModel) and spoken event alerts (`UI/Services/AlertService.cs`).

## Threading model

SAPI `Speak()` is a BLOCKING call, so all speech flows through a
**single-consumer bounded channel** (capacity 10, `DropOldest`, `SingleReader`)
consumed by an `await foreach` worker loop (same pattern as `EventBus`):

- At most one utterance plays at a time; concurrent `SpeakAsync` callers only
  enqueue (queue full → oldest dropped, logged at info).
- The `SpeechSynthesizer` is created lazily on the worker loop and confined to
  it; if SAPI is unavailable (no voices installed) the engine is marked
  unavailable and requests are skipped without retry spam.
- `StopSpeaking()` drains the queue and calls `SpeechSynthesizer.Stop()` on the
  in-flight prompt. `Dispose()` (app shutdown) does the same and completes the
  channel; nothing is awaited/blocking on shutdown.
- No `.Result`/`.Wait()`, no unbounded thread spawn.

## Settings (IDataService Configs keys, category `Tts`)

| Key | Type | Default | Notes |
|-----|------|---------|-------|
| `Tts.Enabled` | bool | `false` (opt-in) | master switch; `SpeakAsync` no-ops when off |
| `Tts.Voice` | string | first installed voice | unknown names fall back to the OS default at speak time |
| `Tts.RatePercent` | int | `0` | clamped -50..50, mapped to SAPI rate -10..10 (`/5`) |
| `Tts.VolumePercent` | int | `100` | clamped 0..100 |
| `Tts.AllowViewers` | bool | `true` | when false only mods may use `!tts` |
| `Tts.MaxLength` | int | `300` | clamped 10..1000, truncates `!tts` text |

`Notifications.DoNotDisturb` (Settings → Notifications, same key
SoundService/AlertService read) gates TTS as well — checked per utterance, so
the toggle is live.

## `!tts` command (`ProcessCommandAsync`)

`!tts <text...>` handled for every platform via the exact contract signature
`(username, platform, isModerator, message)`:

- Not `!tts` (or `!ttsxyz`) → null. No text → `"Usage: !tts <message>"`.
- Disabled, viewers-not-allowed (non-mod), or DND → null (silent).
- Per-user 30s cooldown; **mods bypass**; limited users get a
  `⏳ {username}, you can use !tts again in {n}s.` reply.
- Text truncated to `Tts.MaxLength`, spoken with the configured voice, and
  acknowledged with `🔊 {username} says: {text}`.

## Spoken alerts (AlertService hook)

`AlertSettings.TtsAlertsEnabled` (Alert Settings page) + `Tts.Enabled` must
both be on. After an alert popup actually renders (direct or from the FIFO
queue), `AlertService` fire-and-forgets `SpeakAsync("{title}, {username}")`
with trailing `!` stripped — e.g. `"New Follower!, bob"` → `"New Follower,
bob"`. Test previews speak too, subject to the same gates.
