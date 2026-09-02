# UI Reference

The complete window and view inventory for VyralBot's WPF desktop app: every XAML surface under `src/VyralBot.UI`, how each one is reached, which ViewModel drives it, and where it is documented — with dead surfaces flagged.

Related pages: [App Shell](../home/app-shell.md), [Home](../home/dashboard.md). Reference siblings: [Command Catalog](command-catalog.md), [Variables Catalog](variables-catalog.md), [Database Reference](database-reference.md), [Known Issues Ledger](known-issues.md).

Counts: 46 XAML surfaces inventoried (45 files under `Views/` plus `MainWindow.xaml`) — **41 live** (40 views plus the shell) and **5 dead** (`DemoSetupView`, `NgrokSetupView`, `GenericWebSetupView`, `ProfileSelectorWindow`, `ErrorDialog`), plus 5 orphaned `.old`/`.backup`/`.broken`/`.new` file copies that are not compiled. The Home dashboard adds one more live surface without a file of its own (an inline template in `MainWindow.xaml`). The project's 49 `.xaml` files also include `App.xaml`, the RedSlate theme dictionary, and the shared `VariablePicker` control, which are not view pages.

---

## How the UI is wired

- All main pages render through one `ContentControl` bound to `MainViewModel.CurrentViewModel` ([MainWindow.xaml](../../src/VyralBot.UI/MainWindow.xaml):503) using 27 implicit `DataTemplate`s (`MainWindow.xaml:506-905`). There is no `Frame`/page navigation — navigation commands on [MainViewModel.cs](../../src/VyralBot.UI/ViewModels/MainViewModel.cs) swap the ViewModel and WPF picks the view.
- The five per-platform **chat quick-settings** views render inside `Popup`s in the chat window via a `LazyPopupContent` helper ([ChatView.xaml](../../src/VyralBot.UI/Views/ChatView.xaml):131-273).
- The five **OAuth web-setup wizards** are embedded panels, not pages: `PlatformsView.xaml.cs:37-95` lazily constructs each view into a `*SetupContainer` `ContentControl` (`PlatformsView.xaml:384-402`) when its `Show<Platform>Setup` flag turns on.
- Two settings sub-pages and two platform settings pages render *inside* SettingsView through nested `ContentControl`s that reuse the same implicit templates ([SettingsView.xaml](../../src/VyralBot.UI/Views/SettingsView.xaml):834, :862, :904-905).
- ViewModels are DI singletons (or scoped transients for wizards) registered at [App.xaml.cs](../../src/VyralBot.UI/App.xaml.cs):186-246; most views are never registered — the DataTemplate creates them lazily.

---

## App shell

| Surface | Kind | How it's reached | ViewModel | Documented in |
|---|---|---|---|---|
| [MainWindow.xaml](../../src/VyralBot.UI/MainWindow.xaml) | Window | The app window, resolved and shown at startup ([App.xaml.cs](../../src/VyralBot.UI/App.xaml.cs):318-325). Hosts the custom title bar, sidebar (`:145-497`), and the content host | [MainViewModel.cs](../../src/VyralBot.UI/ViewModels/MainViewModel.cs) | [App Shell](../home/app-shell.md) |
| Home dashboard (inline `DataTemplate` in `MainWindow.xaml:506-770` — no `.xaml` file of its own) | Page | Sidebar **Home** | [DashboardViewModel.cs](../../src/VyralBot.UI/ViewModels/DashboardViewModel.cs) | [Home](../home/dashboard.md) |
| Profile selector overlay (inline in `MainWindow.xaml:911-1162`) | Overlay | Shown automatically at startup when more than one profile exists (`ShowProfileSelector`); includes the create-profile card and PIN entry | MainViewModel | [Profiles](../getting-started/profiles.md) |

The sidebar's **Events & Alerts** (`MainWindow.xaml:250-258`) and **Browser Sources** (`:323-332`) buttons have no Command binding and no target view — see [Dead UI inventory](#dead-ui-inventory) and KI-17.

---

## Main content pages

Hosted directly in the MainWindow content area via the sidebar or Home cards.

