using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using VyralBot.Integrations.Obs.Hosting;

namespace VyralBot.UI.Controls;

/// <summary>
/// Hosts the real obs64.exe window inside WPF: <see cref="HwndHost"/> builds a
/// STATIC child HWND and the <see cref="ObsWindowHostService"/> reparents the
/// OBS main window into it. Detach and HWND teardown PARK the OBS window
/// against the service-owned parking HWND (hidden, process kept alive) — that
/// is what makes tab switches and navigation safe; a later Attach on a fresh
/// child HWND restores it. <see cref="StatusChanged"/> arrives on threadpool
/// threads: service calls are lock-protected user32 only and run inline, but
/// the <see cref="IsActive"/> dependency property is written through a
/// dispatcher guard.
/// </summary>
public sealed class ObsWindowEmbedHost : HwndHost
{
    private const uint WsChild = 0x4000_0000;
    private const uint WsVisible = 0x1000_0000;

    private ObsWindowHostService? service;
    private IntPtr childHwnd;
    private bool disposed;
    private bool statusSubscribed;
    private int embedding; // 1 while an embed attempt is in flight (attach, Loaded and StatusChanged can race)

    /// <summary>True while the OBS window lives inside this host's child HWND; bindable for a "hosted" badge.</summary>
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
    /// Parks the OBS window (hidden at the service's parking HWND — the process
    /// and window stay alive) and drops the badge. The next Attach restores it.
    /// </summary>
    public void Detach()
    {
        ObsWindowHostService? current = service;
        service = null;
        UnsubscribeStatus(current);
        SetIsActive(false);
        ParkIfEmbedded(current);
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
        // Park BEFORE destroying the child — OBS is parented to it, and letting
        // the parent die would orphan the OBS window onto the desktop.
        ParkIfEmbedded(service);
        if (childHwnd != IntPtr.Zero)
        {
            _ = DestroyWindow(childHwnd);
            childHwnd = IntPtr.Zero;
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        try
        {
            ObsWindowHostService? current = service;
            if (current is null)
            {
                return;
            }

            if (IsActive)
            {
                // Keep the embedded OBS window in step with WPF layout.
                current.ResizeTo((int)ActualWidth, (int)ActualHeight);
            }
            else
            {
                // Every size change is also a retry chance (the embed may have
                // been requested before the host was sized or OBS ready).
                EmbedBestEffort();
            }
        }
        catch (Exception)
        {
            // A wedged host state must never fault the UI thread.
        }
    }

    // ---------------------------------------------------------------------
    // Internals
    // ---------------------------------------------------------------------

    private void EmbedBestEffort()
    {
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

        if (Interlocked.CompareExchange(ref embedding, 1, 0) != 0)
        {
            return;
        }

        try
        {
            ObsHostStatus status = current.GetStatus();
            switch (status.State)
            {
                case ObsHostState.ReadyToEmbed:
                case ObsHostState.Embedded:
                    current.EmbedInto(childHwnd);
                    current.ResizeTo((int)ActualWidth, (int)ActualHeight);
                    SetIsActive(true);
                    UnsubscribeStatus(current);
                    break;

                case ObsHostState.Parked:
                    // RestoreInto = EmbedInto + show (embed reparents while
                    // hidden so the chrome-stripped window never flashes).
                    current.RestoreInto(childHwnd);
                    current.ResizeTo((int)ActualWidth, (int)ActualHeight);
                    SetIsActive(true);
                    UnsubscribeStatus(current);
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
}
