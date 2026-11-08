# Poll Overlay

## Overview
Poll OBS browser source: a dependency-free widget page (`polloverlay.html`)
served by the embedded loopback web server. While a poll is active (started
from the Polls page, `!poll start`, or `!poll start 5m Question | A | B`) the
widget shows live vote bars per option (count + percent, CSS width
transitions); when the poll ends the frozen results linger for five minutes
with the winner highlighted (gold ring + WINNER chip; ties show "no single
winner"). The widget polls `GET /poll/data` every 3 seconds and hides itself
completely when there is nothing to show. Vote counts come from the same
per-profile `PollVotes` rows the `!vote` / `!poll vote` commands write, so
the overlay is always consistent with chat.

## Source Files
| File | Description |
|------|-------------|
| `src/VyralBot.Api/Controllers/PollController.cs` | Loopback GET `/poll/data` JSON endpoint (active + lingering last results) |
| `src/VyralBot.Api/wwwroot/polloverlay.html` | OBS browser-source widget (no external dependencies) |
| `src/VyralBot.Api/Services/WebServerService.cs` | Forwards `PollService` from the WPF host into the embedded Kestrel (`FromParent`) |
| `src/VyralBot.Core/Services/PollService.cs` | Read path (`GetActivePollAsync`, `GetAllPollsAsync`, `GetPollVotesAsync`) |

## Endpoint
`GET /poll/data` (loopback only — the embedded server binds `ListenLocalhost`;
same trust model as `/leaderboard` and the `/songs` GET routes: read-only,
not part of the token-gated control API). `Cache-Control: no-store`.

```json
{
  "active": {
    "question": "Best game?",
    "options": [
      { "index": 1, "text": "Zelda", "votes": 4, "percent": 66.7 },
      { "index": 2, "text": "Elden Ring", "votes": 2, "percent": 33.3 }
    ],
    "totalVotes": 6,
    "endsAt": "2026-11-07T18:05:00.0000000Z",
    "endedAt": null,
    "winner": null,
    "isTie": false
  },
  "lastResults": null
}
```

- `active` — the running poll (`endsAt` present only when created with a
  duration token / scheduled end); `winner`/`isTie` are null/false while live.
- `lastResults` — the most recently **Completed** poll, included for 5
  minutes after `EndedAt`, with `winner` (null on ties and zero-vote polls,
  mirroring `PollService.FormatResultsMessage`) and `isTie`.
- Options are index-keyed (duplicate option labels never merge), percents are
  rounded to 1 decimal, and poll timestamps are normalized to UTC ISO ("o")
  regardless of the Kind the SQLite read path returns.
- When `PollService` is not registered (self-host scenario) the endpoint
  returns `503 {"error":"poll_disabled"}` — same degradation convention as
  the other overlay controllers.

## Widget URL params
`http://localhost:<port>/polloverlay.html?title=&style=dark`

| Param | Default | Meaning |
|-------|---------|---------|
| `title` | (poll question) | Optional heading override (truncated to 60 chars) |
| `style` | `dark` | `dark` (translucent night card) or `light` (translucent white card) — same palette as `leaderboard.html` |

## Widget behavior
- Polls every 3s with `cache: 'no-store'`; requests never overlap and the
  loop reconnects forever (leaderboard.html conventions).
- Status badge uses audio.html conventions: Connecting/Connected/Disconnected
  classes, the Connected badge auto-hides after 5s.
- Live bars animate width (`transition: width .6s ease`); rows are keyed by
  question+index so the same poll updates in place while a new poll rebuilds
  with a slide-in animation.
- `endsAt` drives a local per-second countdown ("2:59 left") between fetches;
  the meta line always shows total votes and the `!vote <n>` hint.
- After the end the winner row gets the gold treatment and the header shows
  "final results" (or "no single winner" for ties / zero-vote polls); the
  card disappears when the 5-minute linger window passes.

## Tests
`tests/VyralBot.UI.Tests/Wave16PollTests.cs` covers the ad-hoc `!poll start`
parsing/validation, the duration token (`ScheduledEndTime` set, out-of-range
rejected), the `!vote` alias (success, dedup, non-swallowing of unrelated
commands), the bare-start legacy path, the `!poll end` results string, and
the controller snapshot (ordered options/percent/total, UTC `endsAt`
round-trip, 5-minute linger, tie handling, 503 without the service).
