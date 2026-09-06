using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Wpf;
using VyralBot.Data;
using System;

namespace VyralBot.UI.ViewModels;

/// <summary>
/// Reusable base class for OAuth setup wizards (Twitch, Kick, Google, etc.)
/// Provides all common wizard logic and makes URLs/branding configurable
/// </summary>
public abstract partial class BaseOAuthSetupViewModel : ViewModelBase
{
    protected readonly ILogger _logger;
    protected readonly IDataService _dataService;

    public WebView2? WebView2 { get; set; }

    public event EventHandler? WebView2InitializeRequested;
    public event EventHandler? WebView2DisposeRequested;
    public event EventHandler? SetupCompleted;

    // Protected method to raise WebView2InitializeRequested event from derived classes
    protected void OnWebView2InitializeRequested()
    {
        WebView2InitializeRequested?.Invoke(this, EventArgs.Empty);
    }

    // Configuration Properties (override in derived classes)
    public abstract string WizardTitle { get; }
    public abstract string PlatformName { get; }
    public abstract string Step1Title { get; }
    public abstract string Step1ButtonText { get; }
    public abstract string Step2Title { get; }
    public abstract string Step2ButtonText { get; }
    public abstract string Step3Title { get; }
    public abstract string Step3ButtonText { get; }
    public abstract string Step4Title { get; }

    [ObservableProperty]
    private int _currentStep = 1;

    [ObservableProperty]
    private bool _showSuccessScreen = false;

    [ObservableProperty]
    private bool _showModal = false;

    [ObservableProperty]
    private bool _showBrowser = false;

    [ObservableProperty]
    private bool _bothCredentialsSaved = false;

    [ObservableProperty]
    private bool _clientIdSaved = false;

    [ObservableProperty]
    private bool _clientSecretSaved = false;

    [ObservableProperty]
    private string _maskedClientId = "";

    [ObservableProperty]
    private string _maskedClientSecret = "";

    [ObservableProperty]
    private string _modalTitle = "";

    [ObservableProperty]
    private string _modalDescription = "";

    [ObservableProperty]
    private string _modalInput = "";

    [ObservableProperty]
    private string _clientIdInput = "";

    [ObservableProperty]
    private string _clientSecretInput = "";

    [ObservableProperty]
    private string _channelName = "";

    [ObservableProperty]
    private string _testMessageStatus = "Click 'Send Test Message' to verify the connection";

    [ObservableProperty]
    private string _oAuthStatus = "Not authorized";

    [ObservableProperty]
    private bool _isOAuthAuthorized = false;

    [ObservableProperty]
    private string? _oAuthUrl;

    [ObservableProperty]
    private bool _showManualCallbackInput = false;

    [ObservableProperty]
    private string _callbackUrlInput = "";

    protected string? _savedClientId;
    protected string? _savedClientSecret;
    protected string? _accessToken;
    protected string? _refreshToken;
    protected string? _authorizedUsername;
    protected Func<string, Task>? _modalSaveAction;

    protected BaseOAuthSetupViewModel(ILogger logger, IDataService dataService)
    {
        _logger = logger;
        _dataService = dataService;
    }

    // Abstract methods to be implemented by derived classes
    protected abstract string GetStep1Url();
    protected abstract string GetStep2Url();
    protected abstract Task SaveCredentialsAsync(string clientId, string clientSecret);
    protected abstract Task LoadCredentialsAsync();
    protected abstract string BuildAuthorizationUrl();
    protected abstract Task<(string AccessToken, string RefreshToken, string Username)?> HandleOAuthCallbackAsync(string url);
    protected abstract Task SendTestMessageAsync();

    [RelayCommand]
    private void GoToStep1()
    {
        _logger.LogInformation("GoToStep1 called");
        CurrentStep = 1;
        ShowSuccessScreen = false;
    }

    [RelayCommand]
    private void GoToStep2()
    {
        _logger.LogInformation("GoToStep2 called");
        CurrentStep = 2;
        ShowSuccessScreen = false;
    }

    [RelayCommand]
    private void GoToStep3()
    {
        _logger.LogInformation("GoToStep3 called");
        CurrentStep = 3;
        ShowSuccessScreen = false;
    }

    [RelayCommand]
    private void GoToStep4()
    {
        _logger.LogInformation("GoToStep4 called");
        
        // Dispose WebView2 when entering Step 4
        ShowBrowser = false;
        if (WebView2?.CoreWebView2 != null)
        {
            WebView2.CoreWebView2.Navigate("about:blank");
        }
        
        CurrentStep = 4;
        ShowSuccessScreen = false;
    }

