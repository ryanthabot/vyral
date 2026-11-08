# Dashboard View (Home)

## Overview
XAML view for the Home page, rendered inline in `MainWindow.xaml` via a
`DataTemplate DataType="{x:Type vm:DashboardViewModel}"` (Home content area).
Shows the first-run "Get started" strip, the Quick Actions bar, main content
cards (Connections, Chat, Tools, Integrations, Moderator) and the Support
section.

## Quick Actions bar (one-click streamer power tools)
Row of raised buttons under the "Get started" strip / above the cards, plus a
result line that fades in on action and out again after 2 s (storyboards on
`IsQuickActionStatusVisible`; green success / red error text).
- **✂️ Clip** — `ClipCommand`; enabled only while MainViewModel's live
  `IsTwitchConnected` flag is true (window-RelativeSource binding, same
  source as the strip); tooltip swaps to "Twitch is not connected…" when off.
- **⏺️ Save Replay** — `SaveReplayCommand`; enabled while the embedded OBS
  engine is Ready (`IsObsEngineReady`, fed by `ObsEngine.StatusChanged`);
  disabled tooltip explains the OBS page prerequisite.
- **💰 Ad Break 30s** — `StartAdBreakCommand`; same Twitch gating as Clip.
- **📡 Start/Stop restream** — `ToggleRestreamCommand`; label swaps with
  `IsRestreamRunning` (from `RestreamEngine.StatusChanged`).
- **🔊/🔇 TTS toggle** — `ToggleTtsMuteCommand`; label shows the persisted
  mute state.
- All buttons use `ToolTipService.ShowOnDisabled=True` (set in the shared
  `QuickActionButton` style) so the offline reason stays readable,
  `AutomationProperties.Name`, and are keyboard-focusable like the cards
  above them.

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
