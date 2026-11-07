# UI Reference

The complete window and view inventory for Vyral's WPF desktop app: every XAML surface under `src/VyralBot.UI`, how each one is reached, which ViewModel drives it, and where it is documented — with dead surfaces flagged.

Related pages: [App Shell](../home/app-shell.md), [Home](../home/dashboard.md). Reference siblings: [Command Catalog](command-catalog.md), [Variables Catalog](variables-catalog.md), [Database Reference](database-reference.md), [Known Issues Ledger](known-issues.md).

Counts: 63 XAML surfaces under `Views/` (54 `*View.xaml` pages/views plus 9 window/dialog files) plus `MainWindow.xaml` — **60+ live** (every sidebar/Home/Settings destination, the OBS/Restream/StreamDeck pages, the flow editor and marketplace overlays, and the viewer-profile window) and **3 dead** (`DemoSetupView`, `GenericWebSetupView`, `ProfileSelectorWindow`, `ErrorDialog` remain as dead copies/legacy surfaces; `NgrokSetupView` and its `.old`/`.backup` siblings were deleted), plus one orphaned `SettingsView.xaml.old` copy that is not compiled. The Home dashboard adds one more live surface without a file of its own (an inline template in `MainWindow.xaml`). The project's `.xaml` files also include `App.xaml`, theme dictionaries, and shared controls, which are not view pages.

---

## How the UI is wired

- All main pages render through one `ContentControl` bound to `MainViewModel.CurrentViewModel` ([MainWindow.xaml](../../src/VyralBot.UI/MainWindow.xaml):503) using 32 implicit `DataTemplate`s (`MainWindow.xaml:522-947`). There is no `Frame`/page navigation — navigation commands on [MainViewModel.cs](../../src/VyralBot.UI/ViewModels/MainViewModel.cs) swap the ViewModel and WPF picks the view.
- The five per-platform **chat quick-settings** views render inside `Popup`s in the chat window via a `LazyPopupContent` helper ([ChatView.xaml](../../src/VyralBot.UI/Views/ChatView.xaml):131-273).
- The five **platform web-setup wizards** are embedded panels, not pages: `PlatformsView.xaml.cs:37-95` lazily constructs each view into a `*SetupContainer` `ContentControl` (`PlatformsView.xaml:384-402`) when its `Show<Platform>Setup` flag turns on. Four are OAuth wizards; Discord's is a bot-token flow (see [Discord](../platforms/discord.md)).
- Two settings sub-pages and two platform settings pages render *inside* SettingsView through nested `ContentControl`s that reuse the same implicit templates ([SettingsView.xaml](../../src/VyralBot.UI/Views/SettingsView.xaml):834, :862, :904-905).
- ViewModels are DI singletons (or scoped transients for wizards) registered at [App.xaml.cs](../../src/VyralBot.UI/App.xaml.cs):186-246; most views are never registered — the DataTemplate creates them lazily.

---

## App shell

| Surface | Kind | How it's reached | ViewModel | Documented in |
|---|---|---|---|---|
| [MainWindow.xaml](../../src/VyralBot.UI/MainWindow.xaml) | Window | The app window, resolved and shown at startup ([App.xaml.cs](../../src/VyralBot.UI/App.xaml.cs):318-325). Hosts the custom title bar, sidebar (`:145-497`), and the content host | [MainViewModel.cs](../../src/VyralBot.UI/ViewModels/MainViewModel.cs) | [App Shell](../home/app-shell.md) |
| Home dashboard (inline `DataTemplate` in `MainWindow.xaml:522-792` — no `.xaml` file of its own) | Page | Sidebar **Home** | [DashboardViewModel.cs](../../src/VyralBot.UI/ViewModels/DashboardViewModel.cs) | [Home](../home/dashboard.md) |
| Profile selector overlay (inline in `MainWindow.xaml:953-1205`) | Overlay | Shown automatically at startup when more than one profile exists (`ShowProfileSelector`); includes the create-profile card and PIN entry | MainViewModel | [Profiles](../getting-started/profiles.md) |

The sidebar's **Events & Alerts** (`MainWindow.xaml:249-258`) and **Browser Sources** (`:323-332`) buttons are wired (KI-17 resolved, 2026-08-15): the former navigates to Alert Settings, the latter to Audio Settings (with a tooltip explaining that browser-source configuration lives there).

---

## Main content pages

