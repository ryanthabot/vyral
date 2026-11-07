# Kick

> Kick uses a hybrid design: chat flows over KickLib's Pusher WebSocket, while channel events arrive as official Kick webhooks tunneled through ngrok — with a careful duplicate-prevention split deciding which events use which transport.

## Overview

Kick was the hardest platform to integrate (Cloudflare blocks plain API clients, and the official API has evolved quickly), and the current implementation reflects that history. Connecting Kick runs the standard wizard: sign in, create an app in Kick's developer settings, authorize. Behind the scenes the wizard also scrapes Kick's website to discover your channel IDs, which the chat connection needs.

Once connected, Vyral receives **chat messages** over a Pusher WebSocket and **channel events** (follows, subs, gifts, stream status, moderation) through Kick's official webhook system, which requires the app's embedded web server and an [ngrok tunnel](../settings/web-server-ngrok.md). When you send a message from Vyral, it posts **as your broadcaster account** — Kick's API has no separate bot-identity concept for this flow.

The event pipeline was deliberately split to avoid duplicate events: whichever transport natively carries an event type "owns" it, and the other transport ignores it.

## User Guide

When the Kick chat connects or drops, the Chat window posts a gold System line ("Kick chat connected — listening to your channel" / "Kick chat disconnected") so you can confirm the chat is live at a glance. App-only — never broadcast.


### Before you start

- A Kick account (the bot speaks **as this account**).
- An ngrok authtoken and the ngrok binary installed — needed for **webhooks** (follows/subs/etc.). Run the ngrok setup from **Settings → API & Webhooks** first; see [Web Server & ngrok](../settings/web-server-ngrok.md). Chat works without ngrok; webhooks do not.

### Connecting Kick (wizard walkthrough)

1. Open **Platforms** and click the **Kick** card.
2. **Step 1: Sign In to Kick.** The embedded browser opens `https://kick.com/login`. Sign in, then continue.
3. **Step 2: Create Application.** The wizard opens `https://kick.com/settings/developer`:
   - Create a new app. For the redirect URL use exactly `http://localhost:3000/callback` (copy button provided).
   - Copy the **Client ID** and **Client Secret** and paste them into the wizard's popups.
4. **Step 3: Authorize Account.** The embedded browser opens Kick's OAuth consent page; approving it triggers the `localhost:3000/callback` redirect which the wizard intercepts. Your channel name is taken from the account you just authorized (the step-4 screen shows it as a read-only "Authorized channel" box), and the wizard then uses it to look up your channel ID and chatroom ID in the background (see [How It Works](#how-it-works-developer) for how).
5. **Step 4: Test Connection.** Send a test message — it appears in your Kick chat from your account — then finish.

The card flips to the **Kick Connection** view (masked credentials, RECONNECT/DISCONNECT), and a Kick dashboard entry appears in the sidebar.

### Enabling webhooks (follows, subs, gifts)

Chat connects by itself, but webhook events need the webhook stack:

1. Go to **Settings → API & Webhooks** and start the webhook server (ngrok tunnel runs automatically). Best to enable **Auto-start on Launch** so events work on every restart.
2. Go to **Settings → Platforms → Kick** and turn on **Enable Webhooks** under Webhook Settings.
3. Done — the app registers webhook subscriptions with Kick and starts receiving events.

### Reconnecting and disconnecting

- **DISCONNECT** (Platforms page or Kick settings) keeps the saved configuration but clears the stored credentials and disconnects chat — you must run the wizard again to reconnect. The Kick dashboard may show "Disconnected (reconnect on app restart)".
- On launch, Kick auto-reconnects if the DB status is "Connected": it first refreshes the stored token, then connects chat.
- **If the ngrok URL changes** (it rotates on the free plan), the app automatically resubscribes the Kick webhooks to the new URL (fixed 2026-08-15). If the automatic recovery fails, the app warns you and the manual fix is to open Kick settings and toggle **Enable Webhooks** off and on again.

## How It Works (Developer)

### Auth (`KickLibOAuthService`)

A thin wrapper over KickLib's `KickOAuthGenerator` (the class doc notes it replaced a 530-line custom OAuth service). **OAuth 2.0 with PKCE**, redirect URI `http://localhost:3000/callback`, scopes: `user.read`, `channel.read`, `chat.write`, `events.subscribe`, `moderation.ban`, `kicks.read` (`KickLibOAuthService.cs:41-49`). `ExchangeCodeForTokenAsync` swaps the code for tokens, calls `api.Users.GetMeAsync` for username/userId, and writes everything into the `Platform` row: AccessToken, RefreshToken, TokenExpiresAt (from `expires_in`), Username, **UserId**, ClientId, ClientSecret, Status="Connected".

