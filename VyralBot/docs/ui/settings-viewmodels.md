# Settings Sub-ViewModels

## Overview
ViewModels for specific settings areas: web server, audio, sounds, alerts, variables, and per-platform chat settings.

## Web Server Settings
### WebServerSettingsViewModel
- **File:** `src/VyralBot.UI/ViewModels/WebServerSettingsViewModel.cs` (~502 lines)
- **Class:** `WebServerSettingsViewModel : ViewModelBase`
- **Constructor:** `ILogger`, `IDataService`, `WebhookCoordinatorService?`, `NgrokService?`
- **Observable properties:** `_isEnabled`, `_port`, `_ngrokAuthToken`
- **View:** `src/VyralBot.UI/Views/WebServerSettingsView.xaml(.cs)`

## Audio Settings
### AudioSettingsViewModel
- **File:** `src/VyralBot.UI/ViewModels/AudioSettingsViewModel.cs` (~206 lines)
- **Class:** `AudioSettingsViewModel : ViewModelBase`
- **Constructor:** `ILogger`, `IDataService`, `ISoundService`
- **Observable properties:** `_streamerSoundsEnabled`, `_streamSoundsEnabled`, `_masterVolume`, `_webSocketPort`, `_connectedClients`
- **View:** `src/VyralBot.UI/Views/AudioSettingsView.xaml(.cs)`

## Sounds Management
### SoundsViewModel
- **File:** `src/VyralBot.UI/ViewModels/SoundsViewModel.cs` (~363 lines)
- **Class:** `SoundsViewModel : ViewModelBase`
- **Constructor:** `ILogger`, `ISoundService`, `IDataService`
- **Observable properties:** `_soundFiles`, `_selectedSoundFile`, `_eventMappings`
- **View:** `src/VyralBot.UI/Views/SoundsView.xaml(.cs)`
- **Event mapping rows** (persisted as `EventSound_{platform}_{eventType}` /
  `EventVolume_{platform}_{eventType}`, volumes as invariant "F2" 0..1 strings):
  Kick `follow`/`subscribe`/`giftsub`/`host`/`kicks_gifted`/`redemption`,
  Twitch `follow`/`subscribe`/`resub`/`giftsub`/`raid`/`cheer`/`redemption`,
  YouTube `cheer` ("Super Chat"; keyed "cheer" because ChatViewModel's
  `OnYouTubeSuperChatAsync` plays `PlayEventSoundAsync("cheer", "YouTube")` —
  the workflow trigger key is the separate "superchat") and `membership`, and
  Discord `follow` ("Member Joined") / `subscribe` ("Server Boost"; played by
  `OnDiscordMemberJoinedAsync`/`OnDiscordBoostAsync`). The Kick
  `kicks_gifted`/`redemption` rows play via `ChatViewModel`'s
  `OnKickKicksGiftedAsync`/`OnKickRewardRedeemedAsync` handlers.
- **Storage:** sound files live in the active profile's data folder
  (`appdata/profiles/<name>/data/sounds`, resolved by `SoundService` via
  `IProfileService`). On first run (before a profile exists) the app-global
  `<exe>/wwwroot/sounds` folder is used, and existing global sounds are copied
  into the profile folder once (never overwritten or deleted at the old path).
  Browser sources keep fetching `/sounds/<file>` from the embedded web server,
  which serves the profile folder first and `wwwroot` as fallback.
- **Playback:** all formats (`.wav` included) play locally through WPF
  `MediaPlayer` on a short-lived STA thread so master/per-event volume is always
  honored and simultaneous sounds overlap; the browser-source push
  (`audio-ws`) carries the same final volume.

## Alert Settings
### AlertSettingsViewModel
- **File:** `src/VyralBot.UI/ViewModels/AlertSettingsViewModel.cs` (~159 lines)
- **Class:** `AlertSettingsViewModel : ViewModelBase`
- **Constructor:** `ILogger`, `IAlertService`
- **Observable properties:** `_alertsEnabled`, `_alertDuration`, `_selectedAnimation`, `_selectedPosition`
- **View:** `src/VyralBot.UI/Views/AlertSettingsView.xaml(.cs)`

## Variables Management
### VariablesViewModel
- **File:** `src/VyralBot.UI/ViewModels/VariablesViewModel.cs` (~350 lines)
- **Class:** `VariablesViewModel : ViewModelBase`
- **Constructor:** `ILogger`, `IDataService`, `IVariableReplacementService`
- **Observable properties:** `_variables`, `_categories`
- **View:** `src/VyralBot.UI/Views/VariablesView.xaml(.cs)`

### VariablePickerViewModel
- **File:** `src/VyralBot.UI/ViewModels/VariablePickerViewModel.cs` (~198 lines)
- **Class:** `VariablePickerViewModel : ObservableObject` (extends ObservableObject directly)
- **Constructor:** `ILogger`, `IVariableReplacementService`
- **Observable properties:** `_variables`, `_filteredVariables`, `_categories`, `_selectedCategory`
- **Control:** `src/VyralBot.UI/Controls/VariablePicker.xaml(.cs)`

## Per-Platform Chat Settings ViewModels
| ViewModel | File | Lines | Constructor |
|-----------|------|-------|-------------|
| `TwitchChatSettingsViewModel` | `TwitchChatSettingsViewModel.cs` | ~224 | `ILogger`, `IChatEventSettingsService` |
| `KickChatSettingsViewModel` | `KickChatSettingsViewModel.cs` | ~384 | `ILogger`, `IChatEventSettingsService` |
| `YoutubeChatSettingsViewModel` | `YoutubeChatSettingsViewModel.cs` | ~237 | `ILogger`, `IDataService`, `IEventBus` |
| `TiktokChatSettingsViewModel` | `TiktokChatSettingsViewModel.cs` | ~243 | `ILogger`, `IDataService`, `IEventBus` |
| `DiscordChatSettingsViewModel` | `DiscordChatSettingsViewModel.cs` | ~176 | `ILogger`, `IDataService`, `IEventBus` |

All have properties: `_isChatEnabled`, `_autoStartChat`, `_autoRepliesEnabled`, `_status`
Twitch and Kick additionally have: `_showFollowsInChat`
YouTube + TikTok additionally have: `_showBadges`, `_showTimestamps`, `_compactMode`
(bound by `Views/YoutubeChatSettingsView.xaml` / `Views/TiktokChatSettingsView.xaml`)

Auto-replies master toggle persistence (`IDataService` config keys, category null):
`Chat.Twitch.AutoRepliesEnabled`, `Kick.AutoRepliesEnabled` (shared with
`KickSettingsViewModel`), `Chat.YouTube.AutoRepliesEnabled`,
`Chat.TikTok.AutoRepliesEnabled`, `Chat.Discord.AutoRepliesEnabled` — default
true; saved on toggle and via SaveSettings. YouTube/TikTok display prefs persist
under `Chat.{YouTube|TikTok}.{ShowBadges|ShowTimestamps|CompactMode}`
(defaults: badges on, timestamps/compact off).

## Chat Settings Views
| View File |
|-----------|
| `src/VyralBot.UI/Views/TwitchChatSettingsView.xaml(.cs)` |
| `src/VyralBot.UI/Views/KickChatSettingsView.xaml(.cs)` |
| `src/VyralBot.UI/Views/YoutubeChatSettingsView.xaml(.cs)` |
| `src/VyralBot.UI/Views/TiktokChatSettingsView.xaml(.cs)` |
| `src/VyralBot.UI/Views/DiscordChatSettingsView.xaml(.cs)` |
