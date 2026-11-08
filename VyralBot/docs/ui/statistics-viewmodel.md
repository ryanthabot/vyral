# StatisticsViewModel + StatisticsView

Event statistics page (`Views/StatisticsView.xaml`): stream session summaries
(Sessions tab), aggregated event counts, top contributors per event type, and
all-time top users. Backed by `IStatisticsService`
(`VyralBot.Core/Services/Statistics/StatisticsService.cs`) and
`ISessionSummaryService`
(`VyralBot.Core/Services/Statistics/SessionSummaryService.cs`).

## Sessions tab (wave 16)

The first tab renders the post-stream reports `SessionSummaryService` compiles
when a stream ends (see `services/session-summary-service.md`):

- **Last session card**: platforms + duration (`DurationText`), six headline
  numbers (new followers, new subs, raids, cheers, chat messages, currency
  awarded) and the top-3 breakdown — top chatters (from the summary's
  `TopChatters`) plus top event types (`LastSessionTopEvents`, "Follow × 12"
  rows computed in the VM with the chart-builder ordering: value desc, ordinal
  label tie-break).
- **Recent sessions list**: DataGrid of the persisted summaries — started
  (local time), platforms, duration, headline numbers (`HeadlineText` =
  follows · subs · messages).
- Both cards have empty states (`InvertBooleanToVisibilityConverter`) shown
  until the first tracked stream ends.

`LoadSessionsAsync()` runs inside `LoadStatisticsAsync()` (so Refresh and
every navigation reload it) and reads
`ISessionSummaryService.GetRecentSummariesAsync()`; failures are logged only —
the analytics grids keep working when the summary store is unreadable.

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

Event Counts / Top Contributors / Top Users tabs are DataGrids (column sorting
is the DataGrid default; the Rank column binds via `RowIndexConverter` on the
row so its header is not sortable — intended). Since wave 12 (KI-54) the first
two tabs also render a dependency-free bar chart above each grid.

## Bar charts (KI-54, wave 12)

`Controls/Charts/SimpleBarChart.cs` — an `ItemsControl` subclass whose default
style lives in `Themes/Generic.xaml` (picked up automatically via
`ThemeInfo`; no App.xaml merge needed). Items are immutable
`BarChartItem`s (`Label`, `Platform`, `Value`, pre-normalized `Fraction`,
formatted `ValueText`/`ToolTipText`). Each row is label + proportional bar +
value; bar width = `Fraction × track width` (`BarWidthConverter`
MultiBinding), bar color per platform via `DynamicResource` DataTriggers
(TwitchBrush/KickBrush/YouTubeBrush/DiscordBrush/TikTokBrush, PrimaryBrush
fallback), text via TextPrimary/TextSecondaryBrush — theme switches re-color
without rebuilding items. When `Items` is empty the items host collapses and
the `EmptyText` hint shows instead. `LabelWidth` (default 140) keeps labels
column-aligned across rows.

The pure math is `StatisticsChartBuilder` (bottom of
`StatisticsViewModel.cs`, unit-tested in
`tests/VyralBot.UI.Tests/Wave12StatisticsBarChartTests.cs`): drops zero-count
rows, orders value-desc with ordinal label tie-break, caps at 8 bars and
normalizes widths to the largest value. The overview chart aggregates
`EventCounts` rows by event type across platforms (dominant platform — count
desc, then ordinal platform — picks the bar color); the contributor chart
reuses `TopContributors` as-is. No new SQL — both charts are rebuilt from
collections the page already loaded.

## Service behavior (`StatisticsService`)

- Queries go through `IDataService.GetEventStatisticsAsync`, whose SQL filters
  platform/event-type/date with bound parameters and applies
  `ORDER BY Timestamp DESC LIMIT @Limit` server-side. Top-N *aggregation*
  (GROUP BY user) is client-side over that window — no SQL GROUP BY yet.
- Top-N ordering is deterministic: score DESC, then `Username`, then
  `Platform` (ordinal), matching the wave-2 leaderboard tie-break rule.
