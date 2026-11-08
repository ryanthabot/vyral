# Leaderboard Overlay

## Overview
Points-leaderboard OBS browser source: a dependency-free widget page
(`leaderboard.html`) served by the embedded loopback web server that shows the
top viewers by currency balance and refreshes itself every 10 seconds. The
Currency page's Leaderboard tab has a "Leaderboard Overlay" card that builds
the URL from the chosen row count and title and copies it to the clipboard for
pasting into an OBS Browser Source. Data comes from the same
`ICurrencyService.GetLeaderboardAsync` query the Currency page and the
`!leaderboard` chat command use, so the overlay is per-profile (the active
profile's SQLite database) and ordering is deterministic (Balance DESC, then
Username COLLATE NOCASE, then Platform).

## Source Files
| File | Description |
|------|-------------|
| `src/VyralBot.Api/Controllers/LeaderboardController.cs` | Loopback GET `/leaderboard/data` JSON endpoint |
| `src/VyralBot.Api/wwwroot/leaderboard.html` | OBS browser-source widget (no external dependencies) |
| `src/VyralBot.Api/Services/WebServerService.cs` | Forwards `ICurrencyService` from the WPF host into the embedded Kestrel (`FromParent`) |
| `src/VyralBot.UI/ViewModels/CurrencyViewModel.cs` | Overlay URL builder + `CopyLeaderboardOverlayUrlCommand` |
| `src/VyralBot.UI/Views/CurrencyView.xaml` | "Leaderboard Overlay" card on the Leaderboard tab |
| `src/VyralBot.Core/Services/Currency/CurrencyService.cs` | `GetLeaderboardAsync(limit, platform)` (existing query) |

## Endpoint
`GET /leaderboard/data?count=10&platform=` (loopback only — the embedded
server binds `ListenLocalhost`):

| Param | Default | Rules |
|-------|---------|-------|
| `count` | 10 | Clamped to 1..25 (size cap, not an error — a stale OBS URL keeps working) |
| `platform` | (all) | Optional; case-insensitive match against the known platform set (Twitch, Kick, YouTube, TikTok, Discord, Facebook, Vyral); anything else → `400 {"error":"invalid_platform"}` |

Response (`Cache-Control: no-store`; anonymous-object camelCase JSON like the
`/songs` routes):

```json
{
  "currencyName": "Points",
  "currencySymbol": "⭐",
  "count": 10,
  "platform": null,
  "entries": [
    { "rank": 1, "username": "viewer", "platform": "Twitch", "balance": 12500 }
  ]
}
```

When the currency service is not registered (self-host scenario) the endpoint
returns `503 {"error":"leaderboard_disabled"}` — same degradation convention as
`SongsController`.

## Widget URL params
`http://localhost:<port>/leaderboard.html?count=10&title=Top%20Points&style=dark&platform=`

| Param | Default | Meaning |
|-------|---------|---------|
| `count` | 10 | Rows to show (1..25, forwarded to the data endpoint) |
| `title` | `Top Points` | Heading text (URL-encoded, truncated to 60 chars) |
| `style` | `dark` | `dark` (translucent night card) or `light` (translucent white card) |
| `platform` | (all) | Optional single-platform filter, forwarded to the data endpoint |

Widget behavior: polls every 10s (never overlapping requests, reconnects
forever); rank badges 1-3 get gold/silver/bronze tints; a platform letter chip
uses the app's brand colors (`PlatformToBrushConverter`: Twitch `#9146FF`,
Kick `#53FF1A`, YouTube `#FF0000`, Discord `#5865F2`, TikTok black chip with
white letter, Facebook white chip with blue letter); points are
locale-formatted; rows are keyed by `username|platform` and re-ranked with a
FLIP transform animation (smooth reorder), new entries fade/slide in, and the
status badge follows the `audio.html` conventions (connected auto-hides after
5s, disconnected/connecting stay visible).

## Security model
Same as the `/songs` GET route: the server binds loopback only, the page is
served from the same origin, and the endpoint is read-only with a size-capped
count — so no POST-style Origin gate is required. The token-gated control API
(`/api/*`) is unaffected.

## UI (Currency page → Leaderboard tab)
The "Leaderboard Overlay" card has: Rows (`OverlayCount`, clamped 1..25) and
Title (`OverlayTitle`) inputs; the live URL
(`http://localhost:<port>/leaderboard.html?count=…&title=…` — the port comes
from Web Server settings via `GetWebServerSettingsAsync`, 5000 fallback, same
convention as the audio browser-source URL); a COPY button; and a hint that it
is for OBS Browser Sources on the same machine (loopback).
