# VyralBot Kick Integration - Complete Audit & Implementation Plan

**Date:** December 5, 2025  
**Auditor:** GitHub Copilot  
**Project:** VyralBot  
**Webhook URL:** `https://fructuous-precious-plunderable.ngrok-free.dev/kick/webhook`

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [Current State Analysis](#current-state-analysis)
3. [Architecture Overview](#architecture-overview)
4. [What's Working](#whats-working)
5. [Critical Issues](#critical-issues)
6. [Implementation Gaps](#implementation-gaps)
7. [Phase-by-Phase Implementation Plan](#phase-by-phase-implementation-plan)
8. [Testing Strategy](#testing-strategy)
9. [Configuration Guide](#configuration-guide)

---

## Executive Summary

### Current Status

✅ **COMPLETE:**
- OAuth 2.1 authentication with PKCE
- Kick setup wizard (KickWebSetupViewModel)
- Message sending (via REST API)
- Webhook infrastructure (VyralBot.Api)
- Ngrok tunnel management
- Webhook signature validation
- Webhook subscription management
- Database schema for webhook tracking

🔄 **PARTIALLY WORKING:**
- Webhook message receiving (infrastructure complete, event routing incomplete)
- Chat display (receives webhook events but handler may need updates)

❌ **NOT IMPLEMENTED:**
- Pusher WebSocket connection (deprecated, should be removed)
- Complete event routing from webhook to ChatViewModel
- KickChatMessageEvent handling in ChatViewModel
- Proper badge parsing and display
- Emote rendering in chat

---

## Current State Analysis

### Chat System Flow

#### Twitch (Working)

```
1. TwitchService connects to IRC WebSocket
2. Receives PRIVMSG events
3. Publishes TwitchChatMessageEvent to EventBus
4. ChatViewModel subscribes to event
5. Adds message to ObservableCollection<ChatMessage>
6. UI renders message
```

#### Kick (Current Implementation)

```
Phase 1: Message Sending (✅ WORKING)
1. User types message in ChatViewModel
2. ChatViewModel.SendMessageAsync() called
3. KickService.SendMessageAsync() posts to Kick API
4. Message appears in Kick channel

Phase 2: Message Receiving (🔄 PARTIALLY WORKING)
1. Kick sends webhook to ngrok URL
2. WebhooksController.KickWebhook() receives POST
3. Parses JSON payload
4. Creates KickChatMessageEvent
5. Publishes to EventBus via _eventBus.PublishAsync()
6. ??? (Gap: ChatViewModel subscription may not be active)
7. ??? (Gap: OnKickChatMessageReceivedAsync may not be called)
```

---

## Architecture Overview

### Component Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                         VyralBot.UI                              │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ ChatViewModel                                            │   │
│  │ • Subscribes to KickChatMessageEvent                     │   │
│  │ • OnKickChatMessageReceivedAsync()                       │   │
│  │ • Adds message to Messages collection                    │   │
│  └──────────────────────────────────────────────────────────┘   │
│                           │                                      │
│                           ▼                                      │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ EventBus (In-Memory)                                     │   │
│  │ • Pub/Sub pattern                                        │   │
│  │ • KickChatMessageEvent published here                    │   │
│  └──────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
                           ▲
                           │ Publishes KickChatMessageEvent
                           │
┌─────────────────────────────────────────────────────────────────┐
│                        VyralBot.Api                              │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ WebhooksController                                       │   │
│  │ • POST /webhooks/kick                                    │   │
│  │ • ProcessChatMessage(jsonPayload)                        │   │
│  │ • Creates KickChatMessageEvent                           │   │
│  │ • await _eventBus.PublishAsync(event)                    │   │
│  └──────────────────────────────────────────────────────────┘   │
│                           ▲                                      │
│                           │ HTTP POST                            │
│                           │                                      │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ NgrokService                                             │   │
│  │ • Manages ngrok tunnel                                   │   │
│  │ • Public URL: https://{random}.ngrok-free.dev            │   │
│  │ • Auto-reconnect on URL change                           │   │
│  └──────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
                           ▲
                           │ Webhook POST
                           │
┌─────────────────────────────────────────────────────────────────┐
│                       Kick.com Servers                           │
│  • Sends chat.message.sent events                               │
│  • Includes signature for verification                           │
│  • Retry logic (3 attempts)                                      │
└─────────────────────────────────────────────────────────────────┘
```

---

## What's Working

### ✅ Complete Features

#### 1. Webhook Infrastructure
**Files:**
- `VyralBot.Api/Services/WebServerService.cs` - Kestrel web server
- `VyralBot.Api/Services/NgrokService.cs` - Tunnel management
- `VyralBot.Api/Controllers/WebhooksController.cs` - Webhook receiver
- `VyralBot.Api/Middleware/KickWebhookValidationMiddleware.cs` - Signature verification

**Status:** FULLY FUNCTIONAL
- Web server runs on port 5000
- Ngrok tunnel exposes public URL
- Webhook endpoint receives Kick events
- Signature validation working

#### 2. OAuth Authentication
**Files:**
- `VyralBot.Integrations/Kick/KickOAuthService.cs`
- `VyralBot.UI/ViewModels/KickWebSetupViewModel.cs`
- `VyralBot.UI/Views/KickWebSetupView.xaml`

**Status:** FULLY FUNCTIONAL
- 4-step setup wizard
- PKCE implementation
- Token exchange
- Credential storage

#### 3. Message Sending
**Files:**
- `VyralBot.Integrations/Kick/KickService.cs` - SendMessageAsync()

**Status:** FULLY FUNCTIONAL
- REST API call to POST /chat
- Sends messages successfully
- Bot messages appear in Kick chat

#### 4. Webhook Subscription Management
**Files:**
- `VyralBot.Api/Services/KickWebhookSubscriptionService.cs`
- `VyralBot.Api/Services/WebhookCoordinatorService.cs`

**Status:** FULLY FUNCTIONAL
- Subscribe to events
- Auto-resubscribe on ngrok URL change
- Database tracking of subscriptions

---

## Critical Issues

### 🚨 Issue #1: EventBus Cross-Process Communication

**Problem:** `VyralBot.Api` (ASP.NET Core) and `VyralBot.UI` (WPF) are **separate processes**

**Current Code Assumption:**
```csharp
// In WebhooksController.cs (VyralBot.Api process)
await _eventBus.PublishAsync(kickEvent);  // ❌ Only publishes to Api process

// In ChatViewModel.cs (VyralBot.UI process)
await _eventBus.SubscribeAsync<KickChatMessageEvent>(handler);  // ❌ Different process!
```

**Why It Fails:**
- EventBus uses in-memory pub/sub
- Each process has its own memory space
- Events published in Api never reach UI

**Evidence:**
```
VyralBot.exe (WPF UI) - Process ID: 12345
    └── Has its own EventBus instance

VyralBot.Api.exe (ASP.NET) - Process ID 67890
    └── Has its own EventBus instance (separate!)
```

**Impact:** Webhook events are received and parsed, but **never reach ChatViewModel**

---

### 🚨 Issue #2: Obsolete Pusher WebSocket Code

**Problem:** `KickService.cs` contains Pusher WebSocket code that is:
1. Not documented in official Kick API (docs.kick.com)
2. No longer functional
3. Redundant now that webhooks are implemented

**Dead Code:**
```csharp
// In KickService.cs
var options = new PusherOptions { Cluster = "us2", Encrypted = true };
_pusher = new Pusher("eb1d5f283081a78b932c", options);
await _pusher.ConnectAsync();
_channel = await _pusher.SubscribeAsync($"chatrooms.{_chatroomId}.v2");
_channel.Bind("App\\Events\\ChatMessageEvent", OnChatMessage);
```

**Why It's Dead:**
- Official Kick API only documents webhooks (not Pusher)
- Pusher app key may be revoked/changed
- WebSocket connection is unnecessary overhead

**Impact:**
- Confusing codebase
- False sense of "receiving messages" via Pusher
- Increased memory usage (maintains WebSocket connection)

---

### 🚨 Issue #3: Database Schema Missing Fields

**Problem:** `WebhookSubscription` table doesn't track all necessary metadata

**Current Schema:**
```sql
CREATE TABLE WebhookSubscriptions (
    Id TEXT PRIMARY KEY,
    Platform TEXT NOT NULL,
    EventType TEXT NOT NULL,
    EventVersion INTEGER DEFAULT 1,
    BroadcasterUserId INTEGER,
    WebhookUrl TEXT NOT NULL,
    IsActive INTEGER DEFAULT 1,
    LastEventReceived TEXT,
    CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP
);
```

**Missing:**
- `ProfileId` - Which profile owns this subscription?
- `AppId` - Which Kick app owns this subscription?
- `SubscriptionMetadata` - JSON field for extensibility

**Impact:** Subscriptions not properly scoped to profiles

---

## Implementation Gaps

### Gap #1: Inter-Process Communication

**Current:** EventBus is in-memory (process-local)

**Needed:** Cross-process event delivery

**Options:**

#### Option A: Named Pipes (Recommended)
```csharp
// VyralBot.Api sends event via Named Pipe
var pipeClient = new NamedPipeClientStream(".", "VyralBotEvents", PipeDirection.Out);
await pipeClient.ConnectAsync();
await JsonSerializer.SerializeAsync(pipeClient, kickEvent);

// VyralBot.UI listens on Named Pipe
var pipeServer = new NamedPipeServerStream("VyralBotEvents", PipeDirection.In);
await pipeServer.WaitForConnectionAsync();
var kickEvent = await JsonSerializer.DeserializeAsync<KickChatMessageEvent>(pipeServer);
await _eventBus.PublishAsync(kickEvent);  // Now in UI process!
```

**Pros:**
- Fast (inter-process communication)
- Simple implementation
- Windows native

**Cons:**
- Windows-only
- Requires pipe management

---

#### Option B: HTTP Callback (Simple)
```csharp
// VyralBot.Api forwards event to UI
var httpClient = new HttpClient();
await httpClient.PostAsJsonAsync("http://localhost:5001/events/kick", kickEvent);

// VyralBot.UI adds minimal HTTP endpoint
[HttpPost("events/kick")]
public async Task<IActionResult> ReceiveKickEvent([FromBody] KickChatMessageEvent evt)
{
    await _eventBus.PublishAsync(evt);
    return Ok();
}
```

**Pros:**
- Very simple
- Cross-platform
- Familiar HTTP pattern

**Cons:**
- Requires UI to run web server (port conflict risk)
- Slower than named pipes

---

#### Option C: Shared Memory Queue (Advanced)
```csharp
// Use Memory-Mapped Files
var mmf = MemoryMappedFile.CreateOrOpen("VyralBotEvents", 1024 * 1024);
```

**Pros:**
- Fastest option
- Low overhead

**Cons:**
- Complex implementation
- Manual serialization/synchronization

---

#### **RECOMMENDED: Option D - Merge into Single Process**

**Proposal:** Embed ASP.NET Core Kestrel into WPF app

```csharp
// In VyralBot.UI/App.xaml.cs
protected override async void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    
    // Build host with both WPF and ASP.NET Core
    _host = Host.CreateDefaultBuilder()
        .ConfigureWebHostDefaults(webBuilder =>
        {
            webBuilder.UseKestrel();
            webBuilder.UseUrls("http://localhost:5000");
            webBuilder.UseStartup<ApiStartup>();
        })
        .ConfigureServices(services =>
        {
            // Register UI services
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<ChatViewModel>();
            // ... etc
        })
        .Build();
    
    await _host.StartAsync();
}
```

**Pros:**
- ✅ Single process (no IPC needed!)
- ✅ EventBus works natively
- ✅ Simpler deployment
- ✅ Shared DI container

**Cons:**
- None (this is the optimal solution)

---

### Gap #2: ChatViewModel Subscription Timing

**Problem:** ChatViewModel may not be subscribed when webhook arrives

**Current Code:**
```csharp
public async Task InitializeAsync()
{
    if (_isInitialized) return;  // ❌ Early return prevents subscription
    
    _kickSubscriptionId = await _eventBus.SubscribeAsync<KickChatMessageEvent>(
        OnKickChatMessageReceivedAsync);
    
    _isInitialized = true;
}
```

**Issue:** If user navigates away from Chat tab, ChatViewModel might be disposed/deactivated

**Solution:** Make ChatViewModel a **singleton** (already done) and ensure subscription is **always active**

---

### Gap #3: Webhook Event Not Found in ChatViewModel

**Current Handler:**
```csharp
private async Task OnKickChatMessageReceivedAsync(KickChatMessageEvent evt)
{
    // ❌ This method exists but may never be called if EventBus is process-isolated
}
```

**Verification Needed:**
1. Is `ChatViewModel.InitializeAsync()` called?
2. Is subscription ID stored?
3. Is EventBus the same instance?

---

## Phase-by-Phase Implementation Plan

---

### **PHASE 1: Fix Inter-Process Communication (CRITICAL)**

**Goal:** Enable events to flow from VyralBot.Api → VyralBot.UI

**Option 1A: Merge Processes (RECOMMENDED)**

**Steps:**
1. Move ASP.NET Core hosting into `VyralBot.UI/App.xaml.cs`
2. Reference `VyralBot.Api` project from UI
3. Share EventBus singleton across both
4. Remove separate `VyralBot.Api.exe` startup

**Files to Modify:**
- `VyralBot.UI/App.xaml.cs` - Add Kestrel hosting
- `VyralBot.UI/VyralBot.UI.csproj` - Add `<ProjectReference>` to VyralBot.Api
- Remove `VyralBot.Api/Program.cs` (or make it a library)

**Code Example:**
```csharp
// VyralBot.UI/App.xaml.cs
public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseKestrel();
                webBuilder.ConfigureKestrel(options =>
                {
                    options.ListenLocalhost(5000);  // Webhook receiver port
                });
                webBuilder.ConfigureServices(services =>
                {
                    // Register API services
                    services.AddControllers();
                    services.AddSingleton<WebServerService>();
                    services.AddSingleton<NgrokService>();
                    services.AddSingleton<KickWebhookSubscriptionService>();
                    services.AddSingleton<WebhookCoordinatorService>();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseMiddleware<KickWebhookValidationMiddleware>();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapControllers();
                    });
                });
            })
            .ConfigureServices(services =>
            {
                // Register UI services
                services.AddSingleton<IEventBus, EventBus>();  // ✅ Shared EventBus!
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<ChatViewModel>();
                services.AddSingleton<IKickService, KickService>();
                services.AddSingleton<ITwitchService, TwitchService>();
                // ... rest of DI registrations
            })
            .Build();

        await _host.StartAsync();

        // Show main window
        var mainWindow = new MainWindow
        {
            DataContext = _host.Services.GetRequiredService<MainViewModel>()
        };
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await _host?.StopAsync()!;
        _host?.Dispose();
        base.OnExit(e);
    }
}
```

**Testing:**
1. Run VyralBot.exe
2. Send test message in Kick channel
3. Verify webhook received in logs
4. Verify `OnKickChatMessageReceivedAsync` called
5. Verify message appears in ChatViewModel

**Success Criteria:**
✅ Single process runs both WPF and ASP.NET Core  
✅ Webhook events reach ChatViewModel  
✅ Messages display in chat UI

---

**Option 1B: Named Pipe (Alternative)**

**If merging processes is not feasible:**

**Steps:**
1. Create `NamedPipeEventBridge` service
2. Api writes events to pipe
3. UI reads events from pipe and publishes to local EventBus

**Files to Create:**
- `VyralBot.Core/Services/NamedPipeEventBridge.cs`

**Code Example:**
```csharp
// VyralBot.Core/Services/NamedPipeEventBridge.cs
public class NamedPipeEventBridge
{
    private const string PipeName = "VyralBotEvents";
    private NamedPipeServerStream? _pipeServer;
    private readonly IEventBus _eventBus;
    private readonly ILogger<NamedPipeEventBridge> _logger;

    public async Task StartServerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            _pipeServer = new NamedPipeServerStream(PipeName, PipeDirection.In);
            await _pipeServer.WaitForConnectionAsync(cancellationToken);

            using var reader = new StreamReader(_pipeServer);
            var json = await reader.ReadToEndAsync();
            
            var envelope = JsonSerializer.Deserialize<EventEnvelope>(json);
            
            // Deserialize actual event based on type
            var eventType = Type.GetType(envelope.EventTypeName);
            var evt = JsonSerializer.Deserialize(envelope.Payload, eventType);
            
            await _eventBus.PublishAsync(evt);
        }
    }

    public async Task SendAsync<T>(T evt)
    {
        using var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
        await pipeClient.ConnectAsync(5000);  // 5 second timeout
        
        var envelope = new EventEnvelope
        {
            EventTypeName = typeof(T).AssemblyQualifiedName,
            Payload = JsonSerializer.Serialize(evt)
        };
        
        using var writer = new StreamWriter(pipeClient);
        await writer.WriteAsync(JsonSerializer.Serialize(envelope));
    }
}

