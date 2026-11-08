# Embedded Web Server (VyralBot.Api)

The embedded Kestrel host (`WebServerService`) runs **in-process** with the WPF
app and binds **loopback only** (`ListenLocalhost`, `WebServerService.cs`). It is
started/stopped by `WebhookCoordinatorService` (optionally with an ngrok tunnel
so Kick can reach it from the internet). It is a separate host from the LAN
VyralDeck `DeckServer` (`UI/Services/VyralDeck/DeckServer.cs`, `ListenAnyIP`,
off by default, PIN-paired device tokens).

## Endpoints

| Route | Method | Auth | Notes |
|---|---|---|---|
| `/webhooks/kick` | POST | RSA-SHA256 signature (`KickWebhookValidationMiddleware`) | Publishes Kick platform events to `IEventBus` (bot actions, currency). |
| `/webhooks/health` | GET | none | Static health JSON. |
| `/api/status` | GET | Bearer token (`ControlApiAuthMiddleware`) | Uptime, platform connections/live flags, OBS state, restream state+stats, viewer counts. |
| `/api/stats/today` | GET | Bearer token | Event counts by type+platform since local midnight (from `IStatisticsService`). |
| `/api/events/test` | POST | Bearer token | Fires a canned test event via `TestEventPublisher` — the exact payloads of the Event Testing page. Body: `{"type":"twitch.follow","username":"...","message":"..."}` (≤16 KB). |
| `/api/version` | GET | Bearer token | Entry-assembly version + .NET runtime version. |
| `/audio-ws` | WS | loopback Origin check + 16-connection cap | One-way push of sound-play messages for OBS browser sources. |
| `/songs/queue` | GET | loopback bind + same-origin (no CORS) | `{ current, upcoming }` song-request snapshot for the player page; lazily promotes the queue head to Playing. |
| `/songs/played` | POST | loopback Origin check + 4 KB body cap | Player reports the current video finished/errored — marks Played, promotes next. Body `{"videoId":"..."}` (stale ids are no-ops). |
| `/songs/title` | POST | loopback Origin check + 4 KB body cap | Player reports a resolved title/duration. Body `{"id":"...","title":"...","duration":123}`. |
| `/*` (static) | GET | none | `/sounds/*` is served first from the active profile's sounds folder (`ISoundService.GetSoundsDirectory()` — `appdata/profiles/<name>/data/sounds`), then falls back to `wwwroot` (`audio.html`, `songplayer.html`, legacy `/sounds/*`) from `AppContext.BaseDirectory`. |

## Control API (`/api`) — community-tool surface

A minimal, read-mostly API for community tools (the streamer.bot-style
ecosystem). **Auth is absolute, not per-route**: `ControlApiAuthMiddleware`
gates the entire `/api` path prefix in the pipeline *before* `MapControllers`,
so every current and future `/api` route — including unknown paths that would
404 — requires a valid token first. There is no unauthenticated `/api` route.

- **Token**: the existing webhook auth token (the ngrok auth token stored
  per-profile in `ngrok_credentials.json`, loaded by `NgrokService`). No new
  secret to manage; it is never logged.
- **Transport**: `Authorization: Bearer <token>`. Both provided and configured
  tokens are SHA-256 hashed and compared with
  `CryptographicOperations.FixedTimeEquals` (same pattern as VyralDeck device
  tokens — digest comparison also removes length as an oracle).
- **Responses**: missing/bad token → `401 {"error":"unauthorized"}` with
  `WWW-Authenticate: Bearer`; no token configured → `503 {"error":"api_disabled"}`;
  body over 16 KB → `413` (both the `Content-Length` check and a lowered
  `IHttpMaxRequestBodySizeFeature` limit for chunked bodies).
- **No secrets in payloads**: status exposes booleans, enum strings, counters
  and timestamps only — no paths, tokens, tunnel URLs or restream destination
  details (destination URLs embed stream keys).
- **Services**: `ControlApiStatusService` (Api project, singleton in the WPF
  host DI, forwarded into the Kestrel child container by `WebServerService`)
  aggregates platforms/OBS/restream lazily via `IServiceProvider` so it never
  forces engine startup; it caches the latest Kick viewer count from
  `KickStreamStatusEvent` (Twitch/YouTube do not publish viewer counts on the
  bus yet — they report `null`). `TestEventPublisher` (Integrations) is the
  single source of test-event payloads, shared by the Event Testing page and
  `POST /api/events/test`.