Hosted directly in the MainWindow content area via the sidebar or Home cards.

| View | Kind | How it's reached | ViewModel | Documented in |
|---|---|---|---|---|
| [ChatView.xaml](../../src/VyralBot.UI/Views/ChatView.xaml) | Page | Sidebar **Chat** | [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) | [Chat Window](../chat/chat-window.md) |
| [ObsView.xaml](../../src/VyralBot.UI/Views/ObsView.xaml) | Page | Sidebar **OBS** (`NavigateToObsCommand`, `MainViewModel.cs:1993`). Three tabs: Full OBS (hosts obs64.exe), Bot Studio (embedded libobs), Collections | [ObsViewModel.cs](../../src/VyralBot.UI/ViewModels/ObsViewModel.cs) | [TikTok & OBS](../platforms/tiktok-discord-obs.md) |
| [RestreamView.xaml](../../src/VyralBot.UI/Views/RestreamView.xaml) | Page | Sidebar **Restream → Restream** (`MainViewModel.cs:2022`). FFmpeg multi-destination restreaming | [RestreamViewModel.cs](../../src/VyralBot.UI/ViewModels/RestreamViewModel.cs) | *(page pending — see the Restream group in the sidebar)* |
| [StreamDeckView.xaml](../../src/VyralBot.UI/Views/StreamDeckView.xaml) | Page | Sidebar **Restream → StreamDeck** (`MainViewModel.cs:2048`). Button grid + VyralDeck mobile companion server | [StreamDeckViewModel.cs](../../src/VyralBot.UI/ViewModels/StreamDeckViewModel.cs) | *(page pending)* |
| [ModeratorView.xaml](../../src/VyralBot.UI/Views/ModeratorView.xaml) | Page | Home card **Moderator** / the Chat Hub's Moderator chip (`NavigateToModeratorCommand`, `MainViewModel.cs:1917`) — the mod dashboard | [ModeratorViewModel.cs](../../src/VyralBot.UI/ViewModels/ModeratorViewModel.cs) | [Moderator Guide](../architecture/moderator-guide.md) |
| [ChatHubView.xaml](../../src/VyralBot.UI/Views/ChatHubView.xaml) | Page | Home card **Chat**. Self-contained per-platform chat-event settings page (binds the chat-settings VMs directly, not the popup views below) | [ChatHubViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatHubViewModel.cs) | [Chat Settings](../chat/chat-settings.md) |
| [ToolsView.xaml](../../src/VyralBot.UI/Views/ToolsView.xaml) | Page | Home card **Tools**. Hub of cards that re-route to the Stream Tools pages | [ToolsViewModel.cs](../../src/VyralBot.UI/ViewModels/ToolsViewModel.cs) | [Tools Hub](../home/tools-hub.md) |
| [IntegrationsView.xaml](../../src/VyralBot.UI/Views/IntegrationsView.xaml) | Page | Home card **Integrations**. Placeholder "Coming soon" cards (e.g. BotGhost) | [IntegrationsViewModel.cs](../../src/VyralBot.UI/ViewModels/IntegrationsViewModel.cs) | [Integrations](../home/integrations.md) |
| [DocumentationView.xaml](../../src/VyralBot.UI/Views/DocumentationView.xaml) | Page | Sidebar **Help** and Home support row **Documentation** (in-app GitBook browser) | [DocumentationViewModel.cs](../../src/VyralBot.UI/ViewModels/DocumentationViewModel.cs) | [Documentation](../home/documentation.md) |
| [SettingsView.xaml](../../src/VyralBot.UI/Views/SettingsView.xaml) | Page | Sidebar **Settings**. 16 categories, all implemented: Profile, Theme, Platforms, Data, Notifications, Commands, Viewer Hub, Variables, Timers, Automation, Stream FX, Moderator, API & Webhooks, Performance, Security, Advanced | [SettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/SettingsViewModel.cs) | [Settings Overview](../settings/overview.md) |
| [EventTestingView.xaml](../../src/VyralBot.UI/Views/EventTestingView.xaml) | Page | Sidebar bottom **Event Testing** (developer tool) | [EventTestingViewModel.cs](../../src/VyralBot.UI/ViewModels/EventTestingViewModel.cs) | [Event Testing](../chat-features/event-testing.md) |

### Chat Features section (sidebar group)

