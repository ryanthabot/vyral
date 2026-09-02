# Kick Chat Integration - Migration Plan

**Date**: December 5, 2025  
**Status**: Awaiting Approval

---

## 🔍 Executive Summary

**CRITICAL FINDING**: VyralBot is using webhooks for Kick chat messages, but **Kick uses Pusher WebSocket for real-time chat**. This is why chat messages from other users never arrive.

**Root Cause**: Previous audit (KICK_INTEGRATION_AUDIT_AND_PLAN.md) incorrectly marked Pusher code as "obsolete" and recommended removal. VyralBot HAD working Pusher implementation but removed it in favor of webhooks.

**Solution**: Resurrect Pusher WebSocket implementation + integrate REST API endpoints from kick-chat-system-formatted.md

---

## 📋 Current State Analysis

### **What We Have (Webhook-Based - INCORRECT)**

| Component | Status | Notes |
|-----------|--------|-------|
| **WebhooksController.cs** | ✅ Working | Receives POST /webhooks/kick, processes events |
| **Test Webhooks** | ✅ Working | Proves infrastructure is valid |
| **Real Kick Messages** | ❌ Never Arrive | Kick doesn't send chat via webhooks |
| **KickWebhookSubscriptionService** | ✅ Active | 10 event subscriptions (01KBR03R...) |
| **Signature Validation** | ✅ Working | RSA-SHA256 validation with test bypass |
| **Ngrok Tunnel** | ✅ Active | https://fructuous-precious-plunderable.ngrok-free.dev |
| **Kestrel Server** | ✅ Running | http://localhost:5000 |
| **KickService.cs** | ⚠️ Partial | SendMessageAsync works, no WebSocket |
| **ChatViewModel.cs** | ✅ Ready | Subscribes to EventBus, displays test webhooks |

### **What We Need (Per kick-chat-system-formatted.md - CORRECT)**

| Component | Status | Implementation Required |
|-----------|--------|-------------------------|
| **Pusher WebSocket** | ❌ Missing | Real-time chat via wss://ws-us2.pusher.com |
| **REST API (8.1)** | ⚠️ Partial | Chat & Messaging endpoints (send works, need delete/polls) |
| **REST API (8.2)** | ❌ Missing | Channel & User metadata endpoints |
| **REST API (8.3)** | ❌ Missing | Moderation endpoints (bans, unbans, pins) |
| **Chat History** | ❌ Missing | GET /api/v1/chat/{chatroom_id}/history |
| **Bearer Token Auth** | ✅ Have | Bearer 301683129\|OsoHzxOQIXj7Qx123Do0A69iSA1pMVeSG68AoTbz |

---

## 🔄 Historical Context

### **What Happened**

**VyralBot v1**: Had working Pusher WebSocket implementation
- Evidence: `docs/services/kick-service.md` lines 185-241
- Pusher client with key `eb1d5f283081a78b932c`
- Subscribed to `chatrooms.{chatroom_id}.v2`
- Bound to `App\Events\ChatMessageEvent`

**Previous Audit**: KICK_INTEGRATION_AUDIT_AND_PLAN.md
- Issue #2: Marked Pusher as "Obsolete Pusher WebSocket Code"
- Recommendation: REMOVE Pusher, use webhooks instead
- **This was incorrect** - Pusher IS the correct approach

**Result**: Real-time chat stopped working
- Test webhooks work (infrastructure valid)
- Real Kick messages never arrive (webhooks not for chat)
- Webhook subscriptions are for platform events (follows, subs, raids)

### **Pusher Implementation (From docs/services/kick-service.md)**

```csharp
// Step 1: Get chatroom ID from channel info
var channelInfo = await GetChannelInfoAsync(channelName);
_chatroomId = channelInfo.chatroom.id;

// Step 2: Initialize Pusher client
var options = new PusherOptions
{
    Cluster = "us2",
    Encrypted = true
};
_pusher = new Pusher("eb1d5f283081a78b932c", options);

// Step 3: Connect to Pusher WebSocket
await _pusher.ConnectAsync();
await Task.Delay(1000); // Wait for connection

// Step 4: Subscribe to chatroom channel
var chatroomChannel = $"chatrooms.{_chatroomId}.v2";
_channel = await _pusher.SubscribeAsync(chatroomChannel);

// Step 5: Bind to chat message events
_channel.Bind("App\\Events\\ChatMessageEvent", OnChatMessage);
```

---

## ✅ Migration Plan: Webhook → Pusher WebSocket + REST API

### **Phase 1: Resurrect Pusher WebSocket (Real-time Chat)** 🚀

**Priority**: CRITICAL - Fixes real-time chat message receiving

**Goal**: Restore Pusher WebSocket connection for real-time chat messages

#### **Step 1.1: Add PusherClient NuGet Package**

```powershell
dotnet add src/VyralBot.Integrations/VyralBot.Integrations.csproj package PusherClient
```

