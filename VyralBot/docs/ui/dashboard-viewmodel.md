# Dashboard ViewModel

## Overview
ViewModel for the Home page: high-level bot status, the first-run
"Get started" strip state (dismiss persists app-wide via ui-settings.json;
platform-connection visibility is bound in XAML to MainViewModel flags) and
the Quick Actions bar (one-click streamer power tools).

## Source File
- **File:** `src/VyralBot.UI/ViewModels/DashboardViewModel.cs`
- **Namespace:** `VyralBot.UI.ViewModels`
- **Class:** `DashboardViewModel : ViewModelBase`
- **Constructor:** `DashboardViewModel(UiPreferencesService, ITwitchService?,
  ObsOutputService?, ObsEngine?, RestreamEngine?, RestreamFfmpegLocator?,
  IDataService?, IProfileService?, ITtsService?, ILogger<DashboardViewModel>?)`
  — loads `HomeGettingStartedDismissed` at startup. All quick-action service
  deps are optional (nullable): a null registration disables the matching
  button instead of breaking Home.

## Observable Properties
- `int ActiveTriggerCount`
- `int TotalActionCount`
- `int ConnectedPlatformCount`
- `string Status`
- `bool IsGettingStartedDismissed` — Home strip dismiss state
- Quick-action availability (service presence): `CanQuickTwitch`,
  `CanQuickReplay`, `CanQuickRestream`, `CanQuickTts`
- Quick-action live state: `IsObsEngineReady` (ObsEngine.StatusChanged),
  `IsRestreamRunning` (RestreamEngine.StatusChanged), `IsTtsEnabled`
  (persisted `Tts.Enabled`), label swaps `RestreamActionLabel` /
  `TtsActionLabel`, result feedback `QuickActionStatus` /
  `IsQuickActionStatusVisible` / `IsQuickActionStatusError` (2 s auto-hide)

## Commands
- `DismissGettingStartedCommand` — hides the strip and persists
  `HomeGettingStartedDismissed=true` through `UiPreferencesService`
  (loads fresh before saving so theme prefs are never clobbered).
- `ClipCommand` — `ITwitchService.CreateClipUrlAsync()`; copies the URL to
  the clipboard (falls back to showing it if the clipboard is locked).
- `SaveReplayCommand` — `ObsOutputService.SaveReplayAsync()`; requires the
  embedded engine Ready, surfaces the service's user-facing message.
- `StartAdBreakCommand` — `ITwitchService.StartCommercialAsync(30)`.
- `ToggleRestreamCommand` — stop goes straight to `RestreamEngine.StopAsync`;
  start rebuilds the saved session (FFmpeg via `RestreamFfmpegLocator`,
  destinations from `restream_destinations.json` in the profile credentials
  folder, source settings from the `Restream.Settings` config row — same
  artifacts the Restream page persists) and calls `RestreamEngine.StartAsync`.
- `ToggleTtsMuteCommand` — flips persisted `Tts.Enabled` via
  `ITtsService.Get/SaveSettingsAsync`; muting also cancels current speech.

## Notes
- The engine event subscriptions (`RestreamEngine.StatusChanged`,
  `ObsEngine.StatusChanged`) use named handlers marshalled to the captured UI
  dispatcher. The VM is an app-lifetime singleton that is never disposed by
  MainViewModel, so the subscriptions intentionally live as long as the
  engine singletons — no orphaned-handler buildup across navigations.
- Restream start never logs destination stream keys (secrets stay in the
  credentials file; failures log at info level only).

## All Files
| File | Description |
|------|-------------|
| `src/VyralBot.UI/ViewModels/DashboardViewModel.cs` | Dashboard ViewModel |
| `src/VyralBot.UI/Services/UiPreferencesService.cs` | Persists ui-settings.json |
| `src/VyralBot.Integrations/Twitch/ITwitchService.cs` | Clip + commercial seams |
| `src/VyralBot.Integrations/Obs/Engine/ObsOutputService.cs` | Replay-buffer save |
| `src/VyralBot.Integrations/Restream/RestreamEngine.cs` | Restream session engine |
