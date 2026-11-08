# Quotes

> The streamer.bot quote staple: a per-profile quote database viewers can browse from chat with `!quote` — random, by number, or search — and moderators can grow and prune with `!quote add` / `!quote del`. Managed visually on the **QUOTES** tab of the [Commands](commands.md) page, and fully multi-platform: the reply always goes to the platform the command was typed on.

## Overview

Quotes are the "things chat said that were too good to lose" database. A moderator (or you, from the dashboard) drops a line in with `!quote add`, and from then on any viewer can pull it back out — `!quote` for a random one, `!quote 42` for a specific one, `!quote search rage` to find every quote containing "rage". Every quote remembers who added it and from which platform.

The feature lives in two places:

- **Chat** — the `!quote` family, answered by `QuoteService` from the same chat pipeline as the currency/giveaway/poll/watchtime commands (see [How It Works](#how-it-works-developer)).
- **The Commands page → QUOTES tab** — a searchable grid over the same database: add quotes manually, browse, search, and delete with confirmation.

Quotes are stored in the per-profile SQLite `Quotes` table, so each [profile](../getting-started/profiles.md) has its own quote book and quote numbering starts at #1.

## User Guide

### The chat commands

The whole family (all replies go to the platform the command was typed on — Twitch, Kick, YouTube, or Discord):

| Command | Arguments | Permission | What it does |
|---|---|---|---|
| `!quote` | none | Everyone | A random quote: `Quote #17: "never gamble again" — added by Streamer`. Empty database: a hint telling mods how to add one. |
| `!quote <n>` | quote number | Everyone | Quote #n. Bad number (or ≤ 0) says so and reports how many quotes exist. |
| `!quote search <text>` (alias `find`) | substring | Everyone | `Found 2/40: #3 "…" \| #17 "…"` — up to **3** matches, each excerpt capped at 60 characters so the reply stays chat-friendly. |
| `!quote add <text>` | the quote text | **Moderator+** | Adds the quote and replies `Quote #41 added by <username>.` The quote records the adding user and the platform it was typed on. |
| `!quote del <n>` (aliases `remove`, `delete`) | quote number | **Moderator+** | Deletes quote #n (asks nothing further — gone from the database). |
| `!quote help` | none | Everyone | One-line usage summary of the whole family. |
| `!quotecount` | none | Everyone | `There are 40 quotes in the database.` |

Notes:

- **Permissions are real:** non-moderators who try `!quote add` / `!quote del` get "You must be a moderator to add/delete quotes" — not silence. The check uses the moderator badge from the chat message (broadcaster counts as moderator).
- **Numbering is stable, not gap-free:** numbers come from the database, and deleting a quote does not renumber the rest — `!quote 17` after deleting #3 still means the same quote it always did.
- **Search is substring, case-insensitive** (SQL `LIKE` semantics) and matches the quote **text only** — not the author name.
- Both `!quote` and `!quotecount` start with the same prefix, so either spelling reaches the quote service.
- Quoted text can be anything after the subcommand — no quotes/escaping needed: `!quote add he actually said that`.

### The QUOTES tab (Commands page)

Open **Chat Features → Commands → QUOTES** (the fifth tab). It has three parts:

- **Toolbar** — a count header ("40 quotes", or "3 quotes matching 'rage'" while searching), a **search box** that live-filters as you type (by text or author; **CLEAR** resets it), and **REFRESH** to reload the grid.
- **The grid** — one row per quote: **#** (number, accent-colored), **Quote** (wrapped text), **Author** (chat username, or "Manual"), **Platform** (where it was added, or "Manual"), **Date** (local date), and a **DELETE** action that asks for confirmation first ("Delete quote #17? This cannot be undone.").
- **Add box** — type the quote text and add it from the dashboard; it is recorded with Author/Platform "Manual" so you can tell dashboard additions from chat ones.

An empty database shows a friendly empty state pointing at `!quote add`.

Deleting from the tab and deleting via chat are the same operation — both remove the row permanently.

## How It Works (Developer)

[QuoteService.cs](../../src/VyralBot.Core/Services/Quotes/QuoteService.cs) implements [IQuoteService.cs](../../src/VyralBot.Core/Services/Quotes/IQuoteService.cs) and is deliberately shaped like its siblings `CurrencyCommandService` / `WatchtimeCommandService`: `ProcessCommandAsync(message, username, platform, isModerator)` returns the reply string (or null when the message is not a quote command, letting the chain continue).

Wiring is in the `ChatViewModel` chat orchestrator, after the watchtime fallback: any message starting with `!quote` (which also covers `!quotecount`) is passed to the service, and a non-null response is sent through the **originating** platform's chat service — Twitch, Kick, YouTube, or Discord (the same routing table the other fallback commands use; see [Commands → The chat entry point](commands.md#the-chat-entry-point)). That is the multi-platform guarantee: one database, replies on the platform the command came from, no per-platform setup.

Persistence is plain Dapper against the per-profile `Quotes` table (see [Database Reference](../reference/database-reference.md)): `Number` is a small SQLite autoincrement primary key — stable and viewer-friendly instead of raw GUIDs — with `Text`, `AddedBy`, `Platform`, and `CreatedAt` columns. Search caps nothing at the data layer; the 3-match/60-char reply shaping is the service's chat-friendliness policy (`SearchReplyLimit` / `ExcerptLength`).

The QUOTES tab is the management surface on the same service: `CommandsViewModel` exposes `Quotes`, `LoadQuotesAsync`, `AddQuoteAsync` (dashboard adds hard-code Author/Platform "Manual"), and `DeleteQuoteAsync` (confirmation-gated), driving the same `IQuoteService` methods the chat commands use.

## Files & Code

| File | Role |
|---|---|
| [QuoteService.cs](../../src/VyralBot.Core/Services/Quotes/QuoteService.cs) | The `!quote` family: parsing, moderator gates, random/by-number/search/add/del/count, chat-friendly reply shaping |
| [IQuoteService.cs](../../src/VyralBot.Core/Services/Quotes/IQuoteService.cs) | Service interface: `ProcessCommandAsync` (chat) + the CRUD surface the QUOTES tab uses |
| [Quote.cs](../../src/VyralBot.Data/Entities/Quote.cs) | Entity: `Number` (autoincrement PK), `Text`, `AddedBy`, `Platform`, `CreatedAt` |
| [DapperDataService.cs](../../src/VyralBot.Data/DapperDataService.cs) | `Quotes` table schema + the quote queries (random, by-number, search, count, add, delete) |
| [CommandsViewModel.cs](../../src/VyralBot.UI/ViewModels/CommandsViewModel.cs) | QUOTES tab ViewModel: grid load/search, manual add, confirm-guarded delete |
| [CommandsView.xaml](../../src/VyralBot.UI/Views/CommandsView.xaml) | The QUOTES tab: toolbar, grid with per-row DELETE, empty state, add box |
| [ChatViewModel.cs](../../src/VyralBot.UI/ViewModels/ChatViewModel.cs) | Pipeline wiring: routes `!quote*` messages to the service and sends the reply on the originating platform |

## Data & Settings

- **`Quotes` table** (per-profile SQLite) — one row per quote; see [Database Reference](../reference/database-reference.md).
- No config keys, no settings UI — the feature is always on for every profile that has a database.

## Status & Known Issues

- **Fully functional end to end** — chat commands on all four connected platforms, dashboard management, and persistence all verified against code (2026-11-07, marathon wave 6).
- Quote numbers are not renumbered after deletions — deliberate (stable numbers are what viewers quote back), so gaps appear after deletes.
- Search replies cap at 3 matches and 60-character excerpts per quote — a display cap only; the full rows are in the QUOTES tab.
