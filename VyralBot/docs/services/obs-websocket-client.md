# obs-websocket client (remote control)

## Overview

`ObsWebSocketClient` (in `src/VyralBot.Integrations/Obs/WebSocket/ObsWebSocketClient.cs`)
is the obs-websocket 5 client used to drive a running OBS instance: it connects
to `ws://localhost:{port}`, answers the Hello challenge via `ObsWebSocketAuth`
(base64(SHA256(base64(SHA256(password+salt)) + challenge))), multiplexes
opcode-6 requests against opcode-7 responses, and mirrors opcode-5 events into
`ObsRemoteStatus` snapshots raised through `StatusChanged` (threadpool thread —
UI consumers must marshal).

## Consumers

| Consumer | Mode | Reconnect strategy |
|---|---|---|
| `ObsHostViewModel` (Full OBS tab) | `autoReconnect: true`, password provider = re-read of OBS's `config.json` | Built-in backoff loop (below) |
| `ObsRemoteBridge` (VyralDeck actions) | `autoReconnect: false` (default), fixed password per client | Lazy: drops the client on failure, next button press reconnects with freshly read config |

## Auto-reconnect state machine (v2)

- Any **non-user-initiated** loss — socket drop, OBS quit, 4009 auth rejection,
  failed handshake, or a first connect that never reached a socket (OBS still
  starting) — schedules a single-flight retry loop.
- Backoff: 1 s → 2 s → 4 s → … capped at 30 s, **unlimited attempts** while the
  owning VM keeps the client alive (it only lives while the OBS page is
  initialized and the remote is configured).
- Each attempt **re-reads the password through the `Func<string>` provider**, so
  a password edited in OBS → Tools → WebSocket Server Settings is picked up by
  the next retry (no page re-entry, no app restart).
- On success the Identify handshake is re-sent (omitting `eventSubscriptions`
  subscribes to ALL events — that is the re-subscription) and scene/stream
  state is refreshed via `GetCurrentProgramScene` + `GetStreamStatus` because
  events missed while offline are gone.
- **User-initiated** `DisconnectAsync()` / `Dispose()` cancels the loop — a
  pending retry never fires afterwards.
- State changes surface through the same `StatusChanged` /
  `ObsRemoteStatus(Connected, CurrentScene, StreamActive, Error)` events the UI
  already binds ("reconnecting in N s…" lines included).

## Hardening

- **Send timeout:** every send runs under a 10 s CTS while holding the send
  lock — a stalled TCP peer aborts the socket and throws `TimeoutException`
  instead of wedging the lock forever.
- **Inbound frame cap:** messages over 4 MiB stop being buffered, are drained
  to the frame boundary, dropped, and logged as errors — a hostile/garbled
  remote cannot OOM the client or desync the stream.
- **Stale-generation guard:** a superseded socket generation's receive loop
  cannot fail a newer handshake or spawn a duplicate reconnect round.
- **Secrets:** the password feeds the auth hash only — never sent in clear,
  never present in exceptions, status text, or log messages.

## Configuration sources

- `ObsWebSocketConfigReader` — read-only view of `%APPDATA%\obs-studio\plugin_config\obs-websocket\config.json`.
- `ObsWebSocketConfigEditor` (Hosting) — read + minimal `server_enabled` edit used by the OBS page.

## Tests

`tests/VyralBot.UI.Tests/ObsWebSocketClientTests.cs` — auth hash vectors,
config parsing, loopback connect/request/event tests, fresh-password reconnect,
user-disconnect cancels retry loop, oversized-frame drop.