| View | Kind | How it's reached | ViewModel | Documented in |
|---|---|---|---|---|
| [CurrencyView.xaml](../../src/VyralBot.UI/Views/CurrencyView.xaml) | Page | Sidebar **Chat Features → Currency & Rewards** | [CurrencyViewModel.cs](../../src/VyralBot.UI/ViewModels/CurrencyViewModel.cs) | [Currency & Rewards](../stream-tools/currency.md) |
| [CommandsView.xaml](../../src/VyralBot.UI/Views/CommandsView.xaml) | Page | Sidebar **Chat Features → Commands** (hosts the full-size Visual Flow Editor overlay for command editing) | [CommandsViewModel.cs](../../src/VyralBot.UI/ViewModels/CommandsViewModel.cs) | [Commands](../chat-features/commands.md) |
| [EventHistoryView.xaml](../../src/VyralBot.UI/Views/EventHistoryView.xaml) | Page | Sidebar **Chat Features → Event History** | [EventHistoryViewModel.cs](../../src/VyralBot.UI/ViewModels/EventHistoryViewModel.cs) | [Event History](../chat-features/event-history.md) |
| [AutomationView.xaml](../../src/VyralBot.UI/Views/AutomationView.xaml) | Page | Sidebar **Chat Features → Automation** (`MainViewModel.cs:1262`) — the workflow list/manager | [AutomationViewModel.cs](../../src/VyralBot.UI/ViewModels/AutomationViewModel.cs) | [Automation](../chat-features/automation.md) |
| [FlowEditorView.xaml](../../src/VyralBot.UI/Views/FlowEditorView.xaml) | Overlay | Hosted full-screen by the **Commands** page and the **Automation** page — the visual flow editor canvas (nodes, wires, settings panes) | [FlowEditorViewModel.cs](../../src/VyralBot.UI/ViewModels/FlowEditorViewModel.cs) | [Automation](../chat-features/automation.md) |
| [MarketplaceView.xaml](../../src/VyralBot.UI/Views/MarketplaceView.xaml) | Overlay | Hosted by the **Commands** page — the sharecode/marketplace browser (GitHub-backed store) | [MarketplaceViewModel.cs](../../src/VyralBot.UI/ViewModels/MarketplaceViewModel.cs) | [Commands](../chat-features/commands.md) |
| [ConditionalRulesView.xaml](../../src/VyralBot.UI/Views/ConditionalRulesView.xaml) | Page | Sidebar **Stream Tools → Conditional Rules** | [ConditionalRulesViewModel.cs](../../src/VyralBot.UI/ViewModels/ConditionalRulesViewModel.cs) | [Conditional Rules](../chat-features/conditional-rules.md) |

### Stream Tools section (sidebar group)

| View | Kind | How it's reached | ViewModel | Documented in |
|---|---|---|---|---|
| [AudioSettingsView.xaml](../../src/VyralBot.UI/Views/AudioSettingsView.xaml) | Page | Sidebar **Stream Tools → Audio Settings** | [AudioSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/AudioSettingsViewModel.cs) | [Audio Settings](../stream-tools/audio.md) |
| [SoundsView.xaml](../../src/VyralBot.UI/Views/SoundsView.xaml) | Page | Sidebar **Stream Tools → Sounds Management** | [SoundsViewModel.cs](../../src/VyralBot.UI/ViewModels/SoundsViewModel.cs) | [Sounds](../stream-tools/sounds.md) |
| [StatisticsView.xaml](../../src/VyralBot.UI/Views/StatisticsView.xaml) | Page | Sidebar **Stream Tools → Statistics** | [StatisticsViewModel.cs](../../src/VyralBot.UI/ViewModels/StatisticsViewModel.cs) | [Statistics](../stream-tools/statistics.md) |
| [AlertSettingsView.xaml](../../src/VyralBot.UI/Views/AlertSettingsView.xaml) | Page | Sidebar **Stream Tools → Visual Alerts** (settings persist to the `AlertSettings` Configs key) | [AlertSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/AlertSettingsViewModel.cs) | [Visual Alerts](../stream-tools/visual-alerts.md) |
| [GiveawayView.xaml](../../src/VyralBot.UI/Views/GiveawayView.xaml) | Page | Sidebar **Stream Tools → Giveaways** | [GiveawayViewModel.cs](../../src/VyralBot.UI/ViewModels/GiveawayViewModel.cs) | [Giveaways](../stream-tools/giveaways.md) |
| [PollView.xaml](../../src/VyralBot.UI/Views/PollView.xaml) | Page | Sidebar **Stream Tools → Polls** | [PollViewModel.cs](../../src/VyralBot.UI/ViewModels/PollViewModel.cs) | [Polls](../stream-tools/polls.md) |
| [WatchtimeView.xaml](../../src/VyralBot.UI/Views/WatchtimeView.xaml) | Page | Sidebar **Stream Tools → Watchtime** | [WatchtimeViewModel.cs](../../src/VyralBot.UI/ViewModels/WatchtimeViewModel.cs) | [Watchtime](../stream-tools/watchtime.md) |

