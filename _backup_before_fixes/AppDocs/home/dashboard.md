# Dashboard (Home)

> The Home page is a static launcher — four big navigation cards plus a Support section — with no live data, because its ViewModel is an unfinished stub.

## Overview

Home is the page you land on when the app starts and the page you are returned to after switching [profiles](../getting-started/profiles.md). It is intentionally simple: four cards that take you to the four major areas of the app (Connections, Chat, Tools, Integrations), and a small Support list underneath.

Two things to know up front, so the page does not confuse you:

- **It shows no statistics.** There are no counters, statuses, or live widgets on this page. If you want to know what is connected, look at the [sidebar](app-shell.md) — Twitch and Kick entries appear there only while connected.
- **Two of the three Support rows do nothing.** "Discord" and "Ideas & Suggestions" render as clickable rows with a hand cursor and a chevron, but they have no command attached — clicking them does nothing. Only "Documentation" works, and the page it opens is a placeholder.

## User Guide

### Getting to Home

- Launch the app — Home is the initial page, shown even before you pick a profile.
- Click **Home** in the sidebar at any time.
- Any [profile switch](../getting-started/profiles.md) returns you to Home automatically.

### The four cards

Each card is a large clickable tile. Click anywhere on it to navigate:

| Card | Subtitle | Where it goes |
|---|---|---|
| **Connections** | "Manage your platform connections" | The **Platforms** page — cards for Twitch, Kick, YouTube, TikTok, Discord, and OBS, each opening an OAuth setup wizard. See [Platforms Overview](../platforms/overview.md). |
| **Chat** | "Complete chat management for all platforms" | The **Chat Hub** — *not* the chat feed. The hub holds per-platform chat settings (event display, auto-replies, enable toggles). To read and send messages, use **Chat** in the sidebar instead ([Chat Window](../chat/chat-window.md)). |
| **Tools** | "Stream tools, chat features, and more" | The **Tools** hub — a second, larger card grid covering the same destinations as the sidebar's Stream Tools and Chat Features groups (Alerts, Currency, Commands, Event History, Audio, Sounds, Statistics, Conditional Rules, Giveaways, Polls, Watchtime). |
| **Integrations** | "Third-party integrations and bots" | The **Integrations** page. Every integration on it (BotGhost and similar) is marked "Coming soon" — the page is a placeholder. |

### The Support section

Three rows under a "Support" heading:

- **Documentation** — "Explore the official documentation for VyralBot". Clicking it opens the in-app Documentation page. In the current build that page is a stub: a "← BACK TO HOME" button and a card that reads *"Documentation Coming Soon — The VyralBot documentation is currently being written. Check back soon!"* There is no browser, no link to this documentation, and no content. (These `AppDocs/` pages are what that button is meant to host one day.)
- **Discord** — "Join the VyralBot community!" Dead: hand cursor and chevron, but no click handler. Nothing happens.
- **Ideas & Suggestions** — "Share and discuss ideas for new features or improvements to VyralBot". Dead in the same way.

## How It Works (Developer)

### The Home UI lives inside MainWindow.xaml

There is **no `DashboardView.xaml`**. The entire Home page is an inline `DataTemplate` keyed on `DashboardViewModel` inside the content host's resources — `MainWindow.xaml:505-769`. This is the same DataTemplate-mapping mechanism every page uses (see [App Shell & Navigation](app-shell.md)); Home is just the one case where the template contains a full page layout instead of a single `<local:SomeView/>`.

The cards are `materialDesign:Card` elements with `Cursor="Hand"` and a `MouseBinding` on `LeftClick` that binds to the **Window's** DataContext (i.e. `MainViewModel`) via `RelativeSource AncestorType=Window`:

- Connections → `NavigateToPlatformsCommand`
- Chat → `NavigateToChatHubCommand`
- Tools → `NavigateToToolsCommand`
- Integrations → `NavigateToIntegrationsCommand`
- Documentation row → `NavigateToDocumentationCommand`

The Discord and Ideas rows are plain `Border` elements with `Cursor="Hand"` and a chevron icon but **no `InputBindings`** — that is the entire reason they are dead.

### DashboardViewModel is a stub

[DashboardViewModel.cs](../../src/VyralBot.UI/ViewModels/DashboardViewModel.cs) declares four observable properties — `ActiveTriggerCount`, `TotalActionCount`, `ConnectedPlatformCount`, `Status` — and a `LoadDashboard()` that hard-zeros all of them under a `// TODO: Load actual data from services` comment. **None of these properties is bound anywhere in XAML.** The Home page renders zero live data by construction; it is purely a static navigation launcher.

The VM is registered as a DI singleton (`App.xaml.cs:186`) and injected into `MainViewModel`, which sets `CurrentViewModel = dashboardViewModel` in its constructor (`MainViewModel.cs:149`) — so Home is always the initial page state, even before a profile is chosen.

