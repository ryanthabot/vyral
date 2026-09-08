using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using VyralBot.Integrations.Obs.Engine;
using VyralBot.Integrations.Obs.Native;

namespace VyralBot.UI.Controls;

/// <summary>
/// Renders the libobs program output inside WPF via a native child HWND:
/// <see cref="HwndHost"/> creates a STATIC child window, and when an engine is
/// attached (and Ready) an obs display is created on that HWND with a draw
/// callback that blits the composed main texture. All display calls run through
/// the engine gate (<see cref="IObsNativeGate.RunNativeAsync{T}"/>) — never on
/// the dispatcher thread — and degrade to no-ops when the engine is not Ready
/// (e.g. during shutdown).
/// </summary>
public sealed class ObsPreviewHost : HwndHost
{
    private const uint WsChild = 0x4000_0000;
    private const uint WsVisible = 0x1000_0000;

    /// <summary>Opaque dark gray, packed as 0xAARRGGBB (libobs vec4_from_rgba order — alpha is the high byte).</summary>
    private const uint BackgroundColor = 0xFF20_2020;

    private IObsNativeGate? engine;
    private IntPtr childHwnd;
    private IntPtr display;
    private int creating;
    private bool _disposed;

    /// <summary>
    /// True while an obs display is alive on the child HWND. The studio view
    /// binds this so a dead render path is diagnosable at a glance (display
    /// creation can legitimately lag the engine reaching Ready — the host
    /// retries on Loaded and on every size change until it sticks).
    /// </summary>
    public static readonly DependencyProperty IsPreviewActiveProperty =
        DependencyProperty.Register(
            nameof(IsPreviewActive), typeof(bool), typeof(ObsPreviewHost),
            new PropertyMetadata(false));

    public bool IsPreviewActive
    {
        get => (bool)GetValue(IsPreviewActiveProperty);
        private set => SetValue(IsPreviewActiveProperty, value);
    }

    // Field-pinned: libobs keeps the raw pointer, so the delegate (and the
    // native thunk it marshals to) must outlive every registered callback.
    private readonly ObsDrawCallback drawCallback;

    public ObsPreviewHost()
    {
        drawCallback = OnDrawFrame;
        Loaded += static (sender, _) => ((ObsPreviewHost)sender).CreateDisplayBestEffort();
    }

    /// <summary>
    /// Binds the host to an engine and, when the engine is already Ready,
    /// creates the display. Re-attach after a shutdown is a no-op until the
    /// (new) engine reaches Ready again; Detach first to rebind cleanly.
    /// </summary>
    public void Attach(IObsNativeGate engine)
    {
        this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
        CreateDisplayBestEffort();
    }

    /// <summary>Destroys the display (idempotent). The child HWND itself lives until WPF tears the HwndHost down.</summary>
    public void Detach()
    {
        // Capture the gate BEFORE nulling the field — DestroyDisplay reads it,
        // and a detached host must still route the destroy through the (still
        // live) engine gate or the display leaks.
        IObsNativeGate? gate = engine;
        engine = null;
        DestroyDisplay(display, gate);
    }

    private static void OnDrawFrame(IntPtr param, uint cx, uint cy) =>
        ObsDisplayInterop.RenderMainTexture();

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
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
        _disposed = true;
        DestroyDisplay();
        if (childHwnd != IntPtr.Zero)
        {
            _ = DestroyWindow(childHwnd);
            childHwnd = IntPtr.Zero;
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        // Resize keeps an existing display in step with the layout; before one
        // exists, every size change is also a retry chance (the display may
        // have been requested before the host was sized or the engine ready).
        if (display != IntPtr.Zero)
        {
            ResizeDisplay();
        }
        else
        {
            CreateDisplayBestEffort();
        }
    }

    private void CreateDisplayBestEffort()
    {
        // Fire-and-forget on purpose (the gate work belongs off the dispatcher);
        // a failed preview must never fault the app, so everything is caught.
        _ = CreateDisplayAsync();
    }

    private async Task CreateDisplayAsync()
    {
        IObsNativeGate? gate = engine;
        IntPtr hwnd = childHwnd;
        if (gate is null || !gate.IsReady || hwnd == IntPtr.Zero || display != IntPtr.Zero)
        {
            return;
        }

        // Attach and Loaded can race — only one create at a time.
        if (System.Threading.Interlocked.CompareExchange(ref creating, 1, 0) != 0)
        {
            return;
        }

        try
        {
            (uint cx, uint cy) = CurrentPixelSize();
            IntPtr created = await gate.RunNativeAsync(() =>
            {
                IntPtr d = ObsDisplayInterop.DisplayCreate(hwnd, cx, cy, BackgroundColor);
                if (d == IntPtr.Zero)
                {
                    return IntPtr.Zero;
                }

                ObsDisplayInterop.DisplayAddDrawCallback(d, drawCallback);
                return d;
            }).ConfigureAwait(true); // resume on the dispatcher — display field is UI-thread state

            // The gate await took time: the host/HWND may have been torn down
            // in the meantime (navigation away, window close). Destroy the
            // just-created display immediately instead of assigning it to a
            // dead window.
            if (created != IntPtr.Zero && (_disposed || childHwnd == IntPtr.Zero))
            {
                DestroyDisplay(created, gate);
                return;
            }

            display = created;
            IsPreviewActive = created != IntPtr.Zero;
        }
        catch (Exception)
        {
            // Not-ready races and native init failures both land here; the
            // preview stays blank rather than crashing the host app.
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref creating, 0);
        }
    }

    private void ResizeDisplay()
    {
        IntPtr current = display;
        IObsNativeGate? gate = engine;
        if (current == IntPtr.Zero || gate is null || !gate.IsReady)
        {
            return;
        }

        (uint cx, uint cy) = CurrentPixelSize();
        _ = gate.RunNativeAsync(() =>
        {
            ObsDisplayInterop.DisplayResize(current, cx, cy);
            return true;
        });
    }

    private void DestroyDisplay() => DestroyDisplay(display, engine);

    private void DestroyDisplay(IntPtr stale, IObsNativeGate? gate)
    {
        if (stale == display)
        {
            display = IntPtr.Zero;
            IsPreviewActive = false;
        }

        if (stale == IntPtr.Zero || gate is null || !gate.IsReady)
        {
            // Not ready (or already shutting down): obs_shutdown frees every
            // remaining display, so a gated destroy would be pointless anyway.
            return;
        }

        _ = gate.RunNativeAsync(() =>
        {
            ObsDisplayInterop.DisplayRemoveDrawCallback(stale, drawCallback);
            ObsDisplayInterop.DisplayDestroy(stale);
            return true;
        });
    }

    private (uint Cx, uint Cy) CurrentPixelSize() =>
        ((uint)Math.Max(1, (int)Math.Ceiling(ActualWidth)),
         (uint)Math.Max(1, (int)Math.Ceiling(ActualHeight)));

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