#### **Step 1.2: Restore Pusher Code in KickService.cs**

**Add Fields** (after line 22):
```csharp
private Pusher? _pusher;
private Channel? _channel;
private string? _chatroomId;
```

**Modify ConnectAsync** (replace existing method):
```csharp
public async Task ConnectAsync(string username, string accessToken, string? refreshToken, string channelName, int broadcasterUserId)
{
    try
    {
        _logger.LogInformation("Connecting to Kick as {Username} for channel {Channel}, User ID: {UserId}", 
            username, channelName, broadcasterUserId);

        _botUsername = username;
        _currentChannel = channelName;
        _accessToken = accessToken;
        _refreshToken = refreshToken;
        _broadcasterUserId = broadcasterUserId;

        // Step 1: Get chatroom ID via REST API
        var channelInfo = await GetChannelInfoAsync(channelName);
        if (channelInfo != null)
        {
            _chatroomId = channelInfo.Value.chatroomId;
            _logger.LogInformation("Successfully verified channel: {Channel}, Chatroom ID: {ChatroomId}", 
                channelName, _chatroomId);
        }

        // Step 2: Initialize Pusher client
        if (!string.IsNullOrEmpty(_chatroomId))
        {
            var options = new PusherOptions
            {
                Cluster = "us2",
                Encrypted = true
            };

            _pusher = new Pusher("eb1d5f283081a78b932c", options);
            _pusher.ConnectionStateChanged += OnConnectionStateChanged;
            _pusher.Error += OnPusherError;

            // Step 3: Connect to Pusher WebSocket
            _logger.LogInformation("🔌 Connecting to Pusher WebSocket...");
            await _pusher.ConnectAsync();
            await Task.Delay(1000); // Wait for connection establishment

            // Step 4: Subscribe to chatroom channel
            var chatroomChannel = $"chatrooms.{_chatroomId}.v2";
            _logger.LogInformation("📡 Subscribing to Pusher channel: {Channel}", chatroomChannel);
            _channel = await _pusher.SubscribeAsync(chatroomChannel);

            // Step 5: Bind to chat message events
            _channel.Bind("App\\Events\\ChatMessageEvent", OnChatMessage);
            
            _logger.LogInformation("✅ Successfully connected to Kick chatroom via Pusher: {ChatroomId}", _chatroomId);
        }
        
        IsConnected = true;
        _logger.LogInformation("Kick connection established (UserId: {UserId})", _broadcasterUserId);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error connecting to Kick");
        IsConnected = false;
    }
}
```

**Add OnChatMessage Handler** (new method):
```csharp
private void OnChatMessage(PusherEvent pusherEvent)
{
    try
    {
        _logger.LogInformation("📩 Received Pusher chat message event");

        // Double-decode: Pusher wraps the actual event data
        var outerData = JsonSerializer.Deserialize<JsonElement>(pusherEvent.Data);
        var eventDataString = outerData.GetProperty("event").GetString();
        var eventData = JsonSerializer.Deserialize<JsonElement>(eventDataString);

        // Extract message data
        var data = eventData.GetProperty("data");
        var messageId = data.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
        var content = data.TryGetProperty("content", out var contentProp) ? contentProp.GetString() ?? "" : "";
        
        // Extract sender information
        var sender = data.GetProperty("sender");
        var senderId = sender.GetProperty("id").GetInt32().ToString();
        var username = sender.GetProperty("username").GetString() ?? "Unknown";
        var slug = sender.GetProperty("slug").GetString() ?? username;

        // Extract identity (color and badges)
        string? color = null;
        var badges = new Dictionary<string, string>();
        var isSubscriber = false;
        var isModerator = false;
        var isOwner = false;
        
        if (sender.TryGetProperty("identity", out var identity))
        {
            color = identity.TryGetProperty("color", out var colorProp) ? colorProp.GetString() : null;
            
            if (identity.TryGetProperty("badges", out var badgesProp) && badgesProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var badge in badgesProp.EnumerateArray())
                {
                    var type = badge.GetProperty("type").GetString();
                    switch (type)
                    {
                        case "subscriber":
                            isSubscriber = true;
                            badges["subscriber"] = "1";
                            break;
                        case "moderator":
                            isModerator = true;
                            badges["moderator"] = "1";
                            break;
                        case "broadcaster":
                            isOwner = true;
                            badges["broadcaster"] = "1";
                            break;
                    }
                }
            }
        }

        // Create and publish event to EventBus
        var kickEvent = new KickChatMessageEvent
        {
            Source = "Kick",
            Channel = _currentChannel ?? "Unknown",
            Username = username,
            UserId = senderId,
            Message = content,
            DisplayName = slug,
            Color = color,
            IsSubscriber = isSubscriber,
            IsModerator = isModerator,
            IsOwner = isOwner,
            Badges = badges,
            MessageId = messageId,
            CreatedAt = DateTime.UtcNow
        };

        _logger.LogInformation("📤 Publishing Pusher message to EventBus: {Username}: {Message}", username, content);
        _eventBus.PublishAsync(kickEvent).GetAwaiter().GetResult();
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error processing Pusher chat message");
    }
}

private void OnConnectionStateChanged(object? sender, ConnectionState state)
{
    _logger.LogInformation("🔄 Pusher connection state changed: {State}", state);
}

private void OnPusherError(object? sender, PusherException error)
{
    _logger.LogError("❌ Pusher error: {Message}", error.Message);
}
```

