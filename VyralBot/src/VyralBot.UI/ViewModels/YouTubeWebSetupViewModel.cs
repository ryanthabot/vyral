using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Wpf;
using VyralBot.Data;
using VyralBot.Data.Entities;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;

namespace VyralBot.UI.ViewModels;

public partial class YouTubeWebSetupViewModel : ObservableObject
{
    private readonly ILogger<YouTubeWebSetupViewModel> _logger;
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

    // YouTube-specific properties
    [ObservableProperty]
    private bool _projectIdSaved = false;

    [ObservableProperty]
    private bool _showCopySearch = false;

    [ObservableProperty]
    private bool _showOAuthConsentReload = true;

    [ObservableProperty]
    private bool _scopesAdded = false;

    [ObservableProperty]
    private bool _userAdded = false;

    [RelayCommand]
    private async Task AddUserAsync()
    {
        _logger.LogInformation("AddUser button clicked - navigating to audience page");

        if (string.IsNullOrEmpty(_savedProjectId))
        {
            OAuthStatus = "❌ Project ID not found. Please go back to Step 1.";
            return;
        }

        var audienceUrl = $"https://console.cloud.google.com/auth/audience?project={Uri.EscapeDataString(_savedProjectId)}";

        // Open in WebView2
        WebView2InitializeRequested?.Invoke(this, EventArgs.Empty);
        await Task.Delay(500);

        if (WebView2?.CoreWebView2 != null)
        {
            ShowBrowser = true;
            WebView2.CoreWebView2.Navigate(audienceUrl);
            _logger.LogInformation("Navigated to audience page: {Url}", audienceUrl);
        }
        else
        {
            OAuthStatus = "❌ WebView2 not initialized";
        }

        UserAdded = true;
    }

    [RelayCommand]
    private void CancelOAuth()
    {
        ShowBrowser = false;
        OAuthUrl = null;
        OAuthStatus = "Authorization cancelled";
        _logger.LogInformation("OAuth authorization cancelled by user");
    }

    private string? _savedProjectId;
    private string? _savedClientId;
    private string? _savedClientSecret;
    private string? _accessToken;
    private string? _refreshToken;
    private string? _authorizedUsername;
    private Action<string>? _modalSaveAction;

    public event EventHandler? SetupCompleted;

    public YouTubeWebSetupViewModel(
        ILogger<YouTubeWebSetupViewModel> logger,
        IDataService dataService)
    {
        _logger = logger;
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
        ProjectIdSaved = false;
        ShowCopySearch = false;
        ShowOAuthConsentReload = true;
        ScopesAdded = false;
        UserAdded = false;
        IsOAuthAuthorized = false;
        OAuthStatus = "Not authorized";
        TestMessageStatus = "Click 'Send Test Message' to verify the connection";
        _savedProjectId = null;
        _savedClientId = null;
        _savedClientSecret = null;
        _accessToken = null;
        _refreshToken = null;
        _authorizedUsername = null;
    }

    [RelayCommand]
    private void GoBack()
    {
        if (CurrentStep > 1)
        {
            CurrentStep--;
            ShowBrowser = false;
            _logger.LogInformation("Going back to step {Step}", CurrentStep);

            // Reset OAuth Consent Reload visibility when navigating back to step 2
            if (CurrentStep == 2)
            {
                ShowOAuthConsentReload = true;
            }
        }
    }

