using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using VyralBot.Core.Models;
using VyralBot.Core.Services.Chat;
using VyralBot.Core.Services.EventBus;
using VyralBot.Data;
using VyralBot.Data.Entities;
using VyralBot.Integrations.YouTube.Events;

namespace VyralBot.Integrations.YouTube;

/// <summary>
/// YouTube Live Chat service that polls for messages using YouTube Data API v3.
/// Handles token refresh, live broadcast discovery, and sending/receiving chat messages.
/// </summary>
public class YouTubeChatService : IYouTubeService, IDisposable
{
    private readonly ILogger<YouTubeChatService> _logger;
    private readonly IDataService _dataService;
    private readonly IEventBus _eventBus;
    private readonly IChatMessageStore? _chatMessageStore;
    private readonly HttpClient _httpClient;

    private Platform? _platform;
    private string? _currentChannel;
    private string? _botUsername;
    private string? _accessToken;
    private string? _refreshToken;
    private string? _liveChatId;
    private string? _nextPageToken;
    private bool _disposed;
    private bool _isConnected;
    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;

    // Track the last poll time to honor YouTube's pollingIntervalMillis
    private int _pollingIntervalMs = 5000; // Default 5 seconds, YouTube API will tell us the real interval

    public bool IsConnected => _isConnected;

    /// <inheritdoc />
    public bool RequiresReauth { get; private set; }

    /// <inheritdoc />
    public string? ReauthReason { get; private set; }
    public string? Channel => _currentChannel;
    public string? BotUsername => _botUsername;

    private const string YouTubeApiBase = "https://www.googleapis.com/youtube/v3";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    public YouTubeChatService(
        ILogger<YouTubeChatService> logger,
        IDataService dataService,
        IEventBus eventBus,
        IChatMessageStore? chatMessageStore = null)
    {
        _logger = logger;
        _dataService = dataService;
        _eventBus = eventBus;
        _chatMessageStore = chatMessageStore;
        _httpClient = new HttpClient();
    }

    public async Task ConnectAsync(string username, string accessToken, string channel)
    {
        await ConnectAsync(username, accessToken, null, channel);
    }

    public async Task ConnectAsync(string username, string accessToken, string? refreshToken, string channel)
    {
        if (string.IsNullOrEmpty(accessToken))
            throw new ArgumentNullException(nameof(accessToken));

        _logger.LogInformation("🎬 Connecting to YouTube Live Chat - Channel: {Channel}, Username: {Username}",
            channel, username);
        RequiresReauth = false;
        ReauthReason = null;

        _botUsername = username;
        _currentChannel = channel;
        _accessToken = accessToken;
        _refreshToken = refreshToken;

        // Load platform settings from DB
        _platform = await _dataService.GetPlatformByNameAsync("YouTube");
        if (_platform == null)
        {
            _logger.LogError("❌ YouTube platform not found in database");
            throw new InvalidOperationException("YouTube platform not configured");
        }

        // Use refresh token from platform if not provided
        if (string.IsNullOrEmpty(_refreshToken))
            _refreshToken = _platform.RefreshToken;

        // Refresh token if expired or about to expire
        if (_platform.TokenExpiresAt.HasValue && _platform.TokenExpiresAt.Value <= DateTime.UtcNow.AddMinutes(5))
        {
            _logger.LogInformation("🔄 Access token expired or expiring soon, refreshing...");
            var refreshed = await RefreshTokenAsync();
            if (!refreshed)
            {
                _logger.LogError("❌ Failed to refresh YouTube access token");
                throw new YouTubeReauthException("Failed to refresh YouTube access token. Please re-authorize.", ReauthReason);
            }
        }

        // Try to find an active live broadcast
        _liveChatId = await FindActiveLiveChatIdAsync();

        if (string.IsNullOrEmpty(_liveChatId))
        {
            _logger.LogWarning("⚠️ No active YouTube live broadcast found. Will retry periodically.");
            // Still mark as connected — we'll poll for a live broadcast
        }
        else
        {
            _logger.LogInformation("✅ Found active live chat: {LiveChatId}", _liveChatId);
        }

        _isConnected = true;

        // Start polling task
        _pollCts = new CancellationTokenSource();
        _pollTask = Task.Run(() => PollLoopAsync(_pollCts.Token));

        // Publish connection event
        await _eventBus.PublishAsync(new PlatformConnectionChangedEvent
        {
            Source = "YouTubeChatService",
            Platform = "YouTube",
            Connected = true,
            Channel = _currentChannel,
            BotUsername = _botUsername
        });

        _logger.LogInformation("✅ YouTube Live Chat service connected for channel: {Channel}", _currentChannel);
    }

