# Web Server (Kestrel + ngrok)

> A developer deep-dive into the embedded ASP.NET Core server: how it is built, its middleware order, the `/audio-ws` bridge, Kick webhook validation and subscription management, the coordinator that orchestrates everything, and the ngrok process wrapper.

## Overview

Everything internet-facing in Vyral is one pipeline inside the desktop process:

```
Kick's servers ──► ngrok tunnel ──► Kestrel (localhost:{Port})
                                        ├─ POST /webhooks/kick  → KickWebhookValidationMiddleware → WebhooksController → EventBus
                                        ├─ GET  /webhooks/health
                                        ├─ /audio-ws            → WebSocket for OBS browser-source sound pushes
                                        └─ wwwroot              → audio.html + /sounds/* static files
```

Five services in `src/VyralBot.Api` cooperate:

| Service | Responsibility |
|---|---|
| `WebhookCoordinatorService` | The orchestrator: start/stop ordering, Kick subscribe/unsubscribe, dependency tracking, ngrok URL-change handling |
| `WebServerService` | Builds and runs the embedded `WebApplication` (Kestrel), pulls shared services from the WPF container |
| `NgrokService` | Spawns the ngrok process, discovers the public URL, monitors for changes, persists the per-profile token |
| `KickWebhookSubscriptionService` | Thin KickLib wrapper that (un)subscribes event types with rate-limit friendly delays |
| `WebServerDependencyTracker` | Tracks which features need the server; powers the "stop is blocked" gate |

Twitch and YouTube do **not** use this stack — Twitch receives events over an outbound EventSub WebSocket, YouTube polls. Only Kick needs inbound webhooks. The user-facing configuration page is [API, Web Server & ngrok](../settings/web-server-ngrok.md); the event fan-out beyond the controller is covered in [Event Bus](event-bus.md).

## Flow

### `WebServerService` — building the server

`StartAsync(port)` (default 5000, actual value from the `WebServerSettings` DB row):

1. **Port pre-check** — binds a loopback socket to the port and releases it (`IsPortAvailable`, `WebServerService.cs:227-239`); refuses to start if taken.
2. `WebApplication.CreateBuilder()` with `ConfigureKestrel(options => options.ListenLocalhost(port, Http1AndHttp2))` — the server is reachable only from localhost (plus whatever ngrok exposes).
3. **Shared DI pull-through**: the parent WPF container's `IEventBus` and `ILoggerFactory` are registered into the child container (`WebServerService.cs:62-63`). This is the one line that makes webhook events and WebSocket chat events converge on the same handlers.
4. **Middleware order** (order matters):

```csharp
_app.UseWebSockets();                                        // /audio-ws upgrade
_app.UseStaticFiles();                                       // wwwroot: audio.html, /sounds/*
_app.UseRouting();
_app.UseMiddleware<KickWebhookValidationMiddleware>();       // signature gate for /webhooks/kick
_app.MapControllers();                                       // WebhooksController
```

5. The server runs via `Task.Run(() => _app.RunAsync(cts.Token))`; `StopAsync` cancels with a timeout.

### `/audio-ws` and the static broadcaster bridge

A minimal-API endpoint at `/audio-ws` keeps a `ConcurrentDictionary<WebSocket, bool>` of connected browser-source pages (connect, idle-receive loop, cleanup on close). To let Core-layer services push to those sockets without knowing about Kestrel, the server assigns a static delegate:

```csharp
// WebServerService.cs — bridging Core's SoundService to the Kestrel client set
VyralBot.Core.Services.Audio.AudioWebSocketBroadcaster.BroadcastAsync = async (string message) =>
{
    var tasks = audioClients.Keys
        .Where(ws => ws.State == WebSocketState.Open)
        .Select(ws => ws.SendAsync(/* ... message as text frame ... */));
    await Task.WhenAll(tasks);
};
```

`AudioWebSocketBroadcaster` itself is a static class in Core (`Func<string, Task>? BroadcastAsync`, plus a `ConnectedClientCount` the Audio settings page polls for its live client counter). It is a deliberate compile-time seam. Since the 2026-08-15 fix, `SoundService` broadcasts camelCase `{"type":"play","sound":"...","volume":0..1}` — exactly the shape `audio.html` understands — and the page honors `volume` and derives its WebSocket/sounds URLs from `window.location` (port-agnostic), so browser-source sounds work ([Browser Sources](../stream-tools/browser-sources.md)).

### `WebhooksController` — event mapping

`POST /webhooks/kick` dispatches on the `Kick-Event-Type` header:

