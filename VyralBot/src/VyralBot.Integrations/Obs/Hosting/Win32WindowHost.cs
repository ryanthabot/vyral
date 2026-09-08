using System.Runtime.InteropServices;
using System.Text;

namespace VyralBot.Integrations.Obs.Hosting;

/// <summary>
/// The user32 surface the OBS window host needs: reparenting (SetParent),
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

    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoMove = 0x0002;
    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpNoActivate = 0x0010;

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
    internal static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr GetParent(IntPtr hWnd);

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
}
