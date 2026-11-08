# VyralBot Documentation

## Overview
VyralBot is a WPF streaming bot application supporting Twitch, Kick, YouTube, Discord and TikTok (TikTok is web-setup only — coming soon) with multi-profile support, custom triggers/actions, workflows, currency, commands, giveaways, polls, watchtime tracking, auto-moderation, embedded OBS, restreaming and a mobile StreamDeck companion.

## Tech Stack
- **Framework:** WPF (.NET 10), CommunityToolkit.Mvvm (MVVM pattern)
- **DI/Hosting:** Microsoft.Extensions.Hosting, Serilog logging
- **Database:** SQLite via Dapper (per-profile databases, DPAPI-encrypted secrets at rest)
- **Twitch:** Custom LightweightTwitchIrc (WebSocket IRC), TwitchLib.EventSub.Websockets, Helix moderation APIs
- **Kick:** KickLib NuGet package (WebSocket chat + REST API)
- **EventBus:** System.Threading.Channels (bounded, throttled)
- **Web Server:** Kestrel (port 5000) for webhooks and API
- **Storage:** `appdata` next to the exe when writable, else `%LOCALAPPDATA%\VyralBot` (never scattered)

## Project Structure
```
src/
  VyralBot.UI/          → WPF app, ViewModels, Views, Converters, Services
  VyralBot.Core/        → EventBus, services (Chat, Commands, Currency, Triggers, etc.)
  VyralBot.Data/        → IDataService, DapperDataService, Entities
  VyralBot.Integrations/→ Twitch, Kick, YouTube, Discord, OBS, Restream, lights integrations
  VyralBot.Api/         → ASP.NET controllers (webhooks, API)
  VyralBot.Plugins/     → Plugin system
  VyralBot.Scripting/   → Scripting support
```

## ViewModels (40 files)
| ViewModel | Lines | Description |
|-----------|-------|-------------|
| `MainViewModel` | 1711 | Navigation, scoped VM management, profile orchestration |
| `ChatViewModel` | 2562 | Multi-platform chat (17+ deps) |
| `PlatformsViewModel` | 1126 | Platform connection management |
| `KickWebSetupViewModel` | 975 | Kick OAuth setup wizard |
| `TwitchWebSetupViewModel` | 753 | Twitch OAuth setup wizard |
| `BaseOAuthSetupViewModel` | 639 | Abstract OAuth base with WebView2 |
| `SettingsViewModel` | 604 | Profile management and settings |
| `WebServerSettingsViewModel` | 502 | Web server configuration |
| `KickSettingsViewModel` | 422 | Kick platform settings |
| `GiveawayViewModel` | 416 | Giveaway management |
| `NgrokSetupWizardViewModel` | 416 | Ngrok OAuth wizard |
| `EventTestingViewModel` | 171 | Event testing UI (17 test fires via `TestEventPublisher`) |
| `PollViewModel` | 399 | Poll management |
| `CurrencyViewModel` | 367 | Currency system UI |
| `ProfileSelectorViewModel` | 363 | Profile selection (manual INPC) |
| `SoundsViewModel` | 363 | Sound file management |
| `VariablesViewModel` | 350 | Variable management |
| `EventHistoryViewModel` | 462 | Event log viewer, CSV export (`ui/event-history-viewmodel.md`) |
| `ConditionalRulesViewModel` | 302 | Conditional rule builder |
| `CommandsViewModel` | 300 | Command management |
| `KickChatSettingsViewModel` | 242 | Kick chat settings |
| `BaseWebSetupViewModel` | 228 | Simplified web setup base |
| `TwitchChatSettingsViewModel` | 224 | Twitch chat settings |
| `StatisticsViewModel` | 330 | Statistics dashboard, aggregated CSV export (`ui/statistics-viewmodel.md`) |
| `AudioSettingsViewModel` | 206 | Audio configuration |
| `VariablePickerViewModel` | 198 | Variable picker control |
| `WatchtimeViewModel` | 196 | Watchtime viewer |
| `KickDashboardViewModel` | 181 | Kick dashboard |
| `TwitchDashboardViewModel` | 172 | Twitch dashboard |
| `AlertSettingsViewModel` | 159 | Alert configuration |
| `GoogleDemoSetupViewModel` | 113 | Google demo setup |
| `DiscordChatSettingsViewModel` | 93 | Discord chat settings |
| `YoutubeChatSettingsViewModel` | 93 | YouTube chat settings |
| `TiktokChatSettingsViewModel` | 93 | TikTok chat settings |
| `DashboardViewModel` | 35 | Dashboard status |
| `TwitchSettingsViewModel` | 28 | Twitch settings (minimal) |
| `ViewModelBase` | 12 | Abstract base (ObservableObject) |

