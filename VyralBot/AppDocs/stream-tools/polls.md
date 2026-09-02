# Polls

> Cross-platform chat polls with vote changing — fully built on the management side and live from chat since the command pipeline was wired up.

## Overview

The Polls tool lets a streamer create a multiple-choice poll, start it, let viewers vote by typing `!poll vote <number>` in chat, and then end it to see per-option counts and a winner. Votes can come from any connected platform, and an optional "allow multiple votes" setting lets viewers change their mind instead of being locked to their first choice.

Just like [Giveaways](giveaways.md), the 2026-08-15 fix pass made chat voting live: `!poll` commands reach the engine from real chat on every connected platform. Two eligibility caveats remain:

1. **Followers-only works on Twitch only.** The follower check is a real Helix lookup for Twitch (needs the stored token's `moderator:read:followers` scope — degrades to `false` without it). KickLib 1.8.0 has no followers API and YouTube has no follower concept, so Followers-only still rejects every vote on those platforms (KI-31).
2. **Min-watchtime works everywhere.** Real watchtime is resolved through the [Watchtime](watchtime.md) service at vote time.

There is also no overlay for live poll results — results exist only in chat replies and on the Polls page.

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
| **Followers Only** (toggle) | Only followers may vote. Works on **Twitch** (real Helix check, needs the `moderator:read:followers` scope); always rejects on Kick/YouTube. | Off |
| **Subscribers Only** (toggle) | Only subscribers may vote. This restriction works. | Off |
| **Allowed Platforms** | Comma-separated platform list (`Twitch,Kick`) or `All`. Votes from other platforms are declined. | `All` |
| **Scheduled End** (date picker) | Optional end time. Stored and displayed, but nothing automatically ends the poll at that time. | *(none)* |

The creator is recorded as the hard-coded name "Admin" — the form does not ask who you are. After creation the poll appears in the list with status **Pending**.

### Running a poll from start to finish

1. **Create** it (above); it starts as Pending.
2. Select it and press **Start Poll**. Status becomes **Active**. Only one poll can be Active at a time.
3. Viewers vote with `!poll vote <number>` (1-based). Each (username, platform) gets one vote; with Allow Multiple Votes on, a second vote *moves* their vote rather than being rejected.
4. Press **Refresh** to reload the list, the selected poll's votes, and the results summary (per-option counts, or "No votes yet").
5. Press **End Poll** to close it. The results are aggregated per option, stored on the poll as JSON, the winning option is announced, and the status becomes **Completed**.
6. **Cancel Poll** aborts an Active or Pending poll (status **Cancelled**). **Delete Poll** removes a poll and its votes from the database entirely — the only way to get rid of old polls.

### Chat commands

Implemented by the poll engine; available to chat (see [Commands](../chat-features/commands.md) and the [command catalog](../reference/command-catalog.md)):

| Command | Who | Effect |
|---|---|---|
| `!poll start` | Moderators | Starts the first Pending poll and lists its numbered options. |
| `!poll vote <n>` | Everyone | Votes for option *n* (1-based). Moves an existing vote when vote-changing is allowed. |
| `!poll results` | Everyone | Live per-option counts and percentages; falls back to the most recent Completed poll when none is active. |
| `!poll end` | Moderators | Ends the poll, announces the winning option. |
| `!poll cancel` | Moderators | Cancels the active poll. |
| `!poll status` | Everyone | Shows the active question, options, and total votes. |

Unauthorized use of a moderator command gets a visible "Only moderators can..." reply. Bot responses are only ever sent back to Twitch or Kick; a vote accepted from another platform is recorded, but its confirmation reply is dropped.

## How It Works (Developer)

Two Core services mirror the giveaway design: `PollService` is the engine, `PollCommandService` is the chat-command adapter. Both are singletons (`App.xaml.cs:133-134`).

**Lifecycle.** A `Poll` moves `Pending → Active → Completed|Cancelled`, with the single-active invariant enforced in `StartPollAsync` (`PollService.cs:81-86`). The poll's options are stored as a JSON string array on the `Poll.Options` column; `CreatePollAsync` throws if fewer than 2 options are supplied (`PollService.cs:37-40`).

**Voting.** `VoteAsync` (`PollService.cs:99-198`) validates, in order: poll exists and is Active; the option index is in range; platform allow-list; follower requirement; subscriber requirement; minimum watchtime; and an existing vote for the (username, platform) pair. The duplicate branch is where vote changing happens — when `AllowMultipleVotes` is set, the existing row's `OptionIndex` is updated in place; otherwise the vote is rejected with the previous choice quoted back. New votes insert a `PollVote` row and refresh the denormalized `TotalVotes` counter.

**Results.** `EndPollAsync` (`PollService.cs:203-240`) reads the per-option counts via `GetPollVoteResultsAsync`, writes them into `Poll.VoteResults` as JSON, marks the poll Completed, and formats a winner message from the highest count. `GetCurrentResultsAsync` (`PollService.cs:245`) computes the same aggregation on demand for the `!poll results` command without ending the poll.

**Command routing (live).** `ChatViewModel.OnChatMessageReceivedAsync` gates on messages starting with `!poll` and calls `PollCommandService.ProcessCommandAsync`, which dispatches the six subcommands. The orchestrator consumes the platform-agnostic [ChatMessageReceivedEvent](../../src/VyralBot.Core/Models/ChatMessageReceivedEvent.cs) published by all three chat integrations (dedupe + bot-echo skip included), so votes arrive from Twitch, Kick, and YouTube alike. See [Event Bus](../architecture/event-bus.md).

```csharp
// ChatViewModel.cs — the poll call with real eligibility data
var response = await _pollCommandService.ProcessCommandAsync(
    evt.Message,
    evt.Username,
    evt.Platform,
    evt.IsModerator,
    evt.IsSubscriber,
    isFollower,          // Twitch: real Helix channels/followers check (5-min cache);
                         // Kick/YouTube: false (no followers API / no follower concept)
    watchtimeMinutes);   // real total minutes from WatchtimeService
```

`IsSubscriber` is passed through, so Subscribers-only works. The follower-check scope requirement (Twitch) and the Kick/YouTube gaps are tracked as [KI-31](../reference/known-issues.md) and [KI-33](../reference/known-issues.md).

## Files & Code

| File | Role |
|---|---|
| [PollView.xaml](../../src/VyralBot.UI/Views/PollView.xaml) | The Polls page — create form, poll list, votes grid, action buttons. |
| [PollViewModel.cs](../../src/VyralBot.UI/ViewModels/PollViewModel.cs) | Page logic: form parsing (options one per line), start/end/cancel/delete/refresh, results display. |
| [PollService.cs](../../src/VyralBot.Core/Services/PollService.cs) | The engine: lifecycle, vote validation, vote changing, result aggregation. |
| [PollCommandService.cs](../../src/VyralBot.Core/Services/PollCommandService.cs) | Chat-command adapter: the six `!poll` subcommands. |
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
- **Followers-only is Twitch-only** — real Helix follower check on Twitch (requires the stored token's `moderator:read:followers` scope — KI-33); hard `false` on Kick/YouTube (KI-31). Tracked in the [known issues ledger](../reference/known-issues.md).
- **Min-watchtime works** — real watchtime from [Watchtime](watchtime.md) is checked at vote time on every platform.
- **Scheduled end time is inert** — `ScheduledEndTime` is stored and displayed but no background job ends the poll when it passes.
- **Creator is hard-coded "Admin"** — the UI never asks who creates a poll (`PollViewModel.cs:210`).
- **Replies only go to Twitch/Kick** — votes from other platforms are recorded, but the bot's response is only sent via the Twitch/Kick send paths.
- **No overlay output** — there is no browser-source page for live poll results; see [Browser Sources](browser-sources.md). Results appear only in chat replies and on the Polls page.
