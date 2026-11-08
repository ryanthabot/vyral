# StreamDeck & VyralDeck Mobile

> A customizable button grid — like a hardware Stream Deck, but in the app — that fires Vyral's real stream tools (OBS, chat, Twitch/Kick mod actions, sounds, alerts, workflows, smart lights), plus an optional LAN server the **VyralDeck** phone app pairs with to press and edit the same deck from the couch.

## Overview

Two halves, one deck. The **StreamDeck page** (sidebar, under the Restream expander) is the desktop editor/launcher: pages of 12 button slots, each slot a label, icon, color and an action from a 22-action catalog. The **VyralDeck mobile companion** (Flutter, iOS + Android — see the repo's `mobile/vyral_deck/`) connects over your home Wi-Fi and gets the *same* layout, live-editing it and pressing buttons remotely.

The phone link is deliberately cautious:

- **LAN-only by design** — the deck server is a separate Kestrel host listening on all interfaces, but nothing is reachable from outside your network; no tunnel, no ngrok (DeckServer.cs:18-30).
- **Off by default** — you start it from the StreamDeck page. An **Auto-start on launch** toggle remembers your choice (DeckPairingStore.cs:9-14; StreamDeckViewModel.cs:139-162).
- **PIN pairing + per-device tokens** — the desktop shows a 6-digit PIN valid 10 minutes; a phone exchanges it (with a device name) for a random 256-bit token that stays paired across restarts. A PIN pairs exactly one device and is consumed; failed attempts are rate-limited to 5 per rolling minute (DeckPairingStore.cs:38-39, 90-157).
- **Ports 5199 TCP / 5198 UDP** — TCP carries the HTTP + WebSocket endpoints; UDP 5198 answers `VYRALDECK_DISCOVER` broadcasts so the phone can find the PC without typing an IP. If 5198 is busy, discovery silently degrades and manual IP entry still works (DeckServer.cs:33-34, 488-547).

Contrast with the [webhook server](../settings/web-server-ngrok.md): that one binds **loopback only** (ngrok carries it out when needed). The deck server is the only LAN-exposed surface in the app, and every session endpoint requires a paired token.

## User Guide

### The button grid

The page shows the current deck page as a 12-slot grid (labels + icons; empty slots are dimmed). Tap a slot to **run** it — the result line under the grid shows "✅ Clip created." style feedback. **EDIT MODE** toggles configuration: in edit mode, tapping a slot opens the editor (label, emoji icon, one of nine preset colors, the action dropdown, and the action's one or two parameters). **CLEAR SLOT** empties it. Pages: **+ Add Page / Delete** (the deck always keeps at least one), page pills select the active page (StreamDeckViewModel.cs:269-440).

The default layout ships with four configured buttons: Clip, Marker, Start Stream, Stop Stream (DeckModels.cs:102-110).

### The action catalog (22 actions)

| Category | Actions |
|---|---|
| OBS | Switch Scene (scene name), Start Streaming, Stop Streaming |
| Chat | Send Message (message + platform twitch/kick/youtube) |
| Twitch | Create Clip, Stream Marker, Raid Channel, Cancel Raid, Clear Chat, Toggle Emote-Only, Toggle Follower-Only, Toggle Shield Mode |
| Kick | Clear Chat, Emote-Only, Follower-Only, Slow Mode (each on/off) |
| Audio | Sound: Play (file + optional volume 0.0-1.0), Alert: Show (title + optional message) |
| Automation | Run Workflow (workflow name) |
| Smart Home | Lights: Flash (hex color, times), Lights: Power (on/off) |
| Advanced | Launch URI |

Source: `DeckActionCatalog.Actions` (DeckModels.cs:131-157) — one flat list shared verbatim by the desktop editor and the phone (the server sends it in the welcome message, so the app never carries a hardcoded copy). **Sound Play** plays any file from your [sounds library](sounds.md) at an optional volume; **Alert Show** fires the [overlay alert](visual-alerts.md) titled from the button ("Stream Deck" origin, default title "Stream Deck") — (DeckActionExecutor.cs:121-132, 255-266).

### Pairing a phone

1. On the PC: **Start Server** (set the port first if 5199 is taken). A fresh PIN appears — **Regenerate PIN** mints a new one any time.
2. On the phone (same Wi-Fi): open VyralDeck, discover or enter the PC's address, and enter the PIN with a device name.
3. The device appears in **Paired Devices** with its last-seen time; **Unpair** revokes its token instantly (StreamDeckViewModel.cs:175-225).

While the server runs, the page shows the LAN address, connected-client count and a live log line. Stopping the server closes every connected phone cleanly.

### Editing from the phone

The phone's layout edits are real edits: a `config_set` message persists to the same config store on the PC and rebroadcasts the new layout to every connected client *including the desktop grid*, which refreshes in place (DeckServer.cs:396-442; StreamDeckViewModel.cs:255-265). Live-status pushes (`state`) flip Twitch/Kick/YouTube live indicators on the phone whenever a platform goes live or offline (DeckServer.cs:444-455). Since 1.1.0 the `state` payload also carries three optional **stream-context strings** the mobile app renders as status chips (DeckServer.cs:512-546):

- `nowPlaying` — the current song-request track (from the song queue);
- `uptimeText` — the same formatted uptime the `!uptime` command reads (`Stream.StartedAtUtc`);
- `deathCount` — the shared death counter as a string (the Tools card / `!death` value).

Each key is **omitted** when its backing service or data is unavailable (no song playing, stream offline, counter service absent), and unknown keys are ignorable by older clients — the protocol is forward-compatible. The chips refresh with every state push (platform live flips push a fresh `state`).

## How It Works (Developer)

- **DeckServer** is its own `WebApplication` (not the webhook server): `GET /vyraldeck/info` (unauthenticated identity probe), `POST /vyraldeck/pair` (PIN → token, rate-limited), `GET /vyraldeck/ws?token=…` (the authenticated session socket; unknown token = 401). HTTP/1 only, all interfaces, TCP-port-free probe before bind (DeckServer.cs:89-225).
- **WebSocket protocol v1** is documented in [`mobile/vyral_deck/PROTOCOL.md`](../../mobile/vyral_deck/PROTOCOL.md): welcome (layout + live state + action catalog) → press / action / config_set / ping from the client; press_result / config / state / pong / error back. The `state` payload is the per-platform live flags plus the optional `nowPlaying`/`uptimeText`/`deathCount` context strings above. Per-connection send serialization via semaphore; 256 KB receive buffer (DeckServer.cs:278-329, 657-713).
- **DeckActionExecutor** resolves services from the DI provider per press and never throws — every failure returns as a ❌ message string for the status line / phone (DeckActionExecutor.cs:45-61). OBS actions go through **ObsRemoteBridge**: a lazily-connected obs-websocket client that locates the local OBS install, reads the plugin's remote-control config (must be enabled, password set), connects on first press and drops the cached socket on any failure so the next press reconnects (ObsRemoteBridge.cs:14-105). Workflow actions look the flow up by name-or-id and run it through the real [WorkflowRuntime](../chat-features/workflows.md) with `Platform="StreamDeck"` (DeckActionExecutor.cs:268-295).
- **Security details**: tokens are 256-bit hex from `RandomNumberGenerator`, compared with `FixedTimeEquals` (timing-attack safe), never logged; the PIN lives only in memory; devices/tokens/layout/server settings persist as config KV rows in category "VyralDeck" (DeckPairingStore.cs:36-37, 142-147, 242-247; DeckLayoutStore.cs:16).
- **Layout store**: source of truth in config KV (`VyralDeck.Layout`), in-memory cache shared by page + server, `LayoutChanged` event fans out; `Normalize` guarantees every page is exactly 12 slots (DeckLayoutStore.cs:14-139).

## Files & Code

| File | Role |
|---|---|
| [StreamDeckViewModel.cs](../../src/VyralBot.UI/ViewModels/StreamDeckViewModel.cs) | Page VM (singleton): server controls, PIN, devices, page/slot editing, live-state badges |
| [DeckServer.cs](../../src/VyralBot.UI/Services/VyralDeck/DeckServer.cs) | The LAN Kestrel host: info/pair/ws endpoints, UDP discovery, broadcasts |
| [DeckPairingStore.cs](../../src/VyralBot.UI/Services/VyralDeck/DeckPairingStore.cs) | PIN lifecycle, device tokens, server settings persistence |
| [DeckLayoutStore.cs](../../src/VyralBot.UI/Services/VyralDeck/DeckLayoutStore.cs) | Layout persistence + cache + change event |
| [DeckModels.cs](../../src/VyralBot.UI/Services/VyralDeck/DeckModels.cs) | `DeckActionType` (22 values), button/page/layout models, the shared action catalog |
| [DeckActionExecutor.cs](../../src/VyralBot.UI/Services/VyralDeck/DeckActionExecutor.cs) | Press dispatch to OBS/chat/Twitch-Helix/Kick/sounds/alerts/workflows/lights/URI |
| [ObsRemoteBridge.cs](../../src/VyralBot.UI/Services/VyralDeck/ObsRemoteBridge.cs) | Lazy obs-websocket client for the deck's OBS actions |
| [PROTOCOL.md](../../mobile/vyral_deck/PROTOCOL.md) | The v1 wire protocol — endpoints, message shapes, action list |

## Data & Settings

| Key | Store | Meaning |
|---|---|---|
| `VyralDeck.Devices` | Configs (category "VyralDeck") | Paired devices: token, name, paired/last-seen timestamps |
| `VyralDeck.ServerSettings` | Configs | Port (default 5199) + `AutoStartOnLaunch` |
| `VyralDeck.Layout` | Configs | Pages of 12 button slots (label, icon, colorHex, actionType, param1/2) |

## Status & Known Issues

1. **Server is off until you start it** — nothing listens on 5199/5198 before the first Start Server (or the auto-start toggle) (DeckServer.cs:89-99; StreamDeckViewModel.cs:149-162).
2. **UDP discovery is best-effort** — port 5198 busy means no auto-discovery; the phone falls back to manual IP entry (DeckServer.cs:540-547).
3. **Chat Send has no Discord action** — messages go to Twitch, Kick or YouTube only (DeckActionExecutor.cs:180-227).
4. **Remote control needs OBS's websocket on** — the OBS actions fail with an actionable message until you enable *Tools → WebSocket Server Settings* in OBS (ObsRemoteBridge.cs:88-98).
5. **Tokens are long-lived** — unpairing on the PC is the only revocation path; there is no token expiry.

### Verified against (2026-11-07)

`DeckServer.cs` (endpoints + ports :33-34, 117-185; pairing flow :132-171; ws session + message handling :278-430; discovery :488-547; LAN address pick :603-637), `DeckPairingStore.cs` (settings :9-14; PIN :90-103; TryPair rate-limit/consume :112-157; FixedTimeEquals :242-247), `DeckModels.cs` (enum :10-34, 12-slot pages :71-87, default page :102-110, catalog :131-157), `DeckLayoutStore.cs` (whole file), `DeckActionExecutor.cs` (whole dispatch :63-164; chat :180-227; sound/alert :121-132, 255-266; workflow :268-295), `ObsRemoteBridge.cs` (whole file), `StreamDeckViewModel.cs` (server commands :175-241; editor :269-440; external layout reload :255-265), `mobile/vyral_deck/PROTOCOL.md` (whole file).
