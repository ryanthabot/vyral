namespace VyralBot.Integrations.Obs.Hosting;

/// <summary>
/// Pure WS_* style arithmetic for embedding the OBS top-level window — no
/// P/Invoke, unit-testable. Values mirror the Win32 definitions and are
/// declared as <c>long</c>: Roslyn refuses nint constants above int.MaxValue
/// (they must fit the smallest possible native size), and the WS_POPUP bit is
/// 0x80000000. Windows window styles are 32-bit values, so the long space is
/// bit-exact; the strip helper casts at the nint boundary.
/// </summary>
public static class ObsWindowStyle
{
    public const long WsCaption = 0x00C0_0000;
    public const long WsThickFrame = 0x0004_0000;
    public const long WsSysMenu = 0x0008_0000;
    public const long WsMinimizeBox = 0x0002_0000;
    public const long WsMaximizeBox = 0x0001_0000;
    public const long WsPopup = 0x8000_0000;
    public const long WsVisible = 0x1000_0000;

    /// <summary>The five top-level chrome bits the embedded OBS window must lose. Exposed for tests.</summary>
    public const long TopLevelChromeMask =
        WsCaption | WsThickFrame | WsSysMenu | WsMinimizeBox | WsMaximizeBox;

    /// <summary>
    /// Clears the caption/frame/system-menu/min-max chrome while leaving every
    /// other bit — including WS_POPUP and WS_VISIBLE — exactly as it was.
    /// </summary>
    public static nint StripTopLevelChrome(nint style) => style & ~(nint)TopLevelChromeMask;
}
