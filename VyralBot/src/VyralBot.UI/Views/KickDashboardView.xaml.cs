using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

public partial class KickDashboardView : UserControl
{
    public KickDashboardView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is KickDashboardViewModel viewModel)
        {
            // Wire up WebView2 initialization event (but don't initialize yet - wait for play button)
            viewModel.WebView2InitializeRequested -= OnWebView2InitializeRequested;
            viewModel.WebView2InitializeRequested += OnWebView2InitializeRequested;

            await viewModel.InitializeAsync();
        }
    }

    private async void OnWebView2InitializeRequested(object? sender, EventArgs e)
    {
        // Only initialize WebView2 when user clicks play button
        if (DataContext is KickDashboardViewModel viewModel && StreamPreviewWebViewContainer != null)
        {
            // Check if already created
            if (StreamPreviewWebViewContainer.Child != null)
                return;
                
            try
            {
                // Create WebView2 programmatically to avoid spawning Edge processes at startup
                var webView = new Microsoft.Web.WebView2.Wpf.WebView2();
                StreamPreviewWebViewContainer.Child = webView;
                
                // Use temp folder for WebView2 cache (cleared on exit)
                var tempFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VyralBot_WebView2_Temp");
                var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(
                    userDataFolder: tempFolder);
                await webView.EnsureCoreWebView2Async(env);
                viewModel.StreamPreviewWebView = webView;
            }
            catch
            {
                // Failed to initialize WebView2 for stream preview
            }
        }
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        // Dispose WebView2 when view is unloaded
        if (StreamPreviewWebViewContainer?.Child is Microsoft.Web.WebView2.Wpf.WebView2 webView)
        {
            try
            {
                webView.Dispose();
                StreamPreviewWebViewContainer.Child = null;
            }
            catch
            {
                // Failed to dispose WebView2
            }
        }

        if (DataContext is KickDashboardViewModel viewModel)
        {
            viewModel.WebView2InitializeRequested -= OnWebView2InitializeRequested;
            viewModel.Cleanup();
        }
    }
}
