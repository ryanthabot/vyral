# Startup & Lifecycle

> How Vyral boots (single-instance guard → host → MainWindow-first → profile decision tree), what runs in what order after a profile loads, how profile hot-switching tears down and rebuilds the world, and how aggressively the app shuts down.

## Overview

Vyral is a WPF app (`net10.0-windows`) built on the Generic Host. Two design decisions shape everything here:

1. **Everything is a DI singleton.** Every service *and every ViewModel* is registered as a singleton "to prevent memory leaks from duplicate instances" (`App.xaml.cs:185-224`). Per-navigation "scopes" therefore resolve to the same instances, and the disposal logging that accompanies them is partly theater — the lifecycle logic still works, but fresh instances are never actually created for those types.
2. **The app is memory-obsessed.** Startup and exit clean up Vyral's own leftover WebView2 processes (scoped since 2026-08-15 to `msedgewebview2` processes whose command line references `VyralBot_WebView2_Temp`), navigation forces blocking Gen2 compacting GC, and several subsystems were rewritten specifically to save RAM. See Gotchas.

Related pages: [Profiles](../getting-started/profiles.md) for the user-facing side of profile selection, [Event Bus](event-bus.md) for what the boot ordering protects, [Data Layer](data-layer.md) for per-profile database routing.

## Flow

### Constructor (`App()`)

1. **Single-instance mutex** `VyralBot_SingleInstance_Mutex` (`App.xaml.cs:59`). A second instance shows a MessageBox and exits — profiles cannot run concurrently.
2. **Serilog**: console + rolling daily file `logs/vyralbot-.txt` under the exe directory, 7 days retained.
3. **GC**: `GCSettings.LatencyMode = SustainedLowLatency` (a documented "TIER 4" optimization).
4. **WebView2 prewarm** on a background thread via `WebView2EnvironmentManager.PreWarmAsync()`.
5. **Host build**: full DI registration — EventBus, TriggerEngine, currency/giveaway/poll/watchtime services, Twitch/Kick/YouTube integrations, the web-server stack (`WebServerDependencyTracker`, `WebServerService`, `NgrokService`, `KickWebhookSubscriptionService`, `WebhookCoordinatorService`), all ViewModels, and `MainWindow` itself as singletons; views transient.

### `OnStartup`

1. **Clean up leftover `msedgewebview2` processes** — scoped via a PowerShell `Get-CimInstance` query that matches only processes whose command line contains `VyralBot_WebView2_Temp` — and delete the `%TEMP%\VyralBot_WebView2_Temp` cache.
2. `await _host.StartAsync()` — starts background services. Kestrel is *not* listening yet despite a log line claiming it is; the web server starts later, after profile load, via `WebhookCoordinatorService`.
3. `EnsureDatabaseCreatedAsync()` on the profile-aware `IDataService`; start `ITriggerEngine`.
4. **Show `MainWindow` first** — there is always a window on screen, even before a profile is chosen.
5. **Profile decision tree** (`App.xaml.cs:333-373`):
   - No profiles at all, or empty/missing `current_profile.txt` → show the profile selector overlay (first run forces creating a profile).
   - Current profile name not found among the profiles on disk → selector.
   - More than one profile:
     - current profile's `Settings["AutoLogin"] == "True"` → skip the selector, fire-and-forget `MainViewModel.OnProfileLoadedAsync()`;
     - otherwise → selector overlay.
   - Exactly one profile → auto-load silently (AutoLogin is irrelevant here).

The selector is an inline modal overlay inside `MainWindow.xaml` (ZIndex 2000), not a separate window. `ProfileSelectorWindow`/`ProfileSelectorViewModel` are a complete, never-instantiated alternative — dead code.

### `OnProfileLoadedAsync` — the real boot (`MainViewModel.cs:219-292`)

Order matters and is deliberately strict:

