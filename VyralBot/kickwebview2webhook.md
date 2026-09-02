# Kick WebView2 Integration Plan

## Overview
Implement a **standalone** WebView2-based Kick chat integration that bypasses Cloudflare bot detection by routing all HTTP/WebSocket traffic through an embedded Chromium browser instance instead of using HttpClient. This WebView2 instance is completely separate from existing WebView2 instances used in setup wizards (Twitch/Kick/Ngrok).

## Why Standalone?
- **Isolation**: Existing WebView2 instances in `TwitchWebSetupView`, `KickWebSetupView`, and `NgrokSetupView` are temporary (for OAuth flows)
- **Independent Lifecycle**: Kick chat WebView2 needs to persist for the entire app session when enabled
- **User Control**: Users can enable/disable Kick chat independently via UI toggle in `KickSettingsViewModel`
- **Optional Functionality**: Some users may only want to send messages (webhooks) without receiving (WebView2)

## Ngrok Requirement Analysis

### Before WebView2 (Current State):
- **Webhooks receive**: Followers, Subs, Gifts, Bans, Metadata updates (9 event types)
- **Webhooks do NOT receive**: Chat messages (removed in Phase 4)
- **Pusher receives**: Chat messages only (via PusherClient library - currently failing due to no chatroom_id)

### After WebView2 Implementation:

#### What WebView2 Provides (via Pusher WebSocket):
✅ **Chat Messages** (`App\Events\ChatMessageEvent`)
- User messages
- Emotes
- Badges
- User colors
- Message metadata

✅ **Message Deletions** (`App\Events\MessageDeletedEvent`)
- Deleted message IDs
- Moderation actions

✅ **User Bans/Unbans** (`App\Events\UserBannedEvent`, `App\Events\UserUnbannedEvent`)
- User banned
- Ban duration
- Moderator who banned

#### What Webhooks Provide (ngrok still needed):
✅ **Channel Events**:
- `channel.followed` - New followers
- `channel.subscription.new` - New subs
- `channel.subscription.renewal` - Resubs
- `channel.subscription.gifts` - Community gifts
- `kicks.gifted` - Kick gifts

✅ **Stream Metadata**:
- `livestream.status.updated` - Stream went live/offline
- `livestream.metadata.updated` - Title/category changed

✅ **Moderation**:
- `moderation.banned` - User banned (duplicate with Pusher, but webhooks include more metadata)

✅ **Channel Points**:
- `channel.reward.redemption.updated` - Channel point redemptions

### Conclusion: **YES, Ngrok is still needed**

**Reason**: WebView2 Pusher only provides **chat-related events**. Kick webhooks provide **non-chat events** (followers, subs, stream status, channel points, etc.) that are NOT available via Pusher.

**Architecture After WebView2**:
```
Kick Events Flow:
├── WebView2 Pusher WebSocket → Chat messages, deletions, bans
└── Ngrok Webhooks → Follows, subs, gifts, stream status, metadata, channel points
```

Both systems are complementary, not redundant.

## Architecture

### Core Concept
Instead of:
- `HttpClient` → Cloudflare detects .NET TLS fingerprint → 403 Forbidden

We use:
- `WebView2.ExecuteScriptAsync("fetch(...)")` → Browser TLS fingerprint → Success
- JavaScript WebSocket inside WebView2 → `postMessage` → C# event handlers

### Component Structure

```
KickChatWebView2Service (new standalone service)
├── WebView2 Instance (hidden, persistent, SEPARATE from setup wizards)
├── JavaScript Bridge
│   ├── fetch() for REST API calls (channel info, chat history)
│   └── WebSocket for Pusher (realtime chat messages)
├── Message Router (WebMessageReceived)
├── UI Control (Start/Stop/AutoStart)
│   ├── Controlled by KickSettingsViewModel
│   ├── Toggle: "Enable Kick Chat (WebView2)"
│   ├── Toggle: "Auto-start on Launch"
│   └── Button: "Start/Stop Chat Connection"
└── Lifecycle Management
    ├── Start: User clicks button OR auto-start on launch
    ├── Navigate to kick.com (solve Cloudflare)
    └── Stop: User clicks button OR app shutdown
```

