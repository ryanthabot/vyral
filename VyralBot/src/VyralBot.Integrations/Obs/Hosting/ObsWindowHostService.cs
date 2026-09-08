using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VyralBot.Integrations.Obs.Engine;

namespace VyralBot.Integrations.Obs.Hosting;

/// <summary>
/// Lifecycle of hosting the real obs64.exe window.
/// </summary>
public enum ObsHostState
{
    /// <summary>No OBS Studio install found — or the host has not probed yet.</summary>
    NotInstalled,

    /// <summary>obs64.exe was started; it is still booting.</summary>
    Launching,

    /// <summary>The process is alive but its main Qt window has not been found (yet).</summary>
    WaitingForWindow,

    /// <summary>The OBS main window was found; it is still parented to the desktop, ready to embed.</summary>
    ReadyToEmbed,

    /// <summary>The OBS window is parented into a caller-provided host HWND.</summary>
    Embedded,

    /// <summary>The OBS window is hidden against the service-owned parking HWND.</summary>
    Parked,

    /// <summary>obs64 exited — or (Detail "detached — OBS still running") the window was unembedded while OBS keeps running and can be adopted again.</summary>
    ObsExited,

    /// <summary>The launch attempt failed; <see cref="ObsHostStatus.Detail"/> holds the reason.</summary>
    Failed,
}

/// <summary>Immutable snapshot of the host state for UI binding and diagnostics.</summary>
public sealed record ObsHostStatus(ObsHostState State, string? Detail, int ObsProcessId, IntPtr ObsHwnd);

/// <summary>
/// Owns the real obs64.exe process and its main window: launches (or adopts)
/// OBS, discovers the Qt top-level window by PID, reparents it into a WPF
/// HwndHost HWND, parks/restores it, and restores it to the desktop on exit.
/// OBS is a separate process by design — Dispose never kills it, and a crash
/// there can never take VyralBot down. Polling (window discovery, process
/// exit) runs on the threadpool; <see cref="StatusChanged"/> subscribers must
/// marshal to their own context.
/// </summary>
public sealed class ObsWindowHostService : IDisposable
{
    private const int WindowPollIntervalMs = 250;
    private static readonly TimeSpan defaultWindowTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan exitPollInterval = TimeSpan.FromSeconds(1);

    // Plausibility floor for the main window — transient Qt helper windows are
    // tiny; among candidates the largest area wins.
    private const int MinWindowWidth = 400;
    private const int MinWindowHeight = 300;
    private const string QtClassPrefix = "Qt";
    private const string ObsProcessName = "obs64";

    private readonly ObsInstallLocator locator;
    private readonly ILogger<ObsWindowHostService> logger;
    private readonly object stateLock = new();

    private readonly IntPtr parkingHwnd;

    private ObsHostState state = ObsHostState.NotInstalled;
    private string? detail;
    private Process? process;
    private int processId;
    private IntPtr obsHwnd;
    private nint? originalStyle;
    private IntPtr embeddedHostHwnd;
    private bool processExitedFired;
    private bool exitPollStarted;
    private CancellationTokenSource? exitPollCts;
    private bool disposed;

    public ObsWindowHostService(ObsInstallLocator locator, ILogger<ObsWindowHostService>? logger = null)
    {
        this.locator = locator ?? throw new ArgumentNullException(nameof(locator));
        this.logger = logger ?? NullLogger<ObsWindowHostService>.Instance;

        parkingHwnd = Win32WindowHost.CreateParkingWindow(out int error);
        if (parkingHwnd == IntPtr.Zero)
        {
            this.logger.LogWarning("OBS host: parking window creation failed (win32 error {Error})", error);
        }
    }

    /// <summary>The hidden parking HWND the OBS window is parented to while parked. Created in the constructor.</summary>
    public IntPtr ParkingHwnd => parkingHwnd;

    /// <summary>Raised on a threadpool thread after every state change; subscribers marshal to their own context.</summary>
    public event Action<ObsHostStatus>? StatusChanged;

    /// <summary>Raised once when the obs64 process is observed to have exited (1 s poll).</summary>
    public event Action? ObsProcessExited;

    /// <summary>Lock-protected snapshot of the current state.</summary>
    public ObsHostStatus GetStatus()
    {
        lock (stateLock)
        {
            return SnapshotLocked();
        }
    }