**Modify DisconnectAsync** (add Pusher cleanup):
```csharp
public async Task DisconnectAsync()
{
    try
    {
        _logger.LogInformation("Disconnecting from Kick");

        // Disconnect Pusher WebSocket
        if (_channel != null)
        {
            _channel.Unbind("App\\Events\\ChatMessageEvent");
            await _channel.UnsubscribeAsync();
            _channel = null;
        }

        if (_pusher != null)
        {
            await _pusher.DisconnectAsync();
            _pusher.ConnectionStateChanged -= OnConnectionStateChanged;
            _pusher.Error -= OnPusherError;
            _pusher = null;
        }

        _accessToken = null;
        _refreshToken = null;
        _broadcasterUserId = null;
        _botUsername = null;
        _currentChannel = null;
        _chatroomId = null;

        IsConnected = false;
        _logger.LogInformation("Disconnected from Kick");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error disconnecting from Kick");
    }
}
```

**Update GetChannelInfoAsync** (return chatroom_id):
```csharp
private async Task<(int userId, string chatroomId)?> GetChannelInfoAsync(string channelName)
{
    try
    {
        using var httpClient = new HttpClient();
        var response = await httpClient.GetAsync($"https://api.kick.com/api/v2/channels/{channelName}");
        
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to get channel info for {Channel}: {StatusCode}", channelName, response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        var data = JsonSerializer.Deserialize<JsonElement>(json);
        
        var userId = data.GetProperty("user_id").GetInt32();
        var chatroomId = data.GetProperty("chatroom").GetProperty("id").GetInt32().ToString();
        
        return (userId, chatroomId);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error getting channel info for {Channel}", channelName);
        return null;
    }
}
```

#### **Step 1.3: Remove Comment About Webhooks**

**KickService.cs** line 12 - DELETE:
```csharp
/// Note: Message receiving is handled via webhooks in WebhooksController.
```

**REPLACE WITH**:
```csharp
/// Note: Message receiving is handled via Pusher WebSocket (real-time).
```

#### **Expected Result**
✅ Real-time chat messages from other users appear in ChatViewModel  
✅ Pusher WebSocket connection established on ConnectAsync  
✅ Messages published to EventBus (ChatViewModel already subscribed)

---

### **Phase 2: Implement REST API Endpoints (Sections 8.1, 8.2, 8.3)** 📡

**Priority**: HIGH - Full Kick API integration

**Goal**: Implement REST API endpoints for chat management, channel metadata, and moderation

#### **Step 2.1: Create KickApiService.cs**

**Location**: `src/VyralBot.Integrations/Kick/KickApiService.cs`