## Implementation Plan

### Phase 1: Standalone WebView2 Service Foundation

#### 1.1 Create KickChatWebView2Service (Standalone)
**File**: `src/VyralBot.Integrations/Kick/KickChatWebView2Service.cs`

**Key Distinction**: This is a **separate** WebView2 instance from setup wizards:
- `TwitchWebSetupView` → OAuth flow only (temporary)
- `KickWebSetupView` → OAuth flow only (temporary)  
- `NgrokSetupView` → Setup only (temporary)
- **`KickChatWebView2Service`** → Chat connection (persistent, controllable)

**Responsibilities**:
- Manage single persistent WebView2 instance for Kick chat
- User-controlled lifecycle (Start/Stop via UI)
- Auto-start on launch (if enabled in settings)
- Handle disposal on app close
- Thread-safe operations (WebView2 requires UI thread)

**Key Methods**:
```csharp
public class KickChatWebView2Service : IDisposable
{
    private WebView2? _webView;
    private bool _isInitialized = false;
    private bool _isRunning = false;
    private readonly ILogger<KickChatWebView2Service> _logger;
    
    // Lifecycle
    public async Task InitializeAsync()  // Called once on first use
    public async Task StartAsync(string username, string bearerToken)  // User clicks Start
    public async Task StopAsync()  // User clicks Stop
    public void Dispose()  // App shutdown
    
    // JavaScript Bridge
    public async Task<string> ExecuteFetchAsync(string url, string method = "GET", object body = null)
    public async Task InitializePusherWebSocketAsync(string chatroomId)
    
    // Events
    public event EventHandler<string> MessageReceived;
    public event EventHandler<bool> ConnectionStateChanged;
    
    // State
    public bool IsRunning { get; }
    public bool IsInitialized { get; }
}
```

#### 1.2 UI Control Integration (KickSettingsViewModel)
**File**: `src/VyralBot.UI/ViewModels/Settings/KickSettingsViewModel.cs`

**New Properties**:
```csharp
public bool IsChatEnabled { get; set; }  // Toggle: Enable/Disable chat receiving
public bool AutoStartChat { get; set; }  // Toggle: Auto-start on app launch
public bool IsChatRunning { get; }  // Read-only: Current connection state
public RelayCommand StartStopChatCommand { get; }  // Button: Start/Stop
```

**User Control Flow**:
1. **Enable Chat**: User checks "Enable Kick Chat" → Saves to database
2. **Auto-start**: User checks "Auto-start on launch" → Saves to database
3. **Manual Start**: User clicks "Start Chat" → Calls `StartAsync(username, bearerToken)`
4. **Manual Stop**: User clicks "Stop Chat" → Calls `StopAsync()`

**Why This Matters**:
- Users who only want to **send** messages can disable chat (no WebView2 overhead)
- Users who want **full integration** enable chat
- Independent from setup wizard WebView2 instances (no conflicts)

#### 1.3 Conditional Initialization Logic
**When**: App startup (`App.xaml.cs` or `MainViewModel`)

**Logic**:
```csharp
// On app startup
var kickPlatform = await _dataService.GetPlatformAsync("Kick");

// Only proceed if Kick is configured
if (kickPlatform != null && !string.IsNullOrEmpty(kickPlatform.Username))
{
    // Check if chat is enabled in settings
    bool chatEnabled = await _dataService.GetKickChatEnabledAsync();
    bool autoStart = await _dataService.GetKickChatAutoStartAsync();
    
    if (chatEnabled && autoStart)
    {
        var chatService = _serviceProvider.GetService<KickChatWebView2Service>();
        
        // Initialize WebView2 (if not already initialized)
        if (!chatService.IsInitialized)
            await chatService.InitializeAsync();
        
        // Start chat connection
        await chatService.StartAsync(kickPlatform.Username, kickPlatform.AccessToken);
    }
}
```

**Key Points**:
- ✅ Only initializes if Kick account exists
- ✅ Only auto-starts if user enabled both toggles
- ✅ User can manually start/stop anytime via settings UI
- ✅ WebView2 persists for entire app session (no recreation overhead)