All Stream Tools views are also reachable from the Tools hub cards (`ToolsView.xaml` binds the same `MainViewModel` navigation commands).

---

## Platform surfaces

| View | Kind | How it's reached | ViewModel | Documented in |
|---|---|---|---|---|
| [PlatformsView.xaml](../../src/VyralBot.UI/Views/PlatformsView.xaml) | Page | Home card **Connections**. Platform cards with connect/disconnect; hosts the five web-setup wizards below as embedded panels | [PlatformsViewModel.cs](../../src/VyralBot.UI/ViewModels/PlatformsViewModel.cs) | [Platforms Overview](../platforms/overview.md) |
| [TwitchWebSetupView.xaml](../../src/VyralBot.UI/Views/TwitchWebSetupView.xaml) | Wizard | Platforms page → Twitch card **Connect** (embedded via `TwitchSetupContainer`) | [TwitchWebSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/TwitchWebSetupViewModel.cs) | [Twitch](../platforms/twitch.md) |
| [KickWebSetupView.xaml](../../src/VyralBot.UI/Views/KickWebSetupView.xaml) | Wizard | Platforms page → Kick card **Connect** | [KickWebSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/KickWebSetupViewModel.cs) | [Kick](../platforms/kick.md) |
| [YouTubeWebSetupView.xaml](../../src/VyralBot.UI/Views/YouTubeWebSetupView.xaml) | Wizard | Platforms page → YouTube card **Connect** | [YouTubeWebSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/YouTubeWebSetupViewModel.cs) | [YouTube](../platforms/youtube.md) |
| [TikTokWebSetupView.xaml](../../src/VyralBot.UI/Views/TikTokWebSetupView.xaml) | Wizard | Platforms page → TikTok card **Connect** | [TikTokWebSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/TikTokWebSetupViewModel.cs) | [TikTok & OBS](../platforms/tiktok-discord-obs.md) |
| [DiscordWebSetupView.xaml](../../src/VyralBot.UI/Views/DiscordWebSetupView.xaml) | Wizard | Platforms page → Discord card **Connect**, or Discord Bots page → **RUN FULL WIZARD** — a real bot-token wizard (portal → token → invite → validated connect) for the primary setup bot | [DiscordWebSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/DiscordWebSetupViewModel.cs) | [Discord](../platforms/discord.md) |
| [DiscordBotsView.xaml](../../src/VyralBot.UI/Views/DiscordBotsView.xaml) | Page | Sidebar **Platforms → Discord** (always visible, `NavigateToDiscordBotsCommand`, `MainViewModel.cs:1763`) — Quick Add, per-bot cards (OPEN/START/STOP/RENAME/REMOVE), RUN FULL WIZARD | [DiscordBotsViewModel.cs](../../src/VyralBot.UI/ViewModels/DiscordBotsViewModel.cs) | [Discord](../platforms/discord.md) |
| [DiscordBotDetailView.xaml](../../src/VyralBot.UI/Views/DiscordBotDetailView.xaml) | Page | Discord Bots page → bot card **OPEN** (`NavigateToDiscordBotDetailAsync`, `MainViewModel.cs:1807`) — per-bot dashboard: status, START/STOP/RECONNECT, servers list, SET ACTIVE CHANNEL, owner-ping test | [DiscordBotDetailViewModel.cs](../../src/VyralBot.UI/ViewModels/DiscordBotDetailViewModel.cs) | [Discord](../platforms/discord.md) |
| [TwitchDashboardView.xaml](../../src/VyralBot.UI/Views/TwitchDashboardView.xaml) | Page | Sidebar **Platforms → Twitch** (visible only when connected; `NavigateToTwitchSettingsCommand` actually loads the dashboard, `MainViewModel.cs:1513`) | [TwitchDashboardViewModel.cs](../../src/VyralBot.UI/ViewModels/TwitchDashboardViewModel.cs) | [Platform Dashboards](../platforms/dashboards.md) |
| [KickDashboardView.xaml](../../src/VyralBot.UI/Views/KickDashboardView.xaml) | Page | Sidebar **Platforms → Kick** (same pattern, `MainViewModel.cs:1546`) | [KickDashboardViewModel.cs](../../src/VyralBot.UI/ViewModels/KickDashboardViewModel.cs) | [Platform Dashboards](../platforms/dashboards.md) |
| [YouTubeDashboardView.xaml](../../src/VyralBot.UI/Views/YouTubeDashboardView.xaml) | Page | Sidebar **Platforms → YouTube** (visible only when connected; `NavigateToYouTubeSettingsCommand`) | [YouTubeDashboardViewModel.cs](../../src/VyralBot.UI/ViewModels/YouTubeDashboardViewModel.cs) | [Platform Dashboards](../platforms/dashboards.md) |
| [DiscordDashboardView.xaml](../../src/VyralBot.UI/Views/DiscordDashboardView.xaml) | Page | Discord Bots page header → **DASHBOARD** toggle — the single-bot dashboard hosted lazily as a tab (servers, member counts, channel drop-downs, active-channel picker, test message) | [DiscordDashboardViewModel.cs](../../src/VyralBot.UI/ViewModels/DiscordDashboardViewModel.cs) | [Platform Dashboards](../platforms/dashboards.md) |
| [ObsHostView.xaml](../../src/VyralBot.UI/Views/ObsHostView.xaml) / [ObsStudioView.xaml](../../src/VyralBot.UI/Views/ObsStudioView.xaml) | Tabs | Hosted inside **OBS** page tabs: Full OBS (real obs64.exe window embed + silent install + obs-websocket) and Bot Studio (embedded libobs) | [ObsHostViewModel.cs](../../src/VyralBot.UI/ViewModels/ObsHostViewModel.cs) / [ObsStudioViewModel.cs](../../src/VyralBot.UI/ViewModels/ObsStudioViewModel.cs) | [TikTok & OBS](../platforms/tiktok-discord-obs.md) |
| [TwitchSettingsView.xaml](../../src/VyralBot.UI/Views/TwitchSettingsView.xaml) | Page | Settings → Platforms → **Twitch** card (`SettingsViewModel.cs:577-587` sets `SelectedPlatformViewModel`; rendered at `SettingsView.xaml:904`) | [TwitchSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/TwitchSettingsViewModel.cs) | [Twitch](../platforms/twitch.md) |
| [KickSettingsView.xaml](../../src/VyralBot.UI/Views/KickSettingsView.xaml) | Page | Settings → Platforms → **Kick** card (`SettingsViewModel.cs:588-598`) | [KickSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/KickSettingsViewModel.cs) | [Kick](../platforms/kick.md) |

