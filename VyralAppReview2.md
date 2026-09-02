# VyralBot — CHAT Section Review (VyralAppReview2.md)

Scope: the Chat section only (unified chat page, chat hub, per-platform chat settings, core chat services, message flow in/out, moderation in chat, chat UI features). Read-only review; all findings from actual code reading.

Key roots:
- UI: `src/VyralBot.UI/`
- Core: `src/VyralBot.Core/`
- Integrations: `src/VyralBot.Integrations/`
- Data: `src/VyralBot.Data/`

---

## 1. Unified Chat Page (main chat UI)

- **Where**: `src/VyralBot.UI/Views/ChatView.xaml` (558 lines) + `src/VyralBot.UI/Views/ChatView.xaml.cs` + `src/VyralBot.UI/ViewModels/ChatViewModel.cs` (2880 lines)
- **What**: Single scrolling chat window that merges messages from Twitch, Kick, YouTube (plus generic events for TikTok/Discord paths) into one list. Bottom bar: platform-selector button, message input (Enter = send), SEND button, and a separate round "Send to all connected platforms" (Vyral logo) button.

### Layout (ChatView.xaml)
- Row 0 header, left side: per-platform icon buttons that each open a quick-settings popup (`controls:LazyPopupContent` hosting `TwitchChatSettingsView`, `KickChatSettingsView`, `YoutubeChatSettingsView`, `TiktokChatSettingsView`, `DiscordChatSettingsView`), plus an "All Platforms" Vyral-logo button whose popup contains placeholder action buttons: Start Giveaway / Start Poll / Send Announcement / Trigger Event (no command bindings — visual placeholders only, ChatView.xaml:94-111).
- Platform buttons visibility bound to `IsTwitchConnected` / `IsKickConfigured` / `IsYoutubeConnected` / `IsTiktokConnected` / `IsDiscordConnected` (ChatView.xaml:118,150,182,214,246). Kick uses `IsKickConfigured` (connected OR channel configured) so its quick settings are reachable even when disconnected.
- Header right side: Toggle Auto-Scroll, Load Chat History, Clear Chat (UI only), Delete Chat History from Database (icon buttons; ChatView.xaml:278-311).
- Row 1: `ListBox` named `ChatScrollViewer` bound to `Messages` with UI virtualization (`VirtualizingStackPanel.IsVirtualizing=True`, `VirtualizationMode=Recycling`, CacheLength 5,5). Each row: timestamp (`h:mm tt`), platform logo image (per-platform DataTriggers, ChatView.xaml:19-43), badge images (`BadgeImages` ItemsControl, 18px, tooltip = badge name), colored username (`Foreground={Binding Color}`), message text (wrap). A "New messages" banner button sits collapsed on top (ChatView.xaml:428-439).
- Row 2: input area described above. SEND button background is colored via `PlatformToBrushConverter` from selected platform.

### ChatViewModel internals (src/VyralBot.UI/ViewModels/ChatViewModel.cs)
- Registered as a **singleton** (comment at line 196-197 "now that this is a Singleton"); `InitializeAsync()` guards with `_isInitialized`.
- Subscribes (via `IEventBus.SubscribeAsync<T>`) to ~20 event types at lines 207-270:
  - Chat: `TwitchChatMessageEvent`, `KickChatMessageEvent`, `YouTubeChatMessageEvent`, generic `ChatMessageReceivedEvent`
  - Routing/connection: `PlatformChatRoutingChangedEvent`, `PlatformConnectionChangedEvent`
  - Kick events: Follow, Subscribe, GiftSub, Host, KicksGifted, RewardRedeemed, StreamStatus
  - Twitch events: Follow, Subscribe, Resub, GiftSub, Raid, Cheer
- Message cap: keeps last **500** messages in `Messages` (trims from start; e.g. lines 579-583, 832-836, 2502-2506).

