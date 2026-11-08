# TestEventPublisher

Single source of truth for what a "test event" looks like. Publishes canned
(but plausible) platform event records to the `IEventBus` so handlers, UI,
auto-replies, alerts, statistics, currency and workflows can be exercised
without a live platform connection.

- Source: `src/VyralBot.Integrations/Testing/TestEventPublisher.cs`
- DI: singleton (`App.xaml.cs` ConfigureServices)
- Consumers: `EventTestingViewModel` (Event Testing page); intended to be
  shared by the token-gated HTTP control API (`POST /api/events/test`) so both
  entry points fire identical payloads.
- Lives in VyralBot.Integrations because only that project references every
  platform's event types.

## API

`Task PublishTestEventAsync(string eventType, string? username = null, string? message = null)`
— publishes the named event with defaults `TestUser123` / "This is a test
message". Unknown ids throw `ArgumentException`. Dispatch is by the concrete
CLR event type, so every typed subscriber (ChatViewModel handlers,
WorkflowTriggerService triggers, dashboards) runs for real.

## Supported event types

| Type id | Event record | Fires (ChatViewModel) | Workflow key |
|---|---|---|---|
| `kick.follow` | `KickFollowEvent` | chat line, follow sound, alert, Kick/Follow stats | `follow` (Kick) |
| `kick.subscribe` | `KickSubscribeEvent` | chat line, sub sound, alert, Kick/Subscribe stats | `subscribe` (Kick) |
| `kick.giftsub` | `KickGiftSubEvent` | chat line, alert, stats | `giftsub` (Kick) |
| `kick.host` | `KickHostEvent` | chat line, alert, stats | `host` (Kick) |
| `kick.kicksgifted` | `KickKicksGiftedEvent` | chat line, alert, stats, currency bonus | `kicksgifted` (Kick) |
| `kick.rewardredeemed` | `KickRewardRedeemedEvent` | chat line, redemption sound, alert, stats | `rewardredeemed` (Kick) |
| `twitch.follow` | `TwitchFollowEvent` | chat line, follow sound, alert, Twitch/Follow stats | `follow` (Twitch) |
| `twitch.subscribe` | `TwitchSubscribeEvent` | chat line, sub sound, alert, stats | `subscribe` (Twitch) |
| `twitch.resub` | `TwitchResubEvent` | chat line, alert, stats | `resub` (Twitch) |
| `twitch.giftsub` | `TwitchGiftSubEvent` | chat line, alert, stats | `giftsub` (Twitch) |
| `twitch.raid` | `TwitchRaidEvent` | chat line, raid sound, alert, stats | `raid` (Twitch) |
| `twitch.cheer` | `TwitchCheerEvent` | chat line, cheer sound, alert, stats, currency bonus | `cheer` (Twitch) |
| `twitch.channelpointsredeem` | `TwitchChannelPointsRedeemEvent` | chat line, redemption sound, "Reward Redeemed!" alert, Twitch/RewardRedeemed stats | `rewardredeemed` (Twitch) |
| `youtube.superchat` | `YouTubeSuperChatEvent` ($5.00) | cheer sound, "Super Chat!" alert, YouTube/Cheer stats, currency bonus (500 cents) | `superchat` (YouTube) |
| `youtube.membership` | `YouTubeMembershipEvent` | "New Subscriber!" alert, viewer-status subscription, YouTube/Subscribe stats | none in V1 |
| `discord.memberjoined` | `DiscordMemberJoinedEvent` | follow sound, "New Member!" alert, Discord/MemberJoined stats | `discordmemberjoined` |
| `discord.boost` | `DiscordBoostEvent` | subscribe sound, "Server Boosted!" alert, Discord/Boost stats | `discordboost` |

Sound rows play only when a mapping exists (`SoundService.PlayEventSoundAsync`
resolves `EventSound_{platform}_{event}` config). Statistics rows are visible
on the Statistics / Event History pages. Workflows REALLY execute when an
enabled event trigger matches the key + platform.
