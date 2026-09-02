using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

public partial class TwitchWebSetupView : UserControl
{
    public TwitchWebSetupView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Initialize WebView2 on demand when user clicks a button
        if (DataContext is TwitchWebSetupViewModel viewModel)
        {
            viewModel.WebView2InitializeRequested += OnWebView2InitializeRequested;
            viewModel.WebView2DisposeRequested += OnWebView2DisposeRequested;
        }
    }

    private async void OnWebView2InitializeRequested(object? sender, EventArgs e)
    {
        if (WebView2Container == null)
            return;

        // Create WebView2 programmatically if not already created
        if (WebView2Container.Child == null)
        {
            var webView2 = new Microsoft.Web.WebView2.Wpf.WebView2();
            WebView2Container.Child = webView2;

            try
            {
                // Use temp folder for WebView2 cache (cleared on process exit)
                var tempFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VyralBot_WebView2_Temp");
                var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(
                    userDataFolder: tempFolder);
                await webView2.EnsureCoreWebView2Async(env);

                if (DataContext is TwitchWebSetupViewModel viewModel)
                {
                    viewModel.WebView2 = webView2;
                }
            }
            catch
            {
                // Failed to initialize WebView2
            }
        }
    }

    private void OnWebView2DisposeRequested(object? sender, EventArgs e)
    {
        try
        {
            if (WebView2Container?.Child is Microsoft.Web.WebView2.Wpf.WebView2 webView2)
            {
                if (webView2.CoreWebView2 != null)
                {
                    webView2.Dispose();
                }
                WebView2Container.Child = null;

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
        if (DataContext is TwitchWebSetupViewModel viewModel)
        {
            viewModel.WebView2InitializeRequested -= OnWebView2InitializeRequested;
            viewModel.WebView2DisposeRequested -= OnWebView2DisposeRequested;
        }
    }

    private void FinishSetup_Click(object sender, RoutedEventArgs e)
    {
        // Reload PlatformsViewModel to detect credentials
        if (DataContext is TwitchWebSetupViewModel viewModel)
        {
            viewModel.FinishSetupCommand.Execute(null);
        }
    }

    private void ClientSecretBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is TwitchWebSetupViewModel viewModel && sender is PasswordBox passwordBox)
        {
            viewModel.ClientSecretInput = passwordBox.Password;
        }
    }
}