### Incoming message handling
- `OnTwitchChatMessageReceivedAsync` (line 503), `OnKickChatMessageReceivedAsync` (line 1611), `OnYouTubeChatMessageReceivedAsync` (line 1527), generic `OnChatMessageReceivedAsync` (line 592).
- **Echo suppression**: before sending, the VM records `message.Trim().ToLowerInvariant()` in `_recentTwitchMessages` / `_recentKickMessages` / `_recentYoutubeMessages`; incoming messages matching within a 10s window (15s for YouTube due to polling) are dropped (lines 62-68, 509-540, 1618-1650, 1533-1561). Comment notes this replaced an older "bot-username" filter that blocked ALL of the user's own messages (even ones typed on the platform website).
- **Per-platform display filters**: `TwitchFilterEnabled`, `KickFilterEnabled`, `YoutubeFilterEnabled`, `TiktokFilterEnabled`, `DiscordFilterEnabled` (lines 129-142) drop incoming messages per platform. `KickFilterEnabled` is initialized from `_kickSettings.IsChatEnabled` (lines 275-283) and updated live by `PlatformChatRoutingChangedEvent` → `OnPlatformRoutingChangedAsync` (line 840).
- `UpdateConnectedPlatforms()` (line 418) rebuilds the `ConnectedPlatforms` list: an "All Platforms" Vyral option first (if any platform connected), then Twitch (#9146FF), Kick (#53FC18), YouTube (#FF0000). TikTok/Discord currently hardcoded `false` (lines 485-486). Auto-selects "All Platforms" if nothing selected.
- `PlatformConnectionChangedEvent` → `OnPlatformConnectionChangedAsync` (line 866) refreshes the list on connect/disconnect.

### Generic path = command engine (OnChatMessageReceivedAsync, line 592)
For each generic chat message (any platform), in order:
1. Unified commands: if `_commandService.IsCommand(msg)` → builds `CommandContext` (username, platform, channel from Twitch/Kick service, mod/sub/VIP flags, currency balance), executes, sends response back to the **same platform** via `_twitchService.SendMessageAsync` / `_kickService.SendMessageAsync` (only Twitch+Kick wired; lines 602-657).
2. Currency commands (`!`-prefixed) via `_currencyCommandService.ProcessCommandAsync` (lines 659-686).
3. Giveaway commands via `_giveawayCommandService.ProcessCommandAsync` (lines 688-721).
4. Poll commands (`!poll`) via `_pollCommandService.ProcessCommandAsync` (lines 723-756).
5. Watchtime commands (`!watchtime`) via `_watchtimeCommandService.ProcessCommandAsync` (lines 758-787).
6. Watchtime activity tracking `_watchtimeService.RecordUserActivityAsync` (lines 789-800).
7. Currency chat-activity award for non-command messages (lines 802-813).
8. Finally appends message to UI (lines 815-837).

### Outgoing messages
- `SendMessageAsync` command (line 881): sends to `SelectedPlatform`. Per platform (Twitch case at 911, Kick at 995, YouTube at 1081):
  - Variable replacement via `IVariableReplacementService.ReplaceVariablesAsync` when text contains `{` (context vars from `BuildContextVariables`, line 1332: `bot-username`, `channel`, `platform`).
  - Echo-suppression key recorded **before** send.
  - Sends via `ITwitchService/IKickService/IYouTubeService.SendMessageAsync`.
  - Adds own message to UI with broadcaster badge (Twitch `broadcaster=1`, Kick `Broadcaster=Broadcaster` PascalCase note at line 1064, YouTube `broadcaster=1`).
  - TikTok/Discord sending: `TODO` at line 1144 — not implemented.
- `SendToAllPlatformsAsync` (line 1161): loops all connected platforms in parallel `Task.Run` per platform, collects per-platform errors in a list, shows a single "Partial Send Failure" MessageBox if any fail; adds one synthetic message to UI with `Platform="Vyral"`, username "Vyral", teal `#26A69A`, custom "Vyral" badge (lines 1296-1310).
- `BuildContextVariables` (line 1332) only differentiates Twitch/Kick; other platforms get `bot-username=Bot`, `channel=""`.

### Chat history
- Not loaded on init (comment: caused a 46MB memory spike; lines 288-292).
- `LoadHistoryAsync` command (line 296): one-shot load (guarded by `_historyLoadedCount > 0`), reads up to `MAX_HISTORY_MESSAGES = 200` per platform from `IChatMessageStore.GetRecentAsync("Kick"/"Twitch", channel, 200)` (only Kick + Twitch implemented), dedups via `_loadedMessageIds` (`Platform:User:Ticks:Message` key), inserts chronological at index 0, trims to 500 total. Tooltip says "50 messages" but constant is 200 (minor doc drift, ChatView.xaml:289).
- `ClearChat` (line 1452): clears UI + resets history-load tracking only.
- `ClearHistoryAsync` (line 1461): confirm CustomDialog → `_chatStore.DeleteAsync("Kick"/"Twitch", channel)` for connected channels → clears UI → success dialog.

### Platform selector / quick-settings toggles
- `TogglePlatformSelector`, `SelectPlatform(PlatformOption)` (lines 1362-1374).
- `ToggleTwitchSettings` / `ToggleKickSettings` / `ToggleYoutubeSettings` / `ToggleTiktokSettings` / `ToggleDiscordSettings` (lines 1386-1449): mutually exclusive popup open flags.

### Chat event notifications (follows, subs, raids, etc.)
For each Kick/Twitch event type, handler pattern (e.g. `OnKickFollowAsync` line 1700, `OnTwitchRaidAsync` line 2354):
1. Check platform filter flag.
2. `IChatEventSettingsService.GetSettingAsync(platform, eventType)` → per-event settings (ShowInChat, AutoReplyEnabled, AutoReplyMessage).
3. If `ShowInChat` → `AddSystemMessageAsync(platform, text)` (line 2481): gold `#FFD700` "System" message, `IsSystemMessage=true`.
4. Play sound via `ISoundService.PlayEventSoundAsync(eventType, platform)`.
5. Visual alert via `ShowEventAlertAsync` (line 2628) → `IAlertService.ShowAlertAsync(...)` with `AlertAnimation.SlideInFromTop`, `AlertPosition.TopCenter`, 5s.
6. Currency bonus via `ICurrencyService.AwardEventBonusAsync` (skipped for Twitch gift recipients — gifter gets it; multiplied for gift-sub counts and raid viewer counts).
7. Auto-reply: if enabled and `_autoReplyService.ShouldSendReply(user, platform, eventType)` (rate limiter) → `_autoReplyService.ProcessTemplate(template, variables)` → send via the platform service. Template variables per event: `username`, `displayname`, `tier`, `months`, `streak`, `bits`, `viewers`, `gifter`, `count`, `amount`, `reward`, `cost`, `input`, `hoster`, `sender`, `message`, `platform`.

### ChatMessage model (bottom of ChatViewModel.cs, line 2657)
- Plain INotifyPropertyChanged model: Platform, Username, Message, Timestamp, Color, IsModerator/IsSubscriber/IsVip/IsBroadcaster, IsSystemMessage, `Badges` dictionary, computed `BadgeImages`.
- `ComputeBadgeImages()` (line 2785): maps badge keys case-insensitively (broadcaster/moderator/vip/subscriber/verified) to **Twitch CDN badge URLs** (`static-cdn.jtvnw.net`) for both Twitch and Kick messages. Unknown badge keys are skipped (so Kick's PascalCase "Broadcaster" works via lowercase matching, but e.g. Kick-specific badges with no mapping render nothing).
- `LoadBadgeImage` (line 2737): static `ConcurrentDictionary` cache; supports `data:image/...;base64` URIs and HTTP download with a browser User-Agent and **hardcoded Kick referer/origin headers** (lines 2733-2734). Sync-over-async `GetByteArrayAsync(...).GetAwaiter().GetResult()`.
- Badge display priority: broadcaster(0) < moderator(1) < vip(2) < subscriber(3) < verified(4) (line 2848).

### Code-behind behaviors (ChatView.xaml.cs)
- Auto-scroll engine: finds inner `ScrollViewer` via visual tree; on `ScrollChanged`, if user is within 20px of bottom keeps `AutoScroll=true`; scrolling up disables it; returning to bottom re-enables and clears the unread banner (lines 109-151).
- "New messages" banner: when AutoScroll off and items added, counts unread and shows "N new messages" button; clicking scrolls to end (lines 153-185).
- Attaches/detaches `Messages.CollectionChanged` on Loaded/Unloaded/DataContextChanged to avoid double-subscribe (lines 42-80).

### Cleanup / lifecycle
- `Cleanup()` (line 2510): unsubscribes all ~20 EventBus subscriptions, resets IDs and `_isInitialized` (needed so singleton re-subscribes after profile switch), clears loaded-message tracking and Messages. Comment: without the reset the singleton permanently loses subscriptions after profile switch.

---

## 2. Chat Hub (platform settings hub page)

- **Where**: `src/VyralBot.UI/Views/ChatHubView.xaml` (733 lines) + `src/VyralBot.UI/ViewModels/ChatHubViewModel.cs` (156 lines)
- **What**: NOT a chat feed — it is a settings hub with a platform switcher. It loads connection status from the DB (`IDataService.GetPlatformByNameAsync("Twitch"|"Kick"|"YouTube"|"TikTok"|"Discord")`, Status == "Connected") and hosts the five per-platform chat-settings views as `CurrentPlatformSettings` (ChatHubViewModel.cs:94-149). `SelectedPlatform` defaults to "Twitch"; `SelectPlatformCommand` switches.
- Each child VM (`TwitchChatSettingsViewModel`, `KickChatSettingsViewModel`, `YoutubeChatSettingsViewModel`, `TiktokChatSettingsViewModel`, `DiscordChatSettingsViewModel`) is resolved from DI and `InitializeAsync()`d on hub init (lines 72-82).

---

## 3. Chat Hub UI details (ChatHubView.xaml)

- Title "Chat Management" — "Configure chat behavior, event notifications, and auto-replies for each platform".
- **Platform tabs**: 5 styled RadioButtons (Twitch/Kick/YouTube/TikTok/Discord) each invoking `SelectPlatformCommand` with parameter (ChatHubView.xaml:67-111).
- **Connection status bar**: "{Platform} Chat Settings" + green/red status dot + "Connected"/"Not Connected" (from `IsCurrentPlatformConnected`, DB-driven), plus a red "Connect this platform in Connections to manage chat settings" warning and 40%-opacity disabling of the whole settings area when not connected (ChatHubView.xaml:164-189).
- **General card** (shared, bound to `CurrentPlatformSettings.*`): "Chat Enabled" (Show messages from this platform), "Auto-Start Chat" (Connect on app launch), "Auto-Replies" (Send automatic responses to events) toggles (ChatHubView.xaml:211-255).
- **Twitch tab**: "Event Notifications & Auto-Replies" card with Expanders per event: Follow ({username}), Subscribe ({username},{tier}), Resub ({username},{months}), Gift Sub ({gifter},{count}), Raid ({raider},{viewers}), Cheer ({username},{bits}) — each with "Show in chat" toggle + "Auto-reply" toggle + template TextBox (ChatHubView.xaml:280-417).
- **Kick tab**: same pattern for Follow, Subscribe, Gift Sub, Host ({hoster}), Kicks Gifted ({sender},{amount}), Reward Redeemed ({username},{reward}), Stream Status (show-in-chat only) (ChatHubView.xaml:443-594).
- **YouTube/TikTok/Discord tabs**: display options only — a single "Show messages ... in the unified chat" toggle; text notes "Event-based auto-replies will be added in a future update" (ChatHubView.xaml:601-704).
- Bottom: "Save Settings" button bound to `CurrentPlatformSettings.SaveSettingsCommand` + status TextBlock (ChatHubView.xaml:709-728).

## 4. Per-platform chat settings ViewModels

### TwitchChatSettingsViewModel (src/VyralBot.UI/ViewModels/TwitchChatSettingsViewModel.cs, 223 lines)
- Backed by `IChatEventSettingsService` only. Loads/saves 6 `ChatEventSetting` rows (Platform="Twitch", EventType=Follow/Subscribe/Resub/GiftSub/Raid/Cheer) in `InitializeAsync()` / `SaveSettingsAsync()`. Default templates e.g. "Thanks for the follow @{username}!".
- Note: templates default to `@{username}` but `ProcessTemplate` only replaces `{username}` — the stray `@` stays literal. (AutoReplyService.cs:47 replaces `{key}`; no `@{key}` handling.)
- `IsChatEnabled` / `AutoStartChat` / `AutoRepliesEnabled` observable but NOT persisted by this VM (no persistence code for them).

### KickChatSettingsViewModel (src/VyralBot.UI/ViewModels/KickChatSettingsViewModel.cs, 241 lines)
- Same pattern, 7 event types: Follow, Subscribe, GiftSub, Host, KicksGifted, RewardRedeemed, StreamStatus (StreamStatus is show-in-chat only; AutoReply forced false/null on save, lines 224-228).
- Also does NOT persist `IsChatEnabled`.

### YoutubeChatSettingsViewModel / TiktokChatSettingsViewModel / DiscordChatSettingsViewModel (src/VyralBot.UI/ViewModels/, 92 lines each — near-identical)
- Persisted differently: `OnIsChatEnabledChanged` → `PersistPlatformFlagAsync` writes a per-platform column on the Platform entity via `IDataService.SavePlatformAsync` (`YoutubeChatEnabled` / `TiktokChatEnabled` / `DiscordChatEnabled`) and publishes `PlatformChatRoutingChangedEvent { Source="UI", Platform, Enabled }` on the EventBus (YoutubeChatSettingsViewModel.cs:67-91).
- That event is what `ChatViewModel.OnPlatformRoutingChangedAsync` listens to, toggling the `*FilterEnabled` flags live (ChatViewModel.cs:840-864).
- `InitializeAsync` force-enables the flag if platform is Connected but flag false (lines 45-51).
- `SaveSettingsCommand` is a no-op that just sets Status="Saved".

### Quick-settings popups in Chat page header reuse these views
- `TwitchChatSettingsView.xaml` (222 lines): "Twitch — Event Settings" popup with the same 6 event expanders + Save button.
- `KickChatSettingsView.xaml` (239 lines): same for Kick events.
- `YoutubeChatSettingsView.xaml` / `TiktokChatSettingsView.xaml` / `DiscordChatSettingsView.xaml` (35 lines each): "Chat Quick Settings" with one "Show messages" toggle plus **decorative placeholder checkboxes** ("Show Badges" checked, "Show Timestamps", "Compact Mode") that have NO bindings (YoutubeChatSettingsView.xaml:28-32).
- Code-behind note: `TwitchChatSettingsView.xaml.cs` and `KickChatSettingsView.xaml.cs` construct a **new** ViewModel instance each time the popup content is created (resolved services from `App.ServiceProvider`), so the popup is fresh-from-DB state, not the shared ChatHub VM instance. `YoutubeChatSettingsView.xaml.cs` resolves the DI-registered `YoutubeChatSettingsViewModel` instead (inconsistent lifetimes).

## 5. Core chat services (src/VyralBot.Core/Services/Chat/)

### AutoReplyService (AutoReplyService.cs) — IAutoReplyService
- `ProcessTemplate(template, variables)` (line 37): simple case-insensitive `string.Replace` of `{key}` placeholders.
- `ShouldSendReply(username, platform, eventType)` (line 136): delegates to `AutoReplyRateLimiter.CanReply`.
- `GetAutoReplyAsync(...)` (line 61): richer conditional-rules path — loads `AutoReplyRule`s via `IDataService.GetAutoReplyRulesByPlatformAndEventAsync`, evaluates `RuleCondition` JSON with `IRuleEvaluator.EvaluateCondition`, increments trigger counts, returns processed template. (This method is NOT called by ChatViewModel — the VM uses ShouldSendReply + ProcessTemplate with the simple ChatEventSetting templates. `GetAutoReplyAsync` returns null when DataService/RuleEvaluator missing.)
- Constructor news up an `AutoReplyRateLimiter` with a fixed 30s window (line 26).

### AutoReplyRateLimiter (AutoReplyRateLimiter.cs)
- `ConcurrentDictionary<string,DateTime>` keyed `"{platform}:{eventType}:{username-lowercase}"`; default cooldown 30s (line 16-17). `CleanupExpired()` removes entries older than 10 minutes (line 54).

### ChatEventSettingsService (ChatEventSettingsService.cs) — IChatEventSettingsService
- Cached DB access to `ChatEventSetting` rows: lazy-loaded dictionary keyed `"{Platform}:{EventType}"` guarded by `SemaphoreSlim` (lines 32-64). `GetSettingAsync`, `GetAllSettingsAsync(platform?)`, `SaveSettingAsync` (write-through), `DeleteSettingAsync`, helpers `IsEventEnabledAsync` / `IsAutoReplyEnabledAsync` / `GetAutoReplyMessageAsync` / `GetSoundSettingsAsync` (SoundEnabled/SoundFile/SoundVolume columns exist on the entity), and `InvalidateCache()`.

### IChatMessageStore + InMemoryChatMessageStore (IChatMessageStore.cs, InMemoryChatMessageStore.cs)
- Contract: `AddAsync`, `GetRecentAsync(platform, channel, limit=200)`, `DeleteAsync` — "persist history that arrives before UI subscribes to the EventBus" (IChatMessageStore.cs:7).
- In-memory impl: per `platform:channel` bounded queue (default 500), reflection-based `MessageId` dedupe, oldest-first ordering (InMemoryChatMessageStore.cs:15-93).

### DataChatMessageStore (src/VyralBot.UI/Services/Chat/DataChatMessageStore.cs) — DB-backed IChatMessageStore
- `AddAsync` maps `KickChatMessageEvent` / `TwitchChatMessageEvent` to the `ChatMessage` DB entity (badges serialized to JSON) and saves via `IDataService.SaveChatMessageAsync`; other event types rejected (lines 22-72). Twitch has no MessageId (IRC).
- `GetRecentAsync` reconstitutes platform-specific events from rows (Twitch vs Kick/others) (lines 74-131).
- `DeleteAsync` → `IDataService.DeleteChatMessagesAsync(platform, channel)`.
- This is what `ChatViewModel` gets injected (`IChatMessageStore`), so chat history persists in the app database, not just memory (subject to DI registration — see wiring section).

## 6. Incoming message flow (platform → EventBus → UI)

### Twitch
- `src/VyralBot.Integrations/Twitch/LightweightTwitchIrc.cs` (custom lightweight IRC client) raises `MessageReceived` → `TwitchService.OnMessageReceived` (TwitchService.cs:330-389):
  - Builds `TwitchChatMessageEvent` (username, display name, color, sub/mod/broadcaster/VIP flags, badges, `Emotes` currently always empty list).
  - Publishes to `IEventBus` fire-and-forget via `Task.Run` (comment: sync publish previously blocked the IRC read loop; TwitchService.cs:355-368).
  - Persists via `_chatMessageStore.AddAsync("Twitch", channel, evt)` — note both branches of the `TwitchChatEnabled` if/else store the message anyway (TwitchService.cs:370-381), so the flag only affects display, not storage.
- `SendMessageAsync` (TwitchService.cs:163) delegates to `_client.SendMessageAsync` (IRC PRIVMSG).

### Kick
- `src/VyralBot.Integrations/Kick/KickLibChatService.cs` (implements `IKickService`):
  - `ConnectAsync` loads the Kick `Platform` row from DB (needs `ChannelId` + `ChatroomId`), calls `ListenToChatRoomAsync(chatroomId)` and `ConnectAsync()` on KickLib's Pusher WebSocket client; publishes `PlatformConnectionChangedEvent` (KickLibChatService.cs:100-151). Channel events (follows/subs/status) deliberately NOT taken from Pusher — handled by official Kick webhooks to avoid duplicates (comment at lines 133-135).
  - `OnChatMessage` (KickLibChatService.cs:287-332): maps KickLib `ChatMessageEventArgs` to `KickChatMessageEvent` (badges dictionary from identity badges; subscriber/moderator/owner inferred from badge types; color from identity), publishes via `Task.Run`, stores via `_chatMessageStore.AddAsync("Kick", ...)` always.
  - Other Pusher handlers (OnSubscription etc., lines 334-489) also publish synthetic `KickChatMessageEvent`s from user "Vyral.Bot" as fallback display messages (e.g. "🎉 X just subscribed!") — in addition to dedicated typed events.
  - `SendMessageAsync` (KickLibChatService.cs:193-238): auto-refreshes OAuth token if expiring <5 min, then sends **as the broadcaster user** (`SendMessageAsUserAsync(broadcasterUserId, ...)`) — messages go out under the streamer's account, not a separate bot account.
- **Webhook path**: `src/VyralBot.Api/Controllers/WebhooksController.cs` receives Kick webhooks. `chat.message.sent` is IGNORED (Pusher owns chat; line 59-61), but unfollow (line 210), livestream status (line 361), moderation ban (line 418 — "X was banned by Y" as red system-ish message), and gift (line 472) webhooks are published as synthetic `KickChatMessageEvent`s from "Vyral.Bot" with a custom `vyral` badge. `ProcessChatMessage` (line 504) exists as a legacy/parallel chat path.

### YouTube
- `src/VyralBot.Integrations/YouTube/YouTubeChatService.cs`: polling loop using YouTube Live Chat Messages API (`pollingIntervalMillis` respected from API response, lines 344-352); only `textMessageEvent` items processed (line 382); builds `YouTubeChatMessageEvent` (moderator/owner/member flags, profile image URL, MessageId), publishes fire-and-forget (lines 420-432).
- Stores via `_chatMessageStore.AddAsync("YouTube", ...)` gated on `YoutubeChatEnabled` (lines 434-438) — but `DataChatMessageStore.AddAsync` only maps Kick/Twitch events, so **YouTube history is silently never persisted** (DataChatMessageStore.cs:67-71 returns false for unsupported types).

### TikTok / Discord
- **No integration code exists**. `src/VyralBot.Integrations/` contains only Kick/Twitch/YouTube. TikTok/Discord appear only as: `Platform` entity flags (`TiktokChatEnabled`, `DiscordChatEnabled`), UI converters, settings views/VMs, and hardcoded `IsTiktokConnected=false` / `IsDiscordConnected=false` in ChatViewModel.cs:485-486. Chat page can't display or send them.

### Consumer side
- `ChatViewModel` is the only EventBus subscriber for `TwitchChatMessageEvent` / `KickChatMessageEvent` / `YouTubeChatMessageEvent` (verified via grep). `TriggerEngine` subscribes to base `IEvent` but its matching logic is all TODO (TriggerEngine.cs:118-130), so it just logs.

### ⚠ Notable: the generic `ChatMessageReceivedEvent` command engine is dead code
- `ChatViewModel.OnChatMessageReceivedAsync` (ChatViewModel.cs:592) contains the ENTIRE chat-command pipeline: unified `ICommandService.ExecuteAsync`, currency commands, giveaway commands, poll commands, watchtime commands, watchtime tracking, and chat-activity currency awards (ChatViewModel.cs:602-813).
- `ChatMessageReceivedEvent` (declared at ChatViewModel.cs:2871) is **never published anywhere in the solution** (verified by grep across src). Platform services publish only their typed events. Therefore none of this processing currently fires, and `CommandService`/`GiveawayCommandService`/etc. have no other chat-triggered callers — chat commands do not run from incoming chat as wired today.

## 7. Navigation & DI wiring

- **Sidebar "Chat" button** → `NavigateToChatCommand` → `MainViewModel.NavigateToChatAsync` (MainViewModel.cs:750-797) → shows `ChatView` via DataTemplate (MainWindow.xaml:797-799). The ChatViewModel singleton is created during profile load, before platform auto-connect, specifically so EventBus subscriptions exist before messages arrive (MainViewModel.cs:250-277: "CRITICAL: ChatViewModel must subscribe to EventBus BEFORE platforms connect").
- **Home page "Chat" card** ("Complete chat management for all platforms") → `NavigateToChatHubCommand` → `NavigateToChatHubAsync` (MainViewModel.cs:1389-1414) → `ChatHubView` (MainWindow.xaml:887-888). ChatHub VM is created fresh in a new scope each navigation and disposed when navigating away (MainViewModel.cs:1812).
- Navigation uses `CurrentViewModel = null` + `DisposeNonEssentialViewModelsAsync(excludeViewModel:)` to release WPF visual trees (memory optimization; comments mention avoiding a 100MB spike).
- DI (src/VyralBot.UI/App.xaml.cs): `IChatMessageStore → DataChatMessageStore` (singleton, line 107), `IChatEventSettingsService → ChatEventSettingsService` (line 108), `IAutoReplyService → AutoReplyService` (line 110), `ChatViewModel` singleton (line 202), `ChatHubViewModel` singleton (line 218), `ChatView` transient (line 232). App shutdown calls `chatViewModel.Cleanup()` (App.xaml.cs:476-486). Comment at App.xaml.cs:314 confirms webhook events published in WebhooksController reach ChatViewModel through the shared EventBus.
- Kick display-filter bootstrap: `ChatViewModel` initializes `KickFilterEnabled` from `KickSettingsViewModel.IsChatEnabled` (ChatViewModel.cs:275-283). `KickSettingsViewModel` (Platforms section, src/VyralBot.UI/ViewModels/KickSettingsViewModel.cs:287-306) persists `platform.KickChatEnabled` on toggle and publishes `PlatformChatRoutingChangedEvent`. YouTube equivalent persists `YoutubeChatEnabled` (MainViewModel.cs:542-544).
- ⚠ Twitch has NO publisher of `PlatformChatRoutingChangedEvent` and no equivalent bootstrap (grep-verified: publishers exist only for Kick, YouTube, TikTok, Discord). `TwitchFilterEnabled` in ChatViewModel defaults to `true` and can never be turned off at runtime; the ChatHub "Chat Enabled" toggle for Twitch binds to `TwitchChatSettingsViewModel.IsChatEnabled`, which is an unpersisted observable with no effect.

## 8. Moderation in chat — NOT PRESENT

- Grep across src for `BannedWord`, `ProfanityFilter`, `ModerationService`, `ChatFilter`, `TimeoutUser`, `BanUser`, `DeleteMessage`, `PurgeUser` returns nothing. There are no banned-word filters, no timeout/ban/purge/delete actions, and no per-message moderation context menu in the chat UI.
- The only moderation-adjacent touchpoints:
  - Kick `moderation.ban` webhooks are surfaced as a red system-style message "X was banned by Y" in chat (WebhooksController.cs:418-446) — display only, no action taken by the app.
  - `RuleCondition`/`IRuleEvaluator` (src/VyralBot.Core/Models/Rules/RuleCondition.cs, src/VyralBot.Core/Services/Rules/RuleEvaluator.cs) can gate auto-replies by conditions, but nothing moderates/deletes messages.

## 9. Chat UI extras (emotes / mentions / search / popout)

- **Emotes**: No emote rendering anywhere in the chat UI (no "emote" references in VyralBot.UI). `TwitchChatMessageEvent.Emotes` is populated with an empty list (TwitchService.cs:352). Messages render as plain text only.
- **Mentions**: No mention detection/highlight; no clicking on usernames.
- **Search**: No message search.
- **Popout chat**: None (no separate chat window).
- **What does exist**:
  - Platform logo per message (DataTriggers in ChatView.xaml:19-43; converters `PlatformToImageConverter` / `PlatformToBrushConverter`).
  - Badge images: `ChatMessage.ComputeBadgeImages` (ChatViewModel.cs:2785) is the active path used by the message template; a second richer implementation `BadgesToImagesConverter` (src/VyralBot.UI/Converters/BadgesToImagesConverter.cs) exists with a large Twitch badge UUID table plus Kick CDN (`files.kick.com/images/badges/*.png`) and a local `vyral` badge — but the converter is referenced in ChatView.xaml resources (line 15) while the ItemTemplate binds `BadgeImages` directly, so the converter is effectively unused for rendering.
  - Username colors from platform event (`Color`), system messages in gold `#FFD700`.
  - Timestamps `h:mm tt`, virtualized list, auto-scroll toggle, "N new messages" jump-to-latest banner (ChatView.xaml.cs).
  - `LazyPopupContent` control (src/VyralBot.UI/Controls/LazyPopupContent.cs) defers constructing quick-settings popup views until opened (memory optimization; resolves from DI or Activator).
  - History load / clear-UI / delete-history buttons (see section 1).

## 10. Database entities behind chat

- `src/VyralBot.Data/Entities/ChatEventSetting.cs`: `Platform` + `EventType` keyed rows with `ShowInChat`, `AutoReplyEnabled`, `AutoReplyMessage`, plus `SoundEnabled`/`SoundFile`/`SoundVolume` (defaults true/null/100).
  - ⚠ The sound columns are never surfaced in the chat settings UI (views only offer Show-in-chat + Auto-reply + template), and `IChatEventSettingsService.GetSoundSettingsAsync` / `IsEventEnabledAsync` have no callers outside the service interface itself. Actual event sounds are resolved by `SoundService.PlayEventSoundAsync` (src/VyralBot.Core/Services/Audio/SoundService.cs:108-138) from its own audio settings/event-sound mapping and master volume — the ChatEventSetting sound fields are orphaned.
- `src/VyralBot.Data/Entities/ChatMessage.cs`: persisted chat history row — Platform, Channel, MessageId, UserId, Username, DisplayName, Content, Timestamp, Color, IsModerator/IsSubscriber/IsOwner (ints), BadgesJson, IsSystemMessage, CreatedAt. Written/read via `DataChatMessageStore` (Kick/Twitch only, see section 6).
- `Platform` entity (`src/VyralBot.Data/Entities/Platform.cs`): per-platform display flags consumed by chat: `TwitchChatEnabled`, `KickChatEnabled`, `YoutubeChatEnabled`, `TiktokChatEnabled`, `DiscordChatEnabled`, plus Kick `ChannelId`/`ChatroomId`/`UserId`/tokens used for chat transport.
- `AutoReplyRule` entity + `RuleCondition`/`RuleEvaluator` (src/VyralBot.Core/Models/Rules/RuleCondition.cs, src/VyralBot.Core/Services/Rules/RuleEvaluator.cs): conditional auto-reply rules consumed only by `AutoReplyService.GetAutoReplyAsync`, which ChatViewModel does not call (it uses the simpler ChatEventSetting path) — i.e., the advanced rule engine is wired in Core but not driven from the chat page.

## 11. Root docs cross-reference

- `USER_GUIDE_CHAT_EVENTS.md` — user-facing doc matching implementation: Kick events (Follow, Subscribe, Gift Sub, Host, Kicks Gifted, Reward Redeemed), Twitch events (Follow, Subscribe, Resub, Gift Sub, Raid, Cheer, Channel Points). Note: doc lists a Twitch "Channel Points" event; no handler for it exists in ChatViewModel (only 6 Twitch event handlers).
- `CHAT_EVENTS_IMPLEMENTATION_PLAN.md` — design doc for the ChatEventSettings table, visibility toggles, auto-reply; implemented as described.
- `docs/features/chat-system.md`, `docs/services/*.md`, `docs/ui/chat-view.md`, `docs/ui/chat-viewmodel.md` — mostly accurate mirror of the code; claims badge display uses `BadgesToImagesConverter` while the actual message template binds `ChatMessage.BadgesImages` computed in the VM.
- `kick chat system.md`, `kick-chat-system-formatted.md`, `KICK_CHAT_SYSTEM_COMPREHENSIVE_AUDIT.md` — background on the Kick chat migration to KickLib/Pusher + webhooks (matches KickLibChatService/WebhooksController split: Pusher for chat messages, webhooks for channel events).

## 12. Summary of notable findings / gaps

1. **Dead command engine**: `ChatMessageReceivedEvent` is never published, so the entire incoming-chat command pipeline in ChatViewModel (unified commands, currency, giveaway, poll, watchtime, watchtime tracking, chat-activity currency) never executes. Verified by grep across all src projects.
2. **Moderation is absent**: no banned words, filters, timeouts, bans, message deletion, or purge anywhere; only passive display of Kick moderation-ban webhook notices.
3. **TikTok/Discord are UI-only stubs**: settings toggles and routing events exist, but there is no integration code, no message receive/send, and ChatViewModel hardcodes them disconnected.
4. **YouTube chat history not persisted**: `DataChatMessageStore.AddAsync` only maps Kick/Twitch events, so YouTube messages are displayed live but never stored/loaded from history.
5. **Echo suppression is content-based**: keys are message text only (lowercased), with 10s/15s windows — a viewer typing the exact same text the bot just sent within the window is also filtered out; conversely after SendToAll the same message can't be echoed by anyone for 10s.
6. **Badge rendering quirk**: live path (`ChatMessage.ComputeBadgeImages`) only maps 5 badge types to Twitch CDN URLs (works for Kick's core badges via case-insensitive match); the much richer `BadgesToImagesConverter` (Kick CDN, 50+ Twitch badges, local vyral badge) is instantiated in resources but unused by the message template.
7. **Auto-reply template drift**: default templates use `@{username}` style, but `ProcessTemplate` only replaces `{username}` — the `@` remains in output.
8. **Per-event sound settings** exist in DB schema and service API but no UI and no callers.
9. **Minor doc/UI drift**: Load History tooltip says "50 messages" but loads up to 200/platform; All-Platforms quick-actions popup buttons (Start Giveaway / Start Poll / Send Announcement / Trigger Event) have no command bindings (placeholders); YouTube/TikTok/Discord quick-settings popups contain non-functional decorative checkboxes.
10. **Kick sends as the broadcaster** (`SendMessageAsUserAsync` with the streamer's UserId), not as a bot account; token auto-refresh happens on send.
11. **ChatViewModel is a singleton with careful lifecycle management**: `_isInitialized` guard, `Cleanup()` on shutdown/profile switch (resets all subscription IDs so re-init works), invoked from App.xaml.cs and MainViewModel.
12. **Memory-conscious design throughout**: on-demand history load (46MB spike comment), 500-message cap, virtualizing ListBox with recycling, LazyPopupContent for settings popups, view-model disposal on navigation.
13. **Twitch display filter can't be disabled**: no one publishes `PlatformChatRoutingChangedEvent` for Twitch and `TwitchFilterEnabled` defaults true, so the Twitch "Chat Enabled" toggles (hub + quick settings) are cosmetic/unpersisted, unlike Kick/YouTube/TikTok/Discord which persist a Platform-entity flag and publish the routing event.
