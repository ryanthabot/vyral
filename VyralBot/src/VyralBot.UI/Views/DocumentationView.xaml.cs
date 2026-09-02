using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

/// <summary>
/// In-app documentation browser. Hosts a WebView2 that renders the AppDocs markdown
/// pages (see DocumentationService); link clicks are intercepted in-page and routed
/// through DocumentationViewModel so doc links, code views and history all work.
/// </summary>
public partial class DocumentationView : UserControl
{
    private WebView2? _webView;
    private bool _webViewReady;
    private DocumentationViewModel? _viewModel;

    public DocumentationView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = DataContext as DocumentationViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _ = _viewModel.InitializeAsync();
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
            _ = _viewModel.InitializeAsync();

        await CreateBrowserAsync();
    }

    private async Task CreateBrowserAsync()
    {
        if (_webView is not null || BrowserHost is null)
            return;

        try
        {
            var webView = new WebView2();
            BrowserHost.Child = webView;

            // Shared temp cache, consistent with the other WebView2 hosts in the app.
            var tempFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VyralBot_WebView2_Temp");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: tempFolder);
            await webView.EnsureCoreWebView2Async(environment);

            webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            webView.CoreWebView2.NavigationStarting += OnNavigationStarting;
            webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;

            _webView = webView;
            _webViewReady = true;

            // The ViewModel may already have content (singleton, navigated here before).
            if (_viewModel is { CurrentHtml.Length: > 0 })
                ApplyHtml(_viewModel.CurrentHtml);
        }
        catch (Exception ex)
        {
            if (_viewModel is not null)
            {
                _viewModel.StatusMessage = $"Browser failed to initialize: {ex.Message}";
                _viewModel.HasError = true;
            }
        }
    }

    /// <summary>Backstop: any navigation that escapes the click interceptor (e.g. JS redirects) is routed through the doc engine.</summary>
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (e.Uri is null)
            return;

        if (e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
            e.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return; // our own NavigateToString content

        e.Cancel = true;
        _viewModel?.HandleLink(e.Uri);
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var href = e.TryGetWebMessageAsString();
        if (!string.IsNullOrEmpty(href))
            _viewModel?.HandleLink(href);
    }

    /// <summary>After a doc page renders, jump to the heading fragment the link carried (if any).</summary>
    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        var fragment = _viewModel?.ConsumeScrollFragment();
        if (string.IsNullOrEmpty(fragment) || _webView?.CoreWebView2 is null)
            return;

        try
        {
            var id = System.Text.Json.JsonSerializer.Serialize(fragment);
            await _webView.CoreWebView2.ExecuteScriptAsync(
                $"var el = document.getElementById({id}); if (el) el.scrollIntoView({{behavior:'smooth'}});");
        }
        catch (Exception)
        {
            // Scrolling is cosmetic; ignore failures.
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentationViewModel.CurrentHtml) &&
            _viewModel is { CurrentHtml.Length: > 0 })
        {
            ApplyHtml(_viewModel.CurrentHtml);
        }
    }

    private void ApplyHtml(string html)
    {
        if (_webView is not null && _webViewReady)
        {
            try
            {
                _webView.NavigateToString(html);
            }
            catch (Exception ex)
            {
                if (_viewModel is not null)
                {
                    _viewModel.StatusMessage = $"Failed to render page: {ex.Message}";
                    _viewModel.HasError = true;
                }
            }
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel is null)
            return;

        var back = e.Key == Key.Back || (e.Key == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt) || e.Key == Key.BrowserBack;
        var forward = (e.Key == Key.Right && Keyboard.Modifiers == ModifierKeys.Alt) || e.Key == Key.BrowserForward;

        if (back && _viewModel.CanGoBack)
        {
            _viewModel.GoBackCommand.Execute(null);
            e.Handled = true;
        }
        else if (forward && _viewModel.CanGoForward)
        {
            _viewModel.GoForwardCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_webView is not null)
        {
            try
            {
                if (_webView.CoreWebView2 is not null)
                {
                    _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
                    _webView.CoreWebView2.NavigationStarting -= OnNavigationStarting;
                    _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
                }
                _webView.Dispose();
            }
            catch (Exception)
            {
                // WebView2 can throw during teardown if the core is already gone.
            }

            if (BrowserHost is not null)
                BrowserHost.Child = null;
            _webView = null;
            _webViewReady = false;
        }
    }
}
