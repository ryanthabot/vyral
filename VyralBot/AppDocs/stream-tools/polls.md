# Polls

> Cross-platform chat polls with vote changing — fully built on the management side and live from chat since the command pipeline was wired up. Since 1.1.0's wave 16, polls can be created **straight from chat** (`!poll start` with pipe-separated options and an optional duration), voting has a short `!vote` alias, timed polls auto-end with a cross-platform announcement, and live results render on a browser-source overlay.

## Overview

The Polls tool lets a streamer create a multiple-choice poll, start it, let viewers vote by typing `!poll vote <number>` (or just `!vote <number>`) in chat, and then end it to see per-option counts and a winner. Polls can be built in the UI **or** started ad-hoc from chat with the `!poll start` pipe syntax. Votes can come from any connected platform, and an optional "allow multiple votes" setting lets viewers change their mind instead of being locked to their first choice.

Live results are shown in chat replies, on the Polls page, **and on the [poll overlay](browser-sources.md#poll-results-polloverlayhtml)** browser source (live bars, countdown for timed polls, winner bars lingering 5 minutes after the end).

Just like [Giveaways](giveaways.md), the 2026-08-15 fix pass made chat voting live: `!poll` commands reach the engine from real chat on every connected platform. The eligibility caveats:

1. **Followers-only is platform-asymmetric.** On **Twitch** the follower check is a real Helix lookup (needs the stored token's `moderator:read:followers` scope — degrades to `false` without it, KI-33). On **every other platform** the check falls back to the recorded viewer status: a Kick viewer marked followed by a `KickFollowEvent` webhook passes the gate, one never seen by the webhooks is rejected; YouTube has no follow concept, so eligibility there stays `false` (KI-31, partially resolved).
2. **Min-watchtime works everywhere.** Real watchtime is resolved through the [Watchtime](watchtime.md) service at vote time.

## User Guide

Open **Stream Tools → Polls** in the sidebar (or the Polls card on the Tools hub page). The page has a create form, the poll list, and a details/actions area for the selected poll.

### Creating a poll

Fill in the create form and press **Create Poll**:

| Field | What it does | Default |
|---|---|---|
| **Question** (required) | The poll question, e.g. "What game next?". | *(empty)* |
| **Options** (required, one per line) | The choices. At least 2 non-empty lines are required; blank lines are ignored. Options are numbered 1..N for voting. | `Option 1` / `Option 2` / `Option 3` |
| **Allow Multiple Votes** (toggle) | Really means *allow vote changing*: a viewer who votes again has their vote moved to the new option instead of being rejected. | Off |
| **Min Watchtime (minutes)** | Viewers need at least this much tracked watchtime to vote. Works — real watchtime is checked at vote time. | `0` |
| **Followers Only** (toggle) | Only followers may vote. Works on **Twitch** (real Helix check, needs the `moderator:read:followers` scope) and on **Kick** through the recorded viewer status (a follow webhook must have marked the viewer); always rejects on YouTube. | Off |
| **Subscribers Only** (toggle) | Only subscribers may vote. This restriction works. | Off |
| **Allowed Platforms** | Comma-separated platform list (`Twitch,Kick`) or `All`. Votes from other platforms are declined. | `All` |
| **Scheduled End** (date picker) | Optional end time. When set (also settable from chat via `!poll start 5m …`), the giveaway/poll scheduler auto-ends the poll at that time and announces the results on every connected platform. | *(none)* |

The creator is recorded as the hard-coded name "Admin" — the form does not ask who you are. After creation the poll appears in the list with status **Pending**.

### Running a poll from start to finish

1. **Create** it (above); it starts as Pending.
2. Select it and press **Start Poll**. Status becomes **Active**. Only one poll can be Active at a time.
3. Viewers vote with `!poll vote <number>` (1-based). Each (username, platform) gets one vote — and a viewer with [linked accounts](../chat-features/account-linking.md) gets **one vote across all of them** (see below); with Allow Multiple Votes on, a second vote *moves* their vote rather than being rejected.
4. Press **Refresh** to reload the list, the selected poll's votes, and the results summary (per-option counts, or "No votes yet").
5. Press **End Poll** to close it. The results are aggregated per option, stored on the poll as JSON, the winning option is announced, and the status becomes **Completed**.
6. **Cancel Poll** aborts an Active or Pending poll (status **Cancelled**). **Delete Poll** removes a poll and its votes from the database entirely — the only way to get rid of old polls.

### Chat commands

Implemented by the poll engine; available to chat (see [Commands](../chat-features/commands.md) and the [command catalog](../reference/command-catalog.md)):

| Command | Who | Effect |
|---|---|---|
| `!poll start` | Moderators | Starts the first Pending poll (created in the UI) and lists its numbered options. |
| `!poll start [5m] Question \| Option A \| Option B` | Moderators | Creates and starts an ad-hoc poll straight from chat (wave 16). Optional duration token first (`5m`, `90s`, `1h30m`; 5 s–24 h), then the question (≤ 200 chars) and 2–6 pipe-separated options (≤ 100 chars each). With a duration the scheduler auto-ends the poll and announces results everywhere. |
| `!poll vote <n>` / `!vote <n>` | Everyone | Votes for option *n* (1-based) — `!vote` is the short alias sharing the exact vote path. Moves an existing vote when vote-changing is allowed. |
| `!poll results` | Everyone | Live per-option counts and percentages; falls back to the most recent Completed poll when none is active. |
| `!poll end` | Moderators | Ends the poll, announces the winning option. |
| `!poll cancel` | Moderators | Cancels the active poll. |
| `!poll status` | Everyone | Shows the active question, options, and total votes. |

```
!poll start 5m What game next? | Balatro | Elden Ring | Minecraft
📊 Poll started: What game next? - Options: (1) Balatro (2) Elden Ring (3) Minecraft - ends in 5m - Vote with !poll vote <n> or !vote <n>
```

A duration-less ad-hoc poll runs until a mod ends it (`!poll end`), exactly like a UI-created poll. Malformed starts get a usage reply with the limits spelled out (question/option caps, 2–6 options, duration range).

Unauthorized use of a moderator command gets a visible "Only moderators can..." reply. Bot responses are only ever sent back to Twitch or Kick; a vote accepted from another platform is recorded, but its confirmation reply is dropped.

### The poll overlay

Live results render on the **`polloverlay.html`** browser source (wave 16) — one bar per option with votes and percent, a live countdown for timed polls, and the final winner bars lingering 5 minutes after the end. Setup, URL params (`title`, `style`) and the endpoint contract live on the [Browser Sources](browser-sources.md#poll-results-polloverlayhtml) page.

## How It Works (Developer)

Two Core services mirror the giveaway design: `PollService` is the engine, `PollCommandService` is the chat-command adapter. Both are singletons (`App.xaml.cs:133-134`).

**Lifecycle.** A `Poll` moves `Pending → Active → Completed|Cancelled`, with the single-active invariant enforced in `StartPollAsync` (`PollService.cs:81-86`). The poll's options are stored as a JSON string array on the `Poll.Options` column; `CreatePollAsync` throws if fewer than 2 options are supplied (`PollService.cs:37-40`).

**Voting.** `VoteAsync` (`PollService.cs:99-198`) validates, in order (eligibility re-checked *inside* the end gate — see below): poll exists and is Active; the option index is in range; platform allow-list; follower requirement; subscriber requirement; minimum watchtime; and an existing vote for the (username, platform) pair. The duplicate branch is where vote changing happens — when `AllowMultipleVotes` is set, the existing row's `OptionIndex` is updated in place; otherwise the vote is rejected with the previous choice quoted back. New votes insert a `PollVote` row and refresh the denormalized `TotalVotes` counter.

**Linked viewers get one vote.** When no direct (username, platform) vote exists, `FindLinkedVoteAsync` (`PollService.cs:237-277`) resolves the voter through [account linking](../chat-features/account-linking.md) and looks for a vote from **any other account linked to the same viewer** — vote from Twitch, and your linked Discord account's `!poll vote` is treated as *your* existing vote (change it when vote-changing is on, "already voted" when off). The lookup is best-effort: a linking failure is logged and the poll falls back to direct-pair dedup only, never rejecting the vote outright.

**Race safety.** A single `SemaphoreSlim(1,1)` **end gate** serializes votes against `EndPollAsync`: a vote that waited on the gate re-validates the poll is still Active inside it, and the gate is held across the results snapshot *and* the completion save — so a vote racing the end either lands in the counted results or is rejected ("This poll is not currently active."), never stored-but-uncounted. A double end is likewise rejected inside the gate rather than double-processed.

**Results and the end announcement.** `EndPollAsync` (`PollService.cs:203-240`) reads the per-option counts via `GetPollVoteResultsAsync`, writes them into `Poll.VoteResults` as JSON, marks the poll Completed, and formats the announcement (`FormatResultsMessage`): a **zero-vote poll** completes with an explicit *"📊 Poll Results — {question}: no votes were cast."* (no winner is claimed); a **tie** is announced as *"it's a tie between X and Y ({n} votes each)!"* listing every tied option; otherwise the plain *"{winner} wins with {n} votes!"* line. `GetCurrentResultsAsync` (`PollService.cs:245`) computes the same aggregation on demand for the `!poll results` command without ending the poll.

**Command routing (live).** `ChatViewModel.OnChatMessageReceivedAsync` gates on messages starting with `!poll` (or `!vote`, the wave-16 alias) and calls `PollCommandService.ProcessCommandAsync`, which dispatches the six subcommands plus the alias. The orchestrator consumes the platform-agnostic [ChatMessageReceivedEvent](../../src/VyralBot.Core/Models/ChatMessageReceivedEvent.cs) published by all three chat integrations (dedupe + bot-echo skip included), so votes arrive from Twitch, Kick, and YouTube alike. See [Event Bus](../architecture/event-bus.md).

```csharp
// ChatViewModel.cs — the poll call with real eligibility data
var response = await _pollCommandService.ProcessCommandAsync(
    evt.Message,
    evt.Username,
    evt.Platform,
    evt.IsModerator,
    evt.IsSubscriber,
    isFollower,          // Twitch: real Helix channels/followers check (5-min cache);
                         // other platforms: the recorded viewer status (Kick follow
                         // webhooks mark followers; YouTube stays false)
    watchtimeMinutes);   // real total minutes from WatchtimeService
```

`IsSubscriber` is passed through, so Subscribers-only works. The follower-check scope requirement (Twitch) and the Kick/YouTube gaps are tracked as [KI-31](../reference/known-issues.md) and [KI-33](../reference/known-issues.md).

## Files & Code

| File | Role |
|---|---|
| [PollView.xaml](../../src/VyralBot.UI/Views/PollView.xaml) | The Polls page — create form, poll list, votes grid, action buttons. |
| [PollViewModel.cs](../../src/VyralBot.UI/ViewModels/PollViewModel.cs) | Page logic: form parsing (options one per line), start/end/cancel/delete/refresh, results display. |
| [PollService.cs](../../src/VyralBot.Core/Services/PollService.cs) | The engine: lifecycle, vote validation, vote changing, result aggregation. |
| [PollCommandService.cs](../../src/VyralBot.Core/Services/PollCommandService.cs) | Chat-command adapter: the six `!poll` subcommands, the `!vote` alias, and ad-hoc `!poll start [duration] … \| …` creation (wave 16). |
| [GiveawayPollSchedulerService.cs](../../src/VyralBot.Core/Services/GiveawayPollSchedulerService.cs) | Background auto-end for scheduled polls (and giveaways) — announces results via `PollAutoEndedEvent` on every connected platform. |
| [PollController.cs](../../src/VyralBot.Api/Controllers/PollController.cs) | `GET /poll/data` — the read-only snapshot endpoint behind the poll overlay (wave 16). |
| [Poll.cs](../../src/VyralBot.Data/Entities/Poll.cs) | `Poll` entity and the `PollStatus` enum. |
| [PollVote.cs](../../src/VyralBot.Data/Entities/PollVote.cs) | `PollVote` entity (one row per voter). |
| [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs) | SQLite persistence; `Polls` table at `DapperDataService.cs:2823`, `PollVotes` at `:2849`. |
| [IDataService.cs](../../src/VyralBot.Data/IDataService.cs) | Repository interface: `SavePollAsync`, `GetActivePollAsync`, `GetPollVoteResultsAsync`, `DeletePollAsync`, etc. |
| [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) | Command routing into the poll engine (live) plus the eligibility lookups. |
| [MainViewModel.cs](../../src/VyralBot.UI/ViewModels/MainViewModel.cs) | Navigation: `NavigateToPollsAsync` at `MainViewModel.cs:1160`. |

The vote-changing branch — the only difference between a rejected duplicate vote and a moved one:

```csharp
// PollService.cs:156-173 — existing vote handling in VoteAsync
var existingVote = await _dataService.GetPollVoteAsync(poll.Id, username, platform);
if (existingVote != null)
{
    if (!poll.AllowMultipleVotes)
    {
        return (false, $"You have already voted for option {existingVote.OptionIndex + 1}!");
    }

    // Update existing vote
    existingVote.OptionIndex = optionIndex;
    existingVote.VotedAt = DateTime.UtcNow;
    await _dataService.SavePollVoteAsync(existingVote);

    return (true, $"Vote changed to option {optionIndex + 1}: {options[optionIndex]}");
}
```

And the result aggregation used by both `!poll results` and **End Poll**:

```csharp
// PollService.cs:216-224 — per-option counts from the vote table
var options = JsonSerializer.Deserialize<string[]>(poll.Options) ?? Array.Empty<string>();
var voteResults = await _dataService.GetPollVoteResultsAsync(poll.Id);

var results = new Dictionary<string, int>();
for (int i = 0; i < options.Length; i++)
{
    results[options[i]] = voteResults.ContainsKey(i) ? voteResults[i] : 0;
}
```

## Data & Settings

Everything lives in the per-profile SQLite database (see [Data Layer](../architecture/data-layer.md) and the [database reference](../reference/database-reference.md)):

- **Polls** — one row per poll: `Question`, `Options` (JSON string array), `Status` (0=Pending, 1=Active, 2=Completed, 3=Cancelled), `CreatedAt/StartedAt/EndedAt`, `ScheduledEndTime` (stored, never acted on), `CreatorUsername` (hard-coded "Admin" from the UI), `TotalVotes`, `AllowMultipleVotes`, `AllowedPlatforms`, `FollowersOnly`, `SubscribersOnly`, `MinWatchtimeMinutes`, `VoteResults` (JSON of option-index → count, written at end).
- **PollVotes** — one row per voter: `PollId`, `Username`, `Platform`, `PlatformUserId` (always null from the chat path), `OptionIndex` (0-based), `IsSubscriber`, `IsFollower`, `WatchtimeMinutes` (snapshot at vote time), `VotedAt`.

There are no poll-specific config keys; all settings are per-poll columns.

## Status & Known Issues

- **The chat command pipeline is live** — `!poll ...` commands reach the engine from real chat on all connected platforms and votes are recorded (KI-1 resolved 2026-08-15). Tracked in the [known issues ledger](../reference/known-issues.md).
- **Followers-only is platform-asymmetric** — real Helix follower check on Twitch (requires the stored token's `moderator:read:followers` scope — KI-33); on non-Twitch platforms the gate consults the recorded viewer status (Kick followers honored once a webhook recorded them; YouTube stays `false`) — KI-31, partially resolved. Tracked in the [known issues ledger](../reference/known-issues.md).
- **Linked viewers get one vote** — a vote from any linked (username, platform) pair counts as that viewer's vote (dedup best-effort through account linking; a linking failure falls back to direct-pair dedup).
- **Ties and zero-vote polls are announced honestly** — a tie lists every tied option with the vote count; a poll with no votes completes with "no votes were cast" instead of naming a winner.
- **Votes cannot race the end** — the end gate serializes votes against End Poll; a vote either lands in the results or is rejected, never stored-but-uncounted.
- **Min-watchtime works** — real watchtime from [Watchtime](watchtime.md) is checked at vote time on every platform.
- **Scheduled end is honored by the scheduler** — `GiveawayPollSchedulerService` ticks every 30 s and auto-ends any Active poll whose `ScheduledEndTime` has passed, announcing results on every connected platform via `PollAutoEndedEvent` (the UI date picker and the `!poll start <duration>` token both set it; a poll with no scheduled end runs until a mod ends it).
- **Creator is hard-coded "Admin" from the UI** — the create form never asks who you are (`PollViewModel.cs:210`); ad-hoc chat polls record the issuing moderator's username as creator.
- **Replies only go to Twitch/Kick** — votes from other platforms are recorded, but the bot's response is only sent via the Twitch/Kick send paths.
- **Poll overlay ships** — live results render on the [`polloverlay.html` browser source](browser-sources.md#poll-results-polloverlayhtml) (wave 16): 3 s polling, countdown for timed polls, winner bars lingering 5 minutes.