    [RelayCommand]
    private async Task OpenStep1BrowserAsync()
    {
        _logger.LogInformation("Opening Google Cloud Console in WebView2");
        try
        {
            WebView2InitializeRequested?.Invoke(this, EventArgs.Empty);
            await Task.Delay(500);

            if (WebView2?.CoreWebView2 != null)
            {
                ShowBrowser = true;
                WebView2.CoreWebView2.Navigate("https://console.cloud.google.com/");
                _logger.LogInformation("WebView2 navigated to Google Cloud Console");
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
    private void SaveProjectId()
    {
        _logger.LogInformation("SaveProjectId button clicked");
        ModalTitle = "Save Project ID";
        ModalDescription = "Enter your Google Cloud project ID. You can find this on the Google Cloud Console dashboard.";
        ModalInput = "";
        _modalSaveAction = async (value) =>
        {
            _savedProjectId = value.Trim();
            ProjectIdSaved = true;
            ShowCopySearch = true;
            OnPropertyChanged(nameof(ProjectIdSaved));
            OnPropertyChanged(nameof(ShowCopySearch));
            _logger.LogInformation("Project ID saved: {ProjectId}", _savedProjectId);

            // Navigate to the API library page with the project ID
            try
            {
                WebView2InitializeRequested?.Invoke(this, EventArgs.Empty);
                await Task.Delay(500);

                if (WebView2?.CoreWebView2 != null)
                {
                    ShowBrowser = true;
                    var libraryUrl = $"https://console.cloud.google.com/apis/library?project={Uri.EscapeDataString(_savedProjectId)}";
                    WebView2.CoreWebView2.Navigate(libraryUrl);
                    _logger.LogInformation("WebView2 navigated to API library: {Url}", libraryUrl);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to navigate to API library");
            }
        };
        ShowModal = true;
    }

    [RelayCommand]
    private void CopySearch()
    {
        _logger.LogInformation("Copying YouTube Data API v3 search text to clipboard");
        try
        {
            System.Windows.Clipboard.SetText("YouTube Data API v3");
            _logger.LogInformation("Search text copied to clipboard");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy search text to clipboard");
        }
    }

    [RelayCommand]
    private async Task OpenStep2BrowserAsync()
    {
        _logger.LogInformation("Opening Google Cloud Console Credentials in WebView2");
        try
        {
            WebView2InitializeRequested?.Invoke(this, EventArgs.Empty);
            await Task.Delay(500);

            if (WebView2?.CoreWebView2 != null)
            {
                ShowBrowser = true;
                var credentialsUrl = string.IsNullOrEmpty(_savedProjectId)
                    ? "https://console.cloud.google.com/apis/credentials"
                    : $"https://console.cloud.google.com/apis/credentials?project={Uri.EscapeDataString(_savedProjectId)}";
                WebView2.CoreWebView2.Navigate(credentialsUrl);
                _logger.LogInformation("WebView2 navigated to Credentials: {Url}", credentialsUrl);
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
    private void OAuthConsentReload()
    {
        _logger.LogInformation("OAuth Consent Reload clicked - navigating back to credentials page");
        try
        {
            if (WebView2?.CoreWebView2 != null)
            {
                var credentialsUrl = string.IsNullOrEmpty(_savedProjectId)
                    ? "https://console.cloud.google.com/apis/credentials"
                    : $"https://console.cloud.google.com/apis/credentials?project={Uri.EscapeDataString(_savedProjectId)}";
                WebView2.CoreWebView2.Navigate(credentialsUrl);
                _logger.LogInformation("WebView2 navigated back to credentials page");
            }

            // Hide the button after clicking
            ShowOAuthConsentReload = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload credentials page");
        }
    }

    [RelayCommand]
    private void CopyRedirectUri()
    {
        _logger.LogInformation("Copying redirect URI to clipboard");
        try
        {
            var redirectUri = "http://localhost:3000/oauth2callback";
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
    private async Task AddScopesAsync()
    {
        _logger.LogInformation("Add Scopes button clicked");
        try
        {
            // Copy the scope URL to clipboard
            System.Windows.Clipboard.SetText("https://www.googleapis.com/auth/youtube.force-ssl");
            _logger.LogInformation("Scope URL copied to clipboard");

            // Navigate to the scopes page
            WebView2InitializeRequested?.Invoke(this, EventArgs.Empty);
            await Task.Delay(500);

            if (WebView2?.CoreWebView2 != null)
            {
                ShowBrowser = true;
                var scopesUrl = string.IsNullOrEmpty(_savedProjectId)
                    ? "https://console.cloud.google.com/auth/scopes"
                    : $"https://console.cloud.google.com/auth/scopes?project={Uri.EscapeDataString(_savedProjectId)}";
                WebView2.CoreWebView2.Navigate(scopesUrl);
                _logger.LogInformation("WebView2 navigated to scopes page: {Url}", scopesUrl);
            }

            ScopesAdded = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate to scopes page");
        }
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

        // Save credentials to database
        if (!string.IsNullOrEmpty(_savedClientId) && !string.IsNullOrEmpty(_savedClientSecret))
        {
            _logger.LogInformation("Saving Client ID and Client Secret to database");
            var platform = await _dataService.GetPlatformByNameAsync("YouTube");
            if (platform != null)
            {
                platform.ClientId = _savedClientId;
                platform.ClientSecret = _savedClientSecret;
                if (!string.IsNullOrEmpty(_savedProjectId))
                {
                    platform.ChannelId = _savedProjectId;
                }
                await _dataService.SavePlatformAsync(platform);
                _logger.LogInformation("Client credentials saved to database");
            }
            else
            {
                platform = new Platform
                {
                    Name = "YouTube",
                    ClientId = _savedClientId,
                    ClientSecret = _savedClientSecret,
                    ChannelId = _savedProjectId,
                    Status = "Configuring"
                };
                await _dataService.SavePlatformAsync(platform);
                _logger.LogInformation("Created YouTube platform with client credentials");
            }
        }

        CurrentStep = 3;
        ShowSuccessScreen = false;
        OAuthStatus = "Ready to authorize. Click the button below.";
    }

    [RelayCommand]
    private async Task AuthorizeOAuthAsync()
    {
        _logger.LogInformation("Starting YouTube OAuth authorization with WebView2");
        OAuthStatus = "Preparing authorization...";

        try
        {
            if (string.IsNullOrEmpty(_savedClientId))
            {
                OAuthStatus = "❌ Client ID not found. Please go back to Step 2 and enter your credentials.";
                _logger.LogError("Client ID not available");
                return;
            }

            // Build proper YouTube OAuth URL
            var authUrl = "https://accounts.google.com/o/oauth2/v2/auth" +
                $"?client_id={Uri.EscapeDataString(_savedClientId)}" +
                $"&redirect_uri={Uri.EscapeDataString("http://localhost:3000/oauth2callback")}" +
                "&response_type=code" +
                $"&scope={Uri.EscapeDataString("https://www.googleapis.com/auth/youtube.force-ssl")}" +
                "&access_type=offline" +
                "&prompt=consent";

            OAuthUrl = authUrl;
            _logger.LogInformation("Built OAuth URL: {Url}", authUrl);

            // Request WebView2 initialization
            WebView2InitializeRequested?.Invoke(this, EventArgs.Empty);
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

            ShowBrowser = true;
            OAuthStatus = "✅ Authorizing... Please Sign In to YouTube";

            // Navigate to OAuth URL in WebView2
            WebView2.CoreWebView2.Navigate(authUrl);
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
        if (e.Uri.StartsWith("http://localhost:3000/oauth2callback"))
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
            // Extract the authorization code from the callback URL
            var uri = new Uri(url);
            var queryParams = HttpUtility.ParseQueryString(uri.Query);
            var code = queryParams["code"];

            if (string.IsNullOrEmpty(code))
            {
                var error = queryParams["error"];
                OAuthStatus = $"❌ Authorization failed: {error ?? "No code received"}";
                ShowBrowser = false;
                return;
            }

            _logger.LogInformation("Authorization code received, exchanging for tokens...");
            OAuthStatus = "⏳ Exchanging code for tokens...";

            // Exchange the code for tokens via POST to Google's token endpoint
            using var httpClient = new HttpClient();
            var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "code", code },
                { "client_id", _savedClientId ?? "" },
                { "client_secret", _savedClientSecret ?? "" },
                { "redirect_uri", "http://localhost:3000/oauth2callback" },
                { "grant_type", "authorization_code" }
            });

            var tokenResponse = await httpClient.PostAsync("https://oauth2.googleapis.com/token", tokenRequest);
            var tokenContent = await tokenResponse.Content.ReadAsStringAsync();

            if (!tokenResponse.IsSuccessStatusCode)
            {
                _logger.LogError("Token exchange failed: {StatusCode} - {Response}", tokenResponse.StatusCode, tokenContent);
                OAuthStatus = $"❌ Token exchange failed: {tokenResponse.StatusCode}";
                ShowBrowser = false;
                return;
            }

            var tokenJson = JsonDocument.Parse(tokenContent);
            var root = tokenJson.RootElement;

            if (!root.TryGetProperty("access_token", out var accessTokenElement))
            {
                _logger.LogError("Token response missing 'access_token'. Response: {Response}", tokenContent);
                OAuthStatus = "❌ Token response missing access_token";
                ShowBrowser = false;
                return;
            }

            _accessToken = accessTokenElement.GetString();
            _refreshToken = root.TryGetProperty("refresh_token", out var refreshElement)
                ? refreshElement.GetString()
                : null;

            _logger.LogInformation("Token exchange successful. Access token received. Refresh token: {HasRefresh}",
                _refreshToken != null ? "Yes" : "No");

            // Get user info from YouTube API to get channel name
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);

            var channelResponse = await httpClient.GetAsync(
                "https://www.googleapis.com/youtube/v3/channels?part=snippet&mine=true");

            if (channelResponse.IsSuccessStatusCode)
            {
                var channelContent = await channelResponse.Content.ReadAsStringAsync();
                _logger.LogInformation("Channel API response: {Response}", channelContent);
                var channelJson = JsonDocument.Parse(channelContent);

                if (channelJson.RootElement.TryGetProperty("items", out var items) && items.GetArrayLength() > 0)
                {
                    var firstItem = items[0];
                    if (firstItem.TryGetProperty("snippet", out var snippet) &&
                        snippet.TryGetProperty("title", out var title))
                    {
                        _authorizedUsername = title.GetString();
                        _logger.LogInformation("YouTube channel name: {Username}", _authorizedUsername);
                    }
                    else
                    {
                        _authorizedUsername = "YouTube User";
                        _logger.LogWarning("Channel response missing snippet.title");
                    }
                }
                else
                {
                    _authorizedUsername = "YouTube User";
                    _logger.LogWarning("Channel response has no items");
                }
            }
            else
            {
                var errorContent = await channelResponse.Content.ReadAsStringAsync();
                _authorizedUsername = "YouTube User";
                _logger.LogWarning("Failed to get channel info: {Status} - {Response}", channelResponse.StatusCode, errorContent);
            }

            ChannelName = _authorizedUsername ?? "YouTube User";
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
            var platform = await _dataService.GetPlatformByNameAsync("YouTube");

            if (platform == null)
            {
                platform = new Platform
                {
                    Name = "YouTube",
                    Status = "Connected",
                    Enabled = true,
                    CreatedAt = DateTime.UtcNow
                };
            }

            platform.AccessToken = _accessToken ?? "";
            platform.RefreshToken = _refreshToken ?? "";
            platform.Username = _authorizedUsername;
            platform.ClientId = _savedClientId;
            platform.ClientSecret = _savedClientSecret;
            platform.ChannelId = _savedProjectId;
            platform.TokenExpiresAt = DateTime.UtcNow.AddHours(1);
            platform.Status = "Connected";
            platform.ConnectedAt = DateTime.UtcNow;
            platform.UpdatedAt = DateTime.UtcNow;

            await _dataService.SavePlatformAsync(platform);

            _logger.LogInformation("YouTube platform configuration saved to database");

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
            _logger.LogError(ex, "Failed to save YouTube configuration");
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
        _logger.LogInformation("Sending test message via YouTube Live Chat API");

        if (string.IsNullOrEmpty(_accessToken))
        {
            TestMessageStatus = "❌ Please complete Step 3 first (OAuth authorization)";
            return;
        }

        TestMessageStatus = "⏳ Getting live broadcast info...";

        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);

            // Step 1: Get live broadcast to find liveChatId
            var broadcastResponse = await httpClient.GetAsync(
                "https://www.googleapis.com/youtube/v3/liveBroadcasts?part=snippet&mine=true");
            var broadcastContent = await broadcastResponse.Content.ReadAsStringAsync();

            if (!broadcastResponse.IsSuccessStatusCode)
            {
                TestMessageStatus = $"❌ Failed to get broadcasts: {broadcastResponse.StatusCode}";
                _logger.LogError("Failed to get broadcasts: {Content}", broadcastContent);
                return;
            }

            var broadcastJson = JsonDocument.Parse(broadcastContent);
            var broadcastItems = broadcastJson.RootElement.GetProperty("items");

            if (broadcastItems.GetArrayLength() == 0)
            {
                TestMessageStatus = "❌ No active live broadcasts found. Please start a YouTube live stream first.";
                _logger.LogWarning("No live broadcasts found");
                return;
            }

            var liveChatId = broadcastItems[0].GetProperty("snippet").GetProperty("liveChatId").GetString();
            _logger.LogInformation("Found liveChatId: {LiveChatId}", liveChatId);

            TestMessageStatus = "⏳ Sending test message...";

            // Step 2: Send a chat message
            var messageBody = new
            {
                snippet = new
                {
                    liveChatId = liveChatId,
                    type = "textMessageEvent",
                    textMessageDetails = new
                    {
                        messageText = "VyralBot test message - Setup successful! 🎉"
                    }
                }
            };

            var messageContent = new StringContent(
                JsonSerializer.Serialize(messageBody),
                System.Text.Encoding.UTF8,
                "application/json");

            var messageResponse = await httpClient.PostAsync(
                "https://www.googleapis.com/youtube/v3/liveChat/messages?part=snippet",
                messageContent);

            if (messageResponse.IsSuccessStatusCode)
            {
                TestMessageStatus = $"✓ Test message sent to your YouTube live chat! Check your stream.";
                _logger.LogInformation("Test message sent successfully");
            }
            else
            {
                var errorContent = await messageResponse.Content.ReadAsStringAsync();
                TestMessageStatus = $"❌ Failed to send message: {messageResponse.StatusCode}";
                _logger.LogError("Failed to send test message: {Content}", errorContent);
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
        ModalDescription = "Copy your Client ID from the Google Cloud Console and paste it below.";
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
        ModalDescription = "Copy your Client Secret from the Google Cloud Console and paste it below.";
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
        _logger.LogInformation("Resetting YouTube setup wizard (disposeWebView={DisposeWebView})", disposeWebView);

        CurrentStep = 1;
        ShowSuccessScreen = false;
        ShowBrowser = false;
        ShowModal = false;
        ShowManualCallbackInput = false;

        BothCredentialsSaved = false;
        ClientIdSaved = false;
        ClientSecretSaved = false;

        ProjectIdSaved = false;
        ShowCopySearch = false;
        ShowOAuthConsentReload = true;
        ScopesAdded = false;
        UserAdded = false;

        MaskedClientId = "";
        ClientIdInput = "";
        ClientSecretInput = "";
        ChannelName = "";
        CallbackUrlInput = "";

        IsOAuthAuthorized = false;
        OAuthStatus = "Not authorized";
        TestMessageStatus = "Click 'Send Test Message' to verify the connection";

        _savedProjectId = null;
        _savedClientId = null;
        _savedClientSecret = null;
        _accessToken = null;
        _refreshToken = null;
        _authorizedUsername = null;

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
