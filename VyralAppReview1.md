# VyralBot Review — HOME / DASHBOARD Section

Scope: Home/Dashboard only (app shell, startup flow, profile selection, home cards, demo wizards, in-app docs).
Other sections (Chat, Platforms, Chat Features, Stream Tools, Settings) are covered by other reviewers; interactions are noted but not deeply documented.

All paths relative to `C:\Users\user\OneDrive\Desktop\VyralBot\VyralBot\`.

---

## 1. App Startup Flow (profile selector → dashboard)

**Where:** `src/VyralBot.UI/App.xaml.cs`

- `App()` constructor (App.xaml.cs:55-252):
  - Single-instance guard via named `Mutex` `"VyralBot_SingleInstance_Mutex"` (App.xaml.cs:59) — second instance shows a MessageBox and exits.
  - Serilog configured: console + rolling daily file `logs/vyralbot-.txt` under the exe dir, 7 days retained (App.xaml.cs:72-79).
  - `GCSettings.LatencyMode = SustainedLowLatency` (perf "TIER 4" optimization, App.xaml.cs:85).
  - `WebView2EnvironmentManager.Instance.PreWarmAsync()` pre-warms WebView2 on a background thread ("TIER 2" optimization, App.xaml.cs:91).
  - Generic Host built with full DI: all services (EventBus, TriggerEngine, currency, giveaways, polls, watchtime, Twitch/Kick/YouTube integrations, WebhookCoordinatorService, NgrokService, etc.) + all ViewModels registered as SINGLETONS "to prevent memory leaks from duplicate instances" (App.xaml.cs:185-224). Views registered transient. `MainWindow` singleton.
- `OnStartup` (App.xaml.cs:254-383), in order:
  1. Kills leftover `msedgewebview2` processes (~135 MB claimed) and deletes `%TEMP%\VyralBot_WebView2_Temp` cache (App.xaml.cs:258-294).
  2. `await _host.StartAsync()` — comment says Kestrel is listening on `http://localhost:5000`, but `WebhookCoordinatorService` actually initializes later, after profile load (see note at App.xaml.cs:311).
  3. `EnsureDatabaseCreatedAsync()` on the profile-aware `IDataService`; starts `ITriggerEngine`.
  4. Shows `MainWindow` FIRST (so there is always a window on screen).
  5. Profile decision tree (App.xaml.cs:333-373):
     - No profiles OR no current profile name → `mainViewModel.ShowProfileSelectorAsync()` (first-run forces profile creation).
     - Current profile name in `appdata/current_profile.txt` not found among profiles → show selector.
     - Multiple profiles AND current profile has setting `AutoLogin=true` → skip selector, fire-and-forget `OnProfileLoadedAsync()`.
     - Multiple profiles without AutoLogin → show selector overlay.
     - Exactly one profile → skip selector, auto-load.
- Dead code: `App.AutoConnectTwitchAsync()` (App.xaml.cs:385-422) is never called — Twitch auto-connect is handled by `MainViewModel.CheckPlatformConnectionsAsync()` instead.
- `OnExit` (App.xaml.cs:424-596): starts a 10-second foreground "shutdown watchdog" thread that force-kills the process if cleanup deadlocks; releases mutex; stops host; kills tracked processes via `IProcessManager`; cleans ChatViewModel; stops `WebhookCoordinatorService` (embedded Kestrel + ngrok) before host dispose; disposes Twitch/Kick services; kills all `msedgewebview2` processes again; double aggressive GC.Collect; suppresses WeakEventTable exceptions from `base.OnExit`.

**Interaction notes:** EventBus is a single shared singleton across WPF UI and the Kestrel web server, so webhook events reach UI ViewModels (App.xaml.cs:104-107, 313-314).

---

## 2. Main Window / App Shell (title bar, sidebar, content host)

**Where:** `src/VyralBot.UI/MainWindow.xaml` (1164 lines), `MainWindow.xaml.cs`, `ViewModels/MainViewModel.cs` (1935 lines)

