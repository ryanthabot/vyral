# Goals Overlay

## Overview
Goals-progress OBS browser source: a dependency-free widget page (`goals.html`)
served by the embedded loopback web server that renders one animated progress
bar per enabled Vyral goal and refreshes itself every 15 seconds. The Viewer
Hub's Vyral Goals card has an "Overlay URL" row that shows the URL (built from
Web Server settings) and copies it to the clipboard for pasting into an OBS
Browser Source. Data comes from the same `ISocialGoalService` the Viewer Hub
goal editor uses (`GetGoalsAsync` + `GetCompletionCountAsync`), so the overlay
is per-profile (the active profile's SQLite database).

## Goal model mapping
A Vyral `SocialGoal` is a broadcaster-defined viewer challenge; the aggregate
progress the service exposes is how many viewers completed it. The endpoint
maps the entity to the widget's display fields:

| SocialGoal field | Overlay field |
|------------------|---------------|
| `Name` | `title` |
| `GoalType` (`LinkedPlatforms` / `FollowPlatforms` / `SubAnyPlatform` / `SubTier`) | `kind` (`linked` / `follows` / `subs` / `sub tier`; unknown types pass through raw) |
| `Description` | `subtitle` |
| `GetCompletionCountAsync(goal.Id)` | `current` (aggregate completion count) |
| `TargetCount` | `target` (the per-viewer threshold, e.g. "link 2 platforms") |

Only enabled goals (`IsEnabled`) are returned, ordered by `SortOrder` then
name. No usernames, viewer IDs or any other PII leave the process — only
counts and display text.

## Source Files
| File | Description |
|------|-------------|
| `src/VyralBot.Api/Controllers/GoalsController.cs` | Loopback GET `/goals/data` JSON endpoint |
| `src/VyralBot.Api/wwwroot/goals.html` | OBS browser-source widget (no external dependencies) |
| `src/VyralBot.Api/Services/WebServerService.cs` | Forwards `ISocialGoalService` from the WPF host into the embedded Kestrel (`FromParent`) |
| `src/VyralBot.UI/ViewModels/ViewerHubViewModel.cs` | `GoalsOverlayUrl` builder + `CopyGoalsOverlayUrlCommand` (`LoadOverlayPortAsync`) |
| `src/VyralBot.UI/Views/ViewerHubView.xaml` | "Overlay URL" row with COPY in the Vyral Goals card |
| `src/VyralBot.Core/Services/Linking/SocialGoalService.cs` | `GetGoalsAsync` / `GetCompletionCountAsync` (existing queries) |

## Endpoint
`GET /goals/data` (no params; loopback only — the embedded server binds
`ListenLocalhost`). Response (`Cache-Control: no-store`; anonymous-object
camelCase JSON like the `/leaderboard` route):

```json
{
  "goals": [
    {
      "id": "b0f4…",
      "kind": "linked",
      "title": "Bridge Builder",
      "subtitle": "Link 2 platforms to your viewer identity.",
      "current": 7,
      "target": 2
    }
  ]
}
```

When the social goal service is not registered (self-host scenario) the
endpoint returns `503 {"error":"goals_disabled"}` — same degradation
convention as `LeaderboardController`.

## Widget URL params
`http://localhost:<port>/goals.html?style=dark&size=m`

| Param | Default | Meaning |
|-------|---------|---------|
| `style` | `dark` | `dark` (translucent night card) or `light` (translucent white card) |
| `size` | `m` | `m` (compact cam-side bar) or `l` (bigger fonts + taller bar for full-screen layouts) |

Widget behavior: polls every 15s (never overlapping requests, reconnects
forever); one card per goal with a kind chip, title, big `current / target`
count (locale-formatted, tabular numerals) and a teal gradient bar whose
width animates via CSS transition (percent = `min(100, current / target)`,
target treated as at least 1); cards are keyed by `id` so updates only
transition the bar width instead of rebuilding, and new goals slide in.
Celebration state at 100% (`current >= target`): the fill switches to a
rainbow shimmer (animated gradient sweep) plus a "Complete" tag. The status
badge follows the `audio.html` conventions (connected auto-hides after 5s,
disconnected/connecting stay visible).

## Security model
Same as the `/leaderboard` GET route: the server binds loopback only, the
page is served from the same origin, and the endpoint is read-only — so no
POST-style Origin gate is required. The token-gated control API (`/api/*`) is
unaffected.

## UI (Settings → Viewer Hub → Vyral Goals)
Below the goals DataGrid, an "Overlay URL" row shows
`http://localhost:<port>/goals.html` (the port comes from Web Server settings
via `GetWebServerSettingsAsync`, 5000 fallback — same convention as the
Currency page's leaderboard overlay URL) with a COPY button
(`CopyGoalsOverlayUrlCommand`; clipboard-busy failures are non-fatal and
surface through `StatusMessage`), plus a hint about OBS Browser Sources on
the same machine and the `style` / `size` params.