```csharp
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace VyralBot.Integrations.Kick;

public interface IKickApiService
{
    // 8.1 Chat & Messaging
    Task<bool> DeleteMessageAsync(string messageId);
    Task<JsonElement?> GetChatroomSettingsAsync(int chatroomId);
    Task<JsonElement?> CreatePollAsync(int chatroomId, string question, string[] options, int durationMinutes);
    Task<JsonElement?> GetPollAsync(int pollId);
    
    // 8.2 Channel & User
    Task<JsonElement?> GetChannelAsync(string slug);
    Task<JsonElement?> GetUserAsync(int userId);
    Task<JsonElement?> GetLivestreamStatusAsync(string slug);
    Task<JsonElement?> GetLeaderboardsAsync(string slug);
    
    // 8.3 Moderation
    Task<bool> BanUserAsync(string channelSlug, int userId, bool permanent = false, int? durationMinutes = null);
    Task<bool> UnbanUserAsync(string channelSlug, int userId);
    Task<bool> PinMessageAsync(string channelSlug, string messageId);
    Task<bool> UnpinMessageAsync(string channelSlug);
}

public class KickApiService : IKickApiService
{
    private readonly ILogger<KickApiService> _logger;
    private readonly HttpClient _httpClient;
    private string? _accessToken;

    public KickApiService(ILogger<KickApiService> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient { BaseAddress = new Uri("https://api.kick.com") };
    }

    public void SetAccessToken(string accessToken)
    {
        _accessToken = accessToken;
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    // 8.1 Chat & Messaging
    public async Task<bool> DeleteMessageAsync(string messageId)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"/api/v2/messages/{messageId}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting message {MessageId}", messageId);
            return false;
        }
    }

    public async Task<JsonElement?> GetChatroomSettingsAsync(int chatroomId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/v2/chatrooms/{chatroomId}/settings");
            if (!response.IsSuccessStatusCode) return null;
            
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting chatroom settings for {ChatroomId}", chatroomId);
            return null;
        }
    }

    public async Task<JsonElement?> CreatePollAsync(int chatroomId, string question, string[] options, int durationMinutes)
    {
        try
        {
            var payload = new
            {
                question,
                options = options.Select(opt => new { text = opt }).ToArray(),
                duration = durationMinutes
            };

            var response = await _httpClient.PostAsJsonAsync($"/api/v2/polls/create", payload);
            if (!response.IsSuccessStatusCode) return null;
            
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating poll");
            return null;
        }
    }

    public async Task<JsonElement?> GetPollAsync(int pollId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/v2/polls/{pollId}");
            if (!response.IsSuccessStatusCode) return null;
            
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting poll {PollId}", pollId);
            return null;
        }
    }

    // 8.2 Channel & User
    public async Task<JsonElement?> GetChannelAsync(string slug)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/v2/channels/{slug}");
            if (!response.IsSuccessStatusCode) return null;
            
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting channel {Slug}", slug);
            return null;
        }
    }

    public async Task<JsonElement?> GetUserAsync(int userId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/v2/users/{userId}");
            if (!response.IsSuccessStatusCode) return null;
            
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user {UserId}", userId);
            return null;
        }
    }

    public async Task<JsonElement?> GetLivestreamStatusAsync(string slug)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/v2/channels/{slug}/livestream");
            if (!response.IsSuccessStatusCode) return null;
            
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting livestream status for {Slug}", slug);
            return null;
        }
    }

    public async Task<JsonElement?> GetLeaderboardsAsync(string slug)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/v2/channels/{slug}/leaderboards");
            if (!response.IsSuccessStatusCode) return null;
            
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting leaderboards for {Slug}", slug);
            return null;
        }
    }

    // 8.3 Moderation
    public async Task<bool> BanUserAsync(string channelSlug, int userId, bool permanent = false, int? durationMinutes = null)
    {
        try
        {
            var payload = new
            {
                user_id = userId,
                permanent,
                duration = durationMinutes
            };

            var response = await _httpClient.PostAsJsonAsync($"/api/v2/channels/{channelSlug}/bans", payload);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error banning user {UserId} from {Channel}", userId, channelSlug);
            return false;
        }
    }

    public async Task<bool> UnbanUserAsync(string channelSlug, int userId)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"/api/v2/channels/{channelSlug}/bans/{userId}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unbanning user {UserId} from {Channel}", userId, channelSlug);
            return false;
        }
    }

    public async Task<bool> PinMessageAsync(string channelSlug, string messageId)
    {
        try
        {
            var payload = new { message_id = messageId };
            var response = await _httpClient.PostAsJsonAsync($"/api/v2/channels/{channelSlug}/pinned-message", payload);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pinning message {MessageId} in {Channel}", messageId, channelSlug);
            return false;
        }
    }

    public async Task<bool> UnpinMessageAsync(string channelSlug)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"/api/v2/channels/{channelSlug}/pinned-message");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unpinning message in {Channel}", channelSlug);
            return false;
        }
    }
}
```

#### **Step 2.2: Register KickApiService in DI**

**App.xaml.cs** - Add to service registration:
```csharp
services.AddSingleton<IKickApiService, KickApiService>();
```

#### **Step 2.3: Integrate with KickService**

**KickService.cs** - Inject IKickApiService:
```csharp
private readonly IKickApiService _apiService;

public KickService(ILogger<KickService> logger, IEventBus eventBus, KickOAuthService oAuthService, IKickApiService apiService)
{
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    _oAuthService = oAuthService ?? throw new ArgumentNullException(nameof(oAuthService));
    _apiService = apiService ?? throw new ArgumentNullException(nameof(apiService));
}
```

**ConnectAsync** - Set API access token:
```csharp
// After storing _accessToken
_apiService.SetAccessToken(accessToken);
```

#### **Expected Result**
✅ Full REST API integration (chat, channel, moderation)  
✅ Methods available: DeleteMessage, BanUser, CreatePoll, etc.

---

### **Phase 3: Implement Chat History (Appendix1)** 📜

**Priority**: MEDIUM - Load historical messages on connect

**Goal**: Retrieve and display chat history when connecting to channel

#### **Step 3.1: Add Chat History Method to KickApiService**