## Core Services
| Service | Location | Description |
|---------|----------|-------------|
| EventBus | `VyralBot.Core/Services/EventBus/` | Channel-based pub/sub (capacity 500, 4 concurrent) |
| TriggerEngine | `VyralBot.Core/Services/TriggerEngine/` | Event-driven trigger matching and action dispatch |
| ActionDispatcher | `VyralBot.Core/Services/ActionDispatcher/` | Action execution with timeout/cancellation |
| Chat | `VyralBot.Core/Services/Chat/` | Message store, auto-reply, event settings |
| Commands | `VyralBot.Core/Services/Commands/` | Command registration, execution, cooldowns |
| Currency | `VyralBot.Core/Services/Currency/` | Points, rewards, redemptions |
| Profiles | `VyralBot.Core/Services/Profiles/` | Profile management, PIN security |
| Variables | `VyralBot.Core/Services/Variables/` | Variable replacement with nested support |
| Statistics | `VyralBot.Core/Services/Statistics/` | Event statistics and export |
| Rules | `VyralBot.Core/Services/Rules/` | Condition evaluation engine |
| Audio | `VyralBot.Core/Services/Audio/` | Sound playback via WebSocket |
| Performance | `VyralBot.Core/Services/Performance/` | Metrics monitoring, object pooling |
| WebServer | `VyralBot.Core/Services/WebServer/` | Dependency tracking for web server |
| Giveaway/Poll/Watchtime | `VyralBot.Core/Services/` | Standalone feature services |

## Data Layer
| File | Description |
|------|-------------|
| `VyralBot.Data/IDataService.cs` | 80+ methods, 17 categories |
| `VyralBot.Data/DapperDataService.cs` | SQLite/Dapper implementation (2749 lines) |
| `VyralBot.Data/Entities/` | 23 entity files (Guid IDs) |

## Subsystem Map

Where the major subsystems live and which doc covers them:

| Subsystem | Entry points | Doc |
|---|---|---|
| Quotes | `Core/Services/Quotes/QuoteService`, `!quote` command family, Commands page QUOTES tab | [features/quotes.md](features/quotes.md) |
| Song requests | `Core/Services/Songs/SongRequestService`, `!sr` command family, queue lifecycle | [features/song-requests.md](features/song-requests.md) |
| Restream | `Integrations/Restream/` (engine, builder, validation, bandwidth, FFmpeg locator/acquisition), Restream page | [features/restream.md](features/restream.md) |
| Control API | `Api` Bearer-token `/api` surface (`/api/status`, `/api/events/test`) on the embedded web server | [services/embedded-web-server.md](services/embedded-web-server.md) |
| Event testing | `Integrations/Testing/TestEventPublisher`, Event Testing page | [services/test-event-publisher-service.md](services/test-event-publisher-service.md), [ui/event-testing-viewmodel.md](ui/event-testing-viewmodel.md) |
| Welcome + auto-role | `DiscordBotManager` member-join flow, per-bot `WelcomeSettings` | [services/discord-bot-runtime.md](services/discord-bot-runtime.md) |
| Discord dashboard tab | `DiscordDashboardView`/`ViewModel` (fleet KPIs, mod actions, workflow runs) | [ui/discord-dashboard.md](ui/discord-dashboard.md), [ui/discord-bot-detail.md](ui/discord-bot-detail.md) |
| VyralDeck / StreamDeck | `UI/Services/VyralDeck/` (server, pairing, executor), `mobile/vyral_deck/` | [features/vyraldeck.md](features/vyraldeck.md) |
| Crash pipeline | `App` global handlers, `CrashReport`, crash dialog | [services/crash-pipeline.md](services/crash-pipeline.md) |
| OBS remote control | `Integrations/Obs/WebSocket/ObsWebSocketClient` | [services/obs-websocket-client.md](services/obs-websocket-client.md) |

## Documentation Index

