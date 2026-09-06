using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Wpf;
using VyralBot.Integrations.Twitch;
using VyralBot.Data;
using VyralBot.Data.Entities;
using System;
using System.Net.Http;
using System.Net.Sockets;
using System.IO;

namespace VyralBot.UI.ViewModels;

public partial class TwitchWebSetupViewModel : ObservableObject
{
    private readonly ILogger<TwitchWebSetupViewModel> _logger;
    private readonly TwitchOAuthService _oAuthService;
    private readonly IDataService _dataService;

    public WebView2? WebView2 { get; set; }

    public event EventHandler? WebView2InitializeRequested;
    public event EventHandler? WebView2DisposeRequested;

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

    [RelayCommand]
    private void CancelOAuth()
    {
        ShowBrowser = false;
        OAuthUrl = null;
        OAuthStatus = "Authorization cancelled";
        _logger.LogInformation("OAuth authorization cancelled by user");
    }

    private string? _savedClientId;
    private string? _savedClientSecret;
    private string? _accessToken;
    private string? _refreshToken;
    private string? _authorizedUsername;
    private string? _authorizedUserId;
    private Action<string>? _modalSaveAction;

    public event EventHandler? SetupCompleted;

    public TwitchWebSetupViewModel(
        ILogger<TwitchWebSetupViewModel> logger,
        TwitchOAuthService oAuthService,
        IDataService dataService)
    {
        _logger = logger;
        _oAuthService = oAuthService;
        _dataService = dataService;
    }