```csharp
// Add to IKickApiService interface
Task<List<KickChatMessageEvent>> GetChatHistoryAsync(int chatroomId);

// Add to KickApiService class
public async Task<List<KickChatMessageEvent>> GetChatHistoryAsync(int chatroomId)
{
    try
    {
        var response = await _httpClient.GetAsync($"/api/v1/chat/{chatroomId}/history");
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to get chat history for chatroom {ChatroomId}: {StatusCode}", 
                chatroomId, response.StatusCode);
            return new List<KickChatMessageEvent>();
        }

        var json = await response.Content.ReadAsStringAsync();
        var data = JsonSerializer.Deserialize<JsonElement>(json);
        
        var messages = new List<KickChatMessageEvent>();
        
        if (data.TryGetProperty("data", out var dataArray) && dataArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var msg in dataArray.EnumerateArray())
            {
                var messageId = msg.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                var content = msg.TryGetProperty("content", out var contentProp) ? contentProp.GetString() ?? "" : "";
                
                var sender = msg.GetProperty("sender");
                var senderId = sender.GetProperty("id").GetInt32().ToString();
                var username = sender.GetProperty("username").GetString() ?? "Unknown";
                var slug = sender.GetProperty("slug").GetString() ?? username;
                
                // Parse identity (color, badges)
                string? color = null;
                var badges = new Dictionary<string, string>();
                var isSubscriber = false;
                var isModerator = false;
                var isOwner = false;
                
                if (sender.TryGetProperty("identity", out var identity))
                {
                    color = identity.TryGetProperty("color", out var colorProp) ? colorProp.GetString() : null;
                    
                    if (identity.TryGetProperty("badges", out var badgesProp) && badgesProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var badge in badgesProp.EnumerateArray())
                        {
                            var type = badge.GetProperty("type").GetString();
                            switch (type)
                            {
                                case "subscriber":
                                    isSubscriber = true;
                                    badges["subscriber"] = "1";
                                    break;
                                case "moderator":
                                    isModerator = true;
                                    badges["moderator"] = "1";
                                    break;
                                case "broadcaster":
                                    isOwner = true;
                                    badges["broadcaster"] = "1";
                                    break;
                            }
                        }
                    }
                }
                
                // Parse created_at timestamp
                DateTime? createdAt = null;
                if (msg.TryGetProperty("created_at", out var createdAtProp))
                {
                    var createdAtStr = createdAtProp.GetString();
                    if (DateTime.TryParse(createdAtStr, out var parsedDate))
                    {
                        createdAt = parsedDate;
                    }
                }
                
                messages.Add(new KickChatMessageEvent
                {
                    Source = "Kick",
                    Channel = "Unknown", // Will be set by caller
                    Username = username,
                    UserId = senderId,
                    Message = content,
                    DisplayName = slug,
                    Color = color,
                    IsSubscriber = isSubscriber,
                    IsModerator = isModerator,
                    IsOwner = isOwner,
                    Badges = badges,
                    MessageId = messageId,
                    CreatedAt = createdAt
                });
            }
        }
        
        _logger.LogInformation("📜 Retrieved {Count} chat history messages for chatroom {ChatroomId}", 
            messages.Count, chatroomId);
        
        return messages;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error getting chat history for chatroom {ChatroomId}", chatroomId);
        return new List<KickChatMessageEvent>();
    }
}
```

#### **Step 3.2: Load History in KickService.ConnectAsync**

**Add after Pusher subscription succeeds**:
```csharp
// Step 6: Load chat history
if (!string.IsNullOrEmpty(_chatroomId) && int.TryParse(_chatroomId, out var chatroomIdInt))
{
    _logger.LogInformation("📜 Loading chat history for chatroom {ChatroomId}...", _chatroomId);
    var history = await _apiService.GetChatHistoryAsync(chatroomIdInt);
    
    foreach (var message in history)
    {
        message.Channel = _currentChannel ?? "Unknown";
        await _eventBus.PublishAsync(message);
    }
    
    _logger.LogInformation("✅ Published {Count} historical messages to EventBus", history.Count);
}
```

#### **Expected Result**
✅ Chat history loads when connecting to channel  
✅ Historical messages appear in ChatViewModel BEFORE live messages  
✅ Seamless transition from history to real-time messages

---

### **Phase 4: Keep Webhooks for Platform Events** 🎣

**Priority**: LOW - Optimize webhook subscriptions

**Goal**: Remove redundant chat.message.sent, keep webhooks for platform events

#### **Step 4.1: Update KickWebhookSubscriptionService**

**Modify SubscribeToEventsAsync** - Remove chat.message.sent:

```csharp
private static readonly string[] EventTypes = new[]
{
    // REMOVED: "chat.message.sent", // Now handled via Pusher WebSocket
    "channel.followed",
    "channel.subscription.new",
    "channel.subscription.renewal",
    "channel.subscription.gifts",
    "livestream.status.updated",
    "moderation.banned",
    "moderation.unbanned",
    "moderation.message.deleted",
    "kicks.gifted"
};
```

#### **Step 4.2: Update WebhooksController**