#### 1.4 Thread Safety
WebView2 must run on UI thread (WPF Dispatcher). Service must handle:
- Dispatching WebView2 operations to UI thread via `Dispatcher.InvokeAsync`
- Async/await for cross-thread communication
- Synchronization of JavaScript execution results

### Phase 2: JavaScript Bridge Implementation

#### 2.1 REST API Fetcher
Inject JavaScript function into WebView2 that executes fetch() and returns JSON:

```javascript
(async function() {
    try {
        const response = await fetch('${url}', {
            method: '${method}',
            headers: {
                'Authorization': 'Bearer ${token}',
                'Accept': 'application/json',
                'Content-Type': 'application/json'
            },
            body: ${bodyJson}
        });
        const data = await response.json();
        return JSON.stringify({success: true, data: data});
    } catch (error) {
        return JSON.stringify({success: false, error: error.message});
    }
})();
```

**C# Wrapper**:
```csharp
public async Task<T> FetchJsonAsync<T>(string url, string method = "GET", object body = null)
{
    string jsCode = BuildFetchScript(url, method, body, _bearerToken);
    string result = await _webView.ExecuteScriptAsync(jsCode);
    
    // Double-deserialize: WebView2 returns quoted JSON string
    var jsonString = JsonSerializer.Deserialize<string>(result);
    var response = JsonSerializer.Deserialize<ApiResponse<T>>(jsonString);
    
    if (!response.Success)
        throw new Exception($"Fetch failed: {response.Error}");
    
    return response.Data;
}
```

#### 2.2 Pusher WebSocket Bridge
Inject persistent WebSocket connection managed entirely in JavaScript:

```javascript
// Store in window scope so it persists
window.kickPusher = {
    socket: null,
    
    connect: function(chatroomId) {
        this.socket = new WebSocket('wss://ws-us2.pusher.com/app/eb1d5f283081a78b932c?protocol=7&client=js&version=8.4.0-rc2&flash=false');
        
        this.socket.onopen = () => {
            console.log('Pusher connected');
        };
        
        this.socket.onmessage = (event) => {
            // Send to C# via postMessage
            window.chrome.webview.postMessage({
                type: 'pusher_message',
                data: event.data
            });
        };
        
        this.socket.onclose = () => {
            console.log('Pusher disconnected');
            window.chrome.webview.postMessage({
                type: 'pusher_disconnected'
            });
        };
    },
    
    subscribe: function(channel) {
        const msg = {
            event: 'pusher:subscribe',
            data: {
                channel: channel
            }
        };
        this.socket.send(JSON.stringify(msg));
    },
    
    disconnect: function() {
        if (this.socket) {
            this.socket.close();
        }
    }
};

// Auto-connect
window.kickPusher.connect();
```

**C# Event Handler**:
```csharp
_webView.WebMessageReceived += (sender, args) =>
{
    var messageJson = args.TryGetWebMessageAsString();
    var message = JsonSerializer.Deserialize<WebViewMessage>(messageJson);
    
    if (message.Type == "pusher_message")
    {
        // Parse Pusher protocol
        var pusherEvent = JsonSerializer.Deserialize<PusherMessage>(message.Data);
        
        if (pusherEvent.Event == "App\\Events\\ChatMessageEvent")
        {
            var chatMessage = JsonSerializer.Deserialize<KickChatMessage>(pusherEvent.Data);
            OnChatMessageReceived(chatMessage);
        }
    }
};
```

### Phase 3: Integration with Existing Architecture

#### 3.1 Modify KickService
**File**: `src/VyralBot.Integrations/Kick/KickService.cs`

**Changes**:
- Inject `KickChatWebView2Service` dependency
- Replace `GetChannelInfoAsync()` to use WebView2 fetch
- Subscribe to `KickChatWebView2Service.MessageReceived` event
- Keep existing event publishing logic (EventBus)
- **Remove PusherClient library dependency** (replaced by WebView2 JavaScript WebSocket)

**Before**:
```csharp
private async Task<(int userId, string channelId, string chatroomId)?> GetChannelInfoAsync(string channelName)
{
    using var httpClient = new HttpClient(); // FAILS - Cloudflare blocks
    // ...
}
```