### 2.1 Window chrome / title bar
- Borderless custom chrome: `WindowStyle="None"`, `AllowsTransparency="True"`, rounded 8px corner, `WindowChrome` with `CaptionHeight=32` (MainWindow.xaml:15-31).
- Title bar (MainWindow.xaml:52-116): VyralBot logo + "VyralBot" label; minimize / maximize / close MaterialDesign flat buttons. Close button hovers red.
  - Code-behind `TitleBar_MouseLeftButtonDown` (MainWindow.xaml.cs:31-43): double-click toggles maximize, single click drags window.
- Window `Title` binding: `MainViewModel.Title` = `"VyralBot ({Profile.DisplayName})"` after a profile loads (`UpdateTitleAsync`, MainViewModel.cs:345-356); navigation commands overwrite it (e.g., "VyralBot - Home", "VyralBot - Chat"). Note the window title bar label in chrome always says "VyralBot" (hardcoded TextBlock); the bound `Title` is the OS taskbar text.

### 2.2 Sidebar navigation
- Fixed 200px sidebar (MainWindow.xaml:122) with scrollable nav:
  - **Home** → `NavigateToHomeCommand`
  - **Chat** → `NavigateToChatCommand`
  - **Platforms** collapsible group (`TogglePlatformsCommand`): Twitch and Kick entries are only VISIBLE when `IsTwitchConnected` / `IsKickConnected` (MainWindow.xaml:196-221) → `NavigateToTwitchSettingsCommand` / `NavigateToKickSettingsCommand` (which actually load `TwitchDashboardViewModel` / `KickDashboardViewModel`, see MainViewModel.cs:1489-1553).
  - **Chat Features** group: Events & Alerts (NO command — dead button, MainWindow.xaml:250-258), Currency & Rewards, Commands, Event History.
  - **Stream Tools** group: Browser Sources (NO command — dead button, MainWindow.xaml:324-332), Audio Settings, Sounds Management, Statistics, Visual Alerts, Conditional Rules, Giveaways, Polls, Watchtime.
  - Bottom: Settings, Event Testing, Demo Wizard (`NavigateToDemoWizardCommand`), Help (NO command — dead button, MainWindow.xaml:486-495).
- Dead VM state: `IsSidebarExpanded` / `ToggleSidebarCommand` (MainViewModel.cs:79-80, 608-613) is implemented but never bound in any XAML — the sidebar width is fixed; the toggle is unreachable from the UI.

### 2.3 Navigation pattern (how dashboard interacts with everything)
- `MainViewModel.CurrentViewModel` drives a `ContentControl` whose `ContentControl.Resources` map ViewModel types to Views via `DataTemplate`s (MainWindow.xaml:502-905). Views are created lazily by WPF when the ViewModel is set — this is the app's lazy-loading mechanism.
- Nearly all `NavigateToX` commands follow the same pattern (MainViewModel.cs):
  1. `CheckSetupInProgress(out _)` — if a Twitch/Kick OAuth setup wizard is active in PlatformsViewModel, show `ConfirmDialog` and either cancel navigation or reset the wizard (`ResetWizard(disposeWebView: true)`) (MainViewModel.cs:641-694).
  2. `CurrentViewModel = null` FIRST to release WPF visual-tree references.
  3. `DisposeNonEssentialViewModelsAsync(excludeViewModel)` — disposes the previous page's scope/VM, forces `UpdateLayout` on the Dispatcher, waits 100ms, then blocking Gen2 compacting GC (MainViewModel.cs:1638-1868). ChatViewModel and DashboardViewModel are NEVER disposed (kept alive as singletons).
  4. Recreate the target VM in a fresh DI scope, `InitializeAsync()`, set `CurrentViewModel`, set `Title`, optionally `GC.Collect`.
- Some pages get brand-new VMs every navigation (Settings, Platforms, Sounds, Statistics, etc.); Chat reuses the long-lived singleton created at profile load.
- Quirk: comments in DisposeNonEssentialViewModelsAsync call ViewModels singletons but the code creates them from scopes; because App.xaml.cs registers the VM classes as singletons, the scoped resolution returns the same singleton instance anyway — the scopes are effectively no-ops for those types, but the disposal logging path still nulls the fields.

