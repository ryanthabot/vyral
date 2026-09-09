using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VyralBot.Data;
using VyralBot.Integrations.Obs.Engine;
using VyralBot.Integrations.Obs.Hosting;
using VyralBot.Integrations.Obs.WebSocket;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// Full OBS tab: hosts the real obs64.exe window inside the page, offers a
/// silent install when OBS is missing, and wires obs-websocket remote control
/// so bot features can drive the running OBS. The window itself is embedded by
/// the ObsWindowEmbedHost control (the view code-behind attaches this VM's
/// <see cref="HostService"/>); this VM owns only status, commands
/// and the websocket client. Every service call is wrapped — OBS is a separate
/// process by design and must never take the page down.
/// </summary>
public partial class ObsHostViewModel : ViewModelBase, IDisposable
{
    private readonly ObsWindowHostService _host;
    private readonly ObsInstallLocator _locator;
    private readonly ObsAcquisitionService _acquisition;
    private readonly IDataService _dataService;
    private readonly ILogger<ObsHostViewModel> _logger;

    // StatusChanged arrives on the threadpool — every handler marshals here.
    private readonly Dispatcher _dispatcher;

    private bool _initialized;
    private bool _statusSubscribed;
    private ObsWebSocketConfig? _remoteConfig;
    private ObsWebSocketClient? _remoteClient;
    private ObsHostState _lastState = ObsHostState.NotInstalled;

    [ObservableProperty]
    private string? _hostStatusText;

    [ObservableProperty]
    private bool _isObsInstalled;

    [ObservableProperty]
    private bool _isHosted;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _acquireProgressText;

    // -1 = indeterminate (ProgressBar DataTrigger in ObsHostView).
    [ObservableProperty]
    private int _acquirePercent;

    /// <summary>True when the embed failed because OBS runs elevated — offers a one-click elevated restart of Vyral.</summary>
    [ObservableProperty]
    private bool _needsAdminRestart;

    /// <summary>True while OBS runs detached in its own window after "Open in own window".</summary>
    [ObservableProperty]
    private bool _isDetached;

    /// <summary>Raised when the Full OBS tab becomes visible again — the embed control re-embeds the parked window.</summary>
    public event Action? EmbedRequested;

    [ObservableProperty]
    private string? _remoteStatusText;

    // Transient one-off line (remote-control enable note, timeout hint…).
    [ObservableProperty]
    private string? _statusNote;

    public ObsHostViewModel(
        ObsWindowHostService host,
        ObsInstallLocator locator,
        ObsAcquisitionService acquisition,
        IDataService dataService,
        ILogger<ObsHostViewModel> logger)
    {
        _host = host;
        _locator = locator;
        _acquisition = acquisition;
        _dataService = dataService;
        _logger = logger;
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    /// <summary>The window host — the view's embed control attaches to it.</summary>
    public ObsWindowHostService HostService => _host;

    /// <summary>GPL attribution for the not-installed acquisition panel.</summary>
    public string AttributionNotice => ObsAcquisitionService.ObsAttributionNotice;

    public string DeembedNoteText =>
        "Everything here is the real OBS — settings, mixer, scenes. Changes apply to OBS everywhere.";

    /// <summary>
    /// Runs once per OBS-page navigation (ObsViewModel.Dispose resets it):
    /// probes the install, enables obs-websocket when needed, launches/adopts
    /// obs64 and waits for its window (the view's embed control completes the
    /// embed), then connects the remote-control websocket best-effort.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        StatusNote = null;

        if (!_statusSubscribed)
        {
            // RaiseStatus fires on the threadpool — ApplyHostStatus marshals.
            _host.StatusChanged += OnHostStatusChanged;
            _statusSubscribed = true;
        }

        IsObsInstalled = _acquisition.IsObsInstalled;
        if (!IsObsInstalled)
        {
            HostStatusText = "OBS Studio is not installed";
            return;
        }

        // The enable path probes for a live obs64 itself: ServerEnabled is
        // only read at OBS startup, so the flag is written only while no
        // obs64 runs — a flip under a live OBS can't take effect and would be
        // overwritten when OBS exits.
        EnableRemoteControlIfNeeded();

        if (_host.GetStatus().State is ObsHostState.Parked or ObsHostState.Embedded or ObsHostState.ReadyToEmbed)
        {
            // OBS already lives (parked by the embed control on the last
            // navigation) — EnsureRunningAsync would ADOPT the process and
            // drop the known window handle (state WaitingForWindow, hidden
            // window undiscoverable). The embed control restores it on Attach.
            ApplyHostStatus(_host.GetStatus());
        }
        else
        {
            if (!await _host.EnsureRunningAsync())
            {
                // IsObsInstalled is owned by ApplyHostStatus: NotInstalled (the
                // locator genuinely failed) already flipped it to false; a mere
                // launch failure (Failed) must not — the status line and
                // Relaunch carry that instead of the acquisition panel.
                return;
            }

            // The window wait is fire-and-forget on purpose: the host service
            // keeps discovering for as long as obs64 runs (its poll promotes
            // Launching/WaitingForWindow → ReadyToEmbed even long after the
            // caller's timeout), and this page must render immediately with
            // the live status instead of holding navigation on a blank page.
            _ = WaitForWindowAndEmbedAsync();
        }

        await ConnectRemoteControlAsync();
    }

