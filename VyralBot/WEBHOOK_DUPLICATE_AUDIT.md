# Webhook Duplicate Events Audit Report

## ⚠️ UPDATE: Priority Reversed - Pusher Takes Priority

**Decision Made**: KickLib Pusher WebSocket is now the PRIMARY event source. Webhooks only used for events Pusher doesn't provide.

---

## Executive Summary

**Problem**: Duplicate follow events and auto-responses are occurring.

**Root Cause**: Events were coming through BOTH Pusher WebSocket AND Kick Official Webhooks simultaneously.

**Solution**: Pusher takes priority. Removed duplicate webhook subscriptions. Only subscribe to webhooks for events Pusher doesn't handle.

---

## Event Source Priority (FINAL)

### ✅ Pusher WebSocket (Primary - 9 event types)
- **Chat Messages** - `OnChatMessage`
- **Follows** - `OnFollowersUpdated` ⭐
- **Subscriptions** - `OnSubscription` ⭐
- **Gift Subs** - `OnGiftedSubscription` ⭐
- **Stream Status** - `OnStreamStatusChanged` ⭐
- **Stream Host** - `OnStreamHost`
- **Rewards** - `OnRewardRedeemed`
- **Kicks Gifted** - `OnKicksGifted` ⭐
- **User Bans/Unbans** - `OnUserBanned`, `OnUserUnbanned`

