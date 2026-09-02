# Chat System

## Overview
Multi-platform chat system supporting Twitch and Kick (with placeholders for YouTube, TikTok, Discord). Chat messages flow from platform integrations through the EventBus to the ChatViewModel for display. Includes auto-reply, chat event settings, and message storage.

## Source Files

### ChatViewModel
- **File:** `src/VyralBot.UI/ViewModels/ChatViewModel.cs`
- **Namespace:** `VyralBot.UI.ViewModels`
- **Class:** `ChatViewModel : ViewModelBase`
- **Constructor dependencies (17+):**
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
- **Observable properties:**
  - `ObservableCollection<ChatMessage> Messages`
  - `string MessageInput`
  - `bool AutoScroll`
  - `ObservableCollection<PlatformOption> ConnectedPlatforms`
  - `PlatformOption? SelectedPlatform`
  - `bool IsPlatformSelectorOpen`
  - `bool IsAllPlatformsSettingsOpen`
  - `bool IsTwitchSettingsOpen`
  - `bool IsKickSettingsOpen`
  - `bool IsYoutubeSettingsOpen`
  - `bool IsTiktokSettingsOpen`
- **EventBus subscriptions:** ~20 Guid subscription IDs for various events (chat messages, follows, subs, raids, etc.)
- **Features:** Platform selector, per-platform settings popups, message history dedup, recent Kick message echo tracking

### Chat Message Store
- **Interface:** `src/VyralBot.Core/Services/Chat/IChatMessageStore.cs`
  - `Task<bool> AddAsync(string platform, string channel, IEvent evt)`
  - `Task<IReadOnlyList<IEvent>> GetRecentAsync(string platform, string channel, int limit = 200)`
  - `Task DeleteAsync(string platform, string channel)`
- **Implementation:** `src/VyralBot.Core/Services/Chat/InMemoryChatMessageStore.cs`
  - Uses `ConcurrentDictionary<string, ConcurrentQueue<IEvent>>`
  - Max 500 messages per key
  - Deduplication via MessageId reflection

### Chat Event Settings
- **Interface:** `src/VyralBot.Core/Services/Chat/IChatEventSettingsService.cs`
  - `GetSettingAsync`, `GetAllSettingsAsync`, `SaveSettingAsync`, `DeleteSettingAsync`
  - `IsEventEnabledAsync`, `IsAutoReplyEnabledAsync`, `GetAutoReplyMessageAsync`
  - `GetSoundSettingsAsync`, `InvalidateCache`
- **Implementation:** `src/VyralBot.Core/Services/Chat/ChatEventSettingsService.cs`
  - Constructor: `IDataService`, `ILogger`
  - Caches all `ChatEventSetting` entities in `Dictionary<string, ChatEventSetting>` with `SemaphoreSlim` lock
- **Entity:** `src/VyralBot.Data/Entities/ChatEventSetting.cs`
  - `Guid Id`, `string Platform`, `string EventType`
  - `bool ShowInChat`, `bool AutoReplyEnabled`, `string? AutoReplyMessage`
  - `bool SoundEnabled`, `string? SoundFile`, `double SoundVolume`

### Auto-Reply System
- **Interface:** `src/VyralBot.Core/Services/Chat/IAutoReplyService.cs`
  - `ProcessTemplate`, `ShouldSendReply`, `GetAutoReplyAsync`, `CleanupExpiredEntries`, `ClearRateLimits`
- **Implementation:** `src/VyralBot.Core/Services/Chat/AutoReplyService.cs`
  - Constructor: `ILogger`, `IDataService?`, `IRuleEvaluator?`
  - Uses `AutoReplyRateLimiter` (30-second default cooldown)
  - Evaluates `RuleCondition` JSON from `AutoReplyRule` entities
- **Rate Limiter:** `src/VyralBot.Core/Services/Chat/AutoReplyRateLimiter.cs`
  - Per-user/platform/event cooldown via `ConcurrentDictionary<string, DateTime>`
  - 30-second default, 10-minute cleanup interval
- **Entity:** `src/VyralBot.Data/Entities/AutoReplyRule.cs`
  - `int Id` (auto-increment), `string Name`, `string Platform`, `string EventType`
  - `bool IsEnabled`, `int Priority`, `string ConditionJson`, `string ReplyTemplate`, `int TriggerCount`

### ChatMessage Entity (Database)
- **File:** `src/VyralBot.Data/Entities/ChatMessage.cs`
- **Properties:** `Guid Id`, `string Platform`, `string Channel`, `string? MessageId`, `string? UserId`, `string? Username`, `string? DisplayName`, `string? Content`, `string? Color`, `int IsModerator`, `int IsSubscriber`, `int IsOwner`, `string? BadgesJson`, `int IsSystemMessage`

### Chat Message Flow
```
Platform (Kick/Twitch)
  → Platform Service (KickLibChatService / TwitchService)
    → EventBus.PublishAsync<TwitchChatMessageEvent|KickChatMessageEvent>()
    → IChatMessageStore.AddAsync()
      ↓
ChatViewModel (subscribed via EventBus)
  → Processes event
  → Checks chat event settings (show in chat, auto-reply, sound)
  → Adds to Messages ObservableCollection
  → Runs command processing (ICommandService)
  → Runs auto-reply (IAutoReplyService)
  → Updates currency (ICurrencyService)
  → Updates watchtime (WatchtimeService)
```

### Platform Send Flow
```
ChatViewModel.SendMessageAsync()
  → Checks SelectedPlatform
  → ITwitchService.SendMessageAsync() or IKickService.SendMessageAsync()
```

## All Chat Service Files
| File | Description |
|------|-------------|
| `src/VyralBot.Core/Services/Chat/IChatMessageStore.cs` | Message store interface |
| `src/VyralBot.Core/Services/Chat/InMemoryChatMessageStore.cs` | In-memory implementation |
| `src/VyralBot.Core/Services/Chat/IChatEventSettingsService.cs` | Event settings interface |
| `src/VyralBot.Core/Services/Chat/ChatEventSettingsService.cs` | Cached settings implementation |
| `src/VyralBot.Core/Services/Chat/IAutoReplyService.cs` | Auto-reply interface |
| `src/VyralBot.Core/Services/Chat/AutoReplyService.cs` | Auto-reply with rule evaluation |
| `src/VyralBot.Core/Services/Chat/AutoReplyRateLimiter.cs` | Per-user rate limiting |
| `src/VyralBot.UI/ViewModels/ChatViewModel.cs` | Main chat UI ViewModel |
