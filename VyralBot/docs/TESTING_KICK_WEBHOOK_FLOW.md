# Testing Kick Webhook Flow - Complete Guide

## Current Status
✅ **Phase 1 Complete**: Kestrel merged into WPF, EventBus shared between UI and API

## What Changed
Previously, VyralBot ran as **two separate processes**:
- `VyralBot.exe` (WPF UI) - Had its own EventBus instance
- `VyralBot.Api.exe` (ASP.NET Core) - Had its own EventBus instance
- ❌ Events published in API **never reached** the UI

Now, VyralBot runs as **one single process**:
- `VyralBot.exe` - Hosts both WPF UI AND Kestrel web server
- ✅ EventBus is **shared** - events flow from WebhooksController → ChatViewModel

## Pre-Test Setup

### 1. Verify Ngrok is Running
```powershell
# Check if ngrok is running
Get-Process ngrok -ErrorAction SilentlyContinue
```

Expected output should show ngrok process. If not running:
```powershell
# Start ngrok (from the directory where ngrok.exe is located)
ngrok http 5000 --authtoken YOUR_TOKEN
```

Your webhook URL should be: `https://<your-ngrok-subdomain>.ngrok-free.dev/kick/webhook`

### 2. Verify Kick Webhook is Registered
- Go to Kick Developer Portal: https://kick.com/dashboard/settings/developers
- Check that webhook URL is registered
- Verify it's set to receive `chat_message` events

### 3. Build the Application
```powershell
cd "c:\Users\user\OneDrive\Desktop\VyralBot\VyralBot\src\VyralBot.UI"
dotnet restore
dotnet build
```

## Test Execution

### Step 1: Launch VyralBot
```powershell
cd "c:\Users\user\OneDrive\Desktop\VyralBot\VyralBot\src\VyralBot.UI\bin\Debug\net9.0-windows"
.\VyralBot.exe
```

**Expected Logs** (watch the console/log file):
```
🚀 Building application host...
✅ Application host started successfully - Kestrel is now listening on http://localhost:5000
🎯 CRITICAL: EventBus is now SHARED between WPF UI and Kestrel webhooks!
```

### Step 2: Initialize ChatViewModel
Navigate to the Chat view in the application.

**Expected Logs**:
```
🚀 Initializing ChatViewModel...
✅ Subscribed to KickChatMessageEvent (ID: <some-guid>)
   📡 Ready to receive Kick webhook events from WebhooksController!
✅ Subscribed to TwitchChatMessageEvent (ID: <some-guid>)
✅ Subscribed to GenericChatMessageEvent (ID: <some-guid>)
🎯 ChatViewModel initialization complete - 3 subscriptions active
```

### Step 3: Connect to Kick
1. Go to Settings → Platforms → Kick
2. Ensure access token is present (if not, run OAuth flow)
3. Status should show: **Connected** (green)

**Expected Logs**:
```
Kick service initialized
Connected platforms updated: Kick: True, Twitch: <depends on twitch connection>
```

### Step 4: Send Test Message
Open your Kick channel in a web browser and send a chat message:
```
Hello from Kick! This is a test message.
```

**Expected Event Flow**:

1. **Ngrok receives webhook** (check ngrok web interface at http://127.0.0.1:4040):
   - POST request to `/kick/webhook`
   - Status: 200 OK

2. **WebhooksController receives event**:
   ```
   Received Kick webhook event: chat_message
   Chat message: Hello from Kick! This is a test message.
   📤 Publishing KickChatMessageEvent to EventBus...
      Event details: Username=<your-username>, Message=Hello from Kick! This is a test message.
   ✅ KickChatMessageEvent published successfully!
      🎯 ChatViewModel should receive this event now (shared EventBus)
   Published Kick chat message event from <your-username>
   ```

3. **ChatViewModel receives event**:
   ```
   🎉 ChatViewModel received Kick webhook message!
      Event details: Username=<your-username>, Message=Hello from Kick! This is a test message., Source=Kick
      Display Name: <your-username> | Color: <color-hex> | Channel: <channel-slug>
      Badges: <badge-list> | Reply To: <null or reply-info>
   ✅ Kick message added to UI: [<timestamp>] <your-username>: Hello from Kick! This is a test message.
   Connected platforms updated: Kick: True
   ```

4. **UI Updates**:
   - Message appears in ChatMessages collection
   - Username is displayed in Kick green (#53FC18) or custom color
   - Badges appear next to username (subscriber, moderator, verified, etc.)
   - Timestamp is current time
   - "Connected Platforms" indicator updates to show Kick as active

## Expected Results

### ✅ Success Indicators
- [ ] Kestrel starts on port 5000 (log: "listening on http://localhost:5000")
- [ ] ChatViewModel subscribes to KickChatMessageEvent (log shows subscription ID)
- [ ] Webhook POST request succeeds (ngrok shows 200 OK)
- [ ] WebhooksController publishes event (log: "📤 Publishing KickChatMessageEvent")
- [ ] ChatViewModel receives event (log: "🎉 ChatViewModel received Kick webhook message!")
- [ ] Message appears in chat view with correct formatting
- [ ] Username color is Kick green (#53FC18) or user's custom color
- [ ] Badges display correctly (subscriber, mod, verified, etc.)

### ❌ Failure Indicators
If webhook doesn't reach VyralBot:
- Check ngrok is running and URL matches Kick developer portal
- Check ngrok web interface (http://127.0.0.1:4040) for incoming requests
- Verify firewall isn't blocking port 5000

If ChatViewModel doesn't receive event:
- Check EventBus subscription logs (should see subscription ID)
- Verify WebhooksController published event (log: "📤 Publishing")
- Check for exceptions in logs

If message doesn't appear in UI:
- Verify ChatMessages.Add() was called (log: "✅ Kick message added to UI")
- Check XAML binding in ChatView.xaml
- Verify ObservableCollection updates on UI thread

## Debugging Tips

### Enable Verbose Logging
Check `appsettings.json` in VyralBot.UI:
```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Debug",
      "Override": {
        "Microsoft": "Information",
        "System": "Information"
      }
    }
  }
}
```

### Check EventBus Subscriptions
Add breakpoint in:
- `EventBus.PublishAsync()` - Verify event is published
- `EventBus.SubscribeAsync()` - Verify subscriptions are registered
- `ChatViewModel.OnKickChatMessageReceivedAsync()` - Verify handler is called

### Webhook Payload Inspection
Check ngrok web interface to see full webhook payload:
1. Navigate to http://127.0.0.1:4040
2. Click on POST /kick/webhook request
3. View full JSON payload in request body

### Manual Webhook Test
Use curl or Postman to send test webhook:
```powershell
$headers = @{
    "Content-Type" = "application/json"
}

$body = @{
    event = "chat_message"
    data = @{
        message_id = "test-123"
        content = "Test message"
        created_at = (Get-Date).ToString("o")
        sender = @{
            user_id = 12345
            username = "testuser"
            slug = "testuser"
            is_verified = $false
            profile_picture = "https://example.com/avatar.jpg"
            channel_slug = "testchannel"
            identity = @{
                username_color = "#53FC18"
                badges = @()
            }
        }
    }
} | ConvertTo-Json -Depth 10

Invoke-RestMethod -Uri "http://localhost:5000/kick/webhook" -Method POST -Headers $headers -Body $body
```

## Next Steps After Successful Test

Once webhook flow is verified:
1. **Phase 2**: Remove obsolete Pusher WebSocket code from KickService.cs
2. **Phase 3**: Add webhook subscription management in KickService
3. **Phase 4**: Update database schema to track ProfileId in webhook subscriptions
4. **Phase 5**: Implement token refresh handling
5. **Phase 6**: Comprehensive testing (signature validation, edge cases, error handling)

## Common Issues

### Issue: "EventBus has no subscribers"
**Cause**: ChatViewModel not initialized before webhook received
**Solution**: Navigate to Chat view first, verify subscription logs appear

### Issue: "Signature validation failed"
**Cause**: Webhook signature from Kick doesn't match calculated signature
**Solution**:
- Verify signing secret in Kick developer portal matches KickService configuration
- Check KickWebhookValidationMiddleware is registered in middleware pipeline
- Ensure raw body is used for signature calculation (not parsed JSON)

### Issue: "Message appears but with wrong color"
**Cause**: Color not extracted from webhook payload
**Solution**: Check `sender.identity.username_color` in webhook JSON

### Issue: "Badges not displaying"
**Cause**: Badge parsing failed or XAML binding incorrect
**Solution**:
- Verify badges dictionary in KickChatMessageEvent
- Check ChatView.xaml badge display logic
- Ensure badge images/icons are available in resources

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────┐
│                      VyralBot.exe Process                    │
│                                                               │
│  ┌──────────────┐                    ┌──────────────────┐   │
│  │ WPF UI       │                    │ Kestrel Server   │   │
│  │              │                    │ (Port 5000)      │   │
│  │ ChatViewModel│◄───────┐           │                  │   │
│  │              │        │           │ WebhooksCtrl     │   │
│  └──────────────┘        │           └──────────────────┘   │
│                          │                    ▲              │
│                          │                    │              │
│                     ┌────▼────┐               │              │
│                     │EventBus │◄──────────────┘              │
│                     │(Shared) │                              │
│                     └─────────┘                              │
└─────────────────────────────────────────────────────────────┘
                               ▲
                               │
                    ┌──────────┴──────────┐
                    │  Ngrok Tunnel       │
                    │  (Public URL)       │
                    └──────────┬──────────┘
                               │
                    ┌──────────▼──────────┐
                    │  Kick Webhook API   │
                    │  (kick.com)         │
                    └─────────────────────┘
```

## Log Emoji Legend

- 🚀 = Initialization starting
- ✅ = Success / Completion
- 📡 = Ready state / Listening
- 🎯 = Critical information
- 📤 = Publishing/Sending event
- 🎉 = Event received
- ❌ = Error/Failure

## Success Criteria

The test is successful when:
1. All expected logs appear in correct sequence
2. Message appears in ChatViewModel.ChatMessages within 1-2 seconds of sending
3. Message formatting matches Kick style (green color, proper badges)
4. No exceptions or errors in logs
5. Subsequent messages also appear (flow is repeatable)