| View | Kind | How it's reached | ViewModel | Documented in |
|---|---|---|---|---|
| [ChatView.xaml](../../src/VyralBot.UI/Views/ChatView.xaml) | Page | Sidebar **Chat** | [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) | [Chat Window](../chat/chat-window.md) |
| [ChatHubView.xaml](../../src/VyralBot.UI/Views/ChatHubView.xaml) | Page | Home card **Chat**. Self-contained per-platform chat-event settings page (binds the chat-settings VMs directly, not the popup views below) | [ChatHubViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatHubViewModel.cs) | [Chat Settings](../chat/chat-settings.md) |
| [ToolsView.xaml](../../src/VyralBot.UI/Views/ToolsView.xaml) | Page | Home card **Tools**. Hub of cards that re-route to the Stream Tools pages | [ToolsViewModel.cs](../../src/VyralBot.UI/ViewModels/ToolsViewModel.cs) | [Tools Hub](../home/tools-hub.md) |
| [IntegrationsView.xaml](../../src/VyralBot.UI/Views/IntegrationsView.xaml) | Page | Home card **Integrations**. Placeholder "Coming soon" cards (e.g. BotGhost) | [IntegrationsViewModel.cs](../../src/VyralBot.UI/ViewModels/IntegrationsViewModel.cs) | [Integrations](../home/integrations.md) |
| [DocumentationView.xaml](../../src/VyralBot.UI/Views/DocumentationView.xaml) | Page | Sidebar **Help** and Home support row **Documentation** (in-app GitBook browser) | [DocumentationViewModel.cs](../../src/VyralBot.UI/ViewModels/DocumentationViewModel.cs) | [Documentation](../home/documentation.md) |
| [SettingsView.xaml](../../src/VyralBot.UI/Views/SettingsView.xaml) | Page | Sidebar **Settings**. Categories: Platforms, Variables, API & Webhooks, General, plus "Coming Soon" placeholders (KI-17) | [SettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/SettingsViewModel.cs) | [Settings Overview](../settings/overview.md) |
| [EventTestingView.xaml](../../src/VyralBot.UI/Views/EventTestingView.xaml) | Page | Sidebar bottom **Event Testing** (developer tool) | [EventTestingViewModel.cs](../../src/VyralBot.UI/ViewModels/EventTestingViewModel.cs) | [Event Testing](../chat-features/event-testing.md) |

### Chat Features section (sidebar group)

| View | Kind | How it's reached | ViewModel | Documented in |
|---|---|---|---|---|
| [CurrencyView.xaml](../../src/VyralBot.UI/Views/CurrencyView.xaml) | Page | Sidebar **Chat Features → Currency & Rewards** | [CurrencyViewModel.cs](../../src/VyralBot.UI/ViewModels/CurrencyViewModel.cs) | [Currency & Rewards](../stream-tools/currency.md) |
| [CommandsView.xaml](../../src/VyralBot.UI/Views/CommandsView.xaml) | Page | Sidebar **Chat Features → Commands** (also embeds the `VariablePicker` control) | [CommandsViewModel.cs](../../src/VyralBot.UI/ViewModels/CommandsViewModel.cs) | [Commands](../chat-features/commands.md) |
| [EventHistoryView.xaml](../../src/VyralBot.UI/Views/EventHistoryView.xaml) | Page | Sidebar **Chat Features → Event History** | [EventHistoryViewModel.cs](../../src/VyralBot.UI/ViewModels/EventHistoryViewModel.cs) | [Event History](../chat-features/event-history.md) |
| [ConditionalRulesView.xaml](../../src/VyralBot.UI/Views/ConditionalRulesView.xaml) | Page | Sidebar **Stream Tools → Conditional Rules** | [ConditionalRulesViewModel.cs](../../src/VyralBot.UI/ViewModels/ConditionalRulesViewModel.cs) | [Conditional Rules](../chat-features/conditional-rules.md) |

### Stream Tools section (sidebar group)

