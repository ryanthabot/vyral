using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using VyralBot.UI.ViewModels;
using Microsoft.Web.WebView2.Wpf;

namespace VyralBot.UI.Views;

public partial class KickWebSetupView : UserControl
{
    private WebView2? _webView2Browser;

    public KickWebSetupView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Subscribe to events for dynamic WebView2 creation/disposal
        if (DataContext is KickWebSetupViewModel viewModel)
        {
            viewModel.WebView2InitializeRequested += OnWebView2InitializeRequested;
            viewModel.WebView2DisposeRequested += OnWebView2DisposeRequested;
        }
    }

    private async void OnWebView2InitializeRequested(object? sender, EventArgs e)
    {
        try
        {
            // Don't create if already exists
            if (_webView2Browser != null)
            {
                var existingVm = DataContext as KickWebSetupViewModel;
                if (existingVm != null)
                {
                    existingVm.WebView2 = _webView2Browser;
                }
                return;
            }

            // Dynamically create WebView2 control
            _webView2Browser = new WebView2
            {
                Focusable = true
            };

            // Add to container
            WebView2Container.Child = _webView2Browser;

            // Initialize CoreWebView2 with temp user data folder (cleared on exit)
            var tempFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VyralBot_WebView2_Temp");
            var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(
                userDataFolder: tempFolder);
            await _webView2Browser.EnsureCoreWebView2Async(env);

            // Provide to ViewModel
            var newVm = DataContext as KickWebSetupViewModel;
            if (newVm != null)
            {
                newVm.WebView2 = _webView2Browser;
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
            if (_webView2Browser != null)
            {
                // Clear ViewModel reference
                if (DataContext is KickWebSetupViewModel viewModel)
                {
                    viewModel.WebView2 = null;
                }

                // Navigate to blank to stop ongoing operations
                if (_webView2Browser.CoreWebView2 != null)
                {
                    _webView2Browser.CoreWebView2.Navigate("about:blank");
                }

                // Remove from visual tree
                WebView2Container.Child = null;

                // Dispose the control
                _webView2Browser.Dispose();
                _webView2Browser = null;

                // Force aggressive garbage collection to free WebView2 processes
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
            }
        }
        catch
        {
            // Error disposing WebView2
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Ensure WebView2 is disposed when view unloads
        OnWebView2DisposeRequested(this, EventArgs.Empty);

        if (DataContext is KickWebSetupViewModel viewModel)
        {
            viewModel.WebView2InitializeRequested -= OnWebView2InitializeRequested;
            viewModel.WebView2DisposeRequested -= OnWebView2DisposeRequested;
        }
    }
}