public class EventEnvelope
{
    public string EventTypeName { get; set; }
    public string Payload { get; set; }
}
```

**In WebhooksController:**
```csharp
// Replace
await _eventBus.PublishAsync(kickEvent);

// With
await _namedPipeBridge.SendAsync(kickEvent);
```

**In ChatViewModel:**
```csharp
// No changes needed - still subscribes to local EventBus
await _eventBus.SubscribeAsync<KickChatMessageEvent>(OnKickChatMessageReceivedAsync);
```

---

### **PHASE 2: Remove Obsolete Pusher Code**

**Goal:** Clean up KickService, remove non-functional WebSocket code

**Files to Modify:**
- `VyralBot.Integrations/Kick/KickService.cs`
- `VyralBot.Integrations/VyralBot.Integrations.csproj` - Remove PusherClient NuGet package

**Steps:**

1. **Remove Pusher fields:**
```csharp
// DELETE these fields
private Pusher? _pusher;
private Channel? _channel;
private string? _chatroomId;
```

2. **Simplify ConnectAsync:**
```csharp
public async Task ConnectAsync(string username, string accessToken, string channelName, int broadcasterUserId)
{
    _logger.LogInformation("Setting up Kick connection for {Username} in channel {Channel}", 
        username, channelName);

    _botUsername = username;
    _currentChannel = channelName;
    _accessToken = accessToken;
    _broadcasterUserId = broadcasterUserId;

    // No longer need WebSocket connection - messages come via webhooks
    IsConnected = true;
    
    _logger.LogInformation("Kick connection configured (messages received via webhooks)");
}
```

3. **Remove OnChatMessage handler:**
```csharp
// DELETE this entire method
private void OnChatMessage(string data) { ... }
```

4. **Update DisconnectAsync:**
```csharp
public async Task DisconnectAsync()
{
    _logger.LogInformation("Disconnecting Kick");
    
    // No Pusher cleanup needed anymore
    IsConnected = false;
    
    await Task.CompletedTask;
}
```

**Testing:**
1. Connect to Kick via settings
2. Verify `IsConnected = true`
3. Send message - should work
4. Receive webhook message - should work
5. Verify no Pusher errors in logs

---

### **PHASE 3: Enhance ChatViewModel Message Handler**

**Goal:** Properly display all Kick message metadata (badges, emotes, colors)

**Files to Modify:**
- `VyralBot.UI/ViewModels/ChatViewModel.cs`
- `VyralBot.UI/Models/ChatMessage.cs` (if needed)

**Current Handler (Incomplete):**
```csharp
private async Task OnKickChatMessageReceivedAsync(KickChatMessageEvent evt)
{
    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
    {
        Messages.Add(new ChatMessage
        {
            Platform = "Kick",
            Username = evt.DisplayName ?? evt.Username,
            Message = evt.Message,
            Timestamp = evt.Timestamp,
            Color = evt.Color ?? "#00FF00",  // ❌ Missing null handling
            IsModerator = evt.IsModerator,
            IsSubscriber = evt.IsSubscriber,
            // ❌ Missing: IsOwner, IsVerified, Emotes, Badges
        });
    });
}
```

**Enhanced Handler:**
```csharp
private async Task OnKickChatMessageReceivedAsync(KickChatMessageEvent evt)
{
    _logger.LogInformation("ChatViewModel received Kick message from {Username}: {Message}", 
        evt.Username, evt.Message);
    
    // Refresh platforms list if needed
    if (ConnectedPlatforms.Count == 0)
    {
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            UpdateConnectedPlatforms();
        });
    }
    
    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
    {
        var chatMessage = new ChatMessage
        {
            Platform = "Kick",
            Username = evt.DisplayName ?? evt.Username,
            Message = evt.Message,
            Timestamp = evt.Timestamp,
            Color = evt.Color ?? "#53FC18",  // Kick green default
            IsModerator = evt.IsModerator,
            IsSubscriber = evt.IsSubscriber,
            IsVip = false,  // Kick doesn't have VIP
            IsBroadcaster = evt.IsOwner,
            Badges = evt.Badges ?? new Dictionary<string, string>(),
            MessageId = evt.MessageId,
            // Future: Emotes, ReplyTo
        };
        
        Messages.Add(chatMessage);
        
        _logger.LogInformation("Added Kick message to UI. Total messages: {Count}", Messages.Count);
        
        // Keep only last 500 messages
        while (Messages.Count > 500)
        {
            Messages.RemoveAt(0);
        }
    });
}
```

**Update ChatMessage Model (if needed):**
```csharp
public class ChatMessage
{
    public string Platform { get; set; }  // "Twitch", "Kick", "YouTube", etc.
    public string Username { get; set; }
    public string Message { get; set; }
    public DateTime Timestamp { get; set; }
    public string Color { get; set; } = "#FFFFFF";
    public bool IsModerator { get; set; }
    public bool IsSubscriber { get; set; }
    public bool IsVip { get; set; }
    public bool IsBroadcaster { get; set; }
    public Dictionary<string, string> Badges { get; set; } = new();
    