    public async Task DisconnectAsync()
    {
        _logger.LogInformation("🔌 Disconnecting from YouTube Live Chat...");

        _isConnected = false;
        _liveChatId = null;
        _nextPageToken = null;

        // Cancel polling
        _pollCts?.Cancel();
        if (_pollTask != null)
        {
            try
            {
                await _pollTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }
        _pollCts?.Dispose();
        _pollCts = null;
        _pollTask = null;

        // Publish disconnection event
        await _eventBus.PublishAsync(new PlatformConnectionChangedEvent
        {
            Source = "YouTubeChatService",
            Platform = "YouTube",
            Connected = false,
            Channel = _currentChannel,
            BotUsername = _botUsername
        });

        _logger.LogInformation("✅ Disconnected from YouTube Live Chat");
    }

    public async Task SendMessageAsync(string message)
    {
        if (!_isConnected)
            throw new InvalidOperationException("Not connected to YouTube");

        // If we don't have a liveChatId, try to find one before giving up
        if (string.IsNullOrEmpty(_liveChatId))
        {
            _logger.LogInformation("🔍 No liveChatId cached, attempting to find active broadcast...");
            _liveChatId = await FindActiveLiveChatIdAsync();
        }

        if (string.IsNullOrEmpty(_liveChatId))
            throw new InvalidOperationException("No active live chat found. You must be streaming live on YouTube to send messages.");

        // Ensure token is fresh
        await EnsureTokenFreshAsync();

        var payload = new
        {
            snippet = new
            {
                liveChatId = _liveChatId,
                type = "textMessageEvent",
                textMessageDetails = new
                {
                    messageText = message
                }
            }
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{YouTubeApiBase}/liveChat/messages?part=snippet");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        request.Content = content;

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            _logger.LogError("❌ Failed to send YouTube chat message: {Status} - {Error}",
                response.StatusCode, errorBody);

            // Check for auth errors
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                // Try refresh and retry once
                if (await RefreshTokenAsync())
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
                    response = await _httpClient.SendAsync(request);
                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation("✅ YouTube chat message sent after token refresh: {Message}", message);
                        return;
                    }
                }
            }

