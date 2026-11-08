# YouTube

> YouTube is a polling integration: the app asks the YouTube Data API for new live-chat messages every few seconds — which means it only works while you are actually live (or have an upcoming broadcast). Chat messages, memberships, and **Super Chats / Super Stickers as structured events** (alert, sound family, currency bonus, statistics) all flow from that one poll loop; there are still no follow/raid events.

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
- **Command timers can now target YouTube** (since 2026-11-07): the timer editor offers YouTube alongside Twitch/Kick/Discord, and the timer engine sends through the connected YouTube channel while it is live. A timer tick while you are offline is skipped with a log line and **not** stamped as run, so it fires on the next tick once the broadcast starts.
- While **offline**: the app stays connected and quietly checks for a broadcast every 30 seconds. Your stream's chat picks up automatically when you go live.
- Chat history now persists and reloads for YouTube like the other platforms (fixed 2026-08-15).
- **Memberships and Super Chats trigger event reactions.** A member joining (or a member-milestone message) fires the subscriber alert family; a **Super Chat or Super Sticker** is published as a structured `YouTubeSuperChatEvent` — amount, currency, display string, and comment — in addition to the 💎 chat line. See [Super Chats](#super-chats--super-stickers). Memberships are also a **workflow trigger** — see [Memberships (the `membership` trigger)](#memberships-the-membership-trigger).

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
5. **Special events are mapped, not dropped.** Text messages publish `YouTubeChatMessageEvent` (author details like moderator/owner/member badges are carried on the event). Non-text message types are mapped by shape (`YouTubeChatService.cs`):

   | Live-chat message type | Mapped to |
   |---|---|
   | `textMessageEvent` | `YouTubeChatMessageEvent` |
   | `superChatEvent` / `superStickerEvent` | a synthetic `YouTubeChatMessageEvent` (the 💎 chat line) **plus** the structured `YouTubeSuperChatEvent` |
   | `newSponsorEvent` / `memberMilestoneChatEvent` | `YouTubeMembershipEvent` (new member / milestone text) |

### Super Chats & Super Stickers

Super Chats are YouTube's cheer equivalent, and they are now **first-class** (structured event since 2026-11-07): the publisher parses `superChatDetails` / `superStickerDetails` into `YouTubeSuperChatEvent` with `AmountMicros` (raw micros, tolerant of string-typed values), `AmountDisplayString` ("$5.00"), `Currency`, and the user's comment (Super Chats; stickers carry `StickerDisplayText` instead).

Consumers — all wired in `ChatViewModel.OnYouTubeSuperChatAsync`:

- **Chat line** — the 💎 line ("💎 {user} super chat $5.00! - {comment}") is rendered by the synthetic chat message the publisher sends alongside; the event handler deliberately does not add a second line.
- **Alert** — "Super Chat!" alert with the amount display string and comment; gated by the alerts-side cheer toggle (`AlertSettings.CheerAlertsEnabled`).
- **Sound** — plays the **cheer** sound mapping for YouTube (`PlayEventSoundAsync("cheer", "YouTube")`). Note the [Sounds page](../stream-tools/sounds.md) has no YouTube rows yet, so this only plays if the underlying config key is set — the alert itself always fires.
- **Currency** — awards the cheer bonus with `cents = AmountMicros / 10 000` (1 bit ≈ 1 cent on Twitch, so $5.00 → 500) — exactly the "awards points like bits" rule.
- **Statistics** — a `YouTube / Cheer` statistics row with `username`, `amount`, `currency`, `message`, `platform` variables.

There is **no workflow trigger key** for Super Chats yet — the event is consumed by the reaction pipeline only.

### Memberships (the `membership` trigger)

YouTube memberships are the platform's subscribe equivalent, and they carry their **own workflow trigger key**: both a new member joining (`newSponsorEvent`) and an existing member reaching a milestone (`memberMilestoneChatEvent`) publish a `YouTubeMembershipEvent` (`Username`, `UserId`, `Level`, `IsMilestone`), which `WorkflowTriggerService` dispatches as the canonical **`membership`** event type. The key is deliberately **not** folded into `subscribe` — that key already carries Twitch/Kick subs (the `sub` alias folds onto it), so merging them would make a platform-Any subscribe workflow fire for YouTube members. With separate keys, a subscribe flow targets Twitch/Kick and a membership flow targets YouTube (the trigger's Platform filter separates them further still).

Variables available to an event trigger authored as `membership`:

| Variable | Value |
|---|---|
| `username` | The member's display name (also the context user) |
| `level` | The membership level name (e.g. "Tier 1"), from `newSponsorDetails.memberLevelName` / `memberMilestoneChatDetails.memberLevelName` |
| `tier` | A mirror of `level` — conditions authored against Twitch's `tier` read naturally |
| `userId` | The YouTube channel id of the member |
| `isMilestone` | `true` for milestone renewals, `false` for a brand-new member — filter with a Conditions guard like `{isMilestone} = false` for "new members only" |

