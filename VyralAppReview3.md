# VyralBot Review — Platforms Section (Connections / Authorization / Integration Layer)

Scope: the Platforms page, per-platform OAuth setup flows, chat/event ingestion (WebSocket vs webhook vs polling), webhook receiving + ngrok tunneling, and how platform events fan out into the app. Read-only review; findings cite file paths.

---

## 1. Platforms Page (UI overview)

- View: `src/VyralBot.UI/Views/PlatformsView.xaml` — "Platform Connections" header, subtitle "Select a platform to connect".
- ViewModel: `src/VyralBot.UI/ViewModels/PlatformsViewModel.cs`.
- Platform cards shown in a selection grid (PlatformsView.xaml lines ~89–372):
  - **Twitch** — "Live streaming platform"
  - **Kick** — "Live streaming platform"
  - **YouTube** — "Live streaming platform"
  - **TikTok** — "Live streaming platform"
  - **Discord** — "Communication platform"
  - **OBS** — "Coming Soon / Not Available" (placeholder card only)
- Each platform has visibility flags for: platform selection grid, a setup wizard (`Show*Setup`, hosting a `ContentControl` for the web setup view at PlatformsView.xaml:384–400: TwitchSetupContainer, KickSetupContainer, YouTubeSetupContainer, TikTokSetupContainer, DiscordSetupContainer), and a "connection" detail view (`Show*Connection`) with RECONNECT / DISCONNECT buttons.
- Connection views show masked Client ID/Client Secret and a static "OAuth Permissions" checklist (e.g., Twitch card lists chat read/write, moderation, channel management, polls, subs, bits, clips, channel rewards, raids, whispers, announcements, schedule, stream key, extensions, analytics, AutoMod, blocked terms, VIPs/mods, shield mode, shoutouts, guest star, ads, emotes/chat color — PlatformsView.xaml:541–570). Note: this checklist is hardcoded XAML, not driven by actual granted scopes.

### State model
- `PlatformsViewModel` tracks per-platform `*Status`, `Is*Connected`, `*Username`, mirroring rows in the database via `IDataService.GetPlatformByNameAsync("Twitch"|"Kick"|"YouTube"|"TikTok"|"Discord")` (PlatformsViewModel.cs:380–437). Connection = `platform.Status == "Connected"`.
- Credential presence is also probed on disk per profile: `twitch_credentials.json` and `kick_credentials.json` under the profile credentials folder (PlatformsViewModel.cs:505–559), via `IProfileService.GetProfileCredentialsPath`.
- On setup completion (each WebSetup VM fires `SetupCompleted`), the VM reloads platform data, auto-connects the platform service, fires `_onPlatformConnectionChanged` (used by MainViewModel to refresh sidebar/chat) and `_onNavigateToPlatforms` (PlatformsViewModel.cs:189–331).

### Lifecycle notes
- **Twitch connect** (`ConnectTwitchAsync`, PlatformsViewModel.cs:638): runs `TwitchOAuthService.AuthenticateAsync()`, stores encrypted access/refresh tokens in DB (`Platform` entity), `TokenExpiresAt = Now+4h`, Status="Connected". Auto-connect to IRC happens in `LoadTwitchPlatformAsync` (line 564) which decrypts tokens and calls `ITwitchService.ConnectAsync`; on failure status becomes "Connected (Chat offline)". `JoinChannelAsync` (line 711) manually connects chat; `ReconnectTwitchAsync` (line 775) re-opens the setup wizard at step 3 (OAuth step) rather than silently refreshing. `DisconnectTwitch` (line 831) deletes the platform row entirely and disposes the wizard's WebView2.
- **Kick** follows the same pattern, but disconnect (`DisconnectKick`, line 1232) keeps the DB row and only nulls credentials (AccessToken/RefreshToken/UserId/ChannelId/ChatroomId/ClientId/ClientSecret). Kick tokens are stored **unencrypted** in the DB — comment at line 1011: "Kick tokens are NOT encrypted (KickLib manages token security)". Auto-connect attempts a token refresh first via `KickLibOAuthService.RefreshTokenAsync()` (lines 1017–1038).
- **YouTube / TikTok / Discord** have lighter lifecycles: setup VM → `Load*PlatformAsync` (DB status read only; YouTube additionally auto-connects chat with stored tokens, PlatformsViewModel.cs:265–301). Disconnect nulls credentials but keeps the row (YouTube:1409, TikTok:1548, Discord:1680).

---

## 2. Ngrok integration (cross-platform infrastructure)

File: `src/VyralBot.Api/Services/NgrokService.cs`.

