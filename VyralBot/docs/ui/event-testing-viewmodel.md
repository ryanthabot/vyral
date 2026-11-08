# EventTestingViewModel + EventTestingView

Event Testing page (`Views/EventTestingView.xaml`, `ViewModels/EventTestingViewModel.cs`).
Lets streamers verify the whole event pipeline without live traffic by firing
REAL platform events through the EventBus. Singleton VM, registered in
`App.xaml.cs`, navigated from `MainViewModel` (`_eventTestingScope`).

Event construction is delegated to `TestEventPublisher`
(`src/VyralBot.Integrations/Testing/TestEventPublisher.cs` — see
`docs/services/test-event-publisher-service.md` for the event table); the VM
supplies the test username/message and records what fired.

## Coverage (18 test fires, grouped by platform)

- Twitch: follow, subscribe, resub, gift sub, raid (200 viewers), cheer
  (100 bits), channel points redeem ("Hydrate Reminder", 500 points), first
  message (publishes `FirstTimeChatterEvent` directly, bypassing the
  newcomer detection, so welcome handling runs regardless of DB state).
- Kick: follow, subscribe, gift sub (5), host (150), Kicks gifted (1000),
  reward redeemed ("Test Reward", 500).
- YouTube: Super Chat ($5.00, USD), membership (new member, Tier 1).
- Discord: member joined ("Vyral Test Server"), server boost.

## State

- `TestUsername` / `TestMessage` — shared inputs for every fire.
- `LastFireStatus` / `LastFireFailed` — status line under the safety header,
  updated on every click (green on success, red on failure).
- `RecentFires : ObservableCollection<FiredEventEntry>` — last-10-fired log
  (newest first, hard cap 10). `FiredEventEntry` carries Platform, EventName,
  Detail, Time, Failed; platform names are colored via
  `PlatformToBrushConverter`, failed rows show a red FAILED marker.
- `ClearLogCommand` empties the log.

Every fire flows through the live pipeline: alert, mapped sound, chat line,
statistics row (Statistics / Event History), currency bonus where applicable,
and any workflow with a matching event trigger REALLY executes — the safety
banner in the view warns about this explicitly.

## View conventions

MaterialDesign cards on `BackgroundPrimaryBrush` / `CardBackgroundBrush`;
platform sections use the platform accent brushes (`TwitchBrush`, `KickBrush`,
`YouTubeBrush`, `DiscordBrush`) on icon, title and underline; buttons are
`MaterialDesignRaisedButton` in `WrapPanel`s.
