using System.Windows.Controls;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

public partial class YouTubeDashboardView : UserControl
{
    private bool _webViewInitialized = false;

    public YouTubeDashboardView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        // No longer auto-initialize WebView2 on load
        // It will be initialized on-demand when user clicks "Show Preview"
        if (DataContext is YouTubeDashboardViewModel viewModel)
        {
            viewModel.WebView2InitializeRequested += OnWebView2InitializeRequested;
        }
    }

    private async void OnWebView2InitializeRequested(object? sender, EventArgs e)
    {
        if (_webViewInitialized || StreamPreviewWebViewContainer == null)
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
            _webViewInitialized = true;
            
            if (DataContext is YouTubeDashboardViewModel viewModel)
            {
                viewModel.StreamPreviewWebView = webView;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to initialize WebView2: {ex}");
        }
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is YouTubeDashboardViewModel viewModel)
        {
            viewModel.WebView2InitializeRequested -= OnWebView2InitializeRequested;
        }

        // Dispose WebView2 when view is unloaded
        if (_webViewInitialized && StreamPreviewWebViewContainer?.Child is Microsoft.Web.WebView2.Wpf.WebView2 webView)
        {
            try
            {
                webView.Dispose();
                StreamPreviewWebViewContainer.Child = null;
                _webViewInitialized = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to dispose WebView2: {ex}");
            }
        }
    }
}