    // Kick-specific
    public string? MessageId { get; set; }
    public bool IsVerified { get; set; }
    public int? SubscriberMonths { get; set; }
    
    // Future
    public List<Emote>? Emotes { get; set; }
    public ReplyInfo? ReplyTo { get; set; }
}
```

---

### **PHASE 4: Database Schema Updates**

**Goal:** Track webhook subscriptions per profile

**Files to Modify:**
- Database migration script
- `VyralBot.Core/Models/WebhookSubscription.cs`

**Migration SQL:**
```sql
ALTER TABLE WebhookSubscriptions ADD COLUMN ProfileId TEXT;
ALTER TABLE WebhookSubscriptions ADD COLUMN AppId TEXT;
ALTER TABLE WebhookSubscriptions ADD COLUMN SubscriptionMetadata TEXT;  -- JSON

UPDATE WebhookSubscriptions SET ProfileId = (SELECT Id FROM Profiles LIMIT 1) WHERE ProfileId IS NULL;
```

**Update Model:**
```csharp
public class WebhookSubscription
{
    public string Id { get; set; }  // Kick subscription ID (ULID)
    public string Platform { get; set; }  // "Kick"
    public string EventType { get; set; }  // "chat.message.sent"
    public int EventVersion { get; set; } = 1;
    public string? BroadcasterUserId { get; set; }
    public string WebhookUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastEventReceived { get; set; }
    public DateTime CreatedAt { get; set; }
    