- Manages a child `ngrok http {port} --region us --authtoken {token}` process (NgrokService.cs:60).
- Requires an ngrok auth token (rejected otherwise, line 46–50); token persisted as `{profile}/credentials/ngrok_credentials.json` (`SaveAuthTokenAsync` line 261 / `LoadAuthTokenAsync` line 290).
- Public URL obtained by polling ngrok's local API `http://127.0.0.1:4040/api/tunnels`, first https tunnel (line 160–191); retries 5x2s at startup.
- A `Timer` polls every 30s for URL changes and raises `PublicUrlChanged` (line 193–234) so webhook subscriptions can be re-pointed when the tunnel URL rotates.
- Tunnel detection of "installed" runs `ngrok version` (line 236).
- Ngrok UI setup: `src/VyralBot.UI/Views/NgrokSetupView.xaml` (+ Wizard variant) and ViewModels `NgrokSetupViewModel.cs`, `NgrokSetupWizardViewModel.cs`.

(Details on which services consume the tunnel URL are in the Webhooks section below.)

---

## 3. Embedded web server + webhook pipeline (src/VyralBot.Api)

### WebServerService (`src/VyralBot.Api/Services/WebServerService.cs`)
- Embedded **Kestrel** ASP.NET Core server started on demand, `ListenLocalhost(port)` HTTP1+2 (WebServerService.cs:53–59). Default port 5000 (parameter default; actual port comes from `WebServerSettings` in DB).
- Pulls the shared `IEventBus` and `ILoggerFactory` from the parent WPF DI container into the mini-host (lines 61–63), so webhook controllers publish into the same in-process event bus as the chat services.
- Middleware order: `UseWebSockets` → `UseStaticFiles` (wwwroot audio.html/sounds for OBS browser sources) → `UseRouting` → `KickWebhookValidationMiddleware` → `MapControllers` (lines 83–87).
- Also maps a `/audio-ws` WebSocket endpoint and wires `AudioWebSocketBroadcaster.BroadcastAsync` static delegate to fan out sound triggers to connected OBS browser-source pages (lines 90–135). This is a second, non-ngrok use of the same local web server (browser sources can hit it via the ngrok URL or LAN).
- Port availability probed by binding a loopback socket (line 227).

### WebhookCoordinatorService (`src/VyralBot.Api/Services/WebhookCoordinatorService.cs`)
- Orchestrates the whole pipeline: Kestrel server + ngrok tunnel + Kick webhook subscriptions (class doc, line 8–11).
- `StartAsync()` sequence (lines 86–148): start Kestrel on `WebServerSettings.Port` → load ngrok auth token from profile credentials file → `ngrok.StartTunnelAsync(port, token, settings.NgrokRegion)` → persist `CurrentTunnelUrl` + `LastStarted` to `WebServerSettings` in DB.
- `InitializeAsync()` (line 49): auto-starts the stack on app launch if `AutoStartOnLaunch` is set AND an ngrok auth token exists; called from `MainViewModel.OnProfileLoadedAsync` (MainViewModel.cs:227–228).
- `EnableKickWebhooksAsync(accessToken, broadcasterUserId)` (line 195):
  - Webhook endpoint URL = `{ngrok.PublicUrl}/webhooks/kick` (line 250).
  - Cleans up leftover `ChatMessageSent` subscriptions and **unsubscribes ALL existing Kick subscriptions before re-subscribing** to prevent duplicates (lines 207–246; matches WEBHOOK_DUPLICATE_AUDIT.md work).
  - Subscribes to only **webhook-only** event types via KickLib: `LivestreamMetadataUpdated`, `ModerationUserBanned` (lines 251–264). Comment documents that ChannelFollowed / SubscriptionNew/Renewal/Gifts / LivestreamStatusUpdated / KicksGifted are intentionally NOT subscribed via webhooks because the **Pusher WebSocket (KickLib) takes priority** to avoid duplicates.
  - Saves each subscription to DB as `WebhookSubscription` (Platform="Kick", EventType, BroadcasterUserId, WebhookUrl, IsActive) and registers a dependency via `WebServerDependencyTracker` ("Kick" → "Real-time event webhooks") which guards against disabling the web server while needed.
- `DisableKickWebhooksAsync` (line 307) unsubscribes server-side via KickLib and deactivates DB rows.
- ngrok URL change handling (`OnNgrokUrlChanged`, line 345): updates `CurrentTunnelUrl` in DB and **warns the user to manually disable/re-enable Kick webhooks** — it cannot auto-resubscribe because the OAuth access token isn't available in that context (comment lines 374–376). Free-plan URL rotation is therefore a manual heal step.
- Kick webhooks auto-enable on startup: `MainViewModel.AutoEnableKickWebhooksAsync` (MainViewModel.cs:296–343) — only if the coordinator is running AND active Kick subscriptions exist in DB; user toggles it in `KickSettingsViewModel` (KickSettingsViewModel.cs:226 calls `EnableKickWebhooksAsync`).