**Remove or comment out ProcessChatMessage**:
```csharp
case "chat.message.sent":
    // No longer using webhooks for chat messages (handled via Pusher WebSocket)
    _logger.LogInformation("Received chat.message.sent webhook (ignoring - using Pusher)");
    break;
```

#### **Step 4.3: Add Comment in WebhooksController**

**Add at top of KickWebhook method**:
```csharp
// Note: Real-time chat messages are received via Pusher WebSocket in KickService.
// Webhooks are used for platform events (follows, subscriptions, raids, etc.)
```

#### **Expected Result**
✅ Webhooks only handle platform events (follows, subs, raids)  
✅ No duplicate chat messages from both webhook and WebSocket  
✅ Reduced webhook subscription count (9 instead of 10)

---

## 📊 Implementation Summary

### **Files to Modify**

| File | Changes | Priority |
|------|---------|----------|
| **KickService.cs** | Add Pusher WebSocket, restore ConnectAsync/DisconnectAsync | CRITICAL |
| **KickApiService.cs** | Create new REST API service (8.1, 8.2, 8.3, Appendix1) | HIGH |
| **App.xaml.cs** | Register IKickApiService in DI | HIGH |
| **VyralBot.Integrations.csproj** | Add PusherClient NuGet package | CRITICAL |
| **KickWebhookSubscriptionService.cs** | Remove chat.message.sent from subscriptions | LOW |
| **WebhooksController.cs** | Update chat.message.sent handling | LOW |

### **NuGet Packages Required**

```xml
<PackageReference Include="PusherClient" Version="2.1.0" />
```

### **Configuration Values**

| Parameter | Value | Source |
|-----------|-------|--------|
| **Pusher Key** | `eb1d5f283081a78b932c` | kick-chat-system-formatted.md Section 3.1 |
| **Pusher Cluster** | `us2` | kick-chat-system-formatted.md Section 3.1 |
| **Pusher Protocol** | `7` | kick-chat-system-formatted.md Section 3.1 |
| **Channel Format** | `chatrooms.{chatroom_id}.v2` | Section 4.3 |
| **Event Name** | `App\Events\ChatMessageEvent` | Section 5.2 |
| **Bearer Token** | `Bearer 301683129\|OsoHzxOQIXj7Qx123Do0A69iSA1pMVeSG68AoTbz` | Appendix2 |
| **chatroom_id** | `23862571` | Appendix2 (for idlrp channel) |
| **broadcaster_user_id** | `24865197` | Appendix2 (for idlrp channel) |

---

## 🎯 Migration Options

### **Option A: Full Migration (Recommended)** ⭐

**Phases**: 1 + 2 + 3 + 4  
**Timeline**: 2-4 hours  
**Features**:
- ✅ Real-time chat via Pusher WebSocket
- ✅ Full REST API integration (chat, channel, moderation)
- ✅ Chat history on connect
- ✅ Optimized webhook subscriptions

**Pros**:
- Complete Kick integration matching kick-chat-system-formatted.md
- All endpoints from sections 8.1, 8.2, 8.3 implemented
- Chat history provides context on connect
- Webhooks optimized for platform events only

**Cons**:
- More implementation time
- More testing required

---

### **Option B: Minimal Fix (Faster)** ⚡

**Phases**: 1 only  
**Timeline**: 30-60 minutes  
**Features**:
- ✅ Real-time chat via Pusher WebSocket
- ⚠️ Keep existing SendMessageAsync (works)
- ❌ No additional REST API endpoints
- ❌ No chat history
- ❌ Webhooks unchanged

**Pros**:
- Quick fix for real-time chat
- Minimal code changes
- Low risk

**Cons**:
- Missing advanced features (moderation, polls, etc.)
- No chat history on connect
- Webhook subscriptions not optimized

---

### **Option C: Phased Rollout** 🔄

**Phase 1 Now**: Pusher WebSocket (critical fix)  
**Phase 2 Later**: REST API endpoints (when needed)  
**Phase 3 Later**: Chat history (nice-to-have)  
**Phase 4 Later**: Webhook optimization (cleanup)

**Pros**:
- Get chat working immediately
- Defer non-critical features
- Incremental testing

**Cons**:
- Multiple deployment cycles
- May forget to complete phases 2-4

---

## 📝 Testing Plan

### **Phase 1 Testing (Pusher WebSocket)**

1. **Start Application**
   - Verify Pusher connection logs: "🔌 Connecting to Pusher WebSocket..."
   - Verify subscription logs: "📡 Subscribing to Pusher channel: chatrooms.{id}.v2"
   - Check for connection state changes

2. **Send Chat Message from Another User**
   - Use Kick web app or another account
   - Send message in connected channel
   - Verify message appears in VyralBot ChatViewModel
   - Check logs for "📩 Received Pusher chat message event"

3. **Verify EventBus Publishing**
   - Check logs for "📤 Publishing Pusher message to EventBus"
   - Confirm ChatViewModel receives event
   - Verify message formatting (username, badges, color)

