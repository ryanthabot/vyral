# Dashboard (Home)

> The Home page is your launch pad: a first-run **Get started** strip and a **Quick actions** bar (one-click streamer power tools) sit above the four big navigation cards and the Support section.

## Overview

Home is the page you land on when the app starts and the page you are returned to after switching [profiles](../getting-started/profiles.md). It is intentionally simple: four cards that take you to the four major areas of the app (Connections, Chat, Tools, Integrations), and a small Support list underneath.

Two things to know up front, so the page does not confuse you:

- **Live state lives in the Quick actions bar, not in widgets.** The bar's buttons enable/disable with their backends (Twitch connected, OBS engine ready) and the restream/TTS toggles reflect real engine state — but there are no follower/chat counters here. If you want to know what is connected, look at the [sidebar](app-shell.md) — Twitch and Kick entries appear there only while connected.
- **All three Support rows work.** "Documentation" opens the in-app documentation browser for these `AppDocs/` pages; "Discord" and "Ideas & Suggestions" open the URLs configured under Settings → Profile → **Support Links** (until you set them, clicking either shows a pointer to that settings card).

## User Guide

### Getting to Home

- Launch the app — Home is the initial page, shown even before you pick a profile.
- Click **Home** in the sidebar at any time.
- Any [profile switch](../getting-started/profiles.md) returns you to Home automatically.

### The Get started strip (first run)

