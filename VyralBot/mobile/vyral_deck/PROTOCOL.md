# VyralDeck LAN protocol (v1)

JSON over WebSocket. Server: VyralBot (Windows PC). Client: VyralDeck mobile app.
LAN-only by design; every session is authenticated with a paired-device token.

## Endpoints (desktop, port 5199 by default)

| Endpoint | Auth | Purpose |
|---|---|---|
| `GET /vyraldeck/info` | none | `{app:"VyralDeck", serverName, protocol, tcpPort, discoveryPort, pairedDevices}` |
| `POST /vyraldeck/pair` | PIN | body `{pin, deviceName}` → `{ok:true, token, serverName, protocol}` or 403 `{ok:false, error}` (rate-limited: 5 failures/min) |
| `GET /vyraldeck/ws?token=…` | token | the session WebSocket (401 when the token is unknown) |
| UDP :5198 | none | client broadcasts `VYRALDECK_DISCOVER` → server replies `{app:"VyralDeck", serverName, tcpPort}` |

## WebSocket messages

All messages are single-frame JSON objects with a `type` field (camelCase keys).

Server → client:

```jsonc
{ "type": "welcome", "serverName": "GAMING-PC", "protocol": 1,
  "layout": { "pages": [ { "id": "…", "name": "Main", "buttons": [ {"id":"…","label":"Clip","icon":"🎬","colorHex":"#9146FF","actionType":"TwitchClip","param1":"","param2":""}, …12 slots ] } ] },
  "state": { "Twitch": true, "Kick": false, "YouTube": false },
  "catalog": [ {"type":"TwitchClip","name":"Twitch: Create Clip","category":"Twitch","param1Label":null,"param2Label":null,"param1Placeholder":"","param2Placeholder":""}, … ] }

{ "type": "config", "layout": { … } }          // layout changed (phone or desktop edit)
{ "type": "state",  "state": { … } }           // live flags pushed on live/offline; context keys also refresh on a periodic pump (deduped)
{ "type": "press_result", "buttonId": "…", "ok": true, "message": "✅ Clip created." }
{ "type": "config_saved", "ok": true }
{ "type": "pong" }
{ "type": "error", "message": "…" }
```

Client → server:

```jsonc
{ "type": "press", "buttonId": "…" }                                  // fire a saved button
{ "type": "action", "actionType": "TwitchMarker", "param1": "boss", "param2": "" }  // ad-hoc tool
{ "type": "config_set", "layout": { … } }                             // save an edited layout
{ "type": "ping" }
```

`config_set` persists on the PC (config KV store) and is rebroadcast as
`config` to every connected client, including the sender.

## Live-state payload

`welcome.state` and every `state` frame carry the same map:

```jsonc
{ "Twitch": true, "Kick": false, "YouTube": false,
  "nowPlaying": "Never Gonna Give You Up",   // optional: current song-request title
  "uptimeText": "2h 14m",                    // optional: same source the !uptime command reads
  "deathCount": "42" }                       // optional: the shared !death counter
```

The optional context keys are strings, omitted whenever the PC cannot fill
them (no song playing, stream offline, service absent). Clients should render
only the keys they receive and ignore the rest; older clients ignore unknown
keys entirely (they read the platform flags only).

## Action types

`ObsScene` (p1=scene), `ObsStreamStart`, `ObsStreamStop`, `ChatSend` (p1=message,
p2=twitch|kick|youtube), `TwitchClip`, `TwitchMarker` (p1=description),
`TwitchRaid` (p1=channel), `TwitchStopRaid`, `TwitchClearChat`,
`TwitchEmoteOnlyToggle`, `TwitchFollowerOnlyToggle`, `TwitchShieldToggle`,
`KickClearChat`, `KickEmoteOnly` (p2=on/off), `KickFollowerOnly`, `KickSlowMode`,
`SoundPlay` (p1=file, p2=volume), `AlertShow` (p1=title, p2=message),
`WorkflowRun` (p1=workflow name), `LightsFlash` (p1=hex color, p2=times),
`LightsPower` (p2=on/off), `UriLaunch` (p1=uri).

The authoritative list rides along in the `welcome.catalog` message.