| Kick event type | Result on the EventBus |
|---|---|
| `chat.message.sent` | Accepted but **ignored** — chat arrives via the Pusher WebSocket; this is deliberate duplicate prevention |
| `channel.followed` | `KickFollowEvent` (the webhook path is the one that knows the follower's username — the Pusher path does not) |
| `channel.unfollowed` / `channel.unfollow` | Synthesized bot-authored `KickChatMessageEvent` ("X unfollowed the channel") |
| `channel.subscription.new` / `.renewal` | `KickSubscribeEvent` |
| `channel.subscription.gifts` | `KickGiftSubEvent` |
| `livestream.status.updated`, `moderation.banned`, `kicks.gifted` | Synthesized `KickChatMessageEvent`s authored by "Vyral.Bot" with badge `vyral=1` |

`GET /webhooks/health` returns `{ status: "healthy", timestamp, service: "webhooks" }` — used by the settings page's TEST HEALTH button and the ngrok wizard's end-to-end test.

Payload parsing is defensive: handlers try several JSON shapes (`follower` / `user` / `sender` / `actor`) because Kick's payloads vary; known gaps (no follower count, months, gifter id, or recipients in some payloads) are logged at the parse sites.

### `KickWebhookValidationMiddleware` — RSA-SHA256 verification

Applies only to POSTs under `/webhooks/kick` (GETs pass through). Requires `Kick-Event-Signature`, `Kick-Event-Message-Id`, and `Kick-Event-Timestamp` headers, returning 401 when missing. Verification:

1. Signed message = `{messageId}.{timestamp}.{body}` (UTF-8 bytes); signature is base64.
2. Fetch Kick's PEM public key from `https://api.kick.com/public/v1/public-key`, cached **statically for 1 hour**.
3. `RSA.VerifyData(..., HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)` — invalid signature → 401.

The middleware enables request buffering and resets the body position so the controller can re-read it.

**Test bypass (opt-in since 2026-08-15):** a message id starting with `test-` or a body containing `"test":true` skips signature validation **only** when the `Configs` key `Kick.AllowTestWebhooks` is set — the default (absent/false) is fail-closed. The flag is cached for 60 seconds and resolved through the WPF host's `App.ServiceProvider` via reflection. Operators who enable it should treat the public tunnel URL as sensitive.

### `KickWebhookSubscriptionService` — rate-limited subscription calls

A thin wrapper over KickLib's `IKickApi.EventSubscriptions` (resolved from a scope because KickLib's API client is scoped). Subscriptions are made **one event type at a time with a 1-second `Task.Delay` between calls** to avoid Kick's rate limits; partial failure continues with the remaining types and reports what succeeded. Also provides single/batch unsubscribe, `GetActiveSubscriptionsAsync`, and `ResubscribeToNewUrlAsync` (unsubscribe-all then subscribe-all — the callback URL itself is configured in the Kick developer portal, pointing at the tunnel).

### `WebhookCoordinatorService` — orchestration

**Start order** (`StartAsync`): Kestrel on `settings.Port` → load the ngrok token from the per-profile JSON → `NgrokService.StartTunnelAsync(port, token, region)` → persist `CurrentTunnelUrl` + `LastStarted` to the DB (reloading settings first so concurrent edits are not clobbered). If either the server or the tunnel fails to start, the other is stopped and the start fails. `StopAsync` mirrors it with a 5-second overall timeout.

**`EnableKickWebhooksAsync(accessToken, broadcasterUserId)`** implements the duplicate-prevention policy from the webhook audit work:

1. Remove leftover `chat.message.sent` subscriptions from older versions of the app.
2. **Unsubscribe ALL existing Kick subscriptions** on Kick's servers (and mark DB rows inactive) — a clean slate prevents accumulation.
3. Subscribe **only to webhook-only event types**: `LivestreamMetadataUpdated` and `ModerationUserBanned`. Follows, subscriptions (new/renewal/gifts), stream status, and kicks-gifted are deliberately *not* subscribed — the KickLib Pusher WebSocket is the primary source for those, and dual delivery caused duplicate events.
4. Save each result as a `WebhookSubscription` row (Platform="Kick", EventType, BroadcasterUserId, WebhookUrl `{PublicUrl}/webhooks/kick`, IsActive).
5. Register the "Kick" dependency in `WebServerDependencyTracker` — from this point `CanDisableWebServer()` returns false and the settings page refuses to stop the server.

`DisableKickWebhooksAsync` unsubscribes server-side, deactivates the rows, and unregisters the dependency.

**URL-change handling** (`OnNgrokUrlChanged`, fixed 2026-08-15): updates `CurrentTunnelUrl` in the DB; if Kick subscriptions were active on the old URL it now heals them automatically — it loads the Kick `Platform` row, refreshes the OAuth token if expired (through an injected `KickLibOAuthService`; DI auto-resolves it), then unsubscribes all server-side and resubscribes to the new URL. Only if that recovery fails does it fall back to the old warning telling the user to disable and re-enable Kick webhooks.

### `NgrokService` — process management

- `StartTunnelAsync(port, authToken, region)` spawns `ngrok http {port} --region {region} --authtoken {token}` as a hidden process (`NgrokService.cs:60-73`); requires a non-empty token and ngrok on PATH (checked by running `ngrok version`).
- The public URL is discovered by polling ngrok's local API `http://127.0.0.1:4040/api/tunnels` up to 5 times, 2 seconds apart, taking the first `https` tunnel's `public_url`.
- A **30-second `Timer`** then polls the same API; on change or disconnect it raises `PublicUrlChanged` (empty string = disconnected).
- `StopTunnelAsync` kills the process tree with a 3-second wait.
- Token persistence: `SaveAuthTokenAsync`/`LoadAuthTokenAsync` read/write `{"AuthToken": ...}` to `ngrok_credentials.json` in the **current profile's** credentials folder — plaintext, per-profile, not in the database.

