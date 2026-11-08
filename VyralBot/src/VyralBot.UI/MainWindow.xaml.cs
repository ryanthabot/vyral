using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using VyralBot.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using VyralBot.Integrations.Twitch;
using VyralBot.Integrations.Kick;
using Microsoft.Extensions.Logging;

namespace VyralBot.UI;

/// <summary>
/// Main application window.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MainWindow> _logger;
    private readonly Services.Hotkey.GlobalHotkeyService _hotkeyService;

    public MainWindow(MainViewModel viewModel, IServiceProvider serviceProvider, ILogger<MainWindow> logger,
        Services.Hotkey.GlobalHotkeyService hotkeyService)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _hotkeyService = hotkeyService;

        // Per-theme logo color variant as the window/taskbar icon, live with theme switches
        Services.ThemeManager.Instance.ThemeChanged += OnThemeChanged;
        UpdateWindowIcon();

        // Handle window events
        Closing += MainWindow_Closing;

    }

    private void OnThemeChanged() => Dispatcher.Invoke(UpdateWindowIcon);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ((HwndSource?)PresentationSource.FromVisual(this))?.AddHook(WndProc);

        // Global soundboard hotkeys (KI-55): bind the RegisterHotKey slots to
        // this window's HWND. The window is created once per run and never
        // recreated, so the HWND is stable; WM_HOTKEY arrives in WndProc below.
        try
        {
            _hotkeyService.AttachToWindow(this);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to attach the global hotkey service to the main window");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Global hotkey sink (KI-55): RegisterHotKey posts WM_HOTKEY here with
        // the slot id in wParam. The service owns the table + playback.
        const int WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY)
        {
            handled = _hotkeyService.HandleWmHotkey(wParam);
            return IntPtr.Zero;
        }

        const int WM_GETMINMAXINFO = 0x0024;
        if (msg != WM_GETMINMAXINFO)
        {
            return IntPtr.Zero;
        }

        // WindowChrome maximizes borderless windows to the work area inflated by the
        // resize frame, which pushes the bottom edge behind the taskbar and clips the
        // other edges; clamp the maximized rect to the monitor's real work area.
        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor != IntPtr.Zero)
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                mmi.ptMaxPosition.X = info.rcWork.Left;
                mmi.ptMaxPosition.Y = info.rcWork.Top;
                mmi.ptMaxSize.X = info.rcWork.Right - info.rcWork.Left;
                mmi.ptMaxSize.Y = info.rcWork.Bottom - info.rcWork.Top;
            }
        }

        Marshal.StructureToPtr(mmi, lParam, true);
        handled = true;
        return IntPtr.Zero;
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private void UpdateWindowIcon()
        => Icon = Services.ThemeManager.Instance.ResolveLogoImage(Services.ThemeManager.Instance.CurrentLogoVariant);

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // Double-click to maximize/restore
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        else
        {
            // Single click to drag
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Let WPF handle shutdown gracefully
        // App.OnExit will handle cleanup
    }

    private void NewProfilePinBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.PasswordBox passwordBox)
        {
            _viewModel.NewProfilePin = passwordBox.Password;
        }
    }

    private void ProfilePinBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.PasswordBox passwordBox)
        {
            _viewModel.ProfilePin = passwordBox.Password;
        }
    }
}