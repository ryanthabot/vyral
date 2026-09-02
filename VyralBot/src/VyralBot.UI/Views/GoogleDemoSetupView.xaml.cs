using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

public partial class GoogleDemoSetupView : UserControl
{
    public GoogleDemoSetupView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Initialize WebView2 on demand when user clicks a button
        if (DataContext is GoogleDemoSetupViewModel viewModel)
        {
            viewModel.WebView2InitializeRequested += OnWebView2InitializeRequested;
            viewModel.WebView2DisposeRequested += OnWebView2DisposeRequested;
        }
    }

    private async void OnWebView2InitializeRequested(object? sender, EventArgs e)
    {
        if (WebView2Browser == null)
            return;

        // Always ensure WebView2 is initialized (re-initialize if needed)
        try
        {
            if (WebView2Browser.CoreWebView2 == null)
            {
                await WebView2Browser.EnsureCoreWebView2Async();
            }

            if (DataContext is GoogleDemoSetupViewModel viewModel)
            {
                viewModel.WebView2 = WebView2Browser;
            }
        }
        catch
        {
            // Failed to initialize WebView2
        }
    }

    private void OnWebView2DisposeRequested(object? sender, EventArgs e)
    {
        try
        {
            if (WebView2Browser?.CoreWebView2 != null)
            {
                // Unsubscribe from all events
                WebView2Browser.CoreWebView2.NavigationStarting -= null;

                // Dispose the control
                WebView2Browser.Dispose();

                // Force garbage collection to free memory immediately
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }
        catch
        {
            // Error disposing WebView2
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is GoogleDemoSetupViewModel viewModel)
        {
            viewModel.WebView2InitializeRequested -= OnWebView2InitializeRequested;
            viewModel.WebView2DisposeRequested -= OnWebView2DisposeRequested;
        }
    }

    private void FinishSetup_Click(object sender, RoutedEventArgs e)
    {
        // Reload PlatformsViewModel to detect credentials
        if (DataContext is GoogleDemoSetupViewModel viewModel)
        {
            viewModel.FinishSetupCommand.Execute(null);
        }
    }

    private void ClientSecretBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is GoogleDemoSetupViewModel viewModel && sender is PasswordBox passwordBox)
        {
            viewModel.ClientSecretInput = passwordBox.Password;
        }
    }
}