    // NEW FIELDS
    public string ProfileId { get; set; }  // Foreign key to Profiles
    public string? AppId { get; set; }  // Kick app ID
    public string? SubscriptionMetadata { get; set; }  // JSON for future extensibility
}
```

---

### **PHASE 5: Error Handling & Resilience**

**Goal:** Gracefully handle webhook errors, connection failures, token expiration

**Areas to Enhance:**

#### 5.1 Webhook Signature Validation Failures
```csharp
// In KickWebhookValidationMiddleware.cs
if (!isValid)
{
    _logger.LogWarning("Invalid webhook signature. MessageId: {MessageId}", messageId);
    
    // Store failed webhook attempt in database for debugging
    await _dbContext.FailedWebhooks.AddAsync(new FailedWebhook
    {
        MessageId = messageId,
        ReceivedAt = DateTime.UtcNow,
        Signature = signature,
        Body = body,
        Reason = "Invalid signature"
    });
    
    context.Response.StatusCode = 401;
    return;
}
```

#### 5.2 Token Expiration Handling
```csharp
// In KickService.SendMessageAsync()
public async Task SendMessageAsync(string message)
{
    try
    {
        var response = await _httpClient.PostAsync(...);
        
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Token expired - attempt refresh
            _logger.LogWarning("Kick access token expired, refreshing...");
            var newTokens = await _oAuthService.RefreshTokenAsync(_refreshToken);
            _accessToken = newTokens.AccessToken;
            _refreshToken = newTokens.RefreshToken;
            
            // Retry request
            response = await _httpClient.PostAsync(...);
        }
        
        response.EnsureSuccessStatusCode();
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to send Kick message");
        throw;
    }
}
```

#### 5.3 Webhook Subscription Auto-Recovery
```csharp
// In WebhookCoordinatorService
private async Task MonitorSubscriptionsAsync(CancellationToken cancellationToken)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
        
        // Check if subscriptions are still active on Kick
        var localSubs = await _dataService.GetWebhookSubscriptionsAsync("Kick");
        var kickSubs = await _kickSubscriptionService.GetActiveSubscriptionsAsync();
        
        // Re-subscribe if missing
        foreach (var localSub in localSubs.Where(s => s.IsActive))
        {
            if (!kickSubs.Any(k => k.Id == localSub.Id))
            {
                _logger.LogWarning("Subscription {Id} missing on Kick, resubscribing...", localSub.Id);
                await ResubscribeAsync(localSub);
            }
        }
    }
}
```

---

### **PHASE 6: Testing & Validation**

**Goal:** Comprehensive testing of entire message flow

#### Test Cases

**6.1 End-to-End Message Flow**
```
Test: Send message in Kick channel → Verify appears in VyralBot

