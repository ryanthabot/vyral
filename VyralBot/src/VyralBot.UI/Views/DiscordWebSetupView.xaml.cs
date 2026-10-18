using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VyralBot.UI.ViewModels;

namespace VyralBot.UI.Views;

/// <summary>
/// Discord bot-token setup wizard. Hosts an on-demand WebView2 (Developer Portal /
/// bot invite) exactly like the other platform setup views.
/// </summary>
public partial class DiscordWebSetupView : UserControl
{
    private readonly ILogger<DiscordWebSetupView>? _viewLogger;

    public DiscordWebSetupView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        try
        {
            _viewLogger = App.ServiceProvider?.GetService<ILogger<DiscordWebSetupView>>();
        }
        catch { }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is DiscordWebSetupViewModel viewModel)
        {
            _viewLogger?.LogInformation("DIAG: Discord wizard view loaded, DataContext={Type}, OpenStep1BrowserCommand={Cmd}, WebView2={Wv2}",
                viewModel.GetType().Name,
                viewModel.OpenStep1BrowserCommand?.ToString() ?? "NULL",
                viewModel.WebView2?.ToString() ?? "null");
            viewModel.WebView2InitializeRequested += OnWebView2InitializeRequested;
            viewModel.WebView2DisposeRequested += OnWebView2DisposeRequested;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
        else
        {
            _viewLogger?.LogWarning("DIAG: Discord wizard view loaded with unexpected DataContext {Type}", DataContext?.GetType().Name ?? "null");
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Clear the masked secret box whenever the modal closes so a re-opened modal starts empty
        if (e.PropertyName == nameof(DiscordWebSetupViewModel.ShowModal) &&
            sender is DiscordWebSetupViewModel { ShowModal: false })
        {
            ModalSecretBox?.Clear();
        }

        // The manual fallback token is masked in a PasswordBox (not bindable);
        // clear the box when the view model resets the manual input.
        if (e.PropertyName == nameof(DiscordWebSetupViewModel.ManualTokenInput) &&
            sender is DiscordWebSetupViewModel { ManualTokenInput: "" or null })
        {
            ManualTokenBox?.Clear();
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

                if (DataContext is DiscordWebSetupViewModel viewModel)
                {
                    viewModel.WebView2 = webView2;
                }
                _viewLogger?.LogInformation("DIAG: Discord wizard WebView2 initialized");
            }
            catch (Exception ex)
            {
                _viewLogger?.LogError(ex, "DIAG: Discord wizard WebView2 initialization FAILED");
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
        if (DataContext is DiscordWebSetupViewModel viewModel)
        {
            viewModel.WebView2InitializeRequested -= OnWebView2InitializeRequested;
            viewModel.WebView2DisposeRequested -= OnWebView2DisposeRequested;
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }

    private void ModalSecretBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        // The bot token is masked like the other platform secrets — feed it to the
        // view model manually because PasswordBox.Password is not bindable.
        if (DataContext is DiscordWebSetupViewModel viewModel && sender is PasswordBox passwordBox)
        {
            viewModel.ModalInput = passwordBox.Password;
        }
    }

    private void ManualTokenBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        // The manual fallback bot token is masked like the other platform
        // secrets — feed it to the view model manually because
        // PasswordBox.Password is not bindable.
        if (DataContext is DiscordWebSetupViewModel viewModel && sender is PasswordBox passwordBox)
        {
            viewModel.ManualTokenInput = passwordBox.Password;
        }
    }
}
