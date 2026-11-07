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
| `/audio-ws` | WS | loopback Origin check + 16-connection cap | One-way push of sound-play messages for OBS browser sources. |
| `/*` (static) | GET | none | `/sounds/*` is served first from the active profile's sounds folder (`ISoundService.GetSoundsDirectory()` — `appdata/profiles/<name>/data/sounds`), then falls back to `wwwroot` (`audio.html`, legacy `/sounds/*`) from `AppContext.BaseDirectory`. |

## Webhook pipeline (order matters)

For `POST /webhooks/kick`, `KickWebhookValidationMiddleware` enforces, in order:

1. Required headers present (`Kick-Event-Signature/Message-Id/Message-Timestamp`) → 401.
2. **Timestamp freshness** — reject if older than 10 min or more than 2 min in the future → 401 (the timestamp is signed, so this bounds replay).
3. **Body size cap** — 256 KB via `Content-Length` check (413) and `IHttpMaxRequestBodySizeFeature` (covers chunked bodies). JSON depth is bounded by `JsonDocument`'s default max depth (64).
4. RSA-SHA256 signature over `{messageId}.{timestamp}.{body}` using the Kick public key (cached 1 h from `api.kick.com/public/v1/public-key`, 10 s HTTP timeout). Failure → 401.
5. **Replay protection** — seen message IDs are deduped in a bounded in-memory cache (5000 entries, 10-min window); duplicates get 200 without reprocessing.

The `Kick.AllowTestWebhooks` config flag (default **off**) may waive the
signature check for test-shaped payloads only; every waiver is logged as a
warning (KI-19).

The controller maps malformed JSON to **400** (not 500) and all other failures
to a generic 500 string — no stack traces or paths in responses.

## audio-ws

- Any local process may connect (loopback bind), but a browser page on a
  non-loopback Origin is refused (403) — browsers do not apply CORS to
  WebSockets, so the Origin check is what stops random websites from
  connecting to `ws://localhost:<port>/audio-ws`.
- Max 16 concurrent clients (503 beyond) — one-way broadcast only
  (`AudioWebSocketBroadcaster`); incoming frames are read and discarded.
- No per-profile scoping: the host serves the active profile's sounds; profiles
  are isolated at process level (one profile per running app instance).

## CORS

No `UseCors` anywhere — no CORS headers are emitted, so browsers cannot read
responses cross-origin. Browser sources are served same-origin from this host.

## Secrets

- ngrok auth token: per-profile credentials JSON, never logged (only its path).
- Deck tokens: 128-bit hex from `RandomNumberGenerator`, compared with
  `CryptographicOperations.FixedTimeEquals`, never logged.
- Webhook logs contain message IDs/headers/lengths only — never full payloads.
