# VyralDeck (StreamDeck / mobile companion)

## Overview
VyralDeck turns a phone (or any LAN client) into a Stream Deck for VyralBot.
The desktop app runs a **LAN-only companion server** — its own Kestrel host,
separate from the loopback webhook server — that paired phones connect to over
WebSocket to press buttons, edit the deck layout and run ad-hoc tools. The
server is **off by default** and started from the StreamDeck page (Restream
group in the sidebar); every session must carry a per-device token issued
through PIN pairing.

- Desktop source: `src/VyralBot.UI/Services/VyralDeck/` (all files below)
- Desktop UI: `StreamDeckViewModel` + `StreamDeckRowModels` (`ViewModels/`)
- Mobile app: `mobile/vyral_deck/` (Flutter, iOS + Android, LAN-only)
- **Wire protocol (authoritative): `mobile/vyral_deck/PROTOCOL.md` (v1)**

## Desktop server (`DeckServer.cs`)
| Endpoint | Auth | Purpose |
|---|---|---|
| `GET /vyraldeck/info` | none | Identity JSON (`app`, `serverName`, `protocol`, `tcpPort`, `discoveryPort`, `pairedDevices`) — lets a phone verify the host before pairing |
| `POST /vyraldeck/pair` | PIN | `{pin, deviceName}` → `{ok, token, serverName, protocol}` or 403 + error |
| `GET /vyraldeck/ws?token=…` | token | The session WebSocket (401 on unknown token); echoes the `vyraldeck` sub-protocol when offered |
| UDP :5198 | none | Discovery: client broadcasts `VYRALDECK_DISCOVER`, server replies `{app, serverName, tcpPort}` |

- TCP port **5199** by default (configurable; port is probed for availability
  before start), bound on all interfaces — LAN only by design.
- On connect the server sends a `welcome` message: current `layout`, live
  `state` (`Twitch`/`Kick`/`YouTube` keys stay PascalCase — the phone looks
  them up by .NET spelling) and the full action `catalog` (so the mobile
  editor never hardcodes a copy).
- Push channels: `config` (layout changed on either side), `state` (a platform
  went live/offline via `IStreamLiveRegistry`), `press_result`, `pong`, `error`.
- Client messages: `press` (saved button by id), `action` (ad-hoc tool, no
  saved button), `config_set` (persist an edited layout), `ping`.
- `LanAddress` picks the primary private IPv4 (gateway + 10.x/172.16-31/192.168)
  so the UI can show the address phones should connect to.

## Pairing & security (`DeckPairingStore.cs`)
- **PIN lifecycle:** 6-digit PIN generated with `RandomNumberGenerator`,
  valid **10 minutes**, shown in the StreamDeck page. A PIN is **consumed on
  success — it pairs exactly one device**; a new PIN must be generated for the
  next phone. PINs live only in memory.
- **Rate limiting:** max **5 failed pairing attempts per rolling minute**;
  further attempts get "Too many failed pairing attempts — wait a minute".
- **Per-device tokens:** 256-bit hex (`RandomNumberGenerator.GetHexString(32)`),
  persisted in the profile config KV (`VyralDeck.Devices`) so phones stay
  paired across restarts. Tokens are never logged.
- **Fixed-time compare:** token validation uses
  `CryptographicOperations.FixedTimeEquals` (`ValidateTokenAsync`), which also
  bumps the device's `LastSeenUtc`.
- **Unpair:** `RemoveDeviceAsync` raises `DeviceRemoved` → `DeckServer`
  immediately closes that device's live WebSocket session.
- All pairing state mutations are serialized through a `SemaphoreSlim` gate.

## Executor (`DeckActionExecutor.cs`) — 22 actions
`DeckActionExecutor` dispatches to existing Vyral services (OBS bridge, chat
senders, `IStreamActionsService` Helix/Kick mod APIs, sounds, alerts,
`WorkflowRuntime`, `ILightsClient`). It never throws — every failure returns a
message the desktop status line / mobile app can show.