Steps:
1. Connect to Kick via VyralBot
2. Open Kick channel in browser
3. Send message: "Test message from browser"
4. Verify within 2 seconds:
   ✅ Webhook received (check logs)
   ✅ Signature validated
   ✅ Event published to EventBus
   ✅ ChatViewModel receives event
   ✅ Message added to Messages collection
   ✅ Message visible in UI
```

**6.2 Webhook Signature Validation**
```
Test: Reject webhooks with invalid signatures

Steps:
1. Send POST to /webhooks/kick with fake signature
2. Verify 401 Unauthorized response
3. Verify error logged
```

**6.3 Token Refresh**
```
Test: Refresh expired access token

Steps:
1. Manually expire access token (set expires_at to past)
2. Send message via VyralBot
3. Verify 401 response triggers refresh
4. Verify new token obtained
5. Verify message send retried successfully
```

**6.4 Ngrok URL Change**
```
Test: Auto-resubscribe when ngrok URL changes

Steps:
1. Start VyralBot with ngrok
2. Note current subscriptions
3. Restart ngrok (new URL)
4. Verify WebhookCoordinator detects change
5. Verify old subscriptions unsubscribed
6. Verify new subscriptions created with new URL
```

**6.5 Badge Display**
```
Test: Moderator, subscriber, verified badges display correctly