    private async Task WaitForWindowAndEmbedAsync()
    {
        try
        {
            if (!await _host.WaitForWindowAsync())
            {
                StatusNote = "OBS did not show its window in time — if a dialog is blocking it, close it and press Relaunch.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Waiting for the OBS window failed");
        }
    }

    // ------------------------------------------------------------------
    // Commands
    // ------------------------------------------------------------------

    private bool CanRelaunch() => !IsBusy
        && _lastState is ObsHostState.ObsExited or ObsHostState.Failed or ObsHostState.WaitingForWindow;

    [RelayCommand(CanExecute = nameof(CanRelaunch))]
    private async Task RelaunchAsync()
    {
        try
        {
            StatusNote = null;
            if (!await _host.EnsureRunningAsync())
            {
                return; // install flag handled via ApplyHostStatus (see InitializeAsync)
            }

            if (!await _host.WaitForWindowAsync())
            {
                StatusNote = "OBS did not show its window in time — if a dialog is blocking it, close it and press Relaunch.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Relaunching the OBS window failed");
            StatusNote = $"Relaunch failed: {ex.Message}";
        }
    }

    private bool CanOpenInOwnWindow() => !IsBusy && IsHosted;

    [RelayCommand(CanExecute = nameof(CanOpenInOwnWindow))]
    private void OpenInOwnWindow()
    {
        try
        {
            _host.UnembedToDesktop();
            IsHosted = false;
            IsDetached = true;
            StatusNote = "OBS detached — running in its own window. Use \"Put back into Vyral\" on the Full OBS tab to re-embed.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Detaching the OBS window to the desktop failed");
            StatusNote = $"Detach failed: {ex.Message}";
        }
    }

    private bool CanReEmbed() => IsDetached && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanReEmbed))]
    private void ReEmbedIntoApp()
    {
        _host.IsDetachedByUser = false;
        IsDetached = false;
        ReEmbedIntoAppCommand.NotifyCanExecuteChanged();
        EmbedRequested?.Invoke(); // the view re-runs the embed for the current area
    }

    private bool CanInstall() => !IsBusy && !IsObsInstalled;

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallObsAsync()
    {
        try
        {
            IsBusy = true;
            AcquirePercent = -1;
            AcquireProgressText = "Resolving the latest OBS Studio release…";
            StatusNote = null;

            await foreach (ObsAcquireProgress progress in _acquisition.DownloadAndInstallAsync())
            {
                AcquirePercent = progress.Percent;
                AcquireProgressText = progress.Detail;

                if (progress.Phase == ObsAcquirePhase.Done)
                {
                    StatusNote = "OBS Studio installed.";
                    // IsObsInstalled is not set here — InitializeAsync re-probes
                    // the locator and ApplyHostStatus owns the flag (same path
                    // as every other status).
                    _initialized = false; // install done → run the launch/embed path
                    await InitializeAsync();
                }
                else if (progress.Phase == ObsAcquirePhase.Failed)
                {
                    StatusNote = $"OBS installation failed: {progress.Detail}";
                }
            }
        }
        catch (OperationCanceledException)
        {
            StatusNote = "OBS installation cancelled.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OBS acquisition failed unexpectedly");
            StatusNote = $"OBS installation failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        RelaunchCommand.NotifyCanExecuteChanged();
        OpenInOwnWindowCommand.NotifyCanExecuteChanged();
        InstallObsCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsObsInstalledChanged(bool value) => InstallObsCommand.NotifyCanExecuteChanged();

    // ------------------------------------------------------------------
    // Remote control (obs-websocket)
    // ------------------------------------------------------------------

    private void EnableRemoteControlIfNeeded()
    {
        try
        {
            ObsInstall? install = _locator.Locate();
            if (install is null)
            {
                return;
            }

            ObsWebSocketConfig? config = ObsWebSocketConfigEditor.Read(install.ConfigDir);
            _remoteConfig = config;
            if (config is { ServerEnabled: true })
            {
                return;
            }

            if (IsObs64Running())
            {
                // The flip can't take effect under a running OBS (the flag is
                // read once at startup) and OBS may overwrite the file with
                // the old value on exit — never write under it.
                StatusNote = "OBS is running with remote control off — enable it in "
                    + "OBS → Tools → WebSocket Server Settings, or close OBS and relaunch from this tab.";
                return;
            }

            if (!ObsWebSocketConfigEditor.EnableServer(install.ConfigDir))
            {
                return;
            }

            // No obs64 was running, so our launch below picks the flag up.
            StatusNote = "Enabled OBS remote control so Vyral bot features can drive this OBS (takes effect on OBS launch).";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reading/enabling the obs-websocket config failed");
        }
    }

    private static bool IsObs64Running()
    {
        try
        {
            Process[] running = Process.GetProcessesByName("obs64");
            foreach (Process process in running)
            {
                process.Dispose();
            }

            return running.Length > 0;
        }
        catch (Exception)
        {
            return false; // probe failed — behave as if OBS were not running
        }
    }

    private async Task ConnectRemoteControlAsync()
    {
        // A fresh client per init — the previous one is disposed here.
        DisposeRemoteClient();

        if (_remoteConfig is not { } config || string.IsNullOrEmpty(config.ServerPassword))
        {
            RemoteStatusText = "Remote control: not configured (no password set in OBS remote-control settings)";
            return;
        }

        var client = new ObsWebSocketClient(config.ServerPassword, config.Port);
        _remoteClient = client;
        client.StatusChanged += OnRemoteStatusChanged;
        RemoteStatusText = "Remote control: connecting…";

        try
        {
            await client.ConnectAsync();
        }
        catch (Exception ex)
        {
            // Unreachable port / disabled server / bad handshake — degrade to a line.
            RemoteStatusText = $"Remote control: {ex.Message}";
        }
    }

    private void OnRemoteStatusChanged(ObsRemoteStatus status) =>
        _dispatcher.InvokeAsync(() =>
        {
            if (status.Error is { } error)
            {
                RemoteStatusText = $"Remote control: {error}";
                return;
            }

            if (!status.Connected)
            {
                return; // transient close — the connect result already set a line
            }

            RemoteStatusText = "Remote control: connected"
                + (string.IsNullOrWhiteSpace(status.CurrentScene) ? "" : $" — scene {status.CurrentScene}")
                + (status.StreamActive ? " • stream live" : "");
        });

    // ------------------------------------------------------------------
    // Host status
    // ------------------------------------------------------------------

    private void OnHostStatusChanged(ObsHostStatus status) =>
        _dispatcher.InvokeAsync(() => ApplyHostStatus(status));

    private void ApplyHostStatus(ObsHostStatus status)
    {
        _lastState = status.State;

        // The acquisition panel keys off a genuine locate failure only: the
        // host reports NotInstalled exactly when the locator finds no usable
        // install. Every other state (ObsExited, Failed, Launching, …) proves
        // OBS exists or ran — a late status from the singleton's exit poll
        // must never resurrect "not installed" next to "obs64 has exited".
        IsObsInstalled = status.State != ObsHostState.NotInstalled;
        NeedsAdminRestart = status.State == ObsHostState.Failed && _host.NeedsAdminRestart;

        HostStatusText = status.State switch
        {
            ObsHostState.NotInstalled => "OBS Studio is not installed",
            ObsHostState.Launching => "Launching OBS…",
            ObsHostState.WaitingForWindow => "Waiting for the OBS window…",
            ObsHostState.ReadyToEmbed => "OBS window ready",
            ObsHostState.Embedded => "OBS embedded",
            ObsHostState.Parked => "OBS running (background)",
            ObsHostState.ObsExited => status.Detail ?? "OBS was closed",
            ObsHostState.Failed => status.Detail ?? "Starting OBS failed",
            _ => status.Detail,
        };
        IsHosted = status.State == ObsHostState.Embedded;
        if (status.State == ObsHostState.ReadyToEmbed && IsDetached)
        {
            HostStatusText = "OBS detached — running in its own window";
        }

        RelaunchCommand.NotifyCanExecuteChanged();
        OpenInOwnWindowCommand.NotifyCanExecuteChanged();
        RestartAsAdminCommand.NotifyCanExecuteChanged();
        ReEmbedIntoAppCommand.NotifyCanExecuteChanged();
    }

    private bool CanRestartAsAdmin() => NeedsAdminRestart;

    [RelayCommand(CanExecute = nameof(CanRestartAsAdmin))]
    private void RestartAsAdmin()
    {
        try
        {
            if (string.IsNullOrEmpty(Environment.ProcessPath))
            {
                StatusNote = "Could not locate the Vyral executable to restart elevated.";
                return;
            }

            Process.Start(new ProcessStartInfo(Environment.ProcessPath)
            {
                UseShellExecute = true,
                Verb = "runas", // triggers the elevation prompt
            });
            Environment.Exit(0); // the elevated instance takes over
        }
        catch (Exception ex)
        {
            // User cancelled the UAC prompt (or elevation was refused).
            StatusNote = $"Elevation cancelled: {ex.Message}";
        }
    }

    // ------------------------------------------------------------------
    // Teardown
    // ------------------------------------------------------------------

    /// <summary>
    /// Called by ObsViewModel on every Full OBS tab visibility change: leaving
    /// the tab parks (minimizes) the OBS window — the HwndHost is NOT torn
    /// down by a mere Visibility collapse, so without this the owned window
    /// keeps floating above everything; returning to the tab raises
    /// <see cref="EmbedRequested"/> so the control re-embeds it.
    /// </summary>
    public void OnFullObsTabVisibilityChanged(bool visible)
    {
        if (visible)
        {
            EmbedRequested?.Invoke();
        }
        else
        {
            ParkHostWindow();
        }
    }

    private void ParkHostWindow()
    {
        try
        {
            if (IsDetached)
            {
                return; // user chose own-window mode — leave their window alone
            }

            if (_host.GetStatus().State is ObsHostState.Embedded or ObsHostState.ReadyToEmbed)
            {
                _host.Park();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Parking the OBS window on tab leave failed");
        }
    }

    /// <summary>
    /// Drops the status/websocket wiring on navigation away. The OBS window is
    /// parked (minimized to the taskbar, ownership cleared) so it cannot float
    /// above other pages or apps; the host service itself is an app singleton
    /// and App.OnExit minimizes it for exit.
    /// </summary>
    public void Dispose()
    {
        ParkHostWindow();

        if (_statusSubscribed)
        {
            _host.StatusChanged -= OnHostStatusChanged;
            _statusSubscribed = false;
        }

        DisposeRemoteClient();
        _initialized = false;
    }

    private void DisposeRemoteClient()
    {
        if (_remoteClient is null)
        {
            return;
        }

        ObsWebSocketClient client = _remoteClient;
        _remoteClient = null;
        client.StatusChanged -= OnRemoteStatusChanged;
        try
        {
            client.Dispose();
        }
        catch (Exception)
        {
            // Teardown path — a wedged socket must never fault the UI thread.
        }
    }
}
