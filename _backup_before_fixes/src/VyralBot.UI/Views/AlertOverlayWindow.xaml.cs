using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace VyralBot.UI.Views;

public partial class AlertOverlayWindow : Window
{
    private DispatcherTimer? _autoCloseTimer;
    private Storyboard? _currentAnimation;

    public AlertOverlayWindow()
    {
        InitializeComponent();
        Opacity = 0; // Start invisible
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
        // Update content
        AlertTitle.Text = title;
        AlertUsername.Text = username;
        
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

        // Show window
        Show();

        // Play animation
        PlayAnimation(animation);

        // Set up auto-close timer
        _autoCloseTimer?.Stop();
        _autoCloseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(durationSeconds)
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