Steps:
1. Have moderator send message
2. Verify "MOD" badge appears
3. Have subscriber send message
4. Verify "SUB" badge appears with month count
```

---

## Configuration Guide

### Setup Checklist

#### Step 1: Kick Developer App
- [ ] Create Kick account
- [ ] Enable 2FA
- [ ] Go to https://kick.com/settings/developer
- [ ] Create new application:
  - Name: `VyralBot`
  - Redirect URI: `http://localhost:3000/callback`
- [ ] Save Client ID and Client Secret

#### Step 2: Ngrok Setup
- [ ] Create account at https://ngrok.com
- [ ] Get auth token from dashboard
- [ ] Install ngrok: `choco install ngrok` or download from site
- [ ] Authenticate: `ngrok authtoken <your_token>`

#### Step 3: VyralBot Configuration
- [ ] Run VyralBot
- [ ] Navigate to Settings → Platforms
- [ ] Click "Setup Kick"
- [ ] Follow 4-step wizard:
  1. Sign in to Kick
  2. Enter Client ID + Secret
  3. Authorize app
  4. Test connection

#### Step 4: Webhook Configuration
- [ ] In VyralBot: Settings → API & Webhooks
- [ ] Start Webhook Server (port 5000)
- [ ] Start Ngrok Tunnel
- [ ] Copy public URL (e.g., `https://abc123.ngrok-free.dev`)
- [ ] In Kick Developer Settings:
  - Enable Webhooks: ON
  - Webhook URL: `https://abc123.ngrok-free.dev/webhooks/kick`