## Files & Code

| File | Role |
|---|---|
| [WebServerService.cs](../../src/VyralBot.Api/Services/WebServerService.cs) | Kestrel host, shared-DI pull-through, middleware order, `/audio-ws`, port check |
| [WebhookCoordinatorService.cs](../../src/VyralBot.Api/Services/WebhookCoordinatorService.cs) | Start/stop orchestration, Kick enable/disable with unsubscribe-first policy, dependency tracking, URL-change warning |
| [NgrokService.cs](../../src/VyralBot.Api/Services/NgrokService.cs) | ngrok process lifecycle, 4040 API polling, 30s monitor, per-profile token file |
| [KickWebhookSubscriptionService.cs](../../src/VyralBot.Api/Services/KickWebhookSubscriptionService.cs) | Rate-limited KickLib subscribe/unsubscribe wrapper |
| [WebServerDependencyTracker.cs](../../src/VyralBot.Core/Services/WebServer/WebServerDependencyTracker.cs) | Dependency registry behind `CanDisableWebServer()` |
| [WebhooksController.cs](../../src/VyralBot.Api/Controllers/WebhooksController.cs) | Header dispatch, event mapping, health endpoint |
| [KickWebhookValidationMiddleware.cs](../../src/VyralBot.Api/Middleware/KickWebhookValidationMiddleware.cs) | RSA-SHA256 signature validation with 1h key cache and test bypass |
| [AudioWebSocketBroadcaster.cs](../../src/VyralBot.Core/Services/Audio/AudioWebSocketBroadcaster.cs) | One-field static bridge (`Func<string, Task>?`) from Core to the Kestrel client set |
| [WebServerEntities.cs](../../src/VyralBot.Data/Entities/WebServerEntities.cs) | `WebServerSettings` (Id=1 single row) and `WebhookSubscription` entities |

The subscribe-only-webhook-only-events policy, verbatim from the coordinator:

```csharp
// WebhookCoordinatorService.cs — EnableKickWebhooksAsync
// Events handled by Pusher (REMOVED to prevent duplicates):
// - ChannelFollowed, ChannelSubscriptionNew/Renewal/Gifts,
//   LivestreamStatusUpdated, KicksGifted
var eventTypes = new List<KickLib.Models.v1.EventSubscriptions.EventType>
{
    // Events NOT handled by Pusher (webhook-only):
    KickLib.Models.v1.EventSubscriptions.EventType.LivestreamMetadataUpdated,
    KickLib.Models.v1.EventSubscriptions.EventType.ModerationUserBanned
};
```

The signature scheme the middleware enforces:

```csharp
// KickWebhookValidationMiddleware.cs — ValidateSignature (condensed)
var signedMessage = $"{messageId}.{timestamp}.{body}";        // exact concatenation
using var rsa = RSA.Create();
rsa.ImportFromPem(publicKey);                                  // from api.kick.com, cached 1h
return rsa.VerifyData(
    Encoding.UTF8.GetBytes(signedMessage),
    Convert.FromBase64String(signature),
    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
```

## Gotchas

- **Test-payload signature bypass is opt-in** — `test-` message ids or `"test":true` bodies skip validation only with `Kick.AllowTestWebhooks` set; the default is fail-closed (fixed 2026-08-15) ([known issues ledger](../reference/known-issues.md)).
- **ngrok URL rotation heals automatically** — the coordinator refreshes the Kick token if needed and resubscribes; only a failed recovery leaves the manual off/on toggle as the fallback (fixed 2026-08-15).
- **localhost-only bind** — the Kestrel server is unreachable from the LAN by design; only ngrok exposes it. The audio page derives its URLs from `window.location`, so OBS must load it from the same machine (and the server port must stay 5000 for the Audio page's pinned URL).
- **The controller's `TODO: Validate signature` comment is stale** — validation really happens in the middleware; both layers log full payload previews (including usernames) at Information level.
- **`WebServerSettings.IsEnabled` is a legacy column** — the settings ViewModel no longer reads or writes it (removed 2026-08-15); running state is `webServer.IsRunning && ngrok.IsConnected`.
- **The middleware's public-key cache is `static`** — shared across instances and never proactively refreshed; a key rotation inside the 1-hour window relies on signature failures to surface.
- **Duplicate delivery is prevented by policy, not infrastructure** — if KickLib's Pusher handlers and the webhook set ever drift out of sync (someone re-adds a Pusher-covered event type to `eventTypes`), duplicates return silently.
- **`NgrokSetupView`/`NgrokSetupViewModel` are dead code** — the active UI is the wizard (`NgrokSetupWizardView`/`NgrokSetupWizardViewModel`); see [API, Web Server & ngrok](../settings/web-server-ngrok.md).
