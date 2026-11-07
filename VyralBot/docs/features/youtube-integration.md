# YouTube Integration

## Overview
YouTube Live Chat integration via YouTube Data API v3 (HTTP polling, no WebSocket).
Handles OAuth token refresh, live broadcast discovery, chat send/receive, broadcast
info updates, and structured Super Chat / membership events.

## Source Files

### Interface
- **File:** `src/VyralBot.Integrations/YouTube/IYouTubeService.cs`
- **Namespace:** `VyralBot.Integrations.YouTube`
- **Properties:** `bool IsConnected`, `bool RequiresReauth`, `string? ReauthReason`, `string LiveChatState`, `string? Channel`, `string? BotUsername`
- **Methods:** `ConnectAsync(username, accessToken, channel)`, `ConnectAsync(username, accessToken, refreshToken, channel)`, `DisconnectAsync()`, `SendMessageAsync(message)`, `UpdateStreamInfoAsync(title, description)`
- **Registered:** singleton in `src/VyralBot.UI/App.xaml.cs` (`AddSingleton<IYouTubeService, YouTubeChatService>()`)

### Implementation: YouTubeChatService
- **File:** `src/VyralBot.Integrations/YouTube/YouTubeChatService.cs`
- **Class:** `YouTubeChatService : IYouTubeService, IDisposable`
- **Constructor params:** `ILogger<YouTubeChatService>`, `IDataService`, `IEventBus`, `IChatMessageStore?` (optional)
- Polls `liveChat/messages` on YouTube's recommended interval; discovers the active
  broadcast via `liveBroadcasts` (re-checking every 30s while off-air). Token is
  refreshed on 401 and proactively when close to expiry.

### Live chat message types → EventBus
| snippet.type | EventBus events | Notes |
|---|---|---|
| `textMessageEvent` | `YouTubeChatMessageEvent`, `ChatMessageReceivedEvent` | Stored in `IChatMessageStore` |
| `superChatEvent` | **`YouTubeSuperChatEvent`** + the two chat events | Synthetic chat text `💎 {user} super chatted {amount}! - {comment}` (mirrors Twitch cheer rendering) |
| `superStickerEvent` | **`YouTubeSuperChatEvent`** (`IsSuperSticker` = true) + the two chat events | Synthetic text `💎 {user} sent a Super Sticker {amount}!` |
| `newSponsorEvent` | `YouTubeMembershipEvent` + the two chat events | `💎 {user} became a member!` |
| `memberMilestoneChatEvent` | `YouTubeMembershipEvent` (`IsMilestone`) + the two chat events | Milestone level + comment |

Structured events are published fire-and-forget before the empty-message gate, the
same pattern for all special types.

### YouTubeSuperChatEvent (YouTube's cheer equivalent)
- **File:** `src/VyralBot.Integrations/YouTube/Events/YouTubeSuperChatEvent.cs`
- **Properties:** `Username`, `UserId`, `AmountMicros` (long, millionths of `Currency`;
  1 cent = 10_000 micros), `AmountDisplayString` (e.g. "$5.00"), `Currency` (ISO code),
  `Message` (viewer comment, null for stickers), `IsSuperSticker`, `StickerDisplayText`,
  `CheeredAt`
- Consumers should mirror the Twitch cheer pipeline (`TwitchCheerEvent` handler in
  ChatViewModel): "Bits!"-style alert, `SaveEventStatisticAsync(platform, "Cheer", …)`
  for unified statistics charts, and `CurrencyService.AwardEventBonusAsync(username,
  platform, "cheer", bitsEquivalent)` with `bitsEquivalent = AmountMicros / 10_000`
  (1 bit ≈ 1 cent).

## Command timer fan-out
`src/VyralBot.UI/Services/CommandTimerService.cs` injects `IYouTubeService` (the same
singleton Platforms connects) and sends scheduled timer messages to the connected
YouTube channel (`case "YouTube"` mirrors the Kick branch: skipped with a warning when
not connected; a not-live send throws, leaving `LastRunAt` unstamped so the timer
retries next tick). `TargetPlatform` "YouTube" also resolves `{channel}` for variable
replacement via `IYouTubeService.Channel`.

## Related docs
- `docs/features/chat-system.md` — unified chat pipeline
- `docs/features/event-system.md` — event vocabulary
