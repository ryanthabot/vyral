using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace VyralBot.UI.Views;

public partial class AlertOverlayWindow : Window
{
    private DispatcherTimer? _autoCloseTimer;
    private Storyboard? _currentAnimation;

    // WS_EX_* style bits that make the overlay click-through and non-activating
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int MA_NOACTIVATE = 3;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    public AlertOverlayWindow()
    {
        InitializeComponent();
        Opacity = 0; // Start invisible; ShowAlertAsync sets it to 1 when the alert is displayed
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Make the overlay click-through so it never steals mouse input from the
        // applications/stream beneath it, and never steals focus.
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);

        var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOUSEACTIVATE)
        {
            // Never activate on mouse click
            handled = true;
            return new IntPtr(MA_NOACTIVATE);
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Shows the alert with the specified animation type
    /// </summary>
    public async Task ShowAlertAsync(
        string title,
        string username,
        string? message = null,
        string? imageUrl = null,
        AlertAnimation animation = AlertAnimation.SlideInFromTop,
        AlertPosition position = AlertPosition.TopCenter,
        int durationSeconds = 5)
    {
        // Update content (null-safe: the window is created fresh for every alert)
        AlertTitle.Text = title ?? string.Empty;
        AlertUsername.Text = username ?? string.Empty;
        
        if (!string.IsNullOrEmpty(message))
        {
            AlertMessage.Text = message;
            AlertMessage.Visibility = Visibility.Visible;
        }
        else
        {
            AlertMessage.Visibility = Visibility.Collapsed;
        }

        // Load image if provided
        if (!string.IsNullOrEmpty(imageUrl))
        {
            try
            {
                AlertImage.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(imageUrl));
                AlertImage.Visibility = Visibility.Visible;
            }
            catch
            {
                AlertImage.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            AlertImage.Visibility = Visibility.Collapsed;
        }

        // Position the window
        PositionWindow(position);

        // Show window. The window starts at Opacity 0; raise it to 1 just before the
        // entrance animation begins (the storyboards animate the inner Border, whose
        // opacity multiplies with the window's - leaving the window at 0 would render
        // every alert invisible).
        Opacity = 0;
        Show();
        Opacity = 1;

        // Play animation
        PlayAnimation(animation);

        // Set up auto-close timer (guard against non-positive durations so the
        // alert can never arm a zero/negative interval when fed from settings)
        var duration = Math.Max(1, durationSeconds);
        _autoCloseTimer?.Stop();
        _autoCloseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(duration)
        };
        _autoCloseTimer.Tick += (s, e) =>
        {
            _autoCloseTimer.Stop();
            _ = HideAlertAsync();
        };
        _autoCloseTimer.Start();

        await Task.CompletedTask;
    }

    /// <summary>
    /// Hides the alert with fade out animation
    /// </summary>
    public async Task HideAlertAsync()
    {
        var fadeOut = (Storyboard)FindResource("FadeOut");
        fadeOut.Begin();
        
        // Wait for animation to complete
        await Task.Delay(500);
    }

    private void FadeOut_Completed(object? sender, EventArgs e)
    {
        Hide();
        Opacity = 0;
    }

    private void PlayAnimation(AlertAnimation animation)
    {
        _currentAnimation?.Stop();
        
        var storyboardName = animation switch
        {
            AlertAnimation.SlideInFromTop => "SlideInFromTop",
            AlertAnimation.SlideInFromBottom => "SlideInFromBottom",
            AlertAnimation.FadeIn => "FadeIn",
            AlertAnimation.Bounce => "Bounce",
            _ => "FadeIn"
        };

        _currentAnimation = (Storyboard)FindResource(storyboardName);
        _currentAnimation.Begin();
    }

    private void PositionWindow(AlertPosition position)
    {
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;

        switch (position)
        {
            case AlertPosition.TopLeft:
                Left = 20;
                Top = 20;
                break;
            
            case AlertPosition.TopCenter:
                Left = (screenWidth - Width) / 2;
                Top = 20;
                break;
            
            case AlertPosition.TopRight:
                Left = screenWidth - Width - 20;
                Top = 20;
                break;
            
            case AlertPosition.MiddleLeft:
                Left = 20;
                Top = (screenHeight - Height) / 2;
                break;
            
            case AlertPosition.MiddleCenter:
                Left = (screenWidth - Width) / 2;
                Top = (screenHeight - Height) / 2;
                break;
            
            case AlertPosition.MiddleRight:
                Left = screenWidth - Width - 20;
                Top = (screenHeight - Height) / 2;
                break;
            
            case AlertPosition.BottomLeft:
                Left = 20;
                Top = screenHeight - Height - 60; // 60 for taskbar
                break;
            
            case AlertPosition.BottomCenter:
                Left = (screenWidth - Width) / 2;
                Top = screenHeight - Height - 60;
                break;
            
            case AlertPosition.BottomRight:
                Left = screenWidth - Width - 20;
                Top = screenHeight - Height - 60;
                break;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _autoCloseTimer?.Stop();
        _currentAnimation?.Stop();
        base.OnClosed(e);
    }
}

public enum AlertAnimation
{
    SlideInFromTop,
    SlideInFromBottom,
    FadeIn,
    Bounce
}

public enum AlertPosition
{
    TopLeft,
    TopCenter,
    TopRight,
    MiddleLeft,
    MiddleCenter,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight
}
