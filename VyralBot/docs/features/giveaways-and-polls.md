# Giveaways & Polls

Cross-platform giveaway (weighted raffle) and poll (chat voting) subsystem.
Wave 7 (2026-11-07) hardened draw/vote correctness for live-stream use.

## Components

| Piece | Where |
|---|---|
| `GiveawayService` (lifecycle, entries, weighted draw, redraw, adaptive Live/Timed intake) | `VyralBot.Core/Services/GiveawayService.cs` |
| `GiveawayCommandService` (`!giveaway …` + custom entry keyword) | `VyralBot.Core/Services/GiveawayCommandService.cs` |
| `PollService` (lifecycle, one-vote-per-viewer, results) | `VyralBot.Core/Services/PollService.cs` |
| `PollCommandService` (`!poll …`, ad-hoc start + `!vote` alias) | `VyralBot.Core/Services/PollCommandService.cs` |
| `GiveawayPollSchedulerService` (auto-end at ScheduledEndTime) | `VyralBot.Core/Services/GiveawayPollSchedulerService.cs` |
| `PollController` (loopback `GET /poll/data` for the overlay) | `VyralBot.Api/Controllers/PollController.cs` |
| `polloverlay.html` (OBS browser-source widget) | `VyralBot.Api/wwwroot/polloverlay.html` — see [poll-overlay.md](poll-overlay.md) |
| `GiveawayViewModel` + `GiveawayView` | `VyralBot.UI/ViewModels` / `Views` |
| `PollViewModel` + `PollView` | `VyralBot.UI/ViewModels` / `Views` |

All are DI singletons (`App.xaml.cs`); the scheduler is a hosted service.

## Chat commands

Routed by `ChatViewModel`'s chat-message handler for **Twitch, Kick, YouTube,
Discord** (reply is sent on the platform the message came from).

- `!giveaway start|enter|winner|draw|redraw|cancel|status` — `start`,
  `winner/draw`, `redraw/reroll`, `cancel` are moderator-gated; `enter` and
  `status` are open. The giveaway's custom entry keyword (e.g. `!enter`) also
  enters while a giveaway is active.
- `!poll start|vote <n>|results|end|cancel|status` — `start`, `end`, `cancel`
  are moderator-gated; voting is open.
- **Wave 16 chat-driven polls**: `!poll start [duration] Question? | Option A |
  Option B [| ...]` creates and starts an ad-hoc poll straight from chat
  (2-6 pipe-separated options; question ≤200 chars, options ≤100 chars each).
  The optional duration token (`90s`, `5m`, `1h30m`, 5s..24h) sets
  `ScheduledEndTime`; the existing `GiveawayPollSchedulerService` auto-ends the
  poll at that time and announces on every connected platform via
  `PollAutoEndedEvent` — no new timer. Bare `!poll start` still starts the
  next Pending UI-configured poll. `!poll end` returns the results string and
  the chat router sends it on the originating platform (the UI end path
  additionally announces everywhere via `PollViewModel.AnnounceToChatAsync`).
- `!vote <n>` (wave 16) — everyone's short alias for `!poll vote <n>`; strict
  first-token match (case-insensitive) so `!voters`-style commands are not
  swallowed. Shares the exact vote pipeline (eligibility, linked-identity
  dedup, vote-change rules) with the long form.
- **TikTok**: not a chat source — no TikTok chat pipeline exists (web setup
  only, `TiktokChatSettingsViewModel.ChatPipelineAvailable == false`), so no
  entries, votes or commands can arrive from TikTok.
- Discord mod-tier actions (`ModCommandEngine`) can also start/cancel
  giveaways and start/end polls.

## Giveaway semantics

- **Entry dedup**: one entry row per (username, platform). The same person
  entering from Twitch AND Kick gets two rows — this is intentional
  (cross-platform entries are additive), and the odds boost additionally
  rewards *linked* platforms.
- **Weighting**: each row gets `EntryCount` tickets (sub bonus or odds boost:
  sub tier ladder + follower + linked-platform bonuses, see `GiveawayOdds`).
- **Draw atomicity**: `DrawWinnersAsync` holds a per-service gate
  (`_drawGate`) across the timed-queue flush, the entry snapshot and the
  completion save; every entry persist re-validates status inside the same
  gate. Entries racing the draw either land in the snapshot or are rejected
  with "not active" — never confirmed-then-lost. Concurrent draws (chat +
  UI + scheduler) are serialized; the losers see "not active".
- **Zero entries**: clean `(false, "No entries to draw from!")`, the giveaway
  stays Active (draw later, or re-run the command).
- **Redraw**: `RedrawWinnersAsync` (UI "🎲 Re-draw" on a Completed giveaway,
  or `!giveaway redraw`) un-flags previous winners and picks fresh winners
  from non-winner entries only.
- **Winner announcement** includes the prize: "🎉 Winner: name (Platform) —
  prize: <giveaway name>!". UI draws/redraws announce on all connected
  send-capable platforms (Twitch/Kick/YouTube/Discord, per-platform failure
  isolation); the scheduler announces via `GiveawayAutoEndedEvent`.
- **Eligibility** (follower/sub-only, watchtime, allowed platforms) is
  enforced in `GiveawayService.EnterGiveawayAsync` from real chat flags, not
  just UI checkboxes. Follower status: Twitch uses the Twitch API check;
  other platforms fall back to recorded viewer status (`IViewerStatusService`
  follow events) — `ChatViewModel.IsFollowerForEligibilityAsync`.
- **Scheduled end**: the UI DatePicker value is normalized to UTC on save;
  the scheduler compares against `DateTime.UtcNow` and auto-draws.

## Poll semantics

- **Vote identity**: one vote per (username, platform) row, AND one vote per
  *linked viewer*: `PollService` resolves the unified ViewerId via
  `ILinkedAccountService` and treats a vote from any linked account (e.g.
  Discord row when voting from Twitch) as this viewer's existing vote.
  Unlinked same-person-different-usernames remain two voters (accepted).
- **Vote changes**: `AllowMultipleVotes` (UI: "Allow Vote Changes") permits
  *changing* the recorded vote; otherwise re-voting is rejected with the
  current choice echoed.
- **Vote/end race**: `VoteAsync` and `EndPollAsync` share `_endGate`; a vote
  that loses the race to the end is rejected ("not currently active") instead
  of being stored uncounted.
- **Results**: ties are announced as ties ("it's a tie between A and B (N
  votes each)"); zero-vote polls end with "no votes were cast" (no fake
  winner). UI end + scheduler end both announce per-platform.

## UI notes (wave 7)

- Giveaways page: always-visible LIVE GIVEAWAY banner (name, keyword, ticking
  scheduled-end countdown), entry list + giveaway list empty states, Re-draw
  button on completed giveaways, debounced live reloads via EventBus events.
- Polls page: "✏️ Edit" on Pending polls reopens the create dialog in
  "Save Changes" mode (question/options/settings editable before start;
  Active/Completed polls are locked because votes reference option indexes),
  polls/votes empty states.