    [RelayCommand]
    private void FinishSetup()
    {
        _logger.LogInformation("Finishing setup and transitioning to connected view");
        
        DisposeWebView2();
        
        // Call derived class cleanup hook
        OnFinishSetup();
        
        SetupCompleted?.Invoke(this, EventArgs.Empty);
        
        // Reset wizard state
        ShowSuccessScreen = false;
        CurrentStep = 1;
        ShowBrowser = false;
        ClientIdSaved = false;
        ClientSecretSaved = false;
        BothCredentialsSaved = false;
        IsOAuthAuthorized = false;
        OAuthStatus = "Not authorized";
        TestMessageStatus = "Click 'Send Test Message' to verify the connection";
        _savedClientId = null;
        _savedClientSecret = null;
        _accessToken = null;
        _refreshToken = null;
        _authorizedUsername = null;
    }
    
    protected virtual void OnFinishSetup()
    {
        // Override in derived classes to reset platform-specific state
    }

    [RelayCommand]
    private void GoBack()
    {
        if (CurrentStep > 1)
        {
            CurrentStep--;
            ShowBrowser = false;
            _logger.LogInformation("Going back to step {Step}", CurrentStep);
        }
    }

    [RelayCommand]
    private async Task OpenStep1BrowserAsync()
    {
        _logger.LogInformation("Opening Step 1 URL in WebView2");
        try
        {
            WebView2InitializeRequested?.Invoke(this, EventArgs.Empty);
            await Task.Delay(500);
            
            if (WebView2?.CoreWebView2 != null)
            {
                ShowBrowser = true;
                WebView2.CoreWebView2.Navigate(GetStep1Url());
                _logger.LogInformation("WebView2 navigated to Step 1");
            }
            else
            {
                _logger.LogWarning("WebView2 not initialized");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate WebView2");
        }
    }

    [RelayCommand]
    private async Task OpenStep2BrowserAsync()
    {
        _logger.LogInformation("Opening Step 2 URL in WebView2");
        _logger.LogInformation("Current state - ShowBrowser: {ShowBrowser}, ShowModal: {ShowModal}, CurrentStep: {CurrentStep}", ShowBrowser, ShowModal, CurrentStep);
        
        try
        {
            WebView2InitializeRequested?.Invoke(this, EventArgs.Empty);
            _logger.LogInformation("WebView2InitializeRequested event invoked, waiting for initialization...");
            await Task.Delay(1000); // Increased delay to ensure WebView2 initializes
            
            _logger.LogInformation("After delay - WebView2 is {Null}, CoreWebView2 is {CoreNull}", 
                WebView2 == null ? "NULL" : "initialized", 
                WebView2?.CoreWebView2 == null ? "NULL" : "initialized");
            
            if (WebView2?.CoreWebView2 != null)
            {
                ShowModal = false; // Ensure modal is not shown
                ShowBrowser = true;
                _logger.LogInformation("Set ShowBrowser = true, ShowModal = false");
                WebView2.CoreWebView2.Navigate(GetStep2Url());
                _logger.LogInformation("WebView2 navigated to Step 2");
            }
            else
            {
                _logger.LogWarning("WebView2 not initialized after event. WebView2 is {Null}", WebView2 == null ? "null" : "not null");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate WebView2");
        }
    }

    [RelayCommand]
    private void CopyRedirectUri()
    {
        _logger.LogInformation("Copying redirect URI to clipboard");
        try
        {
            var redirectUri = "http://localhost:3000";
            System.Windows.Clipboard.SetText(redirectUri);
            _logger.LogInformation("Redirect URI copied to clipboard: {Uri}", redirectUri);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy redirect URI to clipboard");
        }
    }

    [RelayCommand]
    private void ContinueFromStep1()
    {
        _logger.LogInformation("Continuing from Step 1");
        ShowBrowser = false;
        
        if (WebView2?.CoreWebView2 != null)
        {
            WebView2.CoreWebView2.Navigate("about:blank");
        }
        
        GoToStep2();
    }

    [RelayCommand]
    private async Task ContinueFromStep2Async()
    {
        _logger.LogInformation("Continuing from Step 2 to Step 3");
        ShowBrowser = false;
        
        if (WebView2?.CoreWebView2 != null)
        {
            WebView2.CoreWebView2.Navigate("about:blank");
        }
        
        if (!string.IsNullOrEmpty(_savedClientId) && !string.IsNullOrEmpty(_savedClientSecret))
        {
            await SaveCredentialsAsync(_savedClientId, _savedClientSecret);
            _logger.LogInformation("Credentials saved, moving to Step 3");
        }
        
        CurrentStep = 3;
        ShowSuccessScreen = false;
        
        await LoadCredentialsAsync();
        OAuthStatus = "Ready to authorize. Click the button below.";
    }

    [RelayCommand]
    private async Task AuthorizeOAuthAsync()
    {
        _logger.LogInformation("Starting OAuth authorization with WebView2");
        OAuthStatus = "Preparing authorization...";
        
        try
        {
            await LoadCredentialsAsync();
            
            var url = BuildAuthorizationUrl();
            
            if (string.IsNullOrEmpty(url) || !url.Contains("client_id="))
            {
                OAuthStatus = "❌ Client ID not found. Please go back to Step 2 and enter your credentials.";
                _logger.LogError("Client ID not loaded");
                return;
            }
            
            WebView2InitializeRequested?.Invoke(this, EventArgs.Empty);
            await Task.Delay(500);
            
            if (WebView2?.CoreWebView2 == null)
            {
                OAuthStatus = "❌ WebView2 not initialized";
                _logger.LogError("WebView2 not initialized");
                return;
            }
            
            // Unsubscribe first — stacking a new handler on every Authorize click
            // made the callback handler run once per previous attempt.
            WebView2.CoreWebView2.NavigationStarting -= OnOAuthNavigationStarting;
            WebView2.CoreWebView2.NavigationStarting += OnOAuthNavigationStarting;
            
            ShowBrowser = true;
            OAuthStatus = $"✅ Authorizing... Please sign in to {PlatformName}";
            
            WebView2.CoreWebView2.Navigate(url);
            _logger.LogInformation("WebView2 navigated to OAuth URL");
        }
        catch (Exception ex)
        {
            OAuthStatus = $"❌ Failed to start authorization: {ex.Message}";
            _logger.LogError(ex, "Failed to start OAuth flow");
        }
    }

    private async void OnOAuthNavigationStarting(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs e)
    {
        if (e.Uri.StartsWith("http://localhost:3000"))
        {
            _logger.LogInformation("OAuth callback detected: {Uri}", e.Uri);
            
            e.Cancel = true;
            
            if (WebView2?.CoreWebView2 != null)
            {
                WebView2.CoreWebView2.NavigationStarting -= OnOAuthNavigationStarting;
            }
            
            ShowBrowser = false;
            
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                await HandleCallbackAsync(e.Uri);
            });
        }
    }