4. **Test Disconnect/Reconnect**
   - Disconnect from Kick
   - Verify Pusher cleanup (channel unsubscribe, disconnect)
   - Reconnect and verify chat still works

### **Phase 2 Testing (REST API)**

1. **Chat & Messaging (8.1)**
   - DeleteMessageAsync: Delete own message, verify removal
   - CreatePollAsync: Create poll, verify in Kick UI
   - GetChatroomSettingsAsync: Retrieve settings

2. **Channel & User (8.2)**
   - GetChannelAsync: Verify channel metadata
   - GetUserAsync: Retrieve user profile
   - GetLivestreamStatusAsync: Check if channel is live

3. **Moderation (8.3)**
   - BanUserAsync: Ban test user, verify in Kick UI
   - UnbanUserAsync: Unban user, verify
   - PinMessageAsync: Pin message, verify

### **Phase 3 Testing (Chat History)**

1. **Load History on Connect**
   - Connect to channel with existing chat history
   - Verify historical messages appear in ChatViewModel
   - Check message order (oldest to newest)

2. **Verify History → Live Transition**
   - After history loads, send new live message
   - Verify no duplicates
   - Check timestamp ordering

### **Phase 4 Testing (Webhook Optimization)**

1. **Verify Webhook Subscriptions**
   - Check active subscriptions (should be 9, not 10)
   - Verify chat.message.sent removed
   - Confirm other events still subscribed

2. **Test Platform Events**
   - Trigger follow event → verify webhook received
   - Trigger subscription event → verify webhook received
   - Verify no chat.message.sent webhooks arrive

---

## 🚨 Risks and Mitigation

### **Risk 1: PusherClient Library Compatibility**

**Risk**: PusherClient NuGet package may not work with .NET 9.0  
**Likelihood**: Low  
**Impact**: High  
**Mitigation**:
- Test package installation first
- Use latest stable version (2.1.0)
- Alternative: Implement raw WebSocket client if needed

### **Risk 2: Double-Decode Complexity**

**Risk**: Pusher event parsing may fail (double JSON encoding)  
**Likelihood**: Medium  
**Impact**: High  
**Mitigation**:
- Follow kick-chat-system-formatted.md Section 5.2 exactly
- Add robust error handling in OnChatMessage
- Log raw Pusher event data for debugging

### **Risk 3: EventBus Race Condition**

**Risk**: History messages may arrive after live messages  
**Likelihood**: Low  
**Impact**: Medium  
**Mitigation**:
- Load history BEFORE subscribing to Pusher
- Add message ordering by timestamp in ChatViewModel
- Add "Loading history..." indicator in UI

### **Risk 4: Token Expiration**

**Risk**: Bearer token may expire mid-session  
**Likelihood**: High (eventually)  
**Impact**: Medium  
**Mitigation**:
- Implement token refresh logic (already have RefreshTokenAsync)
- Add 401 response handling in KickApiService
- Auto-refresh before each API call if token near expiration

### **Risk 5: Webhook Duplicate Events**

**Risk**: During migration, may receive chat messages from both webhook AND WebSocket  
**Likelihood**: Medium  
**Impact**: Low (UI clutter)  
**Mitigation**:
- Implement Phase 4 immediately after Phase 1
- Add message deduplication by messageId in ChatViewModel
- Remove chat.message.sent subscription ASAP

---

## ✅ Success Criteria

### **Phase 1 Success**
- ✅ Pusher WebSocket connection established
- ✅ Real-time chat messages from other users appear in ChatViewModel
- ✅ No errors in logs during connection/disconnection
- ✅ Messages formatted correctly (username, badges, color, content)

### **Phase 2 Success**
- ✅ All REST API methods implemented (8.1, 8.2, 8.3)
- ✅ API calls return expected data (verified via testing)
- ✅ Bearer token authentication working
- ✅ Error handling for 401/403/404 responses

### **Phase 3 Success**
- ✅ Chat history loads on channel connect
- ✅ Historical messages appear BEFORE live messages
- ✅ No duplicates or missing messages
- ✅ Timestamp ordering correct

### **Phase 4 Success**
- ✅ chat.message.sent removed from webhook subscriptions
- ✅ No duplicate chat messages (webhook + WebSocket)
- ✅ Platform events (follows, subs) still received via webhooks
- ✅ Webhook subscription count reduced to 9

---

## 📅 Recommended Timeline

### **Option A: Full Migration**

| Phase | Duration | Dependencies |
|-------|----------|--------------|
| Phase 1 | 1 hour | None |
| Phase 2 | 1.5 hours | Phase 1 (for API token) |
| Phase 3 | 0.5 hours | Phase 2 (uses KickApiService) |
| Phase 4 | 0.5 hours | Phase 1 (after Pusher confirmed working) |
| **Total** | **3.5 hours** | |

