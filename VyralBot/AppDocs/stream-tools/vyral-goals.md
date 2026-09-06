# Vyral Goals

> Broadcaster-defined goals that reward viewers for linking platforms, following, and subscribing — with seeded defaults, chat announcements on completion, and a multi-platform earnings bonus that grows with every linked platform.

## Overview

Vyral Goals ties the [account linking](../chat-features/account-linking.md) system to the [currency](currency.md) economy. A goal declares a condition ("link 2 platforms", "follow on 3 platforms", "reach sub tier 2") and a reward (points, an earnings multiplier, or a badge). Vyral evaluates each linked viewer against the enabled goals as their follow/sub/link state changes, grants the reward exactly once, and announces the completion in chat on every connected platform.

Four goals are **seeded on first run**:

| Goal | Requirement | Reward |
|---|---|---|
| **Bridge Builder** | Link 2 platforms | 500 points |
| **Everywhere At Once** | Link 4 platforms | A badge |
| **Loyal Follower** | Follow the stream on 3 platforms | 250 points |
| **Tier 2 Supporter** | Reach sub tier 2 on any linked platform | 1,000 points |

The same subsystem also powers the **multi-platform bonus** — an earnings multiplier that scales with the viewer's linked-platform count — and the `!mystatus` command that shows a viewer where they stand. Goals and the bonus are managed by the streamer under **Settings → Viewer Hub**.

## User Guide

### Viewer commands

| Command | Who | Effect |
|---|---|---|
| `!goals` | Everyone | Checklists every enabled goal with ✅/⬜ state for the caller, then a progress line: `📊 You: 2 linked | 1 follows | 1 subs | best tier 1`. |
| `!mystatus` | Everyone | `📊 @viewer: linked Kick, Twitch (2) | 💰 Multiplier: 1.25x | 🏆 Goals completed: 1/4`. Unlinked viewers are told to start with `!link`. |
| `!mp on` / `!mp off` | Everyone | Opts the viewer in or out of the multi-platform bonus (meaningful only in OptIn mode — see below). |
| `!link` / `!linked` / `!delink` | Everyone | The linking commands that drive all of this — see [Account Linking](../chat-features/account-linking.md). |

### The multi-platform bonus

The bonus multiplies a viewer's currency earnings (on top of the platform × boost × tier × role [multiplier stack](currency.md#multipliers--the-stack-that-now-applies)) based on how many platforms their identity links:

| Linked platforms | Multiplier (defaults) |
|---|---|
| 1 (no linking) | 1.0x — no bonus |
| 2 | **1.25x** |
| 3 | **1.5x** |
| 4 | **2.0x** |
| 5 or more | **2.5x** |

Three modes, stored in the `MultiPlatform.Mode` config and edited under **Settings → Viewer Hub → Multi-Platform Bonus**:

- **Off** (default) — the bonus never applies; `!mp` explains that it is disabled.
- **OptIn** — the bonus applies only to viewers who typed `!mp on`. Opting in requires at least 2 linked platforms; the opt-in state is stored per viewer.
- **Auto** — every linked viewer gets the bonus; `!mp` replies that no opt-in is needed.

Completed goals whose reward type is **Multiplier** stack on top of the ladder (highest wins per goal family) — giving viewers a second reason to chase goals.

### Managing goals

Goals are managed under **Settings → Viewer Hub → Vyral Goals**: each goal has a name, description, condition type, target count, reward type, and reward value. Reward types:

- **Currency** — the points amount is credited directly to the completing viewer (as a `Bonus` transaction with reason `Vyral goal: <name>`).
- **Multiplier** — a persistent earnings multiplier granted via the multi-platform bonus service.
- **Badge** — declarative only today: the completion is recorded and announced, but no badge is rendered anywhere yet.

Completions are **announced in chat on all connected platforms**: `🏆 {username} completed '{goal}'! 500 points` (or "a new badge" / "1.25x earnings multiplier"). A completion can only happen once per viewer per goal.

## How It Works (Developer)

### SocialGoalService

`VyralBot.Core/Services/Linking/SocialGoalService.cs`:

- **Seeding** — `SeedDefaultsAsync` writes the four default goals once, idempotently: if any `SocialGoal` row exists the table is never touched again (edits survive; deleted defaults do not come back).
- **Evaluation** — `EvaluateViewerAsync(viewerId, username, platform)` aggregates the viewer's state via `IViewerStatusService` (linked-platform count, distinct followed platforms, distinct subbed platforms, best sub tier across linked platforms) and checks each enabled goal with the static `GoalEvaluator.IsMet`. Supported goal types: `LinkedPlatforms`, `FollowPlatforms`, `SubAnyPlatform`, `SubTier` — each met when the aggregate reaches `TargetCount`.
- **Completions** are rows in `ViewerGoalCompletions`, unique per (GoalId, ViewerId) — the check-then-insert is idempotent. Currency rewards are credited through `ICurrencyService.AddCurrencyAsync`; then a `ViewerGoalCompletedEvent` goes onto the [event bus](../architecture/event-bus.md) (publish wrapped in try/catch).
- **Announcements** — `ChatViewModel` subscribes to `ViewerGoalCompletedEvent` and sends the 🏆 message to every connected platform.

