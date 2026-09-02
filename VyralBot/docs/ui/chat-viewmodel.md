# Chat ViewModel

## Overview
Multi-platform chat ViewModel with 17+ dependencies. Displays messages from Twitch and Kick, handles sending, auto-reply, commands, currency, watchtime, and event processing.

## Source File
- **File:** `src/VyralBot.UI/ViewModels/ChatViewModel.cs` (~2562 lines)
- **Namespace:** `VyralBot.UI.ViewModels`
- **Class:** `ChatViewModel : ViewModelBase`

## Constructor Dependencies (17+)
- `ILogger<ChatViewModel>`
- `IEventBus`
- `ITwitchService`
- `IKickService`
- `KickSettingsViewModel`
- `IChatMessageStore`
- `IChatEventSettingsService`
- `IAutoReplyService`
- `ICurrencyService?` (optional)
- `ICurrencyCommandService?` (optional)
- `ICommandService?` (optional)
- `ISoundService?` (optional)
- `IAlertService?` (optional)
- `GiveawayCommandService?` (optional)
- `PollCommandService?` (optional)
- `WatchtimeService?` (optional)
- `WatchtimeCommandService?` (optional)
- `IVariableReplacementService?` (optional)

## Observable Properties
- `ObservableCollection<ChatMessage> Messages`
- `string MessageInput`
- `bool AutoScroll`
- `ObservableCollection<PlatformOption> ConnectedPlatforms`
- `PlatformOption? SelectedPlatform`
- `bool IsPlatformSelectorOpen`
- `bool IsAllPlatformsSettingsOpen`
- `bool IsTwitchSettingsOpen`, `bool IsKickSettingsOpen`, `bool IsYoutubeSettingsOpen`, `bool IsTiktokSettingsOpen`

## EventBus Subscriptions
~20 Guid subscription IDs for: chat messages (Twitch/Kick), follows, subs, gift subs, raids, cheers, channel points, kicks gifted, reward redeemed, stream status, platform connection changes.

## Message Processing Pipeline
For each incoming chat message event:
1. Check chat event settings (show in chat?)
2. Add to Messages collection (UI thread dispatch)
3. Process commands (ICommandService)
4. Process auto-replies (IAutoReplyService)
5. Award chat activity currency (ICurrencyService)
6. Record watchtime activity (WatchtimeService)
7. Play event sounds (ISoundService)

## All Files
| File | Description |
|------|-------------|
| `src/VyralBot.UI/ViewModels/ChatViewModel.cs` | Chat ViewModel (2562 lines) |