### **Option B: Minimal Fix**

| Phase | Duration | Dependencies |
|-------|----------|--------------|
| Phase 1 | 1 hour | None |
| **Total** | **1 hour** | |

---

## 🎬 Next Steps

**Awaiting User Approval**:
- **Option A**: Full Migration (recommended)
- **Option B**: Minimal Fix (faster)
- **Option C**: Phased Rollout (incremental)
- **Custom**: Specify your own approach

**Once Approved**:
1. Begin implementation starting with Phase 1
2. Test each phase before moving to next
3. Update documentation as code changes
4. Mark KICK_INTEGRATION_AUDIT_AND_PLAN.md as obsolete
5. Update this migration plan with actual results

---

**Status**: 🟢 **COMPLETED** - Full Migration Implemented  
**Last Updated**: December 5, 2025  
**Document**: KICK_MIGRATION_PLAN.md

---

## ✅ Implementation Summary

**All 4 Phases Completed Successfully!**

### Phase 1: Pusher WebSocket ✅
- ✅ PusherClient 2.2.1 NuGet package added
- ✅ Pusher fields added to KickService.cs (_pusher, _channel, _chatroomId)
- ✅ ConnectAsync restored with Pusher initialization
- ✅ Pusher WebSocket connection to wss://ws-us2.pusher.com
- ✅ Channel subscription to chatrooms.{chatroom_id}.v2
- ✅ Event binding to App\\Events\\ChatMessageEvent
- ✅ OnChatMessage handler implemented with EventBus publishing
- ✅ DisconnectAsync updated with proper cleanup
- ✅ Connection state and error handlers added

### Phase 2: REST API Endpoints ✅
- ✅ KickApiService.cs created with IKickApiService interface
- ✅ Section 8.1 Chat & Messaging: DeleteMessage, GetChatroomSettings, CreatePoll, GetPoll
- ✅ Section 8.2 Channel & User: GetChannel, GetUser, GetLivestreamStatus, GetLeaderboards
- ✅ Section 8.3 Moderation: BanUser, UnbanUser, PinMessage, UnpinMessage
- ✅ Bearer token authentication via SetAccessToken
- ✅ Registered IKickApiService in App.xaml.cs DI

### Phase 3: Chat History ✅
- ✅ GetChatHistoryAsync implemented in KickApiService
- ✅ GET /api/v1/chat/{chatroom_id}/history endpoint
- ✅ Chat history parsing with full message structure
- ✅ Integrated into KickService.ConnectAsync (Step 6)
- ✅ Historical messages published to EventBus
- ✅ Loads after Pusher connection established

### Phase 4: Webhook Optimization ✅
- ✅ Removed chat.message.sent from WebhookCoordinatorService event list
- ✅ Added comment explaining Pusher WebSocket handles chat
- ✅ Updated WebhooksController to ignore chat.message.sent webhooks
- ✅ Added comment in KickWebhook method about Pusher
- ✅ Webhook subscriptions now only for platform events (9 events)

### Build Status ✅
- ✅ Solution builds successfully (dotnet build)
- ✅ All projects compiled without errors
- ✅ Only minor warnings (null references, async methods)
- ✅ Ready for runtime testing

---

## 🧪 Next Steps: Testing

### Test 1: Pusher WebSocket Connection
1. Start VyralBot application
2. Connect to Kick channel
3. Check logs for:
   - "🔌 Connecting to Pusher WebSocket..."
   - "📡 Subscribing to Pusher channel: chatrooms.{id}.v2"
   - "✅ Successfully connected to Kick chatroom via Pusher"
4. Verify Pusher connection state changes logged

### Test 2: Real-time Chat Messages
1. Send message from another account in connected channel
2. Verify message appears in VyralBot ChatViewModel
3. Check logs for:
   - "📩 Received Pusher chat message event"
   - "📤 Publishing Pusher message to EventBus"
4. Confirm message formatting (username, badges, color)

### Test 3: Chat History Loading
1. Connect to channel with existing chat history
2. Verify historical messages appear in ChatViewModel
3. Check logs for:
   - "📜 Loading chat history for chatroom {id}..."
   - "✅ Published {count} historical messages to EventBus"
4. Verify messages in chronological order

### Test 4: REST API Endpoints
1. Test DeleteMessageAsync on own message
2. Test GetChannelAsync for channel metadata
3. Test GetLivestreamStatusAsync for live status
4. Verify all API calls authenticated with Bearer token

### Test 5: Webhook Platform Events
1. Trigger follow event (from another account)
2. Verify webhook received (not Pusher)
3. Check WebhooksController logs
4. Confirm chat.message.sent NOT in webhook subscriptions

---

**Status**: 🟢 **COMPLETED** - Full Migration Implemented  
**Last Updated**: December 5, 2025  
**Document**: KICK_MIGRATION_PLAN.md
