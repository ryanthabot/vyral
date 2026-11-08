# Commands View (Commands page)

## Overview
Daily-driver command management surface: BUILT-IN / CUSTOM / TIMERS / QUOTES /
HELP tabs plus the marketplace overlay. Every command EDIT and NEW COMMAND
opens the visual flow editor (`FlowEditorView` overlay hosted by
`CommandsView`) — saving a seeded flow ADOPTS the command row.

## Source Files
| File | Description |
|------|-------------|
| `src/VyralBot.UI/Views/CommandsView.xaml` | Tabs, grids, search bar, overlays |
| `src/VyralBot.UI/Views/CommandsView.xaml.cs` | Flow-editor / marketplace overlay hosting |
| `src/VyralBot.UI/ViewModels/CommandsViewModel.cs` | List state, bot scope, search, duplicate, timers, quotes |

## Toolbar / header
- **Search box** (`SearchText`): case-insensitive substring filter on command
  name + response (including alternate `ResponsesJson` replies), applied to
  BOTH the BUILT-IN and CUSTOM grids on top of the bot scope. CLEAR button
  (visible only with text) resets it.
- **DUPLICATE** (`DuplicateCommand`): copies the selected row (built-in or
  custom) into a new CUSTOM command named `{name}-copy`, `{name}-copy2`, ...
  Response/replies/actions, permission, cooldowns, platform flags and bot
  scope carry over; usage stats reset. Duplicating a built-in is the supported
  way to customize one (built-in rows stay locked).

## Grids
- BUILT-IN and CUSTOM grids both carry per-platform checkbox columns
  (Kick / Twitch → `EnabledOnKick` / `EnabledOnTwitch`, saved via
  `TogglePlatformCommand`), Usage and Last Used columns (`UsageCount`,
  `LastUsedAt` — written by `IncrementCommandUsageAsync` on every execution)
  and sortable headers (`SortMemberPath` on template columns).
- CUSTOM grid uses grid-level `IsReadOnly="False"` with read-only text
  columns so the checkbox cell templates stay clickable (same pattern as the
  BUILT-IN grid).
- Platform scope model: Twitch/Kick = per-command flags; Discord = `BotId`
  bot scope; YouTube = runs whenever the command is enabled (no flag column);
  TikTok = no chat pipeline, never selectable anywhere on this page.

## Per-platform responses
One command, a different reply text per platform (streamers want
platform-specific calls-to-action). The flow editor's command-trigger panel
carries a collapsible **Per-platform responses** expander (visible only for
"Prefix command" triggers) with four boxes — Twitch / Kick / YouTube /
Discord — hint "leave blank to use the default response". Values persist on
the linked Command row as `Command.PlatformResponsesJson`
(`{"Twitch":"...","Kick":"..."}`; blank boxes dropped, all-blank → NULL) on
SAVE, and load from the row on EDIT (both the seeded-row path and the
adopted-flow path). No new grid columns — the grids stay lean; the edit flow
(flow editor) shows them.

Resolution precedence, everywhere the row's reply text is picked for the
ORIGINATING platform: a non-blank `PlatformResponsesJson` entry wins, else
random `ResponsesJson` replies, else the single `Response`. Pick sites:
`CommandService.PickResponse`/`StoredResponse` (chat pipeline — custom
commands AND stored built-in templates like `!socials`/`!discord`) and
`CommandTimerService.FireTimerAsync` (timers, keyed by `TargetPlatform`).
Null/blank entries keep the legacy single-response behavior — fully backward
compatible (additive `ALTER TABLE Commands ADD COLUMN PlatformResponsesJson
TEXT` migration; helpers live on the entity: `Command.ParsePlatformResponses`
/ `ResolvePlatformResponse` / `BuildPlatformResponsesJson`). Round-trip
coverage: `tests/VyralBot.UI.Tests/PlatformResponsesRoundTripTests.cs`.

## Timers tab
CRUD + interval (min 5) + platform picker (`TimerPlatforms`:
Twitch/Kick/Discord/YouTube only) + Discord bot target + min messages between
fires (numeric field, 0–1000, shown as the "Min Msgs" grid column). Firing is
handled by `CommandTimerService`: timers with `CommandTimer.MinimumMessages >
0` only fire once that many chat messages arrived on their platform since the
platform's last fire (chat-activity gate counted from
`ChatMessageReceivedEvent`; the per-platform counter is bounded, zeroed when a
timer on that platform fires and cleared when the scheduler stops). 0 (the
column default for pre-existing rows) = fire regardless of chat activity.

## QUOTES tab
Chat quote database (`IQuoteService`, see `docs/features/quotes.md`): count
header, search box (`QuoteSearchText` → `SearchQuotesAsync`, CLEAR resets to
the full lowest-number-first list), REFRESH, a grid of # / Quote / Author
(`AddedBy`) / Platform / Date (`QuoteDateConverter`, local time) with a
per-row DELETE (ConfirmDialog), an empty-state card while
`ShowQuoteEmptyState` is true, and an add box (`NewQuoteText` + ADD —
dashboard rows store AddedBy/Platform "Manual"). The `!quote` command family
(!quote / !quote <n> / !quote search / mod-only add+del, !quotecount) is
answered by `QuoteService` in the ChatViewModel pipeline on every platform.

## HELP tab
Variable list, permission levels, bot scope, where commands run (incl. the
TikTok "coming soon" note) and the built-in command reference — kept in sync
with `CommandService` built-ins, the seeded command rows in
`DapperDataService` and the `CurrencyCommandService` switch (incl. aliases
like `!balance`, `!top`, `!bet`).