### WebhooksController (`src/VyralBot.Api/Controllers/WebhooksController.cs`)
- Route `POST /webhooks/kick` (line 22). Header-driven dispatch on `Kick-Event-Type` (line 35).
- `chat.message.sent` is accepted but **ignored** — comment at line 25–26: chat comes via Pusher WebSocket in KickService; webhook only for follows/subs/etc.
- Handled types (lines 57–104): `channel.followed` → `KickFollowEvent`; `channel.unfollowed` → synthesized bot-authored `KickChatMessageEvent` ("X unfollowed the channel"); `channel.subscription.new|renewal` → `KickSubscribeEvent`; `channel.subscription.gifts` → `KickGiftSubEvent`; `livestream.status.updated`, `moderation.banned`, `kicks.gifted` → synthesized `KickChatMessageEvent`s authored by "Vyral.Bot" with badge `vyral=1`.
- Payload parsing is defensive/heuristic (tries `follower`/`user`/`sender`/`actor` JSON shapes); known data gaps logged in code: webhook payloads don't include follower count, months, gifter ID, or recipients (e.g., lines 158, 279, 318–320).
- All processed events are published to `IEventBus` (injected from the WPF host).
- `GET /webhooks/health` returns a simple health payload (line 680).

### KickWebhookValidationMiddleware (`src/VyralBot.Api/Middleware/KickWebhookValidationMiddleware.cs`)
- Applies only to `/webhooks/kick` POSTs; GET skipped (lines 25–35).
- Requires `Kick-Event-Signature`, `Kick-Event-Message-Id`, `Kick-Event-Timestamp` headers; 401 otherwise (line 45).
- Signature scheme: RSA-SHA256 over `{messageId}.{timestamp}.{body}`, public key fetched from `https://api.kick.com/public/v1/public-key`, PEM-imported, cached 1h (lines 99–196).
- Quirk/security note: "test webhooks" (messageId starting `test-` or body containing `"test":true`) **bypass signature validation** (lines 63–69) — spoofable if an attacker sets those markers.
- The controller's own `TODO: Validate signature` comment (WebhooksController.cs:51) is stale; validation actually lives in this middleware.

### KickWebhookSubscriptionService (`src/VyralBot.Api/Services/KickWebhookSubscriptionService.cs`)
- Thin wrapper over KickLib `IKickApi.EventSubscriptions`: subscribe one event type at a time with a 1s delay between calls to avoid Kick rate limiting (lines 33–67); unsubscribe single/batch; `GetActiveSubscriptionsAsync`; `ResubscribeToNewUrlAsync` (unsubscribe-all + subscribe-all; note comment line 193: "webhook URL is set in Kick developer portal" — the callback URL registered with Kick points at the ngrok tunnel).

---

## 4. Event fan-out (Core EventBus)

