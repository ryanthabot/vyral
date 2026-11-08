# EventHistoryViewModel + EventHistoryView

Event log page (`Views/EventHistoryView.xaml`): paginated view of the
`EventStatistics` table with filters, detail/reply viewers, and CSV export.
Talks to `IDataService` directly.

## Loading and filters

- `LoadEventsAsync()` pushes platform / event-type / date range into
  `GetEventStatisticsAsync` (SQL WHERE + `ORDER BY Timestamp DESC LIMIT`);
  only the username "contains" filter stays in-memory.
- The query window is capped at 10,000 rows (`QueryWindowCap`). A full window
  sets `TruncationNotice` (warning brush) — truncation is never silent.
- Pagination is client-side over the filtered list (50/page).

## Date-range validation

On every load the range is normalized: `EndDate` clamped to today (no future
data), `StartDate` forced ≤ `EndDate`. Clamped values are written back to the
bound DatePicker properties (the picker's `SelectedDateChanged` re-triggers a
search; the second pass is a no-op, so there is no loop). The query bound is
`EndDate.Date.AddDays(1)` (exclusive upper bound).

## CSV export

`ExportToCsvCommand` (Export CSV button) writes the **full filtered result**
(`_lastFilteredEvents`, kept from the last load — all pages, not just the
visible one) with columns `Timestamp,Platform,EventType,Username,
WasAutoReplied,EventData`. UTF-8 **with BOM** for Excel
(`new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)`), every field
RFC-4180 quoted/escaped. `StatusMessage` confirms the row count, and when
`TruncationNotice` is set it appends "newest 10,000-row window" so the file is
never mistaken for the complete range.

## Other actions

- `ViewDetailsCommand` shows a MessageBox dump (pretty-printed EventData).
- `ReplayAutoReplyCommand` looks up the `ChatEventSetting` auto-reply and
  renders it with placeholders substituted.
- `ClearHistoryCommand` deletes every event row for the profile behind a
  `ConfirmDialog`.
