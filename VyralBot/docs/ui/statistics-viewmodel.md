# StatisticsViewModel + StatisticsView

Event statistics page (`Views/StatisticsView.xaml`): aggregated event counts,
top contributors per event type, and all-time top users. Backed by
`IStatisticsService` (`VyralBot.Core/Services/Statistics/StatisticsService.cs`).

## Data flow

- `InitializeAsync()` (called by `MainViewModel` on navigation) → `LoadStatisticsAsync()`.
- Date range defaults to the last 30 days; `EndDate` is tomorrow-midnight
  because the SQL uses `Timestamp <= @EndDate` (exclusive upper bound).
- `GetEventCountsByTypeAsync(start, end)` returns per-platform dictionaries;
  the platform filter ("All" = `null`) is applied client-side to the event-count
  grid. `TopUsersAsync` likewise filters client-side from a 100-row fetch.
- Top Contributors tab queries only when a concrete event type is selected
  ("All" clears the grid).

## Date-range validation

`ClampDateRange()` runs before every load/export: end clamped to
`DateTime.Today.AddDays(1)`, start forced ≤ end, null pickers defaulted
(-30 days). Clamped values are written back to the bound properties so the
DatePickers reflect them.

## CSV export

`ExportToCsvCommand` (Export CSV button) re-aggregates with the current
filters via `GetEventCountsByTypeAsync` and writes `Platform,EventType,Count`
rows — UTF-8 **with BOM** (`new UTF8Encoding(encoderShouldEmitUTF8Identifier:
true)`; `File.WriteAllTextAsync` without an encoding is BOM-less and Excel
misreads it), every field RFC-4180 quoted/escaped. Result is shown in
`StatusMessage` under the filter card (success row count or failure reason).

## Rendering

All three tabs are DataGrids (Event Counts / Top Contributors / Top Users) —
no chart control. Column sorting is the DataGrid default (`CanUserSortColumns`
is true; the Rank column binds via `RowIndexConverter` on the row so its
header is not sortable — intended). Charts would need a charting library; a
gap noted in the wave-8 audit, not a defect.

## Service behavior (`StatisticsService`)

- Queries go through `IDataService.GetEventStatisticsAsync`, whose SQL filters
  platform/event-type/date with bound parameters and applies
  `ORDER BY Timestamp DESC LIMIT @Limit` server-side. Top-N *aggregation*
  (GROUP BY user) is client-side over that window — no SQL GROUP BY yet.
- Top-N ordering is deterministic: score DESC, then `Username`, then
  `Platform` (ordinal), matching the wave-2 leaderboard tie-break rule.