A dismissible three-step guide that appears above the cards on a fresh install: **Connect a platform** (→ Platforms), **Set up alerts** (→ Events & Alerts), **Import a workflow template** (→ Automation's TEMPLATES panel). It hides itself permanently once you dismiss it (the × button — the choice persists in `ui-settings.json`) or as soon as any platform connects (Twitch, Kick, YouTube or Discord), and reappears never — every page it points to stays available in the sidebar.

### The Quick actions bar

One-click streamer power tools, added in 1.1.0:

| Button | What it does | Needs |
|---|---|---|
| **✂️ Clip** | Creates a Twitch clip of the current stream and copies its URL to your clipboard (falls back to showing the URL if the clipboard is locked). | Twitch connected |
| **⏺️ Save Replay** | Saves the OBS replay buffer's current contents as a highlight (the buffer must be active). | Embedded OBS engine ready |
| **💰 Ad Break 30s** | Runs a 30-second commercial. | Twitch connected |
| **📡 Start/Stop restream** | Starts or stops the FFmpeg restream fan-out using exactly the destinations and settings you saved on the [Restream page](../stream-tools/restream.md). | Restream stack (FFmpeg + saved destinations) |
| **🔊 TTS: on/off** | Mutes or unmutes text-to-speech (`!tts` and alert speech); muting also cancels current speech. | TTS service |

Buttons disable honestly when their backend is offline, and the tooltip on a disabled button says why. The result of each action (or its failure, in red) appears on a line under the row and fades out after 2 seconds. Nothing here is configurable on the page itself — the restream toggle reads the Restream page's saved session, and the TTS toggle writes the same `Tts.Enabled` setting the [Alert Settings page](../stream-tools/visual-alerts.md#text-to-speech-card) manages.

### The four cards

Each card is a large clickable tile. Click anywhere on it to navigate:

| Card | Subtitle | Where it goes |
|---|---|---|
| **Connections** | "Manage your platform connections" | The **Platforms** page — cards for Twitch, Kick, YouTube, TikTok, Discord, and OBS (OAuth wizards for Twitch/Kick/YouTube, a bot-token wizard for Discord; TikTok's card is a "Coming Soon" shell, and OBS is reached from the sidebar's **OBS** page, not here). See [Platforms Overview](../platforms/overview.md). |
| **Chat** | "Complete chat management for all platforms" | The **Chat Hub** — *not* the chat feed. The hub holds per-platform chat settings (event display, auto-replies, enable toggles) plus the **Moderator** chip. To read and send messages, use **Chat** in the sidebar instead ([Chat Window](../chat/chat-window.md)). |
| **Tools** | "Stream tools, chat features, and more" | The **Tools** hub — a second, larger card grid covering the same destinations as the sidebar's Stream Tools and Chat Features groups (Alerts, Currency, Commands, Event History, Audio, Sounds, Statistics, Conditional Rules, Giveaways, Polls, Watchtime). |
| **Integrations** | "Third-party integrations and bots" | The **Integrations** page. Every integration on it (BotGhost and similar) is marked "Coming soon" — the page is a placeholder. |

A fifth, full-width **Moderator** card sits in the second row and opens the [Moderator dashboard](../architecture/moderator-guide.md) (`NavigateToModeratorCommand`).

### The Support section

Three rows under a "Support" heading, all functional:

- **Documentation** — "Explore the official documentation for Vyral". Clicking it opens the in-app Documentation page: a browser for these very `AppDocs/` markdown files, with a navigation tree, search, back/forward history, and a read-only source-code viewer (see [Documentation & Contributing](documentation.md)).
- **Discord** — "Join the Vyral community!" Opens your Discord invite URL in the system browser. The URL is configured under Settings → Profile → **Support Links** (Configs key `Support.DiscordUrl`); if it has not been set yet, clicking the row shows a MessageBox pointing you to that settings card.
- **Ideas & Suggestions** — "Share and discuss ideas for new features or improvements to Vyral". Same behavior, driven by the `Support.IdeasUrl` key from the same settings card.

## How It Works (Developer)

### The Home UI lives inside MainWindow.xaml

There is **no `DashboardView.xaml`**. The entire Home page is an inline `DataTemplate` keyed on `DashboardViewModel` inside the content host's resources — `MainWindow.xaml:613-1297`. This is the same DataTemplate-mapping mechanism every page uses (see [App Shell & Navigation](app-shell.md)); Home is just the one case where the template contains a full page layout instead of a single `<local:SomeView/>`. The Get started strip and the Quick actions bar are cards at the top of that template (with the strip auto-collapsing via a `MultiBinding` on the dismiss flag + the four platform-connection flags).

The cards are `materialDesign:Card` elements with `Cursor="Hand"` and a `MouseBinding` on `LeftClick` that binds to the **Window's** DataContext (i.e. `MainViewModel`) via `RelativeSource AncestorType=Window`:

- Connections → `NavigateToPlatformsCommand`
- Chat → `NavigateToChatHubCommand`
- Tools → `NavigateToToolsCommand`
- Integrations → `NavigateToIntegrationsCommand`
- Documentation row → `NavigateToDocumentationCommand`
- Discord row → `OpenDiscordLinkCommand` (MainViewModel) — opens the configured `Support.DiscordUrl` in the system browser via the shared `OpenSupportLinkAsync` helper
- Ideas row → `OpenIdeasLinkCommand` — same helper, keyed on `Support.IdeasUrl`

Both support-row commands read their URL from the `Configs` table at click time; when the key is unset the helper shows a MessageBox directing the user to Settings → Profile → **Support Links**.

### DashboardViewModel owns the strip + Quick actions state

[DashboardViewModel.cs](../../src/VyralBot.UI/ViewModels/DashboardViewModel.cs) carries the old counter placeholders (`ActiveTriggerCount` / `TotalActionCount` / `ConnectedPlatformCount` / `Status` — still unbound) plus everything the strip and bar need:

- **Get started state** — `IsGettingStartedDismissed`, loaded from `ui-settings.json` in the constructor and persisted by `DismissGettingStartedCommand` (which reloads preferences fresh before saving so a dismiss never clobbers theme prefs saved elsewhere in the session).
- **Quick-action availability** — `CanQuickTwitch`, `CanQuickReplay`, `CanQuickRestream`, `CanQuickTts`. Every quick-action service dependency is **nullable in the constructor**: a missing DI registration disables the matching button instead of breaking Home. `CanQuickReplay` needs *both* `ObsOutputService` and `ObsEngine`; `CanQuickRestream` needs *all four* of `RestreamEngine` + `RestreamFfmpegLocator` + `IDataService` + `IProfileService`; clip/ad-break ride `CanQuickTwitch`; the TTS toggle rides `CanQuickTts`.
- **Live engine state** — `IsObsEngineReady` and `IsRestreamRunning` mirror the embedded OBS engine and the restream session through their `StatusChanged` events (each handler hops back to the captured UI dispatcher); `RestreamActionLabel` swaps Start/Stop with engine state; `IsTtsEnabled` + `TtsActionLabel` track the persisted `Tts.Enabled`.
- **Result feedback** — `QuickActionStatus` / `IsQuickActionStatusVisible` / `IsQuickActionStatusError`, driven by `ShowQuickStatus` with a 2-second `DispatcherTimer` auto-hide (the fade itself is a XAML storyboard).

The commands: `ClipCommand` (`CreateClipUrlAsync` + clipboard), `SaveReplayCommand` (`SaveReplayAsync`), `StartAdBreakCommand` (a 30 s commercial), `ToggleRestreamCommand` (stop goes straight to `StopAsync`; start rebuilds the Restream page's saved session — FFmpeg via the locator, destinations from `restream_destinations.json` in the profile credentials folder, source settings from the `Restream.Settings` config row — and starts it), and `ToggleTtsMuteCommand` (flips `Tts.Enabled`, cancels speech on mute).

The engine event subscriptions use named handlers, and the VM is an app-lifetime singleton that is never disposed on navigation, so its subscriptions intentionally live as long as the engine singletons — no orphaned-handler buildup. Destination stream keys are never logged.

The VM is registered as a DI singleton (`App.xaml.cs:382`) and injected into `MainViewModel`, so Home is always the initial page state, even before a profile is chosen.

### Navigation behavior around Home

- `NavigateToHome` (`MainViewModel.cs:1131`) is a no-op if already Home; otherwise it runs the standard navigation pipeline (setup-wizard guard, null the current VM, dispose others, GC), additionally resets the Platforms page's sub-view flags so the next visit starts at platform selection, and sets the title to `"Vyral - Home"`.
- Home is **never disposed** on navigation away — `DisposeNonEssentialViewModelsAsync` is called with `excludeViewModel: _dashboardViewModel` (`MainViewModel.cs:1163`), keeping Dashboard (and Chat) alive for performance.
- After a profile switch, if any other page is showing, `MainViewModel` fire-and-forget navigates back to Home to avoid stale cross-profile data (`MainViewModel.cs:262`).

### The hub destinations

- **Tools hub**: [ToolsViewModel.cs](../../src/VyralBot.UI/ViewModels/ToolsViewModel.cs) is an *empty class* — it exists only as a DataTemplate key. [ToolsView.xaml](../../src/VyralBot.UI/Views/ToolsView.xaml) is a static grid of cards whose `MouseBinding`s call `MainViewModel` commands through `RelativeSource AncestorType=Window` — i.e. the Tools hub is a second, larger copy of the sidebar's grouped navigation.
- **Integrations hub**: [IntegrationsViewModel.cs](../../src/VyralBot.UI/ViewModels/IntegrationsViewModel.cs) is likewise empty, and [IntegrationsView.xaml](../../src/VyralBot.UI/Views/IntegrationsView.xaml) lists third-party bots all marked "Coming soon" with no click actions.
- **Chat Hub**: [ChatHubViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatHubViewModel.cs) hosts the five per-platform chat-settings views — covered in [Chat Settings](../chat/chat-settings.md).
- **Documentation page**: [DocumentationViewModel.cs](../../src/VyralBot.UI/ViewModels/DocumentationViewModel.cs) + [DocumentationView.xaml](../../src/VyralBot.UI/Views/DocumentationView.xaml) are the in-app browser for these `AppDocs/` pages — navigation tree, search, history, and a code viewer — described in [Documentation & Contributing](documentation.md).

## Files & Code

| File | Role |
|------|-------------|
| [MainWindow.xaml](../../src/VyralBot.UI/MainWindow.xaml) | The entire Home page UI — inline `DataTemplate` at `MainWindow.xaml:613-1297` (Get started strip, Quick actions bar, cards, Support rows) |
| [MainViewModel.cs](../../src/VyralBot.UI/ViewModels/MainViewModel.cs) | All card commands, `NavigateToHome`, Home lifecycle |
| [DashboardViewModel.cs](../../src/VyralBot.UI/ViewModels/DashboardViewModel.cs) | Get started + Quick actions state and commands |
| [UiPreferencesService.cs](../../src/VyralBot.UI/Services/UiPreferencesService.cs) | Persists the strip's dismiss in `ui-settings.json` |
| [RestreamEngine.cs](../../src/VyralBot.Integrations/Restream/RestreamEngine.cs) | Quick-action restream toggle target |
| [ToolsViewModel.cs](../../src/VyralBot.UI/ViewModels/ToolsViewModel.cs) | Empty VM used as a navigation key for the Tools hub |
| [ToolsView.xaml](../../src/VyralBot.UI/Views/ToolsView.xaml) | Tools hub — static card grid bound to `MainViewModel` commands |
| [IntegrationsViewModel.cs](../../src/VyralBot.UI/ViewModels/IntegrationsViewModel.cs) | Empty VM for the Integrations hub |
| [IntegrationsView.xaml](../../src/VyralBot.UI/Views/IntegrationsView.xaml) | Integrations hub — all items "Coming soon" |
| [ChatHubViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatHubViewModel.cs) | Chat Hub (the "Chat" card destination) |
| [DocumentationViewModel.cs](../../src/VyralBot.UI/ViewModels/DocumentationViewModel.cs) | The in-app docs browser's ViewModel (tree, search, history, code viewer) |
| [DocumentationView.xaml](../../src/VyralBot.UI/Views/DocumentationView.xaml) | The Documentation page — browser for these `AppDocs/` markdown files |

How a card routes a click to the window's ViewModel (`MainWindow.xaml:956-963`):

```xml
<!-- Connections Card -->
<materialDesign:Card Grid.Column="0"
                    Padding="24"
                    Background="{StaticResource CardBackgroundBrush}"
                    Cursor="Hand">
    <materialDesign:Card.InputBindings>
        <MouseBinding Command="{Binding DataContext.NavigateToPlatformsCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                      MouseAction="LeftClick"/>
    </materialDesign:Card.InputBindings>
    <!-- icon + "Connections" + subtitle ... -->
</materialDesign:Card>
```

## Data & Settings

The page itself persists **one thing**: the Get started strip's dismissal (`HomeGettingStartedDismissed` in `ui-settings.json`, via `UiPreferencesService`). The Quick actions bar reads and writes other features' existing state rather than owning any: the restream toggle uses the [Restream page's](../stream-tools/restream.md) saved session (`Restream.Settings` config row + `restream_destinations.json` in the profile credentials folder), and the TTS toggle flips the same `Tts.Enabled` key the Alert Settings page manages. Everything the cards navigate *to* has its own persistence, described on those pages (e.g. [Database Reference](../reference/database-reference.md)).

## Status & Known Issues

**Works**: the Get started strip (auto-hide on connect, persisted dismiss), all five quick actions against their live backends, all four cards navigate correctly; all three Support rows act (Documentation opens the docs browser; Discord and Ideas open the configured links); Home is restored after profile switches and never leaks memory (it is kept alive deliberately as a lightweight singleton).

**Dead or stubbed** — full list in the [known issues ledger](../reference/known-issues.md):

- **The old counter placeholders remain unbound** — `ActiveTriggerCount`/`TotalActionCount`/`ConnectedPlatformCount`/`Status` are still zeroed placeholders; a future overview widget would bind them.
- **Integrations page is a placeholder** — every third-party bot listed is "Coming soon".
- The "Chat" card goes to the **Chat Hub** (settings), not the chat feed — arguably surprising naming, though the behavior is intentional.
