# Commands View (Commands page)

## Overview
Daily-driver command management surface: BUILT-IN / CUSTOM / TIMERS / HELP tabs
plus the marketplace overlay. Every command EDIT and NEW COMMAND opens the
visual flow editor (`FlowEditorView` overlay hosted by `CommandsView`) — saving
a seeded flow ADOPTS the command row.

## Source Files
| File | Description |
|------|-------------|
| `src/VyralBot.UI/Views/CommandsView.xaml` | Tabs, grids, search bar, overlays |
| `src/VyralBot.UI/Views/CommandsView.xaml.cs` | Flow-editor / marketplace overlay hosting |
| `src/VyralBot.UI/ViewModels/CommandsViewModel.cs` | List state, bot scope, search, duplicate, timers |

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

## Timers tab
CRUD + interval (min 5) + platform picker (`TimerPlatforms`:
Twitch/Kick/Discord/YouTube only) + Discord bot target. Firing is handled by
`CommandTimerService`. Minimum-messages-between-fires is NOT in the data model
yet (gap).

## HELP tab
Variable list, permission levels, bot scope, where commands run (incl. the
TikTok "coming soon" note) and the built-in command reference — kept in sync
with `CommandService` built-ins, the seeded command rows in
`DapperDataService` and the `CurrencyCommandService` switch (incl. aliases
like `!balance`, `!top`, `!bet`).
