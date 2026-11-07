using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using VyralBot.Core.Models;
using VyralBot.Core.Services.Chat;
using VyralBot.Core.Services.EventBus;
using VyralBot.Data;
using VyralBot.Data.Entities;
using VyralBot.Integrations.Twitch.Events;

namespace VyralBot.Integrations.Twitch;

/// <summary>
/// Twitch IRC client service using lightweight IRC implementation.
/// </summary>
public class TwitchService : ITwitchService, IDisposable
{
    private readonly ILogger<TwitchService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IEventBus _eventBus;
    private readonly TwitchOAuthService _oAuthService;
    private readonly IChatMessageStore? _chatMessageStore;
    private readonly IDataService? _dataService;
    private readonly ITwitchEventSubService? _eventSubService;
    private LightweightTwitchIrc? _client;
    private string? _channel;
    private string? _botUsername;
    private string? _accessToken;
    private string? _refreshToken;
    private bool _disposed;
    private bool _intentionalDisconnect;
    private CancellationTokenSource? _reconnectCts;
    private Platform? _platform;

    public bool IsConnected => _client?.IsConnected ?? false;
    public string? Channel => _channel;
    public string? BotUsername => _botUsername;

    public TwitchService(
        ILogger<TwitchService> logger,
        ILoggerFactory loggerFactory,
        IEventBus eventBus,
        TwitchOAuthService oAuthService,
        IChatMessageStore? chatMessageStore = null,
        IDataService? dataService = null,
        ITwitchEventSubService? eventSubService = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _oAuthService = oAuthService ?? throw new ArgumentNullException(nameof(oAuthService));
        _chatMessageStore = chatMessageStore;
        _dataService = dataService;
        _eventSubService = eventSubService;
    }

    /// <summary>
    /// Connects to Twitch IRC chat.
    /// </summary>
    public async Task ConnectAsync(string username, string accessToken, string channel)
    {
        await ConnectAsync(username, accessToken, null, channel);
    }

    /// <summary>
    /// Connects to Twitch IRC chat with optional refresh token.
    /// </summary>
    public async Task ConnectAsync(string username, string accessToken, string? refreshToken, string channel)
    {
        if (string.IsNullOrEmpty(username)) throw new ArgumentNullException(nameof(username));
        if (string.IsNullOrEmpty(accessToken)) throw new ArgumentNullException(nameof(accessToken));
        if (string.IsNullOrEmpty(channel)) throw new ArgumentNullException(nameof(channel));

        // Early-return when already connected to the same channel so repeated
        // ConnectAsync calls can't spawn duplicate IRC clients (duplicated messages).
        var normalizedChannel = channel.TrimStart('#').ToLowerInvariant();
        if (IsConnected && !string.IsNullOrEmpty(_channel) &&
            string.Equals(_channel.TrimStart('#').ToLowerInvariant(), normalizedChannel, StringComparison.Ordinal))
        {
            _logger.LogWarning("Already connected to Twitch IRC #{Channel} - skipping duplicate connect", channel);
            return;
        }

        _logger.LogInformation("Connecting to Twitch as {Username} in #{Channel}", username, channel);

        // Load platform settings for filter checks
        if (_dataService != null)
        {
            try
            {
                _platform = await _dataService.GetPlatformByNameAsync("Twitch");
                _logger.LogInformation("Loaded Twitch platform settings: TwitchChatEnabled={Enabled}", _platform?.TwitchChatEnabled);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load Twitch platform settings");
            }
        }

        // Store credentials for reconnection
        _botUsername = username;
        _accessToken = accessToken;
        _refreshToken = refreshToken;
        _channel = channel;
        _intentionalDisconnect = false;

        // Tear down any previous IRC client before creating a new one so we never
        // leak a connected socket (the old client's events kept firing duplicated
        // messages alongside the new one).
        if (_client != null)
        {
            _logger.LogInformation("Disconnecting previous Twitch IRC client before reconnecting");
            _intentionalDisconnect = true; // prevent the old client's OnDisconnected from triggering auto-reconnect
            _client.Connected -= OnConnected;
            _client.MessageReceived -= OnMessageReceived;
            _client.Disconnected -= OnDisconnected;
            _client.AuthenticationFailed -= OnAuthenticationFailed;
            try
            {
                _client.Disconnect();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disconnecting previous Twitch IRC client");
            }
            _client.Dispose();
            _client = null;
            _intentionalDisconnect = false;
        }

        // Create lightweight IRC client
        var ircLogger = _loggerFactory.CreateLogger<LightweightTwitchIrc>();
        _client = new LightweightTwitchIrc(ircLogger);

        // Wire up events
        _client.Connected += OnConnected;
        _client.MessageReceived += OnMessageReceived;
        _client.Disconnected += OnDisconnected;
        _client.AuthenticationFailed += OnAuthenticationFailed;

        // Connect (non-blocking)
        await _client.ConnectAsync(username, accessToken, channel);

        _logger.LogInformation("Successfully connected to Twitch IRC #{Channel}", channel);

        // Connect EventSub WebSocket if available
        if (_eventSubService != null && _dataService != null)
        {
            try
            {
                var twitchPlatform = await _dataService.GetPlatformByNameAsync("Twitch");
                if (twitchPlatform != null && !string.IsNullOrEmpty(twitchPlatform.UserId) && !string.IsNullOrEmpty(twitchPlatform.ClientId))
                {
                    _logger.LogInformation("Connecting Twitch EventSub for user {UserId}", twitchPlatform.UserId);
                    await _eventSubService.ConnectAsync(twitchPlatform.ClientId, accessToken, twitchPlatform.UserId);
                    _logger.LogInformation("Twitch EventSub connected successfully");
                }
                else
                {
                    _logger.LogWarning("Cannot connect EventSub - missing UserId or ClientId in Platform settings");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to connect Twitch EventSub - events will not be available");
            }
        }
    }

    /// <summary>
    /// Disconnects from Twitch chat.
    /// </summary>
    public async Task DisconnectAsync()
    {
        _intentionalDisconnect = true;
        _reconnectCts?.Cancel();

        if (_eventSubService != null)
        {
            try
            {
                await _eventSubService.DisconnectAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disconnecting EventSub");
            }
        }

        if (_client != null)
        {
            _logger.LogInformation("Disconnecting from Twitch");
            _client.Disconnect();
        }
    }

    /// <summary>
    /// Sends a chat message.
    /// </summary>
    public async Task SendMessageAsync(string message)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("Not connected to Twitch");
        }