1. `UpdateTitleAsync()` — window title becomes `Vyral ({Profile.DisplayName})`.
2. `WebhookCoordinatorService.InitializeAsync()` — loads the `WebServerSettings` row; auto-starts Kestrel + ngrok if `AutoStartOnLaunch` is set *and* a saved ngrok token exists. This is the only auto-start call — later steps explicitly do not repeat it (it caused a double start/stop/start cycle).
3. `SoundService.InitializeDefaultSoundsAsync()`.
4. Create (from temp scopes) `KickSettingsViewModel` and `WebServerSettingsViewModel`, and the long-lived `ChatViewModel` if not already created. `PlatformsViewModel` is deliberately **not** created — lazy-loading it on demand saves ~30 MB.
5. `ProfileSettingsLoader.LoadAllSettingsAsync(platforms: null, kick, webServer, chat)` — Kick settings → web-server settings → Chat settings. Everything subscribes and loads **before** any connection: the comment at the call site is explicit that ChatViewModel must subscribe to the EventBus before platforms connect or messages arriving in between are lost.
6. `CheckPlatformConnectionsAsync()` — reads each platform's DB row; where `Status == "Connected"`, decrypts Twitch tokens (DPAPI), refreshes the Kick token first, and calls `ConnectAsync`. **Side effect:** on first successful auto-connect it force-writes `<Platform>ChatEnabled = true` back to the DB.
7. `AutoEnableKickWebhooksAsync()` — if the webhook infrastructure is running, active Kick subscriptions exist in the DB, and Kick credentials are present, re-enables Kick webhooks.
8. Dispose the two temporary scopes.

### Profile hot-switching

`ProfileService.SwitchProfileAsync` validates the PIN, rewrites `current_profile.txt`, and publishes `ProfileSwitchedEvent` on the shared EventBus. `MainViewModel`'s subscriber (`MainViewModel.cs:154-210`):

- **Same profile re-selected** → just `OnProfileLoadedAsync()` again, no teardown.
- **Different profile** → update title → `DisconnectAllPlatformsAsync()` (Twitch/Kick/YouTube) → `ChatViewModel.Cleanup()` and null it → null the cached Platforms/Settings ViewModels and dispose their scopes → re-run the full `OnProfileLoadedAsync()` → navigate Home if currently on a profile-specific page.

Because `ProfileAwareDataService` re-resolves the profile on every call, all subsequent DB access transparently hits the new profile's database. In-memory caches that are not profile-aware were the historical risk here; `ChatEventSettingsService` now invalidates its cache on `ProfileSwitchedEvent` (fixed 2026-08-15), so chat-event settings switch cleanly too.

### Navigation pattern

Nearly every `NavigateToX` command in `MainViewModel` follows the same sequence:

1. `CheckSetupInProgress` — if a Twitch/Kick OAuth wizard is active, confirm and either cancel navigation or `ResetWizard(disposeWebView: true)`.
2. `CurrentViewModel = null` **first**, releasing the WPF visual tree's references.
3. `DisposeNonEssentialViewModelsAsync(exclude)` — nulls cached VM fields, forces `UpdateLayout` on the Dispatcher, waits 100 ms, then a **blocking Gen2 compacting GC**: `GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true)` (`MainViewModel.cs:1861`).
4. Resolve the target VM (from a "scope" that returns the singleton anyway), `InitializeAsync()`, set `CurrentViewModel`, set the window title, optional `GC.Collect`.

`ChatViewModel` and `DashboardViewModel` are never disposed. Home's entire UI is an inline `DataTemplate` in `MainWindow.xaml` — there is no `DashboardView.xaml`.

### `OnExit` (`App.xaml.cs:424-596`)

A 10-second **foreground watchdog thread** starts first — if cleanup deadlocks, it force-kills the process. Then: release the mutex → stop the host (5 s timeout) → kill tracked child processes via `IProcessManager` → clean ChatViewModel → stop `WebhookCoordinatorService` (Kestrel + ngrok) *before* host dispose → dispose Twitch/Kick services → run the scoped WebView2 cleanup again (only processes referencing `VyralBot_WebView2_Temp`) → double aggressive `GC.Collect` → suppress WeakEventTable exceptions from `base.OnExit`.

## Key classes