    [RelayCommand]
    private async Task SubmitCallbackUrlAsync()
    {
        if (string.IsNullOrWhiteSpace(CallbackUrlInput))
        {
            OAuthStatus = "❌ Please enter the callback URL";
            return;
        }

        _logger.LogInformation("Manual callback URL submitted: {Url}", CallbackUrlInput);
        await HandleCallbackAsync(CallbackUrlInput);
    }

    public async Task HandleWebView2CallbackAsync(string url)
    {
        await HandleCallbackAsync(url);
    }

    private async Task HandleCallbackAsync(string url)
    {
        _logger.LogInformation("OAuth callback received: {Url}", url);
        
        try
        {
            var result = await HandleOAuthCallbackAsync(url);
            
            if (result == null)
            {
                OAuthStatus = "❌ Authorization cancelled or failed";
                ShowBrowser = false;
                return;
            }
            
            _accessToken = result.Value.AccessToken;
            _refreshToken = result.Value.RefreshToken;
            _authorizedUsername = result.Value.Username;
            ChannelName = _authorizedUsername;
            
            IsOAuthAuthorized = true;
            ShowBrowser = false;
            OAuthStatus = $"✓ Authorized as {_authorizedUsername}";
            
            _logger.LogInformation("OAuth authorization successful for user: {Username}", _authorizedUsername);
            
            CurrentStep = 4;
        }
        catch (Exception ex)
        {
            OAuthStatus = $"❌ Authorization failed: {ex.Message}";
            ShowBrowser = false;
            _logger.LogError(ex, "OAuth callback handling failed");
        }
    }

    [RelayCommand]
    private void ContinueFromStep3()
    {
        _logger.LogInformation("Continuing from Step 3 to Step 4");
        ShowBrowser = false;
        
        if (WebView2?.CoreWebView2 != null)
        {
            WebView2.CoreWebView2.Navigate("about:blank");
        }
        
        CurrentStep = 4;
        ShowSuccessScreen = false;
    }

    [RelayCommand]
    private async Task SendTestMessageCommandAsync()
    {
        await SendTestMessageAsync();
    }

    [RelayCommand]
    private async Task CompleteSetupAsync()
    {
        _logger.LogInformation("User confirmed setup is working, showing the success screen");
        await OnCompleteSetupAsync();
        ShowSuccessScreen = true;
    }

    protected virtual async Task OnCompleteSetupAsync()
    {
        await Task.CompletedTask;
        // Override in derived classes to save credentials before finishing
    }

