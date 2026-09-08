namespace VyralBot.Integrations.Obs.Native;

/// <summary>
/// Public preview surface over the internal ObsNative display entry points —
/// VyralBot.UI's ObsPreviewHost cannot see the internal P/Invoke layer, and the
/// display calls are the only native surface a WPF host control needs. Every
/// method must run while the engine gate is held (use
/// <see cref="Engine.IObsNativeGate.RunNativeAsync{T}"/>, which libobs requires
/// anyway since display creation touches the graphics subsystem).
///
/// Values mirror the OBS frontend's OBSQTDisplay::CreateDisplay at tag 32.1.1:
/// num_backbuffers left 0, format GS_BGRA(5), zsformat GS_ZS_NONE(0), and a
/// background color packed red&lt;&lt;0 | green&lt;&lt;8 | blue&lt;&lt;16 | alpha&lt;&lt;24
/// (i.e. conventional 0xAARRGGBB).
/// </summary>
public static class ObsDisplayInterop
{
    public static IntPtr DisplayCreate(IntPtr hwnd, uint cx, uint cy, uint backgroundColor)
    {
        var data = new ObsInitData
        {
            Window = hwnd,
            Cx = cx,
            Cy = cy,
            NumBackbuffers = 0, // frontend parity: gs_init_data zero-init leaves this 0
            Format = ObsNativeConstants.ColorFormatBgra,
            ZsFormat = ObsNativeConstants.ZStencilNone,
            Adapter = 0,
        };
        return ObsNative.ObsDisplayCreate(ref data, backgroundColor);
    }

    public static void DisplayDestroy(IntPtr display) => ObsNative.ObsDisplayDestroy(display);

    public static void DisplayResize(IntPtr display, uint cx, uint cy) =>
        ObsNative.ObsDisplayResize(display, cx, cy);

    public static void DisplayAddDrawCallback(IntPtr display, ObsDrawCallback draw) =>
        ObsNative.ObsDisplayAddDrawCallback(display, draw, IntPtr.Zero);

    public static void DisplayRemoveDrawCallback(IntPtr display, ObsDrawCallback draw) =>
        ObsNative.ObsDisplayRemoveDrawCallback(display, draw, IntPtr.Zero);

    /// <summary>Renders the composed main texture; call from inside the draw callback only.</summary>
    public static void RenderMainTexture() => ObsNative.ObsRenderMainTexture();
}
