# Twitch

> Twitch is the most complete integration: OAuth with a very wide scope list, chat over a lightweight WebSocket IRC client, and follow/sub/raid/cheer events over EventSub WebSockets — no ngrok or webhook server needed.

## Overview

Connecting Twitch means running the built-in wizard once: you sign in to Twitch, register an application in the Twitch developer console, and authorize VyralBot. After that VyralBot keeps a persistent WebSocket connection to Twitch chat (messages in and out) and a second WebSocket for EventSub events (follows, subs, resubs, gift subs, raids, cheers).

Everything runs over outbound WebSocket connections, so Twitch — unlike [Kick](kick.md) — does not need the [ngrok web server](../settings/web-server-ngrok.md) at all.

One honest warning up front: chat works, but the EventSub event half is almost certainly **not connecting** in current builds because of a missing database field (`UserId`). Twitch follow/sub/raid/cheer reactions may never fire. See [Status & Known Issues](#status--known-issues).

## User Guide

### Before you start

You need a Twitch account (the bot speaks as this account — normally your own broadcaster account) and the ability to create an app at the [Twitch developer console](https://dev.twitch.tv/console/apps). Two-factor authentication must be enabled on the Twitch account to use the developer console.

### Connecting Twitch (wizard walkthrough)

1. Open **Platforms** in the sidebar and click the **Twitch** card. The wizard opens with four steps.
2. **Step 1: Sign In to Twitch.** Click to open the embedded browser on `https://www.twitch.tv/login`. Sign in as the account VyralBot should act as, then continue to the next step.
3. **Step 2: Create Application.** The wizard navigates to `https://dev.twitch.tv/console/apps`.
   - Click **Register Your Application** on the Twitch site.
   - For the OAuth Redirect URL, use exactly `http://localhost:3000` — the wizard has a **COPY REDIRECT URI** button for this.
   - Pick an OAuth category (any works; the wizard's guidance covers it) and create the app.
   - Copy the app's **Client ID** and generate/copy a **Client Secret** on the Twitch page.
   - Use the wizard's **CLIENT ID** / **CLIENT SECRET** buttons — a popup asks you to paste each value.
4. **Step 3: Authorize Account.** The wizard opens Twitch's authorization page listing the permissions VyralBot requests (it is a long list). Click **Authorize**. The page will try to redirect to `http://localhost:3000`; the wizard silently intercepts it and moves on — you should never see a "can't connect" error page. If something goes wrong, the wizard offers a manual fallback where you paste the full callback URL.
5. **Step 4: Test Connection.** Click **SEND TEST MESSAGE** to have VyralBot post a message to your channel. On success, click finish.

The card now shows the **Twitch Connection** view: your username, masked Client ID/Secret, the static "OAuth Permissions" checklist, and RECONNECT/DISCONNECT. Your Twitch chat appears in the [Chat Window](../chat/chat-window.md), and a Twitch dashboard entry appears in the sidebar with a live preview of your channel.

### After connecting

- **Chat** flows both ways immediately. [Commands](../chat-features/commands.md) typed in chat are parsed, and messages you send from the chat box go to Twitch ([Sending Messages](../chat/sending-messages.md)).
- **Events** (follows, subs, resubs, gift subs, raids, cheers) would trigger [event reactions](../chat/event-reactions.md) — subject to the EventSub gap below.
- On each launch, Twitch auto-reconnects using the saved (encrypted) tokens. You do not sign in again.

### Reconnecting and disconnecting

- **RECONNECT** re-opens the wizard at step 3 so you re-authorize. This is deliberate: the app re-asks Twitch for consent rather than silently refreshing an expired grant.
- **DISCONNECT** removes Twitch completely — the stored credentials row is deleted from the database. To use Twitch again you must run the whole wizard. This is stricter than every other platform (see [Platforms overview](overview.md)).
- If chat cannot connect at launch (for example, the stored token was revoked), the platform shows **"Connected (Chat offline)"** — the saved configuration is fine, only the live chat socket failed.

## How It Works (Developer)

### Auth (`TwitchOAuthService`)

OAuth 2.0 authorization-code flow against `https://id.twitch.tv/oauth2/authorize` and `/token`, with a fixed redirect URI of `http://localhost:3000` (`TwitchOAuthService.cs:25`), a random CSRF `state` that is verified on callback, and `force_verify=true` so Twitch shows the consent screen every time. There are two callback mechanisms:

1. **System browser + local `HttpListener`** — `AuthenticateAsync` (`TwitchOAuthService.cs:43-264`) opens the default browser and listens on port 3000 for the redirect (5-minute timeout, `HttpListener` at line 164). Used by `PlatformsViewModel.ConnectTwitchAsync`.
2. **WebView2-embedded flow** — used by the setup wizard. `BuildAuthorizationUrl()` + `HandleOAuthCallbackAsync(url)` (`TwitchOAuthService.cs:336-420`); the wizard subscribes to WebView2 `NavigationStarting` and cancels any navigation to `http://localhost:3000` (`TwitchWebSetupViewModel.cs:358-391`), parsing the code client-side. No listener needed.

**Scopes.** The scope list is huge — roughly 75 scopes covering chat, moderation, subs, bits, redemptions, raids, whispers, polls, predictions, schedule, stream key, ads, guest star, emotes, and more. The list exists **twice**: inline in `AuthenticateAsync` (`TwitchOAuthService.cs:58-134`) and again in `GetScopes()` (`TwitchOAuthService.cs:422-501`). They must be kept in sync manually.

**Token storage.** Client ID/Secret go to a plaintext JSON file, `{profile}/credentials/twitch_credentials.json` (`SaveCredentialsAsync`, line 269). The user access/refresh tokens are stored in the DB `Platform` row **DPAPI-encrypted** (CurrentUser scope) via static `Encrypt`/`Decrypt` (lines 708-728) — the only platform that encrypts tokens. `TokenExpiresAt` is set to `Now + 4h`. Token refresh is `RefreshAccessTokenAsync` (line 620), used by `TwitchService` when IRC reports an auth failure.

### Chat: WebSocket IRC (`LightweightTwitchIrc`)

A purpose-built IRC-over-WebSocket client (`LightweightTwitchIrc.cs`, file header notes it replaced TwitchLib IRC to save ~30 MB of memory): connects to `wss://irc-ws.chat.twitch.tv:443`, sends `PASS oauth:{token}` / `NICK {user}`, requests `twitch.tv/tags commands membership` capabilities, JOINs the channel, and runs a keep-alive PING loop (lines 35-79). Incoming PRIVMSGs are parsed into `TwitchChatMessageEvent` and published fire-and-forget via `Task.Run` — a comment at `TwitchService.cs:356` documents an earlier blocking bug where `.GetAwaiter().GetResult()` stalled the read loop. Messages are written to `IChatMessageStore` regardless of the `TwitchChatEnabled` flag (`TwitchService.cs:370-381`); the flag only gates display.

### Events: EventSub WebSocket (`TwitchEventSubService`)

TwitchLib's `EventSubWebsocketClient` is used — WebSockets, not webhooks, so nothing inbound is required. After the socket connects, `SubscribeToEventsAsync` (`TwitchEventSubService.cs:97-168`) creates Helix subscriptions with `EventSubTransportMethod.Websocket` for:

| EventSub type | Version | Published as |
|---|---|---|
| `channel.follow` | 2 | `TwitchFollowEvent` |
| `channel.subscribe` | 1 | `TwitchSubscribeEvent` |
| `channel.subscription.message` | 1 | `TwitchResubEvent` |
| `channel.subscription.gift` | 1 | `TwitchGiftSubEvent` |
| `channel.raid` | 1 | `TwitchRaidEvent` |
| `channel.cheer` | 1 | `TwitchCheerEvent` |

(Additional typed events exist in `Twitch/Events/` — `TwitchChannelPointsEvent`, `TwitchChannelPointsRedeemEvent` — but no subscription is created for them.)

**The UserId gate.** `TwitchService.ConnectAsync` only starts EventSub if the `Platform` row has both `UserId` and `ClientId` (`TwitchService.cs:112-124`):

```csharp
var twitchPlatform = await _dataService.GetPlatformByNameAsync("Twitch");
if (twitchPlatform != null
    && !string.IsNullOrEmpty(twitchPlatform.UserId)
    && !string.IsNullOrEmpty(twitchPlatform.ClientId))
{
    await _eventSubService.ConnectAsync(twitchPlatform.ClientId, accessToken, twitchPlatform.UserId);
}
else
{
    _logger.LogWarning("Cannot connect EventSub - missing UserId or ClientId in Platform settings");
}
```

Neither `TwitchWebSetupViewModel.FinishSetupAutomaticallyAsync` (which sets AccessToken, RefreshToken, Username, ClientId, ClientSecret — no UserId) nor `PlatformsViewModel.ConnectTwitchAsync` ever writes `Platform.UserId`. Unless the field was populated by some external means, the warning branch runs every time and no event subscriptions are created. The `channel.follow` v2 subscription also requires the user to be the broadcaster/moderator, which is why both `broadcaster_user_id` and `moderator_user_id` conditions use the same ID.

Side effects on events: `TwitchEventSubService.cs:203-236` increments the DB variables `twitch-followercount` and `twitch-subscribercount` — but only when those variables already exist (a possibly-null base).

### Reconnection

- **Auth failure** → `RefreshAndReconnectAsync` (`TwitchService.cs:184-257`): refresh the token via `RefreshAccessTokenAsync`, then reconnect IRC.
- **Unintentional disconnect** → `ReconnectAsync` (`TwitchService.cs:279-328`): up to **5 attempts**, delay `min(attempt × 5s, 30s)`.
- **EventSub** has its own retry: `ReconnectAsync` after 2 seconds (`TwitchEventSubService.cs:170-187`).

## Files & Code

| File | Role |
|---|---|
| [TwitchOAuthService.cs](../../src/VyralBot.Integrations/Twitch/TwitchOAuthService.cs) | OAuth (both flows), ~75-scope list (twice), credential file I/O, DPAPI Encrypt/Decrypt, token refresh |
| [TwitchService.cs](../../src/VyralBot.Integrations/Twitch/TwitchService.cs) | Orchestrates IRC + EventSub, reconnection, message publish/store |
| [LightweightTwitchIrc.cs](../../src/VyralBot.Integrations/Twitch/LightweightTwitchIrc.cs) | Minimal Twitch IRC-over-WebSocket client (replaced TwitchLib chat) |
| [TwitchEventSubService.cs](../../src/VyralBot.Integrations/Twitch/TwitchEventSubService.cs) | TwitchLib EventSub WebSocket client; subscribes to the six event types |
| [ITwitchService.cs](../../src/VyralBot.Integrations/Twitch/ITwitchService.cs) / [ITwitchEventSubService.cs](../../src/VyralBot.Integrations/Twitch/ITwitchEventSubService.cs) | Service interfaces |
| [Events/](../../src/VyralBot.Integrations/Twitch/Events) | Typed events: `TwitchChatMessageEvent`, `TwitchFollowEvent`, `TwitchSubscribeEvent`, `TwitchResubEvent`, `TwitchGiftSubEvent`, `TwitchRaidEvent`, `TwitchCheerEvent`, `TwitchChannelPointsEvent`, `TwitchChannelPointsRedeemEvent` |
| [TwitchWebSetupViewModel.cs](../../src/VyralBot.UI/ViewModels/TwitchWebSetupViewModel.cs) / [TwitchWebSetupView.xaml](../../src/VyralBot.UI/Views/TwitchWebSetupView.xaml) | The 4-step setup wizard |
| [PlatformsViewModel.cs](../../src/VyralBot.UI/ViewModels/PlatformsViewModel.cs) | Twitch card state, connect/reconnect/disconnect commands (`ConnectTwitchAsync` line 638, `ReconnectTwitchAsync` line 775, `DisconnectTwitch` line 828) |
| [TwitchDashboardViewModel.cs](../../src/VyralBot.UI/ViewModels/TwitchDashboardViewModel.cs) / [TwitchDashboardView.xaml](../../src/VyralBot.UI/Views/TwitchDashboardView.xaml) | Dashboard with embedded stream preview (`player.twitch.tv`) |
| [TwitchSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/TwitchSettingsViewModel.cs) / [TwitchChatSettingsViewModel.cs](../../src/VyralBot.UI/ViewModels/TwitchChatSettingsViewModel.cs) | Settings stub ("coming soon") and per-event chat display/auto-reply settings |

The IRC handshake in `LightweightTwitchIrc.cs:52-63` — the whole chat transport in six lines:

```csharp
await _webSocket.ConnectAsync(new Uri("wss://irc-ws.chat.twitch.tv:443"), _cts.Token);

await SendRawAsync($"PASS oauth:{oauthToken}");
await SendRawAsync($"NICK {username}");

// Request capabilities for tags (badges/colors), commands, and membership
await SendRawAsync("CAP REQ :twitch.tv/tags twitch.tv/commands twitch.tv/membership");

await SendRawAsync($"JOIN #{Channel}");
```

And the reconnect backoff policy in `TwitchService.cs:279-290`:

```csharp
private async Task ReconnectAsync(CancellationToken cancellationToken)
{
    const int maxRetries = 5;
    int retryCount = 0;

    while (!cancellationToken.IsCancellationRequested && retryCount < maxRetries)
    {
        retryCount++;
        var delay = TimeSpan.FromSeconds(Math.Min(retryCount * 5, 30));
        await Task.Delay(delay, cancellationToken);
        // ... attempt reconnect
    }
}
```

## Data & Settings

- **`Platform` row (Name="Twitch")** in the per-profile database: `AccessToken`/`RefreshToken` (DPAPI-encrypted), `TokenExpiresAt` (connect time + 4h), `Username`, `ClientId`/`ClientSecret` (plaintext columns), `Status`, `TwitchChatEnabled` / `TwitchChatAutoStart`. `UserId` is read by EventSub connect but never written by any Twitch flow — that is the gap.
- **`{profile}/credentials/twitch_credentials.json`** — Client ID and Client Secret in plaintext JSON.
- **DB variables** — `twitch-followercount` and `twitch-subscribercount` are incremented by EventSub events (only if the variables already exist).
- Chat display and per-event auto-reply settings live under [Chat Settings](../chat/chat-settings.md); per-event sounds under [Sounds](../stream-tools/sounds.md).

## Status & Known Issues

- **EventSub UserId gap (critical, feature-silencing)** — EventSub connect requires `Platform.UserId` (`TwitchService.cs:112-124`); no Twitch code path sets it, so follow/sub/resub/gift/raid/cheer events almost certainly never arrive, which silences the Twitch half of [event reactions](../chat/event-reactions.md), event-based sounds/alerts, and follower/sub counters. A fix is one `platform.UserId = …` away (fetch it from Helix `users` after token exchange). Tracked in the [known issues ledger](../reference/known-issues.md).
- **Duplicate scope lists** — the ~75-scope list is maintained twice (`TwitchOAuthService.cs:58-134` and `422-501`); edits to one silently diverge from the other. See the [known issues ledger](../reference/known-issues.md).
- **DISCONNECT deletes the row** — Twitch is the only platform that wipes its configuration on disconnect; reconnecting requires the full wizard. See [Platforms overview](overview.md).
- **"Chat Enabled" toggle is cosmetic for routing** — messages are stored regardless, and the platform-routing event that would filter them live is never published. Details in the [known issues ledger](../reference/known-issues.md).
- **First auto-connect force-enables chat** — a successful auto-connect silently flips `TwitchChatEnabled` to true in the DB.
- **Massive scope request** — every authorization asks for ~75 scopes far beyond what the app uses (`channel:read:stream_key` among them); Twitch shows users the full list at consent.