| Category | Actions |
|---|---|
| OBS (3) | `ObsScene` (p1=scene), `ObsStreamStart`, `ObsStreamStop` |
| Chat (1) | `ChatSend` (p1=message, p2=twitch/kick/youtube, default twitch) |
| Twitch (8) | `TwitchClip`, `TwitchMarker` (p1=description), `TwitchRaid` (p1=channel), `TwitchStopRaid`, `TwitchClearChat`, `TwitchEmoteOnlyToggle`, `TwitchFollowerOnlyToggle`, `TwitchShieldToggle` (toggles read current chat settings then flip) |
| Kick (4) | `KickClearChat`, `KickEmoteOnly`, `KickFollowerOnly`, `KickSlowMode` (p2=on/off) |
| Audio (2) | `SoundPlay` (p1=file, p2=volume 0.0–1.0 clamped), `AlertShow` (p1=title, p2=message) |
| Automation (1) | `WorkflowRun` (p1=workflow name or id; runs via `WorkflowRuntime`, Platform="StreamDeck") |
| Smart home (2) | `LightsFlash` (p1=hex color, p2=times 1–10 default 3), `LightsPower` (p2=on/off) |
| Advanced (1) | `UriLaunch` (p1=uri) |

**URI allowlist:** `UriLaunch` only accepts `http`, `https`, `obs` and
`streamdeck` schemes — a paired phone cannot shell out to local executables
(`file:`, raw `.exe` paths). The launch is a deliberate `Process.Start`
shell hand-off (browser / Stream Deck app), NOT routed through `ProcessManager`,
so zero-orphan shutdown doesn't kill the user's browser.

`DeckModels.cs` defines `DeckActionType` (the one flat enum shared by desktop
page, LAN protocol and mobile app), `DeckButton`, `DeckPage` (**12 slots per
page**), `DeckLayout`, and `DeckActionCatalog` — the human-facing metadata
(display name, category, param labels/placeholders) serialized in `welcome.catalog`.

`ObsRemoteBridge.cs` is a lazily-connected obs-websocket client shared by the
three OBS actions: it locates the local OBS install, connects on first use,
reuses the socket and drops it on any failure so the next press reconnects.

## Layout persistence (`DeckLayoutStore.cs`)
- Source of truth: profile config KV key **`VyralDeck.Layout`** (JSON), with an
  in-memory cache shared by the desktop page and the server.
- Every save raises `LayoutChanged` → `DeckServer` rebroadcasts `config` to
  every connected phone, and the desktop page refreshes when a phone edited
  the layout. `config_set` from a phone persists on the PC and is echoed back
  to all clients including the sender.
- `Normalize` guarantees every page has exactly 12 slots (pads/trims).
- Corrupt JSON falls back to the default layout (Clip / Marker / Start / Stop
  buttons on the "Main" page) rather than crashing.
- Server settings (port, auto-start) persist as `VyralDeck.ServerSettings`.

## Mobile app (`mobile/vyral_deck/`, Flutter)
- **Screens:** `connect_screen.dart` (discovery + manual IP + PIN pairing),
  `deck_screen.dart` (button grid, page switching, long-press edit),
  `tools_screen.dart` (ad-hoc `action` messages without saved buttons).
- **Services:** `discovery.dart` (UDP 5198 broadcast), `pairing.dart`
  (`POST /vyraldeck/pair`), `deck_connection.dart` (WebSocket session,
  token-authenticated, ping/pong keepalive).
- **Core:** `models.dart` + `action_catalog.dart` mirror the desktop models;
  the catalog is refreshed from `welcome.catalog`.
- **Widgets:** `button_editor_sheet.dart` — the same editor as desktop, driven
  by the catalog metadata.
- Release builds land in `releases/` as the VyralDeck APK (never committed).

## Tests
- Desktop: `tests/VyralBot.UI.Tests/StreamDeckServiceTests.cs` (URI-action
  store: scheme allowlist enforcement, upsert/delete, trigger dispatch).
- Mobile: `mobile/vyral_deck/test/widget_test.dart` (`flutter test`).
