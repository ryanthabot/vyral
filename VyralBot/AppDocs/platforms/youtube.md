# YouTube

> YouTube is a polling integration: the app asks the YouTube Data API for new live-chat messages every few seconds — which means it only works while you are actually live (or have an upcoming broadcast), and it delivers chat messages only, no follow/sub events.

## Overview

Connecting YouTube takes longer than the other platforms because you first have to set up a Google Cloud project yourself. The wizard walks you through it with direct links to the right Google Cloud Console pages, then runs the standard OAuth consent, then verifies with a test message.

After connecting, Vyral **polls** your live chat: it looks for an active (or upcoming) broadcast, finds that broadcast's live chat, and fetches new messages on the schedule YouTube's API recommends. When you are not streaming, the app quietly waits and re-checks for a broadcast every 30 seconds, so you can leave it connected permanently. Sending messages from Vyral into YouTube chat also only works while live.

YouTube has no inbound needs — no webhooks, no ngrok — just outbound HTTP with an OAuth token.

## User Guide

### Before you start

- A Google account that owns your YouTube channel.
- Access to [Google Cloud Console](https://console.cloud.google.com/) to create a project and OAuth credentials. If you have never done this, budget 10-15 minutes; the wizard gives step-by-step instructions with links.

### Connecting YouTube (wizard walkthrough)

The wizard has four steps tailored to Google's console:

1. **Step 1: Enable YouTube API.** The embedded browser opens the Google Cloud Console.
   - Create (or pick) a project. The wizard asks you to save your **Project ID** — there is a PROJECT ID button in the wizard bar; you can copy the ID from the console's dashboard URL or project selector.
   - Enable the **YouTube Data API v3** for that project (the wizard's COPY SEARCH button puts the exact API name on your clipboard to paste into the console's search).
2. **Step 2: Create OAuth Credentials.** The wizard opens the console's Credentials page:
   - Create an **OAuth client ID** (type: Desktop app is fine).
   - Before it lets you, Google forces you through **OAuth consent screen** setup — choose External, add yourself as a test user. The wizard links the console's audience/scopes pages for this.
   - Copy the **Client ID** and **Client Secret** and paste them into the wizard's popups.
3. **Step 3: Authorize Account.** The wizard opens Google's consent page (scope: *Manage your YouTube account*). Sign in with the account that owns the channel and approve. The redirect to `http://localhost:3000/oauth2callback` is intercepted by the wizard. The wizard then fetches your channel title to confirm.
4. **Step 4: Test Connection.** Try the test message. Note: **the test only succeeds while you are live streaming with chat** — if you are offline it fails with "You must be streaming live", which is expected. You can still finish setup and use it at stream time.

The card flips to the **YouTube Connection** view with RECONNECT/DISCONNECT.

### The YouTube dashboard page

Once YouTube is connected, the sidebar's **Platforms** section gains a **YouTube** entry (alongside Twitch and Kick) that opens the YouTube dashboard page — same style as the Twitch/Kick dashboards: connection status for the channel and a **PREVIEW STREAM** panel that opens the channel's live page in an embedded browser (YouTube has no handle-based embed player, so it uses the live watch page; close the preview to free the browser).

### Live-chat states (why chat is or isn't flowing)

YouTube's chat only exists while your stream is live, so Vyral tells you exactly which state it's in — as gold system messages in the Chat window, as the status on Settings → Performance, and via the service state:

- **Connecting** — signing in and looking for your broadcast.
- **Connected — waiting for you to go live** — credentials are fine but your channel isn't live, so there's no live chat to read yet. Chat messages will start the moment you go live (the app re-checks every 30 seconds).
- **Connected — chat active** — your stream is live and messages are being ingested.
- **Stream ended** — the broadcast closed; back to waiting for you to go live.
- **Re-authorization required** — Google sign-in needed (see below).

### What "connected" means for YouTube

- While you are **live** (or have an upcoming/testing broadcast): chat messages appear in the [Chat Window](../chat/chat-window.md), you can send from the app ([Sending Messages](../chat/sending-messages.md)), and chat commands work — the poll loop also publishes the platform-agnostic `ChatMessageReceivedEvent` that feeds the command/currency/giveaway/poll/watchtime pipeline (entries and votes are recorded; the bot's *replies* are only sent to Twitch/Kick).
- While **offline**: the app stays connected and quietly checks for a broadcast every 30 seconds. Your stream's chat picks up automatically when you go live.
- Chat history now persists and reloads for YouTube like the other platforms (fixed 2026-08-15).
- YouTube contributes **chat messages only** — no follow/sub events exist in this integration, so YouTube viewers do not trigger [event reactions](../chat/event-reactions.md).

### Reconnecting and disconnecting

- Tokens refresh automatically; you should rarely need to re-authorize. RECONNECT re-runs the stored-token connect path.
- DISCONNECT keeps the saved row with credentials cleared; reconnecting means running the wizard again.

### Re-authorization required ("invalid_grant")

When Google refuses the stored refresh token (`invalid_grant` — it expired or was revoked), startup auto-connect fails and the Platforms page now shows **"Re-authorization required"** for YouTube instead of a false "Connected". This happens most often because the app's Google OAuth consent screen is in **Testing** mode: Google expires Testing-mode refresh tokens after **7 days**. To fix it, run **Home → Connections → YouTube** and sign in again — the wizard fetches a fresh token and the chat reconnects immediately. Long-term fix: publish your Google OAuth app (or move it "In production") so refresh tokens stop expiring weekly. The Platforms page always shows the live chat service's state (a failed auto-connect also marks the saved row Disconnected), and if the row claims Connected while the service is down, the page silently retries once from the stored credentials before giving up.

## How It Works (Developer)

### Auth

Google OAuth 2.0 in the `YouTubeWebSetupViewModel` (note: the OAuth logic lives in the **UI ViewModel** here, not in the Integrations project):

- Authorization endpoint `https://accounts.google.com/o/oauth2/v2/auth`, redirect `http://localhost:3000/oauth2callback`, scope **`https://www.googleapis.com/auth/youtube.force-ssl` only**, `access_type=offline` (to get a refresh token), `prompt=consent` (force a fresh refresh token every authorization) (`YouTubeWebSetupViewModel.cs:505-511`).
- The WebView2 flow watches `NavigationStarting` for the `/oauth2callback` redirect (`YouTubeWebSetupViewModel.cs:547`); the code is exchanged at `https://oauth2.googleapis.com/token` (line 625), and the channel title is fetched from `youtube/v3/channels?mine=true` (line 660).
- The wizard collects ClientId/ClientSecret plus a **Google Project ID** and saves them to the DB `Platform` row.
- **Tokens are not encrypted** — `YouTubeChatService` writes AccessToken/RefreshToken back to the row directly (`YouTubeChatService.cs:598-618`), unlike [Twitch](twitch.md)'s DPAPI-encrypted tokens.

### Chat ingestion: polling (`YouTubeChatService`)

No WebSocket, no webhook — a poll loop (`PollLoopAsync`, `YouTubeChatService.cs:246-295`):

1. **Find a live chat.** `FindActiveLiveChatIdAsync` (lines 447-469) queries `liveBroadcasts?mine=true` with `broadcastStatus=active` first, then falls back to **all** broadcasts — upcoming and testing broadcasts also carry a `liveChatId`, so the app can attach before you actually go live.
2. **Poll messages.** `GET youtube/v3/liveChat/messages?liveChatId=...&part=snippet,authorDetails` with a `pageToken` for pagination (lines 301-371).
3. **Honor the API's cadence.** The response includes `pollingIntervalMillis`; the loop adopts it verbatim (default 5000 ms if never told otherwise) — this keeps the app inside YouTube's quota guidance:

```csharp
// Update polling interval from YouTube's recommendation
if (root.TryGetProperty("pollingIntervalMillis", out var intervalProp))
{
    _pollingIntervalMs = intervalProp.GetInt32();
}
```

4. **Handle stream end.** A 403/404 during polling clears the cached `liveChatId` and returns to broadcast discovery, retrying every 30 seconds (`YouTubeChatService.cs:281-289`).
5. **Filter to text messages.** Only items with `snippet.type == "textMessageEvent"` are processed (line 382) — super chats, memberships, and other message types are dropped, and only `YouTubeChatMessageEvent` is ever published (author details like moderator/owner/member badges are carried on the event).

### Sending

`SendMessageAsync` (lines 173-240) POSTs to `liveChat/messages` with a `textMessageEvent` snippet. If the API answers 401/403 the token is refreshed and the send retried once. With no active broadcast it throws "You must be streaming live on YouTube to send messages." (line 186).

### Token lifecycle

`EnsureTokenFreshAsync` (line 543) runs before every poll and send: if expiry is within 5 minutes it refreshes against `oauth2.googleapis.com/token` using the ClientId/Secret from the `Platform` row, writing new tokens back to the DB (lines 543-633). Broadcast discovery retries once after a refresh on 401/403.

### Connect semantics when offline

`ConnectAsync` refreshes the token if needed, tries broadcast discovery **once**, and — critically — still marks the service connected when no broadcast exists (`YouTubeChatService.cs:103-114`): the poll loop simply keeps looking every 30 seconds. This is why the Platforms card can show YouTube as connected while you are offline, and why the step-4 test message fails with "You must be streaming live" during setup. On connect/disconnect the service publishes `PlatformConnectionChangedEvent` so the UI can react.

## Files & Code

| File | Role |
|---|---|
| [YouTubeChatService.cs](../../src/VyralBot.Integrations/YouTube/YouTubeChatService.cs) | Poll loop, broadcast discovery, send, token refresh |
| [IYouTubeService.cs](../../src/VyralBot.Integrations/YouTube/IYouTubeService.cs) | Service interface |
| [Events/YouTubeChatMessageEvent.cs](../../src/VyralBot.Integrations/YouTube/Events/YouTubeChatMessageEvent.cs) | The only event type YouTube emits |
| [YouTubeWebSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/YouTubeWebSetupViewModel.cs) | Wizard **and** the OAuth flow itself (URL building, code exchange, channel lookup) |
| [YouTubeWebSetupView.xaml](../../src/VyralBot.UI/Views/YouTubeWebSetupView.xaml) | 4-step wizard UI with Google Cloud deep links |
| [PlatformsViewModel.cs](../../src/VyralBot.UI/ViewModels/PlatformsViewModel.cs) | Card state, auto-connect, disconnect (~line 1409) |
| [YoutubeChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/YoutubeChatSettingsViewModel.cs) / [YoutubeChatSettingsView.xaml](../../src/VyralBot.UI/Views/YoutubeChatSettingsView.xaml) | Chat enable toggle (persists `YoutubeChatEnabled`) |

Broadcast discovery's active-then-all fallback (`YouTubeChatService.cs:447-460`):

```csharp
// First try active broadcasts
var chatId = await QueryBroadcastsForChatIdAsync("active");
if (!string.IsNullOrEmpty(chatId))
    return chatId;

// Fall back to all broadcasts (upcoming, testing, etc. also have liveChatIds)
chatId = await QueryBroadcastsForChatIdAsync(null);
if (!string.IsNullOrEmpty(chatId))
    return chatId;
```

And the offline wait-state in the poll loop (`YouTubeChatService.cs:255-268`):

```csharp
// If we don't have a liveChatId, try to find one
if (string.IsNullOrEmpty(_liveChatId))
{
    _liveChatId = await FindActiveLiveChatIdAsync();
    if (!string.IsNullOrEmpty(_liveChatId))
    {
        _nextPageToken = null; // Reset page token for new chat
    }
    else
    {
        await Task.Delay(broadcastCheckInterval, ct);   // 30s until a broadcast appears
        continue;
    }
}
```

## Data & Settings

- **`Platform` row (Name="YouTube")**: AccessToken/RefreshToken (**plaintext**), TokenExpiresAt, Username (channel title), ClientId/ClientSecret, a Google Project ID, Status, `YoutubeChatEnabled` / `YoutubeChatAutoStart`.
- No credential JSON file — unlike Twitch, everything lives in the DB row.
- No webhook subscriptions, no ngrok dependency. See [Web Server & ngrok](../settings/web-server-ngrok.md) for what does need it.
- The YouTube chat-settings toggle persists `YoutubeChatEnabled` and publishes a `PlatformChatRoutingChangedEvent`; see [Chat Settings](../chat/chat-settings.md).

## Status & Known Issues

- **Chat-only integration** — no follow/sub/super-chat/membership events; YouTube viewers cannot trigger [event reactions](../chat/event-reactions.md), and only `textMessageEvent` messages arrive (super chats are filtered out). YouTube also has no follower concept, so follower-based giveaway/poll eligibility passes `false` (KI-31).
- **Works only while a broadcast exists** — sending fails with "You must be streaming live" when offline; the service otherwise idles in a 30-second discovery loop, which is by design.
- **Tokens stored unencrypted in the DB** — same caveat as Kick; inconsistent with Twitch's DPAPI-encrypted tokens. See the [known issues ledger](../reference/known-issues.md).
- **Chat history is persisted (fixed 2026-08-15)** — `DataChatMessageStore` maps YouTube events both directions and the Chat Window's Load History loads them (KI-9 resolved).
- **OAuth lives in the UI layer** — the auth flow is implemented in `YouTubeWebSetupViewModel` rather than an Integrations service, so it is untestable without the wizard and duplicates patterns held elsewhere.
- **Settings-page YouTube card is disabled with an explanatory tooltip** — YouTube is configured through the Platforms wizard; the Settings → Platforms card explains this instead of silently doing nothing (KI-17 resolved). See the [known issues ledger](../reference/known-issues.md).
