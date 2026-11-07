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

    /// <summary>The OBS main window was found; it is still an unowned desktop window, ready to embed.</summary>
    ReadyToEmbed,

    /// <summary>The OBS window is an owned top-level window floating exactly over the caller's embed area.</summary>
    Embedded,

    /// <summary>The OBS window sits minimized on the desktop (owner cleared) — still reachable via its taskbar button even if Vyral crashes.</summary>
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
/// OBS, discovers the Qt top-level window by PID, makes Vyral its OWNER
/// (GWL_HWNDPARENT — never SetParent: cross-process reparenting breaks the
/// foreign window's DWM paint pipeline) and positions it exactly over the
/// caller's embed area, parks/restores it, and restores it to the desktop on
/// exit. OBS is a separate process by design — Dispose never kills it, and a
/// crash there can never take VyralBot down. Park MINIMIZES the window and
/// clears the ownership (a minimized top-level window keeps its taskbar
/// button), so even a Vyral crash leaves a running, recoverable OBS. A named
/// mutex (<c>Local\VyralBot.ObsWindowHost</c>) keeps a second Vyral instance
/// from stealing the hosted window. Polling (window discovery, process exit,
/// embed health) runs on the threadpool; <see cref="StatusChanged"/>
/// subscribers must marshal to their own context.
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

    // Containment floor for ClampToOwner: when the owner is too small at the
    // requested offset, OBS is anchored at the owner's top-left at this size.
    private const int MinEmbedWidth = 120;
    private const int MinEmbedHeight = 120;

    // Containment verification tolerance of the 1 Hz health check — a couple
    // of pixels of rounding (DPI, chrome metrics) must not trigger a heal.
    private const int ContainmentTolerancePx = 2;

    // Same session by design (Local\): two Vyral instances in one session
    // adopting/embedding the same obs64 would steal the window from each other.
    private readonly string hostMutexName;
    private static readonly TimeSpan hostMutexTimeout = TimeSpan.FromSeconds(2);

    // UIPI silently no-ops the embed when OBS runs elevated and Vyral does not.
    private const string EmbedUipiFailureDetail =
        "Could not embed the OBS window (UIPI). If OBS is running as administrator, restart Vyral as administrator too.";

    private readonly ObsInstallLocator locator;
    private readonly ObsBundledCollectionService? bundledCollection;
    private readonly ILogger<ObsWindowHostService> logger;
    private readonly object stateLock = new();
    private readonly object hostMutexGate = new();

    private ObsHostState state = ObsHostState.NotInstalled;
    private string? detail;
    private Process? process;
    private int processId;
    private IntPtr obsHwnd;
    private nint? originalStyle;

    // Vyral's TOP-LEVEL window — the OBS window's owner (GWL_HWNDPARENT), not
    // its parent. Owner-cleared (IntPtr.Zero) while parked/unembedded.
    private IntPtr ownerHwnd;

    // Last known embed rect in SCREEN pixels — MoveTo/ResizeTo complete each
    // other's coordinates from it, and the health check restores it verbatim.
    private int lastEmbedX;
    private int lastEmbedY;
    private int lastEmbedWidth;
    private int lastEmbedHeight;

    // Containment clip state: OBS's Qt enforces a minimum window size and can
    // therefore REFUSE to shrink to a contained rect (WM_WINDOWPOSCHANGING
    // clamps the proposed geometry). When that happens the window region is
    // clipped to the owner intersection instead (see EnsureContained) —
    // nothing may render outside the Vyral window while embedded.
    private bool clippedToOwner;
    private int clipLeft = -1;
    private int clipTop = -1;
    private int clipRight = -1;
    private int clipBottom = -1;

    /// <summary>
    /// Set when obs64 was ADOPTED while a bundled Vyral collection import was
    /// pending. Writing scene-collection files under a RUNNING OBS is pointless
    /// (OBS rewrites its collections on exit), so the adopt branch never writes
    /// — the UI surfaces this hint instead (restart OBS, or import deliberately
    /// via Settings → Advanced). Cleared again once an import actually happens.
    /// </summary>
    public BundledImportState? PendingBundledImport { get; private set; }

    /// <summary>
    /// Set when the last embed failed because OBS runs elevated and Vyral does
    /// not — the UI offers a "restart as administrator" action in that case.
    /// Read only after a Failed status.
    /// </summary>
    public bool NeedsAdminRestart { get; private set; }
    private bool needsAdminRestart;
    private Mutex? hostMutex;
    private int hostMutexOwnerThreadId = -1;
    private CancellationTokenSource? exitPollCts;
    private bool disposed;

    // Set by MinimizeDetached (the App.OnExit path): the window is already
    // detached, chrome-restored and minimized, so Dispose must NOT run
    // UnembedToDesktop (it would restore + show the window again after exit).
    private bool exitMinimized;

    // Consecutive 1 Hz poll ticks with obs64 alive but NO discoverable window
    // while Embedded — after ZombieWindowTicks this is a dead Qt UI (zombie),
    // not a legitimate recreation gap (scene-collection switches are short).
    private int windowMissingTicks;
    private const int ZombieWindowTicks = 30;

    public ObsWindowHostService(ObsInstallLocator locator, ILogger<ObsWindowHostService>? logger = null, string? hostMutexName = null, ObsBundledCollectionService? bundledCollection = null)
    {
        this.locator = locator ?? throw new ArgumentNullException(nameof(locator));
        this.bundledCollection = bundledCollection;
        this.logger = logger ?? NullLogger<ObsWindowHostService>.Instance;
        // Injectable so tests can isolate from a live Vyral instance holding
        // the default mutex.
        this.hostMutexName = string.IsNullOrWhiteSpace(hostMutexName)
            ? @"Local\VyralBot.ObsWindowHost"
            : hostMutexName!;
    }

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
    public async Task<bool> EnsureRunningAsync()
    {
        ThrowIfDisposed();

        // A fresh hosting session invalidates a previous exit's minimize
        // bookkeeping (Dispose must unembed again for THIS session).
        lock (stateLock)
        {
            exitMinimized = false;
        }

        if (!TryAcquireHostMutex())
        {
            SetState(ObsHostState.Failed, "OBS is already hosted by another Vyral window");
            return false;
        }

        Process? adopted = FindRunningObs();
        if (adopted is not null)
        {
            Adopt(adopted);
            await SurfacePendingBundledImportAsync().ConfigureAwait(true);
            return true;
        }

        ObsInstall? install = locator.Locate();
        if (install is null)
        {
            SetState(ObsHostState.NotInstalled, "no OBS Studio installation found");
            return false;
        }

        string exePath = Path.Combine(install.BinDir, ObsProcessName + ".exe");
        if (!File.Exists(exePath))
        {
            SetState(ObsHostState.NotInstalled, $"obs64.exe not found under {install.BinDir}");
            return false;
        }

        // Seed the bundled Vyral starter collection BEFORE the launch so this
        // boot can start into it. Strictly additive: the service only ever
        // writes Vyral.json into the scenes directory. A launch failure here
        // must not block the launch itself.
        string launchArgs = "--disable-updater";
        if (bundledCollection is not null)
        {
            try
            {
                BundledImportState state = await bundledCollection.DecideAsync(install.SceneCollectionsDir).ConfigureAwait(true);
                if (state.ShouldImport)
                {
                    await bundledCollection.ImportToObsAsync(install.SceneCollectionsDir).ConfigureAwait(true);
                    PendingBundledImport = null;
                    // OBS resolves --collection against its scene collections
                    // directory, so the collection NAME is the argument.
                    launchArgs += $" --collection \"{ObsBundledCollectionService.BundledCollectionName}\"";
                    logger.LogInformation(
                        "OBS host: seeded Vyral starter collection into OBS ({Reason})", state.Reason);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "OBS host: bundled collection import failed — launching without it");
            }
        }

        try
        {
            // obs64 resolves libobs/ plugin DLLs against its working directory —
            // launched from anywhere else it exits within seconds, before any
            // log file is written. UseShellExecute honors WorkingDirectory
            // (ShellExecuteEx lpDirectory).
            var startInfo = new ProcessStartInfo(exePath, launchArgs)
            {
                UseShellExecute = true,
                WorkingDirectory = install.BinDir,
            };
            Process? launched = Process.Start(startInfo);
            if (launched is null)
            {
                SetState(ObsHostState.Failed, $"Process.Start returned null for {exePath}");
                return false;
            }

            ObsHostStatus snapshot;
            lock (stateLock)
            {
                process = launched;
                processId = launched.Id;
                ResetWindowLocked();
                state = ObsHostState.Launching;
                detail = $"launched {exePath}";
                snapshot = SnapshotLocked();
            }

            logger.LogInformation("OBS host: launched obs64 (pid {Pid})", processId);
            RestartExitPoll();
            StatusChanged?.Invoke(snapshot);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OBS host: launching obs64 failed");
            SetState(ObsHostState.Failed, $"launching obs64 failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Adopt-branch counterpart of the launch-branch seed: Decide only —
    /// NEVER write scene-collection files under a running OBS (it would
    /// overwrite them with its in-memory state on exit). A pending import is
    /// exposed via <see cref="PendingBundledImport"/> for the UI to surface.
    /// </summary>
    private async Task SurfacePendingBundledImportAsync()
    {
        if (bundledCollection is null)
        {
            return;
        }

        try
        {
            if (locator.Locate() is not { } install)
            {
                return;
            }

            BundledImportState state = await bundledCollection.DecideAsync(install.SceneCollectionsDir).ConfigureAwait(true);
            if (state.ShouldImport)
            {
                PendingBundledImport = state;
                logger.LogInformation(
                    "OBS host: adopted a running OBS with a pending bundled import ({Reason}) — "
                    + "restart OBS or use Settings → Advanced to import",
                    state.Reason);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OBS host: checking the pending bundled import failed");
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
    /// Makes the OBS window OWNED by <paramref name="ownerHwnd"/> (Vyral's
    /// TOP-LEVEL window — an owned window keeps its normal top-level paint and
    /// DWM behavior but floats above its owner and minimizes with it), strips
    /// its top-level chrome (keeping WS_POPUP|WS_VISIBLE) and MoveWindows it
    /// exactly over the given screen-pixel rect. Throws when no window was
    /// found yet. The control re-syncs position/size on every layout pass via
    /// <see cref="MoveTo"/>/<see cref="ResizeTo"/>.
    /// </summary>
    public void EmbedInto(IntPtr ownerHwnd, int x, int y, int width, int height)
    {
        ThrowIfDisposed();
        if (ownerHwnd == IntPtr.Zero)
        {
            throw new ArgumentException("The owner HWND must be non-zero.", nameof(ownerHwnd));
        }

        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException("The embed rect must be non-empty.", nameof(width));
        }

        ObsHostStatus snapshot;
        bool failed;
        string failDetail;
        lock (stateLock)
        {
            if (IsDetachedByUser)
            {
                throw new InvalidOperationException(
                    "OBS is detached — press \"Put back into Vyral\" to embed it again.");
            }

            if (obsHwnd == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "No OBS window to embed — call EnsureRunningAsync and WaitForWindowAsync first.");
            }

            // Stored BEFORE the clamp so ClampToOwner sees this owner (the
            // failure path below clears it again, as before).
            this.ownerHwnd = ownerHwnd;

            // Containment is enforced here — the authoritative clamp. The
            // control's measurement can be stale/oversized (WPF does not clip
            // children), so the OBS window is intersected with the owner's
            // window rect before anything is moved or stored: OBS can never
            // extend past the Vyral window.
            (x, y, width, height) = ClampToOwner(x, y, width, height);

            // OBS can destroy and recreate its Qt main window shortly after
            // startup, which invalidates the handle we captured — and UIPI
            // (elevated OBS vs non-elevated Vyral) makes the ownership/style
            // changes silent no-ops. Both look identical from the raw return
            // values, so each attempt validates the handle, captures the Win32
            // error, and on failure re-discovers the current window before
            // retrying.
            const int maxAttempts = 3;
            int lastError = 0;
            failed = true;
            failDetail = EmbedUipiFailureDetail;

            for (int attempt = 1; attempt <= maxAttempts && failed; attempt++)
            {
                if (attempt > 1)
                {
                    logger.LogInformation(
                        "OBS host: embed attempt {Attempt}/{Max} — re-discovering the OBS window", attempt, maxAttempts);
                    Thread.Sleep(1200);
                    IntPtr fresh = processId != 0 ? FindObsMainWindow(processId) : IntPtr.Zero;
                    if (fresh == IntPtr.Zero)
                    {
                        lock (stateLock)
                        {
                            fresh = obsHwnd;
                        }
                    }

                    if (fresh == IntPtr.Zero || !Win32WindowHost.IsWindow(fresh))
                    {
                        failDetail = "The OBS window disappeared before it could be embedded — press Relaunch.";
                        break;
                    }

                    lock (stateLock)
                    {
                        if (obsHwnd != fresh)
                        {
                            logger.LogInformation(
                                "OBS host: window handle changed 0x{Old:X} → 0x{New:X} (OBS recreated its window)",
                                obsHwnd.ToInt64(), fresh.ToInt64());
                            obsHwnd = fresh;
                            originalStyle = null; // the recreated window has its own original style
                        }
                    }
                }

                if (!Win32WindowHost.IsWindow(obsHwnd))
                {
                    // Handled by the rediscovery pass on the next attempt.
                    continue;
                }

                if (originalStyle is null)
                {
                    // First embed: remember the real top-level style for UnembedToDesktop.
                    originalStyle = Win32WindowHost.GetWindowLongPtrW(obsHwnd, Win32WindowHost.GwlStyle);
                }

                // Ownership, not parenting: GWL_HWNDPARENT leaves the window a
                // top-level window (its surface, DWM redirection and message
                // flow are untouched) — only the owner link and Z-order above
                // the owner change.
                _ = Win32WindowHost.SetWindowLongPtrW(obsHwnd, Win32WindowHost.GwlHwndParent, ownerHwnd);
                lastError = System.Runtime.InteropServices.Marshal.GetLastWin32Error();

                // Strip chrome in place — everything but the five chrome bits
                // (WS_POPUP and WS_VISIBLE included) survives the mask untouched.
                nint style = Win32WindowHost.GetWindowLongPtrW(obsHwnd, Win32WindowHost.GwlStyle);
                _ = Win32WindowHost.SetWindowLongPtrW(
                    obsHwnd, Win32WindowHost.GwlStyle, ObsWindowStyle.StripTopLevelChrome(style));

                // Cover the embed area exactly; the control re-syncs on every
                // layout pass anyway.
                bool moved = Win32WindowHost.MoveWindow(obsHwnd, x, y, width, height, repaint: true);

                // FRAMECHANGED is mandatory after the style change or the
                // stale non-client metrics keep painting chrome shadows.
                _ = Win32WindowHost.SetWindowPos(
                    obsHwnd, IntPtr.Zero, 0, 0, 0, 0,
                    Win32WindowHost.SwpNoSize | Win32WindowHost.SwpNoMove | Win32WindowHost.SwpNoActivate
                    | Win32WindowHost.SwpFrameChanged);

                // UIPI silently no-ops all of the above when OBS runs elevated
                // and Vyral does not, and the raw return values cannot be
                // trusted (SetWindowLongPtrW hands back the previous value,
                // which can legitimately be NULL/0) — so verify the observable
                // effects instead: ownership set, chrome gone, window placed.
                bool owned = Win32WindowHost.GetWindowLongPtrW(obsHwnd, Win32WindowHost.GwlHwndParent) == ownerHwnd;
                nint styleNow = Win32WindowHost.GetWindowLongPtrW(obsHwnd, Win32WindowHost.GwlStyle);
                failed = !owned
                    || (styleNow & ObsWindowStyle.TopLevelChromeMask) != 0
                    || !moved;

                if (failed)
                {
                    failDetail = $"Could not embed the OBS window (Win32 error {lastError}).";
                    logger.LogWarning(
                        "OBS host: embed attempt {Attempt}/{Max} failed — win32 error {Error}, owned={Owned}",
                        attempt, maxAttempts, lastError, owned);
                }
            }

            if (failed)
            {
                // Best-effort: never leave a half-embedded OBS floating above
                // Vyral in Failed state.
                _ = Win32WindowHost.SetWindowLongPtrW(obsHwnd, Win32WindowHost.GwlHwndParent, IntPtr.Zero);
                this.ownerHwnd = IntPtr.Zero;
                state = ObsHostState.Failed;

                // Distinguish the actionable cause: an elevated OBS cannot be
                // owned by a non-elevated Vyral.
                if (Win32WindowHost.IsProcessElevated(processId) && !Win32WindowHost.IsProcessElevated(Environment.ProcessId))
                {
                    failDetail = EmbedUipiFailureDetail;
                    needsAdminRestart = true;
                }

                detail = failDetail;
            }
            else
            {
                this.ownerHwnd = ownerHwnd;
                lastEmbedX = x;
                lastEmbedY = y;
                lastEmbedWidth = width;
                lastEmbedHeight = height;
                state = ObsHostState.Embedded;
                detail = null;
                needsAdminRestart = false;
                IsDetachedByUser = false;
            }

            snapshot = SnapshotLocked();
        }

        if (failed)
        {
            logger.LogWarning(
                "OBS host: embedding (owner HWND 0x{OwnerHwnd:X}) failed: {Detail}", ownerHwnd.ToInt64(), failDetail);
            StatusChanged?.Invoke(snapshot);
            throw new InvalidOperationException(failDetail);
        }

        logger.LogInformation("OBS host: embedded — owned by HWND 0x{OwnerHwnd:X} at ({X},{Y}) {Width}x{Height}",
            ownerHwnd.ToInt64(), x, y, width, height);

        // Full invalidate — harmless with untouched paint pipelines and cures
        // a stale backing store from before a window recreation.
        _ = Win32WindowHost.RedrawWindow(
            obsHwnd, IntPtr.Zero, IntPtr.Zero,
            Win32WindowHost.RdwInvalid | Win32WindowHost.RdwErase | Win32WindowHost.RdwFrame
            | Win32WindowHost.RdwAllChildren | Win32WindowHost.RdwUpdateNow);
        _ = Win32WindowHost.UpdateWindow(obsHwnd);

        // Containment last word: if OBS refused the contained size, clip it.
        EnsureContained(obsHwnd);

        StatusChanged?.Invoke(snapshot);
    }

    /// <summary>
    /// Sizes the embedded OBS window to the given size, keeping the last known
    /// embed position (screen pixels — the window is a top-level one now, its
    /// origin is the control's embed area, not a host child's 0,0). No-op when
    /// not Embedded.
    /// </summary>
    public void ResizeTo(int width, int height)
    {
        IntPtr hwnd;
        int x;
        int y;
        lock (stateLock)
        {
            if (state != ObsHostState.Embedded || obsHwnd == IntPtr.Zero)
            {
                return;
            }

            hwnd = obsHwnd;
            x = lastEmbedX;
            y = lastEmbedY;

            // Containment clamp — the composed rect can overflow an owner
            // (Vyral) window that shrank since the rect was stored. The
            // clamped values replace the stored ones, so the health check
            // restores the contained rect, not the oversized one.
            (x, y, width, height) = ClampToOwner(x, y, width, height);
            lastEmbedX = x;
            lastEmbedY = y;
            lastEmbedWidth = width;
            lastEmbedHeight = height;
        }

        _ = Win32WindowHost.MoveWindow(hwnd, x, y, width, height, repaint: true);
        _ = Win32WindowHost.RedrawWindow(
            hwnd, IntPtr.Zero, IntPtr.Zero,
            Win32WindowHost.RdwInvalid | Win32WindowHost.RdwErase | Win32WindowHost.RdwAllChildren
            | Win32WindowHost.RdwUpdateNow);
        EnsureContained(hwnd);
    }

    /// <summary>
    /// Moves the embedded OBS window so its top-left sits at the given SCREEN
    /// pixel position, at the last known embed size. No-op when not Embedded.
    /// </summary>
    public void MoveTo(int x, int y)
    {
        IntPtr hwnd;
        int width;
        int height;
        lock (stateLock)
        {
            if (state != ObsHostState.Embedded || obsHwnd == IntPtr.Zero)
            {
                return;
            }

            hwnd = obsHwnd;
            width = lastEmbedWidth;
            height = lastEmbedHeight;

            // Containment clamp — same invariant as ResizeTo: the moved rect
            // must stay inside the owner window, and the stored rect must be
            // the clamped one so self-heal cannot restore an oversized rect.
            (x, y, width, height) = ClampToOwner(x, y, width, height);
            lastEmbedX = x;
            lastEmbedY = y;
            lastEmbedWidth = width;
            lastEmbedHeight = height;
        }

        _ = Win32WindowHost.MoveWindow(hwnd, x, y, width, height, repaint: true);
        _ = Win32WindowHost.RedrawWindow(
            hwnd, IntPtr.Zero, IntPtr.Zero,
            Win32WindowHost.RdwInvalid | Win32WindowHost.RdwErase | Win32WindowHost.RdwAllChildren
            | Win32WindowHost.RdwUpdateNow);
        EnsureContained(hwnd);
    }

    /// <summary>
    /// Minimizes the OBS window and clears its ownership so it does not float
    /// above Vyral while minimized — it survives view teardown (tab switches,
    /// navigation) on the desktop. A minimized top-level window keeps its
    /// taskbar button — even if Vyral crashes right here, OBS stays running
    /// and recoverable. The window keeps its chrome-stripped style;
    /// <see cref="RestoreInto"/> re-owns on re-embed. The process and the
    /// window stay alive.
    /// </summary>
    public void Park()
    {
        ThrowIfDisposed();

        ObsHostStatus snapshot;
        lock (stateLock)
        {
            if (!Win32WindowHost.IsWindow(obsHwnd))
            {
                // Dead handle — user32 calls would act on garbage.
                logger.LogWarning("OBS host: park skipped — window handle 0x{Hwnd:X} is not alive", obsHwnd.ToInt64());
                return;
            }

            _ = Win32WindowHost.ShowWindow(obsHwnd, Win32WindowHost.SwMinimize);
            ClearClipRegion(obsHwnd);
            _ = Win32WindowHost.SetWindowLongPtrW(obsHwnd, Win32WindowHost.GwlHwndParent, IntPtr.Zero);
            ownerHwnd = IntPtr.Zero;
            state = ObsHostState.Parked;
            detail = null;
            snapshot = SnapshotLocked();
        }

        StatusChanged?.Invoke(snapshot);
    }

    /// <summary>
    /// Restores a parked OBS window back under the given owner window: undo
    /// Park's minimize, then re-embed (re-own, re-strip the chrome, position
    /// over the given screen-pixel rect).
    /// </summary>
    public void RestoreInto(IntPtr ownerHwnd, int x, int y, int width, int height)
    {
        IntPtr hwnd;
        lock (stateLock)
        {
            hwnd = obsHwnd;
        }

        if (!Win32WindowHost.IsWindow(hwnd))
        {
            // Dead handle — user32 calls would act on garbage.
            logger.LogWarning("OBS host: restore skipped — window handle 0x{Hwnd:X} is not alive", hwnd.ToInt64());
            return;
        }

        // Undo the minimize first — a minimized window would not paint at the
        // embed rect. EmbedInto then re-owns, re-strips and positions.
        _ = Win32WindowHost.ShowWindow(hwnd, Win32WindowHost.SwRestore);
        EmbedInto(ownerHwnd, x, y, width, height);
    }

    /// <summary>
    /// Detaches the OBS window back to the desktop with its original style at a
    /// sane size — used on app exit. OBS KEEPS RUNNING: the state becomes
    /// ObsExited with Detail "detached — OBS still running" and a later
    /// EnsureRunningAsync adopts the same process again.
    /// </summary>
    /// <summary>
    /// App-exit cleanup: clears ownership and minimizes the OBS window
    /// WITHOUT restoring it — a minimized taskbar entry is the least
    /// intrusive way to leave a running OBS behind when Vyral closes.
    /// </summary>
    public void MinimizeDetached()
    {
        IntPtr hwnd;
        lock (stateLock)
        {
            hwnd = obsHwnd;
        }

        if (!Win32WindowHost.IsWindow(hwnd))
        {
            return;
        }

        ObsHostStatus snapshot;
        lock (stateLock)
        {
            ClearClipRegion(hwnd);

            _ = Win32WindowHost.SetWindowLongPtrW(hwnd, Win32WindowHost.GwlHwndParent, IntPtr.Zero);
            ownerHwnd = IntPtr.Zero;
            if (originalStyle is not null)
            {
                // Give the user back the full chrome — after Vyral closes they
                // interact with OBS through its own taskbar button.
                _ = Win32WindowHost.SetWindowLongPtrW(hwnd, Win32WindowHost.GwlStyle, originalStyle.Value);
            }

            exitMinimized = true;
            state = ObsHostState.Parked;
            detail = "detached — OBS still running";
            snapshot = SnapshotLocked();
        }

        _ = Win32WindowHost.ShowWindow(hwnd, Win32WindowHost.SwMinimize);
        StatusChanged?.Invoke(snapshot);
    }

    /// <summary>True after the user detached OBS via "Open in own window" — automatic embeds are suspended until cleared.</summary>
    public bool IsDetachedByUser { get; set; }

    public void UnembedToDesktop()
    {
        IntPtr hwnd;
        lock (stateLock)
        {
            hwnd = obsHwnd;
        }

        if (!Win32WindowHost.IsWindow(hwnd))
        {
            if (hwnd != IntPtr.Zero)
            {
                // Stale handle (OBS closed its own window): user32 calls would
                // act on garbage — skip them, and do not claim a live detach.
                logger.LogInformation(
                    "OBS host: unembed skipped — window handle 0x{Hwnd:X} is no longer alive", hwnd.ToInt64());
            }

            return;
        }

        ObsHostStatus snapshot;
        lock (stateLock)
        {
            // The containment clip must not survive the detach — the freed
            // window would stay cut.
            ClearClipRegion(hwnd);

            // Drop the ownership first — OBS must not float above Vyral.
            _ = Win32WindowHost.SetWindowLongPtrW(obsHwnd, Win32WindowHost.GwlHwndParent, IntPtr.Zero);
            ownerHwnd = IntPtr.Zero;

            // Guaranteed movable/resizable chrome: the saved "original" style
            // can itself be chrome-stripped (it is re-captured after OBS
            // recreates its window mid-embed), which would leave a title-bar-
            // less window nobody can drag.
            _ = Win32WindowHost.SetWindowLongPtrW(
                obsHwnd, Win32WindowHost.GwlStyle,
                (nint)(Win32WindowHost.WsOverlappedWindow | Win32WindowHost.WsVisible));
            _ = Win32WindowHost.SetWindowPos(
                obsHwnd, IntPtr.Zero, 0, 0, 0, 0,
                Win32WindowHost.SwpNoSize | Win32WindowHost.SwpNoMove
                | Win32WindowHost.SwpNoActivate | Win32WindowHost.SwpFrameChanged);

            // A sane windowed size, centered on the desktop.
            if (Win32WindowHost.GetWindowRect(Win32WindowHost.GetDesktopWindow(), out Win32WindowHost.RECT desktop))
            {
                int width = Math.Min(1600, desktop.Right - desktop.Left);
                int height = Math.Min(900, desktop.Bottom - desktop.Top);
                int x = desktop.Left + Math.Max(0, ((desktop.Right - desktop.Left) - width) / 2);
                int y = desktop.Top + Math.Max(0, ((desktop.Bottom - desktop.Top) - height) / 2);
                _ = Win32WindowHost.MoveWindow(obsHwnd, x, y, width, height, repaint: true);
            }

            _ = Win32WindowHost.ShowWindow(obsHwnd, Win32WindowHost.SwRestore);

            // Detached-but-recoverable: the window is a normal desktop window
            // and automatic embeds stay suspended (IsDetachedByUser) until the
            // user presses "Put back into Vyral".
            IsDetachedByUser = true;
            state = ObsHostState.ReadyToEmbed;
            detail = "detached — running in its own window";
            snapshot = SnapshotLocked();
        }

        logger.LogInformation("OBS host: window detached to the desktop (OBS pid {Pid} keeps running)", processId);
        StatusChanged?.Invoke(snapshot);
    }

    /// <summary>
    /// Detaches the OBS window (Embedded/Parked) back to the desktop and
    /// releases the host mutex. Never kills obs64 — the user's OBS session
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
                // MinimizeDetached (App.OnExit) already detached, chrome-restored
                // and MINIMIZED the window — UnembedToDesktop here would restore
                // and show it again after Vyral closed, defeating the documented
                // exit behavior (a minimized taskbar entry, nothing intrusive).
                wasHosted = state is ObsHostState.Embedded or ObsHostState.Parked
                    && !exitMinimized;
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

        ReleaseHostMutex();

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
            state = ObsHostState.WaitingForWindow;
            detail = $"adopted running obs64 (pid {processId})";
            snapshot = SnapshotLocked();
        }

        logger.LogInformation("OBS host: adopted running obs64 (pid {Pid})", processId);
        RestartExitPoll();
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
            windowMissingTicks = 0;
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

    /// <summary>
    /// Starts a fresh exit poll for the CURRENT process. Called whenever the
    /// process reference is replaced (adopt or relaunch): the previous poll's
    /// generation is cancelled, so a relaunched obs64 is monitored by a new
    /// loop instead of nobody.
    /// </summary>
    private void RestartExitPoll()
    {
        Process? target;
        CancellationTokenSource cts;
        lock (stateLock)
        {
            exitPollCts?.Cancel();
            exitPollCts?.Dispose();
            target = process;
            cts = exitPollCts = new CancellationTokenSource();
        }

        if (target is null)
        {
            return;
        }

        _ = Task.Run(() => PollProcessExit(target, cts));
    }

    /// <summary>
    /// 1 Hz self-heal for the embedded window. OBS is an OWNED top-level
    /// window now, so health = handle alive + still owned by Vyral + visible.
    /// When OBS recreated its Qt main window (the handle died), re-discover
    /// the current one; then re-apply the ownership, show the window and
    /// MoveWindow it back to the last known rect (idempotent with the
    /// control's layout-driven re-sync). Runs on the poll thread.
    /// </summary>
    private void CheckEmbedHealth()
    {
        IntPtr hwnd;
        IntPtr owner;
        int x;
        int y;
        int width;
        int height;
        lock (stateLock)
        {
            if (state != ObsHostState.Embedded || obsHwnd == IntPtr.Zero || ownerHwnd == IntPtr.Zero)
            {
                return;
            }

            hwnd = obsHwnd;
            owner = ownerHwnd;
            x = lastEmbedX;
            y = lastEmbedY;
            width = lastEmbedWidth;
            height = lastEmbedHeight;
        }

        if (!Win32WindowHost.IsWindow(hwnd))
        {
            IntPtr fresh = processId != 0 ? FindObsMainWindow(processId) : IntPtr.Zero;
            if (fresh == IntPtr.Zero || !Win32WindowHost.IsWindow(fresh))
            {
                // OBS is between windows (scene collection load, etc.) — next
                // tick retries. But obs64 alive with NO window for a sustained
                // stretch is a dead Qt UI (zombie): the embed area stays blank
                // forever and Relaunch alone cannot recover it (EnsureRunning
                // would just adopt the same zombie). Surface Failed after the
                // bounded window — never kill OBS by design.
                int missingTicks;
                lock (stateLock)
                {
                    missingTicks = ++windowMissingTicks;
                }

                if (missingTicks >= ZombieWindowTicks)
                {
                    lock (stateLock)
                    {
                        windowMissingTicks = 0;
                    }

                    logger.LogWarning(
                        "OBS host: obs64 (pid {Pid}) has had no window for {Seconds}s — zombie UI, giving up the embed",
                        processId, ZombieWindowTicks);
                    SetState(
                        ObsHostState.Failed,
                        "OBS is running but its window is gone (crashed UI). Close OBS via its tray icon or Task Manager, then press Relaunch.");
                }

                return;
            }

            lock (stateLock)
            {
                windowMissingTicks = 0;
                if (obsHwnd != fresh)
                {
                    logger.LogInformation(
                        "OBS host: window handle changed 0x{Old:X} → 0x{New:X} (OBS recreated its window)",
                        obsHwnd.ToInt64(), fresh.ToInt64());
                    obsHwnd = fresh;
                    originalStyle = null; // the recreated window has its own original style
                    hwnd = fresh;
                }
            }
        }
        else if (Win32WindowHost.GetWindowLongPtrW(hwnd, Win32WindowHost.GwlHwndParent) == owner
            && Win32WindowHost.IsWindowVisible(hwnd))
        {
            lock (stateLock)
            {
                windowMissingTicks = 0;
            }

            SelfHealRectIfDrifted(hwnd, x, y, width, height);
            EnsureContained(hwnd); // a refused resize must never linger unclipped
            return; // healthy
        }
        else
        {
            logger.LogInformation(
                "OBS host: embedded window 0x{Hwnd:X} lost its owner/visibility — re-applying ownership and the last rect",
                hwnd.ToInt64());
        }

        // Show first — a window minimized from outside (taskbar) would not
        // paint at the rect until restored.
        _ = Win32WindowHost.ShowWindow(hwnd, Win32WindowHost.SwShow);

        try
        {
            EmbedInto(owner, x, y, width, height);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OBS host: embed self-heal failed — will retry next tick");
        }
    }

    /// <summary>
    /// Position/size half of the 1 Hz self-heal: ownership and visibility can
    /// be intact while the window rect still drifted (a stale measurement
    /// pushed it around) or SPILLED OUTSIDE THE OWNER (WPF does not clip
    /// children). The stored rect is re-clamped against the CURRENT owner
    /// rect, and when the live rect is more than
    /// <see cref="ContainmentTolerancePx"/> px off that target in either
    /// dimension — or extends past the owner window on any side — MoveWindow
    /// puts it back and redraws; a spill then survives at most one poll tick.
    /// Runs on the poll thread.
    /// </summary>
    private void SelfHealRectIfDrifted(IntPtr hwnd, int x, int y, int width, int height)
    {
        if (!Win32WindowHost.GetWindowRect(hwnd, out Win32WindowHost.RECT live))
        {
            return;
        }

        // Re-clamp against the CURRENT owner rect — Vyral may have shrunk
        // since the rect was stored.
        (x, y, width, height) = ClampToOwner(x, y, width, height);

        int liveWidth = live.Right - live.Left;
        int liveHeight = live.Bottom - live.Top;
        bool sizeDrifted = Math.Abs(liveWidth - width) > 2 || Math.Abs(liveHeight - height) > 2;
        bool spilled = !IsRectInsideOwner(live);
        if (!sizeDrifted && !spilled)
        {
            return;
        }

        logger.LogInformation(
            "OBS host: embedded window rect ({Left},{Top}) {LiveWidth}x{LiveHeight} drifted/spilled from ({X},{Y}) {Width}x{Height} — restoring",
            live.Left, live.Top, liveWidth, liveHeight, x, y, width, height);

        // Persist the clamped rect — the next tick must compare against the
        // contained target, not the oversized one.
        lock (stateLock)
        {
            lastEmbedX = x;
            lastEmbedY = y;
            lastEmbedWidth = width;
            lastEmbedHeight = height;
        }

        _ = Win32WindowHost.MoveWindow(hwnd, x, y, width, height, repaint: true);
        _ = Win32WindowHost.RedrawWindow(
            hwnd, IntPtr.Zero, IntPtr.Zero,
            Win32WindowHost.RdwInvalid | Win32WindowHost.RdwErase | Win32WindowHost.RdwAllChildren
            | Win32WindowHost.RdwUpdateNow);
    }

    /// <summary>
    /// True when <paramref name="rect"/> lies fully inside the owner (Vyral)
    /// window rect, within a <see cref="ContainmentTolerancePx"/> px rounding
    /// tolerance. An unknown owner window answers true — containment cannot
    /// be judged there and the health check must not fight a rect it cannot
    /// see.
    /// </summary>
    private bool IsRectInsideOwner(Win32WindowHost.RECT rect)
    {
        IntPtr owner;
        lock (stateLock)
        {
            owner = ownerHwnd;
        }

        if (owner == IntPtr.Zero || !Win32WindowHost.IsWindow(owner)
            || !Win32WindowHost.GetWindowRect(owner, out Win32WindowHost.RECT ownerRect)
            || IsPhantomRect(ownerRect))
        {
            return true;
        }

        return rect.Left >= ownerRect.Left - ContainmentTolerancePx
            && rect.Top >= ownerRect.Top - ContainmentTolerancePx
            && rect.Right <= ownerRect.Right + ContainmentTolerancePx
            && rect.Bottom <= ownerRect.Bottom + ContainmentTolerancePx;
    }

    /// <summary>
    /// Intersects the desired rect with the owner (Vyral) window rect so OBS
    /// can never extend past the app — the authoritative containment, because
    /// WPF does not clip children and the control's DIPs measurement can be
    /// stale. Keeps at least a minimal usable size; when the intersection
    /// would be smaller, the rect is anchored at the owner's top-left with the
    /// minimal size. Returns the rect unclamped when the owner is unknown or
    /// no longer a window.
    /// </summary>
    private (int X, int Y, int W, int H) ClampToOwner(int x, int y, int w, int h)
    {
        IntPtr owner;
        lock (stateLock)
        {
            owner = ownerHwnd;
        }

        if (owner == IntPtr.Zero || !Win32WindowHost.IsWindow(owner)
            || !Win32WindowHost.GetWindowRect(owner, out Win32WindowHost.RECT ownerRect)
            || IsPhantomRect(ownerRect))
        {
            return (x, y, w, h);
        }

        int left = Math.Max(x, ownerRect.Left);
        int top = Math.Max(y, ownerRect.Top);
        int width = Math.Min(x + w, ownerRect.Right) - left;
        int height = Math.Min(y + h, ownerRect.Bottom) - top;

        if (width < MinEmbedWidth || height < MinEmbedHeight)
        {
            // Degenerate intersection (the owner shrank past the rect's
            // offset) — anchor a minimal rect inside the owner's top-left
            // corner so OBS stays fully visible and usable.
            left = ownerRect.Left;
            top = ownerRect.Top;
            width = Math.Max(1, Math.Min(MinEmbedWidth, ownerRect.Right - ownerRect.Left));
            height = Math.Max(1, Math.Min(MinEmbedHeight, ownerRect.Bottom - ownerRect.Top));
        }

        return (left, top, width, height);
    }

    /// <summary>
    /// Minimized windows report the -32000 phantom rect — containment must
    /// not be enforced against it.
    /// </summary>
    private static bool IsPhantomRect(Win32WindowHost.RECT rect) => rect.Left < -30000 || rect.Top < -30000;

    /// <summary>
    /// Final containment enforcement, run after every move/resize attempt and
    /// by the 1 Hz health check: reads back the LIVE window rect and, when the
    /// window still extends past the owner (Vyral) window, clips its window
    /// region to the intersection. This is needed because OBS's Qt enforces a
    /// minimum window size in WM_WINDOWPOSCHANGING and can silently refuse
    /// the contained size MoveWindow just requested — a refused resize would
    /// otherwise spill outside the app with no recourse. The clip is removed
    /// as soon as the window is contained without it, and whenever the window
    /// is detached back to the desktop. Region coordinates are relative to
    /// the window's client origin, which for the chrome-stripped WS_POPUP
    /// window is its top-left corner.
    /// </summary>
    private void EnsureContained(IntPtr hwnd)
    {
        if (!Win32WindowHost.GetWindowRect(hwnd, out Win32WindowHost.RECT live))
        {
            return;
        }

        IntPtr owner;
        lock (stateLock)
        {
            owner = ownerHwnd;
        }

        if (owner == IntPtr.Zero || !Win32WindowHost.IsWindow(owner)
            || !Win32WindowHost.GetWindowRect(owner, out Win32WindowHost.RECT ownerRect)
            || IsPhantomRect(ownerRect))
        {
            return; // containment cannot be judged — leave the window alone
        }

        if (IsRectInsideOwner(live))
        {
            if (clippedToOwner)
            {
                // Contained again (OBS accepted a smaller size) — no clipping.
                ClearClipRegion(hwnd);
            }

            return;
        }

        int left = Math.Max(ownerRect.Left, live.Left) - live.Left;
        int top = Math.Max(ownerRect.Top, live.Top) - live.Top;
        int right = Math.Min(live.Right, ownerRect.Right) - live.Left;
        int bottom = Math.Min(live.Bottom, ownerRect.Bottom) - live.Top;
        if (right - left < 1 || bottom - top < 1)
        {
            return; // degenerate intersection — keep the current region
        }

        bool alreadyClipped;
        lock (stateLock)
        {
            alreadyClipped = clippedToOwner
                && clipLeft == left && clipTop == top
                && clipRight == right && clipBottom == bottom;
        }

        if (alreadyClipped)
        {
            return; // clip is current — avoid re-applying it every tick
        }

        IntPtr region = Win32WindowHost.CreateRectRgn(left, top, right, bottom);
        if (region == IntPtr.Zero || !Win32WindowHost.SetWindowRgn(hwnd, region, redraw: true))
        {
            return;
        }

        lock (stateLock)
        {
            clippedToOwner = true;
            clipLeft = left;
            clipTop = top;
            clipRight = right;
            clipBottom = bottom;
        }

        logger.LogInformation(
            "OBS host: window refuses the contained size — clipped its region to ({Left},{Top}) {Width}x{Height}",
            left, top, right - left, bottom - top);
    }

    /// <summary>Removes the containment clip region (detach, park, unembed).</summary>
    private void ClearClipRegion(IntPtr hwnd)
    {
        _ = Win32WindowHost.SetWindowRgn(hwnd, IntPtr.Zero, redraw: true);
        lock (stateLock)
        {
            clippedToOwner = false;
            clipLeft = -1;
            clipTop = -1;
            clipRight = -1;
            clipBottom = -1;
        }
    }

    private async Task PollProcessExit(Process target, CancellationTokenSource cts)
    {
        try
        {
            while (!cts.IsCancellationRequested)
            {
                await Task.Delay(exitPollInterval, cts.Token).ConfigureAwait(false);

                bool exited;
                try
                {
                    exited = target.HasExited;
                }
                catch (Exception)
                {
                    // Disposed or raced — cancellation/Dispose ends the loop anyway.
                    continue;
                }

                if (!exited)
                {
                    // OBS with many dock plugins can take longer than the
                    // caller's WaitForWindowAsync timeout to show its window —
                    // keep discovering for the life of the process so the
                    // ReadyToEmbed → embed flow always completes eventually.
                    bool waiting;
                    lock (stateLock)
                    {
                        waiting = state is ObsHostState.Launching or ObsHostState.WaitingForWindow;
                    }

                    if (waiting && processId != 0)
                    {
                        IntPtr hwnd = FindObsMainWindow(processId);
                        if (hwnd != IntPtr.Zero)
                        {
                            OnWindowFound(hwnd);
                        }
                    }

                    CheckEmbedHealth();
                    continue;
                }

                ObsHostStatus snapshot;
                lock (stateLock)
                {
                    // A newer process replaced this poll's target — its own
                    // generation reports the exit.
                    if (!ReferenceEquals(process, target))
                    {
                        return;
                    }

                    process = null;
                    state = ObsHostState.ObsExited;
                    detail = "obs64 process has exited";
                    snapshot = SnapshotLocked();
                }

                logger.LogInformation("OBS host: obs64 (pid {Pid}) has exited", processId);
                target.Dispose(); // the exited process's handle only — never a kill
                ReleaseHostMutex();
                ObsProcessExited?.Invoke();
                StatusChanged?.Invoke(snapshot);
                return;
            }
        }
        catch (OperationCanceledException)
        {
            // Disposed or replaced — expected shutdown of the poll loop.
        }
    }

    private void ResetWindowLocked()
    {
        // A fresh obs64 process owns a fresh window; stale discovery from a
        // previous process must not leak into the new state.
        obsHwnd = IntPtr.Zero;
        originalStyle = null;
        ownerHwnd = IntPtr.Zero;
        lastEmbedX = 0;
        lastEmbedY = 0;
        lastEmbedWidth = 0;
        lastEmbedHeight = 0;
        clippedToOwner = false;
        clipLeft = -1;
        clipTop = -1;
        clipRight = -1;
        clipBottom = -1;
        windowMissingTicks = 0;
    }

    private ObsHostStatus SnapshotLocked() => new(state, detail, processId, obsHwnd);

    /// <summary>
    /// Takes the cross-instance host mutex (a short wait — the other instance
    /// answers within it). An <see cref="AbandonedMutexException"/> means the
    /// previous holder crashed while hosting; ownership passes to us, which is
    /// exactly the recovery we want.
    /// </summary>
    private bool TryAcquireHostMutex()
    {
        lock (hostMutexGate)
        {
            if (hostMutex is not null)
            {
                return true; // this instance already hosts
            }

            var mutex = new Mutex(initiallyOwned: false, hostMutexName);
            bool acquired;
            try
            {
                try
                {
                    acquired = mutex.WaitOne(hostMutexTimeout);
                }
                catch (AbandonedMutexException)
                {
                    acquired = true;
                }
            }
            catch (Exception)
            {
                // The mutex was disposed concurrently (release from the exit
                // poll) — treat as not acquired this round.
                mutex.Dispose();
                return false;
            }

            if (!acquired)
            {
                mutex.Dispose();
                return false;
            }

            hostMutex = mutex;
            hostMutexOwnerThreadId = Environment.CurrentManagedThreadId;
            return true;
        }
    }

    /// <summary>
    /// Releases and disposes the host mutex. ReleaseMutex is thread-affine —
    /// from any other thread the disposed handle simply abandons the mutex,
    /// which the next acquirer recovers from explicitly.
    /// </summary>
    private void ReleaseHostMutex()
    {
        Mutex? mutex;
        int ownerThreadId;
        lock (hostMutexGate)
        {
            mutex = hostMutex;
            hostMutex = null;
            ownerThreadId = hostMutexOwnerThreadId;
            hostMutexOwnerThreadId = -1;
        }

        if (mutex is null)
        {
            return;
        }

        if (ownerThreadId == Environment.CurrentManagedThreadId)
        {
            try
            {
                mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Already released — the dispose below still applies.
            }
        }

        try
        {
            mutex.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
    }

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
