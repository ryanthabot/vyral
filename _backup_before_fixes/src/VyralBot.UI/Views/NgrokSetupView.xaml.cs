using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

public partial class NgrokSetupView : UserControl
{
    public NgrokSetupView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is NgrokSetupViewModel viewModel)
        {
            viewModel.WebView2InitializeRequested += OnWebView2InitializeRequested;
        }
    }

    private async void OnWebView2InitializeRequested(object? sender, EventArgs e)
    {
        if (WebView2Browser == null) return;

        if (WebView2Browser.CoreWebView2 == null)
        {
            await WebView2Browser.EnsureCoreWebView2Async();
        }

        if (DataContext is NgrokSetupViewModel viewModel)
        {
            viewModel.WebView2 = WebView2Browser;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is NgrokSetupViewModel viewModel)
        {
            viewModel.WebView2InitializeRequested -= OnWebView2InitializeRequested;
        }
    }
}
