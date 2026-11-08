# Chat Feed Overlay

## Overview
Chat-feed OBS browser source (face-cam captions): a dependency-free widget page
(`chatfeed.html`) served by the embedded loopback web server that shows the
last few chat messages as a vertical caption stack (newest at the bottom),
refreshing itself every 2 seconds. The "Browser Sources" section of the app
(Stream Tools → Browser Sources, rendered on the Audio Settings page) has a
"Chat feed overlay" row that shows the URL and copies it to the clipboard for
pasting into an OBS Browser Source. Data comes from the same
`IChatMessageStore` history the Chat page loads (`GetRecentAsync` — the
per-profile `ChatMessages` table the platform chat services write via
`AddAsync`), so the overlay is per-profile and reflects exactly what the
streamer sees in Chat.

## Source Files
| File | Description |
|------|-------------|
| `src/VyralBot.Api/Controllers/ChatFeedController.cs` | Loopback GET `/chatfeed/data` JSON endpoint |
| `src/VyralBot.Api/wwwroot/chatfeed.html` | OBS browser-source widget (no external dependencies) |
| `src/VyralBot.Api/Services/WebServerService.cs` | Forwards `IChatMessageStore` from the WPF host into the embedded Kestrel (`FromParent`) |
| `src/VyralBot.UI/ViewModels/AudioSettingsViewModel.cs` | `ChatFeedOverlayUrl` builder + `CopyChatFeedOverlayUrlCommand` |
| `src/VyralBot.UI/Views/AudioSettingsView.xaml` | "Chat feed overlay" URL + Copy row in the Browser Source (OBS) card |
| `src/VyralBot.Data/DapperDataService.cs` | `GetChatMessagesAsync` — null/empty channel = wildcard across the platform's channels |
| `src/VyralBot.Core/Services/Chat/InMemoryChatMessageStore.cs` | Store API reference (`IChatMessageStore`, 500-message windows per platform+channel) |

## Endpoint
`GET /chatfeed/data?count=5&platform=&channel=` (loopback only — the embedded
server binds `ListenLocalhost`):

| Param | Default | Rules |
|-------|---------|-------|
| `count` | 5 | Clamped to 1..15 (size cap, not an error — a stale OBS URL keeps working) |
| `platform` | (all) | Optional; case-insensitive match against the chat platforms that persist messages (Twitch, Kick, YouTube, Discord); anything else → `400 {"error":"invalid_platform"}` |
| `channel` | (all) | Optional channel key; empty = most recent messages across ALL channels of the platform (the overlay URL works without knowing channel keys) |

Response (`Cache-Control: no-store`; anonymous-object camelCase JSON like the
`/leaderboard` route; `messages` are NEWEST FIRST — the widget re-orders them
newest-bottom):

```json
{
  "count": 5,
  "platform": null,
  "channel": null,
  "messages": [
    { "username": "viewer", "platform": "Twitch", "message": "hello!", "at": "2026-11-07T18:00:01Z" }
  ]
}
```

Each platform's concrete chat event type (`TwitchChatMessageEvent`,
`KickChatMessageEvent`, `YouTubeChatMessageEvent`, `DiscordChatMessageEvent`)
is mapped to `{ username, platform, message, at }` (display name preferred,
UTC-pinned timestamps). When the store is not registered (self-host scenario)
the endpoint returns `503 {"error":"chatfeed_disabled"}` — same degradation
convention as `SongsController`/`LeaderboardController`.

## Widget URL params
`http://localhost:<port>/chatfeed.html?count=5&fade=true&size=16&maxlength=120&platform=`

| Param | Default | Meaning |
|-------|---------|---------|
| `count` | 5 | Messages to show (1..15, forwarded to the data endpoint) |
| `fade` | `false` | `true` dims older messages (newest row opacity 1.0 → oldest ~0.35) |
| `size` | 16 | Base font size in px (clamped 10..48; the whole stack scales with it) |
| `maxlength` | 120 | Message truncation with an ellipsis (clamped 20..500) |
| `platform` | (all) | Optional single-platform filter, forwarded to the data endpoint |
| `style` | `dark` | `dark` (translucent night card) or `light` (translucent white card) — same as `leaderboard.html` |

Widget behavior: polls every 2s (never overlapping requests, reconnects
forever); rows show a colored platform letter chip (T/K/Y/D with the app's
brand colors — Twitch `#9146FF`, Kick `#53FF1A`, YouTube `#FF0000`, Discord
`#5865F2`; unknown platforms get a neutral chip), a bold username and the
message text; rows are keyed by `platform|username|at|message` so existing
messages keep their DOM node and only genuinely new messages play the
slide-in-from-below keyframe; the status badge follows the `audio.html`
conventions (connected auto-hides after 5s, disconnected/connecting stay
visible).

**XSS safety:** chat messages are untrusted user content. Every dynamic value
(username, message, platform chip letter) is assigned via `textContent`; the
row skeleton built with `innerHTML` contains only constant markup. User
content is never assigned to `innerHTML`.

## Security model
Same as the `/leaderboard` GET route: the server binds loopback only, the page
is served from the same origin, and the endpoint is read-only with a
size-capped count — so no POST-style Origin gate is required. The token-gated
control API (`/api/*`) is unaffected.

## UI (Browser Sources section — Audio Settings page)
The "Browser Source (OBS)" card (Stream Tools → Browser Sources) has a
"Chat feed overlay" row beneath the audio source URL: a read-only URL box
(`http://localhost:<port>/chatfeed.html?count=5` — the port comes from Web
Server settings via `GetWebServerSettingsAsync`, 5000 fallback, refreshed on
Restart Server exactly like the audio URL) and a Copy URL button
(`CopyChatFeedOverlayUrlCommand`), plus a hint listing the supported URL
params.