| View | Kind | How it's reached | ViewModel | Documented in |
|---|---|---|---|---|
| [AudioSettingsView.xaml](../../src/VyralBot.UI/Views/AudioSettingsView.xaml) | Page | Sidebar **Stream Tools → Audio Settings** | [AudioSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/AudioSettingsViewModel.cs) | [Audio Settings](../stream-tools/audio.md) |
| [SoundsView.xaml](../../src/VyralBot.UI/Views/SoundsView.xaml) | Page | Sidebar **Stream Tools → Sounds Management** | [SoundsViewModel.cs](../../src/VyralBot.UI/ViewModels/SoundsViewModel.cs) | [Sounds](../stream-tools/sounds.md) |
| [StatisticsView.xaml](../../src/VyralBot.UI/Views/StatisticsView.xaml) | Page | Sidebar **Stream Tools → Statistics** | [StatisticsViewModel.cs](../../src/VyralBot.UI/ViewModels/StatisticsViewModel.cs) | [Statistics](../stream-tools/statistics.md) |
| [AlertSettingsView.xaml](../../src/VyralBot.UI/Views/AlertSettingsView.xaml) | Page | Sidebar **Stream Tools → Visual Alerts** (facade — KI-6) | [AlertSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/AlertSettingsViewModel.cs) | [Visual Alerts](../stream-tools/visual-alerts.md) |
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
| [TikTokWebSetupView.xaml](../../src/VyralBot.UI/Views/TikTokWebSetupView.xaml) | Wizard | Platforms page → TikTok card **Connect** | [TikTokWebSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/TikTokWebSetupViewModel.cs) | [TikTok, Discord & OBS](../platforms/tiktok-discord-obs.md) |
| [DiscordWebSetupView.xaml](../../src/VyralBot.UI/Views/DiscordWebSetupView.xaml) | Wizard | Platforms page → Discord card **Connect** | [DiscordWebSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/DiscordWebSetupViewModel.cs) | [TikTok, Discord & OBS](../platforms/tiktok-discord-obs.md) |
| [TwitchDashboardView.xaml](../../src/VyralBot.UI/Views/TwitchDashboardView.xaml) | Page | Sidebar **Platforms → Twitch** (visible only when connected; `NavigateToTwitchSettingsCommand` actually loads the dashboard, `MainViewModel.cs:1513`) | [TwitchDashboardViewModel.cs](../../src/VyralBot.UI/ViewModels/TwitchDashboardViewModel.cs) | [Platform Dashboards](../platforms/dashboards.md) |
| [KickDashboardView.xaml](../../src/VyralBot.UI/Views/KickDashboardView.xaml) | Page | Sidebar **Platforms → Kick** (same pattern, `MainViewModel.cs:1546`) | [KickDashboardViewModel.cs](../../src/VyralBot.UI/ViewModels/KickDashboardViewModel.cs) | [Platform Dashboards](../platforms/dashboards.md) |
| [TwitchSettingsView.xaml](../../src/VyralBot.UI/Views/TwitchSettingsView.xaml) | Page | Settings → Platforms → **Twitch** card (`SettingsViewModel.cs:577-587` sets `SelectedPlatformViewModel`; rendered at `SettingsView.xaml:904`) | [TwitchSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/TwitchSettingsViewModel.cs) | [Twitch](../platforms/twitch.md) |
| [KickSettingsView.xaml](../../src/VyralBot.UI/Views/KickSettingsView.xaml) | Page | Settings → Platforms → **Kick** card (`SettingsViewModel.cs:588-598`) | [KickSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/KickSettingsViewModel.cs) | [Kick](../platforms/kick.md) |

The Settings → Platforms grid's YouTube / TikTok / Discord / OBS cards have no navigation command (KI-17); YouTube/TikTok/Discord are configured only through the web-setup wizards above.

---

## Chat quick-settings popups

Rendered as `Popup`s from the chat window header via `LazyPopupContent` (`ChatView.xaml:131-273`). The YouTube popup reuses the DI-singleton VM while Twitch/Kick build fresh instances (KI-9).

| View | Kind | How it's reached | ViewModel | Documented in |
|---|---|---|---|---|
| [TwitchChatSettingsView.xaml](../../src/VyralBot.UI/Views/TwitchChatSettingsView.xaml) | Popup | Chat window header → Twitch quick-settings (`IsTwitchSettingsOpen`) | [TwitchChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/TwitchChatSettingsViewModel.cs) | [Chat Settings](../chat/chat-settings.md) |
| [KickChatSettingsView.xaml](../../src/VyralBot.UI/Views/KickChatSettingsView.xaml) | Popup | Chat window header → Kick quick-settings | [KickChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/KickChatSettingsViewModel.cs) | [Chat Settings](../chat/chat-settings.md) |
| [YoutubeChatSettingsView.xaml](../../src/VyralBot.UI/Views/YoutubeChatSettingsView.xaml) | Popup | Chat window header → YouTube quick-settings | [YoutubeChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/YoutubeChatSettingsViewModel.cs) | [Chat Settings](../chat/chat-settings.md) |
| [TiktokChatSettingsView.xaml](../../src/VyralBot.UI/Views/TiktokChatSettingsView.xaml) | Popup | Chat window header → TikTok quick-settings (decorative toggles — KI-17) | [TiktokChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/TiktokChatSettingsViewModel.cs) | [Chat Settings](../chat/chat-settings.md) |
| [DiscordChatSettingsView.xaml](../../src/VyralBot.UI/Views/DiscordChatSettingsView.xaml) | Popup | Chat window header → Discord quick-settings (decorative toggles — KI-17) | [DiscordChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/DiscordChatSettingsViewModel.cs) | [Chat Settings](../chat/chat-settings.md) |

