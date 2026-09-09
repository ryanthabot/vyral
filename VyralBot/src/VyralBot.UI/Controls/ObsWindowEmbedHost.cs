using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using VyralBot.Integrations.Obs.Hosting;

namespace VyralBot.UI.Controls;

/// <summary>
/// Hosts the real obs64.exe window inside WPF: the
/// <see cref="ObsWindowHostService"/> makes Vyral's TOP-LEVEL window the OBS
/// window's OWNER (never SetParent — cross-process reparenting breaks the
/// foreign window's DWM paint pipeline) and this control keeps the owned
/// top-level window positioned exactly over its own area. The HwndHost's
/// STATIC child HWND stays (the base class requires one) but is only a
/// placeholder — position sync reads this control's screen rect directly.
/// Position sync runs on <see cref="LayoutUpdated">LayoutUpdated</see> (every
/// layout pass) and the host Window's LocationChanged (a window DRAG changes
/// screen coordinates without changing WPF layout at all); each sync is
/// change-throttled. Detach and HWND teardown PARK the OBS window — minimized
/// with ownership cleared (taskbar button kept) — so the process and window
/// survive navigation, and even a Vyral crash leaves a recoverable OBS; a
/// later Attach restores it. <see cref="StatusChanged"/> arrives on
/// threadpool threads: embed attempts marshal to the dispatcher (PointToScreen
/// is UI-thread-only), and the <see cref="IsActive"/> dependency property is
/// written through a dispatcher guard.
/// </summary>
public sealed class ObsWindowEmbedHost : HwndHost
{
    private const uint WsChild = 0x4000_0000;
    private const uint WsVisible = 0x1000_0000;
    private const uint GaRoot = 2;

    private ObsWindowHostService? service;
    private IntPtr childHwnd;
    private bool disposed;
    private bool statusSubscribed;
    private int embedding; // 1 while an embed attempt is in flight (attach, Loaded and StatusChanged can race)

    // The window hooked for LocationChanged (null until first hooked); the
    // owner HWND itself is re-resolved per embed attempt (cheap, and immune
    // to window recreation).
    private Window? hookedWindow;
    private bool layoutSubscribed;

    // Last rect pushed to the service (screen px) — LayoutUpdated is chatty,
    // so sync only when something actually changed.
    private int lastSyncX = -1;
    private int lastSyncY = -1;
    private int lastSyncWidth = -1;
    private int lastSyncHeight = -1;