#### Step 5: Subscribe to Events
- [ ] In VyralBot: Kick Settings
- [ ] Toggle "Enable Webhooks"
- [ ] Select events:
  - Chat Messages
  - Follows
  - Subscriptions
- [ ] Click "Save"

#### Step 6: Test
- [ ] Send message in Kick channel
- [ ] Verify appears in VyralBot chat within 2 seconds
- [ ] Check logs for webhook received

---

## Current Webhook URL

**Your Public URL:** `https://fructuous-precious-plunderable.ngrok-free.dev`

**Full Webhook Endpoint:** `https://fructuous-precious-plunderable.ngrok-free.dev/kick/webhook`

**⚠️ Note:** This URL changes every time ngrok restarts (free tier). Use auth token to avoid rate limits.

**To Set Auth Token:**
```bash
ngrok authtoken <your_token_here>
```

**In VyralBot Settings:**
- Navigate to: Settings → API & Webhooks → Ngrok Tab
- Enter auth token
- Save
- Restart ngrok tunnel

---

## Recommended Implementation Order

### Week 1: Critical Path (Messages Working)
1. ✅ **Day 1-2:** Phase 1 - Merge processes OR implement Named Pipe bridge
2. ✅ **Day 3:** Test message flow end-to-end
3. ✅ **Day 4:** Phase 2 - Remove Pusher code
4. ✅ **Day 5:** Phase 3 - Enhance ChatViewModel handler

