# Host the real OBS window inside VyralBot's OBS tab — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** The OBS tab embeds the actual obs64.exe application window (100% OBS: audio mixer, settings, source properties, all scenes in the user's order, streaming with their encoders). Machines without OBS get a one-click official-installer acquisition. Config is inherently shared (`%APPDATA%\obs-studio`). The native libobs "Bot Studio" remains a secondary automation console.

**Verified environment facts:** OBS 32.1.1 at `C:\Program Files\obs-studio\bin\64bit\obs64.exe`; user has RTX 4070 Ti; obs-websocket config at `%APPDATA%\obs-studio\plugin_config\obs-websocket\config.json` (keys: `alerts_enabled`, `auth_required=true`, `first_load`, `server_enabled=false`, `server_password` plain-text set, `server_port=4455`); OBS launch flags documented (`--multi`, `--disable-updater`, `--collection/--profile/--scene`, `--startstreaming`); installer = NSIS, GitHub releases asset `OBS-Studio-<ver>-Windows-Installer-x64.exe`, `/S` silent (community-confirmed); obs-websocket v5: Hello(0) → Identify(1) auth `base64(SHA256(base64(SHA256(password+salt))+challenge))`, close 4009 on failure; requests `SetCurrentProgramScene`/`StartStream`/`StopStream`/`GetCurrentProgramScene`; events `CurrentProgramSceneChanged`/`StreamStateChanged`.

## Global Constraints

- Zero new NuGet packages (`ClientWebSocket`, user32 P/Invoke only).
- All 335 existing tests stay green; build clean; conventional commits (`feat(obs): …`).
- OBS binaries are never committed — downloaded/installed at user consent only.
- The hosted OBS is a SEPARATE process: a crash there must never take Vyral down; the native libobs engine must NOT auto-boot while hosted mode is the active tab (they are independent engines).
- Secrets: never log the websocket password.
- House style: sealed classes, normal ctors, records for data, DynamicResource brushes, valid PackIconKind.

## Current code anchors

- `VyralBot\src\VyralBot.UI\Views\ObsView.xaml` — Studio|Collections radio pills (`ObsTab` style, lines 17-42), tabs at 66-87, Studio embed at 92-93 (`<views:ObsStudioView DataContext="{Binding Studio}"/>`), Collections content 96-193.
- `VyralBot\src\VyralBot.UI\ViewModels\ObsViewModel.cs` — `Studio` property (line 24), `InitializeAsync` = LoadAsync + Studio.InitializeAsync (60-65), `SelectTab` command (75), `Dispose` chains Studio (71).
- `VyralBot\src\VyralBot.UI\Controls\ObsPreviewHost.cs` — HwndHost pattern to copy (CreateWindowExW STATIC child in BuildWindowCore 84-95, destroy 97-106, size handling 108-122, single-flight create 131-183).
- `VyralBot\src\VyralBot.Integrations\Obs\Engine\ObsEngine.cs` — `IsReady`(95), `HasLoadedNativeState`(105), `StatusChanged`(114), `GetStatus`(125), `RunNativeAsync<T>`(145), `InitializeAsync`(222), `PrepareExitAsync`(289), `ShutdownAsync`(327); `IObsNativeGate` interface.
- `VyralBot\src\VyralBot.UI\App.xaml.cs` — selftest arg check line 431; Obs DI block 309-325; OnExit OBS sequence 946-996 (StopAllAsync → PrepareExitAsync → conditional ShutdownAsync).
- `VyralBot\src\VyralBot.UI\ViewModels\MainViewModel.cs` — `NavigateToObsAsync` 1885-1908 (DI scope per navigation).
- `VyralBot\src\VyralBot.Integrations\Obs\Engine\ObsInstallLocator.cs` — `ObsInstall(Root, BinDir, ObsDllPath, PluginsBinDir, PluginsDataDir, UserPluginsBinDir?, UserPluginsDataDir?, ConfigDir, SceneCollectionsDir, ProfilesDir)`.
- `VyralBot\src\VyralBot.Data\IDataService.cs:165-167` — `GetConfigValueAsync(key)` / `SetConfigValueAsync(key, value, category=null)`.
- Native studio carry-over anchors: `ObsStudioService.GetScenesAsync` (scene ordering fix), `ObsOutputService.StartStreamAsync` + `ObsOutputPlanner` (encoder fallback logging fix).

---

### Task 1: ObsWindowHostService + Win32 interop + websocket-config enabler

