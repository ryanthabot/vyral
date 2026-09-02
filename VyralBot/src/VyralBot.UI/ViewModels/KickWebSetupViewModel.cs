using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Wpf;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using VyralBot.Data;
using VyralBot.Data.Entities;
using VyralBot.Integrations.Kick;
using VyralBot.UI.Views;

namespace VyralBot.UI.ViewModels;

public partial class KickWebSetupViewModel : ObservableObject
{
    private readonly ILogger<KickWebSetupViewModel> _logger;
    private readonly KickLibOAuthService _oAuthService;
    private readonly IDataService _dataService;
    private readonly IKickService _kickService;

    public WebView2? WebView2 { get; set; }

    public event EventHandler? WebView2InitializeRequested;
    public event EventHandler? WebView2DisposeRequested;
    public event EventHandler? SetupCompleted;

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
    private string _authorizedUsername = "KickUser";

    [ObservableProperty]
    private string _clientId = "";

    [ObservableProperty]
    private string _clientSecret = "";

    private string? _savedClientId;
    private string? _savedClientSecret;
    private string? _accessToken;
    private string? _refreshToken;
    private string? _currentVerifier;
    private Action<string>? _modalSaveAction;

    public KickWebSetupViewModel(
        ILogger<KickWebSetupViewModel> logger,
        KickLibOAuthService oAuthService,
        IDataService dataService,
        IKickService kickService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _oAuthService = oAuthService ?? throw new ArgumentNullException(nameof(oAuthService));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _kickService = kickService ?? throw new ArgumentNullException(nameof(kickService));
    }

    [RelayCommand]
    private void GoToStep1()
    {
        CurrentStep = 1;
        ShowSuccessScreen = false;
    }

    [RelayCommand]
    private void GoToStep2()
    {
        CurrentStep = 2;
        ShowSuccessScreen = false;
    }

    [RelayCommand]
    private void GoToStep3()
    {
        CurrentStep = 3;
        ShowSuccessScreen = false;
    }

