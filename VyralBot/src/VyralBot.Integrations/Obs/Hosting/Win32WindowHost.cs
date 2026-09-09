using System.Runtime.InteropServices;
using System.Text;

namespace VyralBot.Integrations.Obs.Hosting;

/// <summary>
/// The user32 surface the OBS window host needs: ownership (GWL_HWNDPARENT),
/// style surgery (GWL_STYLE), window discovery (EnumWindows by PID with a
/// "Qt"-prefixed class name), and liveness/show-state checks for the
/// park/restore lifecycle. Exact Win32 names, SetLastError = true. The
/// GetWindowLongPtrW/SetWindowLongPtrW exports exist only in 64-bit user32 —
/// the solution is x64-pinned.
/// </summary>
internal static class Win32WindowHost
{
    internal const int SwShow = 5;
    internal const int SwMinimize = 6;
    internal const int SwRestore = 9;
    internal const int SwHide = 0;

    // Aliases of ObsWindowStyle's public constants — single source of truth
    // for the frozen Win32 style values.
    internal const long WsCaption = ObsWindowStyle.WsCaption;
    internal const long WsThickFrame = ObsWindowStyle.WsThickFrame;
    internal const long WsSysMenu = ObsWindowStyle.WsSysMenu;
    internal const long WsMinimizeBox = ObsWindowStyle.WsMinimizeBox;
    internal const long WsMaximizeBox = ObsWindowStyle.WsMaximizeBox;
    internal const long WsPopup = ObsWindowStyle.WsPopup;
    internal const long WsVisible = ObsWindowStyle.WsVisible;
    internal const long WsChild = 0x4000_0000;

    internal const int GwlStyle = -16;

    // GWL_HWNDPARENT sets the window's OWNER (not its parent): the target
    // stays a top-level window with an untouched paint/DWM pipeline, it only
    // floats above its owner and minimizes/hides with it.
    internal const int GwlHwndParent = -8;

    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoMove = 0x0002;
    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpFrameChanged = 0x0020;
    internal const uint SwpShowWindow = 0x0040;

    // RedrawWindow flags — full invalidate + erase + children + immediate paint.
    internal const uint RdwInvalid = 0x0000;
    internal const uint RdwErase = 0x0004;
    internal const uint RdwFrame = 0x0400;
    internal const uint RdwAllChildren = 0x0080;
    internal const uint RdwNoInternalUpdate = 0x0010;
    internal const uint RdwUpdateNow = 0x0100;
    internal const uint RdwEraseNow = 0x0200;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateWindow(IntPtr hWnd);

    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, bool repaint);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(IntPtr hWnd, int cmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int index, IntPtr newLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetClassNameW(IntPtr hWnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr GetDesktopWindow();

    private const int ProcessQueryLimitedInformation = 0x1000;
    private const int TokenQuery = 0x0008;
    private const int TokenElevation = 20;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int desiredAccess, bool inheritHandle, int processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr processHandle, int desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass, out uint tokenInfo, int tokenInfoLength, out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>
    /// True when the process with the given id runs with an elevated
    /// (administrator) token. Returns false when the process is gone or the
    /// token cannot be queried — the caller treats "obs elevated + Vyral not"
    /// as the only actionable signal, so both sides must answer.
    /// </summary>
    internal static bool IsProcessElevated(int processId)
    {
        var process = OpenProcess(ProcessQueryLimitedInformation, inheritHandle: false, processId);
        if (process == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            if (!OpenProcessToken(process, TokenQuery, out var token))
            {
                return false;
            }

            try
            {
                return GetTokenInformation(token, TokenElevation, out uint elevated, sizeof(uint), out _)
                    && elevated != 0;
            }
            finally
            {
                _ = CloseHandle(token);
            }
        }
        finally
        {
            _ = CloseHandle(process);
        }
    }
}