**Tokens are stored unencrypted in the database — deliberately.** Code comments state "Kick tokens are NOT encrypted (KickLib manages token security)" (`PlatformsViewModel.cs:1011`, `MainViewModel.cs:333`). This contrasts with [Twitch's DPAPI-encrypted tokens](twitch.md). Tokens refresh on demand (`KickLibOAuthService.RefreshTokenAsync`) and preemptively inside the chat service whenever a send is attempted with under 5 minutes of token life left (`KickLibChatService.cs:203-213`).

### Hybrid transport (`KickLibChatService`)

The file header documents the architecture — **chat via Pusher WebSocket, channel events via official webhooks** — chosen to avoid duplicate events (the split was formalized after the WEBHOOK_DUPLICATE_AUDIT work):

- **Chat:** `ListenToChatRoomAsync(chatroomId)` then `ConnectAsync()` (`KickLibChatService.cs:131-144`). Connect fails fast with "please run the setup wizard again" if `ChannelId`/`ChatroomId` are missing from the DB (lines 119-123).
- **Channel events:** the [Web Server & ngrok](../settings/web-server-ngrok.md) stack. The receiving pipeline is `WebhooksController` (`POST /webhooks/kick`, dispatched on the `Kick-Event-Type` header) → signature middleware → [Event Bus](../architecture/event-bus.md).

**Duplicate prevention in `WebhookCoordinatorService.EnableKickWebhooksAsync`** (lines 195-305): it first deactivates all existing DB subscriptions and **unsubscribes everything server-side**, then subscribes only to the **webhook-only** event types — `LivestreamMetadataUpdated` and `ModerationUserBanned` (`WebhookCoordinatorService.cs:249-264`):

```csharp
// NOTE: KickLib Pusher WebSocket takes priority - only subscribe to events NOT handled by Pusher
var webhookUrl = $"{_ngrokService.PublicUrl}/webhooks/kick";
var eventTypes = new List<KickLib.Models.v1.EventSubscriptions.EventType>
{
    // Events handled by Pusher (REMOVED to prevent duplicates):
    // - ChannelFollowed (Pusher: OnFollowersUpdated)
    // - ChannelSubscriptionNew / Renewal / Gifts, LivestreamStatusUpdated, KicksGifted ...
    //
    // Events NOT handled by Pusher (webhook-only):
    KickLib.Models.v1.EventSubscriptions.EventType.LivestreamMetadataUpdated,
    KickLib.Models.v1.EventSubscriptions.EventType.ModerationUserBanned
};
```

Even so, the controller can still process the Pusher-covered types (`channel.followed`, `channel.subscription.new|renewal|gifts`, `livestream.status.updated`, `moderation.banned`, `kicks.gifted`) if those webhooks were subscribed through the Kick developer portal manually — and `chat.message.sent` webhooks are accepted but deliberately ignored (chat belongs to Pusher). Each `WebhookSubscription` row (Platform, EventType, BroadcasterUserId, WebhookUrl, IsActive) is saved to the DB, and a `WebServerDependencyTracker` entry blocks stopping the web server while Kick webhooks are active.

### Pusher handlers and webhook payload quirks

Pusher handlers publish typed events and, for subs/gifts/hosts/stream-live, also synthesize a bot-authored `KickChatMessageEvent` so the event is visible in chat (`KickLibChatService.cs:334-463`). Ban/unban/message-deleted are only logged. One KickLib limitation: the Pusher follow event carries **no username**, so it publishes `KickFollowEvent` with `Username="Unknown"` (`KickLibChatService.cs:480-489`) — the webhook path (`WebhooksController.ProcessFollowAsync`) is the one that knows the username. Webhook payload parsing is defensive/heuristic (it probes `follower`/`user`/`sender`/`actor` JSON shapes) because payloads lack fields you might expect: no follower count, months, gifter ID, or gift recipients.

### Sending messages

`SendMessageAsync` requires the broadcaster's **user ID** (not channel ID) and calls KickLib REST `SendMessageAsUserAsync(broadcasterUserId, message, accessToken)` (`KickLibChatService.cs:195-240`) — the bot literally posts as the streamer's account. Token is refreshed first if within 5 minutes of expiry.

### Channel-ID discovery (the Cloudflare workaround)

Kick's website blocks plain HTTP clients behind Cloudflare, so the wizard cannot just call the channel API. Instead `KickWebSetupViewModel.FetchAndSaveChannelIdsAsync` (lines 706-800) navigates the **embedded WebView2** (a real browser that passes Cloudflare) to the *unofficial* endpoint `https://kick.com/api/v2/channels/{name}` and scrapes the JSON from the rendered page (10-second timeout), extracting `chatroom.id` and the channel ID and saving them to the `Platform` row. Fragile by nature — any change to that endpoint's shape or Cloudflare behavior breaks wizard step 3.

### Webhook signature validation (and the opt-in test bypass)

`KickWebhookValidationMiddleware` applies to `POST /webhooks/kick` only (GETs, like the health check, pass through). It requires the `Kick-Event-Signature`, `Kick-Event-Message-Id`, and `Kick-Event-Timestamp` headers (401 otherwise) and validates an **RSA-SHA256 signature over `{messageId}.{timestamp}.{body}`** against Kick's published public key (`https://api.kick.com/public-key`), PEM-imported and cached for one hour (`KickWebhookValidationMiddleware.cs:99-196`). The stale `TODO: Validate signature` comment in `WebhooksController.cs:51` predates the middleware — validation is real.

**The bypass — opt-in since 2026-08-15.** The test-payload escape hatch still exists, but it is gated behind the `Configs` key `Kick.AllowTestWebhooks` (absent/false by default = fail-closed; the value is cached for 60 s and resolved through the WPF host's `App.ServiceProvider` via reflection):

```csharp
// KickWebhookValidationMiddleware.cs (condensed)
// Test payloads only skip validation when the operator opted in via the
// "Kick.AllowTestWebhooks" config key (default: false).
var isTestWebhook = messageId?.StartsWith("test-") == true || body.Contains("\"test\":true");

if (isTestWebhook && await IsTestWebhookBypassAllowedAsync())
{
    _logger.LogWarning("TEST WEBHOOK DETECTED - Skipping signature validation for MessageId: {MessageId}", messageId);
}
else
{
    var isValid = await ValidateSignature(signature!, messageId!, timestamp!, body);
    ...
}
```

By default, forged "test" payloads are rejected like any other invalid signature. An operator who genuinely needs Kick's test webhooks can set `Kick.AllowTestWebhooks` to `True` in the profile database — and should treat the public tunnel URL as sensitive while it is on.

### ngrok URL rotation — automatic resubscribe (fixed 2026-08-15)

`NgrokService` polls the tunnel URL every 30 seconds and raises `PublicUrlChanged`. The coordinator's handler (`OnNgrokUrlChanged`) now heals Kick webhooks automatically: it updates `CurrentTunnelUrl` in the DB, loads the Kick `Platform` row, refreshes the OAuth token if it is expired (via a newly injected `KickLibOAuthService` — DI auto-resolves it), then unsubscribes all existing Kick subscriptions and resubscribes them to the new URL. Only if that recovery fails does it keep the old behavior of logging a warning and telling the user to toggle **Enable Webhooks** off/on in Kick settings.

## Files & Code

| File | Role |
|---|---|
| [KickLibChatService.cs](../../src/VyralBot.Integrations/Kick/KickLibChatService.cs) | Pusher WebSocket chat, event handlers, send-as-broadcaster, token refresh |
| [KickLibOAuthService.cs](../../src/VyralBot.Integrations/Kick/KickLibOAuthService.cs) | PKCE OAuth wrapper over KickLib, token exchange/refresh |
| [IKickService.cs](../../src/VyralBot.Integrations/Kick/IKickService.cs) | Service interface |
| [Events/](../../src/VyralBot.Integrations/Kick/Events) | `KickChatMessageEvent`, `KickFollowEvent`, `KickSubscribeEvent`, `KickGiftSubEvent`, `KickHostEvent`, `KickKicksGiftedEvent`, `KickRewardRedeemedEvent`, `KickStreamStatusEvent` |
| [WebhooksController.cs](../../src/VyralBot.Api/Controllers/WebhooksController.cs) | `POST /webhooks/kick` receiver; header-dispatched event processing; `GET /webhooks/health` |
| [KickWebhookValidationMiddleware.cs](../../src/VyralBot.Api/Middleware/KickWebhookValidationMiddleware.cs) | RSA-SHA256 signature validation (opt-in test bypass behind `Kick.AllowTestWebhooks`) |
| [KickWebhookSubscriptionService.cs](../../src/VyralBot.Api/Services/KickWebhookSubscriptionService.cs) | KickLib subscription wrapper (1s spacing between calls to respect rate limits), resubscribe helper |
| [WebhookCoordinatorService.cs](../../src/VyralBot.Api/Services/WebhookCoordinatorService.cs) | Orchestrates Kestrel + ngrok + Kick subscriptions; automatic URL-rotation resubscribe (with `KickLibOAuthService` injected) |
| [NgrokService.cs](../../src/VyralBot.Api/Services/NgrokService.cs) | ngrok child process, public-URL polling, 30s change monitor |
| [KickWebSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/KickWebSetupViewModel.cs) / [KickWebSetupView.xaml](../../src/VyralBot.UI/Views/KickWebSetupView.xaml) | Setup wizard incl. channel-ID scraping (lines 706-800) |
| [PlatformsViewModel.cs](../../src/VyralBot.UI/ViewModels/PlatformsViewModel.cs) | Card state, connect/disconnect (`DisconnectKick` line 1232), auto-connect with token refresh |
| [KickSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/KickSettingsViewModel.cs) / [KickSettingsView.xaml](../../src/VyralBot.UI/Views/KickSettingsView.xaml) | Settings page: disconnect, webhook toggle, chat enable/auto-start |
| [KickDashboardViewModel.cs](../../src/VyralBot.UI/ViewModels/KickDashboardViewModel.cs) / [KickDashboardView.xaml](../../src/VyralBot.UI/Views/KickDashboardView.xaml) | Dashboard with `player.kick.com` preview |

The connect-time duplicate-prevention gate and hybrid split, from `KickLibChatService.cs:131-138`:

```csharp
// Listen to chatroom events (messages only - other events handled via webhooks)
await _kickClient.ListenToChatRoomAsync(_chatroomId);

// NOTE: Channel events (follows, subs, stream status, etc.) are now handled via
// official Kick webhooks in WebhookCoordinatorService to avoid duplicate events.
// Only chat messages use Pusher.

// Connect to Pusher WebSocket
await _kickClient.ConnectAsync();
```

And send-as-broadcaster from `KickLibChatService.cs:218-226`:

```csharp
// Send message as user (not bot) using KickLib API
// This requires the broadcaster USER ID (not channel ID)
var broadcasterUserId = int.Parse(_platform.UserId);
var result = await _kickApi.Chat.SendMessageAsUserAsync(broadcasterUserId, message, _platform.AccessToken);
```

## Data & Settings

- **`Platform` row (Name="Kick")**: AccessToken/RefreshToken (**unencrypted**), TokenExpiresAt, Username, UserId, **ChannelId**, **ChatroomId**, ClientId/ClientSecret, Status, `KickChatEnabled` / `KickChatAutoStart`.
- **`WebhookSubscription` rows** (per-profile DB): Platform="Kick", EventType, BroadcasterUserId, WebhookUrl (the ngrok URL at subscribe time), IsActive, LastEventReceived. The "Enable Webhooks" toggle state is derived from the count of active rows.
- **`WebServerSettings` row**: port (default 5000), ngrok region, current tunnel URL, auto-start flag — see [Web Server & ngrok](../settings/web-server-ngrok.md).
- **DB variable**: `kick-followercount` is updated by `KickLibChatService` when Pusher reports new follower counts.
- The auto-replies toggle ("thank followers / respond to unfollows") persists in the `Configs` table under key `Kick.AutoRepliesEnabled` — see [Auto-Replies](../chat-features/auto-replies.md).

## Status & Known Issues

How Kick's capabilities compare across the other platforms: [Multi-Platform Parity](multi-platform.md).

- **Webhook signature test bypass is opt-in** — payloads with a `test-` message-ID prefix or `"test":true` in the body skip RSA signature validation **only** when the `Kick.AllowTestWebhooks` Configs key is set; the default is fail-closed (fixed 2026-08-15). Tracked in the [known issues ledger](../reference/known-issues.md) (KI-19 resolved).
- **ngrok URL rotation heals automatically** — the coordinator refreshes the Kick token if needed and resubscribes to the new URL; the manual off/on toggle remains only as the fallback if recovery fails (fixed 2026-08-15). See the [known issues ledger](../reference/known-issues.md) (KI-13 resolved).
- **Tokens stored unencrypted in the DB** — deliberate per code comments (documented); anyone with DB file access has live Kick tokens.
- **No followers API for eligibility** — KickLib 1.8.0 exposes no followers endpoint, so Followers-only giveaways/polls still reject Kick entries (KI-31). [known issues ledger](../reference/known-issues.md)
- **Channel-ID scraping is fragile** — wizard step 3 depends on the unofficial `kick.com/api/v2/channels/{name}` endpoint rendered inside WebView2; Kick-side changes break setup.
- **Pusher follows carry no username** — Pusher-sourced follow events show "Unknown" until/unless the webhook path supplies the name; because the coordinator no longer subscribes `ChannelFollowed` webhooks, "Unknown" is the common case.
- **Bot identity is the broadcaster** — all bot messages post as the streamer's account; there is no separate bot user.
- **Webhook payloads are missing data** — follower counts, sub months, gifter IDs, and gift recipients are absent from Kick's payloads; the controller logs the known gaps.
- **First auto-connect force-enables chat** — `KickChatEnabled` is flipped to true on the first successful auto-connect (intended behavior — KI-14). See the [known issues ledger](../reference/known-issues.md).