---

## Settings sub-pages

Rendered inside SettingsView through nested `ContentControl`s that reuse MainWindow's implicit DataTemplates.

| View | Kind | How it's reached | ViewModel | Documented in |
|---|---|---|---|---|
| [VariablesView.xaml](../../src/VyralBot.UI/Views/VariablesView.xaml) | Page | Settings → **Variables** (`SettingsView.xaml:834`). No sidebar entry and no `NavigateToVariables` command exists (KI-18) | [VariablesViewModel.cs](../../src/VyralBot.UI/ViewModels/VariablesViewModel.cs) | [Variables](../chat-features/variables.md) |
| [WebServerSettingsView.xaml](../../src/VyralBot.UI/Views/WebServerSettingsView.xaml) | Page | Settings → **API & Webhooks** (`SettingsView.xaml:862`) | [WebServerSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/WebServerSettingsViewModel.cs) | [API, Web Server & ngrok](../settings/web-server-ngrok.md) |

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

---

## Dead UI inventory

Surfaces present in the source tree that no code path can show, plus orphaned files. Cross-referenced from the [known issues ledger](known-issues.md) (KI-18 unless noted).

| Surface | Why it's dead |
|---|---|
| [ErrorDialog.xaml](../../src/VyralBot.UI/Views/ErrorDialog.xaml) | No `new ErrorDialog` or DI registration anywhere in the solution — superseded by `CustomDialog` |
| [ProfileSelectorWindow.xaml](../../src/VyralBot.UI/Views/ProfileSelectorWindow.xaml) | Superseded by the inline MainWindow profile-selector overlay; only its own files reference it. Its dead VM ([ProfileSelectorViewModel.cs](../../src/VyralBot.UI/ViewModels/ProfileSelectorViewModel.cs), still DI-registered) is the only place with PIN-length validation (KI-20) |
| [DemoSetupView.xaml](../../src/VyralBot.UI/Views/DemoSetupView.xaml) | Superseded by `GoogleDemoSetupView`; referenced nowhere outside its own code-behind |
| [NgrokSetupView.xaml](../../src/VyralBot.UI/Views/NgrokSetupView.xaml) | Previous-generation ngrok UI, replaced by `NgrokSetupWizardView`; referenced nowhere. Also leaves `NgrokSetupView.xaml.backup` and `NgrokSetupView.xaml.old` in the tree |
| [GenericWebSetupView.xaml](../../src/VyralBot.UI/Views/GenericWebSetupView.xaml) | Standalone prototype of the web-setup layout, not a base class of the five platform wizards and referenced nowhere |
| [SettingsView.xaml.old](../../src/VyralBot.UI/Views/SettingsView.xaml.old), [MainWindow.xaml.broken](../../src/VyralBot.UI/MainWindow.xaml.broken), [MainWindow.xaml.new](../../src/VyralBot.UI/MainWindow.xaml.new) | Orphaned file copies, excluded from compilation by extension |

Dead navigation targets with no view behind them: sidebar **Events & Alerts** and **Browser Sources** buttons (KI-17 — browser sources are web pages served by the API, not a WPF view), and MainViewModel's generated `NavigateToTriggersCommand`/`NavigateToActionsCommand` (`MainViewModel.cs:734`, `:858`), which are bound nowhere and have no target views.

---

## See also

- [App Shell](../home/app-shell.md) — the sidebar, navigation model, and dead buttons in context
- [Command Catalog](command-catalog.md) / [Variables Catalog](variables-catalog.md) — the data behind the Commands and Variables pages
- [Database Reference](database-reference.md) — what each page persists
- [Known Issues Ledger](known-issues.md) — KI-6, KI-9, KI-17, KI-18, KI-20 cover the facade/dead surfaces listed above
