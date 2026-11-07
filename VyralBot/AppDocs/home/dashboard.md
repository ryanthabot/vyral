# Dashboard (Home)

> The Home page is a static launcher — four big navigation cards plus a Support section — with no live data, because its ViewModel is an unfinished stub.

## Overview

Home is the page you land on when the app starts and the page you are returned to after switching [profiles](../getting-started/profiles.md). It is intentionally simple: four cards that take you to the four major areas of the app (Connections, Chat, Tools, Integrations), and a small Support list underneath.

Two things to know up front, so the page does not confuse you:

- **It shows no statistics.** There are no counters, statuses, or live widgets on this page. If you want to know what is connected, look at the [sidebar](app-shell.md) — Twitch and Kick entries appear there only while connected.
- **All three Support rows work.** "Documentation" opens the in-app documentation browser for these `AppDocs/` pages; "Discord" and "Ideas & Suggestions" open the URLs configured under Settings → Profile → **Support Links** (until you set them, clicking either shows a pointer to that settings card).

## User Guide

### Getting to Home

- Launch the app — Home is the initial page, shown even before you pick a profile.
- Click **Home** in the sidebar at any time.
- Any [profile switch](../getting-started/profiles.md) returns you to Home automatically.

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

There is **no `DashboardView.xaml`**. The entire Home page is an inline `DataTemplate` keyed on `DashboardViewModel` inside the content host's resources — `MainWindow.xaml:522-792`. This is the same DataTemplate-mapping mechanism every page uses (see [App Shell & Navigation](app-shell.md)); Home is just the one case where the template contains a full page layout instead of a single `<local:SomeView/>`.

The cards are `materialDesign:Card` elements with `Cursor="Hand"` and a `MouseBinding` on `LeftClick` that binds to the **Window's** DataContext (i.e. `MainViewModel`) via `RelativeSource AncestorType=Window`:

- Connections → `NavigateToPlatformsCommand`
- Chat → `NavigateToChatHubCommand`
- Tools → `NavigateToToolsCommand`
- Integrations → `NavigateToIntegrationsCommand`
- Documentation row → `NavigateToDocumentationCommand`
- Discord row → `OpenDiscordLinkCommand` (MainViewModel) — opens the configured `Support.DiscordUrl` in the system browser via the shared `OpenSupportLinkAsync` helper
- Ideas row → `OpenIdeasLinkCommand` — same helper, keyed on `Support.IdeasUrl`

Both support-row commands read their URL from the `Configs` table at click time; when the key is unset the helper shows a MessageBox directing the user to Settings → Profile → **Support Links**.

### DashboardViewModel is a stub

[DashboardViewModel.cs](../../src/VyralBot.UI/ViewModels/DashboardViewModel.cs) declares four observable properties — `ActiveTriggerCount`, `TotalActionCount`, `ConnectedPlatformCount`, `Status` — and a `LoadDashboard()` that hard-zeros all of them under a `// TODO: Load actual data from services` comment. **None of these properties is bound anywhere in XAML.** The Home page renders zero live data by construction; it is purely a static navigation launcher.

The VM is registered as a DI singleton (`App.xaml.cs:186`) and injected into `MainViewModel`, which sets `CurrentViewModel = dashboardViewModel` in its constructor (`MainViewModel.cs:149`) — so Home is always the initial page state, even before a profile is chosen.

### Navigation behavior around Home

- `NavigateToHome` (`MainViewModel.cs:696-732`) is a no-op if already Home; otherwise it runs the standard navigation pipeline (setup-wizard guard, null the current VM, dispose others, GC), additionally resets the Platforms page's sub-view flags so the next visit starts at platform selection, and sets the title to `"Vyral - Home"`.
- Home is **never disposed** on navigation away — `DisposeNonEssentialViewModelsAsync` explicitly keeps Dashboard and Chat alive for performance (`MainViewModel.cs:1642-1644`).
- After a profile switch, if any other page is showing, `MainViewModel` fire-and-forget navigates back to Home to avoid stale cross-profile data (`MainViewModel.cs:198-207`).

### The hub destinations