        if (string.IsNullOrEmpty(_channel))
        {
            throw new InvalidOperationException("No channel connected");
        }

        _logger.LogDebug("Sending message to #{Channel}: {Message}", _channel, message);
        await _client!.SendMessageAsync(message);
    }

    #region Stream Management (Helix API)

    private const string HelixApiBase = "https://api.twitch.tv/helix";

    /// <summary>
    /// Single shared HTTP client (app rule: no per-call HttpClient instances —
    /// they exhaust sockets). Auth headers go on each request message because
    /// DefaultRequestHeaders on a shared instance would stack values across calls.
    /// </summary>
    private static readonly HttpClient s_httpClient = new();

    /// <summary>Raw Helix API response; Success is true for 2xx status codes.</summary>
    private sealed record HelixResponse(bool Success, System.Net.HttpStatusCode StatusCode, string Body);

    /// <summary>
    /// Sends an authenticated Helix request with Bearer + Client-Id headers.
    /// On 401 the stored access token is refreshed once (when a refresh token
    /// exists) and the request is retried. Never throws for HTTP failures —
    /// callers inspect <see cref="HelixResponse"/>.
    /// </summary>
    private async Task<HelixResponse> SendHelixAsync(HttpMethod method, string pathWithQuery, string? jsonBody, bool allowTokenRefresh = true)
    {
        if (string.IsNullOrEmpty(_accessToken))
        {
            return new HelixResponse(false, System.Net.HttpStatusCode.Unauthorized, "Not connected to Twitch");
        }

        var platform = await GetTwitchPlatformAsync();
        var clientId = !string.IsNullOrEmpty(platform?.ClientId) ? platform.ClientId : _oAuthService.ClientId;
        if (string.IsNullOrEmpty(clientId))
        {
            _logger.LogWarning("Twitch Helix call skipped - no Client-Id configured");
            return new HelixResponse(false, System.Net.HttpStatusCode.BadRequest,
                "Twitch client credentials are missing - re-complete the Twitch setup in Platforms.");
        }

        try
        {
            using var request = new HttpRequestMessage(method, $"{HelixApiBase}/{pathWithQuery.TrimStart('/')}");
            request.Headers.Add("Authorization", $"Bearer {_accessToken}");
            request.Headers.Add("Client-Id", clientId);
            if (jsonBody != null)
            {
                request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            }

            using var response = await s_httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                return new HelixResponse(true, response.StatusCode, body);
            }

            // 401 → the stored access token likely expired; refresh once and retry.
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && allowTokenRefresh && await TryRefreshAccessTokenAsync())
            {
                return await SendHelixAsync(method, pathWithQuery, jsonBody, allowTokenRefresh: false);
            }

            _logger.LogWarning("Twitch Helix {Method} {Path} failed: {Status} - {Body}",
                method.Method, pathWithQuery, response.StatusCode, body);
            return new HelixResponse(false, response.StatusCode, body);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Twitch Helix {Method} {Path} threw", method.Method, pathWithQuery);
            return new HelixResponse(false, System.Net.HttpStatusCode.InternalServerError, ex.Message);
        }
    }

    /// <summary>
    /// Refreshes the stored Twitch access token (used on Helix 401 responses).
    /// Returns true when new tokens are available for a retry.
    /// </summary>
    private async Task<bool> TryRefreshAccessTokenAsync()
    {
        if (string.IsNullOrEmpty(_refreshToken))
        {
            _logger.LogWarning("Cannot refresh Twitch token - no refresh token available");
            return false;
        }

        try
        {
            try
            {
                await _oAuthService.LoadCredentialsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load Twitch client credentials before refresh; proceeding anyway");
            }

            var result = await _oAuthService.RefreshAccessTokenAsync(_refreshToken);
            if (!result.HasValue)
            {
                _logger.LogError("Failed to refresh Twitch access token");
                return false;
            }

            _accessToken = result.Value.AccessToken;
            _refreshToken = result.Value.RefreshToken;

            try
            {
                if (!string.IsNullOrEmpty(_botUsername))
                {
                    await _oAuthService.SaveTokensAsync(_botUsername, _accessToken, _refreshToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist refreshed Twitch tokens");
            }

            _logger.LogInformation("Refreshed Twitch access token for Helix API calls");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing Twitch access token");
            return false;
        }
    }

    /// <summary>Loads the Twitch Platform row (UserId / ClientId live here).</summary>
    private async Task<Platform?> GetTwitchPlatformAsync()
    {
        if (_dataService == null)
        {
            return null;
        }

        try
        {
            return await _dataService.GetPlatformByNameAsync("Twitch");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load the Twitch platform row");
            return null;
        }
    }

    /// <summary>
    /// Resolves the broadcaster user id used for Helix stream-management calls:
    /// the Platform row's UserId when present, otherwise the token's own user
    /// via GET /helix/users.
    /// </summary>
    private async Task<string?> ResolveBroadcasterIdAsync()
    {
        var platform = await GetTwitchPlatformAsync();
        if (!string.IsNullOrEmpty(platform?.UserId))
        {
            return platform.UserId;
        }

        var response = await SendHelixAsync(HttpMethod.Get, "users", null);
        if (!response.Success)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(response.Body);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.GetArrayLength() > 0 &&
                data[0].TryGetProperty("id", out var id))
            {
                return id.GetString();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse the Twitch user lookup response");
        }

        return null;
    }

    /// <summary>Resolves a category name to its game id via GET /helix/search/categories.</summary>
    private async Task<string?> ResolveCategoryIdAsync(string categoryName)
    {
        var response = await SendHelixAsync(HttpMethod.Get,
            $"search/categories?query={Uri.EscapeDataString(categoryName)}&first=1", null);
        if (!response.Success)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(response.Body);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.GetArrayLength() > 0 &&
                data[0].TryGetProperty("id", out var id))
            {
                return id.GetString();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse the Twitch category search response");
        }

        return null;
    }

    /// <summary>Resolves a login name to its Twitch user id via GET /helix/users?login=….</summary>
    private async Task<string?> ResolveUserIdByLoginAsync(string login)
    {
        var lookup = await SendHelixAsync(HttpMethod.Get,
            $"users?login={Uri.EscapeDataString(login.Trim().TrimStart('@'))}", null);
        if (!lookup.Success)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(lookup.Body);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.GetArrayLength() > 0 &&
                data[0].TryGetProperty("id", out var id))
            {
                return id.GetString();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse the Twitch user lookup response");
        }

        return null;
    }

    /// <summary>Extracts Twitch's human-readable error message from a Helix error body.</summary>
    private static string? TryExtractTwitchErrorMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
            {
                var text = message.GetString();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
        }
        catch
        {
            // Not JSON (e.g. empty body on 204) - fall through.
        }

        return null;
    }

    /// <summary>Maps a failed Helix response to a user-presentable message.</summary>
    private static string MapHelixFailure(HelixResponse response)
    {
        return response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized =>
                "Twitch rejected this action (401). Re-complete the Twitch setup in Platforms to grant the new moderation permissions.",
            System.Net.HttpStatusCode.Forbidden =>
                "Twitch rejected this action (403). Re-complete the Twitch setup in Platforms to grant the new moderation permissions.",
            _ => TryExtractTwitchErrorMessage(response.Body) is { } detail
                ? $"Twitch rejected this action: {detail}"
                : $"Twitch rejected this action ({(int)response.StatusCode})."
        };
    }

    /// <inheritdoc />
    public async Task<TwitchActionResponse> EditChannelInfoAsync(string? title, string? category)
    {
        var hasTitle = !string.IsNullOrWhiteSpace(title);
        var hasCategory = !string.IsNullOrWhiteSpace(category);
        if (!hasTitle && !hasCategory)
        {
            return new TwitchActionResponse(false, "Nothing to update - enter a title or category first.");
        }

        var broadcasterId = await ResolveBroadcasterIdAsync();
        if (string.IsNullOrEmpty(broadcasterId))
        {
            return new TwitchActionResponse(false,
                "Could not resolve the Twitch broadcaster id - re-complete the Twitch setup in Platforms.");
        }

        var body = new Dictionary<string, object>();
        if (hasTitle)
        {
            body["title"] = title!.Trim();
        }

        if (hasCategory)
        {
            var gameId = await ResolveCategoryIdAsync(category!.Trim());
            if (string.IsNullOrEmpty(gameId))
            {
                return new TwitchActionResponse(false, $"Category \"{category.Trim()}\" was not found on Twitch.");
            }

            body["game_id"] = gameId;
        }

        var response = await SendHelixAsync(HttpMethod.Patch,
            $"channels?broadcaster_id={broadcasterId}", JsonSerializer.Serialize(body));
        if (!response.Success)
        {
            return new TwitchActionResponse(false, MapHelixFailure(response));
        }

        _logger.LogInformation("Updated Twitch channel info (title set: {TitleSet}, category set: {CategorySet})",
            hasTitle, hasCategory);
        return new TwitchActionResponse(true,
            hasTitle && hasCategory
                ? $"Channel updated - title \"{title!.Trim()}\", category \"{category!.Trim()}\"."
                : hasTitle
                    ? $"Channel title updated to \"{title!.Trim()}\"."
                    : $"Channel category updated to \"{category!.Trim()}\".");
    }

    /// <inheritdoc />
    public async Task<TwitchActionResponse> CreateStreamMarkerAsync(string? description)
    {
        var broadcasterId = await ResolveBroadcasterIdAsync();
        if (string.IsNullOrEmpty(broadcasterId))
        {
            return new TwitchActionResponse(false,
                "Could not resolve the Twitch broadcaster id - re-complete the Twitch setup in Platforms.");
        }

        var payload = new Dictionary<string, object> { ["user_id"] = broadcasterId };
        if (!string.IsNullOrWhiteSpace(description))
        {
            payload["description"] = description.Trim();
        }

        var response = await SendHelixAsync(HttpMethod.Post, "streams/markers", JsonSerializer.Serialize(payload));
        if (!response.Success)
        {
            var detail = TryExtractTwitchErrorMessage(response.Body) ?? string.Empty;
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound ||
                detail.Contains("no active", StringComparison.OrdinalIgnoreCase) ||
                detail.Contains("not live", StringComparison.OrdinalIgnoreCase))
            {
                return new TwitchActionResponse(false, "Cannot create a marker - the channel is not currently live.");
            }

            return new TwitchActionResponse(false, MapHelixFailure(response));
        }

        return string.IsNullOrWhiteSpace(description)
            ? new TwitchActionResponse(true, "Stream marker created.")
            : new TwitchActionResponse(true, $"Stream marker \"{description.Trim()}\" created.");
    }

    /// <inheritdoc />
    public async Task<TwitchActionResponse> CreateClipAsync()
    {
        var broadcasterId = await ResolveBroadcasterIdAsync();
        if (string.IsNullOrEmpty(broadcasterId))
        {
            return new TwitchActionResponse(false,
                "Could not resolve the Twitch broadcaster id - re-complete the Twitch setup in Platforms.");
        }

        var response = await SendHelixAsync(HttpMethod.Post,
            $"clips?broadcaster_id={broadcasterId}&has_delayed=false", null);
        if (!response.Success)
        {
            return new TwitchActionResponse(false, MapHelixFailure(response));
        }

        try
        {
            using var doc = JsonDocument.Parse(response.Body);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
            {
                var clip = data[0];
                var url = clip.TryGetProperty("edit_url", out var editUrl) ? editUrl.GetString() : null;
                if (string.IsNullOrEmpty(url) && clip.TryGetProperty("url", out var urlProp))
                {
                    url = urlProp.GetString();
                }

                if (!string.IsNullOrEmpty(url))
                {
                    return new TwitchActionResponse(true, $"Clip created: {url}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse the Twitch clip creation response");
        }

        return new TwitchActionResponse(true, "Clip created.");
    }

    /// <inheritdoc />
    public async Task<TwitchActionResponse> RaidChannelAsync(string targetChannel)
    {
        if (string.IsNullOrWhiteSpace(targetChannel))
        {
            return new TwitchActionResponse(false, "Enter a channel name to raid.");
        }

        var target = targetChannel.Trim().TrimStart('#');

        var broadcasterId = await ResolveBroadcasterIdAsync();
        if (string.IsNullOrEmpty(broadcasterId))
        {
            return new TwitchActionResponse(false,
                "Could not resolve the Twitch broadcaster id - re-complete the Twitch setup in Platforms.");
        }

        var lookup = await SendHelixAsync(HttpMethod.Get, $"users?login={Uri.EscapeDataString(target)}", null);
        string? targetId = null;
        if (lookup.Success)
        {
            try
            {
                using var doc = JsonDocument.Parse(lookup.Body);
                if (doc.RootElement.TryGetProperty("data", out var data) && data.GetArrayLength() > 0 &&
                    data[0].TryGetProperty("id", out var id))
                {
                    targetId = id.GetString();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse the Twitch raid target lookup response");
            }
        }

        if (string.IsNullOrEmpty(targetId))
        {
            return new TwitchActionResponse(false, $"Twitch channel \"{target}\" was not found.");
        }

        var raid = await SendHelixAsync(HttpMethod.Post,
            $"raids?from_broadcaster_id={broadcasterId}&to_broadcaster_id={targetId}", null);
        if (!raid.Success)
        {
            return new TwitchActionResponse(false, MapHelixFailure(raid));
        }

        _logger.LogInformation("Started Twitch raid to {Target}", target);
        return new TwitchActionResponse(true, $"Raid started - heading over to {target}!");
    }

    /// <inheritdoc />
    public async Task<TwitchActionResponse> StopRaidAsync()
    {
        var broadcasterId = await ResolveBroadcasterIdAsync();
        if (string.IsNullOrEmpty(broadcasterId))
        {
            return new TwitchActionResponse(false,
                "Could not resolve the Twitch broadcaster id - re-complete the Twitch setup in Platforms.");
        }

        var response = await SendHelixAsync(HttpMethod.Delete, $"raids?broadcaster_id={broadcasterId}", null);
        if (!response.Success)
        {
            return new TwitchActionResponse(false, MapHelixFailure(response));
        }

        return new TwitchActionResponse(true, "Outgoing raid cancelled.");
    }

    /// <inheritdoc />
    public async Task<TwitchActionResponse> ClearChatAsync()
    {
        var broadcasterId = await ResolveBroadcasterIdAsync();
        if (string.IsNullOrEmpty(broadcasterId))
        {
            return new TwitchActionResponse(false,
                "Could not resolve the Twitch broadcaster id - re-complete the Twitch setup in Platforms.");
        }

        var response = await SendHelixAsync(HttpMethod.Delete,
            $"moderation/chat?broadcaster_id={broadcasterId}&moderator_id={broadcasterId}", null);
        if (!response.Success)
        {
            return new TwitchActionResponse(false, MapHelixFailure(response));
        }

        return new TwitchActionResponse(true, "Twitch chat cleared.");
    }

    /// <summary>
    /// Shared POST /helix/moderation/bans path for ban + timeout (timeout =
    /// same endpoint with a duration in seconds). Best-effort: returns false
    /// with an info log on any failure so auto-mod callers can degrade.
    /// </summary>
    private async Task<bool> BanOrTimeoutUserAsync(string username, string? reason, int? durationSeconds)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            _logger.LogInformation("Twitch ban/timeout skipped - no username provided");
            return false;
        }

        try
        {
            var broadcasterId = await ResolveBroadcasterIdAsync();
            if (string.IsNullOrEmpty(broadcasterId))
            {
                _logger.LogInformation("Twitch ban/timeout for {Username} skipped - could not resolve the broadcaster id",
                    username);
                return false;
            }

            var target = username.Trim().TrimStart('@');
            var targetId = await ResolveUserIdByLoginAsync(target);
            if (string.IsNullOrEmpty(targetId))
            {
                _logger.LogInformation("Twitch ban/timeout skipped - could not resolve user id for {Username}", target);
                return false;
            }

            var payload = new Dictionary<string, object>
            {
                ["user_id"] = targetId,
                ["reason"] = string.IsNullOrWhiteSpace(reason) ? string.Empty : reason.Trim()
            };
            if (durationSeconds.HasValue)
            {
                payload["duration"] = durationSeconds.Value;
            }

            var response = await SendHelixAsync(HttpMethod.Post,
                $"moderation/bans?broadcaster_id={broadcasterId}&moderator_id={broadcasterId}",
                JsonSerializer.Serialize(payload));
            if (!response.Success)
            {
                _logger.LogInformation("Twitch {Action} for {Username} not applied: {Failure}",
                    durationSeconds is null ? "ban" : "timeout", target, MapHelixFailure(response));
                return false;
            }

            _logger.LogInformation("Twitch {Action} applied to {Username}",
                durationSeconds is null ? "ban" : $"timeout ({durationSeconds}s)", target);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Twitch ban/timeout for {Username} failed", username);
            return false;
        }
    }

    /// <inheritdoc />
    public Task<bool> TimeoutUserAsync(string username, TimeSpan duration, string reason)
    {
        // Helix timeouts accept 1 second to 14 days; clamp before the shared call.
        var seconds = Math.Clamp((int)Math.Round(duration.TotalSeconds), 1, 1209600);
        return BanOrTimeoutUserAsync(username, reason, seconds);
    }

    /// <inheritdoc />
    public Task<bool> BanUserAsync(string username, string reason)
        => BanOrTimeoutUserAsync(username, reason, null);

    /// <inheritdoc />
    public async Task<bool> DeleteMessageAsync(string messageId)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            _logger.LogInformation("Twitch message delete skipped - no message id provided");
            return false;
        }

        try
        {
            var broadcasterId = await ResolveBroadcasterIdAsync();
            if (string.IsNullOrEmpty(broadcasterId))
            {
                _logger.LogInformation("Twitch message delete skipped - could not resolve the broadcaster id");
                return false;
            }

            var response = await SendHelixAsync(HttpMethod.Delete,
                $"moderation/chat?broadcaster_id={broadcasterId}&moderator_id={broadcasterId}" +
                $"&message_id={Uri.EscapeDataString(messageId.Trim())}", null);
            if (!response.Success)
            {
                _logger.LogInformation("Twitch message delete not applied: {Failure}", MapHelixFailure(response));
                return false;
            }

            _logger.LogInformation("Deleted a Twitch chat message by id");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Twitch message delete failed");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<TwitchActionResponse> SetChatModeAsync(bool? emoteOnly, bool? followerOnly)
    {
        if (emoteOnly is null && followerOnly is null)
        {
            return new TwitchActionResponse(false, "Nothing to change - pick a chat mode to toggle.");
        }

        var broadcasterId = await ResolveBroadcasterIdAsync();
        if (string.IsNullOrEmpty(broadcasterId))
        {
            return new TwitchActionResponse(false,
                "Could not resolve the Twitch broadcaster id - re-complete the Twitch setup in Platforms.");
        }

        var body = new Dictionary<string, object>();
        if (emoteOnly.HasValue)
        {
            body["emote_mode"] = emoteOnly.Value;
        }

        if (followerOnly.HasValue)
        {
            body["follower_mode"] = followerOnly.Value;
        }

        var response = await SendHelixAsync(HttpMethod.Put,
            $"chat/settings?broadcaster_id={broadcasterId}&moderator_id={broadcasterId}",
            JsonSerializer.Serialize(body));
        if (!response.Success)
        {
            return new TwitchActionResponse(false, MapHelixFailure(response));
        }

        var parts = new List<string>();
        if (emoteOnly.HasValue)
        {
            parts.Add($"emote-only {(emoteOnly.Value ? "on" : "off")}");
        }

        if (followerOnly.HasValue)
        {
            parts.Add($"follower-only {(followerOnly.Value ? "on" : "off")}");
        }

        return new TwitchActionResponse(true, $"Twitch chat updated - {string.Join(", ", parts)}.");
    }

    /// <inheritdoc />
    public async Task<TwitchActionResponse> SetShieldModeAsync(bool enabled)
    {
        var broadcasterId = await ResolveBroadcasterIdAsync();
        if (string.IsNullOrEmpty(broadcasterId))
        {
            return new TwitchActionResponse(false,
                "Could not resolve the Twitch broadcaster id - re-complete the Twitch setup in Platforms.");
        }

        var response = await SendHelixAsync(HttpMethod.Put,
            $"moderation/shield_mode?broadcaster_id={broadcasterId}&moderator_id={broadcasterId}",
            JsonSerializer.Serialize(new { is_enabled = enabled }));
        if (!response.Success)
        {
            return new TwitchActionResponse(false, MapHelixFailure(response));
        }

        return enabled
            ? new TwitchActionResponse(true, "Shield Mode enabled - suspect users are monitored and timed out.")
            : new TwitchActionResponse(true, "Shield Mode disabled.");
    }

    /// <inheritdoc />
    public async Task<TwitchChatModeState?> GetChatModeStateAsync()
    {
        try
        {
            var broadcasterId = await ResolveBroadcasterIdAsync();
            if (string.IsNullOrEmpty(broadcasterId))
            {
                return null;
            }

            var settings = await SendHelixAsync(HttpMethod.Get,
                $"chat/settings?broadcaster_id={broadcasterId}&moderator_id={broadcasterId}", null);
            var shield = await SendHelixAsync(HttpMethod.Get,
                $"moderation/shield_mode?broadcaster_id={broadcasterId}&moderator_id={broadcasterId}", null);
            if (!settings.Success || !shield.Success)
            {
                return null;
            }

            using var settingsDoc = JsonDocument.Parse(settings.Body);
            using var shieldDoc = JsonDocument.Parse(shield.Body);

            if (!settingsDoc.RootElement.TryGetProperty("data", out var settingsData) || settingsData.GetArrayLength() == 0 ||
                !shieldDoc.RootElement.TryGetProperty("data", out var shieldData) || shieldData.GetArrayLength() == 0)
            {
                return null;
            }

            var settings0 = settingsData[0];
            var shield0 = shieldData[0];
            return new TwitchChatModeState(
                settings0.TryGetProperty("emote_mode", out var emote) && emote.GetBoolean(),
                settings0.TryGetProperty("follower_mode", out var follower) && follower.GetBoolean(),
                shield0.TryGetProperty("is_enabled", out var shieldEnabled) && shieldEnabled.GetBoolean());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read the Twitch chat mode state");
            return null;
        }
    }

    #endregion

    private void OnConnected(object? sender, EventArgs e)
    {
        _logger.LogInformation("Twitch client connected");
    }

    private void OnAuthenticationFailed(object? sender, EventArgs e)
    {
        _logger.LogWarning("Twitch authentication failed - attempting token refresh");

        // Attempt token refresh if we have a refresh token
        if (!string.IsNullOrEmpty(_refreshToken))
        {
            _ = Task.Run(async () => await RefreshAndReconnectAsync());
        }
        else
        {
            _logger.LogError("No refresh token available - cannot refresh Twitch token");
        }
    }

    private async Task RefreshAndReconnectAsync()
    {
        try
        {
            _logger.LogInformation("Refreshing Twitch access token...");
            // Ensure OAuth client credentials are loaded before attempting a refresh
            try
            {
                await _oAuthService.LoadCredentialsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load Twitch client credentials before refresh; proceeding anyway");
            }

            var result = await _oAuthService.RefreshAccessTokenAsync(_refreshToken!);
            if (result.HasValue)
            {
                _accessToken = result.Value.AccessToken;
                _refreshToken = result.Value.RefreshToken;

                // Save the new tokens
                await _oAuthService.SaveTokensAsync(_botUsername!, _accessToken, _refreshToken);

                _logger.LogInformation("Successfully refreshed Twitch access token, reconnecting...");

                // Disconnect old client
                if (_client != null)
                {
                    _client.Connected -= OnConnected;
                    _client.MessageReceived -= OnMessageReceived;
                    _client.Disconnected -= OnDisconnected;
                    _client.AuthenticationFailed -= OnAuthenticationFailed;
                    _client.Dispose();
                }

                // Recreate client with new token
                var ircLogger = _loggerFactory.CreateLogger<LightweightTwitchIrc>();
                _client = new LightweightTwitchIrc(ircLogger);

                _client.Connected += OnConnected;
                _client.MessageReceived += OnMessageReceived;
                _client.Disconnected += OnDisconnected;
                _client.AuthenticationFailed += OnAuthenticationFailed;

                await _client.ConnectAsync(_botUsername!, _accessToken, _channel!);

                _logger.LogInformation("Successfully reconnected to Twitch with refreshed token");
            }
            else
            {
                _logger.LogError("Failed to refresh Twitch access token");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing Twitch token");
        }
    }

    private void OnDisconnected(object? sender, EventArgs e)
    {
        _logger.LogWarning("Twitch client disconnected");

        // Attempt reconnection if not intentional disconnect
        if (!_intentionalDisconnect && !_disposed &&
            !string.IsNullOrEmpty(_botUsername) &&
            !string.IsNullOrEmpty(_accessToken) &&
            !string.IsNullOrEmpty(_channel))
        {
            _logger.LogInformation("Attempting to reconnect to Twitch...");
            _reconnectCts = new CancellationTokenSource();
            _ = Task.Run(async () => await ReconnectAsync(_reconnectCts.Token));
        }
    }

    private async Task ReconnectAsync(CancellationToken cancellationToken)
    {
        const int maxRetries = 5;
        int retryCount = 0;

        while (!cancellationToken.IsCancellationRequested && retryCount < maxRetries)
        {
            try
            {
                retryCount++;
                var delay = TimeSpan.FromSeconds(Math.Min(retryCount * 5, 30)); // Exponential backoff up to 30 seconds

                _logger.LogInformation("Reconnection attempt {Attempt}/{MaxRetries} in {Delay} seconds",
                    retryCount, maxRetries, delay.TotalSeconds);

                await Task.Delay(delay, cancellationToken);

                if (cancellationToken.IsCancellationRequested) break;

                // Clean up old client
                if (_client != null)
                {
                    _client.Connected -= OnConnected;
                    _client.MessageReceived -= OnMessageReceived;
                    _client.Disconnected -= OnDisconnected;
                    _client.AuthenticationFailed -= OnAuthenticationFailed;
                    _client.Dispose();
                }

                // Recreate client and reconnect
                var ircLogger = _loggerFactory.CreateLogger<LightweightTwitchIrc>();
                _client = new LightweightTwitchIrc(ircLogger);

                _client.Connected += OnConnected;
                _client.MessageReceived += OnMessageReceived;
                _client.Disconnected += OnDisconnected;
                _client.AuthenticationFailed += OnAuthenticationFailed;

                await _client.ConnectAsync(_botUsername!, _accessToken!, _channel!);

                _logger.LogInformation("Successfully reconnected to Twitch");
                return; // Success
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reconnection attempt {Attempt} failed", retryCount);
            }
        }

        if (retryCount >= maxRetries)
        {
            _logger.LogError("Failed to reconnect to Twitch after {MaxRetries} attempts", maxRetries);
        }
    }

    private void OnMessageReceived(object? sender, TwitchMessageEventArgs e)
    {
        _logger.LogInformation("Received Twitch message from {Username}: {Message}",
            e.Username, e.Message);

        try
        {
            var chatEvent = new TwitchChatMessageEvent
            {
                Source = "Twitch",
                Channel = e.Channel,
                Username = e.Username,
                UserId = e.UserId ?? "",
                DisplayName = e.DisplayName ?? e.Username,
                Message = e.Message,
                Color = e.Color ?? "",
                IsSubscriber = e.IsSubscriber,
                IsModerator = e.IsModerator,
                IsBroadcaster = e.IsBroadcaster,
                IsVip = e.IsVip,
                SubscriberMonths = 0,
                Badges = e.Badges,
                Emotes = new List<string>()
            };

            // Publish to EventBus using fire-and-forget to avoid blocking the WebSocket read thread
            // CRITICAL: .GetAwaiter().GetResult() was blocking the IRC read loop and preventing message delivery
            _ = Task.Run(async () =>
            {
                try
                {
                    await _eventBus.PublishAsync(chatEvent);
                    _logger.LogDebug("Published TwitchChatMessageEvent from {Username}", e.Username);
                }
                catch (Exception pubEx)
                {
                    _logger.LogError(pubEx, "Failed to publish TwitchChatMessageEvent to EventBus");
                }
            });

            // Also publish the platform-agnostic chat message event so unified
            // pipelines (commands, currency, giveaways, polls, watchtime) run for
            // Twitch messages too (KI-1: this event was previously never published).
            var genericEvent = new ChatMessageReceivedEvent
            {
                Source = "Twitch",
                Platform = "Twitch",
                Channel = e.Channel,
                Username = e.Username,
                UserId = e.UserId,
                Message = e.Message,
                IsModerator = e.IsModerator,
                IsSubscriber = e.IsSubscriber,
                IsVip = e.IsVip,
                IsBroadcaster = e.IsBroadcaster
            };

            _ = Task.Run(async () =>
            {
                try
                {
                    await _eventBus.PublishAsync(genericEvent);
                    _logger.LogDebug("Published ChatMessageReceivedEvent from Twitch user {Username}", e.Username);
                }
                catch (Exception pubEx)
                {
                    _logger.LogError(pubEx, "Failed to publish ChatMessageReceivedEvent to EventBus");
                }
            });

            // Store to database (always store - TwitchChatEnabled controls display, not storage)
            if (_platform?.TwitchChatEnabled ?? true)
            {
                _logger.LogDebug("💾 Storing Twitch message in database for channel: {Channel}", _channel ?? "Unknown");
                _chatMessageStore?.AddAsync("Twitch", _channel ?? "Unknown", chatEvent);
            }
            else
            {
                // Still store even when disabled - user may want to load history later
                _logger.LogDebug("💾 Storing Twitch message (TwitchChatEnabled=false): {Username}", e.Username);
                _chatMessageStore?.AddAsync("Twitch", _channel ?? "Unknown", chatEvent);
            }

            _logger.LogInformation("✅ Twitch chat message processed: {Username}: {Message}", e.Username, e.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Twitch chat message");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _intentionalDisconnect = true;
        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();

        // Dispose EventSub
        if (_eventSubService != null)
        {
            try
            {
                _eventSubService.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing EventSub");
            }
        }

        // Unsubscribe from events
        if (_client != null)
        {
            _client.Connected -= OnConnected;
            _client.MessageReceived -= OnMessageReceived;
            _client.Disconnected -= OnDisconnected;
            _client.AuthenticationFailed -= OnAuthenticationFailed;

            _client.Dispose();
            _client = null;
        }

        _disposed = true;

        _logger.LogInformation("TwitchService disposed");
    }
}