    public void Dispose()
    {
        // Cleanup if needed
    }

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
        CurrentStep = 4;
        ShowSuccessScreen = false;
    }

    [RelayCommand]
    private void FinishSetup()
    {
        _logger.LogInformation("Finishing setup and transitioning to connected view");

        // Dispose WebView2 to free resources (will be re-initialized on next setup)
        DisposeWebView2();

        // Notify parent to refresh and show connected view
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
        _authorizedUserId = null;
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
        _logger.LogInformation("Opening Twitch login page in WebView2");
        try
        {
            // Request WebView2 initialization
            WebView2InitializeRequested?.Invoke(this, EventArgs.Empty);

            // Wait a moment for initialization
            await Task.Delay(500);

            if (WebView2?.CoreWebView2 != null)
            {
                ShowBrowser = true;
                WebView2.CoreWebView2.Navigate("https://www.twitch.tv/login");
                _logger.LogInformation("WebView2 navigated to Twitch login");
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
        _logger.LogInformation("Opening Twitch Developer Console in WebView2");
        try
        {
            // Request WebView2 initialization if not already done
            WebView2InitializeRequested?.Invoke(this, EventArgs.Empty);

            // Wait a moment for initialization
            await Task.Delay(500);

            if (WebView2?.CoreWebView2 != null)
            {
                ShowBrowser = true;
                WebView2.CoreWebView2.Navigate("https://dev.twitch.tv/console/apps");
                _logger.LogInformation("WebView2 navigated to Developer Console");
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

        // Don't dispose WebView2 - just hide it for reuse
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

        // Don't dispose WebView2 - just hide it for reuse
        if (WebView2?.CoreWebView2 != null)
        {
            WebView2.CoreWebView2.Navigate("about:blank");
        }

        // Save credentials to file FIRST before moving to Step 3
        if (!string.IsNullOrEmpty(_savedClientId) && !string.IsNullOrEmpty(_savedClientSecret))
        {
            await _oAuthService.SaveCredentialsAsync(_savedClientId, _savedClientSecret);
            _logger.LogInformation("Credentials saved to file, moving to Step 3");
        }

        // Move to Step 3 after credentials are saved
        CurrentStep = 3;
        ShowSuccessScreen = false;

        // Load credentials into OAuth service for immediate use
        await _oAuthService.LoadCredentialsAsync();
        OAuthStatus = "Ready to authorize. Click the button below.";
    }

    [RelayCommand]
    private async Task AuthorizeOAuthAsync()
    {
        _logger.LogInformation("Starting OAuth authorization with WebView2");
        OAuthStatus = "Preparing authorization...";

        try
        {
            // Load credentials from file to ensure they're current
            await _oAuthService.LoadCredentialsAsync();

            // Build OAuth URL (BuildAuthorizationUrl checks for null ClientId internally)
            var url = _oAuthService.BuildAuthorizationUrl();

            if (string.IsNullOrEmpty(url) || !url.Contains("client_id="))
            {
                OAuthStatus = "❌ Client ID not found. Please go back to Step 2 and enter your credentials.";
                _logger.LogError("Client ID not loaded from file");
                return;
            }

            // Request WebView2 initialization for OAuth
            WebView2InitializeRequested?.Invoke(this, EventArgs.Empty);

            // Wait a moment for initialization
            await Task.Delay(500);

            if (WebView2?.CoreWebView2 == null)
            {
                OAuthStatus = "❌ WebView2 not initialized";
                _logger.LogError("WebView2 not initialized");
                return;
            }

            // Subscribe to navigation events to catch the callback.
            // Unsubscribe first so repeated Authorize clicks don't stack handlers.
            WebView2.CoreWebView2.NavigationStarting -= OnOAuthNavigationStarting;
            WebView2.CoreWebView2.NavigationStarting += OnOAuthNavigationStarting;

            // Surface the built authorization URL so the debug panel can show it
            OAuthUrl = url;

            ShowBrowser = true;
            OAuthStatus = "✅ Authorizing... Please sign in to Twitch";

            // Navigate to OAuth URL in WebView2
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
        // Check if this is the callback URL
        if (e.Uri.StartsWith("http://localhost:3000"))
        {
            _logger.LogInformation("OAuth callback detected: {Uri}", e.Uri);

            // Cancel the navigation to localhost (it won't load anyway)
            e.Cancel = true;

            // Unsubscribe from the event
            if (WebView2?.CoreWebView2 != null)
            {
                WebView2.CoreWebView2.NavigationStarting -= OnOAuthNavigationStarting;
            }

            // Hide browser and process the callback
            ShowBrowser = false;

            // Handle the callback on UI thread
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                await HandleOAuthCallbackAsync(e.Uri);
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
        await HandleOAuthCallbackAsync(CallbackUrlInput);
    }

    /// <summary>
    /// Called from code-behind when WebView2 navigates to callback URL or manual input
    /// </summary>
    public async Task HandleWebView2CallbackAsync(string url)
    {
        await HandleOAuthCallbackAsync(url);
    }

    private async Task HandleOAuthCallbackAsync(string url)
    {
        _logger.LogInformation("OAuth callback received: {Url}", url);

        try
        {
            var result = await _oAuthService.HandleOAuthCallbackAsync(url);

            if (result == null)
            {
                OAuthStatus = "❌ Authorization cancelled or failed";
                ShowBrowser = false;
                return;
            }

            _accessToken = result.Value.AccessToken;
            _refreshToken = result.Value.RefreshToken;
            _authorizedUsername = result.Value.Username;
            _authorizedUserId = result.Value.UserId;
            ChannelName = _authorizedUsername; // Auto-fill channel name

            IsOAuthAuthorized = true;
            ShowBrowser = false;
            OAuthStatus = $"✓ Authorized as {_authorizedUsername}";

            _logger.LogInformation("OAuth authorization successful for user: {Username}", _authorizedUsername);

            // Move to Step 4 for testing
            CurrentStep = 4;
        }
        catch (Exception ex)
        {
            OAuthStatus = $"❌ Authorization failed: {ex.Message}";
            ShowBrowser = false;
            _logger.LogError(ex, "OAuth callback handling failed");
        }
    }

    private async Task FinishSetupAutomaticallyAsync()
    {
        _logger.LogInformation("Auto-finishing setup for user: {Username}", _authorizedUsername);

        try
        {
            // Save to database
            var platform = await _dataService.GetPlatformByNameAsync("Twitch");

            if (platform == null)
            {
                platform = new Platform
                {
                    Name = "Twitch",
                    Status = "Connected",
                    Enabled = true,
                    CreatedAt = DateTime.UtcNow
                };
            }

            platform.AccessToken = TwitchOAuthService.Encrypt(_accessToken ?? "");
            platform.RefreshToken = TwitchOAuthService.Encrypt(_refreshToken ?? "");
            platform.Username = _authorizedUsername;

            // Persist the authenticated user id - TwitchService only connects EventSub
            // when the Platform row has both UserId and ClientId
            if (!string.IsNullOrEmpty(_authorizedUserId))
            {
                platform.UserId = _authorizedUserId;
            }

            // Only overwrite the app credentials when they were collected in this wizard
            // run; the reconnect flow (step 3 only) must keep the stored values intact
            if (!string.IsNullOrEmpty(_savedClientId))
            {
                platform.ClientId = _savedClientId;
            }
            if (!string.IsNullOrEmpty(_savedClientSecret))
            {
                platform.ClientSecret = _savedClientSecret;
            }
            platform.TokenExpiresAt = DateTime.UtcNow.AddHours(4);
            platform.Status = "Connected";
            platform.ConnectedAt = DateTime.UtcNow;
            platform.UpdatedAt = DateTime.UtcNow;

            await _dataService.SavePlatformAsync(platform);

            _logger.LogInformation("Twitch platform configuration saved to database");

            // Dispose WebView2 immediately to free resources
            DisposeWebView2();

            // Trigger completion event on UI thread without blocking
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                SetupCompleted?.Invoke(this, EventArgs.Empty);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save Twitch configuration");
            TestMessageStatus = $"❌ Failed to save: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ContinueFromStep3()
    {
        _logger.LogInformation("Continuing from Step 3 to Step 4");
        CurrentStep = 4;
        ShowSuccessScreen = false;
    }

    [RelayCommand]
    private async Task SendTestMessageAsync()
    {
        _logger.LogInformation("Sending test message to channel: {Channel}", ChannelName);

        if (string.IsNullOrWhiteSpace(ChannelName))
        {
            TestMessageStatus = "❌ Please enter a channel name first";
            return;
        }

        if (string.IsNullOrEmpty(_accessToken))
        {
            TestMessageStatus = "❌ Please complete Step 3 first (OAuth authorization)";
            return;
        }

        TestMessageStatus = "⏳ Connecting to Twitch IRC...";

        try
        {
            // Connect to Twitch IRC and send a test message
            using var client = new System.Net.Sockets.TcpClient();
            await client.ConnectAsync("irc.chat.twitch.tv", 6667);

            using var stream = client.GetStream();
            using var reader = new System.IO.StreamReader(stream);
            using var writer = new System.IO.StreamWriter(stream) { AutoFlush = true };

            // Authenticate with OAuth token
            await writer.WriteLineAsync($"PASS oauth:{_accessToken}");
            await writer.WriteLineAsync($"NICK {_authorizedUsername}");
            await writer.WriteLineAsync($"JOIN #{ChannelName.ToLower()}");

            TestMessageStatus = "⏳ Sending test message...";

            // Wait for connection confirmation
            string? line;
            bool connected = false;
            var timeout = DateTime.Now.AddSeconds(10);

            while (DateTime.Now < timeout && (line = await reader.ReadLineAsync()) != null)
            {
                _logger.LogInformation("IRC: {Line}", line);

                if (line.Contains("PING"))
                {
                    await writer.WriteLineAsync($"PONG {line.Split(':')[1]}");
                }

                if (line.Contains("376") || line.Contains("366")) // End of MOTD or end of NAMES
                {
                    connected = true;
                    break;
                }
            }

            if (connected)
            {
                // Send test message
                await writer.WriteLineAsync($"PRIVMSG #{ChannelName.ToLower()} :VyralBot test message - Setup successful! 🎉");
                TestMessageStatus = $"✓ Test message sent to #{ChannelName}! Check your chat.";
                _logger.LogInformation("Test message sent successfully");
            }
            else
            {
                TestMessageStatus = "❌ Connection timeout. Please verify the channel name.";
                _logger.LogWarning("IRC connection timeout");
            }
        }
        catch (Exception ex)
        {
            TestMessageStatus = $"❌ Error: {ex.Message}";
            _logger.LogError(ex, "Failed to send test message");
        }
    }

    [RelayCommand]
    private async Task CompleteSetupAsync()
    {
        _logger.LogInformation("User confirmed setup is working, saving to database");
        await FinishSetupAutomaticallyAsync();
    }

    [RelayCommand]
    private void SaveClientId()
    {
        _logger.LogInformation("SaveClientId button clicked");
        ModalTitle = "Save Client ID";
        ModalDescription = "Copy your Client ID from the Twitch Developer Console and paste it below.";
        ModalInput = "";
        _modalSaveAction = (value) =>
        {
            _savedClientId = value;
            MaskedClientId = value.Substring(0, Math.Min(10, value.Length)) + "...";
            ClientIdSaved = true;
            _logger.LogInformation("Client ID saved: {ClientId}", value.Substring(0, Math.Min(10, value.Length)) + "...");
        };
        ShowModal = true;
    }

    [RelayCommand]
    private void SaveClientSecret()
    {
        _logger.LogInformation("SaveClientSecret button clicked");
        ModalTitle = "Save Client Secret";
        ModalDescription = "Copy your Client Secret from the Twitch Developer Console and paste it below.";
        ModalInput = "";
        _modalSaveAction = (value) =>
        {
            _savedClientSecret = value;
            ClientSecretSaved = true;
            _logger.LogInformation("Client Secret saved");
        };
        ShowModal = true;
    }

    partial void OnClientIdSavedChanged(bool value)
    {
        BothCredentialsSaved = ClientIdSaved && ClientSecretSaved;
        _logger.LogInformation("ClientIdSaved changed to {Value}, BothCredentialsSaved={Both}", value, BothCredentialsSaved);
    }

    partial void OnClientSecretSavedChanged(bool value)
    {
        BothCredentialsSaved = ClientIdSaved && ClientSecretSaved;
        _logger.LogInformation("ClientSecretSaved changed to {Value}, BothCredentialsSaved={Both}", value, BothCredentialsSaved);
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
    private void SaveModal()
    {
        _logger.LogInformation("SaveModal called with input: {Input}", ModalInput?.Substring(0, Math.Min(10, ModalInput?.Length ?? 0)));
        if (!string.IsNullOrWhiteSpace(ModalInput) && _modalSaveAction != null)
        {
            _modalSaveAction(ModalInput);
            ShowModal = false;
            ModalInput = "";
            _modalSaveAction = null;
        }
    }

    public void ResetWizard(bool disposeWebView = false)
    {
        _logger.LogInformation("Resetting Twitch setup wizard (disposeWebView={DisposeWebView})", disposeWebView);

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
        _authorizedUserId = null;

        // Clear WebView2
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

    private void DisposeWebView2()
    {
        // Unsubscribe from WebView2 navigation events if subscribed
        if (WebView2?.CoreWebView2 != null)
        {
            try
            {
                WebView2.CoreWebView2.NavigationStarting -= OnOAuthNavigationStarting;
            }
            catch { /* Event might not be subscribed */ }
        }

        // Request disposal from View to free resources
        WebView2DisposeRequested?.Invoke(this, EventArgs.Empty);
        WebView2 = null;

        _logger.LogInformation("WebView2 disposal requested");
    }
}