            throw new InvalidOperationException($"Failed to send YouTube chat message: {response.StatusCode} - {errorBody}");
        }

        _logger.LogInformation("✅ YouTube chat message sent: {Message}", message);
    }

    #region Polling

    private async Task PollLoopAsync(CancellationToken ct)
    {
        _logger.LogInformation("📡 YouTube chat poll loop started");

        // If no live chat yet, poll for broadcast every 30s
        var broadcastCheckInterval = TimeSpan.FromSeconds(30);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // If we don't have a liveChatId, try to find one
                if (string.IsNullOrEmpty(_liveChatId))
                {
                    _liveChatId = await FindActiveLiveChatIdAsync();
                    if (!string.IsNullOrEmpty(_liveChatId))
                    {
                        _logger.LogInformation("🔴 Found active live broadcast! LiveChatId: {LiveChatId}", _liveChatId);
                        _nextPageToken = null; // Reset page token for new chat
                    }
                    else
                    {
                        await Task.Delay(broadcastCheckInterval, ct);
                        continue;
                    }
                }

                // Poll for messages
                await PollMessagesAsync(ct);

                // Respect YouTube's polling interval
                await Task.Delay(_pollingIntervalMs, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden ||
                                                    ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Stream may have ended - clear liveChatId and go back to checking
                _logger.LogWarning("⚠️ Live chat no longer available (stream may have ended). Waiting for next broadcast...");
                _liveChatId = null;
                _nextPageToken = null;
                await Task.Delay(broadcastCheckInterval, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in YouTube chat poll loop");
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
            }
        }

        _logger.LogInformation("📡 YouTube chat poll loop stopped");
    }

    private async Task PollMessagesAsync(CancellationToken ct)
    {
        await EnsureTokenFreshAsync();

        var url = $"{YouTubeApiBase}/liveChat/messages?liveChatId={_liveChatId}&part=snippet,authorDetails";
        if (!string.IsNullOrEmpty(_nextPageToken))
            url += $"&pageToken={_nextPageToken}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);

            // 401/403 → refresh token and retry
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                _logger.LogWarning("🔄 YouTube API returned {Status}, refreshing token...", response.StatusCode);
                if (await RefreshTokenAsync())
                {
                    // Retry once after refresh
                    return;
                }
            }

            // 404 → stream ended
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new HttpRequestException($"Live chat not found", null, System.Net.HttpStatusCode.NotFound);
            }

            _logger.LogError("❌ YouTube chat poll failed: {Status} - {Error}", response.StatusCode, errorBody);
            return;
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Update page token for next poll
        if (root.TryGetProperty("nextPageToken", out var nextToken))
            _nextPageToken = nextToken.GetString();

        // Update polling interval from YouTube's recommendation
        if (root.TryGetProperty("pollingIntervalMillis", out var intervalProp))
        {
            _pollingIntervalMs = intervalProp.GetInt32();
            _logger.LogDebug("YouTube polling interval: {Interval}ms", _pollingIntervalMs);
        }

        // Process messages
        if (root.TryGetProperty("items", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                try
                {
                    await ProcessChatMessageAsync(item);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing YouTube chat message");
                }
            }
        }
    }

    private async Task ProcessChatMessageAsync(JsonElement item)
    {
        await Task.CompletedTask;

        if (!item.TryGetProperty("snippet", out var snippet))
            return;
        if (!item.TryGetProperty("authorDetails", out var author))
            return;

        // Only process text messages
        var messageType = snippet.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;
        if (messageType != "textMessageEvent")
            return;

        var messageText = "";
        if (snippet.TryGetProperty("textMessageDetails", out var textDetails) &&
            textDetails.TryGetProperty("messageText", out var msgProp))
        {
            messageText = msgProp.GetString() ?? "";
        }

        var displayName = author.TryGetProperty("displayName", out var dnProp) ? dnProp.GetString() ?? "" : "";
        var channelId = author.TryGetProperty("channelId", out var cidProp) ? cidProp.GetString() ?? "" : "";
        var profileImage = author.TryGetProperty("profileImageUrl", out var piProp) ? piProp.GetString() : null;
        var isModerator = author.TryGetProperty("isChatModerator", out var modProp) && modProp.GetBoolean();
        var isOwner = author.TryGetProperty("isChatOwner", out var ownerProp) && ownerProp.GetBoolean();
        var isMember = author.TryGetProperty("isChatSponsor", out var memberProp) && memberProp.GetBoolean();
        var messageId = item.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
        var publishedAt = snippet.TryGetProperty("publishedAt", out var pubProp)
            ? DateTime.TryParse(pubProp.GetString(), out var dt) ? dt : (DateTime?)null
            : null;

        var chatEvent = new YouTubeChatMessageEvent
        {
            Source = "YouTube",
            Channel = _currentChannel ?? "Unknown",
            Username = displayName,
            UserId = channelId,
            Message = messageText,
            DisplayName = displayName,
            ProfileImageUrl = profileImage,
            IsModerator = isModerator,
            IsOwner = isOwner,
            IsMember = isMember,
            MessageId = messageId,
            PublishedAt = publishedAt,
            LiveChatId = _liveChatId
        };

        // Publish to EventBus (fire-and-forget to avoid blocking poll thread)
        _ = Task.Run(async () =>
        {
            try
            {
                await _eventBus.PublishAsync(chatEvent);
                _logger.LogDebug("✅ Published YouTubeChatMessageEvent from {Username}", displayName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to publish YouTubeChatMessageEvent for {Username}", displayName);
            }
        });

        // Also publish the platform-agnostic chat message event so unified
        // pipelines (commands, currency, giveaways, polls, watchtime) run for
        // YouTube messages too (KI-1: this event was previously never published).
        var genericEvent = new ChatMessageReceivedEvent
        {
            Source = "YouTube",
            Platform = "YouTube",
            Channel = _currentChannel ?? "Unknown",
            Username = displayName,
            Message = messageText,
            IsModerator = isModerator,
            IsSubscriber = isMember,
            IsVip = false,
            IsBroadcaster = isOwner ||
                (!string.IsNullOrEmpty(_currentChannel) &&
                 string.Equals(displayName, _currentChannel, StringComparison.OrdinalIgnoreCase)),
            MessageId = string.IsNullOrEmpty(messageId) ? null : $"youtube:{messageId}"
        };

        _ = Task.Run(async () =>
        {
            try
            {
                await _eventBus.PublishAsync(genericEvent);
                _logger.LogDebug("✅ Published ChatMessageReceivedEvent from YouTube user {Username}", displayName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to publish ChatMessageReceivedEvent for {Username}", displayName);
            }
        });

        // Store in database
        if (_platform?.YoutubeChatEnabled ?? true)
        {
            _chatMessageStore?.AddAsync("YouTube", _currentChannel ?? "Unknown", chatEvent);
        }

        _logger.LogInformation("💬 YouTube chat: {Username}: {Message}", displayName, messageText);
    }

    #endregion

    #region Live Broadcast Discovery

    private async Task<string?> FindActiveLiveChatIdAsync()
    {
        try
        {
            await EnsureTokenFreshAsync();

            // First try active broadcasts
            var chatId = await QueryBroadcastsForChatIdAsync("active");
            if (!string.IsNullOrEmpty(chatId))
                return chatId;

            // Fall back to all broadcasts (upcoming, testing, etc. also have liveChatIds)
            _logger.LogInformation("🔍 No active broadcast found, checking all broadcasts...");
            chatId = await QueryBroadcastsForChatIdAsync(null);
            if (!string.IsNullOrEmpty(chatId))
                return chatId;

            _logger.LogDebug("No YouTube live broadcasts with liveChatId found");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding active YouTube live broadcast");
            return null;
        }
    }

    /// <summary>
    /// Queries YouTube liveBroadcasts API for a liveChatId.
    /// </summary>
    /// <param name="broadcastStatus">Status filter (active, upcoming, completed) or null for all broadcasts.</param>
    private async Task<string?> QueryBroadcastsForChatIdAsync(string? broadcastStatus)
    {
        try
        {
            var url = broadcastStatus != null
                ? $"{YouTubeApiBase}/liveBroadcasts?part=snippet&broadcastStatus={broadcastStatus}&mine=true"
                : $"{YouTubeApiBase}/liveBroadcasts?part=snippet&mine=true";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("⚠️ Failed to check live broadcasts (status={Status}): {HttpStatus} - {Error}",
                    broadcastStatus ?? "all", response.StatusCode, errorBody);

                // Try refresh and retry once
                if ((response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                     response.StatusCode == System.Net.HttpStatusCode.Forbidden) &&
                    await RefreshTokenAsync())
                {
                    return await QueryBroadcastsForChatIdAsync(broadcastStatus);
                }

                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("items", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (item.TryGetProperty("snippet", out var snippet) &&
                        snippet.TryGetProperty("liveChatId", out var chatIdProp))
                    {
                        var chatId = chatIdProp.GetString();
                        if (!string.IsNullOrEmpty(chatId))
                        {
                            _logger.LogInformation("🔴 Found broadcast (status={Status}) with liveChatId: {ChatId}",
                                broadcastStatus ?? "all", chatId);
                            return chatId;
                        }
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying broadcasts (status={Status})", broadcastStatus ?? "all");
            return null;
        }
    }

    #endregion

    #region Token Refresh

    private async Task EnsureTokenFreshAsync()
    {
        if (_platform?.TokenExpiresAt.HasValue == true &&
            _platform.TokenExpiresAt.Value <= DateTime.UtcNow.AddMinutes(5))
        {
            _logger.LogInformation("🔄 YouTube access token expiring soon, refreshing...");
            await RefreshTokenAsync();
        }
    }

    private async Task<bool> RefreshTokenAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(_refreshToken) || _platform == null)
            {
                _logger.LogWarning("⚠️ Missing refresh token or platform for YouTube token refresh");
                return false;
            }

            var clientId = _platform.ClientId;
            var clientSecret = _platform.ClientSecret;

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                _logger.LogWarning("⚠️ Missing client ID or secret for YouTube token refresh");
                return false;
            }

            var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["refresh_token"] = _refreshToken,
                ["grant_type"] = "refresh_token"
            });

            var response = await _httpClient.PostAsync(TokenEndpoint, formContent);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("❌ YouTube token refresh failed: {Status} - {Body}",
                    response.StatusCode, responseBody);
                RequiresReauth = true;
                ReauthReason = responseBody.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase)
                    ? "invalid_grant"
                    : response.StatusCode.ToString();
                return false;
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (root.TryGetProperty("access_token", out var tokenProp))
            {
                _accessToken = tokenProp.GetString();
                RequiresReauth = false;
                ReauthReason = null;

                // Update platform in database
                _platform.AccessToken = _accessToken;

                if (root.TryGetProperty("expires_in", out var expProp))
                {
                    var expiresIn = expProp.GetInt32();
                    _platform.TokenExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn);
                }

                // Google may issue a new refresh token (rare but possible)
                if (root.TryGetProperty("refresh_token", out var newRefreshProp))
                {
                    var newRefresh = newRefreshProp.GetString();
                    if (!string.IsNullOrEmpty(newRefresh))
                    {
                        _refreshToken = newRefresh;
                        _platform.RefreshToken = newRefresh;
                    }
                }

                _platform.UpdatedAt = DateTime.UtcNow;
                await _dataService.SavePlatformAsync(_platform);

                _logger.LogInformation("✅ YouTube access token refreshed successfully. Expires: {ExpiresAt}",
                    _platform.TokenExpiresAt);
                return true;
            }

            _logger.LogError("❌ YouTube token refresh response missing access_token");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Exception during YouTube token refresh");
            return false;
        }
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;

        _isConnected = false;
        _pollCts?.Cancel();

        try
        {
            _pollTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Ignore
        }

        _pollCts?.Dispose();
        _httpClient.Dispose();
        _disposed = true;

        _logger.LogInformation("🧹 YouTubeChatService disposed");
    }
}