### Architecture
| Doc | Description |
|-----|-------------|
| [architecture/mvvm-pattern.md](architecture/mvvm-pattern.md) | CommunityToolkit.Mvvm patterns |
| [architecture/viewmodel-base.md](architecture/viewmodel-base.md) | ViewModelBase class |
| [architecture/event-bus.md](architecture/event-bus.md) | Channel-based EventBus |
| [architecture/data-layer.md](architecture/data-layer.md) | Dapper/SQLite data access |
| [architecture/dependency-injection.md](architecture/dependency-injection.md) | DI configuration |
| [architecture/deprecated-components.md](architecture/deprecated-components.md) | Removed components |

### Features
| Doc | Description |
|-----|-------------|
| [features/chat-system.md](features/chat-system.md) | Multi-platform chat |
| [features/event-system.md](features/event-system.md) | Event types and flow |
| [features/kick-integration.md](features/kick-integration.md) | KickLib integration |
| [features/twitch-integration.md](features/twitch-integration.md) | Twitch IRC + EventSub + Helix moderation |
| [features/youtube-integration.md](features/youtube-integration.md) | YouTube chat + SuperChat |
| [features/platform-management.md](features/platform-management.md) | Platform config |
| [features/profile-system.md](features/profile-system.md) | Profile management |
| [features/settings-system.md](features/settings-system.md) | Settings and config |
| [features/flow-editor-canvas.md](features/flow-editor-canvas.md) | Visual flow editor canvas |
| [features/workflow-templates.md](features/workflow-templates.md) | Starter workflow templates (Automation page one-click import) |
| [features/marketplace-sharecodes.md](features/marketplace-sharecodes.md) | Command marketplace + sharecode import |
| [features/quotes.md](features/quotes.md) | Quote database + `!quote` command family |
| [features/song-requests.md](features/song-requests.md) | Chat-driven song request queue + `!sr` command family |
| [features/giveaways-and-polls.md](features/giveaways-and-polls.md) | Weighted-raffle giveaways + chat polls (race-hardened draws/votes) |
| [features/restream.md](features/restream.md) | Multi-platform FFmpeg restream (ingest, fan-out, verified FFmpeg acquisition, uplink hint) |
| [features/vyraldeck.md](features/vyraldeck.md) | VyralDeck mobile companion (LAN server, PIN pairing, 22 deck actions) |

### Services
| Doc | Description |
|-----|-------------|
| [services/action-dispatcher.md](services/action-dispatcher.md) | Action execution |
| [services/workflow-runtime.md](services/workflow-runtime.md) | Workflow graph interpreter + run cancellation |
| [services/trigger-engine.md](services/trigger-engine.md) | Trigger system |
| [services/data-service.md](services/data-service.md) | Data service overview |
| [services/data-services-detailed.md](services/data-services-detailed.md) | Detailed data reference |
| [services/interfaces.md](services/interfaces.md) | All service interfaces |
| [services/kick-service.md](services/kick-service.md) | KickLibChatService |
| [services/kick-oauth-service.md](services/kick-oauth-service.md) | KickLibOAuthService |
| [services/twitch-service.md](services/twitch-service.md) | TwitchService |
| [services/lightweight-twitch-irc.md](services/lightweight-twitch-irc.md) | LightweightTwitchIrc |
| [services/moderation.md](services/moderation.md) | Auto-moderation + mod commands + platform parity |
| [services/discord-bot-runtime.md](services/discord-bot-runtime.md) | Discord gateway + multi-bot manager |
| [services/embedded-web-server.md](services/embedded-web-server.md) | Embedded Kestrel web server (webhooks + Bearer control API) |
| [services/crash-pipeline.md](services/crash-pipeline.md) | Global exception handling, secret scrubbing, crash dialog |
| [services/obs-websocket-client.md](services/obs-websocket-client.md) | obs-websocket 5 remote-control client |
| [services/test-event-publisher-service.md](services/test-event-publisher-service.md) | TestEventPublisher (canned events for testing + control API) |
| [services/performance-monitor.md](services/performance-monitor.md) | Performance + pooling |
| [services/profile-service.md](services/profile-service.md) | ProfileService |
| [services/process-management.md](services/process-management.md) | Process management |
| [services/tts-service.md](services/tts-service.md) | TextToSpeechService (!tts + spoken alerts, SAPI) |

### Models
| Doc | Description |
|-----|-------------|
| [models/entities.md](models/entities.md) | All 23 data entities |
| [models/platform-events.md](models/platform-events.md) | All event records |
| [models/action-context-model.md](models/action-context-model.md) | ActionContext model |
| [models/trigger-and-action-entities.md](models/trigger-and-action-entities.md) | Trigger/BotAction entities |