**After**:
```csharp
private async Task<(int userId, string channelId, string chatroomId)?> GetChannelInfoAsync(string channelName)
{
    // Use WebView2 fetch (bypasses Cloudflare)
    var url = $"https://kick.com/api/v2/channels/{channelName.ToLower()}";
    
    var channelInfo = await _kickChatWebView2Service.ExecuteFetchAsync<KickChannelInfo>(url);
    
    return (channelInfo.User.Id, channelInfo.Id.ToString(), channelInfo.Chatroom.Id.ToString());
}
```

**WebSocket Event Subscription** (in `ConnectAsync`):
```csharp
public async Task ConnectAsync()
{
    // ... existing code ...
    
    // Get channel info via WebView2
    var channelInfo = await GetChannelInfoAsync(_channelName);
    
    if (channelInfo == null)
    {
        _logger.LogError("Failed to get channel info");
        return;
    }
    
    _channelId = channelInfo.Value.channelId;
    _chatroomId = channelInfo.Value.chatroomId;
    
    // Subscribe to WebView2 WebSocket messages
    _kickChatWebView2Service.MessageReceived += OnWebView2MessageReceived;
    
    // Start WebSocket via JavaScript (if not already running)
    if (!_kickChatWebView2Service.IsRunning)
    {
        await _kickChatWebView2Service.StartAsync(_channelName, _bearerToken);
    }
    
    _logger.LogInformation("✅ Kick chat connected via WebView2");
}

private void OnWebView2MessageReceived(object? sender, string messageJson)
{
    try
    {
        var message = JsonSerializer.Deserialize<WebViewMessage>(messageJson);
        
        if (message.Type == "pusher_message")
        {
            // Parse Pusher event
            var pusherEvent = JsonSerializer.Deserialize<PusherMessage>(message.Data);
            
            if (pusherEvent.Event == "App\\Events\\ChatMessageEvent")
            {
                var chatMessage = JsonSerializer.Deserialize<KickChatMessage>(pusherEvent.Data);
                HandleChatMessage(chatMessage);  // Existing method
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error processing WebView2 message");
    }
}
```

**Remove PusherClient**:
```csharp
// DELETE these lines:
// private Pusher? _pusher;
// _pusher = new Pusher(PusherConstants.AppKey, ...);
// await _pusher.ConnectAsync();
// _pusher.Subscribe($"chatrooms.{_chatroomId}.v2");
```

#### 3.2 Modify KickApiService
**File**: `src/VyralBot.Integrations/Kick/KickApiService.cs`

**Changes**:
- Inject `KickChatWebView2Service` dependency
- Replace `GetChatHistoryAsync()` to use WebView2 fetch

**Before**:
```csharp
public async Task<List<KickChatMessage>> GetChatHistoryAsync(string channelId)
{
    using var httpClient = new HttpClient(); // FAILS - Cloudflare blocks
    // ...
}
```

**After**:
```csharp
public async Task<List<KickChatMessage>> GetChatHistoryAsync(string channelId)
{
    var url = $"https://web.kick.com/api/v1/chat/{channelId}/history";
    
    var response = await _kickChatWebView2Service.ExecuteFetchAsync<ChatHistoryResponse>(url);
    
    return response.Messages;
}
```
{
    var url = $"https://kick.com/api/v2/channels/{channelName.ToLower()}";
    var response = await _webView2Service.FetchJsonAsync<ChannelResponse>(url);
    
    return (response.UserId, response.Id.ToString(), response.Chatroom.Id.ToString());
}
```

#### 3.2 Modify KickApiService
**File**: `src/VyralBot.Integrations/Kick/KickApiService.cs`

**Changes**:
- Inject `KickWebView2Service` dependency
- Replace all `_httpClient` calls with `_webView2Service.FetchJsonAsync<T>()`
- Keep method signatures unchanged (maintains compatibility)

**Example**:
```csharp
public async Task<List<KickChatMessage>> GetChatHistoryAsync(int channelId, string channelName)
{
    var url = $"https://web.kick.com/api/v1/chat/{channelId}/history";
    var response = await _webView2Service.FetchJsonAsync<ChatHistoryResponse>(url);
    
    return response.Data.Messages;
}
```

#### 3.3 Dependency Injection Setup
**File**: `src/VyralBot.UI/App.xaml.cs`

Register `KickChatWebView2Service` as singleton:

```csharp
services.AddSingleton<KickChatWebView2Service>(provider =>
{
    var logger = provider.GetRequiredService<ILogger<KickChatWebView2Service>>();
    var dataService = provider.GetRequiredService<IDataService>();
    var dispatcher = Application.Current.Dispatcher;
    
    return new KickChatWebView2Service(logger, dataService, dispatcher);
});
```

**Auto-Start Logic in App Startup** (if enabled):
```csharp
// After profile loaded
var kickChatService = serviceProvider.GetService<KickChatWebView2Service>();
var kickPlatform = await _dataService.GetPlatformAsync("Kick");

