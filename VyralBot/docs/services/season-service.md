# SeasonService (loyalty seasons / competitive resets)

`src/VyralBot.Core/Services/Currency/SeasonService.cs` — loyalty seasons, the
streamer-standard competitive reset. Interface: `ISeasonService` (same folder,
plus the `SeasonResult` / `SeasonStatus` / `SeasonLeaderEntry` records).
Registered as a DI singleton in `App.xaml.cs` next to the other currency
services. Consumers: the Currency page **Seasons card**
(`UI/ViewModels/CurrencyViewModel.cs`, Leaderboard tab) and the `!season` chat
command (`CurrencyCommandService`).

## Storage (append-only history)

`CurrencySeasons` table (per profile, created by
`DapperDataService.EnsureDatabaseCreatedAsync` next to the other currency
tables):

| Column | Type | Notes |
|---|---|---|
| `Id` | INTEGER PK AUTOINCREMENT | doubles as season order (higher = newer) |
| `Name` | TEXT | display name, e.g. "Season 3" |
| `StartedAt` | TEXT (UTC) | season start |
| `EndedAt` | TEXT (UTC, NULL) | NULL while the season is running |
| `TopCount` | TEXT (JSON) | closing snapshot: `[{"username":"…","platform":"…","balance":123}]`, max 20 entries |

Season rows are **never deleted**, and a row is written twice at most (insert,
then one close). `IDataService` surface: `GetCurrencySeasonsAsync` (newest
first), `GetCurrentCurrencySeasonAsync` (open row or null) and
`StartCurrencySeasonAsync(name, topJson, resetBalances)`.

## StartSeasonAsync semantics (safety order)

1. **Snapshot before reset**: the live top-20 leaderboard
   (`GetCurrencyLeaderboardAsync(20)`) is read BEFORE any write, so the closing
   season records the true final standings even when the same call zeroes
   balances.
2. **One transaction** (`StartCurrencySeasonAsync` in DapperDataService):
   close the open season (`EndedAt = now`, `TopCount = snapshot`) → insert the
   new season row → optionally reset. Any failure rolls the whole thing back.
3. **Reset zeroes BALANCES ONLY**: `UPDATE UserCurrency SET Balance = 0`. The
   lifetime `TotalEarned`/`TotalSpent` aggregates and the entire
   `CurrencyTransactions` audit trail are history and are never modified or
   deleted.
4. The reset count is returned (`SeasonResult.ResetBalances`) and logged at
   info level together with the new season id.

Empty names are rejected without writing anything.

## `!season` chat command (everyone)

`CurrencyCommandService` routes `!season` to
`ISeasonService.GetSeasonMessageAsync()`:

```
Season: {name} — day {n}. Previous winner: {user}.
```

The winner sentence is omitted for the first season; before any season exists
the reply is "No season is running yet — ask the streamer to start one!".
`!season` is in the informational-commands cooldown set (10 s per user, same
table as `!points`).

## UI — Currency page → Leaderboard tab → Seasons card

Next to the leaderboard overlay card:

- Current season name + 1-based day count ("Season 2 — day 14", or "No active
  season").
- Season history list: name, date range and the top-3 winners with medals,
  parsed from each closing snapshot (`SeasonService.ParseTopCount` — tolerant
  of null/corrupt JSON).
- **START NEW SEASON** → in-card confirm dialog (app-style, no modal) with a
  **reset-balances checkbox, default OFF**, then runs the start and surfaces
  `SeasonResult.Message` in the page status bar. On success the card and the
  leaderboard refresh (a reset zeroes the visible balances).

## Tests

`tests/VyralBot.UI.Tests/Wave15SeasonServiceTests.cs` — runs the real service
on a real `DapperDataService` with a temp SQLite file (same style as
`DataLayerNewTablesTests`): name validation, first-season open without reset,
snapshot-before-reset (25 seeded users → top-20 cap + pre-reset order),
reset-zeroes-balances-only (earned aggregates + transaction audit untouched),
history ordering, day counting (backdated start), exact `!season` reply
formatting and snapshot-parse tolerance.