### ✅ Webhooks (Supplemental - 2 event types only)
- **Stream Metadata** - `LivestreamMetadataUpdated` (not provided by Pusher)
- **Moderation Bans** - `ModerationUserBanned` (Pusher only logs, doesn't publish)

### ❌ Removed Webhook Duplicates
- ~~ChannelFollowed~~ (handled by Pusher)
- ~~ChannelSubscriptionNew~~ (handled by Pusher)
- ~~ChannelSubscriptionRenewal~~ (handled by Pusher)
- ~~ChannelSubscriptionGifts~~ (handled by Pusher)
- ~~LivestreamStatusUpdated~~ (handled by Pusher)
- ~~KicksGifted~~ (handled by Pusher)

---

## Benefits of Pusher Priority

✅ **Real-time events** (faster than webhook delivery)  
✅ **Fewer API calls** (only 2 webhook subscriptions vs 8)  
✅ **Simpler architecture** (one primary source)  
✅ **Better reliability** (WebSocket connection vs HTTP callbacks)  
✅ **No duplicate events** (eliminated redundancy)

---

## Known Limitation

**Follow Events**: Pusher's `OnFollowersUpdated` doesn't provide follower username (KickLib limitation)
- `Username` will be "Unknown"
- `UserId` will be "0"  
- `FollowerCount` is accurate

**Workaround Options**:
1. Accept "Unknown" username (recommended - follower count works)
2. Re-enable ChannelFollowed webhook for username (reintroduces duplicates)
3. Request KickLib enhancement

---

## Implementation Complete

### Changes Made:
1. ✅ Re-enabled Pusher follow detection (`KickLibChatService.cs`)
2. ✅ Removed duplicate webhook subscriptions (`WebhookCoordinatorService.cs`)
3. ✅ Added subscription cleanup on startup (prevents accumulation)

### Webhook Subscriptions: 8 → 2
- Before: 8 webhook event subscriptions
- After: 2 webhook event subscriptions (75% reduction)

---

*Last Updated: December 11, 2025*  
*Status: Implemented - Pusher Priority*

## Detailed Analysis

### 1. Dual Event Sources Problem

#### Source #1: KickLib Pusher WebSocket (`KickLibChatService.cs`)

**Line 460-473** - `OnFollowersUpdated` event handler:
```csharp
private void OnFollowersUpdated(object? sender, FollowersUpdatedEventArgs e)
{
    // Publish dedicated Follow event
    var followEvent = new KickFollowEvent
    {
        Source = "Kick",
        Username = "Unknown", // KickLib doesn't provide follower username in this event
        UserId = "0",
        FollowerCount = e.Data.FollowersCount
    };
    _ = _eventBus.PublishAsync(followEvent);
    
    _logger.LogInformation("👥 New follower! Total followers: {Count}", e.Data.FollowersCount);
}
```

**Issue**: This listens to Pusher WebSocket and publishes follow events **WITHOUT username** (KickLib limitation).

#### Source #2: Kick Official Webhooks (`WebhooksController.cs`)

**Line 124-185** - `ProcessFollowAsync` method:
```csharp
private async Task ProcessFollowAsync(JsonElement root, string? webhookMessageId, string? timestamp)
{
    // ... extracts follower username from webhook payload ...
    
    var messageText = $"{followerUsername} followed the channel";
    
    var kickEvent = new KickChatMessageEvent
    {
        Source = "Kick",
        Channel = channel,
        Username = "Vyral.Bot",  // ⚠️ Appears as "Vyral.Bot"
        UserId = followerId,
        Message = messageText,
        DisplayName = "Vyral.Bot",
        // ...
    };
    
    await _eventBus.PublishAsync(kickEvent);
}
```

**Issue**: This processes webhook payloads and publishes follow events as **chat messages from "Vyral.Bot"**.

### 2. Event Processing Flow

When a follow occurs:

1. **Pusher WebSocket** detects follower count increase → Publishes `KickFollowEvent` (no username)
2. **Webhook** receives `channel.followed` → Publishes `KickChatMessageEvent` as "Vyral.Bot"
3. Both trigger `ChatViewModel.OnKickFollowAsync` event handler
4. Auto-reply sent **twice** (once per event)

### 3. Auto-Reply Duplication

#### Location 1: `ChatViewModel.OnKickFollowAsync` (Lines 1164-1245)

```csharp
private async Task OnKickFollowAsync(KickFollowEvent evt)
{
    // ... displays in chat ...
    
    // Auto-reply if enabled
    if (settings.AutoReplyEnabled && !string.IsNullOrEmpty(settings.AutoReplyMessage))
    {
        if (_autoReplyService.ShouldSendReply(evt.Username, "Kick", "Follow"))
        {
            var reply = _autoReplyService.ProcessTemplate(settings.AutoReplyMessage, variables);
            await _kickService.SendMessageAsync(reply);  // ⚠️ Reply #1
        }
    }
}
```

#### Location 2: Same pattern for webhooks

When webhook publishes `KickChatMessageEvent` with follow text, it may also trigger auto-response logic.

**Result**: "Thanks for the follow @username" appears **twice**.

### 4. Username Discrepancy

- **Pusher events**: Published as dedicated event types → Processed → Shows as **broadcaster username** in auto-reply
- **Webhook events**: Published as `KickChatMessageEvent` with Username="Vyral.Bot" → Shows as **"Vyral.Bot"**

This explains why you see:
- `vyral.bot: username followed the channel`
- `broadcaster(username): Thanks for the follow @username`

---

## Subscription Lifecycle Issues

### Problem: No Cleanup on Application Exit

**File**: `WebhookCoordinatorService.cs`

**Current State**:
- Implements `IDisposable` (Line 394)
- Has `Dispose()` method that calls `StopAsync()`
- **BUT**: No guarantee `Dispose()` is called when app closes

**Missing**:
- No `IHostApplicationLifetime` integration
- No graceful shutdown hook
- No `ApplicationExit` handler

**Result**: Webhook subscriptions remain active on Kick servers even after app closes.

### Problem: Multiple Subscriptions Over Time

**Scenario**:
1. User starts app → Subscribes to webhooks
2. User closes app **without cleanup** → Subscriptions remain active
3. User starts app again → Creates **NEW** subscriptions
4. Repeat → Accumulates **multiple subscriptions** for same events

**Evidence Needed**: Check `GetActiveSubscriptionsAsync()` to see current count.

---

## Immediate Fixes Required

### Fix #1: Choose One Event Source (Recommended: Webhooks Only)

**Action**: Disable Pusher-based follow detection in `KickLibChatService.cs`

**Rationale**:
- Webhooks provide **complete information** (username, user ID)
- Pusher follow event is **incomplete** (no username)
- Official Kick webhooks are more reliable
- Reduces duplicate events

**Implementation**:
```csharp
// In KickLibChatService.cs - Line 460
private void OnFollowersUpdated(object? sender, FollowersUpdatedEventArgs e)
{
    // DISABLED: Follow events now handled via webhooks only
    // Reason: Webhooks provide username, Pusher doesn't
    _logger.LogInformation("👥 Follower count updated: {Count} (webhook will handle event)", 
        e.Data.FollowersCount);
    
    // Do NOT publish KickFollowEvent here
}
```

### Fix #2: Publish Dedicated Event Types from Webhooks

**Action**: Instead of publishing `KickChatMessageEvent` from webhooks, publish `KickFollowEvent`

**Current** (WebhooksController.cs Line 172):
```csharp
var kickEvent = new KickChatMessageEvent { /* ... */ };
await _eventBus.PublishAsync(kickEvent);
```

**Should be**:
```csharp
var followEvent = new KickFollowEvent
{
    Source = "Kick",
    Username = followerUsername,
    UserId = followerId,
    FollowerCount = 0, // Can be fetched from API if needed
    FollowedAt = createdAt ?? DateTime.UtcNow
};
await _eventBus.PublishAsync(followEvent);
```

### Fix #3: Add Subscription Cleanup on Application Exit

**File**: `Program.cs` or `App.xaml.cs` (WPF Application)

**Add**:
```csharp
// In WPF App.xaml.cs or startup
protected override async void OnExit(ExitEventArgs e)
{
    var coordinator = ServiceProvider.GetRequiredService<WebhookCoordinatorService>();
    
    // Get access token
    var dataService = ServiceProvider.GetRequiredService<IDataService>();
    var platform = await dataService.GetPlatformByNameAsync("Kick");
    
    if (platform?.AccessToken != null)
    {
        await coordinator.DisableKickWebhooksAsync(platform.AccessToken);
    }
    
    await coordinator.StopAsync();
    coordinator.Dispose();
    
    base.OnExit(e);
}
```

### Fix #4: Check and Clean Existing Subscriptions on Startup

**File**: `WebhookCoordinatorService.cs` - Add to `EnableKickWebhooksAsync`

**Add before subscribing** (Line 220):
```csharp
// Check for duplicate subscriptions first
var existingSubscriptions = await _kickSubscriptionService.GetActiveSubscriptionsAsync(accessToken);
_logger.LogInformation("📋 Found {Count} existing webhook subscriptions", existingSubscriptions.Count);

if (existingSubscriptions.Count > 0)
{
    _logger.LogWarning("⚠️ Cleaning up existing subscriptions to prevent duplicates");
    var ids = existingSubscriptions.Select(s => s.SubscriptionId).ToList();
    await _kickSubscriptionService.UnsubscribeAllAsync(ids, accessToken);
    
    // Also mark as inactive in database
    var dbSubs = await _dataService.GetWebhookSubscriptionsAsync("Kick");
    foreach (var sub in dbSubs.Where(s => s.IsActive))
    {
        sub.IsActive = false;
        await _dataService.SaveWebhookSubscriptionAsync(sub);
    }
}
```

### Fix #5: Remove Auto-Reply from Chat Display Events

**Issue**: `KickChatMessageEvent` with webhook-generated messages (like "username followed") should NOT trigger auto-responses.

**Solution**: Add check in `ChatViewModel.OnKickChatMessageReceivedAsync`:

```csharp
private async Task OnKickChatMessageReceivedAsync(KickChatMessageEvent evt)
{
    // Skip auto-response processing for system messages from webhooks
    if (evt.Username == "Vyral.Bot" && evt.Badges?.ContainsKey("vyral") == true)
    {
        // This is a webhook system message, only display it
        await AddMessageAsync(evt);
        return;
    }
    
    // Normal processing continues...
}
```

---

## Testing Plan

### 1. Test Duplicate Subscriptions
```bash
# Check PowerShell script
.\check_kick_subscriptions.ps1
```

Expected: Should show current count of active subscriptions.

### 2. Test Follow Event (After Fixes)
1. Start app
2. Check logs for subscription count
3. Have someone follow
4. Verify **ONE** follow message appears
5. Verify **ONE** auto-reply is sent
6. Check that message appears as **broadcaster username**, not "Vyral.Bot"

### 3. Test Cleanup
1. Start app
2. Note subscription count
3. Close app
4. Check Kick Developer Portal → Webhooks
5. Verify subscriptions are removed

---

## Priority Order

1. **[HIGH]** Fix #1: Disable Pusher follow detection
2. **[HIGH]** Fix #4: Clean existing subscriptions on startup  
3. **[MEDIUM]** Fix #2: Use dedicated event types from webhooks
4. **[MEDIUM]** Fix #5: Skip auto-reply for webhook system messages
5. **[MEDIUM]** Fix #3: Add cleanup on application exit

---

## Files to Modify

1. `KickLibChatService.cs` - Disable `OnFollowersUpdated` publishing
2. `WebhooksController.cs` - Publish `KickFollowEvent` instead of chat messages
3. `WebhookCoordinatorService.cs` - Add subscription cleanup check
4. `App.xaml.cs` - Add `OnExit` cleanup
5. `ChatViewModel.cs` - Skip auto-reply for webhook system messages

---

## Expected Outcome

After implementing these fixes:

✅ **One follow event** per actual follow  
✅ **One auto-reply** per event  
✅ **Consistent username** display (broadcaster name)  
✅ **Clean subscriptions** on app restart  
✅ **No orphaned subscriptions** accumulating on Kick servers

---

## Additional Recommendations

1. **Add subscription monitoring**: Log active subscription count on startup
2. **Add rate limiting**: Prevent auto-reply spam if multiple follows occur rapidly
3. **Add subscription health check**: Periodically verify subscriptions are active
4. **Consider webhook retry logic**: Handle failed webhook deliveries gracefully
5. **Add telemetry**: Track duplicate event occurrences to verify fixes work

---

*Generated: December 11, 2025*
