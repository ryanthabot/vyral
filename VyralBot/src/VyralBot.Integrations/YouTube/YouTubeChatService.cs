using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    /// <inheritdoc />
    public string LiveChatState { get; private set; } = "NotConnected";

    /// <summary>Publishes a live-chat state transition to the EventBus (deduped per state).</summary>
    private async Task SetLiveChatStateAsync(string state, string message)
    {
        if (string.Equals(LiveChatState, state, StringComparison.Ordinal))
        {
            return;
        }

        LiveChatState = state;
        _logger.LogInformation("📺 YouTube live chat state: {State} — {Message}", state, message);
        try
        {
            await _eventBus.PublishAsync(new Events.YouTubeLiveChatStateEvent { State = state, Message = message, Source = "YouTubeChatService" });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish the YouTube live chat state change");
        }
    }
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
        await SetLiveChatStateAsync("Connecting", "Connecting to YouTube live chat…");

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
            await SetLiveChatStateAsync("WaitingForBroadcast",
                "YouTube connected — your channel isn't live right now, so there's no live chat to read. Chat messages will appear once you go live.");
        }
        else
        {
            _logger.LogInformation("✅ Found active live chat: {LiveChatId}", _liveChatId);
            await SetLiveChatStateAsync("Listening", "YouTube live chat is active — listening to your stream's chat.");
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

        // Reset the live-chat state so the UI no longer reports "Listening"/"WaitingForBroadcast"
        await SetLiveChatStateAsync("NotConnected", "Disconnected from YouTube live chat.");

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
                // Try refresh and retry once — the request must be REBUILT because a
                // sent HttpRequestMessage (with its consumed content) cannot be resent.
                if (await RefreshTokenAsync())
                {
                    using var retryRequest = new HttpRequestMessage(HttpMethod.Post, $"{YouTubeApiBase}/liveChat/messages?part=snippet");
                    retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
                    retryRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    response = await _httpClient.SendAsync(retryRequest);
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

    #region Stream Info Update

    /// <inheritdoc />
    public async Task<YouTubeActionResponse> UpdateStreamInfoAsync(string? title, string? description)
    {
        if (!_isConnected)
        {
            return new YouTubeActionResponse(false, "YouTube is not connected — connect YouTube in Platforms first.");
        }

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(description))
        {
            return new YouTubeActionResponse(false, "Nothing to update — enter a title or description first.");
        }

        try
        {
            await EnsureTokenFreshAsync();

            var broadcastId = await FindActiveBroadcastIdAsync();
            if (string.IsNullOrEmpty(broadcastId))
            {
                return new YouTubeActionResponse(false,
                    "No active YouTube broadcast found — you're not live right now, so there's nothing to update. Go live and try again.");
            }

            // Fetch the full broadcast resource (snippet + status), modify only the
            // provided snippet fields, then PUT the whole resource back — YouTube
            // requires the status part to be included unchanged.
            var getUrl = $"{YouTubeApiBase}/liveBroadcasts?part=snippet,status&id={broadcastId}";
            var (getStatusCode, getBody, getSuccess) = await SendYouTubeApiAsync(HttpMethod.Get, getUrl, jsonBody: null);
            if (!getSuccess)
            {
                _logger.LogWarning("⚠️ Failed to load the YouTube broadcast resource (status={Status}): {Body}",
                    getStatusCode, getBody);
                return new YouTubeActionResponse(false,
                    $"Could not load the YouTube broadcast ({(int)getStatusCode}). Try again in a moment.");
            }

            JsonNode? broadcastNode;
            try
            {
                broadcastNode = JsonNode.Parse(getBody)?["items"]?[0];
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse the YouTube broadcast resource");
                broadcastNode = null;
            }

            if (broadcastNode?["snippet"] == null)
            {
                return new YouTubeActionResponse(false,
                    "The YouTube broadcast payload was missing its snippet — try again in a moment.");
            }

            if (!string.IsNullOrWhiteSpace(title))
            {
                broadcastNode["snippet"]!["title"] = title.Trim();
            }

            if (!string.IsNullOrWhiteSpace(description))
            {
                broadcastNode["snippet"]!["description"] = description.Trim();
            }

            var putUrl = $"{YouTubeApiBase}/liveBroadcasts?part=snippet,status";
            var (putStatusCode, putBody, putSuccess) = await SendYouTubeApiAsync(
                HttpMethod.Put, putUrl, broadcastNode.ToJsonString());

            if (!putSuccess)
            {
                _logger.LogWarning("⚠️ YouTube broadcast update failed (status={Status}): {Body}",
                    putStatusCode, putBody);

                if (putStatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    return new YouTubeActionResponse(false,
                        "YouTube rejected the update (403). Re-run the YouTube setup in Platforms to grant the updated permissions.");
                }

                return new YouTubeActionResponse(false, $"YouTube rejected the update ({(int)putStatusCode}).");
            }

            _logger.LogInformation("✅ Updated YouTube broadcast info for {BroadcastId}", broadcastId);

            var changedTitle = !string.IsNullOrWhiteSpace(title);
            var changedDescription = !string.IsNullOrWhiteSpace(description);
            var message = changedTitle && changedDescription
                ? "YouTube stream updated — title and description saved."
                : changedTitle
                    ? $"YouTube stream updated — title set to \"{title!.Trim()}\"."
                    : "YouTube stream updated — description saved.";
            return new YouTubeActionResponse(true, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error updating YouTube stream info");
            return new YouTubeActionResponse(false, $"Updating the YouTube stream failed: {ex.Message}");
        }
    }

    /// <summary>
    /// GET liveBroadcasts (active, mine) → the active broadcast id, or null when none.
    /// </summary>
    private async Task<string?> FindActiveBroadcastIdAsync()
    {
        var url = $"{YouTubeApiBase}/liveBroadcasts?part=id&broadcastStatus=active&mine=true";
        var (statusCode, body, success) = await SendYouTubeApiAsync(HttpMethod.Get, url, jsonBody: null);
        if (!success)
        {
            _logger.LogWarning("⚠️ Failed to look up the active YouTube broadcast (status={Status}): {Body}",
                statusCode, body);
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("items", out var items) && items.GetArrayLength() > 0 &&
                items[0].TryGetProperty("id", out var id))
            {
                var broadcastId = id.GetString();
                if (!string.IsNullOrEmpty(broadcastId))
                {
                    _logger.LogInformation("🔴 Found active YouTube broadcast {BroadcastId}", broadcastId);
                    return broadcastId;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse the YouTube liveBroadcasts lookup response");
        }

        return null;
    }

    /// <summary>
    /// Sends an authenticated YouTube Data API request, refreshing the token and
    /// retrying once on 401 (the request is rebuilt because a sent
    /// HttpRequestMessage cannot be resent). 403 is NOT retried — it usually
    /// means a missing scope, and a refresh would wrongly flag RequiresReauth.
    /// </summary>
    private async Task<(System.Net.HttpStatusCode StatusCode, string Body, bool Success)> SendYouTubeApiAsync(
        HttpMethod method, string url, string? jsonBody)
    {
        async Task<(System.Net.HttpStatusCode StatusCode, string Body)> SendOnceAsync()
        {
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            if (jsonBody != null)
            {
                request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            }

            using var response = await _httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            return (response.StatusCode, body);
        }

        var (statusCode, body) = await SendOnceAsync();

        if (statusCode == System.Net.HttpStatusCode.Unauthorized && await RefreshTokenAsync())
        {
            (statusCode, body) = await SendOnceAsync();
        }

        var success = (int)statusCode >= 200 && (int)statusCode < 300;
        return (statusCode, body, success);
    }

    #endregion

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
                        await SetLiveChatStateAsync("Listening", "You're live on YouTube — live chat is active.");
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
                await SetLiveChatStateAsync("WaitingForBroadcast", "The YouTube stream ended — waiting for you to go live again.");
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
            // 401/403 → refresh token and retry once within this poll iteration.
            // The request must be REBUILT because a sent HttpRequestMessage cannot be resent.
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                _logger.LogWarning("🔄 YouTube API returned {Status}, refreshing token...", response.StatusCode);
                if (await RefreshTokenAsync())
                {
                    // Retry once after refresh with a fresh request message
                    using var retryRequest = new HttpRequestMessage(HttpMethod.Get, url);
                    retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
                    response = await _httpClient.SendAsync(retryRequest, ct);
                }
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);

                // 404 → stream ended
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    throw new HttpRequestException($"Live chat not found", null, System.Net.HttpStatusCode.NotFound);
                }

                _logger.LogError("❌ YouTube chat poll failed: {Status} - {Error}", response.StatusCode, errorBody);
                return;
            }
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

        var messageType = snippet.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;
        if (string.IsNullOrEmpty(messageType))
            return;

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

        // Text messages keep their raw text; special events (superchats, super
        // stickers, new members, member milestones) are mapped to a synthetic
        // display message so they are no longer silently dropped.
        var messageText = messageType == "textMessageEvent"
            ? (snippet.TryGetProperty("textMessageDetails", out var textDetails) &&
               textDetails.TryGetProperty("messageText", out var msgProp)
                ? msgProp.GetString() ?? ""
                : "")
            : BuildSpecialEventMessage(messageType, displayName, snippet);

        // Membership events (new member / member milestone) also publish a
        // structured event so the UI gets the member level without having to
        // parse it out of the synthetic chat text. Published before the
        // empty-message gate below so it fires exactly once per event.
        if (messageType is "newSponsorEvent" or "memberMilestoneChatEvent")
        {
            PublishMembershipEvent(messageType, channelId, displayName, snippet);
        }

        if (string.IsNullOrEmpty(messageText))
            return;

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
            try
            {
                if (_chatMessageStore != null)
                {
                    await _chatMessageStore.AddAsync("YouTube", _currentChannel ?? "Unknown", chatEvent);
                }
            }
            catch (Exception storeEx)
            {
                _logger.LogWarning(storeEx, "Failed to store YouTube chat message from {Username}", displayName);
            }
        }

        _logger.LogInformation("💬 YouTube chat: {Username}: {Message}", displayName, messageText);
    }

    /// <summary>
    /// Publishes a structured <see cref="YouTubeMembershipEvent"/> for a new-member
    /// ("newSponsorEvent") or member-milestone ("memberMilestoneChatEvent") live chat
    /// event. Fire-and-forget on the thread pool with the same try/catch logging the
    /// chat message publishes use, so a publish failure never breaks the poll loop.
    /// </summary>
    private void PublishMembershipEvent(string messageType, string channelId, string displayName, JsonElement snippet)
    {
        // newSponsorEvent → newSponsorDetails.memberLevelName;
        // memberMilestoneChatEvent → memberMilestoneChatDetails.memberLevelName.
        var level = messageType == "memberMilestoneChatEvent"
            ? (snippet.TryGetProperty("memberMilestoneChatDetails", out var milestoneDetails) &&
               milestoneDetails.TryGetProperty("memberLevelName", out var milestoneLevel)
                ? milestoneLevel.GetString() ?? ""
                : "")
            : (snippet.TryGetProperty("newSponsorDetails", out var sponsorDetails) &&
               sponsorDetails.TryGetProperty("memberLevelName", out var sponsorLevel)
                ? sponsorLevel.GetString() ?? ""
                : "");

        var membershipEvent = new YouTubeMembershipEvent
        {
            Source = "YouTube",
            Username = displayName,
            UserId = channelId,
            Level = level,
            IsMilestone = messageType == "memberMilestoneChatEvent"
        };

        _ = Task.Run(async () =>
        {
            try
            {
                await _eventBus.PublishAsync(membershipEvent);
                _logger.LogDebug("✅ Published YouTubeMembershipEvent for {Username} (milestone: {IsMilestone}, level: {Level})",
                    displayName, membershipEvent.IsMilestone, membershipEvent.Level);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to publish YouTubeMembershipEvent for {Username}", displayName);
            }
        });
    }

    /// <summary>
    /// Builds a synthetic chat message for YouTube special events (superchat,
    /// super sticker, new member, member milestone) from the event snippet.
    /// Returns an empty string when the payload can't be mapped.
    /// </summary>
    private static string BuildSpecialEventMessage(string messageType, string displayName, JsonElement snippet)
    {
        try
        {
            switch (messageType)
            {
                case "superChatEvent":
                case "superStickerEvent":
                    var details = messageType == "superChatEvent"
                        ? snippet.TryGetProperty("superChatDetails", out var superChatDetails) ? superChatDetails : default
                        : snippet.TryGetProperty("superStickerDetails", out var superStickerDetails) ? superStickerDetails : default;
                    if (details.ValueKind == JsonValueKind.Undefined)
                        return string.Empty;

                    var amount = details.TryGetProperty("amountDisplayString", out var amountProp)
                        ? amountProp.GetString() : null;
                    var comment = details.TryGetProperty("userComment", out var commentProp)
                        ? commentProp.GetString() : null;
                    var kind = messageType == "superChatEvent" ? "superchatted" : "sent a Super Sticker";

                    return string.IsNullOrEmpty(comment)
                        ? $"💎 {displayName} {kind} {amount ?? "a gift"}!"
                        : $"💎 {displayName} {kind} {amount ?? "a gift"}: {comment}";

                case "newSponsorEvent":
                    return $"🌟 {displayName} became a member!";

                case "memberMilestoneChatEvent":
                    var milestoneDetails = snippet.TryGetProperty("memberMilestoneChatDetails", out var mmd)
                        ? mmd : default;
                    var level = milestoneDetails.ValueKind != JsonValueKind.Undefined &&
                                milestoneDetails.TryGetProperty("memberLevelName", out var levelProp)
                        ? levelProp.GetString() : null;
                    var milestoneComment = milestoneDetails.ValueKind != JsonValueKind.Undefined &&
                                           milestoneDetails.TryGetProperty("memberComment", out var mcProp)
                        ? mcProp.GetString() : null;
                    var milestoneText = string.IsNullOrEmpty(level)
                        ? $"🌟 {displayName} reached a membership milestone!"
                        : $"🌟 {displayName} reached a membership milestone ({level})!";
                    return string.IsNullOrEmpty(milestoneComment) ? milestoneText : $"{milestoneText}: {milestoneComment}";

                default:
                    return string.Empty;
            }
        }
        catch (Exception)
        {
            return string.Empty;
        }
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
                await SetLiveChatStateAsync("ReauthRequired",
                    "YouTube needs re-authorization — run Home → Connections → YouTube to sign in again.");
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