if (kickPlatform != null && !string.IsNullOrEmpty(kickPlatform.Username))
{
    // Check if chat is enabled and auto-start is enabled
    bool chatEnabled = await _dataService.GetKickChatEnabledAsync();
    bool autoStart = await _dataService.GetKickChatAutoStartAsync();
    
    if (chatEnabled && autoStart)
    {
        _logger.LogInformation("Auto-starting Kick chat WebView2...");
        
        // Initialize if not already initialized
        if (!kickChatService.IsInitialized)
            await kickChatService.InitializeAsync();
        
        // Start chat connection
        await kickChatService.StartAsync(kickPlatform.Username, kickPlatform.AccessToken);
        
        _logger.LogInformation("✅ Kick chat auto-started");
    }
}
```

**Disposal on App Exit**:
```csharp
protected override void OnExit(ExitEventArgs e)
{
    var kickChatService = _serviceProvider.GetService<KickChatWebView2Service>();
    kickChatService?.Dispose();
    
    base.OnExit(e);
}
```

### Phase 4: Lifecycle Management (User-Controlled)

#### 4.1 Startup Sequence (Auto-Start Optional)
1. **App Launches** → DI container creates `KickChatWebView2Service` (not initialized)
2. **Profile Loads** → Check database for Kick account + chat settings
3. **If Kick Configured AND Chat Enabled AND Auto-Start Enabled**:
   - Call `kickChatWebView2Service.InitializeAsync()` (creates WebView2)
   - Service navigates to `https://kick.com` (solves Cloudflare)
   - Call `kickChatWebView2Service.StartAsync(username, bearerToken)` (connects chat)
   - WebView2 remains running until user clicks "Stop" or app closes
4. **If Kick NOT Configured OR Chat Disabled**:
   - Service remains uninitialized (no WebView2 created)
   - No overhead, no resources consumed

#### 4.2 User Control Flow

**Scenario 1: Auto-Start Enabled**
```
App Launch → Check DB → Chat Enabled + Auto-Start ON
           → Initialize WebView2
           → Navigate to kick.com
           → Start chat (connect WebSocket)
           → Status: "Connected ●"
```

**Scenario 2: Manual Start**
```
User opens Settings → Platforms → Kick
                    → Clicks "Start Chat" button
                    → Initialize WebView2 (if not already)
                    → Navigate to kick.com
                    → Start chat
                    → Status: "Connected ●"
```

**Scenario 3: Manual Stop**
```
User clicks "Stop Chat" → Close WebSocket (keep WebView2 alive)
                        → Status: "Disconnected ○"
                        → User can restart anytime
```

**Scenario 4: Chat Disabled**
```
User unchecks "Enable Kick Chat" → Stop chat (if running)
                                  → Dispose WebView2 (if initialized)
                                  → No resources consumed
                                  → Webhooks still work (send-only)
```

#### 4.3 Start/Stop Methods (User-Controlled)

