# App Shell & Navigation

> The main window's custom chrome (title bar, sidebar) and the memory-obsessed navigation machinery that swaps every page in and out of one content host.

## Overview

VyralBot runs in a single borderless window ([MainWindow.xaml](../../src/VyralBot.UI/MainWindow.xaml), 1164 lines) with a hand-drawn title bar, a fixed 200px sidebar, and one content area. There are no separate windows per feature (the profile selector and dialogs are overlays inside this window) — every page you can visit is a WPF view swapped into a single `ContentControl`, chosen by which ViewModel is currently assigned to `MainViewModel.CurrentViewModel`.

Two consequences of that design are worth knowing as a user:

- **Page switches have a tiny cost.** After most navigation the app deliberately forces a layout flush, waits 100 ms, and runs a compacting garbage collection. A brief pause when changing pages is normal and intentional.
- **The sidebar is the map.** Because the Home page is just a launcher (see [Dashboard](dashboard.md)), the sidebar is where you actually move around — and a few of its buttons are dead (listed below).

## User Guide

### Title bar

The window has no native Windows chrome — no standard caption, no min/max/close in the usual place. Instead, at the very top:

- **VyralBot logo + "VyralBot" label** on the left. This label is hard-coded; it never changes.
- **Minimize / Maximize / Close** flat buttons on the right. The close button turns red when you hover it.
- **Drag** anywhere on the empty title bar to move the window.
- **Double-click** the title bar to toggle maximize/restore (same as the maximize button).
- Window edges still resize (a 5px invisible resize border is configured).

The *operating system's* taskbar text for the window is bound and does change: `"VyralBot (<profile display name>)"` after a profile loads, then each navigation overwrites it with the page name (e.g. `"VyralBot - Home"`, `"VyralBot - Chat"`). The in-window label stays "VyralBot" regardless.

### Sidebar

A fixed 200px column, scrollable, organized as:

**Top-level**

- **Home** — the [Dashboard](dashboard.md).
- **Chat** — the live chat feed ([Chat Window](../chat/chat-window.md)).

**Platforms** (collapsible group)

