# AlertSettingsViewModel + AlertSettingsView

Visual Alerts settings page (`Views/AlertSettingsView.xaml`): master toggle,
duration, animation, screen position, per-event toggles and per-event TEST
buttons for the alert overlay. Backed by `IAlertService`
(`VyralBot.UI/Services/AlertService.cs`).

## Persistence

Single JSON blob under the Configs key `AlertSettings`
(`AlertService.GetAlertSettingsAsync` / `SaveAlertSettingsAsync`), stored in
the per-profile database via `IDataService.SetConfigValueAsync`. Fields:
`AlertsEnabled`, `DurationSeconds` (1–15, clamped), `Animation`
(`SlideInFromTop|SlideInFromBottom|FadeIn|Bounce`), `Position` (9-point grid),
and six per-event toggles (`Follow|Subscribe|GiftSub|Raid|Host|Cheer`AlertsEnabled).

## Config alignment (settings ↔ service)

Every field `AlertService.ShowAlertAsync(applySettings: true)` reads is
exposed on this page, and every control on the page maps to a field — no dead
controls, no unread settings. Gating happens in `AlertService`:

- `AlertsEnabled` off → all alerts skipped.
- Per-event gating via `TitleToEventMap`: the alert *title* passed by
  ChatViewModel (`"New Follower!"`, `"New Subscriber!"`, `"Resubscribed!"`,
  `"Gift Subs!"`, `"Raid!"`, `"New Host!"`, `"Bits!"`, `"Super Chat!"`,
  `"New Member!"`, `"Server Boosted!"`) maps to a toggle. Unmapped titles
  (`"Kicks Gifted!"`, `"Reward Redeemed!"`, `"Test Alert!"`) always show.
- Do-not-disturb (`Notifications.DoNotDisturb`, owned by Settings →
  Notifications) suppresses alerts before the toggles are consulted.
- Persisted duration/animation/position override caller values (source of
  truth), clamped to 1–15s.

## Per-event TEST buttons

Each toggle row has a TEST button → `TestEventAlertCommand` with
`CommandParameter` = event id (`follow|subscribe|giftsub|raid|host|cheer`).
It fires `ShowAlertAsync` with the **exact production title** from
`EventTestPayloads` (e.g. `cheer` → `"Bits!"`), so popup text and
`TitleToEventMap` routing match live events. `applySettings: false` — the
preview is gated by the *current page* toggles (`IsEventEnabledOnPage` +
master toggle), so flipping a toggle off and pressing TEST shows a
suppression explanation in `StatusMessage` instead of a popup, without
saving first.

The General Settings card's "Preview Current Settings" button previews the
selected duration/animation/position live (also `applySettings: false`).

## Not configurable here (by design, noted in the page UI)

- **Alert text templates do not exist.** Alert titles/messages are hard-coded
  at the ChatViewModel call sites; `VariableReplacementService` is not in the
  alert path, so `{username}` etc. are NOT substituted in alerts. The page
  documents this. Adding templates would require ChatViewModel/AlertService
  changes (ChatViewModel is fragile — coordinate before attempting).
- **Per-event sounds/volumes** live on the Sounds page
  (`EventSound_*` / `EventVolume_*` keys — see `docs/ui/sounds-view.md`).
- **Per-event images**: `AlertSettings` has no per-event asset fields; the
  overlay supports an `imageUrl` argument but no caller or setting supplies
  one. Adding pickers requires model + resolution-logic changes first.
