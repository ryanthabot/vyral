# Profile Settings - Quick Reference

## ✅ IMPLEMENTATION COMPLETE

All profile settings now load **centrally and upfront** during profile initialization. No more clicking tabs to load settings!

---

## 🚀 What Loads During Profile Initialization

### 1. Platform Credentials & Tokens
**Database Table**: `Platforms`

**Twitch**:
- OAuth access token
- OAuth refresh token  
- Username
- Channel name
- Chat enabled flag
- Connection status

**Kick**:
- Session cookies (authentication)
- Username
- Channel name
- Chat enabled flag
- Chat auto-start flag
- Connection status

### 2. Kick Settings
**Database Tables**: `Platforms`, `Configs`

- Channel name
- Chat enabled (KickChatEnabled)
- Chat auto-start (KickChatAutoStart)
- Webhook subscriptions enabled toggle
- Auto-replies enabled (from Configs table)
- Webhook status

### 3. Web Server / ngrok Settings
**Database Table**: `WebServerSettings`

- ngrok auth token (from JSON file)
- Port number
- Region (us, eu, ap, au, sa, jp, in)
- Auto-start on launch flag
- Current tunnel URL (if running)
- Enabled state

### 4. Chat Settings
**Loaded from ViewModels**:

- Kick chat filter enabled (KickFilterEnabled)
- Twitch chat filter enabled (TwitchFilterEnabled)
- Chat routing configuration
- Message display settings
- Connected platforms list

---

## 📋 Loading Order (Sequential)

```
OnProfileLoadedAsync() is called
    ↓
1. Update title with profile name
    ↓
2. Initialize webhook coordinator
    ↓
3. Check platform connection status
    ↓
4. CREATE ALL VIEWMODELS
   ├─ PlatformsViewModel
   ├─ KickSettingsViewModel
   ├─ WebServerSettingsViewModel
   └─ ChatViewModel
    ↓
5. LOAD ALL SETTINGS (ProfileSettingsLoader.LoadAllSettingsAsync)
   ├─ Platform credentials (Twitch, Kick)
   ├─ Kick chat/webhook config
   ├─ Web server/ngrok settings
   └─ Chat filters/routing
    ↓
6. AUTO-START FEATURES
   ├─ Auto-subscribe to Kick webhooks (if enabled)
   └─ Auto-start ngrok tunnel (if AutoStartOnLaunch = true)
    ↓
✅ Profile initialization complete
```

---

## 🎯 Expected Behavior

### On App Launch:
1. **User selects profile** → Profile database loads
2. **All settings load in ~1-2 seconds** (single batch operation)
3. **Chat starts listening immediately** (even before user clicks Chat tab)
4. **ngrok auto-starts** (if AutoStartOnLaunch = true in profile)
5. **Kick webhooks auto-subscribe** (if toggle enabled and Kick connected)
6. **Platforms auto-connect** (using loaded OAuth tokens/cookies)

### When User Opens Chat Tab:
- **Messages already displaying** ✅ (background listening was active)
- **No loading delay** ✅ (ChatViewModel already initialized)
- **Filters already set** ✅ (KickFilterEnabled loaded from profile)

### When User Opens Settings:
- **ngrok already running** ✅ (auto-started if configured)
- **Webhooks already subscribed** ✅ (auto-enabled if toggle ON)
- **All values populated** ✅ (loaded during profile initialization)

---

## 📊 Database Tables per Profile

Each profile has isolated database: `appdata/profiles/{ProfileName}/data/vyralbot.db`

### Key Tables:

**Platforms**
```sql
- Id (GUID)
- Name (TEXT) -- "Twitch", "Kick"
- Status (TEXT) -- "Connected", "Disconnected"
- Username (TEXT)
- AccessToken (TEXT) -- OAuth token or session cookie
- RefreshToken (TEXT) -- OAuth refresh token
- CreatedAt (DATETIME)
- UpdatedAt (DATETIME)
- KickChatEnabled (BOOLEAN) -- Kick-specific
- KickChatAutoStart (BOOLEAN) -- Kick-specific
```

**WebServerSettings**
```sql
- Id (GUID)
- IsEnabled (BOOLEAN)
- Port (INTEGER)
- NgrokRegion (TEXT) -- "us", "eu", etc.
- AutoStartOnLaunch (BOOLEAN)
- CurrentTunnelUrl (TEXT)
- CreatedAt (DATETIME)
- UpdatedAt (DATETIME)
```

**Configs** (Key-Value Pairs)
```sql
- Key (TEXT) -- "Kick.AutoRepliesEnabled", etc.
- Value (TEXT)
- Category (TEXT)
- UpdatedAt (DATETIME)
```

**WebhookSubscriptions**
```sql
- Id (TEXT) -- KickLib subscription ID
- Platform (TEXT) -- "Kick"
- EventType (TEXT) -- "ChatMessage", "Follow", "Subscribe", etc.
- CallbackUrl (TEXT)
- CreatedAt (DATETIME)
```

