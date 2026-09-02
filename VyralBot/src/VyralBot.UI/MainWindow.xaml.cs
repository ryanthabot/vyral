using System.Windows;
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

    public MainWindow(MainViewModel viewModel, IServiceProvider serviceProvider, ILogger<MainWindow> logger)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;
        _serviceProvider = serviceProvider;
        _logger = logger;

        // Per-theme logo color variant as the window/taskbar icon, live with theme switches
        Services.ThemeManager.Instance.ThemeChanged += OnThemeChanged;
        UpdateWindowIcon();

        // Handle window events
        Closing += MainWindow_Closing;

    }

    private void OnThemeChanged() => Dispatcher.Invoke(UpdateWindowIcon);

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