### Navigation behavior around Home

- `NavigateToHome` (`MainViewModel.cs:696-732`) is a no-op if already Home; otherwise it runs the standard navigation pipeline (setup-wizard guard, null the current VM, dispose others, GC), additionally resets the Platforms page's sub-view flags so the next visit starts at platform selection, and sets the title to `"VyralBot - Home"`.
- Home is **never disposed** on navigation away — `DisposeNonEssentialViewModelsAsync` explicitly keeps Dashboard and Chat alive for performance (`MainViewModel.cs:1642-1644`).
- After a profile switch, if any other page is showing, `MainViewModel` fire-and-forget navigates back to Home to avoid stale cross-profile data (`MainViewModel.cs:198-207`).

### The hub destinations

- **Tools hub**: [ToolsViewModel.cs](../../src/VyralBot.UI/ViewModels/ToolsViewModel.cs) is an *empty class* — it exists only as a DataTemplate key. [ToolsView.xaml](../../src/VyralBot.UI/Views/ToolsView.xaml) is a static grid of cards whose `MouseBinding`s call `MainViewModel` commands through `RelativeSource AncestorType=Window` — i.e. the Tools hub is a second, larger copy of the sidebar's grouped navigation.
- **Integrations hub**: [IntegrationsViewModel.cs](../../src/VyralBot.UI/ViewModels/IntegrationsViewModel.cs) is likewise empty, and [IntegrationsView.xaml](../../src/VyralBot.UI/Views/IntegrationsView.xaml) lists third-party bots all marked "Coming soon" with no click actions.
- **Chat Hub**: [ChatHubViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatHubViewModel.cs) hosts the five per-platform chat-settings views — covered in [Chat Settings](../chat/chat-settings.md).
- **Documentation page**: [DocumentationViewModel.cs](../../src/VyralBot.UI/ViewModels/DocumentationViewModel.cs) is an empty class and [DocumentationView.xaml](../../src/VyralBot.UI/Views/DocumentationView.xaml) is a purely static placeholder (back button + "Coming Soon" card).

## Files & Code

| File | Role |
|---|---|
| [MainWindow.xaml](../../src/VyralBot.UI/MainWindow.xaml) | The entire Home page UI — inline `DataTemplate` at `MainWindow.xaml:505-769` (cards) and `641-766` (Support rows) |
| [MainViewModel.cs](../../src/VyralBot.UI/ViewModels/MainViewModel.cs) | All card commands, `NavigateToHome`, Home lifecycle |
| [DashboardViewModel.cs](../../src/VyralBot.UI/ViewModels/DashboardViewModel.cs) | Stub ViewModel — zeroed counters, never bound |
| [ToolsViewModel.cs](../../src/VyralBot.UI/ViewModels/ToolsViewModel.cs) | Empty VM used as a navigation key for the Tools hub |
| [ToolsView.xaml](../../src/VyralBot.UI/Views/ToolsView.xaml) | Tools hub — static card grid bound to `MainViewModel` commands |
| [IntegrationsViewModel.cs](../../src/VyralBot.UI/ViewModels/IntegrationsViewModel.cs) | Empty VM for the Integrations hub |
| [IntegrationsView.xaml](../../src/VyralBot.UI/Views/IntegrationsView.xaml) | Integrations hub — all items "Coming soon" |
| [ChatHubViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatHubViewModel.cs) | Chat Hub (the "Chat" card destination) |
| [DocumentationViewModel.cs](../../src/VyralBot.UI/ViewModels/DocumentationViewModel.cs) | Empty VM for the Documentation page |
| [DocumentationView.xaml](../../src/VyralBot.UI/Views/DocumentationView.xaml) | "Documentation Coming Soon" placeholder page |

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

**Works**: all four cards navigate correctly; the Documentation row opens the Documentation page; Home is restored after profile switches and never leaks memory (it is kept alive deliberately as a lightweight singleton).

**Dead or stubbed** — full list in the [known issues ledger](../reference/known-issues.md):

- **Discord row and Ideas & Suggestions row are dead buttons** — rendered as clickable (`Cursor="Hand"`, chevron) with no command binding (`MainWindow.xaml:692-728`, `731-765`).
- **Documentation page is a "Coming Soon" placeholder** — empty ViewModel, static view, no link to any real documentation content.
- **DashboardViewModel is a stub** — its counter properties are hard-zeroed by a TODO and bound nowhere; Home can never show live data until this is implemented.
- **Integrations page is a placeholder** — every third-party bot listed is "Coming soon".
- The "Chat" card goes to the **Chat Hub** (settings), not the chat feed — arguably surprising naming, though the behavior is intentional.