### UI
| Doc | Description |
|-----|-------------|
| [ui/main-viewmodel.md](ui/main-viewmodel.md) | MainViewModel navigation |
| [ui/main-window.md](ui/main-window.md) | MainWindow layout |
| [ui/chat-viewmodel.md](ui/chat-viewmodel.md) | ChatViewModel |
| [ui/chat-view.md](ui/chat-view.md) | Chat view |
| [ui/commands-view.md](ui/commands-view.md) | Commands page (built-in/custom/timers/quotes tabs) |
| [ui/dashboard-viewmodel.md](ui/dashboard-viewmodel.md) | DashboardViewModel |
| [ui/dashboard-view.md](ui/dashboard-view.md) | Dashboard view |
| [ui/platforms-viewmodel.md](ui/platforms-viewmodel.md) | PlatformsViewModel |
| [ui/platforms-view.md](ui/platforms-view.md) | Platforms view |
| [ui/settings-viewmodel.md](ui/settings-viewmodel.md) | SettingsViewModel |
| [ui/settings-view.md](ui/settings-view.md) | Settings view |
| [ui/settings-viewmodels.md](ui/settings-viewmodels.md) | Sub-setting ViewModels |
| [ui/settings-views-xaml.md](ui/settings-views-xaml.md) | Settings XAML views |
| [ui/sounds-view.md](ui/sounds-view.md) | Sounds page (sound library, test playback) |
| [ui/statistics-viewmodel.md](ui/statistics-viewmodel.md) | Statistics page (dashboards, aggregated CSV export) |
| [ui/profile-selector.md](ui/profile-selector.md) | Profile selector |
| [ui/data-management.md](ui/data-management.md) | Settings → Data Management (storage, inventory, backup/import) |
| [ui/converters.md](ui/converters.md) | Value converters |
| [ui/converters-reference.md](ui/converters-reference.md) | Converter reference |
| [ui/dialogs-and-modals.md](ui/dialogs-and-modals.md) | Dialogs |
| [ui/discord-bot-detail.md](ui/discord-bot-detail.md) | Per-bot Discord dashboard |
| [ui/discord-dashboard.md](ui/discord-dashboard.md) | Discord DASHBOARD tab (fleet KPIs, mod actions, workflow runs) |
| [ui/event-history-viewmodel.md](ui/event-history-viewmodel.md) | Event History page (event log viewer, CSV export) |
| [ui/event-testing-viewmodel.md](ui/event-testing-viewmodel.md) | Event Testing page (TestEventPublisher UI) |
| [ui/performance-view.md](ui/performance-view.md) | Performance tab (live metrics, health, child processes) |
| [ui/viewer-hub.md](ui/viewer-hub.md) | Viewer Hub (linking, bonuses, odds, goals) |
| [ui/kick-ui-viewmodels.md](ui/kick-ui-viewmodels.md) | Kick ViewModels |
| [ui/kick-web-setup-viewmodel.md](ui/kick-web-setup-viewmodel.md) | Kick OAuth setup |
| [ui/twitch-settings-viewmodel.md](ui/twitch-settings-viewmodel.md) | Twitch ViewModels |
| [ui/oauth-setup-wizards.md](ui/oauth-setup-wizards.md) | OAuth setup wizards |
| [ui/setup-views-xaml.md](ui/setup-views-xaml.md) | Setup XAML views |
| [ui/theme-system.md](ui/theme-system.md) | Theme system |
| [ui/THEMING.md](ui/THEMING.md) | Theming guide (switch/extend themes) |
| [ui/view-code-behind.md](ui/view-code-behind.md) | Code-behind patterns |

### Other
| Doc | Description |
|-----|-------------|
| [application/app-entry.md](application/app-entry.md) | App startup and DI |
| [integrations/twitch-irc-client.md](integrations/twitch-irc-client.md) | IRC client details |
| [KICK_API_COMPLETE_DOCUMENTATION.md](KICK_API_COMPLETE_DOCUMENTATION.md) | Kick public API reference |
| [copilot-instructions.md](copilot-instructions.md) | Coding guidelines |
| [CHANGELOG.md](CHANGELOG.md) | Release changelog |
| [release-qa-checklist.md](release-qa-checklist.md) | Manual pre-release QA checklist (build, tests, smoke pass) |