| Class | Role |
|---|---|
| `App` | Mutex, Serilog, GC mode, host build, OnStartup decision tree, OnExit watchdog |
| `MainViewModel` | The shell: navigation, profile-load orchestration, platform auto-connect, the GC-heavy dispose routine |
| `ProfileService` | Profile CRUD + switch (publishes `ProfileSwitchedEvent`); resolves the portable `appdata/` root |
| `ProfileSettingsLoader` | Strictly-ordered settings load before connections |
| `WebView2EnvironmentManager` | Background WebView2 prewarm |
| `WebhookCoordinatorService` | Auto-start of the web stack during profile load |

## Files & Code

| File | Role |
|---|---|
| [App.xaml.cs](../../src/VyralBot.UI/App.xaml.cs) | Everything in the constructor, `OnStartup`, and `OnExit` |
| [MainViewModel.cs](../../src/VyralBot.UI/ViewModels/MainViewModel.cs) | `OnProfileLoadedAsync`, profile-switch subscriber, navigation commands, `DisposeNonEssentialViewModelsAsync` |
| [MainWindow.xaml](../../src/VyralBot.UI/MainWindow.xaml) | Shell chrome, sidebar, the ViewModel→View `DataTemplate` map, the home page (inline), and the profile selector overlay |
| [ProfileService.cs](../../src/VyralBot.Core/Services/Profiles/ProfileService.cs) | Profile storage and switching |
| [ProfileSettingsLoader.cs](../../src/VyralBot.UI/Services/ProfileSettingsLoader.cs) | Ordered settings initialization |
| [WebView2EnvironmentManager.cs](../../src/VyralBot.UI/Services/WebView2EnvironmentManager.cs) | WebView2 prewarm |

The profile decision tree in miniature:

```csharp
// App.xaml.cs (condensed)
if (!profiles.Any() || string.IsNullOrEmpty(initialProfileName))
    await mainViewModel.ShowProfileSelectorAsync();       // first run forces creation
else if (!profiles.Any(p => p.Name == initialProfileName))
    await mainViewModel.ShowProfileSelectorAsync();       // stale current_profile.txt
else if (profiles.Count() > 1 && currentProfile.Settings["AutoLogin"] == "True")
    _ = mainViewModel.OnProfileLoadedAsync();             // auto-login, no selector
else if (profiles.Count() > 1)
    await mainViewModel.ShowProfileSelectorAsync();
else
    _ = mainViewModel.OnProfileLoadedAsync();             // single profile, silent
```

The ordering contract that everything else depends on:

```csharp
// MainViewModel.OnProfileLoadedAsync (condensed) — settings and subscriptions
// MUST exist before platforms connect, or early messages are lost.
await _settingsLoader.LoadAllSettingsAsync(platformsViewModel: null, ...);
await CheckPlatformConnectionsAsync();      // auto-connect AFTER subscriptions
await AutoEnableKickWebhooksAsync();        // only if infra running + subs exist
```

## Gotchas

- **WebView2 cleanup is scoped to Vyral's own processes** — a `Get-CimInstance` filter on `VyralBot_WebView2_Temp` in each process's command line, at startup and exit (fixed 2026-08-15; previously the cleanup killed every `msedgewebview2` on the machine).
- **Blocking Gen2 GC on every page switch** trades navigation latency for memory; under load the UI can stutter at each navigation.
- **`App.AutoConnectTwitchAsync` is dead code** — never called; auto-connect lives in `MainViewModel`.
- **`ProfileSelectorWindow`/`ProfileSelectorViewModel` are dead code** — the live selector is the MainWindow overlay, which (unlike the dead window) cannot delete profiles.
- **Startup log line lies about Kestrel** — "Kestrel is now listening on http://localhost:5000" is logged at host start, but the server actually starts later during profile load (or never, without auto-start).
- **Scopes are no-ops for ViewModels** — all VMs are singletons, so `CreateScope().GetRequiredService<T>()` returns the same instance; "disposed ... to free memory" log lines null the fields but never destroy the objects.
- **The stale-code comment in `ProfileService.cs`** references `net9.0-windows` path structure even though the project targets `net10.0-windows`; the directory walk-up still works because the layout depth is unchanged.
- **First-run UX is modal** — MainWindow appears behind a full-window overlay with no cancel; creating a profile is the only path forward.