**StartAsync** (called by user action or auto-start):
```csharp
public async Task StartAsync(string username, string bearerToken)
{
    if (_isRunning)
    {
        _logger.LogWarning("Chat already running");
        return;
    }
    
    if (!_isInitialized)
    {
        _logger.LogInformation("WebView2 not initialized, initializing now...");
        await InitializeAsync();
    }
    
    _logger.LogInformation("Starting Kick chat for: {Username}", username);
    
    await _dispatcher.InvokeAsync(async () =>
    {
        // Navigate to kick.com (if not already there)
        _webView!.CoreWebView2.Navigate($"https://kick.com/{username}");
        
        // Wait for page load
        await Task.Delay(2000);
        
        // Get channel info via fetch
        var channelInfo = await ExecuteFetchAsync<KickChannelInfo>(
            $"https://kick.com/api/v2/channels/{username.ToLower()}"
        );
        
        // Start Pusher WebSocket
        await InitializePusherWebSocketAsync(channelInfo.Chatroom.Id.ToString());
        
        _isRunning = true;
        ConnectionStateChanged?.Invoke(this, true);
        
        _logger.LogInformation("✅ Kick chat started");
    });
}
```

**StopAsync** (called by user action or app shutdown):
```csharp
public async Task StopAsync()
{
    if (!_isRunning) return;
    
    _logger.LogInformation("Stopping Kick chat...");
    
    await _dispatcher.InvokeAsync(async () =>
    {
        // Close WebSocket via JavaScript
        await _webView!.ExecuteScriptAsync(@"
            if (window.kickPusher && window.kickPusher.socket) {
                window.kickPusher.socket.close();
                window.kickPusher = null;
            }
        ");
        
        _isRunning = false;
        ConnectionStateChanged?.Invoke(this, false);
        
        _logger.LogInformation("✅ Kick chat stopped");
    });
    
    // Keep WebView2 alive for quick restart
}
```

