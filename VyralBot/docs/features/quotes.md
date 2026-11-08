# Quotes System

## Overview
Chat quote database (the streamer.bot staple): moderators add memorable chat
lines from any connected platform, viewers recall them with the `!quote`
command family. Quotes are per-profile (each profile's SQLite database has its
own `Quotes` table) and the command pipeline is platform-agnostic — the same
handler answers on Twitch, Kick, YouTube and Discord, and the reply is sent on
the ORIGINATING platform by the ChatViewModel routing block.

## Source Files
| File | Description |
|------|-------------|
| `src/VyralBot.Core/Services/Quotes/IQuoteService.cs` | Interface: `ProcessCommandAsync` + management surface |
| `src/VyralBot.Core/Services/Quotes/QuoteService.cs` | `!quote` command family + CRUD pass-through |
| `src/VyralBot.Data/Entities/Quote.cs` | Entity (Number, Text, AddedBy, Platform, CreatedAt) |
| `src/VyralBot.Data/DapperDataService.cs` | `Quotes` table DDL + CRUD |
| `src/VyralBot.Data/IDataService.cs` | Quote data methods |
| `src/VyralBot.UI/ViewModels/CommandsViewModel.cs` | QUOTES tab state (Quotes tab section) |
| `src/VyralBot.UI/Views/CommandsView.xaml` | QUOTES tab UI |

## Table shape
`Quotes(Number INTEGER PRIMARY KEY AUTOINCREMENT, Text TEXT NOT NULL,
AddedBy TEXT, Platform TEXT, CreatedAt TEXT)` — `Number` is a small stable
per-profile number so viewers can recall quotes with `!quote <n>`. Per-profile
scoping is the same as the currency tables: the row lives inside the profile's
own SQLite database (no ProfileId column, mirroring every other table).
Quotes are also whitelisted in the Settings → Data Management inventory.

## Commands (handled by `QuoteService.ProcessCommandAsync`)
| Command | Who | Behavior |
|---------|-----|----------|
| `!quote` | Everyone | Uniform random quote (`ORDER BY RANDOM() LIMIT 1`) |
| `!quote <n>` | Everyone | Quote by its number; miss → "not found (+ total)" |
| `!quote search <text>` | Everyone | Case-insensitive substring on Text/AddedBy, top 3 numbers |
| `!quote add <text>` | Moderator+ | Adds quote; stores username as AddedBy, platform as Platform |
| `!quote del\|remove\|delete <n>` | Moderator+ | Deletes by number |
| `!quotecount` | Everyone | "There are N quotes in the database." |
| `!quote help` | Everyone | Usage summary |

Permission gating uses the `isModerator` flag from
`ChatMessageReceivedEvent` exactly like `CurrencyCommandService` (`!give`,
`!take`).

## Pipeline wiring
`ChatViewModel`'s `OnChatMessageReceivedAsync` calls the unified
`CommandService` first; when no command row answers (no `!quote` row is
seeded), execution falls through to `_quoteService.ProcessCommandAsync(...)`
(the block sits right after the `WatchtimeCommandService` block). The
returned string is routed to the originating platform:
Twitch → `ITwitchService`, Kick → `IKickService`, YouTube →
`IYouTubeService`, Discord → `SendDiscordReplyAsync` (per-bot/DM aware).
Returning `null` (non-quote messages) lets the rest of the pipeline continue.

DI: `App.xaml.cs` registers `services.AddSingleton<IQuoteService, QuoteService>();`
(giveaway/poll block). `ProfileAwareDataService` forwards every quote method
to the current profile's `DapperDataService`.

## UI (Commands page → QUOTES tab)
- Count header (`QuoteCountText`: "No quotes yet" / "1 quote" / "N quotes [matching 'x']").
- Search box (`QuoteSearchText`) with CLEAR — filters via `SearchQuotesAsync`;
  empty = full list, lowest number first.
- Grid: # / Quote / Author (AddedBy) / Platform / Date (local time via
  `QuoteDateConverter`) / per-row DELETE button (ConfirmDialog).
- Empty state card while `ShowQuoteEmptyState` is true.
- Add box (`NewQuoteText` + ADD): dashboard-added quotes are stored with
  AddedBy/Platform = "Manual".

## Tests
`tests/VyralBot.UI.Tests/QuoteServiceTests.cs` — table round-trips (sequential
numbers, search, delete, random) and the full command family including
mod-gating and the four-platform origin-tracking test.