---

## 🔧 How Settings Are Saved

### Automatic Profile-Aware Saving

**ProfileAwareDataService** automatically routes all saves to the current profile's database:

```csharp
// Saving platform credentials (e.g., after Kick OAuth)
var platform = await _dataService.GetPlatformByNameAsync("Kick");
platform.AccessToken = "new_session_cookie";
platform.Status = "Connected";
await _dataService.SavePlatformAsync(platform);
// ✅ Automatically saves to current profile DB

// Saving web server settings
var settings = await _dataService.GetWebServerSettingsAsync();
settings.AutoStartOnLaunch = true;
settings.Port = 5000;
await _dataService.SaveWebServerSettingsAsync(settings);
// ✅ Automatically saves to current profile DB

// Saving config values
await _dataService.SetConfigValueAsync("Kick.AutoRepliesEnabled", "true");
// ✅ Automatically saves to current profile DB
```

**No special code needed** - just use IDataService methods and ProfileAwareDataService handles the routing!

---

## 🧪 Testing Guide

### Test 1: Immediate Chat Display
1. Launch app, select profile with Kick connected
2. **DO NOT** click "Chat" tab yet
3. Send test message from Kick dashboard
4. Click "Chat" tab
5. ✅ **Verify message already displayed** (no delay/loading)

### Test 2: ngrok Auto-Start
1. Go to Settings > Web Server
2. Enable "Auto-start on launch"
3. Set ngrok auth token
4. Restart app
5. ✅ **Verify ngrok tunnel auto-starts** (public URL in status bar)

### Test 3: Webhook Auto-Subscribe
1. Connect to Kick
2. Go to Settings > Kick
3. Enable "Webhooks" toggle
4. Ensure ngrok running
5. Restart app
6. ✅ **Verify webhooks auto-subscribe** (check logs: "✅ Subscribed to 8 events")

### Test 4: Profile Switching
1. Create Profile A: Connect Twitch, disable ngrok auto-start
2. Create Profile B: Connect Kick, enable ngrok auto-start
3. Switch to Profile A → ✅ Twitch connected, ngrok off
4. Switch to Profile B → ✅ Kick connected, ngrok auto-starts

---

## 📝 Logging

Look for these log messages during profile initialization:

```
Profile loaded, initializing ALL app data and settings...
🔄 Loading all profile settings before auto-connections...
🔄 Loading ALL profile settings (Twitch, Kick, ngrok, chat, webhooks)...
📋 Loading platform settings...
Loading platform credentials and connection state...
Platform settings loaded from profile database
⚙️ Loading Kick settings...
Initializing Kick settings
🌐 Loading web server/ngrok settings...
Initializing WebServer settings
💬 Initializing chat with loaded settings...
🚀 Initializing unified chat view with EventBus subscriptions...
✅ Subscribed to KickChatMessageEvent
✅ All profile settings loaded successfully - app ready for auto-connections
✅ All settings loaded - chat messages will now display immediately
Auto-starting ngrok tunnel (AutoStartOnLaunch = true)
✅ Profile initialization complete - all settings loaded, features ready
```

---

## 🐛 Troubleshooting

### Chat Messages Not Displaying
**Check**:
- Is Kick connected? (Settings > Platforms)
- Is KickChatEnabled = true in Platforms table?
- Is KickFilterEnabled = true in ChatViewModel?
- Are webhooks subscribed? (Check logs for "Subscribed to 8 events")
- Is ngrok tunnel running? (Required for webhooks)

### ngrok Not Auto-Starting
**Check**:
- Is AutoStartOnLaunch = true in WebServerSettings table?
- Is ngrok auth token set? (Settings > Web Server)
- Check logs for "Auto-starting ngrok tunnel" message
- Verify WebServerSettingsViewModel.InitializeAsync() loaded settings

### Webhooks Not Auto-Subscribing
**Check**:
- Is Kick connected? (platform.Status = "Connected")
- Is webhook toggle enabled in Kick settings?
- Is ngrok tunnel running? (webhooks require public URL)
- Check logs for "Auto-subscribing to Kick webhooks" message

### Settings Not Persisting
**Check**:
- Is correct profile selected?
- Verify database path: `appdata/profiles/{ProfileName}/data/vyralbot.db`
- Check ProfileAwareDataService is routing to correct database
- Use SQLite browser to inspect database tables directly

---

## 📚 Related Documentation

- **CENTRALIZED_SETTINGS_IMPLEMENTATION.md** - Detailed architecture and implementation notes
- **KICK_INTEGRATION_AUDIT_AND_PLAN.md** - Kick webhook and chat system overview
- **KICK_WEBHOOK_PHASE1_COMPLETE.md** - Webhook implementation details

---

**Last Updated**: December 9, 2025  
**Status**: ✅ Fully Implemented and Tested
