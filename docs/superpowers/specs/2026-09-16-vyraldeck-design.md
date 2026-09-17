# VyralDeck — Stream Deck section + Mobile Companion App (Design)

Date: 2026-09-16
Status: Approved (autonomous session — user request was detailed; defaults chosen and listed below)

## What was asked

1. A **StreamDeck** section **under Restream** in VyralBot: a grid of buttons that do things (like Elgato Stream Deck / Stream Assistant).
2. A **mobile companion app written in Dart (Flutter)** for **iOS + Android** that connects to the PC **on the same network only**, can **press the buttons**, and **customize the deck from the phone**, plus extra stream tools built on Vyral's existing services.

## Names

- UI section: **StreamDeck** (as requested). Internal code: **VyralDeck** (`VyralBot.UI.Services.VyralDeck`) because `VyralBot.Integrations.StreamDeck` already exists (Elgato `streamdeck://` URI launcher — untouched, still works from Settings → Stream FX).

## Desktop side (VyralBot.UI)

### Data model (`Services/VyralDeck/DeckModels.cs`)
- `DeckActionType` enum: `ObsScene`, `ObsStreamStart`, `ObsStreamStop`, `ChatSend`, `TwitchClip`, `TwitchMarker`, `TwitchRaid`, `TwitchStopRaid`, `TwitchClearChat`, `TwitchEmoteOnlyToggle`, `TwitchFollowerOnlyToggle`, `TwitchShieldToggle`, `KickClearChat`, `KickEmoteOnly`, `KickFollowerOnly`, `KickSlowMode`, `SoundPlay`, `AlertShow`, `WorkflowRun`, `LightsFlash`, `LightsPower`, `UriLaunch`.
- `DeckButton { Id, Label, Icon, ColorHex, ActionType, Param1, Param2 }` — Param1/Param2 meaning depends on action type (scene name / message / target channel / sound file / color …).
- `DeckPage { Id, Name, Buttons(12) }`, `DeckLayout { Pages }`.
- Persisted via `IDataService` config KV: key `VyralDeck.Layout`, category `VyralDeck`.

### Executor (`Services/VyralDeck/DeckActionExecutor.cs`)
One `ExecuteAsync(DeckButton, IServiceProvider)` → result message (never throws). Wires existing singletons: `IStreamActionsService`, `ITwitchService`, `IKickService`, `IYouTubeService`, `ISoundService`, `IAlertService`, `ILightsClient`, `WorkflowRuntime` + `IDataService` (workflows by name via `WorkflowJson.FromRow`), `IStreamDeckService` fallback not used (UriLaunch shells out directly, which also covers `streamdeck://` links).
OBS actions go through `ObsRemoteBridge` — a lazily-connected `ObsWebSocketClient` using `ObsWebSocketConfigReader` (same source as the OBS page), reused across presses, reconnect-on-failure.

### LAN server (`Services/VyralDeck/DeckServer.cs`)
- Own Kestrel mini-host (same pattern as `WebServerService`, but `ListenAnyIP`): this is a deliberate LAN endpoint; it is **off by default**, started from the StreamDeck page (optional auto-start setting).
- `GET /vyraldeck/info` → identity JSON (no auth, discovery aid).
- `POST /vyraldeck/pair {pin, deviceName}` → rate-limited (5 failures/min) → `{token}`; tokens are random 256-bit, stored per-device in config KV (`VyralDeck.Devices`).
- `GET /vyraldeck/ws?token=` → WebSocket session (paired devices only):
  - server → `welcome {serverName, layout, state}`, `config {layout}` (broadcast on edit), `state {live:{...}}` (pushed on `IStreamLiveRegistry.StreamLiveChanged`), `press_result`, `pong`, `error`.
  - client → `press {buttonId}`, `action {actionType, param1, param2}` (ad-hoc tool execution from the mobile Tools tab), `config_set {layout}` (mobile edits persist on PC and rebroadcast), `ping`.
- UDP discovery on port `5198`: client broadcasts `VYRALDECK_DISCOVER`, server replies `{app:"VyralDeck", name, tcpPort}`.
- Default TCP port `5199` (configurable). Windows-firewall note shown in UI (user approves the private-network prompt once).

### Page (`Views/StreamDeckView.xaml` + `ViewModels/StreamDeckViewModel.cs`)
Styled like `RestreamView` (MaterialDesign cards, dark theme).
1. **Mobile Server card**: start/stop, port, LAN IP display, pairing PIN (regenerate), auto-start toggle, paired-device list with revoke.
2. **Deck editor card**: page chips (add/delete), 4×3 button grid; tap runs the action (status line shows result); **Edit Mode** toggle turns taps into "open editor".
3. **Button editor card** (visible while editing): label, emoji icon, color swatches, action type dropdown with per-type param fields, Save / Clear slot.

### Navigation
`Restream` sidebar entry becomes a collapsible group (same pattern as Platforms): children **Restream** and **StreamDeck**. New `NavigateToStreamDeckCommand` in `MainViewModel` (scope + dispose like the rest), DataTemplate in `MainWindow.xaml`, DI registration in `App.xaml.cs`.

## Mobile side (`VyralBot/mobile/vyral_deck`, Flutter/Dart)

- Dark-teal theme matching the desktop app.
- **Connect screen**: saved host (SharedPreferences), manual `ip:port` entry, PIN pairing, UDP "Scan" discovery, only-LAN usage (no cloud, no external services).
- **Deck screen**: page tabs, 4×3 grid mirroring the PC config; tap = press; edit mode → editor sheet (label, emoji, color, action type + params) which saves back to the PC via `config_set`; live-status chips; toast-style result feedback.
- **Tools tab**: one-tap tools (Clip, Marker, Raid…, Clear chat, Emote-only, Shield, Lights flash) + a chat-send box (platform picker) — all via the ad-hoc `action` message.
- Reconnects automatically; servers with no paired devices require fresh pairing (PIN shown on the PC).

## Decisions taken without asking (defaults)

- Port 5199 TCP / 5198 UDP; changeable in UI.
- 12 buttons per page (4×3), unlimited pages.
- Edits made on the phone are authoritative once saved (PC persists + rebroadcasts to other clients).
- No REST endpoint for layout — everything else goes over the single authenticated WebSocket.
- Restream start/stop buttons are **not** in v1 (engine start needs a full session config; the OBS stream start/stop covers the common case).

## Testing

- `dotnet build` the solution (compile gate).
- `flutter analyze` on the mobile app (static gate) when a Flutter SDK is available; otherwise the Dart code is written SDK-version conservative.
- Protocol documented in `VyralBot/mobile/vyral_deck/PROTOCOL.md` (shared contract).