**ngrok exposure**: when the tunnel runs, `/api` is public — but the Bearer
gate applies identically (the middleware is host-agnostic), and CORS remains
**default-deny** (no `UseCors`/`AddCors` anywhere in the app, none added): no
`Access-Control-Allow-Origin` is ever emitted, so browsers cannot read
responses cross-origin even with a valid token.

## Webhook pipeline (order matters)

For `POST /webhooks/kick`, `KickWebhookValidationMiddleware` enforces, in order:

1. Required headers present (`Kick-Event-Signature/Message-Id/Message-Timestamp`) → 401.
2. **Timestamp freshness** — reject if older than 10 min or more than 2 min in the future → 401 (the timestamp is signed, so this bounds replay).
3. **Body size cap** — 256 KB via `Content-Length` check (413) and `IHttpMaxRequestBodySizeFeature` (covers chunked bodies). JSON depth is bounded by `JsonDocument`'s default max depth (64).
4. RSA-SHA256 signature over `{messageId}.{timestamp}.{body}` using the Kick public key (cached 1 h from `api.kick.com/public/v1/public-key`, 10 s HTTP timeout). Failure → 401.
5. **Replay protection** — seen message IDs are deduped in a bounded in-memory cache (5000 entries, 10-min window); duplicates get 200 without reprocessing.
6. **Cross-transport idempotency** (`WebhooksController` + `KickLibChatService`, via `KickEventRouting.TryBeginEvent`) — the same follow/sub/gift can arrive over BOTH a webhook and the Pusher websocket when a matching subscription exists in the Kick dev portal (leftover/manual `channel.subscription.*` subs). Both transports claim the event under a normalized key (`follow:`/`sub:`/`giftsub:` + lowercase username, 60 s window, bounded 2000-entry cache); the loser skips publishing so alerts and currency awards never double-fire. Follows are additionally routed transport-exclusive via `KickEventRouting.WebhookFollowsActive`.

Unknown-but-well-formed event types are acknowledged **200** with an info log (never a warning/500) so Kick does not retry-storm.

The `Kick.AllowTestWebhooks` config flag (default **off**) may waive the
signature check for test-shaped payloads only; every waiver is logged as a
warning (KI-19).

The controller maps malformed JSON to **400** (not 500) and all other failures
to a generic 500 string — no stack traces or paths in responses.

Middleware order in the embedded host: metrics → websockets → static files →
routing → **ControlApiAuthMiddleware** → KickWebhookValidationMiddleware →
controllers → `/audio-ws` map.

## audio-ws

- Any local process may connect (loopback bind), but a browser page on a
  non-loopback Origin is refused (403) — browsers do not apply CORS to
  WebSockets, so the Origin check is what stops random websites from
  connecting to `ws://localhost:<port>/audio-ws`.
- Max 16 concurrent clients (503 beyond) — one-way broadcast only
  (`AudioWebSocketBroadcaster`); incoming frames are read and discarded.
- No per-profile scoping: the host serves the active profile's sounds; profiles
  are isolated at process level (one profile per running app instance).

## ngrok lifecycle (`NgrokService`, owned by `WebhookCoordinatorService`)

- The ngrok agent is started through the shared **`IProcessManager`** (tracked,
  entire-tree kill, covered by the shutdown kill-all backstop). No direct
  `Process.Start` for the tunnel itself.
- URL change detection: the local 4040 API is polled every 30 s. A disconnect is
  declared only after **3 consecutive failed checks** (a transient 4040 hiccup
  must not cascade into a Kick unsubscribe/resubscribe cycle), the poll is
  reentrancy-guarded, and a dead ngrok process stops the monitor.
- On a real URL rotation the coordinator persists the new URL and **automatically
  resubscribes** the active Kick webhooks (refreshing an expired Kick OAuth token
  first); when the URL bounces back unchanged, resubscription is skipped. If the
  stored token is missing/refresh fails, the UI is told to re-toggle Kick
  webhooks manually.

## CORS

No `UseCors` anywhere — no CORS headers are emitted, so browsers cannot read
responses cross-origin. Browser sources are served same-origin from this host.

## Secrets

- ngrok auth token: per-profile credentials JSON, never logged (not even its
  path — logs say only "saved"/"not found"). Doubles as the control API Bearer
  token (fixed-time digest compare). The ngrok command line carries it, so
  process-start logs deliberately omit arguments (ProcessManager rule).
- Deck tokens: 128-bit hex from `RandomNumberGenerator`, compared with
  `CryptographicOperations.FixedTimeEquals`, never logged.
- Webhook logs contain message IDs/headers/lengths only — never full payloads.
