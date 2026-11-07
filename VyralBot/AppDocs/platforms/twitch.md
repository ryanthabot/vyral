# Twitch

> Twitch is the most complete integration: OAuth with a very wide scope list, chat over a lightweight WebSocket IRC client, and follow/sub/raid/cheer/**channel-points redemption** events over EventSub WebSockets — no ngrok or webhook server needed. It is also the auto-moderator's strongest platform: Twitch verdicts (timeout / ban / delete) are enforced natively through Helix.

## Overview

Connecting Twitch means running the built-in wizard once: you sign in to Twitch, register an application in the Twitch developer console, and authorize Vyral. After that Vyral keeps a persistent WebSocket connection to Twitch chat (messages in and out) and a second WebSocket for EventSub events (follows, subs, resubs, gift subs, raids, cheers).

Everything runs over outbound WebSocket connections, so Twitch — unlike [Kick](kick.md) — does not need the [ngrok web server](../settings/web-server-ngrok.md) at all.

Twitch chat **and** events both work: since the 2026-08-15 fix, both auth flows persist `Platform.UserId` (fetched from Helix `GET /users` after token exchange), which is what the EventSub connection requires. Accounts authorized before the fix need one re-auth to populate the field. See [Status & Known Issues](#status--known-issues).

## User Guide

### Before you start

You need a Twitch account (the bot speaks as this account — normally your own broadcaster account) and the ability to create an app at the [Twitch developer console](https://dev.twitch.tv/console/apps). Two-factor authentication must be enabled on the Twitch account to use the developer console.

### Connecting Twitch (wizard walkthrough)

1. Open **Platforms** in the sidebar and click the **Twitch** card. The wizard opens with four steps.
2. **Step 1: Sign In to Twitch.** Click to open the embedded browser on `https://www.twitch.tv/login`. Sign in as the account Vyral should act as, then continue to the next step.
3. **Step 2: Create Application.** The wizard navigates to `https://dev.twitch.tv/console/apps`.
   - Click **Register Your Application** on the Twitch site.
   - For the OAuth Redirect URL, use exactly `http://localhost:3000` — the wizard has a **COPY REDIRECT URI** button for this.
   - Pick an OAuth category (any works; the wizard's guidance covers it) and create the app.
   - Copy the app's **Client ID** and generate/copy a **Client Secret** on the Twitch page.
   - Use the wizard's **CLIENT ID** / **CLIENT SECRET** buttons — a popup asks you to paste each value.
4. **Step 3: Authorize Account.** The wizard opens Twitch's authorization page listing the permissions Vyral requests (it is a long list). Click **Authorize**. The page will try to redirect to `http://localhost:3000`; the wizard silently intercepts it and moves on — you should never see a "can't connect" error page. If something goes wrong, the wizard offers a manual fallback where you paste the full callback URL.
5. **Step 4: Test Connection.** Click **SEND TEST MESSAGE** to have Vyral post a message to your channel. On success, click finish.

The card now shows the **Twitch Connection** view: your username, masked Client ID/Secret, the static "OAuth Permissions" checklist, and RECONNECT/DISCONNECT. Your Twitch chat appears in the [Chat Window](../chat/chat-window.md), and a Twitch dashboard entry appears in the sidebar with a live preview of your channel.

### After connecting

- **Chat** flows both ways immediately. [Commands](../chat-features/commands.md) typed in chat are parsed, and messages you send from the chat box go to Twitch ([Sending Messages](../chat/sending-messages.md)).
- **Events** (follows, subs, resubs, gift subs, raids, cheers, **Channel Points redemptions**) trigger [event reactions](../chat/event-reactions.md) — sounds, alerts, auto-replies, currency bonuses, and statistics rows.
- **Channel Points redemptions are live** (since 2026-11-07): a viewer redeeming a custom reward posts a 🎯 chat line ("🎯 {user} redeemed \"{reward}\" ({cost} points)"), plays the **Twitch - Reward Redeemed** sound, fires the "Reward Redeemed!" alert, honors the per-event auto-reply template, writes a statistics row, and dispatches the `rewardredeemed` workflow trigger (see [Channel Points redemptions](#channel-points-redemptions)).
- On each launch, Twitch auto-reconnects using the saved (encrypted) tokens. You do not sign in again.

### Setting up a channel-points reward effect

Redemptions arrive as their own event type — not a chat message — so the three layers of the reaction stack each have a "Reward Redeemed" slot. A complete setup:

1. **Sound** — [Sounds Management](../stream-tools/sounds.md): select a sound in the library, click **Assign** on the **Twitch - Reward Redeemed** row, set its volume. This is the only place a per-redemption sound can be mapped (the row exists since 2026-11-07).
2. **Chat line + auto-reply** — [Chat Settings](../chat/chat-settings.md) → the Reward Redeemed row: **Show in chat** toggles the 🎯 line; **Auto-reply** + a template sends your message to Twitch chat per redemption (`{username}`, `{reward}`, `{cost}` resolve). The "Reward Redeemed!" [alert](../stream-tools/visual-alerts.md) fires with the family.
3. **Anything more** (custom logic, variables in commands, timeouts, role grants) — a workflow: [Automation](../chat-features/automation.md) → NEW FLOW → **Event** trigger, event type **Reward Redeemed** (normalizes to the `rewardredeemed` key; authored spellings like "redemption"/"redeemed" also work), platform filter **Twitch** (or leave it empty to also catch [Kick](kick.md) reward redemptions with the same flow). The flow receives `{username}`, `{userId}`, `{reward}`, `{cost}`, and the viewer's reward input as the message variables. Wire any blocks after that — a Send Message thanking the redeemer is the classic start.
4. **Test before going live** — [Event Testing](../chat-features/event-testing.md) publishes a synthetic Twitch redemption through the real pipeline: chat line, sound, alert, auto-reply, statistics row, and any `rewardredeemed` flows all fire exactly as live.

Remember the one prerequisite: the stored Twitch token must carry the `channel:read:redemptions` scope — connections authorized before the scope was added need one **RECONNECT** (re-authorize) for redemptions to arrive at all.

### Reconnecting and disconnecting

When the Twitch chat connects or drops, the Chat window posts a gold System line ("Twitch chat connected — listening to #channel" / "Twitch chat disconnected") so you can confirm the chat is live at a glance. App-only — never broadcast.


- **RECONNECT** re-opens the wizard at step 3 so you re-authorize. This is deliberate: the app re-asks Twitch for consent rather than silently refreshing an expired grant.
- **DISCONNECT** removes Twitch completely — the stored credentials row is deleted from the database. To use Twitch again you must run the whole wizard. This is stricter than every other platform (see [Platforms overview](overview.md)).
- If chat cannot connect at launch (for example, the stored token was revoked), the platform shows **"Connected (Chat offline)"** — the saved configuration is fine, only the live chat socket failed.

## How It Works (Developer)

### Auth (`TwitchOAuthService`)

OAuth 2.0 authorization-code flow against `https://id.twitch.tv/oauth2/authorize` and `/token`, with a fixed redirect URI of `http://localhost:3000` (`TwitchOAuthService.cs:25`), a random CSRF `state` that is verified on callback, and `force_verify=true` so Twitch shows the consent screen every time. There are two callback mechanisms:

1. **System browser + local `HttpListener`** — `AuthenticateAsync` (`TwitchOAuthService.cs:43-264`) opens the default browser and listens on port 3000 for the redirect (5-minute timeout, `HttpListener` at line 164). Used by `PlatformsViewModel.ConnectTwitchAsync`.
2. **WebView2-embedded flow** — used by the setup wizard. `BuildAuthorizationUrl()` + `HandleOAuthCallbackAsync(url)` (`TwitchOAuthService.cs:336-420`); the wizard subscribes to WebView2 `NavigationStarting` and cancels any navigation to `http://localhost:3000` (`TwitchWebSetupViewModel.cs:358-391`), parsing the code client-side. No listener needed.

**Scopes.** The scope list is huge — roughly 75 scopes covering chat, moderation, subs, bits, redemptions, raids, whispers, polls, predictions, schedule, stream key, ads, guest star, emotes, and more. The list exists **twice**: inline in `AuthenticateAsync` (`TwitchOAuthService.cs:58-134`) and again in `GetScopes()` (`TwitchOAuthService.cs:422-501`). They must be kept in sync manually.

**Token storage.** Client ID/Secret go to a plaintext JSON file, `{profile}/credentials/twitch_credentials.json` (`SaveCredentialsAsync`, line 269), and the `ClientSecret` **column in the DB** is additionally wrapped with the app-wide DPAPI protector (`SecretProtector`, `v1:` envelope — see [Settings → Security](../settings/overview.md#security-category)). The user access/refresh tokens are stored in the DB `Platform` row **DPAPI-encrypted** (CurrentUser scope) via static `Encrypt`/`Decrypt` (lines 708-728) — the only platform that encrypts its OAuth tokens on top of the shared protector. `TokenExpiresAt` is set to `Now + 4h`. Token refresh is `RefreshAccessTokenAsync` (line 620), used by `TwitchService` when IRC reports an auth failure.

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
| `channel.channel_points_custom_reward_redemption.add` | 1 | `TwitchChannelPointsRedeemEvent` (live since 2026-11-07) |

The redemption subscription is wrapped in its own try/catch inside `SubscribeToEventsAsync`: if the stored token predates the redemption scope (`channel:read:redemptions` is in the current ~75-scope request list), the failure is logged and the other six subscriptions stay healthy — redemptions are additive, not load-bearing. Re-authorize once to gain the scope on an older connection.

### Channel Points redemptions

`OnChannelPointsCustomRewardRedemptionAdd` (TwitchEventSubService.cs) publishes `TwitchChannelPointsRedeemEvent` — a dedicated redeem event, **not** a chat message (this mirrors Kick's `OnRewardRedeemed`, so redemptions never spam the chat-message pipeline). The event carries `Username`, `UserId`, `RewardId`, `RewardTitle`, `RewardCost`, optional `UserInput`, `Status`, and `RedeemedAt`. From there:

- **ChatViewModel** (`OnTwitchChannelPointsRedeemAsync`) renders the 🎯 chat line (subject to the Twitch filter and the "RewardRedeemed" `ChatEventSetting.ShowInChat` gate), plays the sound mapped to **Twitch / redemption**, shows the "Reward Redeemed!" alert, sends the per-event auto-reply when enabled (`{username}`, `{reward}`, `{cost}` variables available), and writes a `RewardRedeemed` statistics row.
- **WorkflowTriggerService** subscribes the same event and dispatches the normalized trigger key **`rewardredeemed`** on platform "Twitch" with variables `userId`, `reward`, `cost` — the same key Kick redemptions use, so one Event-triggered flow can serve both platforms (an empty platform filter matches either). Authored aliases like "Reward Redeemed" / "reward_redeemed" normalize onto the key.

### Moderation enforcement (Helix)

Twitch is one of the two platforms where the [auto-moderator](../architecture/moderator-guide.md#auto-mod-platform-parity) enforces verdicts natively. `TwitchService` implements the moderation surface on Helix:

- `TimeoutUserAsync(username, duration, reason)` → Helix timeout (a timeout also purges the sender's recent messages).
- `BanUserAsync(username, reason)` → Helix ban.
- `DeleteMessageAsync(messageId)` → Helix chat-message delete; when the pipeline has no message id the adapter's `TwitchModerationAdapter` degrades a Delete verdict to a **1-second timeout**, which still removes the offending line (the purge side effect) — tracked as KI-51 in the [known issues ledger](../reference/known-issues.md).
- `SendNoticeAsync` posts a chat notice for warn-level verdicts.

All of this requires the bot/broadcaster account to hold moderator powers in its own channel (a broadcaster is implicitly a moderator) and the moderation scopes from the standard authorization. See the parity table in the [Moderator Guide](../architecture/moderator-guide.md#auto-mod-platform-parity).

**UserId — set at authorization (fixed 2026-08-15).** `TwitchService.ConnectAsync` only starts EventSub if the `Platform` row has both `UserId` and `ClientId` (`TwitchService.cs:112-124`):

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

Both `TwitchWebSetupViewModel.FinishSetupAutomaticallyAsync` (the wizard) and `PlatformsViewModel.ConnectTwitchAsync` (the system-browser flow) now fetch the authenticated user via Helix `GET /users` after token exchange and write `platform.UserId` (never clobbering an existing id). The reconnect flow no longer wipes `ClientId` (a latent bug fixed in the same pass), so RECONNECT keeps EventSub connectable. The `channel.follow` v2 subscription also requires the user to be the broadcaster/moderator, which is why both `broadcaster_user_id` and `moderator_user_id` conditions use the same ID.

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
| [TwitchEventSubService.cs](../../src/VyralBot.Integrations/Twitch/TwitchEventSubService.cs) | TwitchLib EventSub WebSocket client; subscribes to the seven event types (incl. channel-points redemptions) |
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

- **`Platform` row (Name="Twitch")** in the per-profile database: `AccessToken`/`RefreshToken` (DPAPI-encrypted), `TokenExpiresAt` (connect time + 4h), `Username`, `UserId` (set by both auth flows via Helix `GET /users` — fixed 2026-08-15), `ClientId`/`ClientSecret` (the secret column DPAPI-wrapped via `SecretProtector`), `Status`, `TwitchChatEnabled` / `TwitchChatAutoStart`.
- **`{profile}/credentials/twitch_credentials.json`** — Client ID and Client Secret in plaintext JSON.
- **DB variables** — `twitch-followercount` and `twitch-subscribercount` are incremented by EventSub events (only when those variables already exist).
- **Per-event reactions for redemptions** — the `Twitch / RewardRedeemed` row on the [Sounds](../stream-tools/sounds.md) page and the "RewardRedeemed" row in [Chat Settings](../chat/chat-settings.md) (chat line, sound, alert, auto-reply).
- **Follower eligibility** — giveaway/poll follower checks use Helix `channels/followers` with a 5-minute cache; this requires the stored user token to carry the `moderator:read:followers` scope (KI-33 in the [known issues ledger](../reference/known-issues.md)) — new authorizations request it.
- Chat display and per-event auto-reply settings live under [Chat Settings](../chat/chat-settings.md); per-event sounds under [Sounds](../stream-tools/sounds.md).

## Status & Known Issues

How Twitch's capabilities compare across the other platforms: [Multi-Platform Parity](multi-platform.md).

- **EventSub connects (fixed 2026-08-15)** — both auth flows persist `Platform.UserId` via Helix `GET /users`, and the reconnect flow no longer wipes `ClientId`, so follow/sub/resub/gift/raid/cheer/redemption events, event-based sounds/alerts/auto-replies, and the follower/sub counters all work after (re)authorization. Accounts connected before the fix should re-authorize once. Tracked in the [known issues ledger](../reference/known-issues.md) (KI-2 resolved).
- **Redemptions need the `channel:read:redemptions` scope** — connections authorized before the scope list included it see a logged subscription failure and simply get no redemption events (the other six topics are unaffected); re-authorize once to turn them on.
- **Follower eligibility needs a scope** — the giveaway/poll follower check requires `moderator:read:followers` on the stored user token and degrades to `false` without it (KI-33). [known issues ledger](../reference/known-issues.md)
- **Duplicate scope lists** — the ~75-scope list is maintained twice (`TwitchOAuthService.cs:58-134` and `422-501`); edits to one silently diverge from the other. See the [known issues ledger](../reference/known-issues.md).
- **DISCONNECT deletes the row** — Twitch is the only platform that wipes its configuration on disconnect; reconnecting requires the full wizard. See [Platforms overview](overview.md).
- **"Chat Enabled" toggle works for routing** — it persists `TwitchChatEnabled` and publishes the routing event; messages are still stored regardless of the flag (fixed 2026-08-15).
- **First auto-connect force-enables chat** — a successful auto-connect silently flips `TwitchChatEnabled` to true in the DB (intended behavior — KI-14).
- **Massive scope request** — every authorization asks for ~75 scopes far beyond what the app uses (`channel:read:stream_key` among them); Twitch shows users the full list at consent.
