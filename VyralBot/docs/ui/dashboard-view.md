# Dashboard View (Home)

## Overview
XAML view for the Home page, rendered inline in `MainWindow.xaml` via a
`DataTemplate DataType="{x:Type vm:DashboardViewModel}"` (Home content area).
Shows the first-run "Get started" strip, main content cards (Connections,
Chat, Tools, Integrations, Moderator) and the Support section.

## Get started strip (first-run onboarding)
- Dismissible 3-step card shown above the main cards:
  1. **Connect a platform** → `NavigateToPlatformsCommand` (Platforms hub / "Connections" card)
  2. **Set up alerts** → `NavigateToAlertSettingsCommand` ("Events & Alerts" nav item)
  3. **Import a workflow template** → `NavigateToAutomationCommand` (TEMPLATES button on the Automation page)
- **Visibility:** `MultiBinding` + `AnyTrueToCollapsedConverter` over
  `DashboardViewModel.IsGettingStartedDismissed` and MainViewModel's
  `IsTwitchConnected` / `IsKickConnected` / `IsYouTubeConnected` /
  `IsDiscordConnected` (bound via `RelativeSource AncestorType=Window`) —
  collapses when the strip is dismissed OR any platform is connected.
- **Dismiss ✕** persists via `UiPreferencesService` (`HomeGettingStartedDismissed=true`
  in ui-settings.json, see `UiPreferences`).

## Source Files
- **Note:** There is no dedicated DashboardView.xaml. The dashboard is rendered
  inline in MainWindow via a DataTemplate.
- **ViewModel:** `src/VyralBot.UI/ViewModels/DashboardViewModel.cs`
- **Converter:** `src/VyralBot.UI/Converters/AnyTrueToCollapsedConverter.cs`

## All Files
| File | Description |
|------|-------------|
| `src/VyralBot.UI/ViewModels/DashboardViewModel.cs` | Dashboard ViewModel |
| `src/VyralBot.UI/Converters/AnyTrueToCollapsedConverter.cs` | Any-true → Collapsed multi converter |