    /// <summary>
    /// Adopts an already-running obs64.exe, or launches the installed one with
    /// <c>--disable-updater</c> only (OBS's own single-instance semantics guard
    /// the config; never <c>--multi</c>). Returns false (state NotInstalled)
    /// when no install is present — acquisition is the UI's job then.
    /// </summary>
    public Task<bool> EnsureRunningAsync()
    {
        ThrowIfDisposed();

        Process? adopted = FindRunningObs();
        if (adopted is not null)
        {
            Adopt(adopted);
            return Task.FromResult(true);
        }

        ObsInstall? install = locator.Locate();
        if (install is null)
        {
            SetState(ObsHostState.NotInstalled, "no OBS Studio installation found");
            return Task.FromResult(false);
        }

        string exePath = Path.Combine(install.BinDir, ObsProcessName + ".exe");
        if (!File.Exists(exePath))
        {
            SetState(ObsHostState.NotInstalled, $"obs64.exe not found under {install.BinDir}");
            return Task.FromResult(false);
        }

        try
        {
            var startInfo = new ProcessStartInfo(exePath, "--disable-updater") { UseShellExecute = true };
            Process? launched = Process.Start(startInfo);
            if (launched is null)
            {
                SetState(ObsHostState.Failed, $"Process.Start returned null for {exePath}");
                return Task.FromResult(false);
            }

            ObsHostStatus snapshot;
            lock (stateLock)
            {
                process = launched;
                processId = launched.Id;
                ResetWindowLocked();
                processExitedFired = false;
                state = ObsHostState.Launching;
                detail = $"launched {exePath}";
                snapshot = SnapshotLocked();
            }

            logger.LogInformation("OBS host: launched obs64 (pid {Pid})", processId);
            StartExitPoll();
            StatusChanged?.Invoke(snapshot);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OBS host: launching obs64 failed");
            SetState(ObsHostState.Failed, $"launching obs64 failed: {ex.Message}");
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Polls (250 ms) for the obs64 main window until it appears or the timeout
    /// (default 30 s — OBS cold start is slow with many plugins) elapses. The
    /// found window's ORIGINAL GWL_STYLE is saved here so UnembedToDesktop can
    /// put it back. On success the state is ReadyToEmbed; embedding happens
    /// only in <see cref="EmbedInto"/>.
    /// </summary>
    public async Task<bool> WaitForWindowAsync(TimeSpan? timeout = null)
    {
        ThrowIfDisposed();

        IntPtr knownHwnd;
        lock (stateLock)
        {
            knownHwnd = obsHwnd;
        }

        if (knownHwnd != IntPtr.Zero)
        {
            return true;
        }

        if (processId == 0)
        {
            return false; // EnsureRunningAsync has not produced a process yet
        }

        SetState(ObsHostState.WaitingForWindow, $"waiting up to {(timeout ?? defaultWindowTimeout).TotalSeconds:0}s for the OBS window");

        using var timeoutCts = new CancellationTokenSource(timeout ?? defaultWindowTimeout);
        try
        {
            while (true)
            {
                IntPtr hwnd = FindObsMainWindow(processId);
                if (hwnd != IntPtr.Zero)
                {
                    OnWindowFound(hwnd);
                    return true;
                }

                await Task.Delay(WindowPollIntervalMs, timeoutCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout — the state stays WaitingForWindow so the UI can offer a retry.
            return false;
        }
    }

    /// <summary>
    /// Reparents the OBS window under <paramref name="hostHwnd"/>, strips its
    /// top-level chrome (keeping WS_POPUP|WS_VISIBLE), sizes it to the host
    /// rect and raises it to the top of the host's children. Throws when no
    /// window was found yet. Call <see cref="ResizeTo"/> right after for the
    /// final WPF layout size.
    /// </summary>
    public void EmbedInto(IntPtr hostHwnd)
    {
        ThrowIfDisposed();
        if (hostHwnd == IntPtr.Zero)
        {
            throw new ArgumentException("The host HWND must be non-zero.", nameof(hostHwnd));
        }

        ObsHostStatus snapshot;
        lock (stateLock)
        {
            if (obsHwnd == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "No OBS window to embed — call EnsureRunningAsync and WaitForWindowAsync first.");
            }

            if (originalStyle is null)
            {
                // First embed: remember the real top-level style for UnembedToDesktop.
                originalStyle = Win32WindowHost.GetWindowLongPtrW(obsHwnd, Win32WindowHost.GwlStyle);
            }

            _ = Win32WindowHost.SetParent(obsHwnd, hostHwnd);

            // Strip chrome in place — everything but the five chrome bits
            // (WS_POPUP and WS_VISIBLE included) survives the mask untouched.
            nint style = Win32WindowHost.GetWindowLongPtrW(obsHwnd, Win32WindowHost.GwlStyle);
            _ = Win32WindowHost.SetWindowLongPtrW(
                obsHwnd, Win32WindowHost.GwlStyle, ObsWindowStyle.StripTopLevelChrome(style));

            // Fill the host's client area; the caller fine-tunes via ResizeTo
            // once WPF layout has settled.
            if (Win32WindowHost.GetWindowRect(hostHwnd, out Win32WindowHost.RECT hostRect))
            {
                _ = Win32WindowHost.MoveWindow(
                    obsHwnd, 0, 0, hostRect.Right - hostRect.Left, hostRect.Bottom - hostRect.Top, repaint: true);
            }

            // HWND_TOP among the host's children — an earlier sibling would
            // otherwise cover OBS.
            _ = Win32WindowHost.SetWindowPos(
                obsHwnd, IntPtr.Zero, 0, 0, 0, 0,
                Win32WindowHost.SwpNoSize | Win32WindowHost.SwpNoMove | Win32WindowHost.SwpNoActivate);

            embeddedHostHwnd = hostHwnd;
            state = ObsHostState.Embedded;
            detail = null;
            snapshot = SnapshotLocked();
        }

        logger.LogInformation("OBS host: embedded into host HWND 0x{HostHwnd:X}", hostHwnd.ToInt64());
        StatusChanged?.Invoke(snapshot);
    }

    /// <summary>Resizes the embedded OBS window to fill the given size (origin 0,0). No-op when not Embedded.</summary>
    public void ResizeTo(int width, int height)
    {
        IntPtr hwnd;
        lock (stateLock)
        {
            if (state != ObsHostState.Embedded || obsHwnd == IntPtr.Zero)
            {
                return;
            }

            hwnd = obsHwnd;
        }

        _ = Win32WindowHost.MoveWindow(hwnd, 0, 0, width, height, repaint: true);
    }

    /// <summary>
    /// Hides the OBS window against the service-owned parking HWND so it
    /// survives view teardown (tab switches, navigation). The process and the
    /// window stay alive; <see cref="RestoreInto"/> brings it back.
    /// </summary>
    public void Park()
    {
        ThrowIfDisposed();

        ObsHostStatus snapshot;
        lock (stateLock)
        {
            if (obsHwnd == IntPtr.Zero || parkingHwnd == IntPtr.Zero)
            {
                logger.LogWarning("OBS host: park skipped (window {HasWindow}, parking {HasParking})", obsHwnd != IntPtr.Zero, parkingHwnd != IntPtr.Zero);
                return;
            }

            _ = Win32WindowHost.SetParent(obsHwnd, parkingHwnd);
            _ = Win32WindowHost.ShowWindow(obsHwnd, Win32WindowHost.SwHide);
            embeddedHostHwnd = IntPtr.Zero;
            state = ObsHostState.Parked;
            detail = null;
            snapshot = SnapshotLocked();
        }

        StatusChanged?.Invoke(snapshot);
    }

    /// <summary>Restores a parked OBS window into the given host HWND.</summary>
    public void RestoreInto(IntPtr hostHwnd)
    {
        // Embed first (reparent while hidden), then show — the reverse order
        // would flash the chrome-stripped window on the desktop for a frame.
        EmbedInto(hostHwnd);

        IntPtr hwnd;
        lock (stateLock)
        {
            hwnd = obsHwnd;
        }

        if (hwnd != IntPtr.Zero)
        {
            _ = Win32WindowHost.ShowWindow(hwnd, Win32WindowHost.SwShow);
        }
    }

    /// <summary>
    /// Detaches the OBS window back to the desktop with its original style at a
    /// sane size — used on app exit. OBS KEEPS RUNNING: the state becomes
    /// ObsExited with Detail "detached — OBS still running" and a later
    /// EnsureRunningAsync adopts the same process again.
    /// </summary>
    public void UnembedToDesktop()
    {
        ObsHostStatus snapshot;
        lock (stateLock)
        {
            if (obsHwnd == IntPtr.Zero)
            {
                return; // nothing ever embedded
            }

            _ = Win32WindowHost.SetParent(obsHwnd, Win32WindowHost.GetDesktopWindow());

            if (originalStyle is { } savedStyle)
            {
                _ = Win32WindowHost.SetWindowLongPtrW(obsHwnd, Win32WindowHost.GwlStyle, savedStyle);
            }

            // A sane windowed size, centered on the desktop.
            if (Win32WindowHost.GetWindowRect(Win32WindowHost.GetDesktopWindow(), out Win32WindowHost.RECT desktop))
            {
                int width = Math.Min(1600, desktop.Right - desktop.Left);
                int height = Math.Min(900, desktop.Bottom - desktop.Top);
                int x = desktop.Left + Math.Max(0, ((desktop.Right - desktop.Left) - width) / 2);
                int y = desktop.Top + Math.Max(0, ((desktop.Bottom - desktop.Top) - height) / 2);
                _ = Win32WindowHost.MoveWindow(obsHwnd, x, y, width, height, repaint: true);
            }

            _ = Win32WindowHost.ShowWindow(obsHwnd, Win32WindowHost.SwShow);

            embeddedHostHwnd = IntPtr.Zero;
            state = ObsHostState.ObsExited;
            detail = "detached — OBS still running";
            snapshot = SnapshotLocked();
        }

        logger.LogInformation("OBS host: window detached to the desktop (OBS pid {Pid} keeps running)", processId);
        StatusChanged?.Invoke(snapshot);
    }

    /// <summary>
    /// Detaches the OBS window (Embedded/Parked) back to the desktop and
    /// destroys the parking HWND. Never kills obs64 — the user's OBS session
    /// must outlive VyralBot.
    /// </summary>
    public void Dispose()
    {
        lock (stateLock)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }

        exitPollCts?.Cancel();
        // The poll task is abandoned on purpose: it observes the cancel within
        // a second and Dispose must never block on it.

        try
        {
            bool wasHosted;
            lock (stateLock)
            {
                wasHosted = state is ObsHostState.Embedded or ObsHostState.Parked;
            }

            if (wasHosted)
            {
                UnembedToDesktop();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OBS host: unembed during dispose failed");
        }

        if (parkingHwnd != IntPtr.Zero && !Win32WindowHost.DestroyWindow(parkingHwnd))
        {
            // DestroyWindow is thread-affine: when Dispose runs on another
            // thread than the constructor, the OS reclaims the window at
            // process exit — harmless for a 10x10 hidden STATIC.
            logger.LogInformation("OBS host: parking window destroy failed (thread-affine); reclaimed at process exit");
        }

        Process? current;
        lock (stateLock)
        {
            current = process;
            process = null;
        }

        current?.Dispose(); // disposes the handle only — never kills OBS
    }

    // ---------------------------------------------------------------------
    // Internals
    // ---------------------------------------------------------------------

    private void Adopt(Process adopted)
    {
        ObsHostStatus snapshot;
        lock (stateLock)
        {
            if (process is { HasExited: false })
            {
                // Already adopted/launched a live obs64 — drop the duplicate probe.
                adopted.Dispose();
                return;
            }

            process = adopted;
            processId = adopted.Id;
            ResetWindowLocked();
            processExitedFired = false;
            state = ObsHostState.WaitingForWindow;
            detail = $"adopted running obs64 (pid {processId})";
            snapshot = SnapshotLocked();
        }

        logger.LogInformation("OBS host: adopted running obs64 (pid {Pid})", processId);
        StartExitPoll();
        StatusChanged?.Invoke(snapshot);
    }

    private static Process? FindRunningObs()
    {
        Process[] candidates;
        try
        {
            candidates = Process.GetProcessesByName(ObsProcessName);
        }
        catch (Exception)
        {
            return null;
        }

        Process? alive = null;
        foreach (Process candidate in candidates)
        {
            if (alive is null)
            {
                try
                {
                    if (!candidate.HasExited)
                    {
                        alive = candidate;
                        continue;
                    }
                }
                catch (Exception)
                {
                    // Exited between listing and probing — fall through to Dispose.
                }
            }

            candidate.Dispose();
        }

        return alive;
    }

    private void OnWindowFound(IntPtr hwnd)
    {
        ObsHostStatus snapshot;
        lock (stateLock)
        {
            obsHwnd = hwnd;
            originalStyle = Win32WindowHost.GetWindowLongPtrW(hwnd, Win32WindowHost.GwlStyle);

            if (state is ObsHostState.Launching or ObsHostState.WaitingForWindow
                or ObsHostState.ObsExited or ObsHostState.Failed or ObsHostState.NotInstalled)
            {
                state = ObsHostState.ReadyToEmbed;
                detail = null;
            }

            snapshot = SnapshotLocked();
        }

        logger.LogInformation("OBS host: main window found (hwnd 0x{Hwnd:X})", hwnd.ToInt64());
        StatusChanged?.Invoke(snapshot);
    }

    /// <summary>
    /// The largest visible, titled, "Qt"-prefixed window of the given PID that
    /// meets the minimum plausible main-window size.
    /// </summary>
    private static IntPtr FindObsMainWindow(int pid)
    {
        IntPtr best = IntPtr.Zero;
        long bestArea = 0;

        _ = Win32WindowHost.EnumWindows((hwnd, _) =>
        {
            if (Win32WindowHost.GetWindowThreadProcessId(hwnd, out uint windowPid) != 0
                && windowPid == (uint)pid
                && Win32WindowHost.IsWindowVisible(hwnd)
                && HasClassNamePrefix(hwnd, QtClassPrefix)
                && HasNonEmptyTitle(hwnd)
                && Win32WindowHost.GetWindowRect(hwnd, out Win32WindowHost.RECT rect))
            {
                long width = rect.Right - rect.Left;
                long height = rect.Bottom - rect.Top;
                if (width >= MinWindowWidth && height >= MinWindowHeight)
                {
                    long area = width * height;
                    if (area > bestArea)
                    {
                        bestArea = area;
                        best = hwnd;
                    }
                }
            }

            return true; // keep enumerating
        }, IntPtr.Zero);

        return best;
    }

    private static bool HasClassNamePrefix(IntPtr hwnd, string prefix)
    {
        var buffer = new StringBuilder(256);
        return Win32WindowHost.GetClassNameW(hwnd, buffer, buffer.Capacity) > 0
            && buffer.ToString().StartsWith(prefix, StringComparison.Ordinal);
    }

    private static bool HasNonEmptyTitle(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        return Win32WindowHost.GetWindowTextW(hwnd, buffer, buffer.Capacity) > 0;
    }

    private void StartExitPoll()
    {
        CancellationTokenSource cts;
        lock (stateLock)
        {
            if (exitPollStarted)
            {
                return;
            }

            exitPollStarted = true;
            cts = exitPollCts = new CancellationTokenSource();
        }

        _ = Task.Run(() => PollProcessExit(cts));
    }

    private async Task PollProcessExit(CancellationTokenSource cts)
    {
        try
        {
            while (!cts.IsCancellationRequested)
            {
                await Task.Delay(exitPollInterval, cts.Token).ConfigureAwait(false);

                Process? current;
                lock (stateLock)
                {
                    current = process;
                }

                if (current is null)
                {
                    continue;
                }

                bool exited;
                try
                {
                    exited = current.HasExited;
                }
                catch (Exception)
                {
                    // Disposed or raced — cancellation/Dispose ends the loop anyway.
                    continue;
                }

                if (!exited)
                {
                    continue;
                }

                bool raiseProcessExited;
                ObsHostStatus snapshot;
                lock (stateLock)
                {
                    raiseProcessExited = !processExitedFired;
                    processExitedFired = true;
                    state = ObsHostState.ObsExited;
                    detail = "obs64 process has exited";
                    snapshot = SnapshotLocked();
                }

                logger.LogInformation("OBS host: obs64 (pid {Pid}) has exited", processId);
                if (raiseProcessExited)
                {
                    ObsProcessExited?.Invoke();
                }

                StatusChanged?.Invoke(snapshot);
                return;
            }
        }
        catch (OperationCanceledException)
        {
            // Disposed — expected shutdown of the poll loop.
        }
    }

    private void ResetWindowLocked()
    {
        // A fresh obs64 process owns a fresh window; stale discovery from a
        // previous process must not leak into the new state.
        obsHwnd = IntPtr.Zero;
        originalStyle = null;
        embeddedHostHwnd = IntPtr.Zero;
    }

    private ObsHostStatus SnapshotLocked() => new(state, detail, processId, obsHwnd);

    private void SetState(ObsHostState newState, string? detail)
    {
        ObsHostStatus snapshot;
        lock (stateLock)
        {
            state = newState;
            this.detail = detail;
            snapshot = SnapshotLocked();
        }

        StatusChanged?.Invoke(snapshot);
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(ObsWindowHostService));
        }
    }
}