#### 4.4 Disposal (App Shutdown Only)
```csharp
public void Dispose()
{
    if (_disposed) return;
    
    _logger.LogInformation("Disposing Kick chat WebView2...");
    
    _dispatcher.Invoke(() =>
    {
        if (_webView != null)
        {
            // Clean up event handlers
            _webView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
            
            // Dispose WebView2
            _webView.Dispose();
            _webView = null;
        }
    });
    
    _disposed = true;
    _logger.LogInformation("Kick chat WebView2 disposed");
}
```
        
        // Disconnect WebSocket
        Application.Current.Dispatcher.Invoke(() =>
        {
            _webView.ExecuteScriptAsync("window.kickPusher?.disconnect();");
            
            // Remove from UI tree
            var parent = _webView.Parent as Panel;
            parent?.Children.Remove(_webView);
            
            _webView.Dispose();
        });
        
        _webView = null;
        _logger.LogInformation("Kick WebView2 disposed");
    }
}
```

**App Shutdown Hook**:
```csharp
protected override void OnExit(ExitEventArgs e)
{
    _serviceProvider.GetService<KickWebView2Service>()?.Dispose();
    base.OnExit(e);
}
```

### Phase 5: Bearer Token Integration

#### 5.1 Token Storage
- Load Bearer token from `kick_credentials.json` or database
- Store in `KickWebView2Service` instance
- Inject into fetch() headers for authenticated requests

#### 5.2 Token Refresh
If token expires (401 Unauthorized response):
- Call existing `KickOAuthService.RefreshTokenAsync()`
- Update stored token in `KickWebView2Service`
- Retry failed request

```csharp
private async Task<T> FetchWithRetryAsync<T>(string url, string method = "GET", object body = null)
{
    try
    {
        return await FetchJsonAsync<T>(url, method, body);
    }
    catch (UnauthorizedException)
    {
        _logger.LogWarning("Token expired, refreshing...");
        
        var newToken = await _oauthService.RefreshTokenAsync();
        _bearerToken = newToken;
        
        return await FetchJsonAsync<T>(url, method, body);
    }
}
```

### Phase 6: Error Handling & Resilience

#### 6.1 Cloudflare Detection
Monitor NavigationCompleted events for Cloudflare challenge pages:
- If page title contains "Just a moment" → Still solving challenge
- Wait and retry
- Max timeout: 30 seconds

#### 6.2 WebSocket Reconnection
If Pusher WebSocket disconnects:
- Log disconnection event
- Attempt reconnection after 5 seconds
- Exponential backoff (5s, 10s, 20s, 40s)
- Max retries: 5

```javascript
window.kickPusher.reconnect = function() {
    if (this.reconnectAttempts >= 5) {
        window.chrome.webview.postMessage({
            type: 'pusher_max_retries'
        });
        return;
    }
    
    const delay = Math.pow(2, this.reconnectAttempts) * 5000;
    setTimeout(() => {
        this.reconnectAttempts++;
        this.connect();
    }, delay);
};
```

#### 6.3 JavaScript Errors
Wrap all ExecuteScriptAsync calls in try-catch:
```csharp
try
{
    var result = await _webView.ExecuteScriptAsync(jsCode);
    return result;
}
catch (Exception ex)
{
    _logger.LogError(ex, "JavaScript execution failed");
    throw;
}
```

### Phase 7: Testing Strategy

#### 7.1 Unit Tests
- Mock WebView2 responses
- Test JSON serialization/deserialization
- Test token refresh logic
- Test Start/Stop state transitions

#### 7.2 Integration Tests
- Test with real Kick account
- Verify chat messages received
- Verify API calls return correct data
- Test reconnection after network loss
- Test Start/Stop button functionality
- Test auto-start on launch

#### 7.3 Manual Testing Checklist

**Initialization Tests**:
- [ ] App starts with Kick configured + Chat Enabled + Auto-Start ON → WebView2 auto-starts
- [ ] App starts with Kick configured + Chat Enabled + Auto-Start OFF → WebView2 NOT started
- [ ] App starts with Kick NOT configured → WebView2 NOT created
- [ ] App starts with Chat Disabled → WebView2 NOT created

**User Control Tests**:
- [ ] User clicks "Start Chat" → Chat connects successfully
- [ ] User clicks "Stop Chat" → Chat disconnects (WebView2 stays alive)
- [ ] User clicks "Start Chat" again → Chat reconnects quickly (WebView2 reused)
- [ ] User unchecks "Enable Chat" while running → Chat stops + WebView2 disposed
- [ ] User checks "Enable Chat" → WebView2 can be manually started

**Functionality Tests**:
- [ ] Channel info retrieved successfully via WebView2 fetch
- [ ] Chat history loads via WebView2 fetch
- [ ] Realtime chat messages received via WebView2 WebSocket
- [ ] Webhooks still work (followers, subs, etc.) → Ngrok still needed
- [ ] Multiple chat messages received in sequence

**Lifecycle Tests**:
- [ ] App closes cleanly → WebView2 disposed
- [ ] App reopens with auto-start → Chat auto-connects
- [ ] Network disconnect → Reconnection logic triggers
- [ ] WebView2 instance separate from setup wizards (no conflicts)

**Settings UI Tests**:
- [ ] "Enable Kick Chat" toggle saves to database
- [ ] "Auto-start on launch" toggle saves to database
- [ ] Start/Stop button enabled when Kick configured
- [ ] Status indicator updates correctly (Connected/Disconnected)
- [ ] Button text changes correctly (Start Chat → Stop Chat)
- [ ] Token refresh works when expired
- [ ] Reconnection after disconnect

## Migration Checklist

### Prerequisites
- [ ] Install `Microsoft.Web.WebView2` NuGet package to `VyralBot.Integrations`
- [ ] Ensure .NET 9.0 WPF project references WebView2

### Implementation Order

**Phase 1: Service Foundation + UI (3-4 hours)**
1. [ ] Create `KickChatWebView2Service.cs` skeleton
2. [ ] Implement `InitializeAsync()` with conditional check
3. [ ] Implement `StartAsync()` and `StopAsync()` methods
4. [ ] Implement `Dispose()` logic
5. [ ] Add `MessageReceived` and `ConnectionStateChanged` events
6. [ ] Update `KickSettingsViewModel` with new properties and commands
7. [ ] Update `KickSettingsView.xaml` with UI controls
8. [ ] Create value converters for button text and status indicators
9. [ ] Add database fields for `KickChatEnabled` and `KickChatAutoStart`

**Phase 2: JavaScript Bridge (2-3 hours)**
10. [ ] Implement REST API fetch bridge (`ExecuteFetchAsync<T>()`)
11. [ ] Implement Pusher WebSocket bridge (`InitializePusherWebSocketAsync()`)
12. [ ] Create JavaScript code for fetch() with Bearer token
13. [ ] Create JavaScript WebSocket code with `postMessage`
14. [ ] Implement `CoreWebView2_WebMessageReceived` handler

**Phase 3: Integration (3-4 hours)**
15. [ ] Modify `KickService.cs` to inject `KickChatWebView2Service`
16. [ ] Replace `GetChannelInfoAsync()` with WebView2 fetch
17. [ ] Remove PusherClient library code
18. [ ] Subscribe to `MessageReceived` event
19. [ ] Implement `OnWebView2MessageReceived()` handler
20. [ ] Modify `KickApiService.cs` to inject `KickChatWebView2Service`
21. [ ] Replace `GetChatHistoryAsync()` with WebView2 fetch
22. [ ] Update DI registration in `App.xaml.cs`

**Phase 4: Lifecycle Management (1-2 hours)**
23. [ ] Add auto-start logic in `App.xaml.cs` startup
24. [ ] Add disposal hook in `App.xaml.cs` → `OnExit()`
25. [ ] Test conditional initialization (Kick configured vs not configured)
26. [ ] Test manual start/stop via UI

**Phase 5: Bearer Token Integration (1 hour)**
27. [ ] Load Bearer token on `StartAsync()`
28. [ ] Inject token into fetch() headers
29. [ ] Implement token refresh on 401 response

**Phase 6: Error Handling (1-2 hours)**
30. [ ] Add Cloudflare challenge detection
31. [ ] Implement WebSocket reconnection logic (exponential backoff)
32. [ ] Add JavaScript error logging
33. [ ] Add try-catch for all `ExecuteScriptAsync` calls

**Phase 7: Testing (2-3 hours)**
34. [ ] Test auto-start on launch
35. [ ] Test manual start/stop via UI
36. [ ] Test chat messages received
37. [ ] Test API calls successful
38. [ ] Test disposal on app exit
39. [ ] Test webhooks still work independently
40. [ ] Test reconnection after disconnect

### Cleanup
41. [ ] Remove PusherClient NuGet package (if not used elsewhere)
42. [ ] Remove old HttpClient code from `KickService.cs`
43. [ ] Remove old HttpClient code from `KickApiService.cs`
44. [ ] Update documentation (README, architecture docs)
45. [ ] Commit changes with detailed description

### Rollback Plan
If WebView2 approach fails:
- Keep existing `KickService.cs` and `KickApiService.cs` as fallback
- Feature flag to toggle between HttpClient and WebView2
- Gradual migration: Start with REST API, then add WebSocket

## Success Criteria

✅ **Must Have**:
- WebView2 initializes only when Kick account configured
- Channel info API call succeeds (no 403 Forbidden)
- Chat history loads successfully
- Realtime chat messages received
- WebView2 disposes cleanly on app exit

✅ **Nice to Have**:
- Automatic token refresh
- Reconnection on disconnect
- Performance monitoring (memory usage)

## Potential Issues & Solutions

### Issue 1: WebView2 Runtime Not Installed
**Solution**: Check for WebView2 runtime on startup, show error message with download link

### Issue 2: UI Thread Blocking
**Solution**: All WebView2 operations must be async/await, use Dispatcher.InvokeAsync

### Issue 3: Memory Leaks
**Solution**: Properly dispose WebView2, remove event handlers, null references

### Issue 4: Hidden WebView2 Still Consumes Resources
**Solution**: Monitor memory usage, consider lazy initialization (only create when needed)

### Issue 5: JavaScript Bridge Fragility
**Solution**: Add timeout to ExecuteScriptAsync, validate JSON responses, log all errors

## Timeline Estimate

- **Phase 1-2**: 4-6 hours (service foundation + JavaScript bridge)
- **Phase 3**: 2-3 hours (integrate with existing code)
- **Phase 4**: 1-2 hours (lifecycle management)
- **Phase 5**: 1 hour (token integration)
- **Phase 6**: 2-3 hours (error handling)
- **Phase 7**: 2-4 hours (testing)

**Total**: ~12-19 hours of development + testing

## Notes

- WebView2 uses ~50-100MB memory when idle (acceptable for desktop app)
- First navigation to kick.com may take 2-5 seconds (Cloudflare challenge)
- All API calls will be slightly slower than HttpClient (overhead of JavaScript execution)
- Benefits outweigh costs: **Cloudflare bypass is critical for functionality**