`src/VyralBot.Core/Services/EventBus/EventBus.cs`:
- Bounded `Channel<(IEvent, Type)>` capacity **500**, `DropOldest` backpressure, single reader, max **4 concurrent handlers** (lines 21–39; stated to comply with CONTEXT_MASTER.md).
- Typed pub/sub by exact CLR event type; handlers invoked via `DynamicInvoke` inside `Task.Run` throttled by semaphore (line 133–178).
- Platform services publish into this bus; consumers include ChatViewModel (chat UI), TriggerEngine (commands/auto-replies — other agents' scope), variable updates, etc. The webhook controller and chat services share the same singleton EventBus instance (WebServerService.cs:62).

Platform events emitted (typed event classes):
- Twitch: `TwitchChatMessageEvent`, `TwitchFollowEvent`, `TwitchSubscribeEvent`, `TwitchResubEvent`, `TwitchGiftSubEvent`, `TwitchRaidEvent`, `TwitchCheerEvent`, `TwitchChannelPointsEvent`, `TwitchChannelPointsRedeemEvent` (`src/VyralBot.Integrations/Twitch/Events/`).
- Kick: `KickChatMessageEvent`, `KickFollowEvent`, `KickSubscribeEvent`, `KickGiftSubEvent`, `KickHostEvent`, `KickKicksGiftedEvent`, `KickRewardRedeemedEvent`, `KickStreamStatusEvent` (`src/VyralBot.Integrations/Kick/Events/`).
- YouTube: `YouTubeChatMessageEvent` only (`src/VyralBot.Integrations/YouTube/Events/`).
- Cross-platform: `PlatformConnectionChangedEvent` published by KickLibChatService and YouTubeChatService on connect/disconnect (KickLibChatService.cs:149, YouTubeChatService.cs:123).
- Side-effect writes on events: Kick follower count → DB variable `kick-followercount` (KickLibChatService.cs:472); Twitch follower/sub counts incremented on `twitch-followercount` / `twitch-subscribercount` variables (TwitchEventSubService.cs:203–236) — note the Twitch counters are incremented from a possibly-null base (only increments if the variable already exists).

---

## 5. Twitch integration

UI: TwitchWebSetup wizard + "Twitch Connection" card on Platforms page; TwitchDashboardView/ViewModel.

### Auth (`src/VyralBot.Integrations/Twitch/TwitchOAuthService.cs`)
- OAuth 2.0 authorization-code flow against `https://id.twitch.tv/oauth2/authorize` / `/token`; **redirect URI fixed to `http://localhost:3000`** (line 25).
- Two callback mechanisms:
  1. System browser + local `HttpListener` on port 3000 (5-min timeout) used by `AuthenticateAsync` (lines 43–264) — invoked from PlatformsViewModel.ConnectTwitchAsync.
  2. WebView2-embedded flow used by the setup wizard: `BuildAuthorizationUrl()` + `HandleOAuthCallbackAsync(url)` (lines 336–420); the wizard watches `NavigationStarting` for `http://localhost:3000` and cancels the navigation (TwitchWebSetupViewModel.cs:361; BaseOAuthSetupViewModel.cs:371–391).
- Scopes: a very large list (~75 scopes) duplicated in two places — inline in `AuthenticateAsync` (lines 58–134) and in `GetScopes()` (lines 422–501). Includes chat read/edit, moderation, subs, bits, redemptions, raids, whispers, polls, schedule, stream key, ads, guest star, etc.
- CSRF `state` validation in both flows (lines 240–244, 390–394); `force_verify=true`.
- Client ID/Secret stored **plaintext JSON** per profile: `{profile}/credentials/twitch_credentials.json` (SaveCredentialsAsync line 269). User access/refresh tokens stored in the DB `Platform` row **DPAPI-encrypted** (`Encrypt`/`Decrypt`, CurrentUser scope, lines 707–728).
- Token refresh: `RefreshAccessTokenAsync` (line 620) used by TwitchService on auth failure.

### Chat/event ingestion
- **Chat: WebSocket IRC** — `LightweightTwitchIrc` (`src/VyralBot.Integrations/Twitch/LightweightTwitchIrc.cs`) connects to `wss://irc-ws.chat.twitch.tv:443`, `PASS oauth:{token}` / `NICK`, requests `twitch.tv/tags commands membership` caps, JOINs channel, keep-alive PING loop (lines 35–79). Explicitly built to replace TwitchLib and save ~30MB (file header).
- **Events: EventSub WebSocket** (not webhooks) — `TwitchEventSubService` (`src/VyralBot.Integrations/Twitch/TwitchEventSubService.cs`) uses TwitchLib's `EventSubWebsocketClient`; on connect it creates subscriptions via Helix for `channel.follow` v2, `channel.subscribe`, `channel.subscription.message`, `channel.subscription.gift`, `channel.raid`, `channel.cheer` with `EventSubTransportMethod.Websocket` (lines 97–168). So Twitch needs **no ngrok/webhook at all**.
- `TwitchService.ConnectAsync` (TwitchService.cs:65) connects IRC, then also connects EventSub if `Platform.UserId` and `ClientId` are present in the DB (lines 110–130) — note the PlatformsViewModel OAuth save path never sets `UserId`, so EventSub depends on it being populated elsewhere (e.g., by the web setup flow).
- Resilience: on auth failure → token refresh + reconnect (TwitchService.cs:184–257); on unintentional disconnect → up to 5 reconnect attempts with backoff capped at 30s (lines 275–328); EventSub has its own `ReconnectAsync` retry after 2s (TwitchEventSubService.cs:170–187).
- Chat messages published as `TwitchChatMessageEvent` fire-and-forget via `Task.Run` (comment at TwitchService.cs:356 notes a previous blocking bug where `.GetAwaiter().GetResult()` stalled the read loop). Messages always stored to `IChatMessageStore` regardless of `TwitchChatEnabled` (lines 370–381).

---

## 6. Kick integration

UI: KickWebSetup wizard + "Kick Connection" card on Platforms page; KickDashboardView/ViewModel; KickSettingsView (webhook toggle).

### Auth (`src/VyralBot.Integrations/Kick/KickLibOAuthService.cs`)
- Thin wrapper over KickLib `KickOAuthGenerator` (class doc: replaces a 530-line custom KickOAuthService).
- **OAuth with PKCE**; scopes: `user.read`, `channel.read`, `chat.write`, `events.subscribe`, `moderation.ban`, `kicks.read` (KickLibOAuthService.cs:41–49).
- Redirect URI `http://localhost:3000/callback` (KickWebSetupViewModel.cs:329).
- `ExchangeCodeForTokenAsync` exchanges the code, calls `api.Users.GetMeAsync` for username/userId, and stores everything in the `Platform` DB row: AccessToken, RefreshToken, TokenExpiresAt (from `expires_in`), Username, UserId, ClientId, ClientSecret, Status="Connected" (lines 66–132).
- Tokens are stored **unencrypted** in the DB (by design — comments at PlatformsViewModel.cs:1011 and MainViewModel.cs:333: "Kick tokens are NOT encrypted (KickLib manages token security)"). This contrasts with Twitch's DPAPI-encrypted tokens.
- Token refresh on demand: `RefreshTokenAsync` (line 137) and inside KickLibChatService before sending when expiry < 5 min (KickLibChatService.cs:203–213).

### Chat/event ingestion (`src/VyralBot.Integrations/Kick/KickLibChatService.cs`)
- Hybrid architecture (file header + comments at lines 131–138): **chat messages via KickLib Pusher WebSocket** (`ListenToChatRoomAsync(chatroomId)` then `ConnectAsync`, lines 134–144); channel events (follows, subs, stream status...) via **official Kick webhooks through ngrok** (see section 3). This split exists to avoid duplicate events — documented in WebhookCoordinatorService.cs:249–264 and KICK_WEBHOOK_PHASE1 docs.
- Connect requires `ChannelId` + `ChatroomId` already stored in the DB by the setup wizard; missing IDs throw "please run the setup wizard again" (lines 119–123).
- Sending uses KickLib REST `SendMessageAsUserAsync(broadcasterUserId, ...)` with the broadcaster's OAuth token — the bot speaks as the streamer's account (line 224).
- Pusher handlers publish dedicated typed events AND (for subs/gifts/hosts/stream-live) a synthesized bot-authored `KickChatMessageEvent` for chat display (lines 334–463). Ban/unban/message-deleted are only logged (lines 405–422).
- `OnFollowersUpdated` updates the `kick-followercount` variable; KickLib limitation: Pusher follow event carries **no username**, so it publishes `KickFollowEvent` with Username="Unknown" (lines 480–489) — the webhook path is the one that knows the username (WebhooksController.ProcessFollowAsync).
- Historical note: this service **replaced a WebView2-based KickService** (class doc: eliminates 40–60MB WebView2 overhead; see WEBVIEW2_REMOVAL_KICK_STREAM.md / kickwebview2webhook.md).
- Quirk: the setup wizard discovers ChannelId/ChatroomId by **navigating the embedded WebView2 to the unofficial `https://kick.com/api/v2/channels/{name}` endpoint and scraping the JSON from the rendered page** (KickWebSetupViewModel.FetchAndSaveChannelIdsAsync, lines 706–800) — a workaround for Cloudflare blocking plain HTTP clients.

---

## 7. YouTube integration

UI: YouTubeWebSetup wizard (Google Cloud console steps) + "YouTube Connection" card; YoutubeChatSettingsView.

### Auth (`src/VyralBot.UI/ViewModels/YouTubeWebSetupViewModel.cs`)
- Google OAuth 2.: `https://accounts.google.com/o/oauth2/v2/auth`, redirect `http://localhost:3000/oauth2callback`, scope **`https://www.googleapis.com/auth/youtube.force-ssl`** only, `access_type=offline`, `prompt=consent` (lines 505–511).
- WebView2 flow watches for `http://localhost:3000/oauth2callback` (line 566); code exchanged at `https://oauth2.googleapis.com/token` (line 625); channel title fetched from `youtube/v3/channels?mine=true` (line 660).
- Wizard walks the user through Google Cloud Console (project, API library, OAuth client, scope) with deep links per step (lines 113–429); ClientId/ClientSecret and a **Google Project ID** are collected and saved to the DB `Platform` row (ClientId/ClientSecret/UserId).
- Tokens stored in DB `Platform` row; unlike Twitch they are **not DPAPI-encrypted** (YouTubeChatService writes AccessToken/RefreshToken back directly, YouTubeChatService.cs:598–618).

### Chat/event ingestion (`src/VyralBot.Integrations/YouTube/YouTubeChatService.cs`)
- **Polling** model, not WebSocket/webhook: poll loop hits `GET youtube/v3/liveChat/messages?liveChatId=...&part=snippet,authorDetails` with pageToken, honoring the API-returned `pollingIntervalMillis` (default 5000ms) (lines 244–369).
- Live chat discovery: queries `liveBroadcasts?mine=true` for `active` then falls back to **all** broadcasts (upcoming/testing also carry liveChatId) (lines 447–537); if none found, service still reports connected and retries every 30s until the streamer goes live (lines 103–114, 249–268).
- 403/404 during polling clears the cached liveChatId (stream ended) and goes back to broadcast discovery (lines 281–289).
- Sending: `POST liveChat/messages` as `textMessageEvent`; on 401/403 refreshes the token and retries once (lines 173–240).
- Token lifecycle: auto-refresh when expiring within 5 minutes; refresh handled locally against `oauth2.googleapis.com/token` using ClientId/Secret from the Platform row; new tokens written back to the DB (lines 543–633).
- Events: only `YouTubeChatMessageEvent` is published (text messages only; super chats/membership events filtered out by `type == "textMessageEvent"`, line 382).

---

## 8. TikTok integration — UI shell only (stub)

- Files: `src/VyralBot.UI/ViewModels/TikTokWebSetupViewModel.cs`, `Views/TikTokWebSetupView.xaml(.cs)`, `TiktokChatSettingsViewModel/View`.
- There is **no TikTok service anywhere in Integrations/Core** — grep for TikTok across src finds only UI, Data entities, and converters.
- The setup wizard is a template copy with TODOs: OAuth service commented out (line 18), credential save/load are `Task.CompletedTask /* TODO */` (lines 292–314), OAuth URL is a hardcoded string `"https://www.tiktok.com/v2/auth/authorize/" /* TODO: Build TikTok OAuth URL */` (line 317), and `HandleOAuthCallbackAsync` always returns null (line 410). Steps navigate WebView2 to `https://www.tiktok.com/login` and `https://developers.tiktok.com/apps/` (lines 203, 232).
- Like Discord, finishing setup saves a `Platform { Name="TikTok", Status="Connected" }` row with empty tokens (lines ~455–475), so the Platforms card can show "connected" with nothing actually wired up.

## 9. Discord integration — UI shell only (stub)

- Files: `src/VyralBot.UI/ViewModels/DiscordWebSetupViewModel.cs`, `Views/DiscordWebSetupView.xaml(.cs)`, `DiscordChatSettingsViewModel/View`.
- No Discord gateway/REST service exists; `DiscordOAuthService` is a commented-out TODO (line 18).
- All OAuth steps are stubbed (`TODO: Discord credential save/load`, `TODO: Discord OAuth callback` returns null; OAuth URL is `"https://discord.com/api/oauth2/authorize"` string TODO, lines 288–410).
- `SendTestMessageAsync` (line ~510) attempts a raw **IRC connection to discord.com:6667 with `PASS oauth:{token}`** — Discord does not run an IRC server, so this test can never succeed; it's Twitch IRC code copied verbatim. Similarly `SaveCredentialsToEnvironment` writes `VYRALBOT_TWITCH_*` env vars (line ~726) and the save-error log says "Failed to save Twitch configuration" — copy/paste remnants.
- Finishing setup still writes a `Platform { Name="Discord", Status="Connected", TokenExpiresAt=Now+4h }` row with unencrypted empty tokens, which makes the app treat Discord as connected (no runtime consumer exists).

---

## 10. Setup wizard framework (WebView2-based)

Two base classes exist; per-platform wizards extend one or the other (or neither):

- `src/VyralBot.UI/ViewModels/BaseWebSetupViewModel.cs` — generic 4-step wizard shell with WebView2 lifecycle events (`WebView2InitializeRequested` / `WebView2DisposeRequested`), modal input helpers, `SetupCompleted` event (used by Ngrok wizard).
- `src/VyralBot.UI/ViewModels/BaseOAuthSetupViewModel.cs` — adds the OAuth credential pattern: abstract `GetStep1Url/GetStep2Url/SaveCredentialsAsync/LoadCredentialsAsync/BuildAuthorizationUrl/HandleOAuthCallbackAsync/SendTestMessageAsync` (lines 118–125). It standardizes the flow: Step 1 = platform login page, Step 2 = developer console (paste Client ID/Secret via modal), Step 3 = OAuth in embedded WebView2 (intercept `http://localhost:3000` navigation, cancel it, parse code — lines 371–391), Step 4 = test message + finish. Also supports a manual "paste callback URL" fallback (`SubmitCallbackUrlAsync`).
- The per-platform wizards (Twitch/Kick/YouTube/TikTok/Discord) each carry their own copy of this logic rather than all inheriting the base — TwitchWebSetupViewModel, KickWebSetupViewModel, YouTubeWebSetupViewModel, TikTokWebSetupViewModel, DiscordWebSetupViewModel are standalone `ObservableObject`s; only `NgrokSetupWizardViewModel` visibly overrides `BaseOAuthSetupViewModel`'s abstract strings.
- Redirect URI is fixed `http://localhost:3000` for every platform (Twitch exact, Kick `/callback`, YouTube `/oauth2callback`); no local listener is needed for the WebView2 flows because navigation to localhost is intercepted client-side. The only real `HttpListener` on port 3000 is TwitchOAuthService.AuthenticateAsync.

## 11. Ngrok setup UX and settings surfaces

- Ngrok wizard: `NgrokSetupWizardViewModel` — 4 steps: install ngrok (Microsoft Store link), sign in, open `https://dashboard.ngrok.com/get-started/your-authtoken` in WebView2, paste authtoken (saved via `NgrokService.SaveAuthTokenAsync` to `{profile}/credentials/ngrok_credentials.json`).
- `WebServerSettingsViewModel` + `WebServerSettingsView` ("Settings → API & Webhooks"): configure Port, NgrokRegion, NgrokAuthToken, AutoStartOnLaunch; Start/Stop of the whole Kestrel+ngrok stack; shows current tunnel URL; setup-complete = ngrok on PATH + auth token present.
- `KickSettingsViewModel` (`KickSettingsView`): "ToggleWebhooks" enable/disable for Kick webhooks — requires the coordinator running, platform AccessToken, and UserId (KickSettingsViewModel.cs:190–244); status reflects active subscription count from the DB; stops webhook toggle if the server stops (lines 246–255).
- `WebServerSettings` entity (`src/VyralBot.Data/Entities/WebServerEntities.cs`): single row — IsEnabled, **Port (default 5000)**, NgrokRegion (default "us"), CurrentTunnelUrl, AutoStartOnLaunch, LastStarted/LastStopped. `WebhookSubscription` entity: Id, Platform, EventType, EventVersion, BroadcasterUserId, WebhookUrl, IsActive, LastEventReceived.
- Per-profile credentials files (via `IProfileService.GetProfileCredentialsPath`): `twitch_credentials.json`, `kick_credentials.json`, `ngrok_credentials.json`.

## 12. Startup wiring and DI (`src/VyralBot.UI/App.xaml.cs`)

- Singletons: `ITwitchService→TwitchService`, `TwitchOAuthService` (transient), `AddTwitchLibEventSubWebsockets()` + `ITwitchEventSubService→TwitchEventSubService`, `AddKickLib()` + `IKickService→KickLibChatService` + `KickLibOAuthService`, `IYouTubeService→YouTubeChatService`, `WebServerDependencyTracker`, `WebServerService`, `NgrokService`, `KickWebhookSubscriptionService`, `WebhookCoordinatorService` (App.xaml.cs:146–171). Comment: "Old WebView2-based services removed - no longer needed with KickLib!".
- All setup VMs, dashboards, and PlatformsViewModel are singletons to prevent duplicate-instance leaks (App.xaml.cs:189+).
- Startup sequence in `MainViewModel.OnProfileLoadedAsync` (MainViewModel.cs:219–292): init WebhookCoordinator (auto-start if configured) → load settings + create ChatViewModel → **subscribe ChatViewModel to EventBus BEFORE auto-connecting platforms** (explicit ordering comment, lines 258–274) → `CheckPlatformConnectionsAsync` auto-connects Twitch/Kick/YouTube if DB Status=="Connected" (decrypting Twitch tokens, MainViewModel.cs:408–446) → `AutoEnableKickWebhooksAsync`.

## 13. Per-platform dashboards

- `TwitchDashboardViewModel` / `TwitchDashboardView`: reads Twitch Platform row; shows Connected-as status; on-demand WebView2 stream preview at `https://player.twitch.tv/?channel={name}&parent=localhost` (TwitchDashboardViewModel.cs:52–99).
- `KickDashboardViewModel` / `KickDashboardView`: same shape using `IKickService.IsConnected`; status string distinguishes "Disconnected (reconnect on app restart)"; preview at `https://player.kick.com/{name}?parent=localhost` (KickDashboardViewModel.cs:66–80). Note WEBVIEW2_REMOVAL_KICK_STREAM.md documents the preview WebView2 being removed at one point; the current VM still exposes `StreamPreviewWebView`.

## 14. What consumes platform events (cross-section note)

`ChatViewModel` subscribes to every platform event type on the EventBus (ChatViewModel.cs:207–269): Twitch/Kick/YouTube chat message events, `ChatMessageReceivedEvent` (generic), `PlatformChatRoutingChangedEvent`, `PlatformConnectionChangedEvent`, and all dedicated Kick/Twitch follow/sub/gift/host/raid/cheer/reward/stream-status events. Other consumers (other reviewers' scope): TriggerEngine (commands/auto-responses), variables, currency/loyalty, giveaways, polls, watchtime, alerts, statistics.

## 15. Docs corroboration (root *.md)

- `KICKLIB_MIGRATION_PLAN.md` — migration WebView2 → official KickLib with native Pusher.
- `kickwebview2webhook.md` — earlier plan: standalone WebView2 to bypass Cloudflare; contains ngrok requirement analysis.
- `KICK_WEBHOOK_PHASE1_COMPLETE.md` — built the embedded webhook receiver (WebServerService etc.); at the time claimed webhooks were the only official delivery mechanism.
- `WEBHOOK_DUPLICATE_AUDIT.md` + `WEBHOOK_FIX_IMPLEMENTATION.md` — duplicate follow events found; **priority reversed: Pusher WebSocket is PRIMARY, webhooks only for events Pusher lacks** (matches WebhookCoordinatorService.cs:249–264 and WebhooksController ignoring chat.message.sent).
- `KICK_INTEGRATION_AUDIT_AND_PLAN.md` — audit with an example real tunnel URL `https://fructuous-precious-plunderable.ngrok-free.dev/kick/webhook` (note: that plan's `/kick/webhook` path differs from the implemented `/webhooks/kick`).

## 16. Notable quirks / findings (Platforms section)

1. **Twitch EventSub likely never auto-connects via UI flows**: `TwitchService.ConnectAsync` gates EventSub on `Platform.UserId` (TwitchService.cs:114–124), but neither `TwitchWebSetupViewModel.FinishSetupAutomaticallyAsync` nor `PlatformsViewModel.ConnectTwitchAsync` ever sets `Platform.UserId` (no assignment exists in src). Follow/sub/raid/cheer events therefore depend on stale/externally-populated data.
2. **Inconsistent token encryption**: Twitch user tokens DPAPI-encrypted; Kick and YouTube tokens stored plaintext in the DB (Kick deliberately, per comments); Discord/TikTok stubs save empty tokens unencrypted. `Platform.ClientSecret` doc comment says "Encrypted client secret" but Twitch/Kick/YouTube store it plaintext.
3. **Kick webhook URL change is manual-heal**: ngrok URL rotation (free tier) invalidates Kick subscriptions; coordinator only warns (WebhookCoordinatorService.cs:369–377) since it lacks the OAuth token in that context.
4. **Webhook signature bypass for "test" payloads** in KickWebhookValidationMiddleware (messageId `test-` prefix or `"test":true` in body).
5. **Discord test-message code connects via IRC to discord.com:6667** — Discord has no IRC; copied Twitch logic. TikTok/Discord wizards can mark the platform "Connected" in the DB with no functional backend.
6. **Kick channel-ID discovery scrapes kick.com/api/v2 via the embedded WebView2** (Cloudflare workaround) rather than an authenticated official API call.
7. **Duplicate Twitch scope lists** (TwitchOAuthService.cs:58–134 vs 422–501) risk drift; Platforms page "OAuth Permissions" checklist is hardcoded XAML.
8. **EventBus**: exact-type-only matching; bounded 500 with DropOldest means event loss is possible under burst (by design); handlers throttled to 4 concurrent.
9. **WebhookController double-reads the body** (middleware enables buffering, controller re-reads) — fine, but both log full payload previews at Information level, including usernames.
10. `PlatformsViewModel.DisconnectTwitch` deletes the Platform row, while Kick/YouTube/TikTok/Discord disconnects keep the row and null fields — inconsistent lifecycle semantics.
11. YouTube chat requires an active/upcoming broadcast (liveChatId); with none found the service sits "connected" polling every 30s and sends fail with "You must be streaming live".
12. `KickChatEnabled`/`TwitchChatEnabled` etc. on the `Platform` entity gate chat display/storage and are force-enabled to true on any successful connect (PlatformsViewModel.cs:605–611, 1045–1051; MainViewModel auto-connect does the same).