- **Twitch** and **Kick** entries appear **only while that platform is connected** (they vanish when you disconnect). Each opens that platform's dashboard page with a live stream preview.
- There is deliberately no YouTube entry here, even though YouTube connections work — see [Status & Known Issues](#status--known-issues).

**Chat Features** (collapsible group)

- **Events & Alerts** — dead button, no action.
- **Currency & Rewards** — the currency economy.
- **Commands** — command management.
- **Event History** — past-event browser.

**Stream Tools** (collapsible group)

- **Browser Sources** — dead button, no action.
- **Audio Settings**, **Sounds Management**, **Statistics**, **Visual Alerts**, **Conditional Rules**, **Giveaways**, **Polls**, **Watchtime** — each opens its own page.

**Bottom (always visible)**

- **Settings** — the Settings page.
- **Event Testing** — developer tool that publishes synthetic events onto the real event bus ([Event Testing](../chat-features/event-testing.md)).
- **Demo Wizard** — a 4-step Google OAuth demo of the reusable wizard framework. It connects nothing; it demonstrates the wizard component used by the real platform setup wizards.
- **Help** — opens the in-app [Documentation](../index.md) browser (the built-in docs viewer shipped with the app).

Collapsible groups expand/collapse with a chevron toggle and start **collapsed** each launch.

### What happens when you navigate

For almost every destination the same sequence runs:

1. If a Twitch or Kick **setup wizard is mid-flow** on the Platforms page, a confirmation dialog appears: cancel to stay, or confirm to abandon and reset the wizard.
2. The current page is detached and the previous pages' view models are disposed.
3. The app forces a UI layout pass, waits ~100 ms, and runs a blocking compacting garbage collection — this is the small pause you feel.
4. The target page's view model is (re-)initialized and its view is created fresh.

Two pages behave differently: **Chat** and **Home** keep their view models alive permanently (created once, reused every visit) precisely to avoid that rebuild cost on the two most-visited pages.

### Single instance

Only one VyralBot process can run at a time. Launching a second copy shows "VyralBot is already running." and exits immediately — so you cannot run two [profiles](../getting-started/profiles.md) side by side.

## How It Works (Developer)

### Window chrome

The window declares `WindowStyle="None"`, `AllowsTransparency="True"`, a transparent background, and a `WindowChrome` with `CaptionHeight=32`, `ResizeBorderThickness=5`, `CornerRadius=8` (`MainWindow.xaml:15-31`). Everything below the title bar sits inside a rounded 8px `Border`. The title bar is defined at `MainWindow.xaml:52-116`; its buttons call plain click handlers in [MainWindow.xaml.cs](../../src/VyralBot.UI/MainWindow.xaml.cs), and dragging/maximize logic is `TitleBar_MouseLeftButtonDown` (`MainWindow.xaml.cs:31-43`).

### View-model-first navigation

The content area is a single `ContentControl Content="{Binding CurrentViewModel}"` whose resources map ViewModel **types** to Views via `DataTemplate`s (`MainWindow.xaml:502-905`) — e.g. `<DataTemplate DataType="{x:Type vm:SettingsViewModel}"><local:SettingsView/></DataTemplate>`. WPF instantiates the view lazily when the ViewModel is set; this is the app's lazy-loading mechanism, and the Home page is the one template that inlines an entire page instead of referencing a view.

The navigation pipeline, repeated in every `NavigateToX` command of [MainViewModel.cs](../../src/VyralBot.UI/ViewModels/MainViewModel.cs):

1. `CheckSetupInProgress(out _)` (`MainViewModel.cs:641-694`) — if `PlatformsViewModel.ShowTwitchSetup`/`ShowKickSetup` is set, show `ConfirmDialog`; confirming resets the wizard via `ResetWizard(disposeWebView: true)`. Only Twitch/Kick wizards are guarded.
2. `CurrentViewModel = null` **first**, so WPF releases its visual-tree references before anything is disposed.
3. `DisposeNonEssentialViewModelsAsync(excludeViewModel)` (`MainViewModel.cs:1638-1868`) — nulls the cached VMs/scope fields for every "non-essential" page, then (if anything was disposed) runs `UpdateLayout` on the Dispatcher at `Loaded` priority, `await Task.Delay(100)`, and a blocking `GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true)`.
4. Resolve the target VM (usually from a fresh DI scope), `InitializeAsync()`, assign `CurrentViewModel`, set `Title`, optional low-priority `GC.Collect`.

**Quirk — the scopes are no-ops.** Every ViewModel class is registered as a DI **singleton** in `App.xaml.cs:185-224` ("to prevent memory leaks from duplicate instances"), so `CreateScope().GetRequiredService<T>()` returns the *same instance* every time. The per-page "dispose and recreate" cycle therefore re-initializes long-lived singleton objects, and the "Disposed X to free memory" logs describe nulling fields, not destroying instances. The lifecycle logic still works — re-running `InitializeAsync()` reloads state — but the logging and the scope ceremony are misleading.

Kept alive always: `DashboardViewModel` and `ChatViewModel`. Everything else is field-nulled on navigation away and re-resolved (same singleton instance) with a fresh `InitializeAsync()` on the way back.

### Platform status in the sidebar

`IsTwitchConnected` / `IsKickConnected` / `IsYouTubeConnected` are set by `CheckPlatformConnectionsAsync()` (`MainViewModel.cs:358-406`), which reads the `Platforms` rows from the current profile's database — `Status == "Connected"` maps to `true`; any failure silently forces all three `false`. Only the Twitch and Kick flags are used in XAML (sidebar entry visibility, `MainWindow.xaml:196-221`); `IsYouTubeConnected` is set but **never displayed**. While checking, this method also performs the startup auto-connects (Twitch token DPAPI-decrypt, Kick token refresh, YouTube) — see [Platforms Overview](../platforms/overview.md).

When the user connects/disconnects a platform on the Platforms page, `PlatformsViewModel` fires a callback registered by `NavigateToPlatformsAsync` → `RefreshPlatformConnectionsAsync()` (`MainViewModel.cs:593-606`), which re-checks status and tells `ChatViewModel.UpdateConnectedPlatforms()` on the UI dispatcher.

### The Demo Wizard

The sidebar's Demo Wizard button (`NavigateToDemoWizardCommand`, `MainViewModel.cs:1264-1297`) resolves [GoogleDemoSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/GoogleDemoSetupViewModel.cs) (registered transient — one of the few) and hooks its `SetupCompleted` event to navigate Home. The wizard itself is a 4-step Google OAuth flow built on [BaseOAuthSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/BaseOAuthSetupViewModel.cs) (638 lines) — the same engine the real Twitch/Kick/YouTube/TikTok/Discord setup wizards use. It intercepts the `http://localhost:3000` redirect inside an embedded WebView2, but its callback handler returns hard-coded dummy tokens, and its `SaveCredentialsAsync` writes `VYRALBOT_DEMO_CLIENT_ID/SECRET` as **Windows user environment variables** — outside the portable `appdata/` model. Finishing the demo connects nothing.

An older demo pair, [DemoSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/DemoSetupViewModel.cs) / [DemoSetupView.xaml](../../src/VyralBot.UI/Views/DemoSetupView.xaml), is dead code — registered nowhere, referenced by no DataTemplate.

### Process lifecycle (shell-relevant highlights)

`App()` (`App.xaml.cs:55-252`) grabs the single-instance mutex, configures Serilog (console + rolling `logs/vyralbot-.txt`, 7 days), sets `SustainedLowLatency` GC, pre-warms WebView2 on a background thread, and builds the Generic Host with every service and ViewModel registered. `OnStartup` kills leftover `msedgewebview2` processes **machine-wide**, starts the host, creates the DB schema, shows MainWindow, then runs the profile decision tree (see [Profiles](../getting-started/profiles.md)). `OnExit` (`App.xaml.cs:424-596`) starts a 10-second foreground watchdog thread that force-kills the process if cleanup deadlocks, then stops the host, kills tracked processes, cleans ChatViewModel, tears down the webhook stack, disposes Twitch/Kick services, kills WebView2 processes again, and runs two aggressive compacting GCs. Full sequence: [Startup & Lifecycle](../architecture/startup-lifecycle.md).

## Files & Code

| File | Role |
|---|---|
| [MainWindow.xaml](../../src/VyralBot.UI/MainWindow.xaml) | The whole shell: chrome (`15-31`), title bar (`52-116`), sidebar (`122-497`), content host + DataTemplate map (`502-905`), profile overlay (`910-1161`) |
| [MainWindow.xaml.cs](../../src/VyralBot.UI/MainWindow.xaml.cs) | Title bar drag/maximize/min/max/close handlers; PIN PasswordBox plumbing |
| [MainViewModel.cs](../../src/VyralBot.UI/ViewModels/MainViewModel.cs) | Every navigation command, sidebar state, platform status flags, disposal pipeline |
| [App.xaml.cs](../../src/VyralBot.UI/App.xaml.cs) | DI registrations (all-VM-singletons), startup order, exit watchdog |
| [BaseOAuthSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/BaseOAuthSetupViewModel.cs) | Reusable OAuth wizard engine (used by the Demo Wizard and platform setups) |
| [GoogleDemoSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/GoogleDemoSetupViewModel.cs) | The Demo Wizard's ViewModel (branding overrides over the engine) |
| [GoogleDemoSetupView.xaml](../../src/VyralBot.UI/Views/GoogleDemoSetupView.xaml) | Demo Wizard view (1059 lines, WebView2 steps) |
| [DemoSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/DemoSetupViewModel.cs) | Dead code — older demo wizard, unregistered |
| [DemoSetupView.xaml](../../src/VyralBot.UI/Views/DemoSetupView.xaml) | Dead code — its view |

Borderless chrome setup (`MainWindow.xaml:15-31`):

```xml
Title="{Binding Title}"
Height="720" Width="1280"
WindowStartupLocation="CenterScreen"
WindowStyle="None"
AllowsTransparency="True"
ResizeMode="CanResize"
Background="Transparent">
...
<WindowChrome.WindowChrome>
    <WindowChrome CaptionHeight="32"
                  ResizeBorderThickness="5"
                  GlassFrameThickness="0"
                  CornerRadius="8"
                  UseAeroCaptionButtons="False"/>
</WindowChrome.WindowChrome>
```

The navigation pattern every page follows — `NavigateToSettingsAsync` (`MainViewModel.cs:873-912`) is representative:

```csharp
[RelayCommand]
private async Task NavigateToSettingsAsync()
{
    if (!CheckSetupInProgress(out _))
        return;                                   // wizard guard

    CurrentViewModel = null;                      // release WPF references FIRST

    await DisposeNonEssentialViewModelsAsync(excludeViewModel: _settingsViewModel);

    // Always recreate Settings to ensure fresh state and free memory when navigating away
    _settingsScope?.Dispose();
    _settingsScope = _serviceProvider.CreateScope();
    _settingsViewModel = _settingsScope.ServiceProvider.GetRequiredService<SettingsViewModel>();
    await _settingsViewModel.InitializeAsync();

    CurrentViewModel = _settingsViewModel;
    Title = "VyralBot - Settings";
    GC.Collect(1, GCCollectionMode.Optimized, blocking: false);
}
```

The memory-reclaim core of `DisposeNonEssentialViewModelsAsync` (`MainViewModel.cs:1846-1867`):

```csharp
if (disposedCount > 0)
{
    // Force WPF to process ALL pending UI updates synchronously
    // This ensures ContentControl releases the old view before GC
    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
    {
        System.Windows.Application.Current.MainWindow?.UpdateLayout();
    }, System.Windows.Threading.DispatcherPriority.Loaded);

    // Wait for WPF rendering thread to complete
    await Task.Delay(100);

    // Aggressive compacting collection to reclaim memory.
    // Blocking is necessary to ensure disposal completes before navigation continues.
    GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
}
```

## Data & Settings

The shell persists **no settings of its own**:

- Window size/position are defaults (1280x720, centered) — bounds are not remembered between launches.
- Sidebar group expansion (`IsPlatformsExpanded` etc.) resets to collapsed each launch.
- The sidebar's platform indicators are *derived* state: read from the current profile's `Platforms` table rows (`Status` column) on every check — see [Database Reference](../reference/database-reference.md) and [Data Layer](../architecture/data-layer.md).
- Navigation produces only transient state (`CurrentViewModel`, `Title`). Logging of every navigation click goes to `logs/vyralbot-.txt` (Serilog, 7-day retention).
- Single-instance mutex `VyralBot_SingleInstance_Mutex` (`App.xaml.cs:59`).

## Status & Known Issues

**Works**: custom chrome (drag/double-click/maximize/close), full sidebar navigation for every wired entry, connected-only Twitch/Kick sidebar entries with live refresh from the Platforms page, wizard-in-progress guard, single-instance enforcement, title updates per page and per profile.

**Dead buttons** (render as clickable, do nothing) — details in the [known issues ledger](../reference/known-issues.md):

- Sidebar **Events & Alerts** (`MainWindow.xaml:250-258`), **Browser Sources** (`324-332`). (The sidebar **Help** button was dead until the in-app Documentation browser shipped — it now opens the docs; see [Documentation & Contributing](documentation.md).)
- Home page **Discord** and **Ideas & Suggestions** rows (see [Dashboard](dashboard.md)).

**Wiring gaps and quirks**:

- `IsSidebarExpanded` / `ToggleSidebarCommand` (`MainViewModel.cs:79-80`, `608-613`) are implemented but **never bound in XAML** — the sidebar is a fixed 200px and the toggle is unreachable.
- `IsYouTubeConnected` is tracked but has no sidebar entry; YouTube is reachable only via Platforms/Chat Hub.
- Misleading names: `NavigateToTwitchSettingsCommand` actually loads `TwitchDashboardViewModel` (`MainViewModel.cs:1489-1520`); dispose logs label `GoogleDemoSetupViewModel` as "DemoSetupViewModel" (`MainViewModel.cs:1792`).
- DI scopes for ViewModels are no-ops (singletons) — "recreate" re-initializes the same instance; disposal logging overstates what happens.
- Every navigation does a 100 ms wait plus a blocking Gen2 compacting GC — noticeable micro-pause, by design (memory pressure drove this; see [Startup & Lifecycle](../architecture/startup-lifecycle.md)).
- Startup and exit kill **all** `msedgewebview2` processes on the machine, not just VyralBot's own — this can disrupt other apps using WebView2.
- The Demo Wizard persists dummy credentials to Windows **user environment variables**, outside the portable `appdata/` model.
- Exit cleanup is wrapped in a 10-second kill watchdog; a forced kill after 10 s is possible if teardown hangs.