**Files:** Create `VyralBot\src\VyralBot.Integrations\Obs\Hosting\ObsWindowHostService.cs`, `...\Hosting\Win32WindowHost.cs` (user32 P/Invoke), `...\Hosting\ObsWebSocketConfigEditor.cs`. Test: `tests\VyralBot.UI.Tests\ObsWindowHostTests.cs`.

**Produces:**
```csharp
public enum ObsHostState { NotInstalled, Launching, WaitingForWindow, Embedded, Parked, ObsExited, Failed }
public sealed record ObsHostStatus(ObsHostState State, string? Detail, int ObsProcessId, IntPtr ObsHwnd);
public sealed class ObsWindowHostService : IDisposable
{
    public event Action<ObsHostStatus>? StatusChanged;         // threadpool; UI marshals
    public ObsHostStatus GetStatus();
    public Task<bool> EnsureRunningAsync();                    // adopt running obs64.exe (by process name in the user session) else launch Root\bin\64bit\obs64.exe --disable-updater; returns false when OBS not installed
    public Task<bool> WaitForWindowAsync(TimeSpan timeout);    // poll EnumWindows by PID: visible, has title, class name starts "Qt"
    public void EmbedInto(IntPtr hostHwnd);                    // SetParent + strip WS_CAPTION|WS_THICKFRAME|WS_SYSMENU|WS_MINIMIZEBOX|WS_MAXIMIZEBOX (keep WS_POPUP|WS_VISIBLE) + MoveWindow fill + SetWindowPos top
    public void ResizeTo(int width, int height);
    public void Park();                                        // SetParent to hidden app-owned parking HWND + ShowWindow(SW_HIDE)
    public void RestoreInto(IntPtr hostHwnd);                  // ShowWindow(SW_SHOW) + EmbedInto
    public void UnembedToDesktop();                            // SetParent(GetDesktopWindow()) + restore saved styles + show — used on app exit (leaves OBS running)
    public event Action? ObsProcessExited;                     // poll 1 s; also fire when user closes OBS
}
public static class ObsWebSocketConfigEditor
{
    // Read/enable without touching the password:
    public static ObsWebSocketConfig Read(string configDir);   // record(string? ServerPassword, int Port, bool ServerEnabled, bool AuthRequired)
    public static bool EnableServer(string configDir);          // rewrite JSON with server_enabled=true preserving all other fields (System.Text.Json round-trip); returns false if file missing
}
```
Win32 imports in `Win32WindowHost` (user32, SetLastError=true): `SetParent`, `GetParent`, `MoveWindow`, `ShowWindow`, `SetWindowLongPtrW`/`GetWindowLongPtrW` (delegate to SetWindowLongW on 32-bit — project is x64-pinned), `SetWindowPos`, `EnumWindows`, `GetWindowThreadProcessId`, `IsWindowVisible`, `GetClassNameW`, `GetWindowTextW`, `GetDesktopWindow`, `CreateWindowExW`/`DestroyWindow` (parking HWND: WS_OVERLAPPED hidden, message-only-adjacent). Style constants documented.
Behavior details: launch via `Process.Start(ProcessStartInfo { FileName, Arguments = "--disable-updater", UseShellExecute = true })`; adopt = find process named "obs64" first; after launch poll window up to 30 s (OBS cold start can be slow with many plugins); save the ORIGINAL style on first embed and restore it on UnembedToDesktop; parking HWND owned by the service (not WPF) so it survives view teardown. When OBS not installed → state NotInstalled (acquisition is Task 2b's UI).
**Tests (pure):** style-mask math (strip helper: given 0x14CF0000 → expected masked value), `ObsWebSocketConfigEditor.EnableServer` against a temp config.json (flips flag, preserves password + unknown fields; missing file → false; corrupt JSON → false, no throw). Service itself is live-verified (Task 6).
- [ ] Tests first (RED) → implement → GREEN → build → commit `feat(obs): obs64 process+window host service with parking and websocket config enabler`

### Task 2: ObsWebSocketClient (v5)

**Files:** Create `VyralBot\src\VyralBot.Integrations\Obs\WebSocket\ObsWebSocketClient.cs`, `...\WebSocket\ObsWebSocketAuth.cs`, `...\WebSocket\ObsWebSocketConfigReader.cs`. Test: `tests\VyralBot.UI.Tests\ObsWebSocketClientTests.cs`.

**Produces:**
```csharp
public static class ObsWebSocketAuth
{
    public static string ComputeAuthentication(string password, string salt, string challenge); // base64(SHA256(base64(SHA256(pw+salt))+challenge)) — UTF8 throughout
}
public sealed record ObsRemoteStatus(bool Connected, string? CurrentScene, bool StreamActive, string? Error);
public sealed class ObsWebSocketClient
{
    public ObsWebSocketClient(string password, int port = 4455);   // password from ObsWebSocketConfigEditor.Read
    public event Action<ObsRemoteStatus>? StatusChanged;
    public bool IsConnected { get; }
    public Task ConnectAsync(CancellationToken ct = default);       // ws://localhost:{port}; Hello→Identify(auth); 4009 → friendly error
    public Task DisconnectAsync();
    public Task<string?> GetCurrentProgramSceneAsync();             // request "GetCurrentProgramScene"
    public Task SetCurrentProgramSceneAsync(string sceneName);      // "SetCurrentProgramScene"
    public Task StartStreamAsync(); public Task StopStreamAsync();  // "StartStream"/"StopStream" (StartStream when already active → surfaced as result, not throw)
}
```
Implementation: `System.Net.WebSockets.ClientWebSocket`, receive loop (Task.Run) dispatching opcodes: 0 Hello (capture salt/challenge → send Identify with auth + rpcVersion 1), 2 EventConfirmed, 5 Event (`CurrentProgramSceneChanged` → update+raise; `StreamStateChanged` → update), 7 RequestResponse (match `requestId`; 100=success else surface `comment`), 9 RequestBatch ignored. Requests: send op 6 with `requestType` + `requestId` (Guid) + `requestData`, await via TaskCompletionSource map (timeout 5 s). Reconnect: none in v1 (UI re-invokes Connect). Thread-safe send via SemaphoreSlim.
`ObsWebSocketConfigReader`: thin wrapper returning the client's password/port from `ObsWebSocketConfigEditor.Read(configDir)`.
**Tests (pure):** auth vector — compute a known-input result and assert the exact double-hash chain against an independently-computed expected value in the test (implement SHA256 inline in the test, not by calling the SUT twice); config reader via temp file. Connection flow is live-verified (Task 6).
- [ ] Tests first → implement → GREEN → build → commit `feat(obs): obs-websocket v5 client with auth and remote control requests`

### Task 3a: ObsHostWindowControl + ObsAcquisitionService + not-installed UI

**Files:** Create `VyralBot\src\VyralBot.UI\Controls\ObsWindowEmbedHost.cs` (HwndHost), `VyralBot\src\VyralBot.Integrations\Obs\Hosting\ObsAcquisitionService.cs`. Modify: nothing yet (Task 4 wires UI).
**Produces:**
```csharp
public sealed class ObsWindowEmbedHost : HwndHost   // copy ObsPreviewHost's BuildWindowCore STATIC-child pattern
{
    // Attach(ObsWindowHostService svc): on Loaded + ActualWidth>0 → svc.EmbedInto(childHwnd); Park on Unloaded; ResizeTo on OnRenderSizeChanged
    // DependencyProperty IsActive (bindable) → bool for a "hosted" badge
}
public sealed record ObsAcquireProgress(ObsAcquirePhase Phase, int Percent, string Detail); // Downloading/Installing/Done/Failed
public sealed class ObsAcquisitionService
{
    public ObsAcquisitionService(ObsInstallLocator locator);
    public bool IsObsInstalled { get; }               // locator.Locate() != null
    public IAsyncEnumerable<ObsAcquireProgress> DownloadAndInstallAsync(CancellationToken ct);
    // GitHub releases latest: GET https://api.github.com/repos/obsproject/obs-studio/releases/latest (User-Agent header REQUIRED by GitHub API)
    //   → pick asset name matching "OBS-Studio-*-Windows-Installer-x64.exe" → browser_download_url → download to %TEMP% with progress
    //   → Process.Start(installerPath, "/S") wait exit (log only; no elevation logic — NSIS elevates itself) → re-run locator
}
```
GPL attribution: a const string `ObsAttributionNotice` (OBS Studio © OBS Project, GPL-2.0 — link to source) exposed for the UI panel.
- [ ] Implement; pure-test what's testable (none trivial — skip tests, live-verified Task 6) → build → commit `feat(obs): window embed host control and official installer acquisition service`

### Task 3b: native-studio carry-over fixes

**Files:** Modify `VyralBot\src\VyralBot.Integrations\Obs\Engine\ObsStudioService.cs` (GetScenesAsync: order scenes by the last load plan's `SceneOrder` — the loader must retain the plan; store `IReadOnlyList<string> LastSceneOrder` on the loader or return it in `ObsLoadResult` and let the service sort), `VyralBot\src\VyralBot.Integrations\Obs\Engine\ObsSceneCollectionLoader.cs` (expose SceneOrder), `...\ObsOutputService.cs` + `ObsOutputPlanner.cs` (step logging: log can_begin/initialize/start results and the chosen encoder id distinctly; diagnose why `nvenc` creation logged success then pipeline stalled — add a log after EVERY step of StartStreamAsync so a stall is locatable; verify fallback actually tries `jim_nvenc` then `obs_x264` when `obs_video_encoder_create` returns 0).
- [ ] Implement + tests where pure (planner logging strings) → build + full suite green → commit `fix(obs): scene order from collection, stream start step logging and encoder fallback`

### Task 4: UI rework — three tabs, hosted default

**Files:** Modify `VyralBot\src\VyralBot.UI\Views\ObsView.xaml`, `ViewModels\ObsViewModel.cs`, `Views\ObsStudioView.xaml` (rename tab label), `App.xaml.cs` (DI), `MainViewModel.cs` (no change expected — nav exists), create `VyralBot\src\VyralBot.UI\Views\ObsHostView.xaml` (+.cs) + `ViewModels\ObsHostViewModel.cs`.
**Design:**
- Tabs become **Full OBS | Bot Studio | Collections** (three radio pills, same ObsTab style). Default selected: **Full OBS**.
- `ObsHostViewModel` (inject `ObsWindowHostService`, `ObsInstallLocator`, `ObsAcquisitionService`, `ObsWebSocketClient` factory use, `IDataService`): state surface — `HostStatusText` (from ObsHostStatus states: "Launching OBS…", "Waiting for the OBS window…", "OBS embedded", "OBS was closed — Relaunch"), `IsObsInstalled`, acquisition commands (`InstallObsCommand` → acquisition progress → on Done refresh + auto-embed), `RelaunchObsCommand`, `OpenInOwnWindowCommand` (UnembedToDesktop), `IsHosted` badge. On `InitializeAsync`: if OBS installed → `EnsureRunningAsync` + `WaitForWindowAsync` (this is where the websocket config enabler runs BEFORE first launch: if `ObsWebSocketConfigEditor.Read(ConfigDir).ServerEnabled == false` → `EnableServer` + status note "Enabled OBS remote control so Vyral bot features can drive this OBS"). The websocket client Connect happens here too (best-effort; failure → silent log — bot features degrade).
- `ObsHostView.xaml`: toolbar strip (status + Relaunch + Open in own window buttons + hosted badge), `controls:ObsWindowEmbedHost` filling the card; NotInstalled state → acquisition panel ("OBS Studio isn't installed yet." + Install button + progress bar + GPL attribution notice + hint that Bot Studio still works without OBS).
- `ObsView.xaml`: three tabs; **Full OBS visible by default**; `ObsViewModel`: `SelectTab` extended ("FullObs"/"BotStudio"/"Collections"); `InitializeAsync` gates `Studio.InitializeAsync()` (native engine boot) to only run when the Bot Studio tab is FIRST opened (`SelectTab("BotStudio")` triggers it once, lazily; guard flag) — Full OBS default must not boot libobs.
- `ObsStudioView` unchanged functionally (it becomes the Bot Studio tab content).
- DI: `ObsWindowHostService`, `ObsAcquisitionService`, `ObsHostViewModel` singletons. App OnExit: `ObsWindowHostService.UnembedToDesktop()` (leave OBS running) BEFORE the existing OBS engine sequence.
- [ ] Implement → build + full tests → manual smoke (launch app → OBS tab shows the real OBS window; switch tabs and back — window persists; close app → OBS still running unembedded) → commit `feat(obs): full OBS hosting as the default tab with bot studio and collections`

### Task 5 (was Wave 4): verification + review + ship

- [ ] `dotnet build` + `dotnet test` (all green) → live verification (computer-use): OBS tab → real OBS embedded (screenshot), open OBS Settings inside it, switch to Bot Studio and back (park/restore), websocket `SetCurrentProgramScene` against the hosted OBS, app close leaves OBS running; no-OBS smoke via locator override.
- [ ] Code-review agent over `git diff <pre-feature>..HEAD`; repair pass; commit `fix(obs): review pass`; push to `vyral main`.

## Risks (accepted)
Qt top-level window reparenting quirks (menus/settings dialogs open as separate top-level windows above the app); keyboard focus follows OBS while hovered/focused; OBS updates could change window discovery (mitigated: PID-based, not title-based); websocket enabling writes one boolean into the user's OBS plugin config (disclosed, password untouched).