    /// <summary>True while the OBS window lives (owned and positioned) over this host; bindable for a "hosted" badge.</summary>
    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(
            nameof(IsActive), typeof(bool), typeof(ObsWindowEmbedHost),
            new PropertyMetadata(false));

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        private set => SetIsActive(value);
    }

    public ObsWindowEmbedHost()
    {
        Loaded += static (sender, _) => ((ObsWindowEmbedHost)sender).EmbedBestEffort();
    }

    /// <summary>
    /// Binds the host to the OBS window service and embeds immediately when the
    /// host is already sized and OBS is ReadyToEmbed/Embedded/Parked. Otherwise
    /// it subscribes to <see cref="ObsWindowHostService.StatusChanged"/> and
    /// completes the embed once OBS reaches an embeddable state.
    /// </summary>
    /// <summary>Re-runs the embed attempt — used when the Full OBS tab becomes visible again.</summary>
    public void EnsureEmbedded() => EmbedBestEffort();

    public void Attach(ObsWindowHostService service)
    {
        if (this.service is { } previous && !ReferenceEquals(previous, service))
        {
            UnsubscribeStatus(previous);
        }

        this.service = service ?? throw new ArgumentNullException(nameof(service));
        EmbedBestEffort();
    }

    /// <summary>
    /// Parks the OBS window (minimized, ownership cleared — the process and
    /// window stay alive and reachable via the taskbar) and drops the badge.
    /// The next Attach restores it.
    /// </summary>
    public void Detach()
    {
        ObsWindowHostService? current = service;
        service = null;
        UnsubscribeStatus(current);
        UnhookSync();
        SetIsActive(false);
        ParkIfEmbedded(current);
        ResetSyncCache();
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        // HwndHost rebuilds the child when re-entering the tree — the disposed
        // flag must not survive that.
        disposed = false;
        childHwnd = CreateWindowExW(
            exStyle: 0,
            className: "STATIC",
            windowName: null,
            style: WsChild | WsVisible,
            x: 0, y: 0, width: 0, height: 0,
            hwndParent: hwndParent.Handle,
            hMenu: IntPtr.Zero, hInstance: IntPtr.Zero, lpParam: IntPtr.Zero);
        return new HandleRef(this, childHwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        disposed = true;
        // Park BEFORE destroying the child — the OBS window must not dangle
        // while this host tears down; parking minimizes it (owner cleared) so
        // it survives teardown reachable from the taskbar.
        UnhookSync();
        ParkIfEmbedded(service);
        ResetSyncCache();
        if (childHwnd != IntPtr.Zero)
        {
            _ = DestroyWindow(childHwnd);
            childHwnd = IntPtr.Zero;
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        // Size changes while active are covered by LayoutUpdated (a size change
        // is a layout pass); this is only the embed retry path — every size
        // change is a retry chance (the embed may have been requested before
        // the host was sized or OBS ready).
        if (service is null || IsActive)
        {
            return;
        }

        EmbedBestEffort();
    }

    /// <summary>Converts WPF DIPs to this control's physical pixel size.</summary>
    private (int Width, int Height) ToPhysical(double widthDips, double heightDips)
    {
        var source = PresentationSource.FromVisual(this);
        var matrix = source?.CompositionTarget?.TransformToDevice ?? System.Windows.Media.Matrix.Identity;
        return ((int)Math.Ceiling(widthDips * matrix.M11), (int)Math.Ceiling(heightDips * matrix.M22));
    }

    // ---------------------------------------------------------------------
    // Embed
    // ---------------------------------------------------------------------

    private void EmbedBestEffort()
    {
        // PointToScreen and ToPhysical are UI-thread-only, but StatusChanged
        // fires on threadpool threads — marshal the whole attempt.
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(EmbedBestEffort);
            return;
        }

        try
        {
            TryEmbed();
        }
        catch (Exception)
        {
            // Retried on the next StatusChanged or size change.
            SetIsActive(false);
        }
    }

    private void TryEmbed()
    {
        ObsWindowHostService? current = service;
        if (current is null || disposed || childHwnd == IntPtr.Zero || ActualWidth <= 1)
        {
            return;
        }

        if (current.IsDetachedByUser)
        {
            // The user detached OBS to its own window — automatic embeds stay
            // off until they press "Put back into Vyral".
            SetIsActive(false);
            return;
        }

        if (Interlocked.CompareExchange(ref embedding, 1, 0) != 0)
        {
            return;
        }

        try
        {
            IntPtr owner = ResolveOwnerHwnd();
            if (owner == IntPtr.Zero)
            {
                return;
            }

            var (x, y, width, height) = GetEmbedRect();
            if (width <= 0 || height <= 0)
            {
                return;
            }

            ObsHostStatus status = current.GetStatus();
            switch (status.State)
            {
                case ObsHostState.ReadyToEmbed:
                case ObsHostState.Embedded:
                    current.EmbedInto(owner, x, y, width, height);
                    OnEmbedSucceeded(current, x, y, width, height);
                    break;

                case ObsHostState.Parked:
                    // RestoreInto = undo the minimize, then re-own, re-strip
                    // the chrome and position over the embed rect.
                    current.RestoreInto(owner, x, y, width, height);
                    OnEmbedSucceeded(current, x, y, width, height);
                    // The rect measured above predates WPF's re-layout of the
                    // re-shown tab (ActualHeight can still be the stale
                    // pre-collapse value) — the throttle cache must not
                    // swallow the first sync after the restore.
                    ResetSyncCache();
                    break;

                default:
                    // NotInstalled/Launching/WaitingForWindow/ObsExited/Failed —
                    // finish the embed when the service reaches ReadyToEmbed.
                    SetIsActive(false);
                    if (!statusSubscribed)
                    {
                        current.StatusChanged += OnStatusChanged;
                        statusSubscribed = true;
                    }

                    break;
            }
        }
        finally
        {
            Interlocked.Exchange(ref embedding, 0);
        }
    }

    /// <summary>The owner for the OBS window: Vyral's TOP-LEVEL window handle.</summary>
    private IntPtr ResolveOwnerHwnd()
    {
        Window? window = Window.GetWindow(this);
        if (window is not null)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero)
            {
                return handle;
            }
        }

        // Before the Window's source exists — the root HWND of the child still
        // IS Vyral's top-level window.
        return childHwnd != IntPtr.Zero ? GetAncestor(childHwnd, GaRoot) : IntPtr.Zero;
    }

    /// <summary>The control's area in SCREEN physical pixels — where OBS must sit.</summary>
    private (int X, int Y, int Width, int Height) GetEmbedRect()
    {
        var tl = PointToScreen(new Point(0, 0)); // physical px already (integral values, double-typed)
        var px = ToPhysical(ActualWidth, ActualHeight);

        // Guard against stale/unclamped measurements (a restore that ran
        // before WPF re-laid-out the re-shown tab): the embed area can never
        // be larger than the owner window's client area.
        if (TryGetOwnerClientSize(out int clientWidth, out int clientHeight))
        {
            px.Width = Math.Min(px.Width, clientWidth);
            px.Height = Math.Min(px.Height, clientHeight);
        }

        return ((int)tl.X, (int)tl.Y, px.Width, px.Height);
    }

    /// <summary>
    /// The owner window's CLIENT area in physical pixels — the hard ceiling
    /// for the embed rect. Falls back to the work area (same DIPs→physical
    /// conversion as the rect) before the window handle exists; false only
    /// when neither is known.
    /// </summary>
    private bool TryGetOwnerClientSize(out int width, out int height)
    {
        IntPtr owner = ResolveOwnerHwnd();
        if (owner != IntPtr.Zero && GetClientRect(owner, out RECT client))
        {
            width = client.Right - client.Left;
            height = client.Bottom - client.Top;
            return width > 0 && height > 0;
        }

        // Pre-window fallback — same DIPs→physical conversion as the rect.
        var work = ToPhysical(SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height);
        width = work.Width;
        height = work.Height;
        return width > 0 && height > 0;
    }

    private void OnEmbedSucceeded(ObsWindowHostService current, int x, int y, int width, int height)
    {
        // EmbedInto already placed the window at this rect — seed the throttle
        // cache so the first layout pass does not redundantly MoveWindow.
        lastSyncX = x;
        lastSyncY = y;
        lastSyncWidth = width;
        lastSyncHeight = height;
        SchedulePostEmbedResyncs();
        HookSync();
        SetIsActive(true);
        UnsubscribeStatus(current);
    }

    /// <summary>
    /// Re-syncs once WPF has re-laid-out the (re-shown) tab and again when the
    /// dispatcher idles. The rect measured DURING an embed/restore can predate
    /// that layout — ActualHeight then still reflects the stale/pre-collapse
    /// arrangement and OBS is placed right but sized wrong. Each deferred sync
    /// re-reads the settled rect and pushes only what changed, so a wrong
    /// embed size self-corrects within a frame of the state change.
    /// </summary>
    private void SchedulePostEmbedResyncs()
    {
        _ = Dispatcher.BeginInvoke(SyncObsWindow, DispatcherPriority.Loaded);
        _ = Dispatcher.BeginInvoke(SyncObsWindow, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>Forgets the last pushed rect — the next sync always pushes.</summary>
    private void ResetSyncCache()
    {
        lastSyncX = -1;
        lastSyncY = -1;
        lastSyncWidth = -1;
        lastSyncHeight = -1;
    }

    // ---------------------------------------------------------------------
    // Position sync
    // ---------------------------------------------------------------------

    private void HookSync()
    {
        if (!layoutSubscribed)
        {
            LayoutUpdated += OnLayoutUpdated;
            layoutSubscribed = true;
        }

        Window? window = Window.GetWindow(this);
        if (window is not null && !ReferenceEquals(window, hookedWindow))
        {
            if (hookedWindow is not null)
            {
                hookedWindow.LocationChanged -= OnWindowLocationChanged;
            }

            hookedWindow = window;
            window.LocationChanged += OnWindowLocationChanged;
        }
    }

    private void UnhookSync()
    {
        if (layoutSubscribed)
        {
            LayoutUpdated -= OnLayoutUpdated;
            layoutSubscribed = false;
        }

        if (hookedWindow is not null)
        {
            hookedWindow.LocationChanged -= OnWindowLocationChanged;
            hookedWindow = null;
        }
    }

    private void OnLayoutUpdated(object? sender, EventArgs e) => SyncObsWindow();

    private void OnWindowLocationChanged(object? sender, EventArgs e) => SyncObsWindow();

    /// <summary>
    /// Keeps the owned OBS window exactly over this control's area. Runs on
    /// every layout pass and on window moves — throttled to no-op when
    /// position AND size are unchanged.
    /// </summary>
    private void SyncObsWindow()
    {
        ObsWindowHostService? current = service;
        if (current is null || !IsActive || disposed || ActualWidth <= 1)
        {
            return;
        }

        try
        {
            var (x, y, width, height) = GetEmbedRect();
            if (x == lastSyncX && y == lastSyncY && width == lastSyncWidth && height == lastSyncHeight)
            {
                return;
            }

            current.ResizeTo(width, height);
            current.MoveTo(x, y);
            lastSyncX = x;
            lastSyncY = y;
            lastSyncWidth = width;
            lastSyncHeight = height;
        }
        catch (Exception)
        {
            // A wedged host state must never fault the UI thread; the next
            // layout pass or window move retries.
        }
    }

    // ---------------------------------------------------------------------
    // Status + teardown
    // ---------------------------------------------------------------------

    private void OnStatusChanged(ObsHostStatus status) => EmbedBestEffort();

    private static void ParkIfEmbedded(ObsWindowHostService? current)
    {
        if (current is null)
        {
            return;
        }

        try
        {
            // Only an embedded window needs parking — Park would otherwise log
            // a spurious "nothing to park" warning on every teardown.
            if (current.GetStatus().State == ObsHostState.Embedded)
            {
                current.Park();
            }
        }
        catch (Exception)
        {
            // Teardown path — a wedged host must never fault the UI thread.
        }
    }

    private void UnsubscribeStatus(ObsWindowHostService? from)
    {
        if (from is null || !statusSubscribed)
        {
            return;
        }

        from.StatusChanged -= OnStatusChanged;
        statusSubscribed = false;
    }

    private void SetIsActive(bool value)
    {
        // StatusChanged fires on threadpool threads; the DP write needs the
        // dispatcher (the service calls themselves marshal nothing).
        if (Dispatcher.CheckAccess())
        {
            SetValue(IsActiveProperty, value);
        }
        else
        {
            _ = Dispatcher.BeginInvoke(() => SetValue(IsActiveProperty, value));
        }
    }

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint exStyle,
        string className,
        string? windowName,
        uint style,
        int x, int y, int width, int height,
        IntPtr hwndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", EntryPoint = "DestroyWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
}