    [RelayCommand]
    private void SaveClientId()
    {
        _logger.LogInformation("SaveClientId button clicked");
        ModalTitle = "Save Client ID";
        ModalDescription = $"Copy your Client ID from the {PlatformName} Developer Console and paste it below.";
        ModalInput = "";
        _modalSaveAction = async (value) =>
        {
            await Task.CompletedTask;
            _savedClientId = value;
            MaskedClientId = value.Substring(0, Math.Min(10, value.Length)) + "...";
            ClientIdSaved = true;
            OnPropertyChanged(nameof(ClientIdSaved));
            _logger.LogInformation("Client ID saved: {ClientId}", value.Substring(0, Math.Min(10, value.Length)) + "...");
            
            if (!string.IsNullOrEmpty(_savedClientId) && !string.IsNullOrEmpty(_savedClientSecret))
            {
                BothCredentialsSaved = true;
                OnPropertyChanged(nameof(BothCredentialsSaved));
            }
        };
        ShowModal = true;
    }

    [RelayCommand]
    private void SaveClientSecret()
    {
        _logger.LogInformation("SaveClientSecret button clicked");
        ModalTitle = "Save Client Secret";
        ModalDescription = $"Copy your Client Secret from the {PlatformName} Developer Console and paste it below.";
        ModalInput = "";
        _modalSaveAction = async (value) =>
        {
            await Task.CompletedTask;
            _savedClientSecret = value;
            MaskedClientSecret = value.Substring(0, Math.Min(10, value.Length)) + "...";
            ClientSecretSaved = true;
            OnPropertyChanged(nameof(ClientSecretSaved));
            _logger.LogInformation("Client Secret saved: {Secret}", value.Substring(0, Math.Min(10, value.Length)) + "...");
            
            if (!string.IsNullOrEmpty(_savedClientId) && !string.IsNullOrEmpty(_savedClientSecret))
            {
                BothCredentialsSaved = true;
                OnPropertyChanged(nameof(BothCredentialsSaved));
            }
        };
        ShowModal = true;
    }

    [RelayCommand]
    private void CloseModal()
    {
        _logger.LogInformation("Modal closed");
        ShowModal = false;
        ModalInput = "";
        _modalSaveAction = null;
    }

    [RelayCommand]
    private async Task SaveModalAsync()
    {
        _logger.LogInformation("SaveModal called with input: {Input}", ModalInput?.Substring(0, Math.Min(10, ModalInput?.Length ?? 0)));
        if (!string.IsNullOrWhiteSpace(ModalInput) && _modalSaveAction != null)
        {
            await _modalSaveAction(ModalInput);
            ShowModal = false;
            ModalInput = "";
            _modalSaveAction = null;
        }
    }

    [RelayCommand]
    private void CancelOAuth()
    {
        ShowBrowser = false;
        OAuthUrl = null;
        OAuthStatus = "Authorization cancelled";
        _logger.LogInformation("OAuth authorization cancelled by user");
    }

    public void ResetWizard(bool disposeWebView = false)
    {
        _logger.LogInformation("Resetting wizard (disposeWebView={DisposeWebView})", disposeWebView);
        
        CurrentStep = 1;
        ShowSuccessScreen = false;
        ShowBrowser = false;
        ShowModal = false;
        ShowManualCallbackInput = false;
        
        BothCredentialsSaved = false;
        ClientIdSaved = false;
        ClientSecretSaved = false;
        
        MaskedClientId = "";
        ClientIdInput = "";
        ClientSecretInput = "";
        ChannelName = "";
        CallbackUrlInput = "";
        
        IsOAuthAuthorized = false;
        OAuthStatus = "Not authorized";
        TestMessageStatus = "Click 'Send Test Message' to verify the connection";
        
        _savedClientId = null;
        _savedClientSecret = null;
        _accessToken = null;
        _refreshToken = null;
        _authorizedUsername = null;
        
        if (WebView2?.CoreWebView2 != null)
        {
            try
            {
                WebView2.CoreWebView2.NavigationStarting -= OnOAuthNavigationStarting;
                
                if (disposeWebView)
                {
                    DisposeWebView2();
                }
                else
                {
                    WebView2.CoreWebView2.Navigate("about:blank");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reset WebView2");
            }
        }
        
        _logger.LogInformation("Wizard reset complete");
    }

    protected void DisposeWebView2()
    {
        if (WebView2?.CoreWebView2 != null)
        {
            try
            {
                WebView2.CoreWebView2.NavigationStarting -= OnOAuthNavigationStarting;
            }
            catch { /* Event might not be subscribed */ }
        }
        
        WebView2DisposeRequested?.Invoke(this, EventArgs.Empty);
        WebView2 = null;
        
        _logger.LogInformation("WebView2 disposal requested");
    }

    public void Dispose()
    {
        // Cleanup if needed
    }
}