### MultiPlatformBonusService

`VyralBot.Core/Services/Linking/MultiPlatformBonusService.cs` resolves the multiplier:

- Config keys `MultiPlatform.Mode` (`Off`/`OptIn`/`Auto`, default `Off`) and `MultiPlatform.Bonus2/3/4/5` (defaults 1.25/1.5/2.0/2.5), cached 30 seconds. The static `MultiPlatformBonusLadder.Resolve` maps the linked-platform count onto the ladder (below 2 → 1.0, 5+ → Bonus5).
- Opt-ins live in the viewer's `ViewerPreferences.MultiPlatformBonusOptIn` row; `SetOptInAsync` is a no-op for unlinked accounts (the `!mp` command checks for 2+ linked platforms first and explains instead of silently failing).
- Completed `Multiplier` goals are tracked per viewer from `ViewerGoalCompletedEvent` and multiply the ladder result. Limitation: that map is in-memory and not hydrated on startup, so multiplier-goal rewards apply from process start onward only.
- The final multiplier is cached per `username:platform` for 5 minutes; opt-in changes and goal completions clear the affected cache entries.
- `CurrencyService.ApplyEarnMultipliersAsync` multiplies the (already platform × boost × tier × role-stacked) amount by this bonus, floored — so the bonus feeds directly into every earning path that applies the stack.

## Files & Code

| File | Role |
|---|---|
| [SocialGoalService.cs](../../src/VyralBot.Core/Services/Linking/SocialGoalService.cs) | Goal seeding, evaluation (`GoalEvaluator`), completions, reward granting, `ViewerGoalCompletedEvent`. |
| [MultiPlatformBonusService.cs](../../src/VyralBot.Core/Services/Linking/MultiPlatformBonusService.cs) | Mode/opt-in/ladder resolution + goal multipliers; `MultiPlatformBonusLadder`. |
| [ViewerStatusService.cs](../../src/VyralBot.Core/Services/Linking/ViewerStatusService.cs) | Per-viewer aggregates: linked count, follower-anywhere, best sub tier. |
| [CurrencyCommandService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyCommandService.cs) | `!goals` / `!mystatus` / `!mp` handlers. |
| [CurrencyService.cs](../../src/VyralBot.Core/Services/Currency/CurrencyService.cs) | Applies the bonus inside `ApplyEarnMultipliersAsync`. |
| [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) | Goal-completion chat announcements. |
| [SocialGoal.cs](../../src/VyralBot.Data/Entities/SocialGoal.cs) / [ViewerGoalCompletion.cs](../../src/VyralBot.Data/Entities/ViewerGoalCompletion.cs) | Goal definitions and completions. |
| [ViewerPlatformStatus.cs](../../src/VyralBot.Data/Entities/ViewerPlatformStatus.cs) / [ViewerPreferences.cs](../../src/VyralBot.Data/Entities/ViewerPreferences.cs) | Per-platform follow/sub/tier state; per-viewer opt-in. |
| [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs) | `SocialGoals` (:3243), `ViewerGoalCompletions` (:3261), `ViewerPreferences` (:3233) tables. |

## Data & Settings

Config keys:

| Key | Default | Meaning |
|---|---|---|
| `MultiPlatform.Mode` | `Off` | `Off`, `OptIn`, or `Auto`. |
| `MultiPlatform.Bonus2` | `1.25` | Multiplier at 2 linked platforms. |
| `MultiPlatform.Bonus3` | `1.5` | Multiplier at 3 linked platforms. |
| `MultiPlatform.Bonus4` | `2.0` | Multiplier at 4 linked platforms. |
| `MultiPlatform.Bonus5` | `2.5` | Multiplier at 5+ linked platforms. |

Tables: **SocialGoals** (one row per goal), **ViewerGoalCompletions** (unique per GoalId+ViewerId), **ViewerPlatformStatus** (follow/sub/tier per username+platform), **ViewerPreferences** (per-viewer `MultiPlatformBonusOptIn`).

## Status & Known Issues

- **Seeding is once-only** — the defaults are written only while the `SocialGoals` table is empty; afterwards the broadcaster owns the list entirely (deleting a default does not resurrect it).
- **Multiplier-goal rewards are not hydrated on startup** — a viewer who completed a Multiplier goal before the last app restart only benefits from it again after a new goal completion event (in-memory map; see [MultiPlatformBonusService](../../src/VyralBot.Core/Services/Linking/MultiPlatformBonusService.cs)).
- **Badge rewards are declarative** — completions are recorded and announced, but no badge UI exists yet.
- **The bonus mode defaults to Off** — out of the box `!mystatus` will always show `1x` until the streamer picks OptIn or Auto under Settings → Viewer Hub.
- Goal evaluation runs when viewer-status/linking state changes; the `!goals` checklist is computed live from the same aggregates, so it can show goals as "met" a moment before the completion row (and announcement) lands.