Alias spellings that normalize onto the key (so old or hand-typed trigger names still match): `youtubemembership`, `ytmembership`, `sponsor`, `newsponsor`, `newsponsorevent`, `membermilestone`, `membermilestonechatevent` (WorkflowTriggerService.cs:2492-2499). See [Workflows → Event trigger keys](../chat-features/workflows.md#triggers) for the full trigger table.

### Sending

`SendMessageAsync` (lines 173-240) POSTs to `liveChat/messages` with a `textMessageEvent` snippet. If the API answers 401/403 the token is refreshed and the send retried once. With no active broadcast it throws "You must be streaming live on YouTube to send messages." (line 186).

### Token lifecycle

`EnsureTokenFreshAsync` (line 543) runs before every poll and send: if expiry is within 5 minutes it refreshes against `oauth2.googleapis.com/token` using the ClientId/Secret from the `Platform` row, writing new tokens back to the DB (lines 543-633). Broadcast discovery retries once after a refresh on 401/403.

### Connect semantics when offline

`ConnectAsync` refreshes the token if needed, tries broadcast discovery **once**, and — critically — still marks the service connected when no broadcast exists (`YouTubeChatService.cs:103-114`): the poll loop simply keeps looking every 30 seconds. This is why the Platforms card can show YouTube as connected while you are offline, and why the step-4 test message fails with "You must be streaming live" during setup. On connect/disconnect the service publishes `PlatformConnectionChangedEvent` so the UI can react.

## Files & Code

| File | Role |
|---|---|
| [YouTubeChatService.cs](../../src/VyralBot.Integrations/YouTube/YouTubeChatService.cs) | Poll loop, broadcast discovery, send, token refresh, special-event mapping (Super Chat / membership) |
| [IYouTubeService.cs](../../src/VyralBot.Integrations/YouTube/IYouTubeService.cs) | Service interface |
| [Events/YouTubeChatMessageEvent.cs](../../src/VyralBot.Integrations/YouTube/Events/YouTubeChatMessageEvent.cs) | Chat-message event |
| [Events/YouTubeSuperChatEvent.cs](../../src/VyralBot.Integrations/YouTube/Events/YouTubeSuperChatEvent.cs) | Structured Super Chat / Super Sticker event (amount, currency, comment) |
| [CommandTimerService.cs](../../src/VyralBot.UI/Services/CommandTimerService.cs) | Timer sends: the YouTube case sends via the connected service and skips (unstamped) while offline |
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

- **`Platform` row (Name="YouTube")**: AccessToken/RefreshToken (**plaintext**), TokenExpiresAt, Username (channel title), ClientId/ClientSecret (the secret column is DPAPI-wrapped via `SecretProtector` — see [Settings → Security](../settings/overview.md#security-category)), a Google Project ID, Status, `YoutubeChatEnabled` / `YoutubeChatAutoStart`.
- No credential JSON file — unlike Twitch, everything lives in the DB row.
- No webhook subscriptions, no ngrok dependency. See [Web Server & ngrok](../settings/web-server-ngrok.md) for what does need it.
- The YouTube chat-settings toggle persists `YoutubeChatEnabled` and publishes a `PlatformChatRoutingChangedEvent`; see [Chat Settings](../chat/chat-settings.md).

## Status & Known Issues

How YouTube's capabilities compare across the other platforms: [Multi-Platform Parity](multi-platform.md).

- **No follow/raid events** — memberships and Super Chats/Super Stickers are surfaced (alerts, sounds family, currency, statistics — see [Super Chats](#super-chats--super-stickers); memberships additionally drive the `membership` workflow trigger, see [Memberships](#memberships-the-membership-trigger)), but YouTube has no follow/raid concept in this integration, so no such triggers exist; follower-based giveaway/poll eligibility passes `false` (KI-31). Super Chats do not dispatch a workflow trigger key yet.
- **Works only while a broadcast exists** — sending (and command timers) fail or skip with "You must be streaming live" when offline; the service otherwise idles in a 30-second discovery loop, which is by design. Timer ticks while offline are skipped and retried on the next tick.
- **Tokens stored unencrypted in the DB** — same caveat as Kick; inconsistent with Twitch's DPAPI-encrypted tokens. See the [known issues ledger](../reference/known-issues.md).
- **Chat history is persisted (fixed 2026-08-15)** — `DataChatMessageStore` maps YouTube events both directions and the Chat Window's Load History loads them (KI-9 resolved).
- **OAuth lives in the UI layer** — the auth flow is implemented in `YouTubeWebSetupViewModel` rather than an Integrations service, so it is untestable without the wizard and duplicates patterns held elsewhere.
- **Settings-page YouTube card is disabled with an explanatory tooltip** — YouTube is configured through the Platforms wizard; the Settings → Platforms card explains this instead of silently doing nothing (KI-17 resolved). See the [known issues ledger](../reference/known-issues.md).