The Settings → Platforms grid's YouTube / TikTok / Discord / OBS cards are deliberately disabled with explanatory tooltips (`ShowOnDisabled`) — YouTube and Discord are configured only through their Platforms-page wizards (Discord's dashboard handles channels), TikTok has no backend, and OBS is managed from its own sidebar **OBS** page rather than a settings sub-page (KI-17's dead-button half is resolved).

---

## Chat quick-settings popups

Rendered as `Popup`s from the chat window header via `LazyPopupContent` (`ChatView.xaml:131-273`). The YouTube popup reuses the DI-singleton VM while Twitch/Kick build fresh instances (an inconsistent-but-harmless lifecycle quirk).

| View | Kind | How it's reached | ViewModel | Documented in |
|---|---|---|---|---|
| [TwitchChatSettingsView.xaml](../../src/VyralBot.UI/Views/TwitchChatSettingsView.xaml) | Popup | Chat window header → Twitch quick-settings (`IsTwitchSettingsOpen`) | [TwitchChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/TwitchChatSettingsViewModel.cs) | [Chat Settings](../chat/chat-settings.md) |
| [KickChatSettingsView.xaml](../../src/VyralBot.UI/Views/KickChatSettingsView.xaml) | Popup | Chat window header → Kick quick-settings | [KickChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/KickChatSettingsViewModel.cs) | [Chat Settings](../chat/chat-settings.md) |
| [YoutubeChatSettingsView.xaml](../../src/VyralBot.UI/Views/YoutubeChatSettingsView.xaml) | Popup | Chat window header → YouTube quick-settings | [YoutubeChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/YoutubeChatSettingsViewModel.cs) | [Chat Settings](../chat/chat-settings.md) |
| [TiktokChatSettingsView.xaml](../../src/VyralBot.UI/Views/TiktokChatSettingsView.xaml) | Popup | Chat window header → TikTok quick-settings (toggles persist to config keys; not yet consumed by the feed) | [TiktokChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/TiktokChatSettingsViewModel.cs) | [Chat Settings](../chat/chat-settings.md) |
| [DiscordChatSettingsView.xaml](../../src/VyralBot.UI/Views/DiscordChatSettingsView.xaml) | Popup | Chat window header → Discord quick-settings (show-messages toggle live; appearance toggles persist but are not yet consumed) | [DiscordChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/DiscordChatSettingsViewModel.cs) | [Chat Settings](../chat/chat-settings.md) |

---

## Settings sub-pages

Rendered inside SettingsView through nested `ContentControl`s that reuse MainWindow's implicit DataTemplates.

| View | Kind | How it's reached | ViewModel | Documented in |
|---|---|---|---|---|
| [VariablesView.xaml](../../src/VyralBot.UI/Views/VariablesView.xaml) | Page | Settings → **Variables** (`SettingsView.xaml:834`). No sidebar entry and no `NavigateToVariables` command exists (KI-18) | [VariablesViewModel.cs](../../src/VyralBot.UI/ViewModels/VariablesViewModel.cs) | [Variables](../chat-features/variables.md) |
| [WebServerSettingsView.xaml](../../src/VyralBot.UI/Views/WebServerSettingsView.xaml) | Page | Settings → **API & Webhooks** (`SettingsView.xaml:862`) | [WebServerSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/WebServerSettingsViewModel.cs) | [API, Web Server & ngrok](../settings/web-server-ngrok.md) |
| [PerformanceView.xaml](../../src/VyralBot.UI/Views/PerformanceView.xaml) | Page | Settings → **Performance** (nested `ContentControl` at `SettingsView.xaml:2269`) — the live performance monitor | [PerformanceViewModel.cs](../../src/VyralBot.UI/ViewModels/PerformanceViewModel.cs) | [Settings Overview](../settings/overview.md) |
| [ViewerHubView.xaml](../../src/VyralBot.UI/Views/ViewerHubView.xaml) | Page | Settings → **Viewer Hub** (nested `ContentControl` at `SettingsView.xaml:2273`) — linking, multi-platform bonus, goals, linked viewers | [ViewerHubViewModel.cs](../../src/VyralBot.UI/ViewModels/ViewerHubViewModel.cs) | [Settings Overview](../settings/overview.md) |

---

## Standalone wizards and overlays

| View | Kind | How it's reached | ViewModel | Documented in |
|---|---|---|---|---|
| [GoogleDemoSetupView.xaml](../../src/VyralBot.UI/Views/GoogleDemoSetupView.xaml) | Wizard | Sidebar bottom **Demo Wizard** (`MainViewModel.cs:1264-1287`). Reusable-component demo that writes dummy `VYRALBOT_*` env vars (KI-22) | [GoogleDemoSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/GoogleDemoSetupViewModel.cs) | [Demo Wizard](../home/demo-wizard.md) |
| [NgrokSetupWizardView.xaml](../../src/VyralBot.UI/Views/NgrokSetupWizardView.xaml) | Wizard | Settings → API & Webhooks → **Run Setup Wizard** (`WebServerSettingsViewModel.cs:196-207` calls `NavigateToNgrokSetupWizard`, `MainViewModel.cs:1300-1323`) | [NgrokSetupWizardViewModel.cs](../../src/VyralBot.UI/ViewModels/NgrokSetupWizardViewModel.cs) | [API, Web Server & ngrok](../settings/web-server-ngrok.md) |
| [AlertOverlayWindow.xaml](../../src/VyralBot.UI/Views/AlertOverlayWindow.xaml) | Window | Never user-opened — created on demand by [AlertService.cs](../../src/VyralBot.UI/Services/AlertService.cs):61 when a stream event fires the on-screen visual alert | none (code-behind) | [Visual Alerts](../stream-tools/visual-alerts.md) |

---

## Dialogs

| Dialog | Kind | How it's reached | ViewModel | Documented in |
|---|---|---|---|---|
| [ConfirmDialog.xaml](../../src/VyralBot.UI/Views/ConfirmDialog.xaml) | Dialog | Shown by `MainViewModel.CheckSetupInProgress` (`MainViewModel.cs:649`, `:673`) when navigating away mid-Twitch/Kick OAuth setup | none (code-behind) | [App Shell](../home/app-shell.md) |
| [CustomDialog.xaml](../../src/VyralBot.UI/Views/CustomDialog.xaml) | Dialog | Chat window confirmations: clear history, send-to-all-platforms, errors (`ChatViewModel.cs:1465-1512`) | none (code-behind) | [Chat Window](../chat/chat-window.md) |
| [ViewerProfileWindow.xaml](../../src/VyralBot.UI/Views/ViewerProfileWindow.xaml) | Window | Clicking a username in the chat feed opens the viewer's profile (balances, linked accounts, DM history) | none (code-behind) | [Viewer Profiles](../chat-features/viewer-profiles.md) |
| [ShareCommandDialog.xaml](../../src/VyralBot.UI/Views/ShareCommandDialog.xaml) / [ExportCodeDialog.xaml](../../src/VyralBot.UI/Views/ExportCodeDialog.xaml) / [ImportCodeDialog.xaml](../../src/VyralBot.UI/Views/ImportCodeDialog.xaml) | Dialogs | The Commands page / Marketplace sharecode flows (share a command as a code, export/import flow JSON) | none (code-behind) | [Commands](../chat-features/commands.md) |

---

## Dead UI inventory

Surfaces present in the source tree that no code path can show, plus orphaned files. Cross-referenced from the [known issues ledger](known-issues.md) (KI-18 unless noted).

| Surface | Why it's dead |
|---|---|
| [ErrorDialog.xaml](../../src/VyralBot.UI/Views/ErrorDialog.xaml) | No `new ErrorDialog` or DI registration anywhere in the solution — superseded by `CustomDialog` |
| [ProfileSelectorWindow.xaml](../../src/VyralBot.UI/Views/ProfileSelectorWindow.xaml) | Superseded by the inline MainWindow profile-selector overlay; only its own files reference it. Its dead VM ([ProfileSelectorViewModel.cs](../../src/VyralBot.UI/ViewModels/ProfileSelectorViewModel.cs), still DI-registered) is the only place with PIN-length validation (KI-20's PBKDF2 fix landed in the live `ProfileService`; the dead VM is unchanged) |
| [DemoSetupView.xaml](../../src/VyralBot.UI/Views/DemoSetupView.xaml) | Superseded by `GoogleDemoSetupView`; referenced nowhere outside its own code-behind |
| [GenericWebSetupView.xaml](../../src/VyralBot.UI/Views/GenericWebSetupView.xaml) | Standalone prototype of the web-setup layout, not a base class of the five platform wizards and referenced nowhere |
| [SettingsView.xaml.old](../../src/VyralBot.UI/Views/SettingsView.xaml.old) | Orphaned file copy, excluded from compilation by extension (the former `NgrokSetupView.xaml` and its `.old`/`.backup` siblings, and `MainWindow.xaml.broken`/`.new`, have since been deleted) |

`DiscordDashboardView` is **no longer dead** — it left this table when the Discord Bots page gained its **DASHBOARD** tab (see [Platform Dashboards](../platforms/dashboards.md)).

Dead navigation targets with no view behind them: MainViewModel's generated `NavigateToTriggersCommand`/`NavigateToActionsCommand` (`MainViewModel.cs:734`, `:858`), which are bound nowhere and have no target views. (The formerly dead sidebar **Events & Alerts** and **Browser Sources** buttons and the Home **Discord**/**Ideas & Suggestions** support rows were all wired on 2026-08-15 — KI-17 resolved; the support rows open the configurable `Support.DiscordUrl`/`Support.IdeasUrl` links.)

---

## See also

- [App Shell](../home/app-shell.md) — the sidebar, navigation model, and dead buttons in context
- [Command Catalog](command-catalog.md) / [Variables Catalog](variables-catalog.md) — the data behind the Commands and Variables pages
- [Database Reference](database-reference.md) — what each page persists
- [Known Issues Ledger](known-issues.md) — KI-17 (dead buttons, resolved) and KI-18 (dead/orphaned code, partially resolved) cover the surfaces listed above