- **Tools hub**: [ToolsViewModel.cs](../../src/VyralBot.UI/ViewModels/ToolsViewModel.cs) is an *empty class* — it exists only as a DataTemplate key. [ToolsView.xaml](../../src/VyralBot.UI/Views/ToolsView.xaml) is a static grid of cards whose `MouseBinding`s call `MainViewModel` commands through `RelativeSource AncestorType=Window` — i.e. the Tools hub is a second, larger copy of the sidebar's grouped navigation.
- **Integrations hub**: [IntegrationsViewModel.cs](../../src/VyralBot.UI/ViewModels/IntegrationsViewModel.cs) is likewise empty, and [IntegrationsView.xaml](../../src/VyralBot.UI/Views/IntegrationsView.xaml) lists third-party bots all marked "Coming soon" with no click actions.
- **Chat Hub**: [ChatHubViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatHubViewModel.cs) hosts the five per-platform chat-settings views — covered in [Chat Settings](../chat/chat-settings.md).
- **Documentation page**: [DocumentationViewModel.cs](../../src/VyralBot.UI/ViewModels/DocumentationViewModel.cs) + [DocumentationView.xaml](../../src/VyralBot.UI/Views/DocumentationView.xaml) are the in-app browser for these `AppDocs/` pages — navigation tree, search, history, and a code viewer — described in [Documentation & Contributing](documentation.md).

## Files & Code

| File | Role |
|---|---|
| [MainWindow.xaml](../../src/VyralBot.UI/MainWindow.xaml) | The entire Home page UI — inline `DataTemplate` at `MainWindow.xaml:522-792` (cards) and `641-766` (Support rows) |
| [MainViewModel.cs](../../src/VyralBot.UI/ViewModels/MainViewModel.cs) | All card commands, `NavigateToHome`, Home lifecycle |
| [DashboardViewModel.cs](../../src/VyralBot.UI/ViewModels/DashboardViewModel.cs) | Stub ViewModel — zeroed counters, never bound |
| [ToolsViewModel.cs](../../src/VyralBot.UI/ViewModels/ToolsViewModel.cs) | Empty VM used as a navigation key for the Tools hub |
| [ToolsView.xaml](../../src/VyralBot.UI/Views/ToolsView.xaml) | Tools hub — static card grid bound to `MainViewModel` commands |
| [IntegrationsViewModel.cs](../../src/VyralBot.UI/ViewModels/IntegrationsViewModel.cs) | Empty VM for the Integrations hub |
| [IntegrationsView.xaml](../../src/VyralBot.UI/Views/IntegrationsView.xaml) | Integrations hub — all items "Coming soon" |
| [ChatHubViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatHubViewModel.cs) | Chat Hub (the "Chat" card destination) |
| [DocumentationViewModel.cs](../../src/VyralBot.UI/ViewModels/DocumentationViewModel.cs) | The in-app docs browser's ViewModel (tree, search, history, code viewer) |
| [DocumentationView.xaml](../../src/VyralBot.UI/Views/DocumentationView.xaml) | The Documentation page — browser for these `AppDocs/` markdown files |

The whole ViewModel — this is genuinely all of it:

```csharp
public partial class DashboardViewModel : ViewModelBase
{
    [ObservableProperty]
    private int _activeTriggerCount;

    [ObservableProperty]
    private int _totalActionCount;

    [ObservableProperty]
    private int _connectedPlatformCount;

    [ObservableProperty]
    private string _status = "Ready";

    public DashboardViewModel()
    {
        LoadDashboard();
    }

    private void LoadDashboard()
    {
        // TODO: Load actual data from services
        ActiveTriggerCount = 0;
        TotalActionCount = 0;
        ConnectedPlatformCount = 0;
    }
}
```

How a card routes a click to the window's ViewModel (`MainWindow.xaml:520-527`):

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

The Dashboard persists **nothing** and reads nothing. Its only state is `MainViewModel.CurrentViewModel` (which page is showing) and the window `Title` string — both transient, both owned by the [app shell](app-shell.md). Everything the cards navigate *to* has its own persistence, described on those pages (e.g. [Database Reference](../reference/database-reference.md)).

## Status & Known Issues

**Works**: all four cards navigate correctly; all three Support rows act (Documentation opens the docs browser; Discord and Ideas open the configured links); Home is restored after profile switches and never leaks memory (it is kept alive deliberately as a lightweight singleton).

**Dead or stubbed** — full list in the [known issues ledger](../reference/known-issues.md):

- **DashboardViewModel is a stub** — its counter properties are hard-zeroed by a TODO and bound nowhere; Home can never show live data until this is implemented.
- **Integrations page is a placeholder** — every third-party bot listed is "Coming soon".
- The "Chat" card goes to the **Chat Hub** (settings), not the chat feed — arguably surprising naming, though the behavior is intentional.