### Week 2: Polish & Resilience
5. ⏳ **Day 1-2:** Phase 4 - Database schema updates
6. ⏳ **Day 3-4:** Phase 5 - Error handling & resilience
7. ⏳ **Day 5:** Phase 6 - Comprehensive testing

### Week 3: Advanced Features (Optional)
8. 📋 Emote rendering
9. 📋 Reply threading
10. 📋 Badge customization
11. 📋 Rate limit handling
12. 📋 Performance optimization

---

## Success Metrics

### Must Have (MVP)
- [ ] Kick messages appear in chat within 2 seconds of send
- [ ] Webhook signature validation working
- [ ] No orphaned processes
- [ ] No memory leaks (<100 MB steady state)
- [ ] Handles ngrok URL changes automatically
- [ ] Token refresh works automatically

### Should Have
- [ ] Badge display (moderator, subscriber, verified)
- [ ] Color coding per user
- [ ] Timestamped messages
- [ ] Handles 100 messages/minute without lag

### Nice to Have
- [ ] Emote rendering
- [ ] Reply threading
- [ ] Message search
- [ ] Export chat logs

---

## Key Contacts & Resources

- **Kick API Docs:** https://docs.kick.com
- **Kick Developer Portal:** https://dev.kick.com
- **Kick Discord:** https://discord.gg/SvyWXP5aWb
- **Kick Support:** developers@kick.com
- **Ngrok Docs:** https://ngrok.com/docs
- **VyralBot Docs:** `docs/features/kick-integration.md`

---

## Next Actions

### Immediate (Do Now)
1. ✅ Choose IPC solution (Recommendation: Merge processes - Option 1A)
2. ✅ Implement Phase 1 (Critical blocker)
3. ✅ Test message flow
4. ✅ Verify ChatViewModel receives events

### This Week
5. ⏳ Remove Pusher code (Phase 2)
6. ⏳ Enhance message display (Phase 3)
7. ⏳ Update database schema (Phase 4)

### Next Week
8. 📋 Implement error handling (Phase 5)
9. 📋 Run full test suite (Phase 6)
10. 📋 Document final implementation

---

**End of Audit & Implementation Plan**

Generated by: GitHub Copilot  
Date: December 5, 2025  
Project: VyralBot - Kick Integration

