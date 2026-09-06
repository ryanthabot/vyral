using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Buffers;
using VyralBot.Data;
using VyralBot.Data.Entities;
using VyralBot.Core.Services.Profiles;

namespace VyralBot.Integrations.Twitch;

/// <summary>
/// Handles Twitch OAuth 2.0 authentication flow.
/// </summary>
public class TwitchOAuthService
{
    private readonly ILogger<TwitchOAuthService> _logger;
    private readonly IDataService _dataService;
    private readonly IProfileService _profileService;
    private const string TwitchAuthUrl = "https://id.twitch.tv/oauth2/authorize";
    private const string TwitchTokenUrl = "https://id.twitch.tv/oauth2/token";
    private const string RedirectUri = "http://localhost:3000";
    
    private static string? _currentState; // Store state for WebView2 flow (mutable for WebView flow)
    
    // Credentials loaded from file or environment
    public string? ClientId { get; private set; }
    public string? ClientSecret { get; private set; }

    public TwitchOAuthService(ILogger<TwitchOAuthService> logger, IDataService dataService, IProfileService profileService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
    }

    /// <summary>
    /// Starts OAuth flow by opening browser and listening for callback.
    /// Returns the access/refresh tokens plus the authenticated user's id and login name.
    /// </summary>
    public async Task<(string AccessToken, string RefreshToken, string Username, string? UserId)?> AuthenticateAsync()
    {
        _logger.LogInformation("Starting Twitch OAuth flow");

        // Check for credentials
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
        {
            _logger.LogError("Twitch credentials not configured. Please set VYRALBOT_TWITCH_CLIENT_ID and VYRALBOT_TWITCH_CLIENT_SECRET environment variables.");
            return null;
        }

        // Generate random state for CSRF protection
        var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        // Build authorization URL with comprehensive scopes
        var scopes = new[]
        {
            "analytics:read:extensions",
            "user:edit",
            "user:read:email",
            "clips:edit",
            "clips:create",
            "bits:read",
            "analytics:read:games",
            "user:edit:broadcast",
            "user:read:broadcast",
            "chat:read",
            "chat:edit",
            "channel:moderate",
            "channel:read:subscriptions",
            "whispers:read",
            "whispers:edit",
            "moderation:read",
            "channel:read:redemptions",
            "channel:edit:commercial",
            "channel:read:hype_train",
            "channel:read:stream_key",
            "channel:manage:extensions",
            "channel:manage:broadcast",
            "user:edit:follows",
            "channel:manage:redemptions",
            "channel:read:editors",
            "channel:manage:videos",
            "user:read:blocked_users",
            "user:manage:blocked_users",
            "user:read:subscriptions",
            "user:read:follows",
            "channel:manage:polls",
            "channel:manage:predictions",
            "channel:read:polls",
            "channel:read:predictions",
            "moderator:manage:automod",
            "channel:manage:schedule",
            "channel:read:goals",
            "moderator:read:automod_settings",
            "moderator:manage:automod_settings",
            "moderator:manage:banned_users",
            "moderator:read:blocked_terms",
            "moderator:manage:blocked_terms",
            "moderator:read:chat_settings",
            "moderator:manage:chat_settings",
            "channel:manage:raids",
            "moderator:manage:announcements",
            "moderator:manage:chat",
            "moderator:manage:chat_messages",
            "user:manage:chat_color",
            "channel:manage:moderators",
            "channel:read:vips",
            "channel:manage:vips",
            "user:manage:whispers",
            "channel:read:charity",
            "moderator:read:chatters",
            "moderator:read:shield_mode",
            "moderator:manage:shield_mode",
            "moderator:read:shoutouts",
            "moderator:manage:shoutouts",
            "moderator:read:followers",
            "channel:read:guest_star",
            "channel:manage:guest_star",
            "moderator:read:guest_star",
            "moderator:manage:guest_star",
            "channel:bot",
            "user:bot",
            "user:read:chat",
            "channel:manage:ads",
            "channel:read:ads",
            "user:read:moderated_channels",
            "user:write:chat",
            "user:read:emotes",
            "moderator:read:unban_requests",
            "moderator:manage:unban_requests",
            "moderator:read:suspicious_users",
            "moderator:manage:warnings"
        };

        var authUrl = $"{TwitchAuthUrl}?" +
            $"client_id={ClientId}&" +
            $"redirect_uri={Uri.EscapeDataString(RedirectUri)}&" +
            $"response_type=code&" +
            $"scope={Uri.EscapeDataString(string.Join(" ", scopes))}&" +
            $"state={state}&" +
            $"force_verify=true"; // Force Twitch to show authorization screen every time

        // Open browser
        _logger.LogInformation("Opening browser for OAuth authorization. URL: {AuthUrl}", authUrl);
        
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = authUrl,
                UseShellExecute = true
            };
            using var browserProcess = Process.Start(psi);
            _logger.LogInformation("Browser opened");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start browser process");
            return null;
        }

        // Start local HTTP server to receive callback
        var listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:3000/");
        
        try
        {
            listener.Start();
            _logger.LogInformation("Listening for OAuth callback on http://localhost:3000/");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start HTTP listener on port 3000. Port may be in use.");
            try { listener.Stop(); listener.Close(); } catch { }
            return null;
        }

        // Wait for callback (with timeout)
        var contextTask = listener.GetContextAsync();
        var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5));
        var completedTask = await Task.WhenAny(contextTask, timeoutTask);

        if (completedTask == timeoutTask)
        {
            _logger.LogWarning("OAuth callback timed out after 5 minutes");
            try { listener.Stop(); listener.Close(); } catch { }
            return null;
        }

        var context = await contextTask;
        var request = context.Request;
        var response = context.Response;

        // Extract code and state from query string
        var code = request.QueryString["code"];
        var returnedState = request.QueryString["state"];
        var error = request.QueryString["error"];
        var errorDescription = request.QueryString["error_description"];

        // Check for errors
        if (!string.IsNullOrEmpty(error))
        {
            _logger.LogError("OAuth error: {Error} - {Description}", error, errorDescription);
            
            var errorResponseString = $"<html><body><h1>Authentication failed</h1><p>{error}: {errorDescription}</p></body></html>";
            var errorBuffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(errorResponseString.Length));
            try
            {
                var bytesWritten = Encoding.UTF8.GetBytes(errorResponseString, errorBuffer);
                response.ContentLength64 = bytesWritten;
                await response.OutputStream.WriteAsync(errorBuffer.AsMemory(0, bytesWritten));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(errorBuffer);
            }
            try { response.OutputStream.Close(); } catch { }
            try { listener.Stop(); listener.Close(); } catch { }
            return null;
        }

        // Send response to browser
        var responseString = "<html><body><h1>Authentication successful!</h1><p>You can close this window and return to VyralBot.</p></body></html>";
        var buffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(responseString.Length));
        try
        {
            var bytesWritten = Encoding.UTF8.GetBytes(responseString, buffer);
            response.ContentLength64 = bytesWritten;
            await response.OutputStream.WriteAsync(buffer.AsMemory(0, bytesWritten));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
        try { response.OutputStream.Close(); } catch { }
        try { listener.Stop(); listener.Close(); } catch { }

        // Validate state
        if (returnedState != state)
        {
            _logger.LogError("OAuth state mismatch - possible CSRF attack");
            return null;
        }

        if (string.IsNullOrEmpty(code))
        {
            _logger.LogError("No authorization code received");
            return null;
        }

        // Exchange code for tokens
        _logger.LogInformation("Exchanging authorization code for access token");
        var tokens = await ExchangeCodeForTokensAsync(code);
        
        if (tokens == null)
            return null;
            
        // Get the authenticated user (id + username) from Twitch API
        _logger.LogInformation("Getting authenticated user from Twitch API");
        var user = await GetAuthenticatedUserAsync(tokens.Value.AccessToken);

        return (tokens.Value.AccessToken, tokens.Value.RefreshToken, user?.Username ?? "Unknown", user?.UserId);
    }
    
    /// <summary>
    /// Save credentials to a JSON file.
    /// </summary>
    public async Task SaveCredentialsAsync(string clientId, string clientSecret)
    {
        try
        {
            var profile = await _profileService.GetCurrentProfileAsync();
            var credentialsFolder = _profileService.GetProfileCredentialsPath(profile.Name);
            var credentialsPath = Path.Combine(credentialsFolder, "twitch_credentials.json");
            
            Directory.CreateDirectory(Path.GetDirectoryName(credentialsPath)!);
            
            var credentials = new { ClientId = clientId, ClientSecret = clientSecret };
            var json = System.Text.Json.JsonSerializer.Serialize(credentials, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            
            await File.WriteAllTextAsync(credentialsPath, json);
            _logger.LogInformation("Credentials saved to file: {Path}", credentialsPath);
            
            // Update current instance
            ClientId = clientId;
            ClientSecret = clientSecret;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save credentials to file");
            throw;
        }
    }
    
    /// <summary>
    /// Load credentials from JSON file.
    /// </summary>
    public async Task LoadCredentialsAsync()
    {
        try
        {
            var profile = await _profileService.GetCurrentProfileAsync();
            var credentialsFolder = _profileService.GetProfileCredentialsPath(profile.Name);
            var credentialsPath = Path.Combine(credentialsFolder, "twitch_credentials.json");
            
            _logger.LogInformation("Looking for credentials at: {Path}", credentialsPath);
            
            if (!File.Exists(credentialsPath))
            {
                _logger.LogWarning("Credentials file not found at: {Path}", credentialsPath);
                return;
            }
            
            var json = await File.ReadAllTextAsync(credentialsPath);
            _logger.LogInformation("Credentials file content: {Json}", json);
            
            var credentials = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
            
            ClientId = credentials.GetProperty("ClientId").GetString() ?? string.Empty;
            ClientSecret = credentials.GetProperty("ClientSecret").GetString() ?? string.Empty;
            
            _logger.LogInformation("Credentials loaded - ClientId: {ClientId}, ClientSecret length: {SecretLength}", 
                ClientId.Length > 0 ? ClientId.Substring(0, Math.Min(8, ClientId.Length)) + "..." : "EMPTY",
                ClientSecret.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load credentials from file");
        }
    }
    
    /// <summary>
    /// Builds the OAuth authorization URL for WebView2.
    /// </summary>
    public string BuildAuthorizationUrl()
    {
        // Generate random state for CSRF protection
        _currentState = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var scopes = GetScopes();

        _logger.LogInformation("Building OAuth URL with ClientId: {ClientId} (length: {Length})", 
            ClientId?.Substring(0, Math.Min(8, ClientId?.Length ?? 0)) + "...", 
            ClientId?.Length ?? 0);

        var authUrl = $"{TwitchAuthUrl}?" +
            $"client_id={ClientId}&" +
            $"redirect_uri={Uri.EscapeDataString(RedirectUri)}&" +
            $"response_type=code&" +
            $"scope={Uri.EscapeDataString(string.Join(" ", scopes))}&" +
            $"state={_currentState}&" +
            $"force_verify=true";

        // Log FULL URL for debugging
        _logger.LogInformation("FULL OAuth URL: {Url}", authUrl);
        _logger.LogInformation("ClientId in URL: {ClientId}", ClientId ?? "NULL");

        return authUrl;
    }
    
    /// <summary>
    /// Handles OAuth callback from WebView2 navigation.
    /// Returns the access/refresh tokens plus the authenticated user's id and login name.
    /// </summary>
    public async Task<(string AccessToken, string RefreshToken, string Username, string? UserId)?> HandleOAuthCallbackAsync(string callbackUrl)
    {
        try
        {
            var uri = new Uri(callbackUrl);
            
            // Parse query string manually
            var queryParams = uri.Query.TrimStart('?').Split('&')
                .Select(param => param.Split('='))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0], parts => Uri.UnescapeDataString(parts[1]));
            
            queryParams.TryGetValue("code", out var code);
            queryParams.TryGetValue("state", out var returnedState);
            queryParams.TryGetValue("error", out var error);
            queryParams.TryGetValue("error_description", out var errorDescription);

            // Check for errors
            if (!string.IsNullOrEmpty(error))
            {
                _logger.LogError("OAuth error: {Error} - {Description}", error, errorDescription);
                return null;
            }

            // Validate state
            if (returnedState != _currentState)
            {
                _logger.LogError("OAuth state mismatch - possible CSRF attack");
                return null;
            }

            if (string.IsNullOrEmpty(code))
            {
                _logger.LogError("No authorization code received");
                return null;
            }

            // Exchange code for tokens
            _logger.LogInformation("Exchanging authorization code for access token");
            var tokens = await ExchangeCodeForTokensAsync(code);
            
            if (tokens == null)
                return null;
                
            // Get the authenticated user (id + username) from Twitch API
            _logger.LogInformation("Getting authenticated user from Twitch API");
            var user = await GetAuthenticatedUserAsync(tokens.Value.AccessToken);

            return (tokens.Value.AccessToken, tokens.Value.RefreshToken, user?.Username ?? "Unknown", user?.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling OAuth callback");
            return null;
        }
    }
    
    private static string[] GetScopes()
    {
        return new[]
        {
            "analytics:read:extensions",
            "user:edit",
            "user:read:email",
            "clips:edit",
            "clips:create",
            "bits:read",
            "analytics:read:games",
            "user:edit:broadcast",
            "user:read:broadcast",
            "chat:read",
            "chat:edit",
            "channel:moderate",
            "channel:read:subscriptions",
            "whispers:read",
            "whispers:edit",
            "moderation:read",
            "channel:read:redemptions",
            "channel:edit:commercial",
            "channel:read:hype_train",
            "channel:read:stream_key",
            "channel:manage:extensions",
            "channel:manage:broadcast",
            "user:edit:follows",
            "channel:manage:redemptions",
            "channel:read:editors",
            "channel:manage:videos",
            "user:read:blocked_users",
            "user:manage:blocked_users",
            "user:read:subscriptions",
            "user:read:follows",
            "channel:manage:polls",
            "channel:manage:predictions",
            "channel:read:polls",
            "channel:read:predictions",
            "moderator:manage:automod",
            "channel:manage:schedule",
            "channel:read:goals",
            "moderator:read:automod_settings",
            "moderator:manage:automod_settings",
            "moderator:manage:banned_users",
            "moderator:read:blocked_terms",
            "moderator:manage:blocked_terms",
            "moderator:read:chat_settings",
            "moderator:manage:chat_settings",
            "channel:manage:raids",
            "moderator:manage:announcements",
            "moderator:manage:chat",
            "moderator:manage:chat_messages",
            "user:manage:chat_color",
            "channel:manage:moderators",
            "channel:read:vips",
            "channel:manage:vips",
            "user:manage:whispers",
            "channel:read:charity",
            "moderator:read:chatters",
            "moderator:read:shield_mode",
            "moderator:manage:shield_mode",
            "moderator:read:shoutouts",
            "moderator:manage:shoutouts",
            "moderator:read:followers",
            "channel:read:guest_star",
            "channel:manage:guest_star",
            "moderator:read:guest_star",
            "moderator:manage:guest_star",
            "channel:bot",
            "user:bot",
            "user:read:chat",
            "channel:manage:ads",
            "channel:read:ads",
            "user:read:moderated_channels",
            "user:write:chat",
            "user:read:emotes",
            "moderator:read:unban_requests",
            "moderator:manage:unban_requests",
            "moderator:read:suspicious_users",
            "moderator:manage:warnings"
        };
    }
    
    /// <summary>
    /// Gets the authenticated Twitch user's id and login name from an access token
    /// via the Helix GET /users endpoint. The id is required for EventSub subscriptions
    /// (see TwitchService.ConnectAsync); the login is used as the account username.
    /// </summary>
    private async Task<(string UserId, string Username)?> GetAuthenticatedUserAsync(string accessToken)
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
            client.DefaultRequestHeaders.Add("Client-Id", ClientId);

            var response = await client.GetAsync("https://api.twitch.tv/helix/users");
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to get user info from Twitch API. Status: {Status}, Response: {Response}",
                    response.StatusCode, responseContent);
                return null;
            }

            var userInfo = JsonSerializer.Deserialize<JsonElement>(responseContent);
            if (userInfo.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
            {
                var userId = data[0].TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                var username = data[0].TryGetProperty("login", out var loginProp) ? loginProp.GetString() : null;

                if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(username))
                {
                    _logger.LogInformation("Got authenticated user from Twitch: {Username} (Id: {UserId})", username, userId);
                    return (userId, username);
                }
            }

            _logger.LogError("Unexpected user info payload from Twitch API - no user found");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting authenticated user from token");
            return null;
        }
    }

    /// <summary>
    /// Exchanges authorization code for access and refresh tokens.
    /// </summary>
    private async Task<(string AccessToken, string RefreshToken)?> ExchangeCodeForTokensAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
        {
            _logger.LogError("Twitch credentials not configured");
            return null;
        }

        using var client = new HttpClient();

        var requestBody = new Dictionary<string, string>
        {
            ["client_id"] = ClientId!,
            ["client_secret"] = ClientSecret!,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = RedirectUri
        };

        var response = await client.PostAsync(TwitchTokenUrl, new FormUrlEncodedContent(requestBody));
        var responseContent = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to exchange code for tokens: {Response}", responseContent);
            return null;
        }

        var tokenResponse = JsonSerializer.Deserialize<TwitchTokenResponse>(responseContent);
        if (tokenResponse == null)
        {
            _logger.LogError("Failed to parse token response");
            return null;
        }

        _logger.LogInformation("Successfully obtained access token (expires in {ExpiresIn}s)",
            tokenResponse.ExpiresIn);

        return (tokenResponse.AccessToken, tokenResponse.RefreshToken);
    }

    /// <summary>
    /// Refreshes an expired access token using refresh token.
    /// </summary>
    public async Task<string?> RefreshTokenAsync(string clientId, string clientSecret, string refreshToken)
    {
        _logger.LogInformation("Refreshing Twitch access token");

        using var client = new HttpClient();

        var requestBody = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        };

        var response = await client.PostAsync(TwitchTokenUrl, new FormUrlEncodedContent(requestBody));
        var responseContent = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to refresh token: {Response}", responseContent);
            return null;
        }

        var tokenResponse = JsonSerializer.Deserialize<TwitchTokenResponse>(responseContent);
        if (tokenResponse == null)
        {
            _logger.LogError("Failed to parse refresh token response");
            return null;
        }

        _logger.LogInformation("Successfully refreshed access token");
        return tokenResponse.AccessToken;
    }

    /// <summary>
    /// Refreshes access token using stored refresh token and returns both new tokens.
    /// </summary>
    public async Task<(string AccessToken, string RefreshToken)?> RefreshAccessTokenAsync(string refreshToken)
    {
        if (string.IsNullOrEmpty(refreshToken))
        {
            _logger.LogWarning("Cannot refresh token - refresh token is empty");
            return null;
        }

        if (string.IsNullOrEmpty(ClientId) || string.IsNullOrEmpty(ClientSecret))
        {
            _logger.LogWarning("Cannot refresh token - client credentials not configured");
            return null;
        }

        _logger.LogInformation("Refreshing Twitch access token");

        using var client = new HttpClient();

        var requestBody = new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        };

        var response = await client.PostAsync(TwitchTokenUrl, new FormUrlEncodedContent(requestBody));
        var responseContent = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to refresh token: {Response}", responseContent);
            return null;
        }

        var tokenResponse = JsonSerializer.Deserialize<TwitchTokenResponse>(responseContent);
        if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
        {
            _logger.LogError("Failed to parse refresh token response");
            return null;
        }

        _logger.LogInformation("Successfully refreshed Twitch access token");
        return (tokenResponse.AccessToken, tokenResponse.RefreshToken);
    }

    /// <summary>
    /// Saves access and refresh tokens to the database.
    /// When userId is provided (post-authorization flows), it is persisted so that
    /// EventSub can connect (TwitchService requires a non-empty Platform.UserId).
    /// </summary>
    public async Task SaveTokensAsync(string username, string accessToken, string refreshToken, string? userId = null)
    {
        try
        {
            await _dataService.EnsureDatabaseCreatedAsync();
            var platform = await _dataService.GetPlatformByNameAsync("Twitch");

            if (platform == null)
            {
                platform = new Platform
                {
                    Name = "Twitch",
                    Username = username,
                    AccessToken = Encrypt(accessToken),
                    RefreshToken = Encrypt(refreshToken),
                    UserId = userId,
                    Status = "Connected"
                };
                await _dataService.SavePlatformAsync(platform);
            }
            else
            {
                platform.AccessToken = Encrypt(accessToken);
                platform.RefreshToken = Encrypt(refreshToken);
                if (!string.IsNullOrEmpty(userId))
                {
                    platform.UserId = userId;
                }
                await _dataService.SavePlatformAsync(platform);
            }

            _logger.LogInformation("Saved refreshed Twitch tokens to database");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving Twitch tokens");
            throw;
        }
    }

    /// <summary>
    /// Encrypts sensitive data using Windows DPAPI.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encryptedBytes);
    }

    /// <summary>
    /// Decrypts sensitive data using Windows DPAPI.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static string Decrypt(string encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText)) return string.Empty;

        var encryptedBytes = Convert.FromBase64String(encryptedText);
        var plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plainBytes);
    }

    private class TwitchTokenResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;
        
        [System.Text.Json.Serialization.JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = string.Empty;
        
        [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("scope")]
        public string[] Scope { get; set; } = Array.Empty<string>();
        
        [System.Text.Json.Serialization.JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty;
    }
}