### 2.4 Platform status display (sidebar visibility)
- `IsTwitchConnected` / `IsKickConnected` / `IsYouTubeConnected` set by `CheckPlatformConnectionsAsync()` (MainViewModel.cs:358-406): reads `Platform` rows from the profile DB via `IDataService.GetPlatformByNameAsync(...)`, `Status == "Connected"` → true. Note: there is NO sidebar entry for YouTube even though the flag exists (only Twitch/Kick buttons are gated; `IsYouTubeConnected` is set but unused in MainWindow.xaml).
- On failure all three flags are silently forced false.
- `RefreshPlatformConnectionsAsync()` (MainViewModel.cs:593-606) is invoked via a callback from PlatformsViewModel when the user connects/disconnects a platform; it re-checks status and tells ChatViewModel (`UpdateConnectedPlatforms()`) on the UI dispatcher.

---

## 3. Home / Dashboard page

**Where:** inline `DataTemplate DataType="{x:Type vm:DashboardViewModel}"` in MainWindow.xaml:505-769. There is NO DashboardView.xaml — the entire home page UI is defined inline inside MainWindow.xaml.

### 3.1 What the user sees
1. **Four main navigation cards** (grid of 4, MainWindow.xaml:509-639), each a clickable `materialDesign:Card` (MouseBinding on LeftClick to the Window's DataContext command):
   - **Connections** → `NavigateToPlatformsCommand` — "Manage your platform connections".
   - **Chat** → `NavigateToChatHubCommand` — "Complete chat management for all platforms" (goes to ChatHub hub page, not the raw Chat view).
   - **Tools** → `NavigateToToolsCommand` — "Stream tools, chat features, and more".
   - **Integrations** → `NavigateToIntegrationsCommand` — "Third-party integrations and bots".
2. **Support section** (MainWindow.xaml:641-766):
   - **Documentation** row → `NavigateToDocumentationCommand` — "Explore the official documentation for VyralBot".
   - **Discord** row — "Join the VyralBot community!" — has `Cursor="Hand"` and chevron but NO InputBinding/command: **dead button** (MainWindow.xaml:692-728).
   - **Ideas & Suggestions** row — "Share and discuss ideas..." — same: **dead button** (MainWindow.xaml:731-765).

### 3.2 How it works internally
- `DashboardViewModel` (src/VyralBot.UI/ViewModels/DashboardViewModel.cs) is a **stub**: 4 observable properties (`ActiveTriggerCount`, `TotalActionCount`, `ConnectedPlatformCount`, `Status="Ready"`) whose `LoadDashboard()` only sets everything to 0 with a `// TODO: Load actual data from services` (DashboardViewModel.cs:27-33). **None of these properties are bound anywhere in XAML** — the home page shows zero live data; it is a purely static navigation launcher.
- Registered as singleton in DI (App.xaml.cs:186) and injected into MainViewModel, which sets `CurrentViewModel = dashboardViewModel` at construction (MainViewModel.cs:149) — so Home is always the initial page state even before a profile is picked.
- `NavigateToHome` (MainViewModel.cs:696-732): skips if already home; checks setup-in-progress; nulls CurrentViewModel; resets Platforms view flags (ShowPlatformSelection=true etc.); disposes non-essential VMs; sets `Title = "VyralBot - Home"`.

### 3.3 Interactions
- Home cards navigate to hub pages (Platforms/ChatHub/Tools/Integrations) — the hub pages themselves are other reviewers' scope, but home is the primary entry point to them.
- Home is never disposed on navigation (kept alive for performance, MainViewModel.cs:1642-1644).
- After a profile switch, if the user was on any other page, MainViewModel auto-navigates back to Home to avoid stale data (MainViewModel.cs:198-207).

---

## 4. Profile system (selection at startup)

### 4.1 Actual selector UI = inline overlay in MainWindow
**Where:** MainWindow.xaml:910-1161 (overlay `Border` with `Panel.ZIndex=2000`, `ModalBackgroundBrush`), state on MainViewModel.

Flow (MainViewModel):
- `ShowProfileSelectorAsync()` (MainViewModel.cs:1559-1571) loads profiles into `Profiles` ObservableCollection and sets `ShowProfileSelector=true`.
- Profile list card (MainWindow.xaml:917-1052): each profile row shows DisplayName + Name and a lock icon when `HasPin`; clicking calls `SelectProfileCommand`.
- `SelectProfile(profile)` (MainViewModel.cs:1573-1592): stores selection; if `PinCode` non-empty → `ShowPinEntry=true` (inline PasswordBox, max 6 chars, code-behind pushes password into `MainViewModel.ProfilePin`, MainWindow.xaml.cs:74-80); otherwise immediately `ConfirmProfileSelectionAsync()`.
- `ConfirmProfileSelectionAsync()` (MainViewModel.cs:1594-1632): validates PIN via `IProfileService.ValidatePinAsync`, calls `SwitchProfileAsync`, hides overlay, clears PIN, updates title.
- **Create New Profile card** (MainWindow.xaml:1054-1159): fields Name (required, letters/numbers/underscore/hyphen), optional Display Name, optional 4-6 digit PIN. `ConfirmCreateProfileAsync` (MainViewModel.cs:1892-1934) creates the profile via `IProfileService.CreateProfileAsync`, switches to it, hides the overlay, and calls `CheckPlatformConnectionsAsync()`.

### 4.2 Dead alternative implementation: ProfileSelectorWindow
- `src/VyralBot.UI/Views/ProfileSelectorWindow.xaml` (+ .xaml.cs) and `ViewModels/ProfileSelectorViewModel.cs` implement a FULL standalone selector window: ordered-by-LastUsedAt list, auto-select most recent, PIN entry gating, profile creation, **and profile deletion with confirm** (`DeleteProfileCommand`, can only delete when >1 profile). 
- **Never instantiated anywhere** — only self-references and a DI registration (`services.AddTransient<ProfileSelectorViewModel>()`, App.xaml.cs:223). The MainWindow overlay has no delete-profile capability, so profile deletion is effectively unreachable in the shipped UI (must be exercised via Settings or manual file deletion; see Settings section of other reviewer).

### 4.3 ProfileService (backend)
**Where:** `src/VyralBot.Core/Services/Profiles/ProfileService.cs` (+ `IProfileService.cs`)

- **Portable storage**: resolves app root by walking 6 directories up from the exe (`net9.0-windows → Debug → bin → VyralBot.UI → src → root`, ProfileService.cs:29-40); data lives in `<root>/appdata/`. Falls back to exe dir in published builds.
  - `appdata/profiles/<ProfileName>/profile.json` — per-profile metadata (JSON).
  - `appdata/current_profile.txt` — name of the active profile.
  - `appdata/profiles/<ProfileName>/data/` — per-profile database/data path (`GetProfileDataPath`).
- `Profile` model (`src/VyralBot.Core/Models`): Name, DisplayName, CreatedAt, LastUsedAt, PinCode (SHA256 hash, Base64 — unsalted, ProfileService.cs:407-416), Settings dictionary (string→string; `AutoLogin` key checked at startup), HasPin.
- Key methods: `GetCurrentProfileName()` (sync, reads current_profile.txt), `GetProfilesAsync()` (scans profile dirs, orders by LastUsedAt desc), `CreateProfileAsync` (validates name regex `^[a-zA-Z0-9_-]+$`, hashes PIN, auto-sets first profile as current), `SwitchProfileAsync` (validates PIN, writes current_profile.txt, publishes `ProfileSwitchedEvent` on the EventBus), `DeleteProfileAsync` (GC.Collect + delay hack to release handles, deletes dir, clears current_profile.txt if it was current), `ValidatePinAsync`, `UpdateProfilePinAsync`, `ExportProfileAsync`/`ImportProfileAsync` (ZIP-based; unused by the dashboard UI).
- Doc comment claims "requires app restart" for switching, but the app actually hot-switches via the EventBus event (see 4.4) — the comment is stale.

### 4.4 Profile switch handling (hot reload)
MainViewModel subscribes to `ProfileSwitchedEvent` (MainViewModel.cs:154-210):
- Same profile re-selected → just `OnProfileLoadedAsync()` (no teardown).
- Different profile → update title, `DisconnectAllPlatformsAsync()` (Twitch/Kick/YouTube disconnect), `ChatViewModel.Cleanup()` + null it, null cached Platforms/Settings VMs, dispose their scopes, `OnProfileLoadedAsync()`, and navigate back to Home if currently on another page.

### 4.5 OnProfileLoadedAsync (post-profile initialization) — MainViewModel.cs:219-292
1. `UpdateTitleAsync()`.
2. `WebhookCoordinatorService.InitializeAsync()` — starts embedded Kestrel + ngrok (auto-start is handled here, deliberately NOT repeated elsewhere to avoid a double start/stop cycle, MainViewModel.cs:284-285).
3. `SoundService.InitializeDefaultSoundsAsync()`.
4. Creates (in temp scopes) `KickSettingsViewModel` and `WebServerSettingsViewModel`, and the long-lived `ChatViewModel`.
5. `ProfileSettingsLoader.LoadAllSettingsAsync(platforms: null, kick, webServer, chat)` — loads persisted settings BEFORE connections so EventBus subscriptions exist before messages can arrive (comment at MainViewModel.cs:260-262). Platforms VM is deliberately NOT created at startup (lazy, "saves ~30MB").
6. `CheckPlatformConnectionsAsync()` — sets sidebar flags and auto-connects Twitch (decrypt stored token via `TwitchOAuthService.Decrypt`), Kick (refreshes token via `KickLibOAuthService.RefreshTokenAsync` first; Kick tokens stored unencrypted), YouTube. After each successful auto-connect, chat-enable flags are force-set true and saved back to the DB (e.g., `TwitchChatEnabled`, MainViewModel.cs:437-443).
7. `AutoEnableKickWebhooksAsync()` — if webhook server running and active Kick subscriptions exist in DB, re-enables Kick webhooks (MainViewModel.cs:296-343).

### 4.6 Data layer behind the profile system
- **ProfileAwareDataService** (`src/VyralBot.UI/Services/ProfileAwareDataService.cs`): decorator over `DapperDataService` (SQLite) registered as the singleton `IDataService`. On EVERY call it checks `_profileService.GetCurrentProfileName()`; when the profile changes it transparently constructs a new `DapperDataService` pointed at `appdata/profiles/<Name>/data/vyralbot.db` (ProfileAwareDataService.cs:26-46). Falls back to profile name `"temp"` when none is selected (which is why an `appdata/profiles/temp/` folder appears on disk). `ClearConnection()` (ProfileAwareDataService.cs:51-64) nulls the inner service, calls `SqliteConnection.ClearAllPools()` + GC to release DB file locks (used to allow profile deletion).
- **ProfileSettingsLoader** (`src/VyralBot.UI/Services/ProfileSettingsLoader.cs`): orchestrates per-profile settings load. `LoadAllSettingsAsync(...)` (ProfileSettingsLoader.cs:28-68) initializes in strict order: (optional) Platforms credentials → `KickSettingsViewModel.InitializeAsync()` → `WebServerSettingsViewModel.InitializeAsync()` (ngrok auth token, port, region, auto-start) → `ChatViewModel.InitializeAsync()`. Deliberately runs BEFORE platform auto-connect so EventBus subscriptions exist first.

### 4.7 On-disk profile structure (verified: `appdata/`)
```
appdata/
├── current_profile.txt              # active profile name, e.g. "Drip"
└── profiles/
    ├── Drip/
    │   ├── profile.json             # Name, DisplayName, CreatedAt, LastUsedAt, Description, PinCode (hash), Settings{}
    │   ├── data/vyralbot.db         # per-profile SQLite DB (all platforms, currency, polls, etc.)
    │   ├── twitch_credentials.json  # written by TwitchOAuthService / PlatformsViewModel
    │   └── ngrok_credentials.json   # written by NgrokService
    └── temp/data/vyralbot.db        # fallback DB when no profile selected yet
```
README.md:104-126 documents this portable layout ("No registry, nothing in %APPDATA%, delete appdata/ for fresh start").

---

## 5. Demo Setup Wizard (sidebar "Demo Wizard" button)

**Where:** sidebar bottom button → `NavigateToDemoWizardCommand` (MainWindow.xaml:473-483) → `MainViewModel.NavigateToDemoWizardAsync` (MainViewModel.cs:1264-1297) → `GoogleDemoSetupViewModel` → DataTemplate → `Views/GoogleDemoSetupView.xaml` (1059 lines).

### 5.1 What it does (user-visible)
A 4-step "Google Demo Setup Wizard" demonstrating the reusable OAuth setup wizard framework:
- Step 1 "Sign In to Google" — opens `https://accounts.google.com/` in an embedded WebView2.
- Step 2 "Create Application" — opens `https://console.cloud.google.com/`; "Copy Redirect URI" copies `http://localhost:3000` to clipboard; modals collect Client ID / Client Secret (masked to first 10 chars).
- Step 3 "Authorize Account" — builds a real Google OAuth URL from the entered Client ID and opens it in WebView2; intercepts navigation to `http://localhost:3000...` as the OAuth callback; manual callback-URL paste fallback (`ShowManualCallbackInput` / `SubmitCallbackUrlCommand`); status text updates (emoji ✅/❌).
- Step 4 "Test Connection" — "Send Test Message" fakes success after a 1-second delay ("✓ Test message sent to #channel! (Demo mode)").
- "Finish Setup" → `SetupCompleted` event → MainViewModel navigates back to Home (MainViewModel.cs:1280-1284); wizard state resets.

### 5.2 How it works internally
- `BaseOAuthSetupViewModel` (`src/VyralBot.UI/ViewModels/BaseOAuthSetupViewModel.cs`, 638 lines) is the reusable engine, also used by the real Twitch/Kick/YouTube/TikTok/Discord wizards (scope of the Platforms reviewer). Key mechanics:
  - The ViewModel does NOT own the browser: it exposes a `WebView2` property plus `WebView2InitializeRequested` / `WebView2DisposeRequested` events that the VIEW's code-behind handles (view creates/destroys the control). Navigation is fired after a hard-coded `Task.Delay(500-1000)` hoping the view initialized it (BaseOAuthSetupViewModel.cs:218-248).
  - OAuth interception: subscribes `CoreWebView2.NavigationStarting`, cancels and hides the browser when URI starts with `http://localhost:3000`, then dispatches `HandleOAuthCallbackAsync` (BaseOAuthSetupViewModel.cs:371-391).
  - `ResetWizard(disposeWebView)` is called by MainViewModel's `CheckSetupInProgress` when the user abandons a wizard by navigating away (MainViewModel.cs:663, 687).
- `GoogleDemoSetupViewModel` (`src/VyralBot.UI/ViewModels/GoogleDemoSetupViewModel.cs`) overrides only branding strings + 6 abstract hooks. Quirks: `SaveCredentialsAsync` stores the client id/secret in Windows USER ENVIRONMENT VARIABLES (`VYRALBOT_DEMO_CLIENT_ID/SECRET`, GoogleDemoSetupViewModel.cs:42-57) — persists outside the profile/appdata; `HandleOAuthCallbackAsync` returns hard-coded dummy tokens ("demo_access_token", "DemoUser") regardless of the actual callback URL.
- `SetupCompleted` completion → Home navigation means finishing the demo wizard does NOT connect anything (pure demo).

### 5.3 Dead sibling: DemoSetupViewModel / DemoSetupView
- `ViewModels/DemoSetupViewModel.cs` (demo of the older `BaseWebSetupViewModel`, opens github.com/google.com/stackoverflow.com) and `Views/DemoSetupView.xaml` exist but are **never registered in DI nor referenced by any DataTemplate** — unreachable dead code. (docs/application/app-entry.md:38 still claims `DemoSetupViewModel` is a registered singleton — stale.)
- Note: MainViewModel.cs:1792 logs "Disposed DemoSetupViewModel" when it actually disposes `GoogleDemoSetupViewModel` (field name `_demoSetupViewModel`) — misleading log label.

### 5.4 Related wizard reachable from Settings (not Home)
`NavigateToNgrokSetupWizard` (MainViewModel.cs:1299-1331) hosts the Ngrok setup wizard page; it is triggered from `WebServerSettingsViewModel` (WebServerSettingsViewModel.cs:204), i.e., Settings section, not the dashboard — noted here because MainViewModel (the dashboard's VM) owns the navigation command.

---

## 6. In-app Documentation page

**Where:** Home → Support → Documentation row → `NavigateToDocumentationCommand` (MainViewModel.cs:1464-1487) → `DocumentationViewModel` → `Views/DocumentationView.xaml`.

- `DocumentationViewModel` (src/VyralBot.UI/ViewModels/DocumentationViewModel.cs) is a completely EMPTY class (7 lines) — the view is purely static.
- `DocumentationView.xaml` (52 lines): "← BACK TO HOME" button (binds `NavigateToHomeCommand` via Window ancestor), title "Documentation", and a placeholder card: "Documentation Coming Soon — The VyralBot documentation is currently being written. Check back soon!" (DocumentationView.xaml:39-47).
- No WebView, no link to the repo's extensive `docs/` folder — purely a stub page.
- The other two Support rows (Discord, Ideas & Suggestions) have no command bindings at all (see section 3.1) — dead UI.

---

## 7. Hub pages the Home cards navigate to (interaction summary)

- **Connections card → PlatformsView** (Platforms section, other reviewer): platform selection + OAuth wizards. On connect/disconnect it calls back into MainViewModel via `SetConnectionChangedCallback` → `RefreshPlatformConnectionsAsync()` which updates sidebar visibility flags (MainViewModel.cs:1359-1362).
- **Chat card → ChatHubView** (`ChatHubViewModel`): hub for per-platform chat settings (Twitch/Kick/YouTube/TikTok/Discord `*ChatSettingsViewModel`s); tracks `IsTwitchConnected`/`IsKickConnected`/`IsYouTubeConnected`/`IsTikTokConnected`/`IsDiscordConnected` flags itself.
- **Tools card → ToolsView**: `ToolsViewModel` is an EMPTY class (the VM is only a navigation key); the XAML is a static grid of cards (Events & Alerts, Currency & Rewards, Commands, Event History, Audio Settings, Sounds, Statistics, Visual Alerts, Conditional Rules, Giveaways, Polls, Watchtime) that all bind to `MainViewModel` commands through `RelativeSource AncestorType=Window` (ToolsView.xaml:59, 73, 87, ...). I.e., Home → Tools is a second, larger copy of the sidebar's grouped navigation.
- **Integrations card → IntegrationsView**: `IntegrationsViewModel` also EMPTY; view shows third-party bots (BotGhost, ...) each marked "Coming soon" (IntegrationsView.xaml:53-56).

---

## 8. Docs / spec cross-check (discrepancies found)

- `docs/ui/dashboard-view.md` + `dashboard-viewmodel.md`: accurate — confirm there is no DashboardView.xaml and the VM is a 35-line stub. They do not mention that none of the VM's properties are bound.
- `docs/ui/profile-selector.md`: describes `ProfileSelectorWindow.xaml` as "Modal window for profile selection on app startup" — **stale**: the window is never instantiated; the live selector is the inline overlay in MainWindow.xaml:910-1161 driven by MainViewModel. The dead window's VM also has delete-profile support that the live overlay lacks.
- `docs/application/app-entry.md`: startup diagram claims `ConfigureWebHostDefaults (Kestrel, port 5000)` in the host builder and "If no profiles: create default 'Default' profile". Code reality: Kestrel is started on demand by `WebhookCoordinatorService.InitializeAsync()` after profile load (App.xaml.cs:93 comment, MainViewModel.cs:227-228), and first run shows a profile-CREATION overlay rather than silently creating "Default" (App.xaml.cs:337-342). Also lists registrations that no longer exist (`InMemoryChatMessageStore`, `AudioWebSocketServer`, `DemoSetupViewModel`) and misses new ones (`DataChatMessageStore`, watchtime/currency services, etc.).
- `docs/ui/main-window.md`: accurate but shallow (layout summary only).
- `README.md`: aspirational/outdated vs. the code — lists Currency/Commands/YouTube/Giveaways/Polls/Statistics as "Planned (Future)" while all are fully implemented and registered in App.xaml.cs:116-139; says .NET 8.0 while ProfileService.cs:31 comments reference `net9.0-windows`; "Discord: Coming soon" matches the dead Discord home card.
- `vyralbot.html` (722 lines): NOT a UI spec — it is a Tailwind marketing landing-page mockup (hero, feature cards, roadmap FAQ). Only loosely matches the app theme (red/slate); has no dashboard layout spec.

---

## 9. Surprising findings / quirks (Home/Dashboard scope)

1. **Dashboard is a stub**: `DashboardViewModel` contains only zeroed counters with a TODO; home shows NO live data — purely static navigation cards. The sidebar's connected-platform indicators are the only "status" the shell shows.
2. **Home UI lives inside MainWindow.xaml** (DataTemplate, lines 505-769) — there is no DashboardView.xaml file.
3. **Dead buttons in shipped UI**: sidebar "Events & Alerts", "Browser Sources", "Help" (MainWindow.xaml:250, 324, 486); home "Discord" and "Ideas & Suggestions" rows (MainWindow.xaml:692, 731). All render as clickable (`Cursor="Hand"` for home rows) but do nothing.
4. **Dead code files**: `ProfileSelectorWindow.xaml`(+cs)/`ProfileSelectorViewModel.cs` (full standalone selector incl. profile deletion — unreachable), `DemoSetupView.xaml`(+cs)/`DemoSetupViewModel.cs`. `App.AutoConnectTwitchAsync()` never called.
5. **`IsYouTubeConnected` is tracked but never displayed** — no YouTube sidebar entry exists even though YouTube auto-connect works (MainViewModel.cs:515-555).
6. **Sidebar collapse is unbound**: `ToggleSidebarCommand`/`IsSidebarExpanded` implemented in MainViewModel but no XAML uses them; sidebar is fixed 200px.
7. **Memory-obsessed navigation**: every page switch does Dispatcher layout flush + 100ms delay + blocking Gen2 compacting GC (MainViewModel.cs:1846-1867); App startup/exit kill all msedgewebview2 processes machine-wide (could kill unrelated apps' WebView2 processes!) and OnExit runs a 10s process-kill watchdog thread.
8. **Demo wizard leaks credentials to user environment variables** (`VYRALBOT_DEMO_CLIENT_ID/SECRET`) — outside the portable appdata model.
9. **Profile PINs are unsalted SHA256** (ProfileService.cs:407-416); PIN length validation (4-6 digits) exists only in the dead ProfileSelectorViewModel, not in the live overlay (MainWindow accepts any ≤6-char password text and lets ProfileService hash it).
10. **Misleading names**: "NavigateToTwitchSettingsCommand" actually loads `TwitchDashboardViewModel` (MainViewModel.cs:1489-1520); dispose logs say "DemoSetupViewModel" for `GoogleDemoSetupViewModel`.
11. **Scopes are no-ops for the "scoped" ViewModels**: all ViewModels are registered singletons, so `CreateScope().GetRequiredService<T>()` returns the same instance; disposal logging implies fresh instances that never happen.
12. First-run UX: MainWindow shows behind a full-window modal overlay; on first run the only path forward is creating a profile (no cancel/close of the overlay is offered).

*(All planned sections complete. Sections 5-9 added in final pass: demo wizard, documentation page, hub interactions, docs cross-check, quirks.)*