    [RelayCommand]
    private void GoToStep4()
    {
        CurrentStep = 4;
        ShowSuccessScreen = false;
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
    private void FinishSetup()
    {
        _logger.LogInformation("Finishing setup and transitioning to connected view");

        // Dispose WebView2 to free resources (will be re-initialized on next setup)
        DisposeWebView2();

        SetupCompleted?.Invoke(this, EventArgs.Empty);
        ResetWizard();
    }

    [RelayCommand]
    private void SaveClientId()
    {
        _logger.LogInformation("SaveClientId button clicked");
        ModalTitle = "Save Client ID";
        ModalDescription = "Copy your Client ID from Kick Developer Settings and paste it below.";
        ModalInput = "";
        _modalSaveAction = (value) =>
        {
            ClientId = value;
            _savedClientId = value;
            MaskedClientId = value.Substring(0, Math.Min(10, value.Length)) + "...";
            ClientIdSaved = true;
            _logger.LogInformation("Client ID saved to private field: {ClientId}", value.Substring(0, Math.Min(10, value.Length)) + "...");
        };
        ShowModal = true;
    }

    [RelayCommand]
    private void SaveClientSecret()
    {
        _logger.LogInformation("SaveClientSecret button clicked");
        ModalTitle = "Save Client Secret";
        ModalDescription = "Copy your Client Secret from Kick Developer Settings and paste it below.";
        ModalInput = "";
        _modalSaveAction = (value) =>
        {
            ClientSecret = value;
            _savedClientSecret = value;
            ClientSecretSaved = true;
            _logger.LogInformation("Client Secret saved to private field");
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
        ShowModal = false;
        ModalInput = "";
        _modalSaveAction = null;
    }

    [RelayCommand]
    private void SaveModal()
    {
        if (!string.IsNullOrWhiteSpace(ModalInput) && _modalSaveAction != null)
        {
            _modalSaveAction(ModalInput);
            ShowModal = false;
            ModalInput = "";
            _modalSaveAction = null;
        }
    }

    [RelayCommand]
    private void ContinueFromStep1()
    {
        ShowBrowser = false;

        // Clear WebView2 to ensure clean state
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

        // Clear WebView2 to ensure clean state
        if (WebView2?.CoreWebView2 != null)
        {
            WebView2.CoreWebView2.Navigate("about:blank");
        }

        // Save Client ID and Client Secret to database
        if (!string.IsNullOrEmpty(_savedClientId) && !string.IsNullOrEmpty(_savedClientSecret))
        {
            _logger.LogInformation("💾 Saving Client ID and Client Secret to database");
            var platform = await _dataService.GetPlatformByNameAsync("Kick");
            if (platform != null)
            {
                platform.ClientId = _savedClientId;
                platform.ClientSecret = _savedClientSecret; // TODO: Should be encrypted
                await _dataService.SavePlatformAsync(platform);
                _logger.LogInformation("✅ Client credentials saved to database");
            }
            else
            {
                _logger.LogWarning("⚠️ Kick platform not found in database, creating new entry");
                platform = new Platform
                {
                    Name = "Kick",
                    ClientId = _savedClientId,
                    ClientSecret = _savedClientSecret,
                    Status = "Configuring"
                };
                await _dataService.SavePlatformAsync(platform);
                _logger.LogInformation("✅ Created Kick platform with client credentials");
            }
        }
        else
        {
            _logger.LogWarning("⚠️ Client ID or Client Secret not provided");
        }

        // Move to Step 3 after credentials are saved
        CurrentStep = 3;

        OAuthStatus = "Ready to authorize. Click the button below.";
    }

    [RelayCommand]
    private async Task ContinueFromStep3Async()
    {
        try
        {
            _logger.LogInformation("Continue from Step 3 - fetching channel ID and chatroom ID...");
            OAuthStatus = "Fetching channel information...";

            // Fetch channel ID and chatroom ID before proceeding
            var success = await FetchAndSaveChannelIdsAsync();

            if (success)
            {
                _logger.LogInformation("✅ Channel IDs fetched successfully");
                OAuthStatus = "✅ Authorization successful!";
                CurrentStep = 4;
                ShowSuccessScreen = false;
            }
            else
            {
                _logger.LogError("❌ Failed to fetch channel IDs");
                OAuthStatus = "❌ Failed to fetch channel information. Please try again.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during ContinueFromStep3");
            OAuthStatus = $"❌ Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task AuthorizeOAuthAsync()
    {
        if (string.IsNullOrEmpty(ClientId) || string.IsNullOrEmpty(ClientSecret))
        {
            OAuthStatus = "⚠️ Please enter Client ID and Client Secret";
            return;
        }

        try
        {
            OAuthStatus = "Generating authorization URL...";

            var redirectUri = "http://localhost:3000/callback";

            // Use KickLib's OAuth generator
            var authUrl = _oAuthService.GenerateAuthorizationUrl(ClientId, redirectUri, out var verifier);

            // Store verifier for later use
            _currentVerifier = verifier;

            OAuthStatus = "Opening authorization page...";

            // Request WebView2 initialization
            WebView2InitializeRequested?.Invoke(this, EventArgs.Empty);

            // Wait a moment for initialization
            await Task.Delay(500);

            if (WebView2?.CoreWebView2 != null)
            {
                // Subscribe to navigation events to capture OAuth callback
                WebView2.CoreWebView2.NavigationStarting -= OnOAuthNavigationStarting;
                WebView2.CoreWebView2.NavigationStarting += OnOAuthNavigationStarting;

                // Show the WebView2 browser
                ShowBrowser = true;

                // Navigate WebView2 to authorization URL
                WebView2.CoreWebView2.Navigate(authUrl);

                OAuthStatus = "Please authorize VyralBot in the browser...";
            }
            else
            {
                OAuthStatus = "❌ Failed to initialize browser";
                _logger.LogWarning("WebView2 not initialized");
            }
        }
        catch (Exception ex)
        {
            OAuthStatus = $"❌ Error: {ex.Message}";
            _logger.LogError(ex, "Failed to start OAuth flow");
        }
    }

    [RelayCommand]
    private void CancelOAuth()
    {
        ShowBrowser = false;
        OAuthUrl = null;
        OAuthStatus = "Authorization cancelled";

        // Unsubscribe from navigation events and clean up
        if (WebView2?.CoreWebView2 != null)
        {
            WebView2.CoreWebView2.NavigationStarting -= OnOAuthNavigationStarting;
            WebView2.CoreWebView2.Navigate("about:blank");
        }

        // Request WebView2 disposal to free memory
        WebView2DisposeRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void CopyRedirectUri()
    {
        try
        {
            var redirectUri = "http://localhost:3000/callback";
            System.Windows.Clipboard.SetText(redirectUri);
            _logger.LogInformation("✅ Redirect URI copied to clipboard: {RedirectUri}", redirectUri);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy redirect URI");
        }
    }

    [RelayCommand]
    private async Task OpenStep1BrowserAsync()
    {
        try
        {
            // Request WebView2 initialization
            WebView2InitializeRequested?.Invoke(this, EventArgs.Empty);

            // Wait a moment for initialization
            await Task.Delay(500);

            if (WebView2?.CoreWebView2 != null)
            {
                ShowBrowser = true;
                WebView2.CoreWebView2.Navigate("https://kick.com/login");
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
        try
        {
            // Request WebView2 initialization if not already done
            WebView2InitializeRequested?.Invoke(this, EventArgs.Empty);

            // Wait a moment for initialization
            await Task.Delay(500);

            if (WebView2?.CoreWebView2 != null)
            {
                ShowBrowser = true;
                WebView2.CoreWebView2.Navigate("https://kick.com/settings/developer");

                // Set focus to WebView2 so user can type
                _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    WebView2?.Focus();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate WebView2");
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
                await HandleOAuthCallbackAsync(e.Uri);
            });
        }
    }

    private async Task HandleOAuthCallbackAsync(string url)
    {
        _logger.LogInformation("OAuth callback received: {Url}", url);

        try
        {
            var uri = new Uri(url);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var code = query["code"];
            var state = query["state"];

            if (string.IsNullOrEmpty(code))
            {
                OAuthStatus = "❌ Authorization failed: No code received";
                return;
            }

            // Extract verifier from state parameter (KickLib encodes it in the state)
            string? verifier = _currentVerifier;
            if (!string.IsNullOrEmpty(state))
            {
                try
                {
                    var stateJson = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(System.Web.HttpUtility.UrlDecode(state)));
                    var stateData = System.Text.Json.JsonDocument.Parse(stateJson);
                    if (stateData.RootElement.TryGetProperty("CodeVerifier", out var verifierElement))
                    {
                        verifier = verifierElement.GetString();
                        _logger.LogInformation("Extracted code verifier from state parameter");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to extract verifier from state, using stored verifier");
                }
            }

            if (string.IsNullOrEmpty(verifier))
            {
                OAuthStatus = "❌ Missing code verifier";
                _logger.LogError("Code verifier is null or empty");
                return;
            }

            OAuthStatus = "Exchanging code for tokens...";
            _logger.LogInformation("Exchanging code with state and verifier for tokens...");

            // Use KickLib's token exchange (requires both state and verifier)
            var success = await _oAuthService.ExchangeCodeForTokenAsync(
                code,
                ClientId,
                ClientSecret,
                "http://localhost:3000/callback",
                state!,
                verifier);

            if (success)
            {
                OAuthStatus = "✅ Authorization successful!";

                // Fetch the username and access token from database after OAuth completes
                var platform = await _dataService.GetPlatformByNameAsync("Kick");
                if (platform != null)
                {
                    if (platform.Username != null)
                    {
                        ChannelName = platform.Username;
                        _logger.LogInformation("Set ChannelName to authorized username: {Username}", platform.Username);
                    }

                    if (platform.AccessToken != null)
                    {
                        _accessToken = platform.AccessToken;
                        _refreshToken = platform.RefreshToken;
                        _logger.LogInformation("Loaded access token for test message");
                    }
                }

                // Clean up WebView2 after successful OAuth
                if (WebView2?.CoreWebView2 != null)
                {
                    WebView2.CoreWebView2.Navigate("about:blank");
                }

                // Stay on step 3 - user must click Continue to fetch channel IDs and proceed to step 4
                IsOAuthAuthorized = true;
                ShowBrowser = false;
            }
            else
            {
                OAuthStatus = "❌ Failed to exchange code for tokens";
            }
        }
        catch (Exception ex)
        {
            OAuthStatus = $"❌ Error: {ex.Message}";
            _logger.LogError(ex, "Failed to handle OAuth callback");
        }
    }

    private async Task<(int userId, string username)?> GetBroadcasterInfoAsync()
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");
            httpClient.DefaultRequestHeaders.Add("Accept", "*/*");

            var response = await httpClient.GetAsync("https://api.kick.com/public/v1/channels");

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to get channels: {StatusCode}, Response: {Response}",
                    response.StatusCode, errorContent);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("Channels API response: {Response}", content);

            var apiResponse = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(content);

            if (apiResponse.TryGetProperty("data", out var data) && data.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var channels = data.EnumerateArray().ToList();
                if (channels.Count > 0)
                {
                    var channel = channels[0];
                    int? userId = null;
                    string? slug = null;

                    if (channel.TryGetProperty("broadcaster_user_id", out var broadcasterUserId))
                    {
                        userId = broadcasterUserId.GetInt32();
                        _logger.LogInformation("Found broadcaster_user_id: {UserId}", userId);
                    }

                    if (channel.TryGetProperty("slug", out var slugProp))
                    {
                        slug = slugProp.GetString();
                        _logger.LogInformation("Found channel slug: {Slug}", slug);
                    }

                    if (userId.HasValue && !string.IsNullOrEmpty(slug))
                    {
                        return (userId.Value, slug);
                    }
                }
            }

            _logger.LogError("Could not find broadcaster_user_id or slug in API response");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching broadcaster info from API");
            return null;
        }
    }

    [RelayCommand]
    private async Task SendTestMessageAsync()
    {
        _logger.LogInformation("Sending test message to Kick channel: {Channel}, HasAccessToken: {HasToken}",
            ChannelName, !string.IsNullOrEmpty(_accessToken));

        if (string.IsNullOrWhiteSpace(ChannelName))
        {
            TestMessageStatus = "❌ Please enter a channel name first";
            return;
        }

        if (string.IsNullOrEmpty(_accessToken))
        {
            TestMessageStatus = "❌ Please complete Step 3 first (OAuth authorization)";
            _logger.LogError("Access token is null or empty - OAuth may have failed");
            return;
        }

        TestMessageStatus = "⏳ Fetching channel information...";

        try
        {
            // Get broadcaster info (user_id and username) from the Kick API
            _logger.LogInformation("Fetching broadcaster info from API");
            var broadcasterInfo = await GetBroadcasterInfoAsync();

            if (!broadcasterInfo.HasValue)
            {
                TestMessageStatus = "❌ Could not retrieve broadcaster information from Kick API";
                _logger.LogError("Failed to get broadcaster info");
                return;
            }

            TestMessageStatus = "⏳ Connecting to Kick...";
            _logger.LogInformation("Attempting to connect with User ID: {UserId}, Username: {Username}",
                broadcasterInfo.Value.userId, broadcasterInfo.Value.username);
            await _kickService.ConnectAsync(broadcasterInfo.Value.username, _accessToken, ChannelName, broadcasterInfo.Value.userId);

            _logger.LogInformation("ConnectAsync returned, IsConnected: {IsConnected}", _kickService.IsConnected);

            if (_kickService.IsConnected)
            {
                TestMessageStatus = "⏳ Sending test message...";
                _logger.LogInformation("Connected successfully, sending test message");
                await Task.Delay(2000); // Wait for connection to stabilize

                await _kickService.SendMessageAsync("VyralBot test message - Setup successful! 🎉");
                TestMessageStatus = $"✓ Test message sent to {ChannelName}! Check your chat.";
                _logger.LogInformation("Test message sent successfully");
            }
            else
            {
                TestMessageStatus = "❌ Failed to connect to Kick. Check your User ID and credentials.";
                _logger.LogError("Connection failed - IsConnected is false after ConnectAsync");
            }
        }
        catch (Exception ex)
        {
            TestMessageStatus = $"❌ Error: {ex.Message}";
            _logger.LogError(ex, "Exception in SendTestMessageAsync: {Message}", ex.Message);
        }
    }

    private async Task<bool> FetchAndSaveChannelIdsAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(ChannelName))
            {
                _logger.LogError("Cannot fetch channel IDs - ChannelName is empty");
                return false;
            }

            _logger.LogInformation("🔍 Fetching channel info for username: {Username}", ChannelName);

            // Initialize WebView2 if not already
            if (WebView2?.CoreWebView2 == null)
            {
                WebView2InitializeRequested?.Invoke(this, EventArgs.Empty);
                await Task.Delay(500); // Wait for initialization
            }

            if (WebView2?.CoreWebView2 == null)
            {
                _logger.LogError("WebView2 not initialized");
                return false;
            }

            // Create a TaskCompletionSource to wait for navigation
            var tcs = new TaskCompletionSource<bool>();
            string? capturedHtml = null;

            // Subscribe to NavigationCompleted event
            void OnNavigationCompleted(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
            {
                if (e.IsSuccess)
                {
                    // Get the page content using a script that extracts from <pre> tag
                    var script = @"
                        (() => {
                            const preElement = document.querySelector('pre');
                            if (preElement) {
                                return preElement.textContent;
                            }
                            return document.body.textContent;
                        })();
                    ";
                    WebView2.CoreWebView2.ExecuteScriptAsync(script)
                        .ContinueWith(task =>
                        {
                            try
                            {
                                var result = task.Result;
                                // ExecuteScriptAsync returns a JSON-encoded string with quotes and escape sequences
                                if (!string.IsNullOrEmpty(result) && result.StartsWith("\"") && result.EndsWith("\""))
                                {
                                    // Remove surrounding quotes
                                    result = result.Substring(1, result.Length - 2);
                                    // Unescape JSON escape sequences (\n, \", \\, etc.)
                                    result = System.Text.RegularExpressions.Regex.Unescape(result);
                                }
                                capturedHtml = result;
                                _logger.LogInformation("📄 Captured page content (length: {Length})", capturedHtml?.Length ?? 0);
                                tcs.TrySetResult(true);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error extracting page content");
                                tcs.TrySetResult(false);
                            }
                        });
                }
                else
                {
                    _logger.LogError("Navigation failed: {Status}", e.WebErrorStatus);
                    tcs.TrySetResult(false);
                }
            }

            WebView2.CoreWebView2.NavigationCompleted += OnNavigationCompleted;

            try
            {
                // Navigate to the channel API endpoint
                var apiUrl = $"https://kick.com/api/v2/channels/{ChannelName}";
                _logger.LogInformation("🌐 Navigating to: {Url}", apiUrl);
                WebView2.CoreWebView2.Navigate(apiUrl);

                // Wait for navigation to complete (with timeout)
                var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(10000));
                if (completedTask != tcs.Task)
                {
                    _logger.LogError("⏱️ Timeout waiting for page to load");
                    return false;
                }

                var success = await tcs.Task;
                if (!success || string.IsNullOrEmpty(capturedHtml))
                {
                    _logger.LogError("Failed to capture page content");
                    return false;
                }

                // Parse the JSON from the page content
                _logger.LogInformation("📝 Parsing JSON response...");
                var jsonDoc = System.Text.Json.JsonDocument.Parse(capturedHtml);
                var root = jsonDoc.RootElement;

                if (!root.TryGetProperty("id", out var channelIdElement) ||
                    !root.TryGetProperty("chatroom", out var chatroomElement) ||
                    !chatroomElement.TryGetProperty("id", out var chatroomIdElement))
                {
                    _logger.LogError("❌ Failed to parse channel ID or chatroom ID from JSON");
                    return false;
                }

                var channelId = channelIdElement.GetInt32();
                var chatroomId = chatroomIdElement.GetInt32();

                _logger.LogInformation("✅ Successfully extracted - Channel ID: {ChannelId}, Chatroom ID: {ChatroomId}",
                    channelId, chatroomId);

                // Save to database
                var platform = await _dataService.GetPlatformByNameAsync("Kick");
                if (platform != null)
                {
                    _logger.LogInformation("📝 Before save - Platform ID: {Id}, ChannelId: {ChannelId}, ChatroomId: {ChatroomId}",
                        platform.Id, platform.ChannelId ?? "null", platform.ChatroomId ?? "null");

                    // Store channel ID and chatroom ID in platform data
                    platform.ChannelId = channelId.ToString();
                    platform.ChatroomId = chatroomId.ToString();

                    _logger.LogInformation("📝 After update - Platform ID: {Id}, ChannelId: {ChannelId}, ChatroomId: {ChatroomId}",
                        platform.Id, platform.ChannelId, platform.ChatroomId);

                    await _dataService.SavePlatformAsync(platform);

                    _logger.LogInformation("💾 Saved channel IDs to database");

                    // Verify the save by reloading
                    var verification = await _dataService.GetPlatformByNameAsync("Kick");
                    _logger.LogInformation("✅ Verification - ChannelId: {ChannelId}, ChatroomId: {ChatroomId}",
                        verification?.ChannelId ?? "null", verification?.ChatroomId ?? "null");
                }
                else
                {
                    _logger.LogError("❌ Kick platform not found in database - cannot save channel IDs");
                }

                // Navigate back to blank page
                WebView2.CoreWebView2.Navigate("about:blank");

                return true;
            }
            finally
            {
                // Cleanup event handler
                WebView2.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error fetching channel IDs");
            return false;
        }
    }

    [RelayCommand]
    private async Task CompleteSetupAsync()
    {
        _logger.LogInformation("User confirmed setup is working, saving to database");

        try
        {
            // Get broadcaster info to save UserId
            var broadcasterInfo = await GetBroadcasterInfoAsync();
            if (!broadcasterInfo.HasValue)
            {
                _logger.LogError("Cannot complete setup - broadcaster info not available");
                TestMessageStatus = "❌ Failed to get broadcaster info";
                return;
            }

            var username = broadcasterInfo.Value.username;
            var userId = broadcasterInfo.Value.userId;

            // Tokens are already saved by ExchangeCodeForTokenAsync

            var platform = await _dataService.GetPlatformByNameAsync("Kick");
            if (platform != null)
            {
                platform.Username = username;
                platform.UserId = userId.ToString();
                platform.Status = "Connected";
                platform.ConnectedAt = DateTime.UtcNow;
                await _dataService.SavePlatformAsync(platform);
            }

            _logger.LogInformation("Kick platform configuration saved to database - Username: {Username}, UserId: {UserId}", username, userId);

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
            _logger.LogError(ex, "Failed to save Kick configuration");
            TestMessageStatus = $"❌ Failed to save: {ex.Message}";
        }
    }

    public void ResetWizard(bool disposeWebView = false)
    {
        _logger.LogInformation("Resetting Kick setup wizard (disposeWebView={DisposeWebView})", disposeWebView);

        CurrentStep = 1;
        ShowSuccessScreen = false;
        ShowBrowser = false;
        ShowModal = false;
        BothCredentialsSaved = false;
        ClientIdSaved = false;
        ClientSecretSaved = false;
        MaskedClientId = "";
        ChannelName = "";
        IsOAuthAuthorized = false;
        OAuthStatus = "Not authorized";
        TestMessageStatus = "Click 'Send Test Message' to verify the connection";
        _savedClientId = null;
        _savedClientSecret = null;
        _accessToken = null;
        _refreshToken = null;
        AuthorizedUsername = "KickUser";

        // Clear WebView2
        if (WebView2?.CoreWebView2 != null)
        {
            try
            {
                // Unsubscribe from navigation events
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
        // Request disposal from View to free resources
        WebView2DisposeRequested?.Invoke(this, EventArgs.Empty);
        WebView2 = null;

        _logger.LogInformation("WebView2 disposal requested");
    }
}
